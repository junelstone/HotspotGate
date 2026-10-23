using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Security;
using StonesHotspot.Core.Services;
using StonesHotspot.Core.Sms;

namespace StonesHotspot.Core.Payments;

public enum PayStartStatus { Started, NotAvailable, PlanNotSold, InvalidNumber, WrongOperator, AlreadyPending, TooMany, DeviceBlocked, OperatorError }

/// <param name="Reference">The payment to wait for (also with <see cref="PayStartStatus.AlreadyPending"/>).</param>
public sealed record PayStartResult(PayStartStatus Status, string? Reference = null, string? Error = null);

/// <summary>
/// Plans bought on the portal with MTN MoMo or Airtel Money: asks the operator, follows the payment (page polling,
/// background job, callbacks) and, exactly once per payment, creates the code, records the sale, logs the device in
/// and sends the code by SMS.
/// </summary>
public sealed class MobileMoneyService(
    HotspotDbContext db, SettingsService settings, ISecretProtector secrets, IPaymentProviderFactory providers,
    DeviceService devices, SessionService sessions, VoucherService vouchers, CashService cash, SmsService sms,
    AuditService audit, TimeProvider clock, ILogger<MobileMoneyService> logger)
{
    public const int MaxRequestsPerDevicePerHour = 5;
    public static readonly TimeSpan MinCheckInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan LateSuccessWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan StaleProcessing = TimeSpan.FromMinutes(2);

    public Task<PaymentSettings> GetSettingsAsync(CancellationToken ct = default) => PaymentSettings.LoadAsync(settings, secrets, ct);

    /// <summary>Plans offered on the portal: the chosen ones, or every active plan with a price.</summary>
    public async Task<List<Tariff>> ListSellableTariffsAsync(PaymentSettings config, CancellationToken ct = default) =>
        (await db.Tariffs.AsNoTracking().Where(t => t.IsActive).ToListAsync(ct))
            .Where(t => t.Price.Amount > 0 && (config.TariffIds.Count == 0 || config.TariffIds.Contains(t.Id)))
            .OrderBy(t => t.Price).ThenBy(t => t.Name).ToList();

    public async Task<PayStartResult> StartAsync(string mac, string? ip, int tariffId, PaymentMethod method, string? phoneText, CancellationToken ct = default)
    {
        var now = Now;
        var config = await GetSettingsAsync(ct);
        var provider = method is PaymentMethod.MtnMoMo or PaymentMethod.AirtelMoney ? providers.Create(method, config) : null;
        if (provider is null)
            return new(PayStartStatus.NotAvailable);

        var device = await devices.TouchAsync(mac, ip, null, ct);
        if (device.Id == 0)
            await db.SaveChangesAsync(ct);
        if (await sessions.CheckDeviceAsync(device, now, ct) is not null)
            return new(PayStartStatus.DeviceBlocked);

        var tariff = (await ListSellableTariffsAsync(config, ct)).FirstOrDefault(t => t.Id == tariffId);
        if (tariff is null)
            return new(PayStartStatus.PlanNotSold);
        var phone = PhoneNumber.Normalize(phoneText);
        if (phone is null)
            return new(PayStartStatus.InvalidNumber);
        if (!PhoneNumber.IsAllowed(phone, config.PrefixesFor(method)))
            return new(PayStartStatus.WrongOperator);

        var pending = await db.MobilePayments.Where(p => p.DeviceId == device.Id && p.Status == MobilePaymentStatus.Pending)
            .OrderByDescending(p => p.Id).FirstOrDefaultAsync(ct);
        if (pending is not null && now - pending.CreatedAt < config.Timeout)
            return new(PayStartStatus.AlreadyPending, pending.Reference);
        var hourAgo = now.AddHours(-1);
        if (await db.MobilePayments.CountAsync(p => p.DeviceId == device.Id && p.CreatedAt >= hourAgo, ct) >= MaxRequestsPerDevicePerHour)
            return new(PayStartStatus.TooMany);

        var business = await settings.GetAsync(SettingKeys.BusinessName, ct) ?? "";
        var payment = new MobilePayment
        {
            Reference = provider.NewReference(), Method = method, Phone = phone, TariffId = tariff.Id, Amount = tariff.Price,
            DeviceId = device.Id, Status = MobilePaymentStatus.Pending, CreatedAt = now,
        };
        db.MobilePayments.Add(payment);
        await db.SaveChangesAsync(ct);

        var callback = config.CallbackBaseUrl is { } baseUrl
            ? $"{baseUrl}/api/payments/{(method == PaymentMethod.MtnMoMo ? "mtn" : "airtel")}"
            : null;
        try
        {
            await provider.RequestToPayAsync(new PaymentRequest(payment.Reference, phone, tariff.Price, $"{business} Wi-Fi {tariff.Name}".Trim(), callback), ct);
        }
        catch (PaymentException ex)
        {
            logger.LogWarning("{Method} payment {Reference} could not be requested: {Error}", method, payment.Reference, ex.Message);
            payment.Status = MobilePaymentStatus.Failed;
            payment.FailureReason = Clip(ex.Message);
            payment.CompletedAt = Now;
            await db.SaveChangesAsync(ct);
            return new(PayStartStatus.OperatorError, payment.Reference, ex.Message);
        }

        logger.LogInformation("{Method} payment {Reference} requested: {Plan}, {Amount}, {Phone}", method, payment.Reference, tariff.Name, tariff.Price, SmsService.Mask(phone));
        return new(PayStartStatus.Started, payment.Reference);
    }

    public Task<MobilePayment?> FindAsync(string reference, CancellationToken ct = default) =>
        db.MobilePayments.AsNoTracking().Include(p => p.Tariff).Include(p => p.Voucher).Include(p => p.Device)
            .FirstOrDefaultAsync(p => p.Reference == reference, ct);

    /// <summary>
    /// Asks the operator about a pending payment (at most every few seconds) and applies the answer; also used for
    /// callbacks and for staff re-checking a timed-out payment (<paramref name="force"/>).
    /// </summary>
    public async Task<MobilePayment?> CheckAsync(string reference, bool force = false, CancellationToken ct = default)
    {
        var payment = await db.MobilePayments.AsNoTracking().FirstOrDefaultAsync(p => p.Reference == reference, ct);
        if (payment is null)
            return null;
        var now = Now;
        var open = payment.Status == MobilePaymentStatus.Pending
                   || payment.Status == MobilePaymentStatus.TimedOut && (force || now - payment.CreatedAt < LateSuccessWindow);
        if (!open || !force && payment.LastCheckedAt is { } last && now - last < MinCheckInterval)
            return await FindAsync(reference, ct);

        var config = await GetSettingsAsync(ct);
        var provider = providers.Create(payment.Method, config);
        OperatorPaymentStatus? state = null;
        if (provider is not null)
        {
            try
            {
                state = await provider.GetStatusAsync(payment.Reference, ct);
            }
            catch (PaymentException ex)
            {
                logger.LogWarning("{Method} payment {Reference}: status check failed: {Error}", payment.Method, reference, ex.Message);
            }
        }
        await db.MobilePayments.Where(p => p.Id == payment.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.LastCheckedAt, now), ct);

        switch (state?.State)
        {
            case OperatorPaymentState.Successful:
                if (state.Amount is { } paid && paid != payment.Amount.Amount)
                    await FailAsync(payment.Id, $"Amount paid ({paid}) differs from the price ({payment.Amount.Amount})", ct);
                else
                    await CompleteAsync(payment.Id, state.OperatorTransactionId, ct);
                break;
            case OperatorPaymentState.Failed:
                await FailAsync(payment.Id, state.Reason ?? "Refused", ct);
                break;
            default:
                if (payment.Status == MobilePaymentStatus.Pending && now - payment.CreatedAt >= config.Timeout)
                {
                    await db.MobilePayments.Where(p => p.Id == payment.Id && p.Status == MobilePaymentStatus.Pending)
                        .ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, MobilePaymentStatus.TimedOut).SetProperty(p => p.CompletedAt, now), ct);
                }
                break;
        }
        return await FindAsync(reference, ct);
    }

    /// <summary>Checks every pending payment (background job): payments finish even if the customer closed the page.</summary>
    public async Task<int> CheckPendingAsync(CancellationToken ct = default)
    {
        // A payment left "processing" (service stopped in the middle) goes back to pending: completing it is all-or-nothing.
        var stale = Now - StaleProcessing;
        await db.MobilePayments.Where(p => p.Status == MobilePaymentStatus.Processing && (p.LastCheckedAt == null || p.LastCheckedAt < stale))
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, MobilePaymentStatus.Pending), ct);
        var references = await db.MobilePayments.AsNoTracking().Where(p => p.Status == MobilePaymentStatus.Pending)
            .OrderBy(p => p.Id).Select(p => p.Reference).Take(50).ToListAsync(ct);
        foreach (var reference in references)
            await CheckAsync(reference, ct: ct);
        return references.Count;
    }

    /// <summary>Most recent payments, newest first, optionally of one local day range (UTC bounds).</summary>
    public Task<List<MobilePayment>> ListAsync(DateTime? fromUtc, DateTime? toUtc, int take = 200, CancellationToken ct = default) =>
        db.MobilePayments.AsNoTracking().Include(p => p.Tariff).Include(p => p.Voucher)
            .Where(p => (fromUtc == null || p.CreatedAt >= fromUtc) && (toUtc == null || p.CreatedAt < toUtc))
            .OrderByDescending(p => p.Id).Take(take).ToListAsync(ct);

    /// <summary>
    /// Creates the code, the sale and the login for a paid payment — once: the row is first claimed with a conditional
    /// update, so a callback and a poll arriving together cannot both do it.
    /// </summary>
    private async Task CompleteAsync(long paymentId, string? operatorTransactionId, CancellationToken ct)
    {
        var claimed = await db.MobilePayments
            .Where(p => p.Id == paymentId && (p.Status == MobilePaymentStatus.Pending || p.Status == MobilePaymentStatus.TimedOut))
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, MobilePaymentStatus.Processing), ct);
        if (claimed == 0)
            return;

        var payment = await db.MobilePayments.Include(p => p.Tariff).Include(p => p.Device).FirstAsync(p => p.Id == paymentId, ct);
        var now = Now;
        Voucher voucher;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            voucher = await vouchers.CreateForDeviceAsync(payment.Tariff, VoucherSource.MobileMoney, payment.DeviceId, payment.Phone, ct);
            voucher.Status = VoucherStatus.Sold;
            voucher.SoldAt = now;
            var receipt = await cash.NextReceiptNumberAsync(ct);
            await cash.AddTransactionAsync(TransactionType.VoucherSale, payment.Amount, payment.Method, null, receipt, ct, t =>
            {
                t.VoucherId = voucher.Id;
                t.Description = payment.Tariff.Name;
                t.ExternalReference = operatorTransactionId ?? payment.Reference;
            });
            payment.Status = MobilePaymentStatus.Successful;
            payment.OperatorTransactionId = operatorTransactionId;
            payment.CompletedAt = now;
            payment.VoucherId = voucher.Id;
            payment.ReceiptNumber = receipt;
            audit.Record(null, "MobileMoney.Paid", $"{payment.Method} {payment.Reference} ({operatorTransactionId}): {payment.Tariff.Name}, {payment.Amount}, receipt {receipt}");
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nothing was saved: give the payment back so the next check completes it.
            db.ChangeTracker.Clear();
            await db.MobilePayments.Where(p => p.Id == paymentId && p.Status == MobilePaymentStatus.Processing)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, MobilePaymentStatus.Pending), CancellationToken.None);
            logger.LogError(ex, "Paid {Method} payment {Reference}: could not create the code, will retry", payment.Method, payment.Reference);
            return;
        }
        logger.LogInformation("{Method} payment {Reference} paid ({OperatorId}), code created", payment.Method, payment.Reference, operatorTransactionId);

        // The customer may still be waiting on the portal: let the device in now. Failing this is not fatal (the code works).
        if (payment.Device is { } device)
        {
            var login = await sessions.LoginWithVoucherAsync(device.Mac, device.LastIp, voucher.Code, LoginMethod.MobileMoney, ct);
            if (!login.Succeeded)
                logger.LogInformation("Paid device {Mac} not logged in automatically: {Status}", device.Mac, login.Status);
        }

        if ((await sms.GetSettingsAsync(ct)).Configured)
        {
            var business = await settings.GetAsync(SettingKeys.BusinessName, ct) ?? "";
            var text = string.Format(CultureInfo.CurrentUICulture, Strings.Sms_VoucherCode, business, VoucherCodeGenerator.Format(voucher.Code), payment.Tariff.Name);
            await sms.SendAsync(payment.Phone, text, SmsPurpose.Voucher, payment.DeviceId, ct);
        }
    }

    private async Task FailAsync(long paymentId, string reason, CancellationToken ct)
    {
        var now = Now;
        var changed = await db.MobilePayments
            .Where(p => p.Id == paymentId && (p.Status == MobilePaymentStatus.Pending || p.Status == MobilePaymentStatus.TimedOut))
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, MobilePaymentStatus.Failed)
                .SetProperty(p => p.FailureReason, Clip(reason)).SetProperty(p => p.CompletedAt, now), ct);
        if (changed > 0)
            logger.LogInformation("Mobile Money payment {Id} failed: {Reason}", paymentId, reason);
    }

    private static string Clip(string text) => text.Length > 300 ? text[..300] : text;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
