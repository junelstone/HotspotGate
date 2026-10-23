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
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

/// <summary>
/// Printable codes on A4: the codes of one sale (<c>?receipt=</c>, any staff) or a whole batch (<c>?batch=</c>, admins),
/// as cards to cut out. Each code has a QR that opens the portal with the code filled in.
/// </summary>
public sealed class PrintModel(
    VoucherService vouchers, HotspotDbContext db, SettingsService settings, PortalLocation portal, IAuthorizationService authorization) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Receipt { get; set; }
    [BindProperty(SupportsGet = true)] public string? Batch { get; set; }

    public IReadOnlyList<Voucher> Vouchers { get; private set; } = [];
    public string BusinessName { get; private set; } = "";
    public string? WifiName { get; private set; }
    public Transaction? Sale { get; private set; }
    public Xaf Total { get; private set; }
    public string? Seller { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
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
    public string PortalAddress => PortalLinks.Address(portal);

    /// <summary>What the QR opens: the portal with the code filled in.</summary>
    public string PortalLink(Voucher v) => PortalLinks.ForCode(portal, v.Code);
}
