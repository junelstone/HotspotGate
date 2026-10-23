using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

/// <summary>Connection logs keep MAC, IP and phone numbers only for the retention period; money records stay.</summary>
public sealed class RetentionTests
{
    [Fact]
    public async Task Old_connection_logs_lose_personal_data_and_recent_ones_and_sales_stay()
    {
        await using var host = await TestHost.CreateAsync();
        var tariff = await host.CreateTariffAsync(price: 500);

        // A year ago: a phone used a code, sent an SMS and paid with MTN; a whitelisted printer was seen.
        var oldLogin = await host.LoginAsync(TestHost.PhoneMac, (await host.CreateVoucherAsync(tariff)).Code);
        await host.Run<SessionService>(s => s.EndAsync(oldLogin.Session!.Id, SessionEndReason.Logout));
        await host.Run<DeviceService>(d => d.SetWhitelistedAsync(TestHost.TabletMac, true, null));
        await host.Run<HotspotDbContext>(async db =>
        {
            db.SmsMessages.Add(new SmsMessage { Phone = "+242061234567", Purpose = SmsPurpose.LoginCode, CreatedAt = host.Now, Sent = true });
            db.MobilePayments.Add(new MobilePayment
            {
                Reference = "R1", Method = PaymentMethod.MtnMoMo, Phone = "+242061234567", TariffId = tariff.Id, Amount = new Xaf(500),
                Status = MobilePaymentStatus.Successful, CreatedAt = host.Now,
            });
            await db.SaveChangesAsync();
        });

        host.Advance(TimeSpan.FromDays(400));
        // Recently: another phone is browsing now.
        var recent = await host.LoginAsync(TestHost.LaptopMac, (await host.CreateVoucherAsync(tariff)).Code);
        await host.Run<SettingsService>(s => s.SetAsync(SettingKeys.LogRetentionDays, "365", null));

        var result = await host.Run<RetentionService, RetentionResult>(r => r.ApplyAsync());

        Assert.Equal(new RetentionResult(Sessions: 1, Devices: 1, Phones: 2, SmsChallenges: 0), result);
        await host.Query(async db =>
        {
            var old = await db.Sessions.Include(s => s.Device).SingleAsync(s => s.Id == oldLogin.Session!.Id);
            Assert.Null(old.Ip);
            Assert.StartsWith(RetentionService.AnonymousPrefix, old.Device.Mac);
            Assert.Equal(tariff.Id, old.TariffId);   // what was sold and used stays for the reports

            var now = await db.Sessions.Include(s => s.Device).SingleAsync(s => s.Id == recent.Session!.Id);
            Assert.Equal(TestHost.LaptopMac, now.Device.Mac);
            Assert.NotNull(now.Ip);

            Assert.True(await db.Devices.AnyAsync(d => d.Mac == TestHost.TabletMac && d.Whitelisted));   // a rule, kept
            Assert.Equal("+24206****567", (await db.SmsMessages.SingleAsync()).Phone);
            var payment = await db.MobilePayments.SingleAsync();
            Assert.Equal("+24206****567", payment.Phone);
            Assert.Equal(500, payment.Amount.Amount);
            Assert.Contains(await db.AuditLogs.Select(a => a.Action).ToListAsync(), a => a == "Privacy.Retention");
            return 0;
        });

        // Running again changes nothing; the setting cannot go below 30 days.
        Assert.Equal(0, (await host.Run<RetentionService, RetentionResult>(r => r.ApplyAsync())).Total);
        await host.Run<SettingsService>(s => s.SetAsync(SettingKeys.LogRetentionDays, "1", null));
        Assert.Equal(RetentionService.MinDays, await host.Run<RetentionService, int>(r => r.DaysAsync()));
    }
}
