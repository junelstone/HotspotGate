using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>
/// Printable codes: the ticket of one sale (<c>?receipt=</c>, any staff) or a whole batch (<c>?batch=</c>, admins),
/// as A4 cards or 80/58 mm thermal tickets. Each code has a QR that opens the portal with the code filled in.
/// </summary>
public sealed class PrintModel(
    VoucherService vouchers, HotspotDbContext db, SettingsService settings, PortalLocation portal, IAuthorizationService authorization) : PageModel
{
    public const string FallbackGateway = "192.168.137.1";

    [BindProperty(SupportsGet = true)] public string? Receipt { get; set; }
    [BindProperty(SupportsGet = true)] public string? Batch { get; set; }
    [BindProperty(SupportsGet = true)] public string Format { get; set; } = "80";

    public IReadOnlyList<Voucher> Vouchers { get; private set; } = [];
    public string BusinessName { get; private set; } = "";
    public string? WifiName { get; private set; }
    public Transaction? Sale { get; private set; }
    public Xaf Total { get; private set; }
    public string? Seller { get; private set; }

    public bool IsA4 => Format == "a4";
    public int WidthMm => Format == "58" ? 48 : 72;

    public async Task<IActionResult> OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        if (Format is not ("a4" or "80" or "58"))
            Format = "80";

        if (!string.IsNullOrEmpty(Receipt))
        {
            Vouchers = await vouchers.ListByReceiptAsync(Receipt, ct);
            var lines = await db.Transactions.AsNoTracking().Include(t => t.Staff).Where(t => t.ReceiptNumber == Receipt).ToListAsync(ct);
            Sale = lines.FirstOrDefault();
            Total = Xaf.Sum(lines.Select(l => l.Amount));
            Seller = Sale?.Staff?.DisplayName ?? Sale?.Staff?.Username;
        }
        else if (!string.IsNullOrEmpty(Batch))
        {
            if (!(await authorization.AuthorizeAsync(User, StaffPolicies.Admin)).Succeeded)
                return Forbid();
            Vouchers = (await vouchers.ListBatchAsync(Batch, ct))
                .Where(v => v.Status is VoucherStatus.New or VoucherStatus.Sold).ToList();
        }
        if (Vouchers.Count == 0)
            return NotFound();

        BusinessName = await settings.GetAsync(SettingKeys.BusinessName, ct) ?? "";
        WifiName = await settings.GetAsync(SettingKeys.WifiName, ct);
        return Page();
    }

    /// <summary>The portal address, printed for customers whose phone cannot scan the QR.</summary>
    public string PortalAddress
    {
        get
        {
            var gateway = portal.Gateway?.ToString() ?? FallbackGateway;
            return portal.PortalPort == 80 ? $"http://{gateway}/portal" : $"http://{gateway}:{portal.PortalPort}/portal";
        }
    }

    /// <summary>What the QR opens: the portal with the code filled in.</summary>
    public string PortalLink(Voucher v) => $"{PortalAddress}?code={VoucherCodeGenerator.Format(v.Code)}";
}
