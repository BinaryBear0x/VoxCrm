using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using VoxCrm.Application.Workday;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
namespace VoxCrm.Infrastructure.FollowUp;

public sealed class OwnerFollowUpService(VoxCrmDbContext db, ITenantService tenant)
{
    private sealed record BoundTenant(Guid Id):ITenantService { public Guid GetClinicId()=>Id; public bool IsSystemContext=>false; }
    private Guid ClinicId=>tenant.GetClinicId() is var id && id!=Guid.Empty?id:throw new WorkdayException("Klinik hesabıyla giriş yapın.",true);
    private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static Guid RequestId(OwnerFollowUpShare s,Guid patient,string kind)=>new(SHA256.HashData(Encoding.UTF8.GetBytes($"follow-up:{s.ID}:{s.Revision}:{patient}:{kind}"))[..16]);
    private static string Snapshot(PetOwner o,IReadOnlyList<Patient> patients)=>Hash($"{o.ID}|{o.FirstName}|{o.LastName}|{o.Phone}|"+string.Join(";",patients.OrderBy(x=>x.ID).Select(x=>$"{x.ID}:{x.Name}:{x.Owners.Single(y=>y.PetOwnerId==o.ID && y.IsActive).ID}")));
    private async Task<ApplicationUser> Actor(Guid actor,CancellationToken ct)
    {
        if(!await db.Clinics.AnyAsync(x=>x.ID==ClinicId && x.IsActive && x.Dealer.IsActive,ct))throw new WorkdayException("Klinik hesabı aktif değil.",true);
        var user=await db.Users.SingleOrDefaultAsync(x=>x.Id==actor && x.ClinicId==ClinicId && !x.LoginDisabled,ct);
        if(user==null || user.LockoutEnd>DateTimeOffset.UtcNow || !await db.UserRoles.Join(db.Roles,x=>x.RoleId,x=>x.Id,(a,r)=>new{a.UserId,r.Name}).AnyAsync(x=>x.UserId==actor && x.Name=="Clinic",ct))throw new WorkdayException("Yetkili klinik hesabı gerekli.",true);
        return user;
    }
    private void Audit(string action,Guid id,Guid? actor=null)=>db.SystemAuditLogs.Add(new SystemAuditLog{ClinicId=ClinicId,ActorUserId=actor,Category="OwnerFollowUp",Action=action,EntityType="OwnerFollowUpShare",EntityId=id.ToString(),Message=action});
    public async Task<FollowUpManager> ManageAsync(Guid? ownerId,CancellationToken ct=default)
    {
        var owners=await db.PetOwners.Where(x=>x.ClinicID==ClinicId && x.IsActive).AsNoTracking().OrderBy(x=>x.FirstName).ThenBy(x=>x.LastName).ToListAsync(ct);
        var owner=owners.SingleOrDefault(x=>x.ID==ownerId);
        if(ownerId.HasValue && owner==null)throw new WorkdayException("Müşteri bulunamadı.",true);
        var patients=owner==null?new List<Patient>():await db.Patients.AsNoTracking().Where(x=>x.ClinicID==ClinicId && x.IsActive && x.Owners.Any(y=>y.IsActive && y.PetOwnerId==owner.ID)).OrderBy(x=>x.Name).ToListAsync(ct);
        var shares=await db.OwnerFollowUpShares.AsNoTracking().Where(x=>x.ClinicID==ClinicId).OrderByDescending(x=>x.CreatedAt).ToListAsync(ct);
        var requests=await db.ClinicTasks.AsNoTracking().Where(x=>x.ClinicID==ClinicId && x.IsActive && (x.Kind=="OwnerContact" || x.Kind=="OwnerAppointment") && x.Status=="Open").OrderBy(x=>x.DueDate).ToListAsync(ct);
        return new(owners,owner,patients,shares.SingleOrDefault(x=>x.PetOwnerId==ownerId),shares,requests);
    }
    public async Task<string> IssueAsync(FollowUpInput input,Guid actor,CancellationToken ct=default)
    {
        await Actor(actor,ct);
        if(!input.ConfirmRecipient || input.IssueId==Guid.Empty || input.PatientIds.Count is <1 or >30 || input.PatientIds.Distinct().Count()!=input.PatientIds.Count || input.Days is <1 or >30)throw new WorkdayException("Alıcıyı onaylayın, en az bir hasta ve 1–30 gün geçerlilik seçin.");
        if(input.PublicNote?.Length>2000)throw new WorkdayException("Not en fazla 2000 karakter olabilir.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ClinicId+":owner-share:"+input.OwnerId},0))",ct);
        var owner=await db.PetOwners.SingleOrDefaultAsync(x=>x.ID==input.OwnerId && x.ClinicID==ClinicId && x.IsActive,ct)??throw new WorkdayException("Müşteri bulunamadı.",true);
        var patients=await db.Patients.Include(x=>x.Owners).Where(x=>x.ClinicID==ClinicId && x.IsActive && input.PatientIds.Contains(x.ID) && x.Owners.Any(y=>y.IsActive && y.PetOwnerId==owner.ID)).ToListAsync(ct);
        if(patients.Count!=input.PatientIds.Count)throw new WorkdayException("Seçilen hastaların tamamı bu müşterinin aktif hastaları olmalı.");
        var share=await db.OwnerFollowUpShares.SingleOrDefaultAsync(x=>x.ClinicID==ClinicId && x.PetOwnerId==owner.ID,ct);
        if(share?.LastIssueId==input.IssueId)throw new WorkdayException("Bu form zaten işlendi. Listeye dönün; yeni bağlantı eski bağlantıyı kapatır.");
        if((share?.Revision??0)!=input.Revision)throw new WorkdayException("Bağlantı başka bir işlemde değişti. Sayfayı yenileyin.");
        if(share==null){share=new OwnerFollowUpShare{ClinicID=ClinicId,PetOwnerId=owner.ID};db.Add(share);}
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        share.PatientIdsJson=JsonSerializer.Serialize(input.PatientIds.OrderBy(x=>x));share.RecipientSnapshot=Snapshot(owner,patients);share.TokenHash=Hash(token);share.ExpiresAt=DateTime.UtcNow.AddDays(input.Days);share.IsActive=true;share.ShowVaccinations=input.ShowVaccinations;share.ShowHistory=input.ShowHistory;share.ShowAppointments=input.ShowAppointments;share.ShowCare=input.ShowCare;share.PublicNote=string.IsNullOrWhiteSpace(input.PublicNote)?null:input.PublicNote.Trim();share.NoteApprovedBy=share.PublicNote==null?null:actor;share.LastIssueId=input.IssueId;share.Revision++;
        Audit("OwnerFollowUp.LinkIssued",share.ID,actor);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return token;
    }
    public async Task RevokeAsync(Guid id,long revision,Guid actor,CancellationToken ct=default)
    {
        await Actor(actor,ct);await using var tx=await db.Database.BeginTransactionAsync(ct);
        var initial=await db.OwnerFollowUpShares.AsNoTracking().SingleOrDefaultAsync(x=>x.ID==id && x.ClinicID==ClinicId,ct)??throw new WorkdayException("Bağlantı bulunamadı.",true);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ClinicId+":owner-share:"+initial.PetOwnerId},0))",ct);
        var s=await db.OwnerFollowUpShares.SingleAsync(x=>x.ID==id && x.ClinicID==ClinicId,ct);
        if(s.Revision!=revision)throw new WorkdayException("Bağlantı değişti; sayfayı yenileyin.");
        s.TokenHash=null;s.IsActive=false;s.Revision++;Audit("OwnerFollowUp.LinkRevoked",s.ID,actor);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private async Task<(OwnerFollowUpShare Share,PetOwner Owner,Clinic Clinic,List<Patient> Patients)?> Resolve(string token,CancellationToken ct)
    {
        if(token.Length!=64 || token.Any(x=>!Uri.IsHexDigit(x)))return null;
        var hash=Hash(token);var share=await db.OwnerFollowUpShares.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.TokenHash==hash && x.IsActive && x.ExpiresAt>DateTime.UtcNow,ct);if(share==null)return null;
        var clinic=await db.Clinics.AsNoTracking().SingleOrDefaultAsync(x=>x.ID==share.ClinicID && x.IsActive && x.Dealer.IsActive,ct);
        var owner=await db.PetOwners.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.ID==share.PetOwnerId && x.ClinicID==share.ClinicID && x.IsActive,ct);if(clinic==null || (clinic.EnabledModules & 2)==0 || owner==null)return null;
        var ids=JsonSerializer.Deserialize<List<Guid>>(share.PatientIdsJson)??new();
        var patients=await db.Patients.IgnoreQueryFilters().AsNoTracking().Include(x=>x.Owners.Where(y=>y.IsActive && y.ClinicID==share.ClinicID)).Where(x=>x.ClinicID==share.ClinicID && x.IsActive && ids.Contains(x.ID) && x.Owners.Any(y=>y.IsActive && y.ClinicID==share.ClinicID && y.PetOwnerId==owner.ID)).OrderBy(x=>x.Name).ToListAsync(ct);
        return patients.Count==0 || patients.Count!=ids.Count || Snapshot(owner,patients)!=share.RecipientSnapshot?null:(share,owner,clinic,patients);
    }
    private static DateTime Local(DateTime value,TimeZoneInfo zone)=>TimeZoneInfo.ConvertTimeFromUtc(value.Kind==DateTimeKind.Local?value.ToUniversalTime():DateTime.SpecifyKind(value,DateTimeKind.Utc),zone);
    public async Task<PublicFollowUp?> ReadAsync(string token,CancellationToken ct=default)
    {
        var found=await Resolve(token,ct);if(found==null)return null;var(s,owner,clinic,patients)=found.Value;var zone=TimeZoneInfo.FindSystemTimeZoneById(clinic.TimeZoneId);var today=Local(DateTime.UtcNow,zone).Date;var output=new List<FollowUpPatient>();
        foreach(var p in patients)
        {
            var vaccines=new List<FollowUpVaccine>();
            if(s.ShowVaccinations || s.ShowHistory)
            {
                var rows=await db.VaccinationRecords.IgnoreQueryFilters().AsNoTracking().Include(x=>x.VaccineType).Where(x=>x.ClinicID==s.ClinicID && x.PatientId==p.ID && x.IsActive && x.VaccineType.ClinicID==s.ClinicID).OrderByDescending(x=>x.AdministeredDate).ToListAsync(ct);
                vaccines=rows.Select(x=>new FollowUpVaccine(x.VaccineType.Name,Local(x.AdministeredDate,zone).Date,Local(x.NextDueDate,zone).Date,!rows.Any(y=>y.VaccineTypeId==x.VaccineTypeId && y.AdministeredDate.Date>x.AdministeredDate.Date))).ToList();
            }
            if(!s.ShowHistory) vaccines=vaccines.Where(x=>x.Current).ToList();
            var appointments=new List<FollowUpAppointment>();
            if(s.ShowAppointments){var rows=await db.Appointments.IgnoreQueryFilters().AsNoTracking().Where(x=>x.ClinicID==s.ClinicID && x.PatientId==p.ID && x.IsActive && x.Status=="Planlandı" && x.ScheduledAt>DateTime.UtcNow).OrderBy(x=>x.ScheduledAt).ToListAsync(ct);appointments=rows.Select(x=>new FollowUpAppointment(Local(x.ScheduledAt,zone))).ToList();}
            var care=new List<FollowUpCare>();
            if(s.ShowCare)
            {
                var cards=await db.CareCards.IgnoreQueryFilters().AsNoTracking().Where(x=>x.ClinicID==s.ClinicID && x.PetOwnerId==owner.ID && x.IsActive && x.State=="Published" && x.ExpiresAt>DateTime.UtcNow && db.Muayeneler.IgnoreQueryFilters().Any(e=>e.ID==x.ExaminationId && e.ClinicID==s.ClinicID && e.IsActive && e.PatientId==p.ID)).OrderByDescending(x=>x.ApprovedAt).ToListAsync(ct);
                foreach(var c in cards)if(c.RecipientSnapshot==Hash($"{owner.ID}|{owner.FirstName}|{owner.LastName}|{owner.Phone}|{p.ID}|{p.Name}") && p.Owners.Any(x=>x.PetOwnerId==owner.ID && x.IsPrimaryOwner) && await db.Users.AnyAsync(x=>x.Id==c.ApprovedBy && x.ClinicId==s.ClinicID && !x.LoginDisabled && x.CanApproveCareCards,ct))care.Add(new(c.TodayActions,c.HomeSteps,c.CallInstructions,c.FollowUpDate));
            }
            var contact=await db.ClinicTasks.IgnoreQueryFilters().AnyAsync(x=>x.ClinicID==s.ClinicID && x.SubmissionId==RequestId(s,p.ID,"OwnerContact"),ct);
            var appointment=await db.ClinicTasks.IgnoreQueryFilters().AnyAsync(x=>x.ClinicID==s.ClinicID && x.SubmissionId==RequestId(s,p.ID,"OwnerAppointment"),ct);
            output.Add(new(p.ID,p.Name??"Hasta",p.Species,vaccines,appointments,care,contact,appointment));
        }
        var note=s.PublicNote;
        if(note!=null && !await db.Users.AnyAsync(x=>x.Id==s.NoteApprovedBy && x.ClinicId==s.ClinicID && !x.LoginDisabled,ct))note=null;
        return new(clinic.Name,clinic.phone,today,note,s.ShowVaccinations,s.ShowHistory,s.ShowAppointments,s.ShowCare,output);
    }
    public async Task<bool> RequestAsync(string token,Guid patientId,string kind,CancellationToken ct=default)
    {
        if(kind is not ("OwnerContact" or "OwnerAppointment"))return false;
        var initial=await Resolve(token,ct);if(initial==null || !initial.Value.Patients.Any(x=>x.ID==patientId))return false;
        if(tenant.GetClinicId()!=initial.Value.Share.ClinicID)
        {
            await using var boundDb=new VoxCrmDbContext((DbContextOptions<VoxCrmDbContext>)db.GetService<IDbContextOptions>(),new BoundTenant(initial.Value.Share.ClinicID));
            return await new OwnerFollowUpService(boundDb,new BoundTenant(initial.Value.Share.ClinicID)).RequestAsync(token,patientId,kind,ct);
        }
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ClinicId+":owner-share:"+initial.Value.Owner.ID},0))",ct);
        db.ChangeTracker.Clear();var found=await Resolve(token,ct);if(found==null || !found.Value.Patients.Any(x=>x.ID==patientId))return false;var(s,o,c,patients)=found.Value;
        var key=RequestId(s,patientId,kind);if(await db.ClinicTasks.AnyAsync(x=>x.ClinicID==ClinicId && x.SubmissionId==key,ct))return true;
        db.Add(new ClinicTask{ClinicID=ClinicId,PatientId=patientId,SubmissionId=key,Kind=kind,Title=(kind=="OwnerContact"?"Hasta sahibi aranmak istiyor — ":"Randevu talebi — ")+patients.Single(x=>x.ID==patientId).Name,Status="Open",Important=true,DueDate=DateOnly.FromDateTime(Local(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(c.TimeZoneId))),CreatedBy=Guid.Empty});
        Audit("OwnerFollowUp."+kind,s.ID);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return true;
    }
}
