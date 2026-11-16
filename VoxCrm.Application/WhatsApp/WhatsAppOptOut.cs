namespace VoxCrm.Application.WhatsApp;

public sealed record WhatsAppOptOutResult(int OwnersUpdated, int NotificationsCancelled);

/// <summary>
/// Recognizes a customer's request to stop receiving WhatsApp messages and turns the
/// sender address of an inbound message into the phone formats clinics store.
/// </summary>
public static class WhatsAppOptOut
{
    // Whole-message matches only. "durum nasıl" or "randevuyu iptal etmek istiyorum"
    // must never switch a customer's consent off.
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "DUR", "STOP", "DURDUR", "UNSUBSCRIBE",
        "ABONELIKTENCIK", "LISTEDENCIK", "MESAJISTEMIYORUM", "MESAJGONDERME"
    };

    public static bool IsOptOutMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var folded = new List<char>(text.Length);
        foreach (var raw in text)
        {
            if (!char.IsLetter(raw)) continue;
            folded.Add(raw switch
            {
                'ı' or 'İ' or 'I' or 'i' => 'I',
                'ş' or 'Ş' => 'S',
                'ç' or 'Ç' => 'C',
                'ğ' or 'Ğ' => 'G',
                'ü' or 'Ü' => 'U',
                'ö' or 'Ö' => 'O',
                _ => char.ToUpperInvariant(raw)
            });
        }
        return Keywords.Contains(new string(folded.ToArray()));
    }

    /// <summary>
    /// Digits of the sender's phone number, or null when the address is not a phone
    /// number (WhatsApp "@lid" identifiers cannot be mapped to a customer).
    /// </summary>
    public static string? PhoneDigits(string? fromPhone)
    {
        if (string.IsNullOrWhiteSpace(fromPhone)) return null;
        var address = fromPhone.Trim();
        var at = address.IndexOf('@');
        if (at >= 0)
        {
            if (!address[(at + 1)..].StartsWith("s.whatsapp.net", StringComparison.OrdinalIgnoreCase)) return null;
            address = address[..at];
        }
        var device = address.IndexOf(':');
        if (device >= 0) address = address[..device];
        var digits = new string(address.Where(char.IsDigit).ToArray());
        return digits.Length >= 10 ? digits : null;
    }

    /// <summary>
    /// Clinics store phones as typed ("+90555…", "0555…", "555…"), and the lookup key is the
    /// digits of that text, so every equivalent spelling of the number has to be tried.
    /// </summary>
    public static IReadOnlyList<string> PhoneLookupCandidates(string digits)
    {
        // "0090 500..." is the international dialing prefix written out: same number as "+90 500...".
        var international = digits.StartsWith("00", StringComparison.Ordinal) ? digits[2..] : digits;
        var national = international switch
        {
            { Length: 12 } when international.StartsWith("90", StringComparison.Ordinal) => international[2..],
            { Length: 11 } when international.StartsWith('0') => international[1..],
            { Length: 10 } => international,
            _ => null
        };
        if (national == null) return new[] { digits, international, "00" + international }.Distinct().ToArray();
        return new[] { digits, "90" + national, "0" + national, national, "0090" + national }.Distinct().ToArray();
    }
}
