using System.ComponentModel.DataAnnotations;
namespace WorkJournal.Web.Models;

public class GoogleCalendarConnection
{
    public int Id { get; set; }
    [MaxLength(450)] public string ApplicationUserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    [MaxLength(255)] public string GoogleAccountId { get; set; } = "";
    [MaxLength(320)] public string GoogleEmail { get; set; } = "";
    [MaxLength(255)] public string CalendarId { get; set; } = "primary";
    public string? EncryptedRefreshToken { get; set; }
    public string? EncryptedAccessToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    [MaxLength(2000)] public string GrantedScopes { get; set; } = "";
    public bool IsConnected { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
