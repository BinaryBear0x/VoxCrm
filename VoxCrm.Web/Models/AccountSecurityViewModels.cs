using System.ComponentModel.DataAnnotations;

namespace VoxCrm.Web.Models;

public sealed class TwoFactorLoginViewModel
{
    [Required, StringLength(7, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}

public sealed class MfaSetupViewModel
{
    public string SharedKey { get; set; } = string.Empty;
    public string AuthenticatorUri { get; set; } = string.Empty;
    [Required, StringLength(7, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;
    public IReadOnlyList<string> RecoveryCodes { get; set; } = Array.Empty<string>();
    public bool IsCompleted { get; set; }
}

public sealed class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), MinLength(12)]
    public string NewPassword { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class RecoveryCodeLoginViewModel
{
    [Required]
    public string RecoveryCode { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}

public sealed class ResetMfaViewModel
{
    [Required, DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;
}
