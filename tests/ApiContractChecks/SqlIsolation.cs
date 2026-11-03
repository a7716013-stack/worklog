using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkJournal.Api;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
public static class SqlIsolation
{
    public static async Task Run(Action<bool,string> check) {
        var database="WorkJournalPaperChecks_"+Guid.NewGuid().ToString("N");
        var connection=TestDatabase.Connection("WorkJournalPaperChecks_",database);
        await using var setup=new JournalDbContext(new DbContextOptionsBuilder<JournalDbContext>().UseSqlServer(connection).Options);
        try {
            await setup.Database.MigrateAsync();
            setup.Users.AddRange(new ApplicationUser {Id="alice",UserName="alice"},new ApplicationUser {Id="bob",UserName="bob"});await setup.SaveChangesAsync();
            var alice=new PaperTradingAccount {ApplicationUserId="alice",Name="Alice",Cash=111m,InitialCash=111m};
            var bob=new PaperTradingAccount {ApplicationUserId="bob",Name="Bob",Cash=222m,InitialCash=222m};
            setup.PaperTradingAccounts.AddRange(alice,bob);await setup.SaveChangesAsync();
            setup.PaperOrders.AddRange(new PaperOrder {AccountId=alice.Id,StockId="ALICE",Quantity=1,ClientRequestId=Guid.NewGuid()},new PaperOrder {AccountId=bob.Id,StockId="BOB",Quantity=1,ClientRequestId=Guid.NewGuid()});await setup.SaveChangesAsync();
            await using var app=ApiHost.Build(["--environment","Development","--ConnectionStrings:JournalDatabase",connection,"--StrategyCenter:Enabled","false","--RadarTracking:SchedulerEnabled","false","--InvestmentInsights:SchedulerEnabled","false"],b=> {
                b.WebHost.UseTestServer();b.Logging.ClearProviders();
                b.Services.AddAuthentication(o=>{o.DefaultAuthenticateScheme="test";o.DefaultChallengeScheme="test";}).AddScheme<AuthenticationSchemeOptions,TestAuth>("test",_=>{});
            });
            await app.StartAsync();using var http=app.GetTestClient();
            foreach(var (user,cash,stock) in new[]{("alice",111m,"ALICE"),("bob",222m,"BOB")}) {
                http.DefaultRequestHeaders.Remove("X-Test-User");http.DefaultRequestHeaders.Add("X-Test-User",user);
                var account=JsonNode.Parse(await http.GetStringAsync("/api/v1/paper/account?userId="+(user=="alice"?"bob":"alice")))!;
                check(account["cash"]!.GetValue<decimal>()==cash,"SQL authenticated account isolation "+user);
                var orders=JsonNode.Parse(await http.GetStringAsync("/api/v1/paper/orders?userId="+(user=="alice"?"bob":"alice")))!;
                check(orders["items"]!.AsArray().Count==1&&orders["items"]![0]!["instrument"]!.GetValue<string>()==stock,"SQL order isolation "+user);
                check(!orders.ToJsonString().Contains("accountId")&&!account.ToJsonString().Contains("applicationUserId"),"private entity fields omitted");
                check(JsonNode.Parse(await http.GetStringAsync("/api/v1/paper/positions"))!["totalCount"]!.GetValue<int>()==0,"empty portfolio isolated");
                check(JsonNode.Parse(await http.GetStringAsync("/api/v1/paper/trades"))!["totalCount"]!.GetValue<int>()==0,"empty trades isolated");
            }
            check(JsonNode.Parse(await http.GetStringAsync("/api/v1/system/readiness"))!["databaseReachable"]!.GetValue<bool>(),"real SQL readiness");
            await app.StopAsync();
        } finally {await TestDatabase.DeleteOwnedAsync(setup,"WorkJournalPaperChecks_",database);}
    }
}
