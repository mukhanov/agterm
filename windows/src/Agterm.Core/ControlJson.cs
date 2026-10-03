using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agterm.Core;

/// <summary>
/// Swift-parity JSON for the control wire. The macOS sides use bare <c>JSONEncoder()</c>/<c>JSONDecoder()</c>
/// (ControlServer.swift:524, SocketClient.swift:40). Swift's JSONEncoder key order is NOT deterministic
/// across processes (per-process hash seed — verified: two runs of the fixture generator produce different
/// orderings), so the parity contract is SEMANTIC JSON equality, not byte equality:
/// <list type="bullet">
/// <item>null optionals omitted (Swift encodeIfPresent),</item>
/// <item>forward slash escaped as <c>\/</c> (Foundation default; System.Text.Json never escapes it),</item>
/// <item>doubles in Swift's <c>Double.description</c> form: shortest round-trip, always with a '.' or
/// exponent, lowercase 'e' ("300.0", "0.05", "1e+20", "1e-07"),</item>
/// <item>non-ASCII left raw, C0 controls escaped with the \b\t\n\f\r shorthands.</item>
/// </list>
/// Decoding mirrors Swift's synthesized Codable: case-sensitive keys, unknown members ignored, unknown
/// enum raw values failing with Swift's wording.
/// </summary>
public static class ControlJson
{
    /// <summary>Options matching Swift's JSONEncoder output byte-for-byte (after the slash pass).</summary>
    public static readonly JsonSerializerOptions Encode = BuildEncode();

    /// <summary>Options matching Swift's JSONDecoder acceptance.</summary>
    public static readonly JsonSerializerOptions Decode = BuildDecode();

    public static string Serialize<T>(T value)
    {
        var encoded = JsonSerializer.Serialize(value, Encode);
        return EscapeSlashes(encoded);
    }

    public static byte[] SerializeToUtf8Bytes<T>(T value)
    {
        var encoded = JsonSerializer.SerializeToUtf8Bytes(value, Encode);
        return EscapeSlashes(encoded);
    }

    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Decode)
        ?? throw new JsonException($"Cannot decode {typeof(T).Name} from null");

    public static T Deserialize<T>(byte[] json) => JsonSerializer.Deserialize<T>(json, Decode)
        ?? throw new JsonException($"Cannot decode {typeof(T).Name} from null");

    /// <summary>One request line, newline-terminated — the server's reply framing.</summary>
    public static string SerializeResponseLine(Protocol.ControlResponse response) =>
        Serialize(response) + "\n";

    /// <summary>
    /// Forward slash appears in JSON only inside string content, and the encoder never emits it escaped,
    /// so a whole-document byte replace is safe. This is the one escaping Foundation does by default that
    /// System.Text.Json has no built-in encoder for.
    /// </summary>
    private static string EscapeSlashes(string json) => json.Replace("/", "\\/");

    private static byte[] EscapeSlashes(byte[] json)
    {
        var hasSlash = Array.IndexOf(json, (byte)'/') >= 0;
        if (!hasSlash) return json;
        var output = new byte[json.Length + CountSlashes(json)];
        var o = 0;
        foreach (var b in json)
        {
            output[o++] = b;
            if (b == (byte)'/') output[o++] = (byte)'\\';
        }
        return output;
    }

    private static int CountSlashes(byte[] json)
    {
        var count = 0;
        foreach (var b in json)
            if (b == (byte)'/') count++;
        return count;
    }

    private static JsonSerializerOptions BuildEncode()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new SwiftDoubleConverter());
        return options;
    }

    private static JsonSerializerOptions BuildDecode()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            IncludeFields = false,
        };
        options.Converters.Add(new SwiftDoubleConverter());
        return options;
    }
}

/// <summary>Formats doubles the way Swift's JSONEncoder does: shortest round-trip digits, a '.' or
/// exponent always present, 'e' lowercase with a signed two-plus-digit exponent.</summary>
public sealed class SwiftDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDouble();

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (!double.IsFinite(value))
            throw new JsonException($"Cannot encode non-finite double {value}");
        writer.WriteRawValue(Format(value), skipInputValidation: true);
    }

    public static string Format(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        // .NET's shortest round-trip form uses 'E' for exponents and drops the ".0" on integral values;
        // Swift keeps a decimal point and spells the exponent lowercase.
        if (text.Contains('E')) text = text.Replace('E', 'e');
        if (!text.Contains('.') && !text.Contains('e')) text += ".0";
        return text;
    }
}
