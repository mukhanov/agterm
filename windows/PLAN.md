# agterm на Windows — план порта

## Прогресс (обновляется)

- **Фаза 0 ✅** (`windows/` spine, codec, 119 golden-фикстур, 127 тестов). Открытие: Swift `JSONEncoder` даёт недетерминированный порядок ключей между процессами → контракт семантический, не побайтовый.
- **Фаза 1 ✅** (диспетчер MVP-команд с pinned-ошибками, resolve, 92 теста).
- **Фаза 2 ✅** (Agterm.Control: сокет-сервер с владением; StoreControlActions; модель; headless-хост; smoke по протоколу зелёный; 250 тестов). Ловушка: `Socket.Close()` из Stop() ждёт висящий блокирующий `Accept()` → дедлок; лечится Poll-циклом.
- **Swift-ветки ✅** (агtermCore собирается на Windows; agtermctl.exe прогнан end-to-end против headless и GUI).
- **Фаза 3 ✅** (ConPTY + XtermSharp + `--live` headless; все проверки зелёные), **Фаза 4 ✅** (WinUI 3 chrome:
  сайдбар/дек/сплиты/акселераторы/control на UI-потоке), **Фаза 5 ✅** (Direct2D-рендер, темы, скроллбэк,
  ресайз pty из сетки), сайдбар-паритет (статусы/rename/контексты), drag-разделитель.
- **Фазы 6–7 ✅** (E2E-сьют `tests/Agterm.E2E.Tests`, CI `windows-ci.yml`, `tools/publish.ps1`).
- Отложено: selection мышью, системная тема следом за Windows, оконные команды (NullWindowHost),
  глиф-атлас как оптимизация, keymap.conf.

## Context

**Зачем.** agterm — нативный macOS-терминал (SwiftUI/AppKit + libghostty) для параллельной работы с coding-агентами: именованные workspaces/sessions, полный control-API (`agtermctl` через локальный AF_UNIX сокет, newline-delimited JSON), статусы агентов, оверлеи. Цель — работающий agterm на Windows.

**Почему порт, а не сборка.** UI-слой (~300 файлов SwiftUI/AppKit) на Windows не существует; терминальный движок libghostty upstream на Windows не собирается; zsh/zmx/ssh-механика — macOS-специфична. При этом `agtermCore` (Swift: модель + протокол + `agtermctl`) на ~90–95% компилируется Swift-тулчейном Windows — он остаётся **контрактом и источником CLI**, а не рантаймом UI.

**Образец** — форк agterm-linux (общий `agtermCore` + нативный UI, ветка `linux-port`, rebase на upstream). Стек C#+ConPTY+Direct2D доказан agwinterm.

**Утверждено пользователем:** C#/.NET 10 + WinUI 3; объём MVP; верификация на Windows-машине пользователя (разработка с macOS).

## Топология

- Ветка `windows-port` от `master` (сейчас `1f09f69`); `master` зеркалит upstream; синхронизация — rebase.
- Новая директория `windows/` (C#-решение); правки Swift — только узкие `#if os(Windows)` / `#if canImport(WinSDK)` ветки (~13 файлов, без переноса кода и переименований — `agtermCore` публикуемая библиотека, публичные символы load-bearing).

```
windows/
  Agterm.sln
  Directory.Build.props / Directory.Packages.props
  src/
    Agterm.Core/       # модель + протокол-кодек; net10.0, без UI/сокетов — тестится на macOS
    Agterm.Control/    # AF_UNIX listener + framing + dispatcher (IControlActions)
    Agterm.Terminal/   # ConPTY + VT-движок + Direct2D-рендер; net10.0-windows
    Agterm.Windows/    # WinUI 3 app (unpackaged)
  tests/
    Agterm.Core.Tests/ Agterm.Control.Tests/ Agterm.E2E.Tests/
  tools/golden/        # Swift-генератор golden-фикстур протокола (macOS)
  tools/windows-build.md
.github/workflows/windows-ci.yml        # только на ветке
```

## Ключевые решения

| Область | Решение |
|---|---|
| VT-движок | **XtermSharp** (порт xterm.js, MIT, активный, frontend-agnostic) — вендорим форк в `Agterm.Terminal/Vt/`. Фаза 0 — spike: OSC 7/9/777 хуки, readback буфера, selection; гэты чиним патчем форка. VtNetCorePatched слабее по xterm.js-паритету. |
| Рендер | D3D11 + Direct2D/DirectWrite (Vortice), `SwapChainPanel` через `CreateSwapChainForComposition`; глиф-атлас по `(glyphId, bold, italic)`, сетка ячеек, dirty-rows; шрифт Cascadia Mono + fallback-цепочка. |
| pty | ConPTY напрямую (P/Invoke ~60 строк: `CreatePseudoConsole`/`ResizePseudoConsole`, `STARTUPINFOEX` + `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`, размер **в ячейках**). |
| Транспорт | AF_UNIX (Win10 1803+, `UnixDomainSocketEndPoint`), тот же протокол. Named pipe — только задокументированный план Б. |
| agtermctl | Собирается из того же Swift-кода на Windows-тулчейне. План Б — `Agterm.Ctl` на C#-кодеке (решение по итогам 2-дневного timebox после первой сборки). |
| Сессии | Eager deck как на macOS: все поверхности смонтированы, переключение видимостью; рендер скрытых не презентует (occlusion-parity), ConPTY живут. |
| Snapshot JSON | **Совместим с Swift по ключам** (`Snapshot.swift`, `windows.json` index): файлы state читаются кросс-платформенно; UUID — uppercase (`Guid.ToString("D").ToUpperInvariant()` — Swift `UUID.uuidString` uppercase), закрепить фикстурой. Lossy-optional decode: битый optional → default, не ошибка. |
| Упаковка | Unpackaged WinUI 3 self-contained exe: нет MSIX-песочницы для AF_UNIX, agtermctl.exe рядом. |

## Паритет протокола (MVP)

Конверт: `{"cmd":…,"target":…,"args":{…}}` (nil-поля опускаются), ответ `{"ok":true,"result":{…}}`/`{"ok":false,"error":"…"}`. Контракт: `agtermCore/Sources/agtermCore/ControlProtocol.swift` + `ControlProjection.swift` (+ `.claude/rules/control-api.md` — каталог команд).

**IN:** `version`, `tree`, `events.read`; `workspace.new/rename/delete/select/go/move/focus/filter/collapse/expand`; `session.new/duplicate/close/select/reveal/go/rename/move/type/text/status/flag/seen/context/split/split.close/swap/focus/resize/copy/paste/selectall`; `surface.cursor`; `font.inc/dec/reset`; `window.new/list/select/go/close/rename/delete/resize/move/minimize/zoom/fullscreen`; `sidebar`/`sidebar.width`; `theme.set/list`; `notify`.
Пиннинги: строки ошибок (`"no such session: X"`, `"ambiguous session prefix 'x' → …"`, `"event run changed"` и др.), правила `session.text` (viewport по умолчанию; `--all`/`--lines` = screen+scrollback; тримминг хвостовых пробельных строк; пустой результат = пустая строка, не ошибка), `session.type` (CR/LF→LF, строка → текстовый burst, конец строки → **отдельное нажатие Return**, финальный Return с задержкой 10 мс, запрет NUL), `surface.cursor` (0-based колонка), `session.resize` (клэмп 0.05–0.95, echo `%.3f`).
**DEFERRED** (`zmx.*`, `session.overlay.*`, `hud`, `pick.*`, `ask.*`, `quick*`, `dashboard`, `keymap.*`, `hooks.*`, `session.search/restore/background/scratch`, …) отвечают Swift-fallback строкой `"control dispatcher did not handle {cmd}"`.

## Сокет и владение

- Путь (байт-в-байт с обеих сторон): `%LOCALAPPDATA%\agterm\agterm.sock`; прецеденция `--socket` → `AGTERM_CONTROL_SOCKET` → `<AGTERM_STATE_DIR>/agterm.sock` → LOCALAPPDATA; гард длины `sun_path` ≥104.
- Владение вместо `flock`: `FileStream(lock, OpenOrCreate, ReadWrite, FileShare.None)` на `<socket>.lock` — атомарно и освобождается ядром при смерти процесса. Отказ: `refused=true`, shells получают `AGTERM_SOCKET=<path>.unavailable` (никогда не опускаем), бинд нет, повторная попытка захвата при каждом `window.new`. Не пробовать `connect()`-зонд.
- Framing: read до `\n`, кап 1 MiB, дедлайн чтения 10 с, `ReceiveTimeout`/`SendTimeout` 5 с, один JSON-ответ + `\n`.

## ConPTY и окружение

- Профили: PowerShell (`pwsh -NoLogo`, fallback `powershell`), cmd (`%ComSpec%`), Git Bash (registry `HKLM\SOFTWARE\GitForWindows`), WSL (`wsl.exe -l -q`, парсер UTF-16LE вывода). Профиль на сессию; `--command` — сырой argv (как на macOS).
- Env (паритет `SurfaceEnvironment.swift`): `TERM=xterm-256color` (задокументированное расхождение с `xterm-ghostty`), `TERM_PROGRAM=agterm`, `TERM_PROGRAM_VERSION`, `COLORTERM=truecolor`, `AGTERM_ENABLED/SESSION_ID/SOCKET/WINDOW_ID/WORKSPACE_ID/PANE/PANE_ID` — `AGTERM_PANE_ID` стабилен сквозь swap/promote (паритет `--pane-id`).
- Выход: primary exit при живом сплите → promote выжившего; `--wait` → удержание с "process exited (code N) — press any key".

## WinUI 3 chrome

MainWindow: кастомный titlebar (Mica) + TreeView-сайдбар (`WindowLibraryModel`) + `SessionDeck` + `SplitHost` (Grid + divider → splitRatio). Акселераторы: Ctrl+T/W/D, Ctrl+Tab, Ctrl+1..9, Ctrl+=/-/0. `keymap.conf` читается из `~/.config/agterm/` (кросс-платформенный путь), модификаторы `ctrl/shift/alt`; `super+` мапится в `ctrl+` (задокументировать). Темы: минимальный каталог + следование системе. `notify` в MVP: unseen + ок при выключенных тостах.

## Persistence

`%LOCALAPPDATA%\agterm\windows\windows.json` (index: version/frontmost/windows[{id,name,isOpen}]) + `windows/<uuid>.json` (Snapshot: те же ключи, что Swift; незаполняемые поля опускаются — lossy decode macOS-файлов допустим). Restore = свежие шеллы в сохранённом layout (режим 1 из 3). Сейв: структурные мутации сразу, selection/font с дебаунсом 300 мс, при выходе; атомарная запись (temp + `File.Replace`).

## Swift-ветки для agtermctl.exe

| Файл | Правка |
|---|---|
| `agtermCore/…/TerminfoInstall.swift` | обернуть `#if !os(Windows)`; регистрацию `terminfo` в `agtermctlKit/TerminfoCommands.swift` тоже |
| `agtermCore/…/PersistenceStore.swift`, `SettingsStore.swift` | defaultDirectory → `%LOCALAPPDATA%\agterm` |
| `agtermCore/…/ConfigPaths.swift` | `editorCommand` → notepad/$EDITOR (путь `~/.config/agterm` оставить) |
| `agtermCore/…/CLIInstall.swift` | → `%LOCALAPPDATA%\Microsoft\WindowsApps` |
| `agtermCore/…/CommandPath.swift` | Windows PATH по умолчанию |
| `agtermCore/…/ZmxSupport.swift`, `RemoteSession.swift` | инертные Windows-альтернативы (`%TEMP%`, `cmd /c`) чтобы компилировались |
| `agtermCore/…/HtmlOverlay.swift` | проверить `NSHomeDirectory()` на Windows, при необходимости ветка |
| `agtermctlKit/SocketClient.swift` | `#elseif canImport(WinSDK)`: winsock2, WSAStartup, connect; без SO_NOSIGPIPE; `ownershipLockHeld` → nil |
| `agtermctlKit/Commands.swift` | socketPath default → LOCALAPPDATA |
| `agtermctlKit/MiscCommands.swift` | `clientPath()` → `Bundle.main.executableURL` ветка |

`Package.swift` правок не требует. Сборка: `swift build -c release --product agtermctl` в `agtermCore/` на Windows (Swift 6.x).

## Фазы (каждая проверяема на Windows-машине)

0. **Spine + golden-фикстуры** (macOS): скелет решения; C# кодек; Swift-генератор фикстур (отдельный SwiftPM-пакет от локального `agtermCore`) → `tests/Golden/*.json`; C# round-trip байт-в-байт. Spike XtermSharp (решающий гейт для §VT). Проверка: `dotnet test` + `swift test` на macOS.
1. **Модель + dispatcher** (macOS): `SessionModel/WorkspaceModel/StoreModel/WindowLibraryModel`, `IControlActions` + dispatcher на весь IN-список, events ring, snapshot codec. Тесты зеркалят Swift `ControlDispatcher*Tests`.
2. **Control server + headless host** (первый Windows-милстоун): listener/ownership/framing; headless-хост со стаб-деревом; сборка `agtermctl.exe`; `version/tree/workspace new/session new/close/events`. Проверка: `--json` байт-сравнение с фикстурами; second-instance refusal.
3. **Терминал, одна поверхность**: ConPTY + XtermSharp + рендер + `TerminalControl`. Проверка: `session.type "echo hi\r"` → `session.text` содержит `hi`; `surface.cursor`; `font.inc`.
4. **Chrome**: сайдбар, workspaces, selection, persistence/restore. Скриптовый проход по всему IN-списку.
5. **Splits + профили оболочек**: split/swap/focus/resize, promote-on-exit, env-parity сквозь swap.
6. **Status/events/themes/window.\***: глиф агента, ring, blocked-owner правило, темы.
7. **E2E + CI + упаковка + docs**: `Agterm.E2E.Tests` (запуск app + agtermctl.exe), `windows-ci.yml`, publish, `windows/README.md` (расхождения: TERM, тосты, `super+`→`ctrl+`, dropped snapshot-поля).

## Верификация

- **macOS (авторинг):** `swift test` в `agtermCore` (регрессия Mac-сборки) + `dotnet test` Core/Control.
- **Windows-машина:** runbook `windows/tools/windows-build.md`: `swift build … --product agtermctl`; `dotnet build windows/Agterm.sln`; `dotnet run --project Agterm.Windows`; E2E-сьют.
- **CI (опционально, ветка):** job `windows-latest` (setup-swift + setup-dotnet 10 + build + test, UI-зависимые ассерты `[SkippableFact]`) + job macOS (`swift test` + `dotnet test` — сигнал, что ветки не сломали Mac).
- **DoD:** полный IN-список через `agtermctl.exe --json` байт-паритет с фикстурами; владение сокетом (`.unavailable` в `AGTERM_SOCKET` второй инстанции); restore layout; env `AGTERM_*` точно по спецификации; `swift test` macOS зелёный на ветке.

## Риски

1. **Swift-on-Windows флейк** (главный) → timebox 2 дня после Phase 2, план Б `Agterm.Ctl` на том же кодеке (паритет по построению).
2. Длинный `sun_path` → гард + `AGTERM_STATE_DIR`; named pipe задокументирован как запас.
3. SwapChainPanel/Direct2D interop → изолировано в `TerminalRenderer`; fallback Win2D-подход.
4. DirectWrite немоно-глифы → позиции ячеек принадлежат строке сетки (не равный advance).
5. XtermSharp-гэты → Phase-0 spike гейтит; форк+патч, upstream first.
6. ConPTY quirks (resize-гонки, wsl UTF-16, первый resize-echo) → юнит-тесты без UI.
7. Дрейф протокола с upstream → golden-фикстуры + двойной CI — сигнализация.

## Опорные файлы

- `agtermCore/Sources/agtermCore/ControlProtocol.swift` — wire-контракт (Command raw values, ControlArgs, конверт `cmd/target/args`)
- `agtermCore/Sources/agtermCore/ControlProjection.swift` — формы `tree`/`window.list` узлов
- `agterm/Control/ControlServer.swift` — ownership/flock, framing, тайминги (порт семантики)
- `agterm/Ghostty/GhosttySurfaceView+IO.swift` — `inject`/`readScreenText`/`readCursorColumn` (паритет `session.type/text`, `surface.cursor`)
- `agterm/Control/ControlServer+SurfaceIO.swift` — правила readback/таргетинга панелей
- `agtermCore/Sources/agtermctlKit/SocketClient.swift` — место WinSDK-ветки (уже есть Darwin/Glibc)
