using System.ComponentModel.DataAnnotations;
using VoxCrm.Domain.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace VoxCrm.Web.Models;

public sealed class VaccinationBatchViewModel
{
    public Guid PatientId { get; set; }
    public DateTime AdministeredDate { get; set; }
    public Guid SubmissionId { get; set; } = Guid.NewGuid();
    public List<VaccinationBatchRow> Rows { get; set; } = new();
    [BindNever, ValidateNever] public IReadOnlyList<Patient> Patients { get; set; } = Array.Empty<Patient>();
}
public sealed class VaccinationBatchRow
{
    public bool Selected { get; set; }
    public Guid VaccineTypeId { get; set; }
    [StringLength(200, ErrorMessage = "Her satırın aşı kodu en fazla 200 karakter olabilir.")] public string? VaccineCode { get; set; }
    [BindNever, ValidateNever] public string Name { get; set; } = "";
    [BindNever, ValidateNever] public int ValidityDays { get; set; }
}
