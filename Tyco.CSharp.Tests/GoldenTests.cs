using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Tyco.CSharp.Tests;

public sealed class GoldenTests
{
    private static string SuiteRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tyco-test-suite"));

    [Fact]
    public void CanonicalSuite()
    {
        var inputsDir = Path.Combine(SuiteRoot, "inputs");
        var expectedDir = Path.Combine(SuiteRoot, "expected");

        var files = Directory.EnumerateFiles(inputsDir, "*.tyco").OrderBy(path => path).ToList();
        Assert.NotEmpty(files);

        foreach (var inputPath in files)
        {
            var name = Path.GetFileNameWithoutExtension(inputPath);
            var expectedPath = Path.Combine(expectedDir, $"{name}.json");
            if (!File.Exists(expectedPath))
            {
                continue;
            }

            var context = TycoParser.Load(inputPath);
            var actual = context.AsObject();
            var expected = JsonNode.Parse(File.ReadAllText(expectedPath))!;

            if (!JsonEquals(actual, expected))
            {
                var actualPretty = actual.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                var expectedPretty = expected.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                throw new Xunit.Sdk.XunitException($"Mismatch for {name}:\nExpected:\n{expectedPretty}\nActual:\n{actualPretty}");
            }
        }
    }

    private static bool JsonEquals(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }
        if (left is JsonValue lv && right is JsonValue rv)
        {
            if (lv.TryGetValue<double>(out var ld) && rv.TryGetValue<double>(out var rd))
            {
                return Math.Abs(ld - rd) < double.Epsilon;
            }
            if (lv.TryGetValue<string>(out var ls) && rv.TryGetValue<string>(out var rs))
            {
                return ls == rs;
            }
            if (lv.TryGetValue<bool>(out var lb) && rv.TryGetValue<bool>(out var rb))
            {
                return lb == rb;
            }
            return lv.ToJsonString() == rv.ToJsonString();
        }
        if (left is JsonObject lo && right is JsonObject ro)
        {
            if (lo.Count != ro.Count)
            {
                return false;
            }
            foreach (var kvp in lo)
            {
                if (!ro.TryGetPropertyValue(kvp.Key, out var rvNode))
                {
                    return false;
                }
                if (!JsonEquals(kvp.Value, rvNode))
                {
                    return false;
                }
            }
            return true;
        }
        if (left is JsonArray la && right is JsonArray ra)
        {
            if (la.Count != ra.Count)
            {
                return false;
            }
            for (var i = 0; i < la.Count; i++)
            {
                if (!JsonEquals(la[i], ra[i]))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }
}
