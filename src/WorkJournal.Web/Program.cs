using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    var messages = options.ModelBindingMessageProvider;
    messages.SetValueIsInvalidAccessor(value => $"輸入值「{value}」無效。");
    messages.SetAttemptedValueIsInvalidAccessor((value, field) => $"{field} 的值「{value}」無效。");
    messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"輸入值「{value}」無效。");
    messages.SetValueMustNotBeNullAccessor(field => "請填寫此欄位。");
    messages.SetValueMustBeANumberAccessor(field => $"{field} 必須為數字。");
});
builder.Services.AddDbContext<JournalDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("JournalDatabase"),
        sql => sql.EnableRetryOnFailure()));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<WorkJournal.Web.Services.FinMindStockService>(client =>
{
    client.BaseAddress = new Uri("https://api.finmindtrade.com/api/v4/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<WorkJournal.Web.Services.EtfOfficialService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 WorkJournal/1.1.4");
});
var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=WorkLogs}/{action=Index}/{id?}")
    .WithStaticAssets();
app.Run();
