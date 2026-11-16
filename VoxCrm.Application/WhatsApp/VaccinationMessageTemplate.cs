using System.Text.RegularExpressions;

namespace VoxCrm.Application.WhatsApp;

public sealed record VaccinationMessageValues(
    string? OwnerFirstName,
    string? OwnerLastName,
    string? PatientName,
    string? VaccineName,
    DateTime DueDate,
    string? ClinicName);

/// <summary>
/// Renders the vaccination reminder text. Used by the daily scheduler and by the manual
/// preview so both produce the same message from the same template.
/// </summary>
public static class VaccinationMessageTemplate
{
    public const string DefaultBody =
        "Merhaba {ownerFirstName} {ownerLastName}, {patientName} için {vaccineName} zamanı: {dueDate}. Randevu almak isterseniz Veteriner Hekim Ebru Hanım ile iletişime geçebilirsiniz. {clinicName}";

    public const string LegacyBody =
        "Sayin {ownerFirstName}, {patientName} icin {vaccineName} hatirlatmasi: sonraki tarih {dueDate}. {clinicName}";

    private static readonly Regex Placeholder = new(@"\{(\w+)\}", RegexOptions.Compiled);
    private static readonly Regex RepeatedBlanks = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex BlankBeforePunctuation = new(@" +([,.;])", RegexOptions.Compiled);

    public static readonly IReadOnlyList<string> Placeholders =
        new[] { "ownerFirstName", "ownerLastName", "patientName", "vaccineName", "dueDate", "clinicName" };

    /// <summary>Placeholders written in the text that the renderer does not know (typos).</summary>
    public static IReadOnlyList<string> UnknownPlaceholders(string? body) =>
        string.IsNullOrEmpty(body)
            ? Array.Empty<string>()
            : Placeholder.Matches(body)
                .Select(match => match.Groups[1].Value)
                .Where(name => !Placeholders.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    public static bool IsDefault(string? body) =>
        string.IsNullOrWhiteSpace(body) || body.Trim() == LegacyBody;

    public static string Render(string? templateBody, VaccinationMessageValues values)
    {
        var body = IsDefault(templateBody) ? DefaultBody : templateBody!;
        var firstName = string.IsNullOrWhiteSpace(values.OwnerFirstName) ? "Değerli hayvan sahibi" : values.OwnerFirstName;

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ownerFirstName"] = firstName,
            ["ownerLastName"] = values.OwnerLastName ?? string.Empty,
            ["patientName"] = values.PatientName ?? string.Empty,
            ["vaccineName"] = values.VaccineName ?? string.Empty,
            ["dueDate"] = values.DueDate.ToString("dd.MM.yyyy"),
            ["clinicName"] = values.ClinicName ?? string.Empty
        };

        // One pass over the template: a value that itself contains "{patientName}" (a pet or
        // owner name typed with braces) stays literal text instead of being expanded again.
        // Unknown placeholders are left as written so a typo is visible in the preview.
        var text = Placeholder.Replace(body, match =>
            replacements.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

        // An empty value (for example no last name) must not leave "Ali , Pamuk" behind.
        text = RepeatedBlanks.Replace(text, " ");
        text = BlankBeforePunctuation.Replace(text, "$1");
        return text.Trim();
    }
}
