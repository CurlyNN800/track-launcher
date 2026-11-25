"""
Помощник для Python-шагов лаунчера треков.

Лаунчер запускает скрипт и пишет в stdin JSON:
    {"track": "vesper", "trackName": "Курсовая Vesper", "phase": "start" | "end",
     "scriptsDir": "...", "input": {...параметры из конструктора...}}

Скрипт отвечает последней строкой stdout:
    {"ok": true, "message": "Что получилось"}

Использование:
    from launcher_step import read_context, done, fail

    ctx = read_context()
    params = ctx["input"]
    ...
    done("Готово")
"""

import json
import sys


def read_context() -> dict:
    """Читает JSON от лаунчера. Если скрипт запущен руками (без stdin) — вернёт пустой контекст."""
    if sys.stdin is None or sys.stdin.isatty():
        return {"track": None, "trackName": None, "phase": "manual", "input": {}}
    raw = sys.stdin.read().strip()
    data = json.loads(raw) if raw else {}
    if not isinstance(data.get("input"), dict):
        data["input"] = {}
    return data


def done(message: str = "Готово") -> None:
    print(json.dumps({"ok": True, "message": message}, ensure_ascii=False), flush=True)
    sys.exit(0)


def fail(message: str) -> None:
    print(json.dumps({"ok": False, "message": message}, ensure_ascii=False), flush=True)
    sys.exit(1)
