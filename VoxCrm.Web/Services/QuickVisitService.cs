using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VoxCrm.Application.Finance;
using VoxCrm.Application.Vaccinations;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Web.Models;
namespace VoxCrm.Web.Services;
public sealed class QuickVisitService(VoxCrmDbContext db,ITenantService tenant,IMemoryCache cache,IVaccinationService vaccines,IFinanceService finance)
{
    private Guid Clinic=>tenant.GetClinicId() is var id&&id!=Guid.Empty?id:throw new InvalidOperationException("Klinik bulunamadı.");
    private string Key(Guid actor,Guid token)=>$"visit:{Clinic}:{actor}:{token}";
    public async Task ChoicesAsync(QuickVisitModel model,CancellationToken ct)
    {
        model.Patients=await db.Patients.Where(x=>x.ClinicID==Clinic&&x.IsActive).Include(x=>x.Owners.Where(o=>o.IsActive)).ThenInclude(x=>x.PetOwner).AsNoTracking().OrderBy(x=>x.Name).ToListAsync(ct);
        model.Types=await db.VaccineTypes.Where(x=>x.ClinicID==Clinic&&x.IsActive).AsNoTracking().OrderBy(x=>x.Name).ToListAsync(ct);
    }
    public Task<DateOnly> TodayAsync(CancellationToken ct)=>ClinicCalendar.TodayAsync(db,Clinic,ct);
    public static (string? Weight,string? Amount,string? Paid) Parse(string text)
    {
        var weight=Regex.Match(text,@"(?<!\d)(\d{1,3}(?:[,.]\d{1,3})?)\s*(?:kilo(?:gram)?|kg)\b(?:\s*(\d{1,3})\s*gram)?",RegexOptions.IgnoreCase);
        string? w=weight.Success?weight.Groups[1].Value.Replace(',','.') : null;
        if(w!=null&&weight.Groups[2].Success){var value=decimal.Parse(w,CultureInfo.InvariantCulture)+decimal.Parse(weight.Groups[2].Value,CultureInfo.InvariantCulture)/1000m;w=value.ToString(CultureInfo.InvariantCulture);}
        string? Money(string pattern){var m=Regex.Match(text,pattern,RegexOptions.IgnoreCase);return m.Success?m.Groups[1].Value.Replace(".","").Replace(',','.') : null;}
        const string amount=@"(\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?|\d+(?:,\d{1,2})?)";
        return(w,Money(@"toplam\s*"+amount+@"\s*(?:lira|tl|₺)"),Money(amount+@"\s*(?:lira|tl|₺)\s*(?:(?:nakit|kartla|havale)\s*)?(?:ödendi|aldım|ödeme alındı)"));
    }
    public async Task<QuickVisitPreview> PreviewAsync(QuickVisitModel m,Guid actor,CancellationToken ct)
    {
        var enabled=await db.Clinics.Where(x=>x.ID==Clinic).Select(x=>x.EnabledModules).SingleAsync(ct);
        if((enabled&1)==0)throw new InvalidOperationException("Hızlı ziyaret modülü kapalı.");
        if((enabled&2)==0&&!string.IsNullOrWhiteSpace(m.HomeSteps))throw new InvalidOperationException("Bakım kartı modülü kapalı. Bakım notunu kaldırın.");
        var zone=await db.Clinics.Where(x=>x.ID==Clinic).Select(x=>x.TimeZoneId).SingleAsync(ct);
        var today=ClinicTime.Today(zone).ToDateTime(TimeOnly.MinValue);
        if(m.SubmissionId==Guid.Empty||string.IsNullOrWhiteSpace(m.Notes)||m.Notes.Length>4000||(m.HomeSteps?.Length??0)>2000||(m.CallInstructions?.Length??0)>1000)throw new InvalidOperationException("Muayene notu ve geçerli form bilgileri gerekli.");
        if(m.VisitDate.Year<2000||m.VisitDate.Date>today||m.DueDate.Year is <2000 or >2100||m.FollowUpDate?.Year is <2000 or >2100)throw new InvalidOperationException("Tarihleri kontrol edin; ziyaret gelecekte olamaz.");
        var patient=await db.Patients.AsNoTracking().SingleOrDefaultAsync(x=>x.ClinicID==Clinic&&x.ID==m.PatientId&&x.IsActive,ct)??throw new InvalidOperationException("Aktif hasta seçin.");
        var owner=await db.PatientOwners.Where(x=>x.ClinicID==Clinic&&x.PatientId==m.PatientId&&x.PetOwnerId==m.OwnerId&&x.IsActive&&x.PetOwner.IsActive&&x.PetOwner.ClinicID==Clinic).Select(x=>x.PetOwner).AsNoTracking().SingleOrDefaultAsync(ct)??throw new InvalidOperationException("Hastanın aktif sahibini seçin.");
        decimal Number(string? s,int digits,decimal max){if(string.IsNullOrWhiteSpace(s))return 0;if(!Regex.IsMatch(s,@"^\d{1,7}([,.]\d{1,"+digits+@"})?$")||!decimal.TryParse(s.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n)||n>max)throw new InvalidOperationException("Sayısal alanları binlik ayırıcı olmadan girin: 1500 veya 1500,50.");return n;}
        decimal? weight=string.IsNullOrWhiteSpace(m.Weight)?null:Number(m.Weight,3,2000);if(weight is <=0)throw new InvalidOperationException("Kilo sıfırdan büyük olmalı.");
        var amount=Number(m.Amount,2,9_999_999);var paid=Number(m.Paid,2,9_999_999);if(paid>amount)throw new InvalidOperationException("Tahsilat toplam ücreti aşamaz.");
        if(paid>0&&!FinancePaymentMethods.Allowed.Contains(m.PaymentMethod))throw new InvalidOperationException("Ödeme yöntemi geçersiz.");
        if(m.VaccineIds.Count>30||m.VaccineIds.Distinct().Count()!=m.VaccineIds.Count)throw new InvalidOperationException("Uygulamaları tekrar etmeden seçin.");
        var types=await db.VaccineTypes.Where(x=>x.ClinicID==Clinic&&x.IsActive&&m.VaccineIds.Contains(x.ID)).AsNoTracking().ToListAsync(ct);if(types.Count!=m.VaccineIds.Count)throw new InvalidOperationException("Uygulama türlerinden biri geçersiz.");
        var preview=new QuickVisitPreview(m,patient.Name??"İsimsiz",(owner.FirstName+" "+owner.LastName).Trim(),owner.Phone??"",types.OrderBy(x=>x.ID).Select(x=>(x.Name??"")+" · "+x.ValidityDays+" gün").ToList(),weight,amount,paid);
        var activeKey=$"visit-active:{Clinic}:{actor}";
        if(cache.TryGetValue<Guid>(activeKey,out var previous)&&previous!=m.SubmissionId)cache.Remove(Key(actor,previous));
        cache.Set(activeKey,m.SubmissionId,TimeSpan.FromMinutes(20));
        cache.Set(Key(actor,m.SubmissionId),preview,TimeSpan.FromMinutes(20));return preview;
    }
    public QuickVisitPreview Get(Guid actor,Guid token)=>cache.Get<QuickVisitPreview>(Key(actor,token))??throw new InvalidOperationException("Ziyaret taslağının süresi doldu. Yeniden önizleyin.");
    public async Task<Guid> CommitAsync(Guid actor,string actorName,Guid token,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({Clinic+":visit:"+token},0))",ct);
        var prior=await db.SystemAuditLogs.AsNoTracking().SingleOrDefaultAsync(x=>x.ClinicId==Clinic&&x.ActorUserId==actor&&x.Action=="QuickVisit.Created"&&x.CorrelationId==token.ToString(),ct);if(prior!=null)return Guid.Parse(prior.EntityId!);
        var expected=Get(actor,token);var current=await PreviewAsync(expected.Model,actor,ct);
        if(expected.Patient!=current.Patient||expected.Owner!=current.Owner||expected.Phone!=current.Phone||!expected.Vaccines.SequenceEqual(current.Vaccines))throw new InvalidOperationException("Hasta, sahip veya uygulama bilgisi değişti. Yeniden önizleyin.");
        var m=current.Model;var exam=new Muayene{ClinicID=Clinic,PatientId=m.PatientId,Subjective=m.Notes,WeightAtVisit=current.Weight,CreatedAt=DateTime.SpecifyKind(m.VisitDate.Date,DateTimeKind.Utc)};db.Muayeneler.Add(exam);await db.SaveChangesAsync(ct);
        if(m.VaccineIds.Count>0){var result=await vaccines.CreateBatchAsync(new(m.PatientId,m.VisitDate,token,m.VaccineIds.Select(x=>new VaccinationBatchItem(x,null)).ToList()),actor,ct);if(!result.Succeeded)throw new InvalidOperationException(result.Error);}
        if(current.Amount>0)
        {
            var person=new FinanceActor(actor,actorName);var account=await finance.CreateAsync(new FinanceAccountInput{SubmissionId=token,CustomerMode="registered",PetOwnerId=m.OwnerId,PatientId=m.PatientId,Description="Ziyaret · "+m.VisitDate.ToString("dd.MM.yyyy"),Amount=current.Amount,ChargeDate=DateOnly.FromDateTime(m.VisitDate),DueDate=m.DueDate},person,ct);
            if(current.Paid>0)await finance.SaveEntryAsync(new FinanceEntryInput{AccountId=account,Revision=1,Kind="receipt",Amount=current.Paid,Date=DateOnly.FromDateTime(m.VisitDate),PaymentMethod=m.PaymentMethod,Notes="Ziyaret ekranında onaylandı."},person,ct);
            exam.ReviewedFinanceAccountId=account;exam.BillingReviewedAt=DateTime.UtcNow;exam.BillingReviewedBy=actor;
        }
        if(!string.IsNullOrWhiteSpace(m.HomeSteps))db.CareCards.Add(new CareCard{ClinicID=Clinic,ExaminationId=exam.ID,PetOwnerId=m.OwnerId,TodayActions=m.Notes,HomeSteps=m.HomeSteps,CallInstructions=m.CallInstructions??"",FollowUpDate=m.FollowUpDate,State="Draft"});
        db.SystemAuditLogs.Add(new SystemAuditLog{ClinicId=Clinic,ActorUserId=actor,Category="QuickVisit",Action="QuickVisit.Created",EntityType="Muayene",EntityId=exam.ID.ToString(),CorrelationId=token.ToString(),Message="Ziyaret son kontrol sonrası birlikte kaydedildi.",MetadataJson=JsonSerializer.Serialize(new{m.PatientId,Vaccinations=m.VaccineIds.Count,Finance=current.Amount>0,CareCard=!string.IsNullOrWhiteSpace(m.HomeSteps)})});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);cache.Remove(Key(actor,token));return exam.ID;
    }
}
