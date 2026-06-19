using System.Text.Json;

namespace KeyVaultSync;

public static class JsonFlattener
{
    public static IReadOnlyList<KeyValuePair<string, string>> Flatten(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<KeyValuePair<string, string>>();
        Walk(doc.RootElement, "", result);
        return result;
    }

    private static void Walk(JsonElement element, string prefix, List<KeyValuePair<string, string>> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = prefix.Length == 0 ? prop.Name : $"{prefix}:{prop.Name}";
                    Walk(prop.Value, key, result);
                }
                break;

            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, $"{prefix}:{i}", result);
                    i++;
                }
                break;

            case JsonValueKind.String:
                result.Add(new(prefix, element.GetString() ?? ""));
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                result.Add(new(prefix, element.GetRawText()));
                break;

            case JsonValueKind.Null:
            default:
                break;
        }
    }
}
