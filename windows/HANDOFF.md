# agterm → Windows: handoff context

**Прочитай этот файл целиком перед тем, как что-либо делать.** Он написан для продолжения работы на
Windows-машине; вся работа до сих пор велась с macOS (авторинг + тесты), и часть кода ещё ни разу не
запускалась на Windows.

- **Форк:** https://github.com/mukhanov/agterm (от umputun/agterm)
- **Ветка:** `windows-port` (в форке; `master` зеркалирует upstream)
- **Коммит на момент хендаффа:** см. `git log --oneline -6` — серия `windows: phase 0/1/2/3(start)` + `agtermCore: compile on Windows`
- **План (полный):** `windows/PLAN.md` рядом с этим файлом; прогрессы и ловушки — ниже.

## Что это за проект

Порт agterm — нативного macOS-терминала umputun'а для параллельной работы с coding-агентами — на Windows.
UI macOS (SwiftUI/AppKit) и движок libghostty на Windows не существуют, поэтому порт идёт по образцу
официального форка agterm-linux: **Swift-пакет `agtermCore` остаётся контрактом и источником `agtermctl.exe`**
(Swift-on-Windows ветки уже сделаны), а нативный фронтенд пишется на **C#/.NET 10** в `windows/`.
Стек: WinUI 3 (хром) + XtermSharp (VT-движок, вендорен) + ConPTY (pty) + Direct2D/DirectWrite (рендер,
Vortice) — когда дойдём до рендера.

Решения пользователя (утверждены): C#/.NET 10; объём MVP (workspaces/sessions/splits/профили
оболочек/control-протокол+agtermctl/persistence/restore-Layout/базовые темы; БЕЗ zmx, remote, HTML-оверлеев,
hooks, quick terminal, dashboard); верификация на Windows-машине.

## Архитектура (что уже есть)

```
agtermCore/            Swift-пакет: wire-контракт + agtermctl (ветки os(Windows) сделаны, macOS 4008 тестов зелёные)
windows/
  Agterm.sln
  src/Agterm.Core/     модель+кодек, net10.0 (собирается и тестируется на ЛЮБОЙ ОС)
    Protocol/          ControlCommand/ControlArgs/Request/Response/Tree/Events — паритет со Swift по ИМЕНАМ
    Control/           ControlDispatcher (порт валидации и строк ошибок macOS), ControlResolve, KeystrokeSegments,
                       StoreControlActions (реализация IControlActions над моделью), ControlJson
    Model/             SessionModel/WorkspaceModel/StoreModel/WindowLibraryModel/ControlEventRing/SnapshotStore
  src/Agterm.Control/  AF_UNIX сокет-сервер: владение (FileStream-lock вместо flock), framing, таймауты
  src/Agterm.Terminal/ ВЕНДОРЕН XtermSharp @1bed529 (Vt/, OSC 7 патч), Pty/ConPty.cs + PtySession (ConPTY),
                       TerminalEmulator (адаптер IPaneSurface), ShellProfiles (PowerShell/cmd/GitBash/WSL)
  src/Agterm.Headless/ ГОТОВЫЙ хост без UI: echo-панели, весь протокол — ПЕРВАЯ ЦЕЛЬ ПРОВЕРКИ на Windows
  tests/Agterm.Core.Tests/  250 тестов: golden-фикстуры (119, сгенерированы Swift-кодеком), dispatcher, model, socket
  tools/golden/        Swift-генератор golden-фикстур (запускать только на macOS)
  tools/windows-build.md  runbook сборки
```

Транспорт и паритет: newline-JSON через AF_UNIX, путь `%LOCALAPPDATA%\agterm\agterm.sock`, прецеденция
`--socket` → `AGTERM_CONTROL_SOCKET` → `<AGTERM_STATE_DIR>/agterm.sock` → LOCALAPPDATA (обе стороны
считают одинаково). UUID uppercase. `TERM=xterm-256color` (задокументированное расхождение с
`xterm-ghostty`). Env панелей: AGTERM_ENABLED/SESSION_ID/SOCKET/WINDOW_ID/WORKSPACE_ID/PANE/PANE_ID,
TERM_PROGRAM=agterm.

## Статус по фазам (обновлено 2026-10-03, Windows-машина)

- **Фазы 0–2 ✅ и ВЕРИФИЦИРОВАНЫ на Windows**: dotnet build 0 ошибок, 250/250 тестов, echo-режим
  end-to-end (version/new/type/text/tree/close) через живой сокет.
- **Swift-ветки ✅** на macOS (4008 тестов), но на Windows НЕ СОБИРАЛИСЬ: тулчейн не установлен.
- **Фаза 3 ✅ и ВЕРИФИЦИРОВАНА на Windows**: `--live` headless — реальные cmd.exe через ConPTY +
  XtermSharp; type→text round-trip, сплит, ввод в правую панель, выход сплита, промоут выжившего,
  закрытие сессии при выходе без сплита — всё зелёное (smoke: `C:\Users\nikol\agterm-win-dl\smoke\run-smoke.sh`).
- **Фазы 4–7** не начаты: WinUI-хром, рендер Direct2D, сплиты в UI, E2E-сьют, CI, упаковка.

### Что сделано/починено в Фазе 3 (не повторяй)

1. ConPTY-attach: `UpdateProcThreadAttribute` для PSEUDOCONSOLE берёт **сам HPCON значением**, не
   указателем на него — указатель = 0xC0000142 у ребёнка.
2. `STARTF_USESTDHANDLES` + `INVALID_HANDLE_VALUE` на три stdio (рецепт node-pty): без этого ребёнок
   наследует redirected stdio хоста и экранный текст НЕ идёт в пайп (только init-кадр ~119 байт).
3. Кик `ResizePseudoConsole` сразу после CreateProcessW: без первого ресайза кадр не эмитится.
4. Teardown: sync ReadFile не просыпается от закрытия хэндла, а ClosePseudoConsole ждёт осушения —
   TerminateProcess → `CancelIoEx` → join помпы → закрыть хэндлы, иначе вечный дедлок.
5. Коллизия Rune (NStack кладёт свой Rune в namespace System): `CharData.Rune.ToString()` паддовал
   каждую ячейку пробелами. Алиас `System.Text.Rune` в CharData.cs + правки BufferLine/InputHandler/
   SelectionService/TerminalBufferManipulation (полная квалификация `new System.Text.Rune(...)`).
6. `PtySession.Write` DllImport требует `EntryPoint = "WriteFile"` (локальное имя WindowsWriteFile).
7. Паритет: отсутствующий target в ResolveSession теперь подставляет "active" (как macOS), а не "".
8. Тест `SocketPathDerivation` переведён на `Path.Combine` (POSIX-слэши на Windows не проходят).
9. HandlePaneExit: выход сплита — teardown+очистка; выход primary при живом сплите — промоут
   (свап cwd/title/initial, PromoteToPrimaryPane, teardown умершего); повторная доставка Exited от
   уже снятой поверхности игнорируется (иначе закрытие всей сессии).
10. Тулчейны машины: .NET SDK 10.0.401 user-local в `C:\Users\nikol\dotnet` (нужен DOTNET_ROOT
    на каждый шелл); MSVC BuildTools 18/2022 + WinSDK 10.0.26100 уже стоят. Swift 6.4.0 installer
    скачан в `C:\Users\nikol\agterm-win-dl\swift-installer.exe`, но `/S` без elevation молча
    выходит — установка ЖДЁТ интерактивного запуска с UAC-подтверждением.

## ЧТО ДЕЛАТЬ ДАЛЬШЕ (по порядку; обновлено 2026-10-03)

1. **Установить Swift-тулчейн** (блокер для agtermctl.exe): запустить
   `C:\Users\nikol\agterm-win-dl\swift-installer.exe` интерактивно, подтвердить UAC; проверить
   `C:\Library\Swift-development\bin\swift.exe --version`. Затем первый запуск свифтовых веток:
   `swift build -c release --product agtermctl` в `agtermCore/` — возможны ошибки в `SocketClient.swift`
   (WinSDK-поверхность: sockaddr_un/afunix.h, ADDRESS_FAMILY, INVALID_SOCKET/SOCK_STREAM/WSAECONNREFUSED,
   квалификация `WinSDK.send`/`WinSDK.connect`) и `MiscCommands.swift`. Чинить узкими os(Windows)-ветками.
2. **agtermctl.exe end-to-end против headless**: version/tree/session new/type/text/close по runbook
   (`windows/tools/windows-build.md`); сокет изолированным путём. После этого agtermctl-паритет
   доказан и Фаза 3 закрыта полностью.
3. **Фаза 4 — WinUI 3**: проект `Agterm.Windows` (unpackaged, WindowsAppSDK), окно: сайдбар TreeView ←
   WindowLibraryModel, SessionDeck (eager deck), SplitHost с ratio 0.05–0.95, акселераторы
   Ctrl+T/W/D/Tab/1..9/=/-/0, ControlServer на UI-потоке (делегат-маршал в ControlServer уже есть).
4. **Рендер**: SwapChainPanel + D3D11/Direct2D/DirectWrite (Vortice.Windows), глиф-атлас, сетка ячеек,
   font size из TerminalEmulator.CurrentFontSize.
5. **Потом**: persistence-проверка restore-Layout, статусы в UI, темы, E2E-сьют, CI windows-latest,
   `dotnet publish` self-contained.

## Быстрые команды (Windows, PowerShell)

```powershell
git clone https://github.com/mukhanov/agterm; cd agterm; git checkout windows-port
dotnet build windows\Agterm.sln
dotnet test windows\Agterm.Core.Tests
dotnet run --project windows\src\Agterm.Headless
cd agtermCore; swift build -c release --product agtermctl
# доказательство паритета: запусти headless, потом
..\.build\release\agtermctl.exe version / tree / session new / session type --text "echo hi`r" / session text
```

## Исходный план (конденсированный; полный — windows/PLAN.md)

МVP-список команд протокола (IN): version, tree, events.read; workspace.new/rename/delete/select/go/move/
focus/filter/collapse/expand; session.new/duplicate/close/select/reveal/go/rename/move/type/text/status/
flag/seen/context/split/split.close/swap/focus/resize/copy/paste/selectall; surface.cursor; font.inc/dec/
reset; window.new/list/select/go/close/rename/delete/resize/move/minimize/zoom/fullscreen; sidebar(+width);
theme.set/list; notify. Всё остальное — deferred. Пиннинги строк/порядка валидации: см.
`windows/src/Agterm.Core/Control/IControlActions.cs` (порт ControlDispatcher.swift, порядок проверок
байт-в-байт) и тесты DispatcherTests.
