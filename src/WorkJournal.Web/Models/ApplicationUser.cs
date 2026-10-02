using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
namespace WorkJournal.Web.Models;

public class ApplicationUser : IdentityUser
{
    [MaxLength(200)] public string? DisplayName { get; set; }
    [MaxLength(2048)] public string? PictureUrl { get; set; }
    public ICollection<WorkLog> WorkLogs { get; set; } = new List<WorkLog>();
    public GoogleCalendarConnection? GoogleCalendarConnection { get; set; }
}
