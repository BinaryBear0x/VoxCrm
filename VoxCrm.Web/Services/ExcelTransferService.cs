using ClosedXML.Excel;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Web.Services;

public sealed record TransferRow(int Number, string FirstName, string LastName, string Phone, string Patient, string Species, string Breed, string Birth, string Chip, string Notes);
public sealed record TransferCheck(TransferRow Row, bool Blocked, string Message, Guid? OwnerId);
public sealed record TransferStage(string[] Headers, List<string[]> Rows, List<TransferCheck>? Checks = null);
public sealed record TransferManifest(Dictionary<Guid,string> Patients, Dictionary<Guid,string> Owners, Dictionary<Guid,Guid> Links);
public sealed class ExcelTransferService(VoxCrmDbContext db, ITenantService tenant, IMemoryCache cache)
{
    public static readonly string[] Headers = ["Sahip Adı", "Sahip Soyadı", "Telefon", "Hasta Adı", "Tür", "Irk", "Doğum Tarihi", "Çip No", "Hasta Notu"];
    public static readonly string[][] Aliases = [["sahipadı","ad","firstname","müşteriadı"],["sahipsoyadı","soyad","lastname","sahipsoyadı"],["telefon","phone","gsm","ceptelefonu"],["hastaadı","hayvanadı","petname","name"],["tür","species","cins"],["ırk","breed"],["doğumtarihi","dateofbirth"],["çipno","mikroçip","microchipnumber"],["hastanotu","not","notes"]];
    private Guid Clinic => tenant.GetClinicId() is var id && id != Guid.Empty ? id : throw new InvalidOperationException("Klinik seçimi gerekli.");
    private string Key(Guid actor, Guid token) => $"excel:{Clinic}:{actor}:{token}";
    public TransferStage Get(Guid actor, Guid token) => cache.Get<TransferStage>(Key(actor,token)) ?? throw new InvalidOperationException("Önizleme süresi doldu. Dosyayı yeniden yükleyin.");
    public async Task<Guid> UploadAsync(Stream input, Guid actor, CancellationToken ct)
    {
        using var buffer = new MemoryStream(); await input.CopyToAsync(buffer, ct);
        if(buffer.Length is < 1 or > 5_242_880) throw new InvalidOperationException("Dosya en fazla 5 MB olabilir.");
        buffer.Position=0;
        using(var zip=new ZipArchive(buffer,ZipArchiveMode.Read,true))
        {
            if(zip.Entries.Count > 1500 || zip.Entries.Sum(e=>e.Length)>40_000_000 || zip.Entries.Any(e=>e.FullName.Contains("vbaProject",StringComparison.OrdinalIgnoreCase)||e.FullName.Contains("externalLinks/",StringComparison.OrdinalIgnoreCase)||e.FullName.Contains("embeddings/",StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Makro, harici bağlantı, gömülü dosya veya aşırı büyük içerik kabul edilmiyor.");
        }
        buffer.Position=0; using var workbook=new XLWorkbook(buffer);var sheet=workbook.Worksheets.FirstOrDefault() ?? throw new InvalidOperationException("Çalışma sayfası yok.");
        var lastRow=sheet.LastRowUsed()?.RowNumber()??0;var lastColumn=sheet.LastColumnUsed()?.ColumnNumber()??0;
        if(lastRow is < 2 or > 1001 || lastColumn is < 1 or > 20) throw new InvalidOperationException("İlk sayfada başlık + 1–1000 veri satırı ve en fazla 20 sütun olmalı.");
        var rows=new List<string[]>();
        for(var r=1;r<=lastRow;r++)
        {
            var cells=new string[lastColumn];
            for(var c=1;c<=lastColumn;c++)
            {
                var cell=sheet.Cell(r,c);if(cell.HasFormula)throw new InvalidOperationException($"{r}. satırda formül var. Önce Excel'de değer olarak yapıştırın.");
                cells[c-1]=cell.DataType==XLDataType.DateTime ? cell.GetDateTime().ToString("yyyy-MM-dd") : cell.GetString().Trim();
                if(cells[c-1].Length>1000)throw new InvalidOperationException($"{r}. satırdaki hücre 1000 karakteri aşıyor.");
            }
            rows.Add(cells);
        }
        var headers=rows[0];rows.RemoveAt(0);var token=Guid.NewGuid();
        var actorKey=$"excel-active:{Clinic}:{actor}";if(cache.TryGetValue<Guid>(actorKey,out var prior))cache.Remove(Key(actor,prior));
        cache.Set(actorKey,token,TimeSpan.FromMinutes(30));cache.Set(Key(actor,token),new TransferStage(headers,rows),TimeSpan.FromMinutes(30));return token;
    }
    public async Task<TransferStage> MapAsync(Guid actor,Guid token,int[] map,CancellationToken ct)
    {
        var stage=Get(actor,token);
        if(map.Length!=Headers.Length || map.Any(x=>x < -1 || x>=stage.Headers.Length) || new[]{0,2,3,4}.Any(x=>map[x]<0))throw new InvalidOperationException("Sahip adı, telefon, hasta adı ve tür sütunlarını eşleştirin.");
        var checks=new List<TransferCheck>();
        for(var i=0;i<stage.Rows.Count;i++)
        {
            var raw=stage.Rows[i];if(raw.All(string.IsNullOrWhiteSpace))continue;
            string V(int n)=>map[n]<0?"":raw[map[n]];
            checks.Add(new(new(i+2,V(0),V(1),V(2),V(3),V(4),V(5),V(6),V(7),V(8)),false,"",null));
        }
        var verified=await CheckAsync(checks.Select(x=>x.Row).ToList(),ct);stage=stage with { Checks=verified };cache.Set(Key(actor,token),stage,TimeSpan.FromMinutes(30));return stage;
    }
    private async Task<List<TransferCheck>> CheckAsync(List<TransferRow> rows,CancellationToken ct)
    {
        var owners=await db.PetOwners.Where(x=>x.ClinicID==Clinic).AsNoTracking().ToListAsync(ct);
        var patients=await db.Patients.Where(x=>x.ClinicID==Clinic).Include(x=>x.Owners).AsNoTracking().ToListAsync(ct);
        var today=(await ClinicCalendar.TodayAsync(db,Clinic,ct)).ToDateTime(TimeOnly.MinValue);
        var result=new List<TransferCheck>();var chips=new HashSet<string>();var keys=new HashSet<string>();
        foreach(var r in rows)
        {
            var phone=Phone(r.Phone);var name=Norm(r.FirstName+" "+r.LastName);string? error=null;Guid? ownerId=null;var message="Yeni sahip ve hasta";
            if(string.IsNullOrWhiteSpace(r.FirstName)||string.IsNullOrWhiteSpace(r.Patient)||string.IsNullOrWhiteSpace(r.Species)||phone.Length is < 10 or > 15 || r.Phone.Length>40 || !System.Text.RegularExpressions.Regex.IsMatch(r.Phone,@"^[+0-9 ().-]+$"))error="Ad, hasta adı, tür ve geçerli telefon gerekli.";
            if(new[]{r.FirstName,r.LastName,r.Patient,r.Species,r.Breed}.Any(x=>x.Length>100)||r.Chip.Length>50)error="Ad/tür/ırk en fazla 100, çip en fazla 50 karakter olabilir.";
            if(r.Birth!="" && (!DateTime.TryParseExact(r.Birth,["yyyy-MM-dd","dd.MM.yyyy","d.M.yyyy","dd/MM/yyyy"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var birth)||birth>today||birth.Year<1900))error="Doğum tarihi belirsiz/geçersiz. yyyy-AA-gg veya gg.AA.yyyy kullanın.";
            var matches=owners.Where(x=>Phone(x.Phone??"")==phone).ToList();
            if(matches.Count==1 && matches[0].IsActive && Norm(matches[0].FirstName+" "+matches[0].LastName)==name){ownerId=matches[0].ID;message="Aynı ad ve telefon: mevcut sahip kullanılacak; ayrıca onaylayın.";}
            else if(matches.Count>0)error="Telefon mevcut başka/çoklu/pasif kayıtla eşleşiyor. Önce sahip kaydını kontrol edin.";
            else if(owners.Any(x=>Norm(x.FirstName+" "+x.LastName)==name))message="Benzer isim, farklı telefon: ayrı sahip oluşturulacak; kontrol edin.";
            if(r.Chip!="" && (!chips.Add(Norm(r.Chip))||patients.Any(x=>Norm(x.MicrochipNumber??"")==Norm(r.Chip))))error="Çip numarası mevcut veya dosyada tekrarlı; aktarılmayacak.";
            if(!keys.Add(phone+":"+Norm(r.Patient)) || (ownerId.HasValue && patients.Any(x=>Norm(x.Name??"")==Norm(r.Patient)&&x.Owners.Any(o=>o.PetOwnerId==ownerId))))error="Aynı sahip/hasta zaten mevcut veya dosyada tekrarlı; aktarılmayacak.";
            if(rows.Any(other=>other.Number!=r.Number&&Phone(other.Phone)==phone&&Norm(other.FirstName+" "+other.LastName)!=name))error="Dosyada aynı telefon farklı sahip adlarıyla kullanılmış. Kontrol edin.";
            result.Add(new(r,error!=null,error??message,ownerId));
        }
        return result;
    }
    public async Task<int> ConfirmAsync(Guid actor,Guid token,int[] selected,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(ct);
        var previous=await db.ExcelImportBatches.AnyAsync(x=>x.ClinicID==Clinic&&x.SubmissionId==token&&x.ActorUserId==actor,ct);if(previous)return 0;
        var stage=Get(actor,token);if(stage.Checks==null)throw new InvalidOperationException("Önce sütunları eşleştirip önizleyin.");
        if(selected.Length==0||selected.Distinct().Count()!=selected.Length||selected.Any(n=>!stage.Checks.Any(x=>x.Row.Number==n&&!x.Blocked)))throw new InvalidOperationException("Aktarılabilecek satırları seçin.");
        var checks=await CheckAsync(stage.Checks.Select(x=>x.Row).ToList(),ct);var chosen=checks.Where(x=>selected.Contains(x.Row.Number)).ToList();
        if(chosen.Any(x=>x.Blocked||x.OwnerId!=stage.Checks.Single(s=>s.Row.Number==x.Row.Number).OwnerId))throw new InvalidOperationException("Kayıtlar önizlemeden sonra değişti. Önizlemeyi yenileyin.");
        var createdOwners=new Dictionary<string,PetOwner>();var patients=new List<Patient>();var links=new Dictionary<Guid,Guid>();
        foreach(var item in chosen)
        {
            var r=item.Row;Guid owner;
            if(item.OwnerId.HasValue)owner=item.OwnerId.Value;
            else {var key=Phone(r.Phone)+":"+Norm(r.FirstName+" "+r.LastName);if(!createdOwners.TryGetValue(key,out var o)){o=new PetOwner{ClinicID=Clinic,FirstName=r.FirstName,LastName=r.LastName,Phone=r.Phone,WhatsAppConsent=false,SubmissionId=Guid.NewGuid()};createdOwners.Add(key,o);db.PetOwners.Add(o);}owner=o.ID;}
            var p=new Patient{ClinicID=Clinic,Name=r.Patient,Species=r.Species,Breed=r.Breed,MicrochipNumber=r.Chip,Notes=r.Notes,SubmissionId=Guid.NewGuid(),DateOfBirth=r.Birth==""?null:DateTime.SpecifyKind(DateTime.ParseExact(r.Birth,["yyyy-MM-dd","dd.MM.yyyy","d.M.yyyy","dd/MM/yyyy"],CultureInfo.InvariantCulture,DateTimeStyles.None),DateTimeKind.Utc)};
            db.Patients.Add(p);db.PatientOwners.Add(new PatientOwner{ClinicID=Clinic,PatientId=p.ID,PetOwnerId=owner});patients.Add(p);links.Add(p.ID,owner);
        }
        var manifest=new TransferManifest(patients.ToDictionary(x=>x.ID,Fingerprint),createdOwners.Values.ToDictionary(x=>x.ID,Fingerprint),links);
        db.ExcelImportBatches.Add(new ExcelImportBatch{ClinicID=Clinic,SubmissionId=token,ActorUserId=actor,ManifestJson=JsonSerializer.Serialize(manifest),PatientCount=patients.Count});
        db.SystemAuditLogs.Add(new SystemAuditLog{ClinicId=Clinic,ActorUserId=actor,Category="Excel",Action="Excel.Imported",EntityType="ExcelBatch",EntityId=token.ToString(),Message=$"{patients.Count} hasta Excel'den aktarıldı.",MetadataJson=JsonSerializer.Serialize(manifest)});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);cache.Remove(Key(actor,token));return patients.Count;
    }
    public async Task UndoAsync(Guid actor,Guid token,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(ct);
        var batch=await db.ExcelImportBatches.SingleOrDefaultAsync(x=>x.ClinicID==Clinic&&x.SubmissionId==token,ct)??throw new InvalidOperationException("Aktarım bulunamadı.");
        if(batch.UndoneAt.HasValue)throw new InvalidOperationException("Bu aktarım zaten geri alındı.");
        var manifest=JsonSerializer.Deserialize<TransferManifest>(batch.ManifestJson)!;if(manifest.Links==null)throw new InvalidOperationException("Bu eski aktarımın sahiplik metadatası eksik; kayıtları tek tek kontrol edin.");var ids=manifest.Patients.Keys.ToList();var owners=manifest.Owners.Keys.ToList();
        var patients=await db.Patients.Where(x=>x.ClinicID==Clinic&&ids.Contains(x.ID)).ToListAsync(ct);var people=await db.PetOwners.Where(x=>x.ClinicID==Clinic&&owners.Contains(x.ID)).ToListAsync(ct);
        var currentLinks=await db.PatientOwners.Where(x=>x.ClinicID==Clinic&&ids.Contains(x.PatientId)&&x.IsActive).AsNoTracking().ToListAsync(ct);
        if(currentLinks.Count!=ids.Count||currentLinks.Any(x=>!manifest.Links.TryGetValue(x.PatientId,out var owner)||owner!=x.PetOwnerId))throw new InvalidOperationException("Hasta sahipliği değiştirildi; aktarım otomatik geri alınamaz.");
        var shares=await db.OwnerFollowUpShares.Where(x=>x.ClinicID==Clinic).AsNoTracking().ToListAsync(ct);
        if(shares.Any(x=>JsonSerializer.Deserialize<List<Guid>>(x.PatientIdsJson)!.Any(ids.Contains)))throw new InvalidOperationException("Aktarılan hasta için takip bağlantısı oluşturuldu; otomatik geri alma durduruldu.");
        if(patients.Count!=ids.Count||people.Count!=owners.Count||patients.Any(x=>!x.IsActive||Fingerprint(x)!=manifest.Patients[x.ID])||people.Any(x=>!x.IsActive||Fingerprint(x)!=manifest.Owners[x.ID]))throw new InvalidOperationException("Aktarılan kayıtlar değiştirildi. Güvenli geri alma için tek tek kontrol edin.");
        if(await db.Muayeneler.AnyAsync(x=>x.ClinicID==Clinic&&ids.Contains(x.PatientId),ct)||await db.VaccinationRecords.AnyAsync(x=>x.ClinicID==Clinic&&ids.Contains(x.PatientId),ct)||await db.Appointments.AnyAsync(x=>x.ClinicID==Clinic&&x.PatientId.HasValue&&ids.Contains(x.PatientId.Value),ct)||await db.FinanceAccounts.AnyAsync(x=>x.ClinicID==Clinic&&((x.PatientId.HasValue&&ids.Contains(x.PatientId.Value))||(x.PetOwnerId.HasValue&&owners.Contains(x.PetOwnerId.Value))),ct)||await db.ClinicTasks.AnyAsync(x=>x.ClinicID==Clinic&&x.PatientId.HasValue&&ids.Contains(x.PatientId.Value),ct)||await db.OwnerFollowUpShares.AnyAsync(x=>x.ClinicID==Clinic&&owners.Contains(x.PetOwnerId),ct)||await db.CareCards.AnyAsync(x=>x.ClinicID==Clinic&&owners.Contains(x.PetOwnerId),ct)||await db.Borçlar.AnyAsync(x=>x.ClinicID==Clinic&&owners.Contains(x.PetOwnerId),ct)||await db.PatientOwners.AnyAsync(x=>x.ClinicID==Clinic&&owners.Contains(x.PetOwnerId)&&!ids.Contains(x.PatientId)&&x.IsActive,ct))throw new InvalidOperationException("Aktarım sonrasında işlem veya yeni bağlantı var. Veri kaybını önlemek için geri alma durduruldu.");
        foreach(var item in patients.Cast<BaseEntity>().Concat(people)){item.IsActive=false;item.ArchivedAt=DateTime.UtcNow;item.ArchivedByUserId=actor;}
        foreach(var link in await db.PatientOwners.Where(x=>x.ClinicID==Clinic&&ids.Contains(x.PatientId)).ToListAsync(ct)){link.IsActive=false;link.ArchivedAt=DateTime.UtcNow;link.ArchivedByUserId=actor;}
        batch.UndoneAt=DateTime.UtcNow;batch.UndoneBy=actor;
        db.SystemAuditLogs.Add(new SystemAuditLog{ClinicId=Clinic,ActorUserId=actor,Category="Excel",Action="Excel.Undone",EntityType="ExcelBatch",EntityId=token.ToString(),Message="Excel aktarımı arşivlenerek geri alındı."});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private Task Lock(CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({Clinic+":excel-import"},0))",ct);
    public async Task<List<SystemAuditLog>> HistoryAsync(CancellationToken ct)
    {
        var batches=await db.ExcelImportBatches.AsNoTracking().Where(x=>x.ClinicID==Clinic).OrderByDescending(x=>x.CreatedAt).Take(30).ToListAsync(ct);
        return batches.SelectMany(x=>{
            var list=new List<SystemAuditLog>{new(){ClinicId=Clinic,ActorUserId=x.ActorUserId,Action="Excel.Imported",EntityId=x.SubmissionId.ToString(),CreatedAt=x.CreatedAt,Message=$"{x.PatientCount} hasta Excel'den aktarıldı."}};
            if(x.UndoneAt.HasValue)list.Add(new(){ClinicId=Clinic,ActorUserId=x.UndoneBy,Action="Excel.Undone",EntityId=x.SubmissionId.ToString(),CreatedAt=x.UndoneAt.Value,Message="Excel aktarımı geri alındı."});return list;
        }).OrderByDescending(x=>x.CreatedAt).ToList();
    }
    public async Task<byte[]> ExportAsync(bool template,CancellationToken ct)
    {
        var rows=new List<string[]>();
        if(!template)
        {
            var patients=await db.Patients.Where(x=>x.ClinicID==Clinic&&x.IsActive).Include(x=>x.Owners.Where(o=>o.IsActive)).ThenInclude(x=>x.PetOwner).AsNoTracking().OrderBy(x=>x.Name).ToListAsync(ct);
            foreach(var p in patients)foreach(var owner in p.Owners.Where(x=>x.PetOwner.IsActive&&x.PetOwner.ClinicID==Clinic).Select(x=>x.PetOwner).DefaultIfEmpty())rows.Add([owner?.FirstName??"",owner?.LastName??"",owner?.Phone??"",p.Name??"",p.Species??"",p.Breed??"",p.DateOfBirth?.ToString("yyyy-MM-dd")??"",p.MicrochipNumber??"",p.Notes??""]);
        }
        using var workbook=new XLWorkbook();var sheet=workbook.AddWorksheet("Hasta ve Sahipler");for(var c=0;c<Headers.Length;c++){sheet.Cell(1,c+1).Value=Headers[c];sheet.Column(c+1).Width=c==8?50:24;sheet.Column(c+1).Style.NumberFormat.Format="@";}
        for(var r=0;r<rows.Count;r++)for(var c=0;c<Headers.Length;c++)sheet.Cell(r+2,c+1).Value=rows[r][c];
        sheet.Row(1).Style.Fill.BackgroundColor=XLColor.FromHtml("#176B57");sheet.Row(1).Style.Font.FontColor=XLColor.White;sheet.Row(1).Style.Font.Bold=true;sheet.SheetView.FreezeRows(1);sheet.Range(1,1,Math.Max(1,rows.Count+1),Headers.Length).SetAutoFilter();
        using var output=new MemoryStream();workbook.SaveAs(output);return output.ToArray();
    }
    public static string Norm(string value)=>string.Join("",value.ToLower(new CultureInfo("tr-TR")).Where(char.IsLetterOrDigit));
    public static string Phone(string value){var digits=new string(value.Where(char.IsDigit).ToArray());return digits.Length==11&&digits.StartsWith('0')?"90"+digits[1..]:digits.Length==10?"90"+digits:digits;}
    private static string Fingerprint(Patient p)=>Hash(new object?[]{p.Name,p.Species,p.Breed,p.DateOfBirth?.ToString("yyyy-MM-dd"),p.MicrochipNumber,p.Notes,p.pasaportNumarasi,p.cinsiyet});
    private static string Fingerprint(PetOwner p)=>Hash(new object?[]{p.FirstName,p.LastName,p.Phone,p.Email,p.Notes,p.Address,p.WhatsAppConsent,p.NationalIdentityNumber});
    private static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
