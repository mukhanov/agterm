# Building and verifying agterm on Windows

Runbook for the Windows machine/VM. Everything is authored on macOS; this is the build-and-verify side.
Branch: `windows-port`. The macOS gates (`swift test` in `agtermCore/`, `dotnet test` in `windows/`) must
stay green on both sides.

## One-time setup

1. **.NET SDK 10** — <https://dotnet.microsoft.com/download> (x64). Verify: `dotnet --version` → `10.x`.
2. **Swift 6 toolchain for Windows** — <https://www.swift.org/install/windows/> (run the installer,
   then restart the terminal). Verify: `swift --version`.
3. **Git** — any recent build; `git clone` your fork, `git checkout windows-port`.

## Build

```powershell
# C# solution (Core, Control, Headless + tests)
cd windows
dotnet build

# Swift CLI — agtermctl.exe from the same source the macOS app ships
cd ..\agtermCore
swift build -c release --product agtermctl
# binary at .build\release\agtermctl.exe — copy somewhere on PATH or use the full path
```

## Verify — headless milestone (no UI)

```powershell
# terminal 1
cd windows
dotnet run --project src\Agterm.Headless
# prints: agterm headless serving %LOCALAPPDATA%\agterm\agterm.sock

# terminal 2 (from agtermCore, or with agtermctl.exe on PATH)
..\.build\release\agtermctl.exe version
..\.build\release\agtermctl.exe session new --name demo
..\.build\release\agtermctl.exe session type --text "echo hi`r"
..\.build\release\agtermctl.exe session text          # contains "hi"
..\.build\release\agtermctl.exe tree
..\.build\release\agtermctl.exe session close --target <id-from-new>
```

If `agtermctl` cannot resolve the socket, check that `%LOCALAPPDATA%\agterm` matches on both sides or
pass `--socket <path>` explicitly.

## Run the test suites

```powershell
# C# protocol/model/dispatcher/socket tests (same suite as on macOS)
cd windows
dotnet test

# Swift package regression (should pass unchanged)
cd ..\agtermCore
swift test
```

## What is expected vs known-limits (as of this phase)

- The headless host has no terminal engine: panes echo into memory. `session.type` → `session.text`
  round-trips, but there is no real shell yet.
- Deferred commands (`zmx.*`, `session.overlay.*`, `hud`, `pick.*`, `ask.*`, `quick*`, `dashboard`,
  `keymap.*`, `hooks.*`, `session.search/restore/background`) answer
  `control dispatcher did not handle <cmd>`.
- `TERM=xterm-256color` (macOS: `xterm-ghostty`) — the documented divergence until a Windows terminfo
  story exists.
- Swift-on-Windows branch sites flagged as uncertain during authoring are listed in the phase-2 commit;
  if `swift build` fails on Windows, note the file/error and fix there (keep macOS `swift test` green).
