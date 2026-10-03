# XtermSharp — vendored

Vendored from https://github.com/migueldeicaza/XtermSharp at commit
1bed529 (Backport of SwiftTerm fix 4fc8ecbb853ec74a178c615387abf75456c0ebd0), MIT license (LICENSE
beside this file).

Local changes (kept deliberately small; upstream PRs where sensible):
- Pty.cs NOT vendored — the Windows host owns ConPTY (../Pty/ConPty.cs).
- InputHandler.cs: OSC 7 handler added (cwd report), feeding Terminal.ReportCwd.
- Renderer/ and gui.cs-coupled helpers may be trimmed when the Direct2D renderer lands.

Regenerating: clone upstream, copy `XtermSharp/*.cs` (minus Pty.cs) + InputHandlers/ + Utils/ +
Renderer/, then re-apply the OSC 7 patch.
