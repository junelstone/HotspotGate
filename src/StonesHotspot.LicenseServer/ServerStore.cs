using System.Globalization;
using Microsoft.Data.Sqlite;

namespace StonesHotspot.LicenseServer;

public sealed record ServerLicence(uint Id, string Customer, string MachineId, string Edition, DateOnly IssuedOn, DateOnly ExpiresOn, string Code, DateTimeOffset? RevokedAt);
public sealed record ServerTrial(string MachineId, DateTimeOffset FirstRun, DateTimeOffset RegisteredAt, DateTimeOffset LastSeen, string? Version);
public sealed record ServerCheckIn(long Id, uint? LicenceId, string MachineId, string Kind, string Result, DateTimeOffset At, string? Ip, string? Version);

/// <summary>The licence server's own SQLite database: licences (uploaded by LicenseManager), trials, check-ins.</summary>
public sealed class ServerStore
{
    private readonly string _connection;
    private readonly object _lock = new();

    public ServerStore(string databaseFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databaseFile))!);
        _connection = new SqliteConnectionStringBuilder { DataSource = databaseFile }.ToString();
        Execute("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS licences (id INTEGER PRIMARY KEY, customer TEXT NOT NULL, machine_id TEXT NOT NULL, edition TEXT NOT NULL,
                issued_on TEXT NOT NULL, expires_on TEXT NOT NULL, code TEXT NOT NULL, revoked_at TEXT);
            CREATE TABLE IF NOT EXISTS trials (machine_id TEXT PRIMARY KEY, first_run TEXT NOT NULL, registered_at TEXT NOT NULL, last_seen TEXT NOT NULL, version TEXT);
            CREATE TABLE IF NOT EXISTS checkins (id INTEGER PRIMARY KEY AUTOINCREMENT, licence_id INTEGER, machine_id TEXT NOT NULL, kind TEXT NOT NULL,
                result TEXT NOT NULL, at TEXT NOT NULL, ip TEXT, version TEXT);
            CREATE INDEX IF NOT EXISTS ix_checkins_at ON checkins (at);
            """);
    }

    /// <summary>Adds or replaces licences: a newer code (renewal) or a revocation from LicenseManager.</summary>
    public void UpsertLicences(IEnumerable<ServerLicence> licences)
    {
        lock (_lock)
        {
            using var db = Open();
            using var transaction = db.BeginTransaction();
            foreach (var l in licences)
            {
                Execute(db, """
                    INSERT INTO licences (id, customer, machine_id, edition, issued_on, expires_on, code, revoked_at)
                    VALUES ($id, $c, $m, $e, $i, $x, $code, $r)
                    ON CONFLICT(id) DO UPDATE SET customer = $c, machine_id = $m, edition = $e, issued_on = $i, expires_on = $x, code = $code,
                        revoked_at = COALESCE($r, licences.revoked_at)
                    """, ("$id", (long)l.Id), ("$c", l.Customer), ("$m", l.MachineId), ("$e", l.Edition), ("$i", Day(l.IssuedOn)),
                    ("$x", Day(l.ExpiresOn)), ("$code", l.Code), ("$r", l.RevokedAt?.ToString("O")));
            }
            transaction.Commit();
        }
    }

    /// <summary>Adds a licence seen at activation, unless the server already knows a newer version of it.</summary>
    public void AddIfNewer(ServerLicence licence)
    {
        if (Find(licence.Id) is { } known && known.IssuedOn >= licence.IssuedOn)
            return;
        UpsertLicences([licence]);
    }

    public ServerLicence? Find(uint id) => Licences($"WHERE id = {id}").FirstOrDefault();

    public IReadOnlyList<ServerLicence> Licences(string where = "") =>
        Query($"SELECT id, customer, machine_id, edition, issued_on, expires_on, code, revoked_at FROM licences {where} ORDER BY id DESC",
            r => new ServerLicence((uint)r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), D(r.GetString(4)), D(r.GetString(5)), r.GetString(6),
                r.IsDBNull(7) ? null : T(r.GetString(7))));

    /// <summary>Registers a trial; the earliest first-run date ever seen for this PC is kept and returned.</summary>
    public ServerTrial RegisterTrial(string machineId, DateTimeOffset firstRun, DateTimeOffset now, string? version)
    {
        lock (_lock)
        {
            Execute("""
                INSERT INTO trials (machine_id, first_run, registered_at, last_seen, version) VALUES ($m, $f, $n, $n, $v)
                ON CONFLICT(machine_id) DO UPDATE SET first_run = MIN(trials.first_run, $f), last_seen = $n, version = $v
                """, ("$m", machineId), ("$f", firstRun.ToUniversalTime().ToString("O")), ("$n", now.ToString("O")), ("$v", version));
            return Trials($"WHERE machine_id = '{machineId.Replace("'", "''")}'").Single();
        }
    }

    public IReadOnlyList<ServerTrial> Trials(string where = "") =>
        Query($"SELECT machine_id, first_run, registered_at, last_seen, version FROM trials {where} ORDER BY registered_at DESC",
            r => new ServerTrial(r.GetString(0), T(r.GetString(1)), T(r.GetString(2)), T(r.GetString(3)), r.IsDBNull(4) ? null : r.GetString(4)));

    public void RecordCheckIn(uint? licenceId, string machineId, string kind, string result, DateTimeOffset at, string? ip, string? version) =>
        Execute("INSERT INTO checkins (licence_id, machine_id, kind, result, at, ip, version) VALUES ($l, $m, $k, $r, $a, $ip, $v)",
            ("$l", licenceId is { } id ? (long)id : null), ("$m", machineId), ("$k", kind), ("$r", result), ("$a", at.ToString("O")), ("$ip", ip), ("$v", version));

    public IReadOnlyList<ServerCheckIn> CheckIns(int take) =>
        Query($"SELECT id, licence_id, machine_id, kind, result, at, ip, version FROM checkins ORDER BY id DESC LIMIT {take}",
            r => new ServerCheckIn(r.GetInt64(0), r.IsDBNull(1) ? null : (uint)r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4), T(r.GetString(5)),
                r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7)));

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connection);
        db.Open();
        return db;
    }

    private void Execute(string sql, params (string, object?)[] parameters)
    {
        using var db = Open();
        Execute(db, sql, parameters);
    }

    private static void Execute(SqliteConnection db, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var list = new List<T>();
        while (reader.Read())
            list.Add(map(reader));
        return list;
    }

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateTimeOffset T(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture);
}
