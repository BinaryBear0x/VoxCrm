using VoxCrm.Domain.Common;
namespace VoxCrm.Domain.Entities;
public sealed class ExcelImportBatch : BaseEntity, ITenantEntity
{
    public Guid ClinicID {get;set;}
    public Guid SubmissionId {get;set;}
    public Guid ActorUserId {get;set;}
    public string ManifestJson {get;set;}="";
    public int PatientCount {get;set;}
    public DateTime? UndoneAt {get;set;}
    public Guid? UndoneBy {get;set;}
}
