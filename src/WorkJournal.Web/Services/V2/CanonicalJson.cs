using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace WorkJournal.Web.Services.V2;

public static class CanonicalJson
{
    public static string Normalize(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, doc.RootElement);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public static string Hash(string canonical) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    public static string Of(object value) => Normalize(JsonSerializer.Serialize(value));
    public static Guid Identity(string kind, string canonical) => new(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\n" + canonical)).AsSpan(0, 16));
    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
                if (properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                    throw new ArgumentException("Duplicate JSON property names are not canonicalizable.");
                foreach (var p in properties) { writer.WritePropertyName(p.Name); Write(writer, p.Value); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(writer, item); writer.WriteEndArray(); break;
            case JsonValueKind.Number: writer.WriteRawValue(Number(value.GetRawText())); break;
            default: value.WriteTo(writer); break;
        }
    }
    // Exact decimal lexical normalization, with no double rounding: 1, 1.0 and 1e0 agree.
    private static string Number(string raw)
    {
        var parts = raw.ToLowerInvariant().Split('e');
        var exponent = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        if (Math.Abs((long)exponent) > 10000 || raw.Length > 10000) throw new ArgumentException("JSON number exceeds canonical limits.");
        var mantissa = parts[0]; var point = mantissa.IndexOf('.');
        if (point >= 0) exponent -= mantissa.Length - point - 1;
        var integer = BigInteger.Parse(mantissa.Replace(".", ""), CultureInfo.InvariantCulture);
        if (integer.IsZero) return "0";
        while (integer % 10 == 0) { integer /= 10; exponent++; }
        return integer.ToString(CultureInfo.InvariantCulture) + (exponent == 0 ? "" : "e" + exponent.ToString(CultureInfo.InvariantCulture));
    }
}
