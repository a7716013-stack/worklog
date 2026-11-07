using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkJournal.Api.Contracts;
namespace WorkJournal.Api.OpenApi;
// Generates schemas and routes from actual transport DTOs/controllers, never EF entities.
public static class ApiDocument
{
    public static JsonObject Create() {
        var schemas=new JsonObject();var paths=new JsonObject();
        foreach(var controller in typeof(ApiHost).Assembly.GetTypes().Where(t=>t.IsSubclassOf(typeof(ControllerBase)))) {
            var root=controller.GetCustomAttribute<RouteAttribute>()!.Template;
            foreach(var method in controller.GetMethods().Where(m=>m.GetCustomAttribute<HttpGetAttribute>() is not null || m.GetCustomAttribute<HttpPostAttribute>() is not null)) {
                var post=method.GetCustomAttribute<HttpPostAttribute>();
                var path="/"+root+"/"+(post?.Template??method.GetCustomAttribute<HttpGetAttribute>()!.Template);
                var type=method.ReturnType;
                if(type.IsGenericType && type.GetGenericTypeDefinition()==typeof(Task<>))type=type.GetGenericArguments()[0];
                if(type.IsGenericType && type.GetGenericTypeDefinition()==typeof(ActionResult<>))type=type.GetGenericArguments()[0];
                var responses=new JsonObject { ["200"]=new JsonObject { ["description"]="Success",["content"]=new JsonObject {["application/json"]=new JsonObject {["schema"]=Schema(type,schemas)}}}};
                foreach(var status in new[]{400,401,403,404,429,500,503})responses[status.ToString()]=new JsonObject { ["description"]="ProblemDetails",["content"]=new JsonObject {["application/problem+json"]=new JsonObject {["schema"]=new JsonObject {["$ref"]="#/components/schemas/ApiProblem"}}}};
                var parameters=new JsonArray(); JsonObject? requestBody=null;
                foreach(var parameter in method.GetParameters()) {
                    if(parameter.ParameterType==typeof(CancellationToken))continue;
                    if(parameter.GetCustomAttribute<FromBodyAttribute>() is not null) {requestBody=new JsonObject {["required"]=true,["content"]=new JsonObject {["application/json"]=new JsonObject {["schema"]=Schema(parameter.ParameterType,schemas)}}};continue;}
                    if(parameter.ParameterType==typeof(PageQuery)) {
                        parameters.Add(Parameter("page","query",typeof(int),schemas,false,1));parameters.Add(Parameter("pageSize","query",typeof(int),schemas,false,50));continue;
                    }
                    var location=path.Contains("{"+parameter.Name+"}")?"path":"query";
                    var entry=Parameter(parameter.Name!,location,parameter.ParameterType,schemas,location=="path");
                    if(parameter.Name=="market")entry["schema"]!["enum"]=new JsonArray("all","twse","tpex","etf");
                    parameters.Add(entry);
                }
                var operation=new JsonObject { ["operationId"]=controller.Name+"_"+method.Name,["parameters"]=parameters,["responses"]=responses };
                if(controller.GetCustomAttributes<AuthorizeAttribute>().Any() || method.GetCustomAttributes<AuthorizeAttribute>().Any())
                    operation["security"]=new JsonArray(new JsonObject {["ExistingUserSession"]=new JsonArray()});
                if(requestBody is not null)operation["requestBody"]=requestBody;
                if(post is not null)parameters.Add(Parameter("RequestVerificationToken","header",typeof(string),schemas,true));
                if(post?.Template=="radar/jobs") {responses["202"]=responses["200"]!.DeepClone();responses.Remove("200");}
                if(paths[path] is not JsonObject pathItem) {pathItem=new JsonObject();paths[path]=pathItem;}
                pathItem[post is null?"get":"post"]=operation;
            }
        }
        schemas["ClaimDto"]!["properties"]!["status"]!["enum"]=new JsonArray("SUPPORTED","PARTIALLY_SUPPORTED","UNSUPPORTED","CONFLICTING","UNKNOWN");
        schemas["RunDto"]!["properties"]!["status"]!["enum"]=new JsonArray("NOT_RUN","BLOCKED_BY_AUTHORITY","BLOCKED_BY_DATA","COMPLETED","COMPLETED_INSUFFICIENT_SCOPE","FAILED");
        schemas["ModelDto"]!["properties"]!["scoreMeaning"]!["description"]="Evidence strength score. Not probability, expected return, position size, allocation, or trade instruction.";
        paths["/api/v1/system/readiness"]!["get"]!["responses"]!["503"]=new JsonObject {["description"]="Application not ready",["content"]=new JsonObject {["application/json"]=new JsonObject {["schema"]=new JsonObject {["$ref"]="#/components/schemas/ReadinessDto"}}}};
        schemas["ApiProblem"]=new JsonObject {["type"]="object",["required"]=new JsonArray("status","code","traceId"),["properties"]=new JsonObject {
            ["type"]=new JsonObject {["type"]="string"},["title"]=new JsonObject {["type"]="string"},["status"]=new JsonObject {["type"]="integer"},
            ["instance"]=new JsonObject {["type"]="string"},["traceId"]=new JsonObject {["type"]="string"},["code"]=new JsonObject {["type"]="string",["enum"]=new JsonArray(ApiErrors.Codes.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray())}}};
        return new JsonObject {["openapi"]="3.0.3",["info"]=new JsonObject {["title"]="WorkJournal REST API",["version"]="v1",["description"]="ExistingUserSession; read APIs and CSRF-protected bounded formula/job operations. FullEvidence100 is evidence strength, not probability, expected return, position size, allocation, or trade instruction. Research snapshot is an audited release snapshot, not a live approval store."},["paths"]=paths,
            ["components"]=new JsonObject {["schemas"]=schemas,["securitySchemes"]=new JsonObject {["ExistingUserSession"]=new JsonObject {["type"]="apiKey",["in"]="cookie",["name"]="__Host-WorkJournal"}}}};
    }
    private static JsonObject Parameter(string name,string location,Type type,JsonObject schemas,bool required,int? defaultValue=null) {
        var schema=Schema(type,schemas);if(defaultValue.HasValue){schema["default"]=defaultValue.Value;schema["minimum"]=1;schema["maximum"]=name=="pageSize"?200:1000000;}
        return new JsonObject {["name"]=name,["in"]=location,["required"]=required,["schema"]=schema};
    }
    private static JsonObject Schema(Type type,JsonObject schemas) {
        if(Nullable.GetUnderlyingType(type) is {} inner){var node=Schema(inner,schemas);node["nullable"]=true;return node;}
        if(type.IsArray)return new JsonObject {["type"]="array",["items"]=Schema(type.GetElementType()!,schemas)};
        if(type.IsEnum)return new JsonObject {["type"]="integer",["enum"]=new JsonArray(Enum.GetValues(type).Cast<object>().Select(x=>(JsonNode?)JsonValue.Create(Convert.ToInt32(x))).ToArray())};
        if(type.IsGenericType && (type.GetGenericTypeDefinition()==typeof(System.Collections.Immutable.ImmutableArray<>) || type.GetGenericTypeDefinition()==typeof(IReadOnlyList<>) || type.GetGenericTypeDefinition()==typeof(List<>)))
            return new JsonObject {["type"]="array",["items"]=Schema(type.GetGenericArguments()[0],schemas)};
        if(type==typeof(string))return new JsonObject {["type"]="string"};
        if(type==typeof(bool))return new JsonObject {["type"]="boolean"};
        if(type==typeof(int))return new JsonObject {["type"]="integer",["format"]="int32"};
        if(type==typeof(decimal))return new JsonObject {["type"]="number",["format"]="decimal"};
        if(type==typeof(Guid)||type==typeof(DateOnly)||type==typeof(DateTimeOffset))return new JsonObject {["type"]="string",["format"]=type==typeof(Guid)?"uuid":type==typeof(DateOnly)?"date":"date-time"};
        var name=type.IsGenericType?(type.GetGenericTypeDefinition()==typeof(PageDto<>)?"PageOf":type.Name.Split('`')[0]+"Of")+string.Join("And",type.GetGenericArguments().Select(t=>t.IsGenericType?t.Name.Split('`')[0]+"Of"+t.GetGenericArguments()[0].Name:t.Name)):type.Name;
        if(!schemas.ContainsKey(name)) {
            var properties=new JsonObject();var required=new JsonArray();schemas[name]=new JsonObject {["type"]="object",["properties"]=properties,["required"]=required};
            foreach(var p in type.GetProperties()) {
                var key=JsonNamingPolicy.CamelCase.ConvertName(p.Name);var field=Schema(p.PropertyType,schemas);
                if(new NullabilityInfoContext().Create(p).ReadState==NullabilityState.Nullable)field["nullable"]=true;
                properties[key]=field;if(p.GetCustomAttribute<WorkJournal.Api.Contracts.ApiOptionalAttribute>() is null)required.Add(key);
            }
        }
        return new JsonObject {["$ref"]="#/components/schemas/"+name};
    }
}
