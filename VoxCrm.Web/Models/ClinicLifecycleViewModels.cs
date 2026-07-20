using System.ComponentModel.DataAnnotations;

namespace VoxCrm.Web.Models;

public sealed class CreateClinicViewModel
{
    [Required(ErrorMessage = "Klinik adı zorunludur.")]
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }

    [EmailAddress(ErrorMessage = "Geçerli bir klinik e-postası girin.")]
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool IsWhatsAppEnabled { get; set; }

    [Required(ErrorMessage = "İlk kullanıcı adı zorunludur.")]
    public string InitialUserFirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "İlk kullanıcı soyadı zorunludur.")]
    public string InitialUserLastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "İlk kullanıcı e-postası zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir kullanıcı e-postası girin.")]
    public string InitialUserEmail { get; set; } = string.Empty;
}

public sealed class EditClinicViewModel
{
    public Guid ClinicId { get; set; }

    [Required(ErrorMessage = "Klinik adı zorunludur.")]
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }

    [EmailAddress(ErrorMessage = "Geçerli bir klinik e-postası girin.")]
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool IsWhatsAppEnabled { get; set; }
    public string? WhatsAppPhoneNumberId { get; set; }
    public bool WhatsAppSendWindowEnabled { get; set; } = true;
    public TimeOnly WhatsAppSendWindowStart { get; set; } = new(9, 0);
    public TimeOnly WhatsAppSendWindowEnd { get; set; } = new(19, 0);
    public string WhatsAppTimeZoneId { get; set; } = "Europe/Istanbul";
}

public sealed class ProvisionedClinicViewModel
{
    public string ClinicName { get; init; } = string.Empty;
    public string UserEmail { get; init; } = string.Empty;
    public string ActivationUrl { get; init; } = string.Empty;
}

public sealed class ActivateClinicUserViewModel
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? Email { get; set; }

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre tekrarı zorunludur.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Şifreler eşleşmiyor.")]
    public string ConfirmPassword { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}
