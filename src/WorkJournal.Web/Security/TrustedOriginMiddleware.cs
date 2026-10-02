using System.Security.Cryptography;
using System.Text;
namespace WorkJournal.Web.Security;

public class TrustedOriginMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // The Worker overwrites this header. Trust only the configured shared secret,
        // and use a fixed origin from server configuration, never an arbitrary forwarded host.
        var expected = configuration["Authentication:TrustedProxyKey"];
        var supplied = context.Request.Headers["X-WorkJournal-Proxy-Key"].ToString();
        if (!string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(supplied) &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
        {
            var origin = new Uri(GoogleAuthSettings.Origin(configuration));
            context.Request.Scheme = origin.Scheme;
            context.Request.Host = HostString.FromUriComponent(origin);
        }
        context.Request.Headers.Remove("X-WorkJournal-Proxy-Key");
        // OAuth codes must be redeemed using the same registered HTTPS origin that started the flow.
        if (GoogleAuthSettings.IsConfigured(configuration) &&
            (context.Request.Path.Equals("/signin-google", StringComparison.OrdinalIgnoreCase) ||
             context.Request.Path.Equals("/GoogleCalendar/OAuthCallback", StringComparison.OrdinalIgnoreCase)) &&
            !string.Equals($"{context.Request.Scheme}://{context.Request.Host}", GoogleAuthSettings.Origin(configuration), StringComparison.OrdinalIgnoreCase))
        { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        await next(context);
    }
}
