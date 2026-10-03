using System.Globalization;
using System.Text.Json.Nodes;

namespace Agterm.Core.Tests;

/// <summary>
/// Semantic JSON equality: same key-value structure, numbers compared by mathematical value (so "1e-07"
/// equals "1E-07" and "300.0" equals "300"), strings byte-exact, arrays order-sensitive. Key ORDER is
/// ignored — Swift's JSONEncoder ordering is not deterministic across processes, so it is not part of the
/// wire contract.
/// </summary>
public static class JsonCanonical
{
    public static void AssertEqual(string expected, string actual, string context)
    {
        var expectedNode = JsonNode.Parse(expected) as JsonObject
            ?? throw new Xunit.Sdk.XunitException($"{context}: expected is not a JSON object: {expected}");
        var actualNode = JsonNode.Parse(actual) as JsonObject
            ?? throw new Xunit.Sdk.XunitException($"{context}: actual is not a JSON object: {actual}");
        var differences = new List<string>();
        if (!NodesEqual(expectedNode, actualNode, "$", differences))
            throw new Xunit.Sdk.XunitException(
                $"{context}: JSON differs:{Environment.NewLine}{string.Join(Environment.NewLine, differences)}{Environment.NewLine}expected: {expected}{Environment.NewLine}actual:   {actual}");
    }

    private static bool NodesEqual(JsonNode? expected, JsonNode? actual, string path, List<string> differences)
    {
        switch (expected, actual)
        {
            case (JsonObject expectedObject, JsonObject actualObject):
                foreach (var key in expectedObject.Select(kv => kv.Key).Union(actualObject.Select(kv => kv.Key)))
                {
                    if (!expectedObject.ContainsKey(key))
                    {
                        differences.Add($"{path}.{key}: unexpected in actual");
                        return false;
                    }
                    if (!actualObject.ContainsKey(key))
                    {
                        differences.Add($"{path}.{key}: missing in actual");
                        return false;
                    }
                    if (!NodesEqual(expectedObject[key], actualObject[key], $"{path}.{key}", differences))
                        return false;
                }
                return true;

            case (JsonArray expectedArray, JsonArray actualArray):
                if (expectedArray.Count != actualArray.Count)
                {
                    differences.Add($"{path}: array length {expectedArray.Count} != {actualArray.Count}");
                    return false;
                }
                for (var i = 0; i < expectedArray.Count; i++)
                    if (!NodesEqual(expectedArray[i], actualArray[i], $"{path}[{i}]", differences))
                        return false;
                return true;

            case (JsonValue expectedValue, JsonValue actualValue):
                if (NumbersEqual(expectedValue, actualValue, out var difference))
                    return true;
                differences.Add($"{path}: {difference}");
                return false;

            default:
                differences.Add($"{path}: kind mismatch ({expected?.GetType().Name ?? "null"} vs {actual?.GetType().Name ?? "null"})");
                return false;
        }
    }

    private static bool NumbersEqual(JsonValue expected, JsonValue actual, out string? difference)
    {
        difference = null;
        var expectedText = expected.ToJsonString();
        var actualText = actual.ToJsonString();

        if (expected.TryGetValue<ulong>(out var expectedUlong) && actual.TryGetValue<ulong>(out var actualUlong))
        {
            if (expectedUlong == actualUlong) return true;
            difference = $"{expectedText} != {actualUlong} (ulong)";
            return false;
        }
        if (expected.TryGetValue<double>(out var expectedDouble) && actual.TryGetValue<double>(out var actualDouble))
        {
            if (expectedDouble.Equals(actualDouble)) return true;
            difference = $"{expectedText} != {actualText} (double {expectedDouble.ToString("R", CultureInfo.InvariantCulture)} vs {actualDouble.ToString("R", CultureInfo.InvariantCulture)})";
            return false;
        }
        if (expected.TryGetValue<bool>(out var expectedBool) && actual.TryGetValue<bool>(out var actualBool))
        {
            if (expectedBool == actualBool) return true;
            difference = $"{expectedText} != {actualText} (bool)";
            return false;
        }
        if (expected.TryGetValue<string>(out var expectedString) && actual.TryGetValue<string>(out var actualString))
        {
            if (expectedString == actualString) return true;
            difference = $"string {expectedText} != {actualText}";
            return false;
        }
        if (expected.ToJsonString() == actual.ToJsonString()) return true;
        difference = $"{expectedText} != {actualText} (kind mismatch)";
        return false;
    }
}
