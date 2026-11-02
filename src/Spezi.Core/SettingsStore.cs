using System.Text.Json;

namespace Spezi.Core;

public static class SettingsStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spezi",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return Deserialize(json);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Settings could not be loaded. Defaults are used.", ex);
        }

        var created = new AppSettings();
        created.Clamp();
        return created;
    }

    public static void Save(AppSettings settings)
    {
        settings.Clamp();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var json = JsonSerializer.Serialize(settings, Options);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, FilePath, true);
    }

    internal static AppSettings Deserialize(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            var properties = document.RootElement.EnumerateObject().ToArray();
            if (!properties.Any(p => p.Name.Equals(nameof(AppSettings.ReplaySeconds), StringComparison.OrdinalIgnoreCase)))
            {
                var minutes = properties.FirstOrDefault(p => p.Name.Equals("ReplayMinutes", StringComparison.OrdinalIgnoreCase));
                if (minutes.Value.ValueKind == JsonValueKind.Number && minutes.Value.TryGetInt32(out var legacyMinutes))
                    settings.ReplaySeconds = Math.Clamp(legacyMinutes, 1, 5) * 60;
            }
        }
        settings.ReplayHotkey ??= new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x77);
        settings.RecordHotkey ??= new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x78);
        settings.OutputDeviceIds ??= [];
        settings.DeviceGains ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        settings.Clamp();
        return settings;
    }
}
