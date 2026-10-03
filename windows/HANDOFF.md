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

## Статус по фазам

- **Фаза 0 ✅** кодек + 119 golden-фикстур + 127 тестов. **Фаза 1 ✅** dispatcher (pinned строки ошибок).
- **Фаза 2 ✅** сокет-сервер + StoreControlActions + модель + headless-хост; end-to-end smoke по протоколу
  зелёный (macOS). 250 тестов.
- **Swift-ветки ✅** 14 файлов agtermCore + Package.swift (AgtermResponsibility → os(macOS)-блок).
  macOS: `swift build`, `swift build --product agtermctl`, `swift test` — зелёные (4008 тестов).
- **Фаза 3 (начало)** XtermSharp вендорен и КОМПИЛИРУЕТСЯ; ConPTY-слой написан, но **НИ РАЗУ НЕ ЗАПУСКАЛСЯ**.
- **Фазы 4–7** не начаты: WinUI-хром, рендер Direct2D, сплиты в UI, E2E-сьют, CI, упаковка.

## ЧТО ДЕЛАТЬ ДАЛЬШЕ (по порядку)

1. **Верификация Фаз 0–2 на Windows** (runbook: `windows/tools/windows-build.md`):
   - `dotnet build windows/Agterm.sln` и `dotnet test` (должно быть 250/250);
   - `swift build -c release --product agtermctl` в `agtermCore/` — ЭТО ПЕРВЫЙ РЕАЛНЫЙ ЗАПУСК СВИФОВЫХ
     ВЕТОК; возможны ошибки компиляции в `SocketClient.swift` (WinSDK-поверхность: sockaddr_un/AF_UNIX через
     afunix.h, ADDRESS_FAMILY, типы макросов INVALID_SOCKET/SOCK_STREAM/WSAECONNREFUSED,
     `WinSDK.send`/`WinSDK.connect` квалификация, recv-ребинд в CChar) и `MiscCommands.swift`
     (Bundle.main.executableURL для голого exe). Чини на месте, держи macOS-ветки нетронутыми.
   - `dotnet run --project windows/src/Agterm.Headless` + прогони agtermctl.exe version/tree/session
     new/type/text/close (см. runbook). Если сокет не находится — сравни `%LOCALAPPDATA%\agterm`.
2. **Фаза 3 — живой терминал в headless-хосте**: замени EchoSurface на TerminalEmulator (PtySession +
   XtermSharp): заводи `windows/src/Agterm.Headless/` вариант `--live`, строй TerminalSpawn из
   ShellProfiles.Default(), проверь `session.type "echo hi\r"` → `session.text` содержит `hi`,
   resize, выход процесса (промоут при живом сплите — см. StoreControlActions/promote), OSC 7/title
   (PowerShell-профиль-сниппет, эмитящий OSC 7, опционален).
3. **Фаза 4 — WinUI 3**: проект `Agterm.Windows` (unpackaged, WindowsAppSDK), окно: сайдбар TreeView ←
   WindowLibraryModel, SessionDeck (eager deck: все поверхности смонтированы, переключение видимостью),
   SplitHost с ratio 0.05–0.95, акселераторы Ctrl+T/W/D/Tab/1..9/=/-/0, ControlServer на UI-потоке
   (маршал через DispatcherQueue — в ControlServer уже есть делегат-маршал).
4. **Рендер**: SwapChainPanel + D3D11/Direct2D/DirectWrite (Vortice.Windows), глиф-атлас, сетка ячеек,
   font size из TerminalEmulator.CurrentFontSize.
5. **Потом**: persistence-проверка restore-Layout, статусы в UI, темы, E2E-сьют (`tests/Agterm.E2E.Tests`
   запускает app + agtermctl.exe), CI windows-latest, `dotnet publish` self-contained.

## Грабли, на которые уже наступили (не повторяй)

1. **Паритет JSON — семантический, не побайтовый.** Swift `JSONEncoder` даёт НЕдетерминированный порядок
   ключей между процессами (проверено двумя прогонами генератора). Тесты сравнивают канонически
   (`JsonCanonical.AssertEqual`). C# кодек при этом воспроизводит стиль Swift: `\/` экранирование,
   даблы "300.0"/"1e+20" (см. `SwiftDoubleConverter`), null-пропуск.
2. **Socket.Close() дедлок:** закрывать слушающий сокет при висящем блокирующем `Accept()` из другого
   потока НЕЛЬЗЯ (Close ждёт операцию). В `ControlServer.AcceptLoop` — Poll-цикл по 500мс. Не «оптимизируй»
   обратно в блокирующий Accept.
3. **sun_path ≤ 104 байта** (macOS; на Windows AF_UNIX 108). Гард уже стоит; в тестах пути короткие.
4. **Не убий `dotnet` через `pkill -9` без `dotnet build-server shutdown`** — останутся висячие
   MSBuild-ноды и локи, последующие сборки зависают «молча». Лечение: `dotnet build-server shutdown`,
   `rm -rf windows/**/obj windows/**/bin`, запуск заново.
5. **NStack.Core** ставит свои `Rune`/`ustring` в namespace `System` → на net10 коллизия с
   `System.Text.Rune`. В двух файлах Vt/ стоит `using Rune = System.Rune;` — сохраняй при регенерации вендора.
6. **Swift-frontend fatalError без диагностики** на холодной компиляции SessionHostRuntimeTests на этой
   macOS — транзиент окружения; лечится `rm -rf agtermCore/.build` + повтор.
7. **Скрытые на Windows Swift-команды** — `terminfo`, `zmx present`, `session overlay run-job`,
   StreamBridge, OverlayRunJob за `#if !os(Windows)` — это POSIX-сторонние вещи по дизайну, не баги.
8. Bootstrap-инвариант: у стора ВСЕГДА ≥1 workspace (`StoreModel.EnsureBootstrap`). Пустое дерево невалидно.
9. Деферред-команды отвечают `"control dispatcher did not handle <cmd>"` — так и задумано (MVP).
10. Владение сокетом: lock-файл `<socket>.lock` НЕ удалять никогда; отказавший инстанс рекламирует
    `<socket>.unavailable` в AGTERM_SOCKET.

## Конвенции (соблюдать)

- Swift-правки: только узкие `#if os(Windows)`-ветки, macOS-код байт-в-байт тот же; `swift test` в
  `agtermCore` обязателен после любых Swift-правок (на Windows есть Swift-тулчейн — macOS-набор тестов там
  не запустится целиком из-за session-host таргетов, они в os(macOS)-блоке — это нормально; финальную
  проверку macOS-веток делаем позже на маке).
- C#-код в `Agterm.Core`/`Agterm.Control`: строго nullable + TreatWarningsAsErrors; строковые константы
  протокола не менять без сверки со Swift (`ControlProtocol.swift`/`ControlDispatcher.swift`).
- Коммиты: продолжай серию `windows: phase N — …`, в конце `Co-Authored-By: Claude Code <noreply@anthropic.com>`.
- Пуш в `fork` (origin может не быть) — `git push fork windows-port`.

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
