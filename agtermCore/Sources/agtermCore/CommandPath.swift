/// Builds the `PATH` a custom command runs with.
///
/// A GUI app started by launchd (Dock, Finder, Spotlight, `open`) inherits `/usr/bin:/bin:/usr/sbin:/sbin`,
/// and the runner's `/bin/sh -c` is neither a login nor an interactive shell, so it never reaches
/// `path_helper`. Without widening, a bare `agtermctl`, `jq` or `lazygit` in a keymap line exits 127 with
/// the shell's own diagnostic discarded (#393).
public enum CommandPath {
    /// Appended when absent: the Help installer's target, which reaches `PATH` only through `path_helper`
    /// and so only in a login shell, and the cask's — the route the README documents. `RemoteSession`
    /// widens a REMOTE shell with the same list, so the two cannot disagree about where the CLI lives.
    static let standardDirectories = [CLIInstall.installDirectory, "/opt/homebrew/bin"]

    /// The PATH a launchd-started app inherits, used when the environment carries none at all; the
    /// Windows system defaults stand in for it there.
    #if os(Windows)
    static let launchdDefault = "%SystemRoot%\\system32;%SystemRoot%;%SystemRoot%\\System32\\Wbem;%LOCALAPPDATA%\\Programs"
    #else
    static let launchdDefault = "/usr/bin:/bin:/usr/sbin:/sbin"
    #endif

    /// `bundledCLIDirectory` goes FIRST because it is present even when nothing is installed, and its
    /// protocol matches the running app. It does NOT decide which instance the CLI drives: `--socket`, then
    /// `AGTERM_STATE_DIR`, then Application Support resolve that identically for either binary. The standard
    /// directories go LAST, so a PATH that already names them keeps its own order and anything the user did
    /// widen still wins.
    public static func widened(_ path: String?, bundledCLIDirectory: String?) -> String {
        let base = path.flatMap { $0.isEmpty ? nil : $0 } ?? launchdDefault
        var entries: [String] = []
        var seen = Set<String>()
        func add(_ dir: String) {
            guard !dir.isEmpty, seen.insert(dir).inserted else { return }
            entries.append(dir)
        }
        bundledCLIDirectory.map(add)
        #if os(Windows)
        base.split(separator: ";", omittingEmptySubsequences: true).forEach { add(String($0)) }
        #else
        base.split(separator: ":", omittingEmptySubsequences: true).forEach { add(String($0)) }
        #endif
        standardDirectories.forEach(add)
        #if os(Windows)
        return entries.joined(separator: ";")
        #else
        return entries.joined(separator: ":")
        #endif
    }
}
