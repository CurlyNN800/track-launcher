"""
Делает zip-бэкап папки проекта: <dest>/<имя папки>_ГГГГ-ММ-ДД_ЧЧММ.zip
Для Unity по умолчанию пропускает тяжёлые папки, которые Unity пересоздаёт сам.

Параметры (input):
    source  — папка проекта                                    (обязательно)
    dest    — куда класть архивы, по умолчанию <source>/../Backups
    exclude — список имён папок, которые не нужны в архиве
    keep    — сколько последних архивов хранить, по умолчанию 10 (старые удаляются)
"""

import os
import zipfile
from datetime import datetime
from pathlib import Path

from launcher_step import done, fail, read_context

UNITY_JUNK = ["Library", "Temp", "Obj", "obj", "Logs", "Build", "Builds", "UserSettings", ".git", ".vs", ".idea"]


def main() -> None:
    params = read_context()["input"]
    source_raw = params.get("source")
    if not source_raw:
        fail("Не указан параметр source — какую папку бэкапить")

    source = Path(os.path.expandvars(source_raw)).expanduser().resolve()
    if not source.is_dir():
        fail(f"Папка не найдена: {source}")

    dest = Path(os.path.expandvars(params.get("dest") or str(source.parent / "Backups"))).expanduser()
    dest.mkdir(parents=True, exist_ok=True)
    exclude = set(params.get("exclude") or UNITY_JUNK)
    keep = int(params.get("keep", 10))

    archive = dest / f"{source.name}_{datetime.now():%Y-%m-%d_%H%M}.zip"
    files = 0
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as zf:
        for root, dirs, names in os.walk(source):
            dirs[:] = [d for d in dirs if d not in exclude]
            for name in names:
                path = Path(root) / name
                zf.write(path, path.relative_to(source.parent))
                files += 1

    # Ротация: оставляем только последние `keep` архивов этого проекта.
    old = sorted(dest.glob(f"{source.name}_*.zip"), key=lambda p: p.stat().st_mtime, reverse=True)[keep:]
    for path in old:
        path.unlink()

    size_mb = archive.stat().st_size / 1024 / 1024
    done(f"Бэкап {archive.name}: {files} файлов, {size_mb:.1f} МБ")


if __name__ == "__main__":
    main()
