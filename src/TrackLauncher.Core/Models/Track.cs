namespace TrackLauncher.Core.Models;

/// <summary>
/// Трек — набор шагов, которые выполняются по порядку при запуске,
/// и шагов, которые выполняются при «Завершить трек».
/// </summary>
public sealed class Track
{
    /// <summary>Идентификатор, он же имя файла: tracks/{id}.json.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Эмодзи или короткий текст для карточки.</summary>
    public string? Icon { get; set; }

    /// <summary>Цвет карточки в формате #rrggbb.</summary>
    public string? Color { get; set; }

    public string? Description { get; set; }

    public List<Step> Steps { get; set; } = [];

    /// <summary>Шаги, которые выполняются при завершении трека (до закрытия окон).</summary>
    public List<Step> OnEnd { get; set; } = [];
}

/// <summary>Куда поставить окно.</summary>
public sealed class Layout
{
    /// <summary>Номер монитора: 0 — основной, дальше слева направо.</summary>
    public int Monitor { get; set; }

    /// <summary>
    /// maximized, left-half, right-half, top-half, bottom-half,
    /// top-left, top-right, bottom-left, bottom-right, center, custom.
    /// </summary>
    public string Position { get; set; } = "maximized";

    // Для custom: координаты относительно рабочей области монитора (без панели задач), в пикселях.
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    public override string ToString() => Position == "custom"
        ? $"монитор {Monitor + 1}, {Width}×{Height} в ({X}, {Y})"
        : $"монитор {Monitor + 1}, {Position}";
}

/// <summary>Как узнать окно, которое открыл шаг.</summary>
public sealed class WindowMatch
{
    /// <summary>Имя процесса без .exe (Unity, Code, chrome). По умолчанию — имя запускаемого файла.</summary>
    public string? Process { get; set; }

    /// <summary>Часть заголовка окна.</summary>
    public string? Title { get; set; }
}
