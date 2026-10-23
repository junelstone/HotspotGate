using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using StonesHotspot.Core;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Security;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Tests.Infrastructure;

/// <summary>
/// A migrated in-memory SQLite database with all Core services and a controllable clock. Every
/// <see cref="Run{TService,TResult}"/> call uses a new scope, like a web request or a tick in production,
/// so tests see what was really saved.
/// </summary>
public sealed class TestHost : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);

    public const string PhoneMac = "AA:BB:CC:00:00:01";
    public const string LaptopMac = "AA:BB:CC:00:00:02";
    public const string TabletMac = "AA:BB:CC:00:00:03";

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;

    public FakeTimeProvider Clock { get; } = new(Start);
    public FakeSmsTransportFactory Sms { get; } = new();
    public FakePayments Payments { get; } = new();
    public string? InitialAdminPassword { get; private set; }

    private TestHost(Action<IServiceCollection>? configure)
    {
        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        _connection.Open();

        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(Clock)
            .AddHotspotCore(o => o.UseSqlite(_connection))
            .AddSingleton<IPasswordHasher>(new BcryptPasswordHasher(workFactor: 4)) // fast hashing in tests
            .AddLogging()
            .AddSingleton<ISecretProtector>(new FakeSecretProtector())
            .AddSingleton<StonesHotspot.Core.Sms.ISmsTransportFactory>(Sms)
            .AddSingleton<StonesHotspot.Core.Payments.IPaymentProviderFactory>(Payments);
        configure?.Invoke(services);
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <param name="configure">Extra or replacement registrations (fakes) for the test.</param>
    public static async Task<TestHost> CreateAsync(Action<IServiceCollection>? configure = null)
    {
        var host = new TestHost(configure);
        host.InitialAdminPassword = (await host.Run<DatabaseInitializer, DatabaseInitResult>(i => i.InitializeAsync())).InitialAdminPassword;
        return host;
    }

    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    public void Advance(TimeSpan by) => Clock.Advance(by);

    public async Task<TResult> Run<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task Run<TService>(Func<TService, Task> action) where TService : notnull
    {
        await using var scope = _services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <summary>Reads the database in a fresh context.</summary>
    public Task<T> Query<T>(Func<HotspotDbContext, Task<T>> query) => Run(query);

    // ---- Helpers used by many tests ----

    public Task<Tariff> CreateTariffAsync(
        TariffType type = TariffType.TimeBased, long quota = 60, int price = 200,
        CountdownMode mode = CountdownMode.UsageOnly, int? validityHours = null, int maxDevices = 1,
        int? idleMinutes = null, string? name = null) =>
        Run<TariffService, Tariff>(s => s.CreateAsync(new Tariff
        {
            Name = name ?? $"Test {Guid.NewGuid():N}"[..20],
            Type = type,
            Quota = quota,
            Price = new Xaf(price),
            CountdownMode = mode,
            ValidityHours = validityHours ?? (type == TariffType.Unlimited ? 24 : null),
            MaxDevices = maxDevices,
            IdleTimeoutMinutes = idleMinutes,
        }, null));

    public async Task<Voucher> CreateVoucherAsync(Tariff tariff, bool singleUse = false) =>
        (await Run<VoucherService, List<Voucher>>(s => s.GenerateBatchAsync(tariff.Id, 1, "test", singleUse, null))).Single();

    public Task<Staff> AdminAsync() =>
        Query(db => db.Staff.SingleAsync(s => s.Username == DatabaseInitializer.AdminUsername));

    public Task<Staff> CreateCashierAsync(string username = "caissier") =>
        Run<StaffService, Staff>(s => s.CreateAsync(username, "motdepasse1", StaffRole.Cashier, null, null));

    public Task<LoginResult> LoginAsync(string mac, string code) =>
        Run<SessionService, LoginResult>(s => s.LoginWithVoucherAsync(mac, "192.168.137.10", code));

    public Task<IReadOnlyList<Session>> TickAsync(params (long SessionId, long Bytes)[] traffic) =>
        Run<SessionService, IReadOnlyList<Session>>(s => s.TickAsync(
            traffic.ToDictionary(t => t.SessionId, t => new TrafficDelta(t.Bytes / 2, t.Bytes - t.Bytes / 2))));

    public Task<Voucher> GetVoucherAsync(int id) => Query(db => db.Vouchers.AsNoTracking().SingleAsync(v => v.Id == id));

    public Task<Session> GetSessionAsync(long id) => Query(db => db.Sessions.AsNoTracking().SingleAsync(s => s.Id == id));

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
