using WorkJournal.Api;
var app = ApiHost.Build(args);
if (app.Configuration.GetValue<bool>("LocalGuest:Initialize")) await LocalGuestBootstrap.Initialize(app);
await app.RunAsync();
public partial class ApiProgram { }
