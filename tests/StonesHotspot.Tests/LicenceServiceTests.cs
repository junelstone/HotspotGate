using StonesHotspot.Licensing;
using StonesHotspot.Licensing.Codes;
using StonesHotspot.Licensing.Machine;
using StonesHotspot.Licensing.Online;

namespace StonesHotspot.Tests;

/// <summary>The licence check on the hotspot: codes, Machine ID, trial, grace, clock, revocation (fake clock, test keys).</summary>
public sealed class LicenceServiceTests : IDisposable
{
    internal static readonly MachineId ThisPc = MachineId.FromRaw("guid-1", "board-1", "disk-1", "cpu-1");
    internal static readonly (byte[] Private, byte[] Public) LicenceKey = Ed25519.GenerateKeyPair();
    internal static readonly (byte[] Private, byte[] Public) ServerKey = Ed25519.GenerateKeyPair();

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shs-licence-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryTrialStore _store = new();
    private readonly AdjustableClock _clock = new(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero));

    /// <summary>A clock that can be set back (like a user changing the date), with a separate monotonic timer.</summary>
    internal sealed class AdjustableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        private long _ticks;

        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <summary>Real time passes: the wall clock and the monotonic timer move together.</summary>
        public void Advance(TimeSpan by)
        {
            _now += by;
            _ticks += by.Ticks;
        }

        /// <summary>Someone changes the PC date: only the wall clock moves.</summary>
        public void SetWallClock(DateTimeOffset value) => _now = value;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    internal sealed class MemoryTrialStore : ITrialStore
    {
        public List<string> Places { get; } = ["", ""];
        public IReadOnlyList<string> ReadAll() => Places.Where(p => p.Length > 0).ToList();
        public void WriteAll(string sealedRecord)
        {
            for (var i = 0; i < Places.Count; i++)
                Places[i] = sealedRecord;
        }
    }

    private LicenseService Service(MachineId? machine = null, string? dir = null) =>
        new(new LicensingOptions(dir ?? _dir, LicenceKey.Public, ServerKey.Public), new FixedMachineIdentity(machine ?? ThisPc), _store, _clock);

    private string Code(Edition edition = Edition.Pro, int days = 365, MachineId? machine = null, uint id = 1001, string customer = "Bar Chez Ngoma", DateOnly? issued = null)
    {
        var today = issued ?? DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        return LicenceCode.Create(new LicenceData(id, edition, today, today.AddDays(days - 1), machine ?? ThisPc, customer), LicenceKey.Private);
    }

    private void Days(double days) => _clock.Advance(TimeSpan.FromDays(days));

    [Fact]
    public void Codes_round_trip_in_copyable_groups()
    {
        var code = Code(customer: "Café « Le Paradis » de Brazzaville Centre-ville");
        Assert.StartsWith("SHS1-", code);
        Assert.Matches("^SHS1(-[A-HJ-NP-Z2-9]{1,5})+$", code);
        Assert.All(code.Split('-').Skip(1).SkipLast(1), g => Assert.Equal(5, g.Length));

        var data = LicenceCode.Verify(code.ToLowerInvariant().Replace("-", " - "), LicenceKey.Public)!;   // case and spacing do not matter
        Assert.Equal((1001u, Edition.Pro, ThisPc), (data.Id, data.Edition, data.MachineId));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(data.Customer) <= LicenceData.MaxCustomerBytes);
        Assert.StartsWith("Café « Le Paradis »", data.Customer);
        Assert.Equal(new DateOnly(2027, 3, 1), data.ExpiresOn);

        var lic = $"# Stone's Hotspot licence\n{code}\n";
        Assert.NotNull(LicenceCode.Verify(lic, LicenceKey.Public));   // .lic file content
    }

    [Fact]
    public void Edited_or_forged_codes_are_rejected()   // (2)
    {
        var code = Code();
        for (var i = 8; i < code.Length; i += 7)
        {
            if (code[i] == '-')
                continue;
            var changed = code[..i] + (code[i] == 'A' ? 'B' : 'A') + code[(i + 1)..];
            Assert.Null(LicenceCode.Verify(changed, LicenceKey.Public));
        }
        var forged = LicenceCode.Create(new LicenceData(1001, Edition.Pro, new DateOnly(2026, 3, 2), new DateOnly(2030, 1, 1), ThisPc, "x"), Ed25519.GenerateKeyPair().PrivateKey);
        Assert.Null(LicenceCode.Verify(forged, LicenceKey.Public));
        Assert.Null(LicenceCode.Verify("SHS1-ABCDE-FGHJK", LicenceKey.Public));
        Assert.Null(LicenceCode.Verify("bonjour", LicenceKey.Public));
        Assert.Equal(ActivationResult.Invalid, Service().Activate(forged));
    }

    [Fact]
    public void Code_for_another_pc_is_rejected_but_one_changed_part_is_tolerated()   // (1)
    {
        var other = MachineId.FromRaw("guid-2", "board-2", "disk-2", "cpu-2");
        Assert.Equal(ActivationResult.OtherMachine, Service().Activate(Code(machine: other)));

        var newDisk = MachineId.FromRaw("guid-1", "board-1", "disk-NEW", "cpu-1");
        Assert.Equal(ActivationResult.Activated, Service(newDisk).Activate(Code()));
        var twoChanged = MachineId.FromRaw("guid-1", "board-1", "disk-NEW", "cpu-NEW");
        Assert.Equal(ActivationResult.OtherMachine, Service(twoChanged, Path.Combine(_dir, "b")).Activate(Code()));
    }

    [Fact]
    public void Machine_id_format_and_matching()
    {
        var text = ThisPc.ToString();
        Assert.Matches("^MID-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", text);
        Assert.True(MachineId.TryParse(text.ToLowerInvariant(), out var parsed));
        Assert.Equal(ThisPc, parsed);
        Assert.False(MachineId.TryParse("MID-123", out _));

        var unreadable = MachineId.FromRaw("guid-1", "To be filled by O.E.M.", null, "cpu-1");
        Assert.Equal((0, 0), (unreadable.Board, unreadable.Disk));
        Assert.Equal(2, unreadable.MatchingParts(ThisPc));   // missing parts never count as matching
        Assert.False(ThisPc.Matches(unreadable));            // a licence made for 4 readable parts needs 3

        // A PC where only 2 parts can be read: both must match.
        Assert.True(unreadable.Matches(MachineId.FromRaw("guid-1", null, null, "cpu-1")));
        Assert.False(unreadable.Matches(MachineId.FromRaw("guid-1", null, null, "cpu-2")));
        Assert.False(MachineId.Empty.Matches(MachineId.Empty));
    }

    [Fact]
    public void Basic_licence_hides_pro_features()   // (3)
    {
        var service = Service();
        Assert.True(service.IsAllowed(Feature.MobileMoneyPayments));   // trial = Pro features
        Assert.Equal(ActivationResult.Activated, service.Activate(Code(Edition.Basic)));

        Assert.Equal(Edition.Basic, service.GetStatus().Edition);
        Assert.True(service.IsAllowed(Feature.Vouchers));
        Assert.True(service.IsAllowed(Feature.BasicReports));
        foreach (var pro in new[] { Feature.MobileMoneyPayments, Feature.SmsLogin, Feature.Shifts, Feature.PointOfSale, Feature.FullReports, Feature.MultipleStaffAccounts })
            Assert.False(service.IsAllowed(pro));
    }

    [Fact]
    public void Trial_warns_expires_has_seven_days_of_grace_then_stops()   // (4)
    {
        var service = Service();
        var start = service.GetStatus();
        Assert.Equal((Edition.Trial, LicenceKind.Trial, 30, true), (start.Edition, start.Kind, start.WarningDaysLeft ?? -1, start.HotspotAllowed));

        Days(16);
        Assert.Equal(14, service.GetStatus().WarningDaysLeft);
        Days(13.5);
        Assert.Equal(1, service.GetStatus().WarningDaysLeft);

        Days(0.5);   // day 30: trial over
        var grace = service.GetStatus();
        Assert.True(grace is { InGracePeriod: true, HotspotAllowed: true, Edition: Edition.Trial, WarningDaysLeft: null });
        Assert.Equal(start.ExpiresAt!.Value.AddDays(7), grace.StopsAt);

        Days(6.9);
        Assert.True(service.GetStatus().HotspotAllowed);
        Days(0.1);
        var stopped = service.GetStatus();
        Assert.True(stopped is { HotspotAllowed: false, Edition: Edition.None, LicensedEdition: Edition.Trial, Stopped: true });
        Assert.False(service.IsAllowed(Feature.CaptivePortal));

        // A valid code works at once, even when stopped.
        Assert.Equal(ActivationResult.Activated, service.Activate(Code()));
        Assert.True(service.GetStatus() is { HotspotAllowed: true, Edition: Edition.Pro, InGracePeriod: false });
    }

    [Fact]
    public void Reinstalling_does_not_reset_the_trial()   // (5)
    {
        var firstEnd = Service().GetStatus().ExpiresAt;
        Days(20);

        // Uninstall + reinstall: a new data folder, but the protected trial record survives.
        var reinstalled = Service(dir: Path.Combine(_dir, "new-install")).GetStatus();
        Assert.Equal(firstEnd, reinstalled.ExpiresAt);
        Assert.Equal(10, reinstalled.DaysLeft(_clock.GetUtcNow()));

        // One of the two places deleted: the other one is enough (and the deleted one is rewritten).
        _store.Places[0] = "";
        Assert.Equal(firstEnd, Service().GetStatus().ExpiresAt);
        Assert.NotEqual("", _store.Places[0]);

        // An edited record is ignored; the other copy still wins.
        _store.Places[1] = _store.Places[1].Replace("2026-03-02", "2026-03-20");
        Assert.Equal(firstEnd, Service().GetStatus().ExpiresAt);
    }

    [Fact]
    public void Trial_record_of_another_pc_does_not_count()
    {
        Service(MachineId.FromRaw("x", "y", "z", "w")).GetStatus();
        Days(25);
        var mine = Service().GetStatus();
        Assert.Equal(30, mine.DaysLeft(_clock.GetUtcNow()));   // a fresh trial for this PC
    }

    [Fact]
    public void Moving_the_clock_back_does_not_extend_the_trial()   // (6)
    {
        var service = Service();
        Days(25);
        Assert.Equal(5, service.GetStatus().DaysLeft(_clock.GetUtcNow()));

        _clock.SetWallClock(_clock.GetUtcNow().AddDays(-20));
        var status = service.GetStatus();
        Assert.True(status.ClockProblem);
        Assert.Equal(25, status.DaysLeft(_clock.GetUtcNow()));   // measured from the wrong clock…
        Assert.Equal(5, (int)Math.Ceiling((status.ExpiresAt!.Value - _clock.GetUtcNow().AddDays(20)).TotalDays));

        // …but the end is computed from the latest time seen plus real elapsed time: 5 more real days and it is over.
        Days(5);
        Assert.True(service.GetStatus().InGracePeriod);
        Assert.True(Service().GetStatus().InGracePeriod);   // a restart does not go back either
        Days(7);
        Assert.False(service.GetStatus().HotspotAllowed);
        Assert.False(Service().GetStatus().HotspotAllowed);
    }

    [Fact]
    public void Revoked_licence_stops_after_the_grace_period()   // (7)
    {
        var service = Service();
        service.Activate(Code());
        Days(10);
        var revokedAt = _clock.GetUtcNow();

        Assert.False(service.ApplyServerAnswer(Signed(ServerStatus.Revoked, revokedAt, nonce: "N1", key: Ed25519.GenerateKeyPair().PrivateKey), "N1"));   // forged
        Assert.False(service.ApplyServerAnswer(Signed(ServerStatus.Revoked, revokedAt, nonce: "OLD"), "N1"));                                              // replayed
        Assert.False(service.GetStatus().InGracePeriod);

        Assert.True(service.ApplyServerAnswer(Signed(ServerStatus.Revoked, revokedAt, nonce: "N2"), "N2"));
        var status = service.GetStatus();
        Assert.True(status is { InGracePeriod: true, HotspotAllowed: true });
        Assert.Equal(revokedAt, status.RevokedAt);
        Assert.Equal(revokedAt.AddDays(7), status.StopsAt);

        Days(7);
        Assert.False(Service().GetStatus().HotspotAllowed);   // remembered after a restart
    }

    [Fact]
    public void Licence_works_offline_until_it_ends()   // (8)
    {
        var service = Service();
        service.Activate(Code(days: 365));
        Days(300);
        var status = Service().GetStatus();   // reloaded from licence.lic, no server ever contacted
        Assert.True(status is { Edition: Edition.Pro, Kind: LicenceKind.Licence, HotspotAllowed: true, InGracePeriod: false, LastCheckIn: null });
        Assert.Equal(1001u, status.LicenceId);
        Assert.Null(status.WarningDaysLeft);                  // 65 days left: no warning yet
        Days(40);
        Assert.Equal(25, Service().GetStatus().WarningDaysLeft);
        Days(25);
        Assert.True(Service().GetStatus().InGracePeriod);
        Days(7);
        Assert.False(Service().GetStatus().HotspotAllowed);
    }

    [Fact]
    public void Renewal_from_the_server_extends_and_old_or_expired_codes_are_refused()
    {
        var service = Service();
        var first = Code(days: 365);
        service.Activate(first);
        Days(350);
        var renewed = LicenceCode.Create(new LicenceData(1001, Edition.Pro, DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime),
            new DateOnly(2028, 3, 1), ThisPc, "Bar Chez Ngoma"), LicenceKey.Private);

        Assert.True(service.ApplyServerAnswer(Signed(ServerStatus.Renewed, null, "N", renewedCode: renewed), "N"));
        Assert.Equal(new DateTimeOffset(2028, 3, 2, 0, 0, 0, TimeSpan.Zero), service.GetStatus().ExpiresAt);
        Assert.Equal(ActivationResult.Older, service.Activate(first));
        Assert.Equal(ActivationResult.Expired, service.Activate(Code(days: 10, id: 2000, issued: new DateOnly(2026, 1, 1))));
    }

    [Fact]
    public void Server_time_counts_when_the_pc_is_behind()
    {
        var service = Service();
        service.Activate(Code(days: 30));
        Assert.True(service.ApplyServerAnswer(Signed(ServerStatus.Valid, null, "T", serverTime: _clock.GetUtcNow().AddDays(33)), "T"));
        Assert.True(service.GetStatus().InGracePeriod);   // the server says we are already past the end
    }

    private SignedAnswer Signed(string status, DateTimeOffset? revokedAt, string nonce, byte[]? key = null, string? renewedCode = null, DateTimeOffset? serverTime = null) =>
        SignedAnswer.Sign(new ServerAnswer(status, 1001, ThisPc.ToString(), nonce, serverTime ?? _clock.GetUtcNow(), revokedAt, renewedCode), key ?? ServerKey.Private);
}
