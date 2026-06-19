using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class JsonFlattenerTests
{
    [Fact]
    public void Flattens_nested_objects_with_colon()
    {
        var json = """{ "ConnectionStrings": { "Default": "Server=." } }""";
        var pairs = JsonFlattener.Flatten(json);
        Assert.Contains(new KeyValuePair<string, string>("ConnectionStrings:Default", "Server=."), pairs);
    }

    [Fact]
    public void Expands_arrays_with_index()
    {
        var json = """{ "Hosts": ["a", "b"] }""";
        var pairs = JsonFlattener.Flatten(json);
        Assert.Contains(new KeyValuePair<string, string>("Hosts:0", "a"), pairs);
        Assert.Contains(new KeyValuePair<string, string>("Hosts:1", "b"), pairs);
    }

    [Fact]
    public void Emits_numbers_and_bools_as_json_text()
    {
        var json = """{ "Port": 8080, "Enabled": true }""";
        var pairs = JsonFlattener.Flatten(json);
        Assert.Contains(new KeyValuePair<string, string>("Port", "8080"), pairs);
        Assert.Contains(new KeyValuePair<string, string>("Enabled", "true"), pairs);
    }

    [Fact]
    public void Skips_null_values()
    {
        var json = """{ "A": null, "B": "x" }""";
        var pairs = JsonFlattener.Flatten(json);
        Assert.DoesNotContain(pairs, p => p.Key == "A");
        Assert.Single(pairs);
    }

    [Fact]
    public void Empty_object_yields_no_pairs()
    {
        Assert.Empty(JsonFlattener.Flatten("{}"));
    }
}
