using System.Text;
using System.Text.Json;
using TrackLauncher.Core.Models;

namespace TrackLauncher.Core;

/// <summary>Треки лежат по одному в файле: tracks/{id}.json.</summary>
public sealed class TrackRepository(string directory)
{
    private readonly object _sync = new();

    public string Directory { get; } = directory;

    /// <summary>Файлы, которые не удалось прочитать при последней загрузке, с текстом ошибки.</summary>
    public List<(string File, string Error)> LastErrors { get; } = [];

    public List<Track> LoadAll()
    {
        lock (_sync)
        {
            LastErrors.Clear();
            var tracks = new List<Track>();
            foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json"))
            {
                if (!IsValidId(Path.GetFileNameWithoutExtension(file)))
                {
                    LastErrors.Add((Path.GetFileName(file), "имя файла может содержать только латиницу, цифры, дефис и подчёркивание — переименуй файл"));
                    continue;
                }
                try
                {
                    var track = Json.Read<Track>(file);
                    if (track is null)
                        continue;
                    track.Id = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrWhiteSpace(track.Name))
                        track.Name = track.Id;
                    tracks.Add(track);
                }
                catch (Exception ex)
                {
                    var message = ex is JsonException je ? $"ошибка JSON в строке {je.LineNumber + 1}: {je.Message}" : ex.Message;
                    LastErrors.Add((Path.GetFileName(file), message));
                    Logger.Error($"Не удалось прочитать трек {Path.GetFileName(file)}: {message}");
                }
            }
            return tracks.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    public Track? Get(string id)
    {
        if (!IsValidId(id))
            return null;
        var file = FileFor(id);
        lock (_sync)
        {
            if (!File.Exists(file))
                return null;
            try
            {
                var track = Json.Read<Track>(file);
                if (track is not null)
                    track.Id = id;
                return track;
            }
            catch (Exception ex)
            {
                Logger.Error($"Не удалось прочитать трек {id}", ex);
                return null;
            }
        }
    }

    public bool Exists(string id) => IsValidId(id) && File.Exists(FileFor(id));

    /// <summary>Сохраняет трек. Если у трека нет id — придумывает его из названия.</summary>
    public Track Save(Track track)
    {
        if (string.IsNullOrWhiteSpace(track.Id))
            track.Id = UniqueId(Slugify(track.Name));
        if (!IsValidId(track.Id))
            throw new LauncherException("id трека может содержать только латиницу, цифры, дефис и подчёркивание");

        lock (_sync)
            Json.Write(FileFor(track.Id), track);
        return track;
    }

    public void Delete(string id)
    {
        if (!IsValidId(id))
            return;
        lock (_sync)
        {
            var file = FileFor(id);
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    /// <summary>Свободный id: vesper, vesper-2, vesper-3…</summary>
    public string UniqueId(string baseId)
    {
        var id = string.IsNullOrWhiteSpace(baseId) ? "track" : baseId;
        if (!Exists(id))
            return id;
        for (int i = 2; ; i++)
        {
            var candidate = $"{id}-{i}";
            if (!Exists(candidate))
                return candidate;
        }
    }

    public string FileFor(string id) => Path.Combine(Directory, id + ".json");

    public static bool IsValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 64 && id.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');

    private static readonly Dictionary<char, string> Translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh",
        ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
        ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts",
        ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu",
        ['я'] = "ya",
    };

    /// <summary>«Курсовая Vesper» → kursovaya-vesper</summary>
    public static string Slugify(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
                sb.Append(ch);
            else if (Translit.TryGetValue(ch, out var latin))
                sb.Append(latin);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 48)
            slug = slug[..48].Trim('-');
        return slug.Length == 0 ? $"track-{DateTime.Now:yyyyMMddHHmmss}" : slug;
    }
}
