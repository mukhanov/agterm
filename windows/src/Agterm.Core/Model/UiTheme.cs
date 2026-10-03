namespace Agterm.Core.Model;

/// <summary>
/// The app-wide terminal theme: name plus the palette the renderer reads. macOS themes drive Ghostty
/// config; here the renderer consumes the values directly. Two bundled themes while the catalog is a
/// Windows MVP; the set/list control arms validate against <see cref="Names"/>.
/// </summary>
public static class UiTheme
{
    public static readonly IReadOnlyList<string> Names = ["agterm-dark", "agterm-light"];

    public static string Current { get; private set; } = "agterm-dark";

    public static event Action<string>? Changed;

    public static bool Set(string name)
    {
        if (!Names.Contains(name, StringComparer.Ordinal)) return false;
        if (Current == name) return true;
        Current = name;
        Changed?.Invoke(name);
        return true;
    }

    /// <summary>Background and foreground as 0x00RRGGBB for the named theme (the current one when null).</summary>
    public static (int Background, int Foreground) Palette(string? name = null)
    {
        var isLight = (name ?? Current) == "agterm-light";
        return isLight ? (0xFFFFFF, 0x1A1A1A) : (0x0C0C0C, 0xCCCCCC);
    }
}
