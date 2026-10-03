# Building and verifying agterm on Windows

Runbook for the Windows machine/VM. Everything is authored on macOS; this is the build-and-verify side.
Branch: `windows-port`. The macOS gates (`swift test` in `agtermCore/`, `dotnet test` in `windows/`) must
stay green on both sides.

## One-time setup

1. **.NET SDK 10** — the user-local zip avoids elevation: download
   `https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-win-x64.zip`, extract to
   `C:\Users\<you>\dotnet`, and export `DOTNET_ROOT` per shell (apphosts resolve the runtime through it,
   not PATH). Verify: `%DOTNET_ROOT%\dotnet.exe --version` → `10.0.x`.
2. **Swift 6 toolchain for Windows** — INSTALLED (2026-10-03): Swift 6.4.0 managed layout at
   `C:\Users\nikol\AppData\Local\Programs\Swift\` (Toolchains/Platforms/Runtimes). The installer writes
   `SDKROOT` and both `Path` entries into `HKCU\Environment` — new shells only; an older shell needs
   `source C:\Users\nikol\agterm-win-env.sh` (PATH, SDKROOT, runtime DLLs, `core.symlinks=false` for
   dependency checkouts). Self-contained layout: no MSVC/Windows SDK requirement.
3. **Git** — any recent build; `git clone` your fork, `git checkout windows-port`.

## Build

```powershell
# C# solution (Core, Control, Headless + tests)
cd windows
dotnet build

# Swift CLI — agtermctl.exe from the same source the macOS app ships
cd ..\agtermCore
swift build -c release --product agtermctl
# binary at .build\out\Products\Release-windows-x86_64\agtermctl.exe - the .build\release symlink needs Developer Mode and stays a warning
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
