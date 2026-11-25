"""
Создаёт папку под новую практику: «<prefix> <дата>» внутри base.
Если указан template — копирует в неё содержимое шаблона.

Параметры (input):
    base     — где создавать, например "D:/Study/SQL"            (обязательно)
    prefix   — начало имени папки, по умолчанию "Практика"
    template — папка-шаблон, содержимое которой скопировать     (необязательно)
    open     — открыть созданную папку в проводнике, по умолчанию false
"""

import os
import shutil
from datetime import date
from pathlib import Path

from launcher_step import done, fail, read_context


def main() -> None:
    params = read_context()["input"]
    base = params.get("base")
    if not base:
        fail("Не указан параметр base — где создавать папку")

    base_path = Path(os.path.expandvars(base)).expanduser()
    prefix = params.get("prefix", "Практика")
    target = base_path / f"{prefix} {date.today():%Y-%m-%d}"

    # Если сегодня такая папка уже есть — добавим номер.
    n = 2
    while target.exists():
        target = base_path / f"{prefix} {date.today():%Y-%m-%d} ({n})"
        n += 1

    template = params.get("template")
    if template:
        template_path = Path(os.path.expandvars(template)).expanduser()
        if not template_path.is_dir():
            fail(f"Шаблон не найден: {template_path}")
        shutil.copytree(template_path, target)
    else:
        target.mkdir(parents=True)

    if params.get("open"):
        os.startfile(target)  # type: ignore[attr-defined]  # есть только в Windows

    done(f"Создана папка {target.name}")


if __name__ == "__main__":
    main()
