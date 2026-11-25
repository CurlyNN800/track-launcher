"""
Наводит порядок в «Загрузках»: файлы старше N дней переезжают в Загрузки/_старое/ГГГГ-ММ.
Ничего не удаляет — только перекладывает.

Параметры (input):
    days   — сколько дней файл может лежать, по умолчанию 14
    folder — какую папку разбирать, по умолчанию ~/Downloads
"""

import os
import shutil
import time
from datetime import datetime
from pathlib import Path

from launcher_step import done, fail, read_context


def main() -> None:
    params = read_context()["input"]
    days = int(params.get("days", 14))
    folder = Path(os.path.expandvars(params.get("folder") or "~/Downloads")).expanduser()
    if not folder.is_dir():
        fail(f"Папка не найдена: {folder}")

    archive_root = folder / "_старое"
    limit = time.time() - days * 86400
    moved = 0

    for item in folder.iterdir():
        if item == archive_root or item.name.startswith("."):
            continue
        try:
            mtime = item.stat().st_mtime
        except OSError:
            continue
        if mtime > limit:
            continue

        month_dir = archive_root / datetime.fromtimestamp(mtime).strftime("%Y-%m")
        month_dir.mkdir(parents=True, exist_ok=True)
        target = month_dir / item.name
        n = 2
        while target.exists():
            target = month_dir / f"{item.stem} ({n}){item.suffix}"
            n += 1
        try:
            shutil.move(str(item), str(target))
            moved += 1
        except OSError:
            # Файл открыт в другой программе — пропускаем.
            continue

    done(f"Разложено файлов: {moved}" if moved else "В «Загрузках» и так порядок")


if __name__ == "__main__":
    main()
