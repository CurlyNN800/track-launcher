# Лаунчер треков

Приложение для Windows. При включении компа оно показывает карточки «треков». **Трек** — это сценарий работы: одной кнопкой открываются нужные программы, проекты, сайты и папки, окна сами встают по местам, а запускаются команды, Python-скрипты и таймеры. Когда закончил, нажимаешь «Завершить трек»: лаунчер закрывает всё, что открыл, и записывает время в статистику.

```
┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│ 🥽 Vesper    │   │ 🗄️ SQL       │   │ 🌅 Утро      │
│ Unity + Code │   │ SSMS + доки  │   │ План на день │
│ [▶ Запустить]│   │ [▶ Запустить]│   │ [▶ Запустить]│
└──────────────┘   └──────────────┘   └──────────────┘
```

## Возможности

- **Треки из шагов**: программа, сайты, папка, команда, Python-скрипт, пауза, уведомление, таймер, вложенный трек.
- **Глубокие ссылки**: Unity сразу с нужным проектом (`-projectPath`), VS Code с папкой, Chrome с нужным профилем в отдельном окне.
- **Раскладка окон**: половины, четверти, «развернуть» на нужном мониторе или свои координаты. Учитываются DPI и невидимые рамки Windows 10/11.
- **«Записать раскладку»**: расставь окна руками, нажми кнопку, и в следующий раз они встанут так же.
- **«Завершить трек»**: окна закрываются как по крестику (программы успевают спросить «Сохранить?»). Фоновые команды вроде `npm run dev` останавливаются. Окна, которые были открыты до трека, не трогаются.
- **Шаги при завершении**: бэкап проекта, напоминание про коммит и т. п.
- **Статистика**: часы по трекам и дням, самая длинная сессия, серия дней подряд, экспорт в CSV. Если комп выключили без «Завершить», сессия закрывается по последней отметке «ещё жив».
- **Конструктор** с перетаскиванием шагов, проверкой каждого шага кнопкой ▶ и подсветкой ошибок.
- Автозапуск с Windows, значок в трее, глобальная горячая клавиша (по умолчанию `Ctrl+Alt+Space`), клавиши `1–9` запускают треки.
- Импорт и экспорт треков в JSON, чтобы делиться с одногруппниками.

## Стек

| Часть | Технология | Где |
| --- | --- | --- |
| Ядро | C# / .NET 10 | `src/TrackLauncher.Core` — треки, шаги, Win32, сессии, статистика |
| Приложение | WPF + WebView2 | `src/TrackLauncher.App` — окно, трей, горячая клавиша, мост JS ↔ C# |
| Интерфейс | HTML / CSS / JS без фреймворков | `src/TrackLauncher.App/ui` |
| Скрипты-шаги | Python 3 | `samples/scripts` |
| Хранение | JSON (треки, настройки), SQLite (статистика) | `%AppData%\TrackLauncher` |

## Установка

**Готовая сборка.** Скачай `TrackLauncher-win-x64.zip` со страницы Releases (или из артефактов GitHub Actions), распакуй куда угодно и запусти `TrackLauncher.exe`. Сборка самодостаточная: .NET ставить не нужно. Нужен WebView2 Runtime, но в Windows 10/11 он обычно уже стоит.

**Из исходников.**

```powershell
# нужен .NET 10 SDK: https://dotnet.microsoft.com/download
git clone https://github.com/<твой-ник>/track-launcher
cd track-launcher
dotnet run --project src/TrackLauncher.App
```

Или открой `TrackLauncher.slnx` в Visual Studio 2022 (17.13+) / Rider и запусти `TrackLauncher.App`.

Релизная сборка в папку:

```powershell
dotnet publish src/TrackLauncher.App -c Release -r win-x64 --self-contained true -o publish
```

При первом запуске в `%AppData%\TrackLauncher\tracks` появятся три примера: **Vesper**, **SQL практика** и **Утро**. Пути в них условные (`D:\Projects\Vesper`), поправь их под себя в конструкторе.

## Где что лежит

```
%AppData%\TrackLauncher\
├─ tracks\          ← треки, по файлу на трек (можно править руками)
├─ scripts\         ← Python-скрипты для шагов
├─ logs\            ← журнал по дням
├─ settings.json    ← настройки
├─ session.json     ← идущий сейчас трек (чтобы «Завершить» работал после перезапуска лаунчера)
└─ stats.db         ← статистика (SQLite)
```

## Формат трека

```json
{
  "name": "Курсовая Vesper",
  "icon": "🥽",
  "color": "#7c5cff",
  "description": "Unity слева, VS Code справа",
  "steps": [
    { "type": "command", "command": "git pull", "workingDir": "D:\\Projects\\Vesper" },
    {
      "type": "app",
      "path": "%ProgramFiles%\\Unity\\Hub\\Editor\\6000.0.30f1\\Editor\\Unity.exe",
      "args": "-projectPath \"D:\\Projects\\Vesper\"",
      "windowMatch": { "title": "Vesper" },
      "windowTimeoutSec": 180,
      "layout": { "monitor": 0, "position": "left-half" }
    },
    { "type": "timer", "minutes": 50, "message": "Перерыв!" }
  ],
  "onEnd": [
    { "type": "notify", "message": "Не забудь закоммитить" }
  ]
}
```

В путях работают переменные окружения (`%LocalAppData%`, `%ProgramFiles%`) и `~`.

### Типы шагов

| `type` | Что делает | Основные поля |
| --- | --- | --- |
| `app` | Запускает программу, ждёт её окно, ставит по раскладке | `path`, `args`, `workingDir`, `layout`, `windowMatch {process, title}`, `windowTimeoutSec` (60), `settleMs` (1000), `blocking`, `keepOpen` |
| `url` | Открывает сайты. С `browser` — в отдельном окне, которое потом закроется | `urls[]`, `browser` (`chrome`, `edge`, `firefox`, `brave`, `yandex`, `opera` или путь к exe), `profile`, `newWindow` (true), `layout`, `keepOpen` |
| `folder` | Открывает папку в проводнике | `path`, `layout`, `keepOpen` |
| `command` | Команда `cmd` | `command`, `workingDir`, `wait` (true), `showWindow`, `timeoutSec` (120) |
| `python` | Python-скрипт из папки `scripts` | `script`, `input` (любой JSON), `args`, `timeoutSec` (60) |
| `wait` | Пауза | `seconds` |
| `notify` | Уведомление Windows | `title`, `message` |
| `timer` | Напоминание через N минут | `minutes`, `message` |
| `track` | Выполнить шаги другого трека | `trackId` |

Общие поля у всех шагов: `label` (подпись), `enabled` (true), `continueOnError` (true; при false трек остановится, если шаг упал).

### Раскладка (`layout`)

`monitor`: 0 — основной монитор, дальше слева направо.
`position`: `maximized`, `left-half`, `right-half`, `top-half`, `bottom-half`, `top-left`, `top-right`, `bottom-left`, `bottom-right`, `center` или `custom`. Для `custom` нужны ещё `x`, `y`, `width`, `height` в пикселях относительно рабочей области монитора.

## Python-шаги

Лаунчер запускает скрипт и пишет ему в stdin JSON:

```json
{ "track": "vesper", "trackName": "Курсовая Vesper", "phase": "start", "scriptsDir": "...", "input": { ... } }
```

Скрипт отвечает **последней строкой** stdout:

```json
{ "ok": true, "message": "Бэкап готов" }
```

Чтобы не писать это руками, есть помощник `launcher_step.py`:

```python
from launcher_step import read_context, done, fail

params = read_context()["input"]
if "source" not in params:
    fail("Не указан source")
done("Готово")
```

Готовые скрипты:

- `backup_project.py` — zip-бэкап проекта с датой, без папок `Library`/`Temp` и т. п.; хранит последние N архивов.
- `new_practice_folder.py` — создаёт папку «Практика ГГГГ-ММ-ДД», при желании из шаблона.
- `tidy_downloads.py` — переносит старые файлы из «Загрузок» в `_старое/ГГГГ-ММ`. Ничего не удаляет.

Путь к Python задаётся в настройках (`python`, `py` или полный путь), там же есть кнопка «Проверить».

## Как это устроено

```
 WebView2 (ui/*.html, js)  ──postMessage {id, action, payload}──▶  Bridge.cs
            ▲                                                           │
            └──────── {id, ok, data} / события {event, data} ◀──────────┤
                                                                        ▼
                                                    TrackEngine (Core)
                                     ┌──────────────┬──────────────┬──────────────┐
                                     │ Steps        │ WindowManager│ StatsStore   │
                                     │ app/url/...  │ Win32 P/Invoke│ SQLite      │
                                     └──────┬───────┴──────────────┴──────────────┘
                                            │ python.exe + stdin/stdout JSON
                                            ▼
                                      scripts/*.py
```

- **Как лаунчер узнаёт «своё» окно.** Перед запуском шага он запоминает все окна. Потом ждёт новое окно с нужным именем процесса (и, если задано, с частью заголовка). Так работают даже программы, которые запускают другой процесс и закрываются сами (Chrome, Unity Hub, Discord). Если программа уже была открыта и нового окна не создала, лаунчер расставит старое окно, но закрывать его не будет.
- **Закрытие** идёт через `WM_CLOSE`, как крестик. Процессы не убиваются, кроме фоновых команд (`command` с `wait: false`), которые запустил сам трек.
- **Сессия** пишется в `session.json`, поэтому лаунчер можно перезапустить посреди трека, и «Завершить» всё равно закроет нужные окна.

### Как добавить свой тип шага

1. Класс-наследник `Step` (или `WindowStep`) в `src/TrackLauncher.Core/Models/Steps.cs` с методами `ExecuteAsync` и `Describe`.
2. Строчка `[JsonDerivedType(typeof(MyStep), "my")]` над классом `Step`.
3. Описание полей в `src/TrackLauncher.App/ui/js/schema.js`, и шаг появится в конструкторе.

### Вёрстка интерфейса без C#

`ui` открывается и в обычном браузере: вместо C# включается заглушка `mock.js` с демо-треками.

```powershell
cd src/TrackLauncher.App/ui
python -m http.server 8080   # и открыть http://localhost:8080
```

В Debug-сборке внутри лаунчера работают DevTools (F12).

## Ограничения

- Окна программ, запущенных **от администратора**, лаунчер (обычный процесс) двигать и закрывать не может: это защита Windows (UIPI).
- Вкладки браузера по отдельности закрыть нельзя, поэтому сайты трека открываются в отдельном окне (`browser` + `newWindow`). Сайты в «браузере по умолчанию» не закрываются.
- Одновременно идёт один трек. При запуске другого лаунчер предложит завершить текущий.
- Только Windows 10/11.

## Дальше

Идеи в [BACKLOG.md](BACKLOG.md): MCP-сервер («Claude, запусти трек Vesper»), виртуальные рабочие столы, «Не беспокоить», громкость, интеграция с TickTick.

## Лицензия

MIT — см. [LICENSE](LICENSE).
