using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StonesHotspot.Core;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Localization;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;
using StonesHotspot.Web.Admin;
using StonesHotspot.Web.Auth;

namespace StonesHotspot.Web.Pages.Admin;

[Authorize(Policy = StaffPolicies.Admin)]
[RequestSizeLimit(512 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 512 * 1024 * 1024)]
public sealed class BackupModel(BackupService backups, SessionService sessions, AccessRefreshSignal refresh, CurrentStaff current) : PageModel
{
    public IReadOnlyList<BackupFile> Files { get; private set; } = [];
    [BindProperty] public IFormFile? Upload { get; set; }

    public void OnGet() => Files = backups.List();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var name = await backups.CreateAsync(await current.GetAsync(), ct: HttpContext.RequestAborted);
        return Download(name);
    }

    public IActionResult OnGetDownload(string name) => Download(name);

    public async Task<IActionResult> OnPostRestoreAsync()
    {
        if (Upload is null || Upload.Length == 0)
        {
            TempData.Error(Strings.Backup_NoFile);
            return RedirectToPage();
        }
        try
        {
            await using var stream = Upload.OpenReadStream();
            var safety = await backups.RestoreAsync(stream, await current.GetAsync(), HttpContext.RequestAborted);
            await sessions.ResumeAfterRestartAsync(HttpContext.RequestAborted);
            refresh.Request();
            TempData.Success(string.Format(Strings.Backup_Restored, safety));
        }
        catch (BusinessRuleException)
        {
            TempData.Error(Strings.Backup_InvalidFile);
        }
        return RedirectToPage();
    }

    private IActionResult Download(string name) =>
        backups.PathOf(name) is { } path ? PhysicalFile(path, "application/octet-stream", name) : NotFound();
}
