using System.Text.Json.Nodes;
public static class ContractCompatibility
{
    public static List<string> Breaks(JsonNode previous,JsonNode current) {
        var errors=new List<string>();Visit(previous,current,"");return errors;
        void Visit(JsonNode? before,JsonNode? after,string path) {
            if(before is JsonObject old) {
                if(after is not JsonObject next){errors.Add(path+" removed/type changed");return;}
                foreach(var p in old) {
                    if(!next.ContainsKey(p.Key)){errors.Add(path+"/"+p.Key+" removed");continue;}
                    if(p.Key=="enum" && p.Value is JsonArray values && next[p.Key] is JsonArray allowed && values.Any(x=>!allowed.Any(y=>JsonNode.DeepEquals(x,y))))errors.Add(path+" enum removed");
                    else if(p.Key=="required" && p.Value is JsonArray was && next[p.Key] is JsonArray now && now.Any(x=>!was.Any(y=>JsonNode.DeepEquals(x,y))))errors.Add(path+" required property added");
                    else if(p.Key is "type" or "format" or "$ref" && !JsonNode.DeepEquals(p.Value,next[p.Key]))errors.Add(path+" type changed");
                    else if(p.Value is JsonObject)Visit(p.Value,next[p.Key],path+"/"+p.Key);
                }
            }
        }
    }
}
