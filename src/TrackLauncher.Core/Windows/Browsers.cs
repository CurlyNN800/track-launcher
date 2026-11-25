namespace TrackLauncher.Core.Windows;

/// <summary>Короткие имена браузеров → путь к exe.</summary>
public static class Browsers
{
    private static readonly Dictionary<string, string[]> KnownPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] =
        [
            @"%ProgramFiles%\Google\Chrome\Application\chrome.exe",
            @"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe",
            @"%LocalAppData%\Google\Chrome\Application\chrome.exe",
        ],
        ["edge"] =
        [
            @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe",
            @"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe",
        ],
        ["firefox"] =
        [
            @"%ProgramFiles%\Mozilla Firefox\firefox.exe",
            @"%ProgramFiles(x86)%\Mozilla Firefox\firefox.exe",
        ],
        ["brave"] =
        [
            @"%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe",
            @"%LocalAppData%\BraveSoftware\Brave-Browser\Application\brave.exe",
        ],
        ["yandex"] =
        [
            @"%LocalAppData%\Yandex\YandexBrowser\Application\browser.exe",
        ],
        ["opera"] =
        [
            @"%LocalAppData%\Programs\Opera\opera.exe",
        ],
    };

    public static IEnumerable<string> ShortNames => KnownPaths.Keys;

    /// <summary>Возвращает путь к exe или null, если браузер не установлен.</summary>
    public static string? Resolve(string browser)
    {
        var value = browser.Trim();
        if (KnownPaths.TryGetValue(value, out var candidates))
            return candidates.Select(PathUtil.Expand).FirstOrDefault(File.Exists);

        var path = PathUtil.Expand(value);
        return File.Exists(path) ? path : null;
    }
}
