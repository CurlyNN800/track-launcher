using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrackLauncher.Core;

public static class Json
{
    /// <summary>Единые настройки JSON для файлов треков, настроек и общения с интерфейсом.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // "type" у шага может стоять не первым полем (так удобнее писать JSON руками).
        AllowOutOfOrderMetadataProperties = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        // Кириллица в файлах остаётся читаемой, а не К...
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static T? Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);

    /// <summary>Пишет файл атомарно: сначала во временный, потом подменяет.</summary>
    public static void Write<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
