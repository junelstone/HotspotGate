using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StonesHotspot.Licensing.Codes;
using StonesHotspot.Licensing.Machine;
using StonesHotspot.Licensing.Online;

namespace StonesHotspot.Licensing;

/// <summary>
/// The real licence check: 30-day trial from the first run on this PC, then a signed licence code for this Machine
/// ID; warnings, 7 days of grace after the end (or after a revocation), then stop. Durations never go backwards: the
/// latest time seen (and the licence server's time) count, not only the PC clock.
/// </summary>
public sealed class LicenseService : ILicenseService
{
    public const string LicenceFileName = "licence.lic";
    public const string AnswerFileName = "licence-server.json";

    private static readonly TimeSpan PersistEvery = TimeSpan.FromHours(1);
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromHours(24);

    private readonly LicensingOptions _options;
    private readonly ITrialStore _trials;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly MachineId _machine;
    private readonly object _lock = new();

    private TrialRecord _trial;
    private DateTimeOffset _persistedLatest;
    private LicenceData? _licence;
    private ServerAnswer? _answer;
    private DateTimeOffset? _lastCheckIn;

    /// <summary>"Now" for licence purposes: moves forward with real elapsed time, never back (see <see cref="Now"/>).</summary>
    private DateTimeOffset _effective;
    private long _lastTimestamp;

    public LicenseService(LicensingOptions options, IMachineIdentitySource machine, ITrialStore trials, TimeProvider clock, ILogger<LicenseService>? logger = null)
    {
        _options = options;
        _trials = trials;
        _clock = clock;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _machine = machine.Read();
        _trial = LoadTrial();
        _persistedLatest = _trial.LatestSeen;
        var local = clock.GetUtcNow();
        _effective = local > _trial.LatestSeen ? local : _trial.LatestSeen;
        _lastTimestamp = clock.GetTimestamp();
        LoadLicence();
    }

    public string MachineId => _machine.ToString();

    public MachineId Machine => _machine;

    /// <summary>The active licence, if any (valid signature, this PC).</summary>
    public LicenceData? Licence
    {
        get { lock (_lock) return _licence; }
    }

    public bool TrialRegistered
    {
        get { lock (_lock) return _trial.Registered; }
    }

    public DateTimeOffset TrialStartedAt
    {
        get { lock (_lock) return _trial.FirstRun; }
    }

    public bool IsAllowed(Feature feature)
    {
        var status = GetStatus();
        return status.HotspotAllowed && EditionFeatures.Includes(status.Edition, feature);
    }

    public LicenseStatus GetStatus()
    {
        lock (_lock)
        {
            var local = _clock.GetUtcNow();
            var now = Now(local);
            Touch(now);

            DateTimeOffset end;
            DateTimeOffset? revokedAt = null;
            Edition licensed;
            if (_licence is { } licence)
            {
                end = licence.EndsAt;
                licensed = licence.Edition;
                if (_answer is { Status: ServerStatus.Revoked, LicenceId: var id } answer && id == licence.Id)
                    revokedAt = answer.RevokedAt ?? answer.ServerTime;
            }
            else
            {
                end = _trial.FirstRun.AddDays(LicensingOptions.TrialDays);
                licensed = Edition.Trial;
            }

            var graceFrom = revokedAt is { } r && r < end ? r : end;
            var stopsAt = graceFrom.AddDays(LicensingOptions.GraceDays);
            var allowed = now < stopsAt;
            var inGrace = allowed && now >= graceFrom;
            var daysLeft = (int)Math.Ceiling((end - now).TotalDays);

            return new LicenseStatus(allowed ? licensed : Edition.None, end, inGrace, allowed)
            {
                Kind = _licence is null ? LicenceKind.Trial : LicenceKind.Licence,
                LicenceId = _licence?.Id,
                Customer = _licence?.Customer,
                LicensedEdition = licensed,
                MachineId = MachineId,
                RevokedAt = revokedAt,
                StopsAt = stopsAt,
                ClockProblem = local < now - ClockTolerance,
                LastCheckIn = _lastCheckIn,
                WarningDaysLeft = allowed && !inGrace && daysLeft <= LicensingOptions.WarningDays[0] ? Math.Max(daysLeft, 0) : null,
            };
        }
    }

    public ActivationResult Activate(string code)
    {
        lock (_lock)
        {
            var result = Check(code, out var licence);
            if (result != ActivationResult.Activated)
            {
                _logger.LogWarning("Licence code refused: {Result}", result);
                return result;
            }

            if (_licence?.Id != licence!.Id)
                ForgetAnswer();   // a revocation of another licence does not apply to this one
            _licence = licence;
            Write(LicenceFileName, $"# Stone's Hotspot — licence {licence.Id} ({licence.Edition}) for {licence.Customer}, {MachineId}\n{code.Trim()}\n");
            _logger.LogInformation("Licence {Id} activated: {Edition} until {Expiry}", licence.Id, licence.Edition, licence.ExpiresOn);
            return ActivationResult.Activated;
        }
    }

    /// <summary>
    /// Applies a licence server answer: renewal (new code), revocation, "still valid", trial registration. Ignored when
    /// the signature, the nonce or the Machine ID is wrong.
    /// </summary>
    public bool ApplyServerAnswer(SignedAnswer signed, string nonce)
    {
        var answer = signed.Verify(_options.ServerPublicKey);
        if (answer is null || answer.Nonce != nonce || answer.MachineId != MachineId)
        {
            _logger.LogWarning("Licence server answer refused (signature, nonce or Machine ID)");
            return false;
        }

        lock (_lock)
        {
            var local = _clock.GetUtcNow();
            if (answer.ServerTime > Now(local))
                _effective = answer.ServerTime;   // the PC clock is behind: trust the server's signed time
            _lastCheckIn = local;

            switch (answer.Status)
            {
                case ServerStatus.TrialRegistered:
                    var first = answer.TrialFirstRun is { } f && f < _trial.FirstRun ? f : _trial.FirstRun;
                    _trial = _trial with { FirstRun = first, Registered = true };
                    _trials.WriteAll(_trial.Seal());
                    break;
                case ServerStatus.Renewed when answer.RenewedCode is not null:
                    if (Activate(answer.RenewedCode) is ActivationResult.Activated or ActivationResult.Older)
                        SaveAnswer(signed, answer with { Status = ServerStatus.Valid });
                    break;
                case ServerStatus.Revoked or ServerStatus.Valid when _licence is not null && answer.LicenceId == _licence.Id:
                    SaveAnswer(signed, answer);
                    break;
            }
            return true;
        }
    }

    /// <summary>
    /// "Now" that never goes back and keeps counting when the clock is set back: the previous value plus the real time
    /// elapsed since (a monotonic timer, unaffected by clock changes), or the PC clock when it is ahead of that. At start
    /// it is the later of the PC clock and the latest time seen (stored with the trial record).
    /// </summary>
    private DateTimeOffset Now(DateTimeOffset local)
    {
        var timestamp = _clock.GetTimestamp();
        _effective += _clock.GetElapsedTime(_lastTimestamp, timestamp);
        _lastTimestamp = timestamp;
        if (local > _effective)
            _effective = local;
        return _effective;
    }

    private void Touch(DateTimeOffset now)
    {
        if (now <= _trial.LatestSeen)
            return;
        _trial = _trial with { LatestSeen = now };
        if (now - _persistedLatest >= PersistEvery)
        {
            _trials.WriteAll(_trial.Seal());
            _persistedLatest = now;
        }
    }

    private ActivationResult Check(string code, out LicenceData? licence)
    {
        licence = LicenceCode.Verify(code, _options.LicencePublicKey);
        if (licence is null)
            return ActivationResult.Invalid;
        if (!licence.MachineId.Matches(_machine))
            return ActivationResult.OtherMachine;
        if (licence.EndsAt <= Now(_clock.GetUtcNow()))
            return ActivationResult.Expired;
        if (_licence is { } current && current.Id == licence.Id && licence.IssuedOn < current.IssuedOn)
            return ActivationResult.Older;
        return ActivationResult.Activated;
    }

    /// <summary>The earliest valid trial record of this PC from every store; a new one at first run. Rewritten everywhere.</summary>
    private TrialRecord LoadTrial()
    {
        var now = _clock.GetUtcNow();
        var records = _trials.ReadAll().Select(TrialRecord.Unseal).OfType<TrialRecord>()
            .Where(r => Codes.MachineId.TryParse(r.MachineId, out var id) && id.Matches(_machine)).ToList();
        var trial = records.Count == 0
            ? new TrialRecord(MachineId, now, now)
            : new TrialRecord(MachineId, records.Min(r => r.FirstRun), records.Max(r => r.LatestSeen), records.Any(r => r.Registered));
        if (records.Count == 0)
            _logger.LogInformation("First run on this PC ({MachineId}): 30-day trial starts", MachineId);
        _trials.WriteAll(trial.Seal());
        return trial;
    }

    private void LoadLicence()
    {
        var text = Read(LicenceFileName);
        if (text is null)
            return;
        var licence = LicenceCode.Verify(text, _options.LicencePublicKey);
        if (licence is null || !licence.MachineId.Matches(_machine))
        {
            _logger.LogWarning("The saved licence is not valid on this PC: ignored");
            return;
        }
        _licence = licence;

        if (Read(AnswerFileName) is { } json && SignedAnswer.FromJson(json) is { } signed
            && signed.Verify(_options.ServerPublicKey) is { } answer && answer.MachineId == MachineId && answer.LicenceId == licence.Id)
        {
            _answer = answer;
            _lastCheckIn = answer.ServerTime;
        }
    }

    private void SaveAnswer(SignedAnswer signed, ServerAnswer answer)
    {
        _answer = answer;
        Write(AnswerFileName, signed.ToJson());
    }

    private void ForgetAnswer()
    {
        _answer = null;
        try
        {
            File.Delete(Path.Combine(_options.Directory, AnswerFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string? Read(string name)
    {
        var path = Path.Combine(_options.Directory, name);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Cannot read {File}: {Error}", path, ex.Message);
            return null;
        }
    }

    private void Write(string name, string content)
    {
        Directory.CreateDirectory(_options.Directory);
        var path = Path.Combine(_options.Directory, name);
        File.WriteAllText(path + ".tmp", content);
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
