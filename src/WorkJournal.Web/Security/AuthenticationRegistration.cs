using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
namespace WorkJournal.Web.Security;

public static class AuthenticationRegistration
{
    public static IServiceCollection AddJournalAuthentication(this IServiceCollection services, IConfiguration config)
    {
        services.AddDataProtection().SetApplicationName("WorkJournal");
        // Provider failures can contain raw response bodies. Log only our sanitized failure event.
        services.AddLogging(logging => logging.AddFilter("Microsoft.AspNetCore.Authentication.Google", LogLevel.None));
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.SignIn.RequireConfirmedEmail = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
        }).AddEntityFrameworkStores<JournalDbContext>().AddDefaultTokenProviders()
          .AddClaimsPrincipalFactory<ApplicationClaimsFactory>();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-WorkJournal";
            options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.LoginPath = "/Account/Login"; options.AccessDeniedPath = "/Account/AccessDenied";
            options.ExpireTimeSpan = TimeSpan.FromHours(8); options.SlidingExpiration = true;
        });
        services.ConfigureExternalCookie(options =>
        {
            options.Cookie.Name = "__Host-WorkJournal.External";
            options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax; options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        });
        services.AddAntiforgery(options =>
        {
            options.Cookie.HttpOnly = true;
            // Public stock tools continue to work on the existing HTTP development profile.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddAuthorization();
        services.AddHttpClient("GoogleCalendarApi", client => client.Timeout = TimeSpan.FromSeconds(15))
            .RemoveAllLoggers();
        services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
        services.AddScoped<ICalendarSyncGateway, GoogleCalendarService>();
        services.AddScoped<CalendarSyncService>();
        services.AddScoped<CalendarAggregationService>();
        if (GoogleAuthSettings.IsConfigured(config))
        {
            services.AddAuthentication()
                .AddGoogle(GoogleAuthSettings.LoginScheme, options => ConfigureGoogle(options, config, false))
                .AddGoogle(GoogleAuthSettings.CalendarScheme, options => ConfigureGoogle(options, config, true));
        }
        return services;
    }

    private static void ConfigureGoogle(GoogleOptions options, IConfiguration config, bool calendar)
    {
        options.ClientId = config["Authentication:Google:ClientId"]!;
        options.ClientSecret = config["Authentication:Google:ClientSecret"]!;
        options.CallbackPath = calendar ? "/GoogleCalendar/OAuthCallback" : "/signin-google";
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.SaveTokens = false; options.UsePkce = true;
        options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(10);
        options.CorrelationCookie.HttpOnly = true; options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Scope.Clear();
        foreach (var scope in new[] { "openid", "profile", "email" }) options.Scope.Add(scope);
        if (calendar) options.Scope.Add(GoogleAuthSettings.CalendarScope);
        options.ClaimActions.MapJsonKey("picture", "picture");
        options.ClaimActions.MapJsonKey("email_verified", "email_verified", ClaimValueTypes.Boolean);
        options.Events = new OAuthEvents
        {
            OnRedirectToAuthorizationEndpoint = context =>
            {
                // Pin host and scheme; never construct the registered callback from an untrusted Host header.
                var expected = GoogleAuthSettings.Origin(config);
                var actual = $"{context.Request.Scheme}://{context.Request.Host}";
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) || context.Request.PathBase.HasValue)
                    throw new InvalidOperationException("Open the configured HTTPS PublicOrigin before starting Google authorization.");
                var url = context.RedirectUri;
                if (calendar) url = QueryHelpers.AddQueryString(url, new Dictionary<string,string?>
                { ["access_type"] = "offline", ["prompt"] = "consent", ["include_granted_scopes"] = "true" });
                context.Response.Redirect(url);
                return Task.CompletedTask;
            },
            OnCreatingTicket = async context =>
            {
                // The provider's default v3 userinfo endpoint returns email_verified and sub.
                if (!context.User.TryGetProperty("email_verified", out var verified) || verified.ValueKind != System.Text.Json.JsonValueKind.True)
                    throw new CalendarServiceException("Google 電子郵件尚未驗證，無法完成登入。");
                if (!calendar) return;
                var application = await context.HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
                var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var signedIn = application.Succeeded ? await users.GetUserAsync(application.Principal!) : null;
                if (signedIn is null || !context.Properties.Items.TryGetValue(GoogleAuthSettings.UserIdItem, out var userId) ||
                    signedIn.Id != userId || !context.Properties.Items.TryGetValue(GoogleAuthSettings.SecurityStampItem, out var stamp) ||
                    stamp != signedIn.SecurityStamp || application.Principal!.FindFirstValue("AspNet.Identity.SecurityStamp") != signedIn.SecurityStamp)
                    throw new CalendarServiceException("登入狀態已變更，請重新登入後連結行事曆。");
                var service = context.HttpContext.RequestServices.GetRequiredService<IGoogleCalendarService>();
                await service.SaveAuthorizationAsync(signedIn.Id, context.User.GetProperty("sub").GetString()!,
                    context.User.GetProperty("email").GetString()!, context.TokenResponse.Response!.RootElement, context.HttpContext.RequestAborted);
            },
            OnTicketReceived = context =>
            {
                if (calendar)
                {
                    // Calendar consent must not replace the website identity or issue an external token cookie.
                    context.HandleResponse(); context.Response.Redirect("/GoogleCalendar");
                }
                return Task.CompletedTask;
            },
            OnRemoteFailure = context =>
            {
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("WorkJournal.Authentication")
                    .LogWarning("Google authorization failed for {Flow}.", calendar ? "Calendar" : "Login");
                // Never display/log provider payloads, authorization codes or tokens.
                context.HandleResponse();
                context.Response.Redirect(calendar ? "/GoogleCalendar?authorizationFailed=true" : "/Account/Login?failed=true");
                return Task.CompletedTask;
            }
        };
    }
}
