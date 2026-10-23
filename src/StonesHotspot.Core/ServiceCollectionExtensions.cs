using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Security;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the database and the business services (scoped: one unit of work per request/tick).</summary>
    public static IServiceCollection AddHotspotCore(this IServiceCollection services, string databaseFile) =>
        services.AddHotspotCore(o => o.UseSqlite($"Data Source={databaseFile};Foreign Keys=True"));

    public static IServiceCollection AddHotspotCore(this IServiceCollection services, Action<DbContextOptionsBuilder> configureDatabase)
    {
        services.AddDbContext<HotspotDbContext>(configureDatabase);
        services.AddSingleton<IPasswordHasher>(new BcryptPasswordHasher());
        services.AddScoped<AuditService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ReceiptNumbers>();
        services.AddScoped<CashService>();
        services.AddScoped<TariffService>();
        services.AddScoped<VoucherService>();
        services.AddScoped<ProductService>();
        services.AddScoped<SalesService>();
        services.AddScoped<ReportService>();
        services.AddScoped<FreeAccessService>();
        services.AddScoped<SmsLoginService>();
        services.AddScoped<Sms.SmsService>();
        services.AddScoped<Payments.MobileMoneyService>();
        services.AddSingleton<Payments.IPaymentProviderFactory>(sp =>
            new Payments.HttpPaymentProviderFactory(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<AccountService>();
        services.AddScoped<StaffService>();
        services.AddScoped<SetupService>();
        services.AddScoped<RetentionService>();
        services.AddSingleton<SetupState>();
        services.AddScoped<DeviceService>();
        services.AddScoped<SessionService>();
        services.AddScoped<WebFilterService>();
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<BackupService>();
        return services;
    }
}
