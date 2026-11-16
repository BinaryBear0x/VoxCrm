using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.Dashboard;

namespace VoxCrm.Web.ViewComponents;

public sealed class ClinicHeaderViewComponent(IClinicWorkspaceService workspace) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!HttpContext.User.IsInRole("Clinic")) return Content("VoxCRM");
        try { return Content(await workspace.GetClinicNameAsync(HttpContext.RequestAborted)); }
        catch (VoxCrm.Application.Workday.WorkdayException) { return Content("Kliniğim"); }
    }
}
