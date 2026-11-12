using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace WorkJournal.Web.Security;

[AttributeUsage(AttributeTargets.Method|AttributeTargets.Class)]
public sealed class FeatureAccessAttribute(string policy,bool json=false):AuthorizeAttribute(policy)
{
    public bool JsonResponse {get;}=json;
}
// Public quote contract is preserved. Only the optional analysis operation requires membership.
public sealed class AnalysisAccessAttribute:Attribute,IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if(!bool.TryParse(context.HttpContext.Request.Query["analyze"].FirstOrDefault(),out var analyze)||!analyze)return;
        var auth=context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        if((await auth.AuthorizeAsync(context.HttpContext.User,null,FeaturePolicies.Swing)).Succeeded)return;
        var status=context.HttpContext.User.Identity?.IsAuthenticated==true?403:401;
        FeatureResponses.Headers(context.HttpContext);context.Result=new ObjectResult(FeatureResponses.Problem(context.HttpContext,status)){StatusCode=status,ContentTypes={"application/problem+json"}};
    }
}
public static class FeatureResponses
{
    public static void Headers(HttpContext context)
    {context.Response.Headers.CacheControl="no-store, private";context.Response.Headers.Pragma="no-cache";if(!context.Response.Headers.ContainsKey("X-Trace-Id"))context.Response.Headers["X-Trace-Id"]=context.TraceIdentifier;}
    public static object Problem(HttpContext context,int status)=>new{type="about:blank",title=status==401?"Authentication required":"Access denied",status,code=status==401?"AUTHENTICATION_REQUIRED":"FORBIDDEN",traceId=context.Response.Headers["X-Trace-Id"].ToString()};
    public static async Task Challenge(HttpContext context,int status,string redirect)
    {
        Headers(context);
        if(context.GetEndpoint()?.Metadata.GetMetadata<FeatureAccessAttribute>()?.JsonResponse==true)
        {context.Response.StatusCode=status;await context.Response.WriteAsJsonAsync(Problem(context,status),options:(System.Text.Json.JsonSerializerOptions?)null,contentType:"application/problem+json",cancellationToken:context.RequestAborted);}
        else context.Response.Redirect(redirect);
    }
}
public sealed class MemberCacheFilter:IAsyncAlwaysRunResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context,ResultExecutionDelegate next)
    {
        if(context.HttpContext.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count>0)
            context.HttpContext.Response.OnStarting(()=>{FeatureResponses.Headers(context.HttpContext);return Task.CompletedTask;});
        await next();
    }
}
