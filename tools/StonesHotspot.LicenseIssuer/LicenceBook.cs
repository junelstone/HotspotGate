using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using StonesHotspot.Licensing;
using StonesHotspot.Licensing.Codes;

namespace StonesHotspot.LicenseIssuer;

public sealed record Customer(long Id, string Name, string? Phone, string? Business, DateTimeOffset CreatedAt);

/// <param name="Synced">False until the licence server has received this version (new, renewed or revoked).</param>
public sealed record IssuedLicence(
    uint Id, long CustomerId, string CustomerName, string? Phone, string MachineId, Edition Edition, DateOnly IssuedOn, DateOnly ExpiresOn,
    string Code, DateTimeOffset? RevokedAt, string? RevokedReason, bool Synced)
{
    public bool Revoked => RevokedAt is not null;
}

/// <summary>
/// The owner's book of customers and licences (SQLite file next to the key file), and the issuing rules: new licence,
/// renewal (from the current expiry when renewed early), revocation, CSV export, backup.
/// </summary>
public sealed class LicenceBook : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly OwnerKeys _keys;
    private readonly TimeProvider _clock;

    public LicenceBook(string databaseFile, OwnerKeys keys, TimeProvider? clock = null)
    {
        _keys = keys;
        _clock = clock ?? TimeProvider.System;
        DatabaseFile = databaseFile;
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databaseFile, Pooling = false }.ToString());
        _db.Open();
        Execute("""
            CREATE TABLE IF NOT EXISTS customers (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, phone TEXT, business TEXT, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS licences (id INTEGER PRIMARY KEY, customer_id INTEGER NOT NULL REFERENCES customers(id), machine_id TEXT NOT NULL,
                edition TEXT NOT NULL, issued_on TEXT NOT NULL, expires_on TEXT NOT NULL, code TEXT NOT NULL,
                revoked_at TEXT, revoked_reason TEXT, synced INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS licence_history (id INTEGER PRIMARY KEY AUTOINCREMENT, licence_id INTEGER NOT NULL, at TEXT NOT NULL,
                action TEXT NOT NULL, expires_on TEXT NOT NULL, code TEXT NOT NULL);
            """);
    }

    public string DatabaseFile { get; }

    public Customer AddCustomer(string name, string? phone, string? business)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("The customer name is required.", nameof(name));
        var now = _clock.GetUtcNow();
        var id = Scalar<long>("INSERT INTO customers (name, phone, business, created_at) VALUES ($n, $p, $b, $t); SELECT last_insert_rowid();",
            ("$n", name), ("$p", Clean(phone)), ("$b", Clean(business)), ("$t", now.ToString("O")));
        return new Customer(id, name, Clean(phone), Clean(business), now);
    }

    public IReadOnlyList<Customer> Customers() =>
        Query("SELECT id, name, phone, business, created_at FROM customers ORDER BY name",
            r => new Customer(r.GetInt64(0), r.GetString(1), Nullable(r, 2), Nullable(r, 3), DateTimeOffset.Parse(r.GetString(4), CultureInfo.InvariantCulture)));

    /// <summary>Signs a new licence for a PC. <paramref name="days"/>: 365 for one year, or any number of days.</summary>
    public IssuedLicence Issue(long customerId, string machineIdText, Edition edition, int days)
    {
        if (!MachineId.TryParse(machineIdText, out var machine))
            throw new ArgumentException("Invalid Machine ID (expected MID-XXXX-XXXX-XXXX).", nameof(machineIdText));
        if (edition is not (Edition.Basic or Edition.Pro))
            throw new ArgumentException("A licence is Basic or Pro.", nameof(edition));
        if (days is < 1 or > 3660)
            throw new ArgumentException("Duration must be 1 to 3660 days.", nameof(days));
        var customer = Customers().FirstOrDefault(c => c.Id == customerId) ?? throw new ArgumentException("Unknown customer.", nameof(customerId));

        var today = Today;
        var id = (uint)Math.Max(1000, Scalar<long>("SELECT COALESCE(MAX(id), 1000) FROM licences") + 1);
        var data = new LicenceData(id, edition, today, today.AddDays(days - 1), machine, customer.Business ?? customer.Name);
        var code = LicenceCode.Create(data, _keys.LicencePrivate);
        Execute("""
            INSERT INTO licences (id, customer_id, machine_id, edition, issued_on, expires_on, code, synced)
            VALUES ($id, $c, $m, $e, $i, $x, $code, 0)
            """, ("$id", (long)id), ("$c", customerId), ("$m", machine.ToString()), ("$e", edition.ToString()),
            ("$i", Day(data.IssuedOn)), ("$x", Day(data.ExpiresOn)), ("$code", code));
        History(id, "issued", data.ExpiresOn, code);
        return Find(id)!;
    }

    /// <summary>
    /// Extends a licence by <paramref name="days"/>: from its current expiry when it is still running (early renewal
    /// loses nothing), from today when it has already ended. Same licence number, new code.
    /// </summary>
    public IssuedLicence Renew(uint licenceId, int days)
    {
        var licence = Find(licenceId) ?? throw new ArgumentException("Unknown licence.", nameof(licenceId));
        if (licence.Revoked)
            throw new InvalidOperationException("A revoked licence cannot be renewed: issue a new one.");
        if (days is < 1 or > 3660)
            throw new ArgumentException("Duration must be 1 to 3660 days.", nameof(days));
        var today = Today;
        var from = licence.ExpiresOn >= today ? licence.ExpiresOn.AddDays(1) : today;
        MachineId.TryParse(licence.MachineId, out var machine);
        var data = new LicenceData(licenceId, licence.Edition, today, from.AddDays(days - 1), machine, Customers().First(c => c.Id == licence.CustomerId) is var c ? c.Business ?? c.Name : "");
        var code = LicenceCode.Create(data, _keys.LicencePrivate);
        Execute("UPDATE licences SET issued_on = $i, expires_on = $x, code = $code, synced = 0 WHERE id = $id",
            ("$i", Day(today)), ("$x", Day(data.ExpiresOn)), ("$code", code), ("$id", (long)licenceId));
        History(licenceId, "renewed", data.ExpiresOn, code);
        return Find(licenceId)!;
    }

    /// <summary>Marks a licence revoked; the hotspot learns it at its next check-in and stops after the grace period.</summary>
    public IssuedLicence Revoke(uint licenceId, string reason)
    {
        var licence = Find(licenceId) ?? throw new ArgumentException("Unknown licence.", nameof(licenceId));
        Execute("UPDATE licences SET revoked_at = $t, revoked_reason = $r, synced = 0 WHERE id = $id",
            ("$t", _clock.GetUtcNow().ToString("O")), ("$r", Clean(reason)), ("$id", (long)licenceId));
        History(licenceId, "revoked", licence.ExpiresOn, licence.Code);
        return Find(licenceId)!;
    }

    public IssuedLicence? Find(uint licenceId) => Licences().FirstOrDefault(l => l.Id == licenceId);

    public IReadOnlyList<IssuedLicence> Licences() =>
        Query("""
            SELECT l.id, l.customer_id, c.name, c.phone, l.machine_id, l.edition, l.issued_on, l.expires_on, l.code, l.revoked_at, l.revoked_reason, l.synced
            FROM licences l JOIN customers c ON c.id = l.customer_id ORDER BY l.id DESC
            """,
            r => new IssuedLicence((uint)r.GetInt64(0), r.GetInt64(1), r.GetString(2), Nullable(r, 3), r.GetString(4), Enum.Parse<Edition>(r.GetString(5)),
                DateOnly.ParseExact(r.GetString(6), "yyyy-MM-dd", CultureInfo.InvariantCulture), DateOnly.ParseExact(r.GetString(7), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.GetString(8), Nullable(r, 9) is { } t ? DateTimeOffset.Parse(t, CultureInfo.InvariantCulture) : null, Nullable(r, 10), r.GetInt64(11) == 1));

    public IReadOnlyList<IssuedLicence> Unsynced() => Licences().Where(l => !l.Synced).ToList();

    public void MarkSynced(IEnumerable<uint> ids)
    {
        foreach (var id in ids)
            Execute("UPDATE licences SET synced = 1 WHERE id = $id", ("$id", (long)id));
    }

    /// <summary>CSV for Excel (";", UTF-8 with BOM).</summary>
    public byte[] ExportCsv()
    {
        var sb = new StringBuilder("licence;customer;phone;machine_id;edition;issued_on;expires_on;revoked_at;revoked_reason\n");
        foreach (var l in Licences())
            sb.Append(CultureInfo.InvariantCulture, $"{l.Id};{Csv(l.CustomerName)};{Csv(l.Phone)};{l.MachineId};{l.Edition};{Day(l.IssuedOn)};{Day(l.ExpiresOn)};{l.RevokedAt:yyyy-MM-dd};{Csv(l.RevokedReason)}\n");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    /// <summary>A zip with a consistent copy of the database and the (encrypted) key file.</summary>
    public void Backup(string zipPath, string keyFile)
    {
        var copy = Path.Combine(Path.GetTempPath(), $"licences-{Guid.NewGuid():N}.db");
        using (var target = new SqliteConnection($"Data Source={copy};Pooling=False"))
        {
            target.Open();
            _db.BackupDatabase(target);
        }
        try
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(copy, "licences.db");
            zip.CreateEntryFromFile(keyFile, Path.GetFileName(keyFile));
        }
        finally
        {
            File.Delete(copy);
        }
    }

    public void Dispose() => _db.Dispose();

    private DateOnly Today => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

    private void History(uint id, string action, DateOnly expires, string code) =>
        Execute("INSERT INTO licence_history (licence_id, at, action, expires_on, code) VALUES ($id, $t, $a, $x, $c)",
            ("$id", (long)id), ("$t", _clock.GetUtcNow().ToString("O")), ("$a", action), ("$x", Day(expires)), ("$c", code));

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, parameters);
        command.ExecuteNonQuery();
    }

    private T Scalar<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, parameters);
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), CultureInfo.InvariantCulture);
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map)
    {
        using var command = Command(sql, []);
        using var reader = command.ExecuteReader();
        var list = new List<T>();
        while (reader.Read())
            list.Add(map(reader));
        return list;
    }

    private SqliteCommand Command(string sql, (string Name, object? Value)[] parameters)
    {
        var command = _db.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static string? Nullable(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Csv(string? v) => v is null ? "" : v.IndexOfAny([';', '"', '\n']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
}
