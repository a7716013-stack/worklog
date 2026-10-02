namespace WorkJournal.Web.Security;

public static class GoogleAuthSettings
{
    public const string LoginScheme = "Google";
    public const string CalendarScheme = "GoogleCalendar";
    public const string CalendarScope = "https://www.googleapis.com/auth/calendar.events.owned";
    public const string UserIdItem = "workjournal.user";
    public const string SecurityStampItem = "workjournal.stamp";
    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]) &&
        !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]);

    public static string Origin(IConfiguration configuration)
    {
        var value = configuration["Authentication:Google:PublicOrigin"] ?? "https://localhost:7180";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("Authentication:Google:PublicOrigin must be an HTTPS origin without a path.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
