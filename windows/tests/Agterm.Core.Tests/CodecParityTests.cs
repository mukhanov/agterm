using System.Text.Json;
using Agterm.Core;
using Agterm.Core.Protocol;
using Xunit;

namespace Agterm.Core.Tests;

/// <summary>
/// Wire parity against Swift-generated golden fixtures (windows/tools/golden). Every fixture is one
/// newline-terminated JSON line encoded by the SAME bare JSONEncoder the macOS server uses; each test
/// decodes it into the C# model, re-encodes, and requires semantic equality. This catches missing field
/// mappings, lossy number round-trips, enum raw-value drift, and null-omission breakage.
/// </summary>
public class CodecParityTests
{
    private static readonly string GoldenDir = Path.Combine(AppContext.BaseDirectory, "Golden");

    public static IEnumerable<object[]> RequestFixtures() => Fixtures("request");
    public static IEnumerable<object[]> ResponseFixtures() => Fixtures("response");
    public static IEnumerable<object[]> TextFixtures() => Fixtures("text");

    private static IEnumerable<object[]> Fixtures(string kind)
    {
        var manifest = File.ReadAllLines(Path.Combine(GoldenDir, "manifest.txt"))
            .Select(line => line.Split(' ', 2))
            .Where(parts => parts.Length == 2 && parts[1] == kind)
            .Select(parts => parts[0])
            .ToList();
        Assert.NotEmpty(manifest);
        return manifest.Select(name => new object[] { name });
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(GoldenDir, $"{name}.json")).TrimEnd('\n');

    [Theory]
    [MemberData(nameof(RequestFixtures))]
    public void RequestRoundTrips(string name)
    {
        var line = ReadFixture(name);
        var request = ControlJson.Deserialize<ControlRequest>(line);
        var reencoded = ControlJson.Serialize(request);
        JsonCanonical.AssertEqual(line, reencoded, name);
    }

    [Theory]
    [MemberData(nameof(ResponseFixtures))]
    public void ResponseRoundTrips(string name)
    {
        var line = ReadFixture(name);
        var response = ControlJson.Deserialize<ControlResponse>(line);
        var reencoded = ControlJson.Serialize(response);
        JsonCanonical.AssertEqual(line, reencoded, name);
    }

    [Fact]
    public void UnknownCommandErrorMatchesSwiftWording()
    {
        var pinned = ReadFixture("resp_error_unknown_cmd");
        var exception = Assert.Throws<JsonException>(
            () => ControlJson.Deserialize<ControlRequest>("{\"cmd\":\"bogus.command\",\"args\":{}}"));
        Assert.Equal(pinned, ControlWire.InvalidRequestMessage(exception));
    }

    [Fact]
    public void UnknownArgumentDecodesAsNoOp()
    {
        // A field the server does not model is dropped silently — macOS servers behave the same way
        // against a newer CLI (version-skew tolerance, pinned in .claude/rules/control-api.md).
        var request = ControlJson.Deserialize<ControlRequest>(
            "{\"cmd\":\"session.new\",\"target\":\"active\",\"args\":{\"cwd\":\"/tmp\",\"futureField\":42}}");
        Assert.Equal(ControlCommand.SessionNew, request.Cmd);
        Assert.Equal("active", request.Target);
        Assert.Equal("/tmp", request.Args?.Cwd);
    }

    [Fact]
    public void CaseSensitiveKeys()
    {
        // Swift's JSONDecoder is case-sensitive; a differently-cased key is simply absent.
        var request = ControlJson.Deserialize<ControlRequest>("{\"cmd\":\"tree\",\"Target\":\"active\"}");
        Assert.Null(request.Target);
    }

    [Fact]
    public void NullOptionalsOmitted()
    {
        var encoded = ControlJson.Serialize(new ControlResponse(true, new ControlResult { Id = "x" }));
        Assert.Equal("{\"ok\":true,\"result\":{\"id\":\"x\"}}", encoded);
    }

    [Fact]
    public void ForwardSlashEscapedLikeFoundation()
    {
        var encoded = ControlJson.Serialize(new ControlResponse(true, new ControlResult { Text = "/a/b" }));
        Assert.Contains("\\/a\\/b", encoded);
    }

    [Fact]
    public void DoublesFormatLikeSwift()
    {
        Assert.Equal("0.5", SwiftDoubleConverter.Format(0.5));
        Assert.Equal("300.0", SwiftDoubleConverter.Format(300));
        Assert.Equal("0.05", SwiftDoubleConverter.Format(0.05));
        Assert.Equal("1e-07", SwiftDoubleConverter.Format(1e-7));
        Assert.Equal("1e+20", SwiftDoubleConverter.Format(1e20));
        Assert.Equal("-0.5", SwiftDoubleConverter.Format(-0.5));
        Assert.Equal("1750000000.5", SwiftDoubleConverter.Format(1750000000.5));
        Assert.Equal("0.001", SwiftDoubleConverter.Format(0.001));
    }

    [Fact]
    public void ControlCharactersSurviveRoundTrip()
    {
        var response = new ControlResponse(true, new ControlResult { Text = "a\u0001b\u001fc\bd\ne" });
        var reencoded = ControlJson.Serialize(ControlJson.Deserialize<ControlResponse>(ControlJson.Serialize(response)));
        JsonCanonical.AssertEqual(ControlJson.Serialize(response), reencoded, "control chars");
    }

    [Fact]
    public void CommandWireNamesCoverEveryCommand()
    {
        foreach (ControlCommand command in Enum.GetValues<ControlCommand>())
            Assert.False(string.IsNullOrEmpty(command.WireName()), $"{command} has no wire name");
    }

    [Fact]
    public void RequestLineFraming()
    {
        var line = ControlJson.SerializeResponseLine(new ControlResponse(true, new ControlResult { Id = "x" }));
        Assert.EndsWith("\n", line);
        Assert.Single(line[..^1].Split('\n'));// exactly one trailing newline, nothing after
    }
}
