using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
namespace VoxCrm.Web.Services;
public sealed class ClinicModules(VoxCrmDbContext db, ITenantService tenant)
{
    private Clinic? current;
    public async Task<Clinic?> CurrentAsync()=>current??=await db.Clinics.AsNoTracking().SingleOrDefaultAsync(x=>x.ID==tenant.GetClinicId());
    public async Task<bool> QuickVisitAsync()=>((await CurrentAsync())?.EnabledModules & 1)==1;
    public async Task<bool> FollowUpAsync()=>((await CurrentAsync())?.EnabledModules & 2)==2;
    public async Task<bool> WorkdayAsync()=>((await CurrentAsync())?.EnabledModules & 4)==4;
    public async Task<bool> WhatsAppAsync()=>(await CurrentAsync())?.IsWhatsAppEnabled==true;
    public Task<Clinic?> OwnedAsync(Guid dealer,Guid id,CancellationToken ct)=>db.Clinics.AsNoTracking().SingleOrDefaultAsync(x=>x.ID==id&&x.DealerId==dealer,ct);
    public async Task SaveAsync(Guid dealer,Guid actor,Guid id,long revision,bool quick,bool follow,bool whatsapp,CancellationToken ct,bool workday=false)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var clinic=await db.Clinics.FromSqlInterpolated($"SELECT * FROM \"Clinics\" WHERE \"ID\"={id} AND \"DealerId\"={dealer} FOR UPDATE").SingleOrDefaultAsync(ct)??throw new KeyNotFoundException();
        if(clinic.ModulesRevision!=revision)throw new InvalidOperationException("Modüller başka bir işlemle değiştirildi. Sayfayı yenileyin.");
        var before=new{clinic.EnabledModules,clinic.IsWhatsAppEnabled};clinic.EnabledModules=(quick?1:0)|(follow?2:0)|(workday?4:0);clinic.IsWhatsAppEnabled=whatsapp;if(!whatsapp)clinic.AutomaticVaccinationRemindersEnabled=false;clinic.ModulesRevision++;
        db.SystemAuditLogs.Add(new SystemAuditLog{ClinicId=id,DealerId=dealer,ActorUserId=actor,Category="Clinic",Action="Clinic.ModulesUpdated",EntityId=id.ToString(),EntityType="Clinic",Message="Klinik modülleri bayi tarafından güncellendi.",MetadataJson=System.Text.Json.JsonSerializer.Serialize(new{Before=before,After=new{clinic.EnabledModules,clinic.IsWhatsAppEnabled}})});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
public sealed class ClinicModuleFilter(ClinicModules modules):IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next)
    {
        if(!context.HttpContext.User.IsInRole("Clinic")){await next();return;}
        var controller=context.RouteData.Values["controller"]?.ToString();var action=context.RouteData.Values["action"]?.ToString();
        var allowed=controller switch
        {
            "QuickVisit"=>await modules.QuickVisitAsync(),
            "OwnerFollowUp"=>await modules.FollowUpAsync(),
            "WhatsApp"=>await modules.WhatsAppAsync(),
            "Workday" when action is "SaveCare" or "PublishCare" or "RevokeCare"=>await modules.FollowUpAsync(),
            "Workday" when action is "Index" or "Task" or "SaveTask" or "TaskStatus" or "Acknowledge"=>await modules.WorkdayAsync(),
            _=>true
        };
        if(!allowed){context.Result=new ViewResult{ViewName="~/Views/Shared/ModuleUnavailable.cshtml",StatusCode=403};return;}
        await next();
    }
}
