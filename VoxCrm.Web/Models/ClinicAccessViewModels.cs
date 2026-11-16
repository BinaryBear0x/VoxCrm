using System.ComponentModel.DataAnnotations;
using VoxCrm.Application.Clinics;

namespace VoxCrm.Web.Models;

public sealed class ClinicUserAccessViewModel
{
    public required ClinicAccessPage Clinic { get; init; }
    public required ClinicAccessUser User { get; init; }
}
public sealed class ClinicUserActionInput
{
    public ClinicAccessOperation Operation { get; set; }
    [Required, DataType(DataType.Password), StringLength(128)] public string DealerPassword { get; set; } = "";
    [Required, StringLength(500)] public string Reason { get; set; } = "";
    [StringLength(100)] public string? FirstName { get; set; }
    [StringLength(100)] public string? LastName { get; set; }
    [StringLength(254)] public string? Email { get; set; }
    [DataType(DataType.Password), StringLength(128)] public string? NewPassword { get; set; }
}
public sealed class CreateClinicUserInput
{
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(128)] public string DealerPassword { get; set; } = "";
    [Required, StringLength(500)] public string Reason { get; set; } = "";
}
public sealed record ClinicUserActivationViewModel(Guid ClinicId, string Email, string ActivationUrl);
