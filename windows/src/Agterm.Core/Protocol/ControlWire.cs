using System.Text.Json;

namespace Agterm.Core.Protocol;

/// <summary>Shared wire limits and error wording, mirroring agtermCore's ControlWire.</summary>
public static class ControlWire
{
    /// <summary>1 MiB cap on a request line (newline excluded). Over it the server rejects the line and
    /// closes the connection; the client checks the same cap before writing.</summary>
    public const int MaxRequestLineBytes = 1 << 20;

    /// <summary>
    /// The error for a request that does not decode. Mirrors Swift's
    /// <c>ControlWire.invalidRequestMessage</c>: it reports the DecodingError's context, which names the
    /// rejected <c>cmd</c>. The cmd converter already throws with Swift's wording, so the unknown-command
    /// case needs no translation; anything else degrades to the message as-is, as Swift does for
    /// non-Decoding errors.
    /// </summary>
    public static string InvalidRequestMessage(JsonException error) => $"invalid request: {error.Message}";
}
