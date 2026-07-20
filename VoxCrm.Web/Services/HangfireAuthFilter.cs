using Hangfire.Annotations;
using Hangfire.Dashboard;

namespace VoxCrm.Web.Services;

/// <summary>
    /// /hangfire dashboard'una sadece "SystemAdmin" rolündeki kullanıcıların
/// erişmesine izin veren filtre.
///
/// Neden bu gerekli?
/// Hangfire paneli; zamanlanmış jobları (botun WhatsApp kuyruğu vb.)
/// listelemek, silmek ve manuel tetiklemek için kullanılır.
/// Bu panele yetkisiz erişim, sistemin iç işleyişini ifşa eder.
/// </summary>
public class HangfireAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize([NotNull] DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Giriş yapmamışsa → kesinlikle hayır
        if (!(httpContext.User?.Identity?.IsAuthenticated ?? false))
            return false;

        // Hangfire job'ları tüm tenantları etkilediğinden Dealer yetkisi yeterli
        // değildir. Bu yüzey yalnız platform yöneticilerine aittir.
        return httpContext.User.IsInRole("SystemAdmin");
    }
}
