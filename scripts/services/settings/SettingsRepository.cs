using System.Text.Json;
using Godot;
using SolarixFailure.Core;

namespace SolarixFailure;

internal sealed class SettingsRepository
{
    private const int CurrentSchemaVersion = 2;
    private const string DirectoryPath = "user://settings";
    private const string DocumentPath = "user://settings/settings.json";
    private const string BackupPath = "user://settings/settings.json.bak";
    private const string TemporaryPath = "user://settings/settings.json.tmp";

    public SettingsDocument LoadOrCreate()
    {
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(DirectoryPath));
        string target = ProjectSettings.GlobalizePath(DocumentPath);
        string backup = ProjectSettings.GlobalizePath(BackupPath);
        if (TryLoad(target, out SettingsDocument? document))
            return Normalize(document!);
        if (TryLoad(backup, out document))
        {
            PreserveCorrupt(target);
            SettingsDocument recovered = Normalize(document!);
            Save(recovered);
            return recovered;
        }
        PreserveCorrupt(target);
        SettingsDocument created = Normalize(new SettingsDocument());
        Save(created);
        return created;
    }

    public bool Save(SettingsDocument document)
    {
        try
        {
            Directory.CreateDirectory(ProjectSettings.GlobalizePath(DirectoryPath));
            string target = ProjectSettings.GlobalizePath(DocumentPath);
            string backup = ProjectSettings.GlobalizePath(BackupPath);
            string temporary = ProjectSettings.GlobalizePath(TemporaryPath);
            document.SchemaVersion = CurrentSchemaVersion;
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, SettingsJson.Options));
            if (File.Exists(target))
                File.Copy(target, backup, true);
            File.Move(temporary, target, true);
            return true;
        }
        catch (Exception exception)
        {
            GD.PushError($"Unable to save settings: {exception.Message}");
            return false;
        }
    }

    private static bool TryLoad(string path, out SettingsDocument? document)
    {
        document = null;
        if (!File.Exists(path))
            return false;
        try
        {
            document = JsonSerializer.Deserialize<SettingsDocument>(
                File.ReadAllText(path), SettingsJson.Options);
            return document is not null && document.SchemaVersion is >= 1 and <= CurrentSchemaVersion;
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Settings load failed for '{path}': {exception.Message}");
            return false;
        }
    }

    private static SettingsDocument Normalize(SettingsDocument document)
    {
        document.SchemaVersion = CurrentSchemaVersion;
        document.Profiles = new Dictionary<string, SettingsProfile>(
            document.Profiles ?? new Dictionary<string, SettingsProfile>(),
            StringComparer.OrdinalIgnoreCase);
        if (!document.Profiles.ContainsKey(SettingsDefaults.DefaultProfileName))
            document.Profiles[SettingsDefaults.DefaultProfileName] = new SettingsProfile();
        if (!document.Profiles.ContainsKey(document.CurrentProfile))
            document.CurrentProfile = SettingsDefaults.DefaultProfileName;
        foreach (SettingsProfile profile in document.Profiles.Values)
        {
            profile.Settings ??= new GameSettings();
            profile.Bindings ??= SettingsDefaults.CreateBindings();
            profile.Settings.Gameplay.AutoSaveRetention =
                Math.Clamp(profile.Settings.Gameplay.AutoSaveRetention, 1, 10);
        }
        return document;
    }

    private static void PreserveCorrupt(string path)
    {
        if (!File.Exists(path))
            return;
        try
        {
            File.Move(path, $"{path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}", true);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Could not preserve corrupt settings: {exception.Message}");
        }
    }
}
