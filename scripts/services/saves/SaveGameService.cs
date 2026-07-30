using System.Text.Json;
using Godot;
using SolarixFailure.Core;
using SolarixFailure.Domain;

namespace SolarixFailure;

public partial class SaveGameService : Node
{
    private const string SaveDirectory = "user://saves";
    private const string IndexPath = "user://saves/index.json";
    private static readonly JsonSerializerOptions JsonOptions = new(SettingsJson.Options);

    public static SaveGameService Instance { get; private set; } = null!;
    public event Action? SaveIndexChanged;

    private readonly Dictionary<Guid, SaveGameDocument> _documents = [];
    private Godot.Timer _autosaveTimer = null!;
    private DateTimeOffset _sessionStartedUtc;

    public SaveGameDocument? ActiveSession { get; private set; }
    public bool HasValidSaves => _documents.Count > 0;

    public override void _EnterTree()
    {
        if (Instance is not null && IsInstanceValid(Instance) && Instance != this)
        {
            QueueFree();
            return;
        }
        Instance = this;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        LoadIndexOrRebuild();
        _autosaveTimer = new Godot.Timer { OneShot = false, ProcessMode = ProcessModeEnum.Always };
        _autosaveTimer.Timeout += PeriodicAutosave;
        AddChild(_autosaveTimer);
        SettingsService.Instance.SettingsChanged += SettingsChanged;
        SettingsService.Instance.ProfileChanged += _ => RefreshAutosaveTimer();
        RefreshAutosaveTimer();
    }

    public SaveGameDocument StartNewRun()
    {
        _sessionStartedUtc = DateTimeOffset.UtcNow;
        ActiveSession = NewDocument(SaveKind.Autosave, "New Game");
        ActiveSession.RunId = Guid.NewGuid();
        ActiveSession.Stage = GameFlowStage.StoryPrologue;
        ActiveSession.StoryPanelIndex = 0;
        WriteAutosave("New Game");
        RefreshAutosaveTimer();
        return ActiveSession;
    }

    public IReadOnlyList<SaveGameSummary> ListSaves(SaveQuery query = default) =>
        _documents.Values
            .Where(save => query.RunId is null || save.RunId == query.RunId)
            .Where(save => query.Kind is null || save.Kind == query.Kind)
            .OrderByDescending(save => save.UpdatedUtc)
            .Select(ToSummary)
            .ToArray();

    public SaveGameDocument? GetMostRecent() =>
        _documents.Values.OrderByDescending(save => save.UpdatedUtc).FirstOrDefault();

    public bool Load(Guid saveId)
    {
        if (!_documents.TryGetValue(saveId, out SaveGameDocument? document))
            return false;
        ActiveSession = Clone(document);
        _sessionStartedUtc = DateTimeOffset.UtcNow;
        RefreshAutosaveTimer();
        return true;
    }

    public bool CreateManualSave(string requestedName, out SaveGameSummary? summary, out string error)
    {
        summary = null;
        if (ActiveSession is null)
        {
            error = "No active game session.";
            return false;
        }

        string name = requestedName.Trim();
        if (name.Length is < 1 or > 48)
        {
            error = "Save names must contain 1–48 characters.";
            return false;
        }
        if (_documents.Values.Any(save => save.Kind == SaveKind.Manual
            && save.RunId == ActiveSession.RunId
            && string.Equals(save.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = "A manual save with that name already exists in this run.";
            return false;
        }

        SaveGameDocument document = Clone(ActiveSession);
        document.SaveId = Guid.NewGuid();
        document.Kind = SaveKind.Manual;
        document.DisplayName = name;
        document.CreatedUtc = DateTimeOffset.UtcNow;
        document.UpdatedUtc = document.CreatedUtc;
        CapturePlayedTime(document);
        if (!WriteDocument(document))
        {
            error = "The save could not be written.";
            return false;
        }
        _documents[document.SaveId] = document;
        WriteIndex();
        SaveIndexChanged?.Invoke();
        summary = ToSummary(document);
        error = string.Empty;
        return true;
    }

    public bool RenameManualSave(Guid saveId, string requestedName, out string error)
    {
        if (!_documents.TryGetValue(saveId, out SaveGameDocument? document)
            || document.Kind != SaveKind.Manual)
        {
            error = "Only manual saves can be renamed.";
            return false;
        }
        string name = requestedName.Trim();
        if (name.Length is < 1 or > 48)
        {
            error = "Save names must contain 1–48 characters.";
            return false;
        }
        if (_documents.Values.Any(save => save.SaveId != saveId
            && save.Kind == SaveKind.Manual
            && save.RunId == document.RunId
            && string.Equals(save.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = "A manual save with that name already exists in this run.";
            return false;
        }
        document.DisplayName = name;
        document.UpdatedUtc = DateTimeOffset.UtcNow;
        bool saved = WriteDocument(document);
        if (saved)
            SaveIndexChanged?.Invoke();
        error = saved ? string.Empty : "The save could not be renamed.";
        return saved;
    }

    public bool DeleteSave(Guid saveId)
    {
        if (!_documents.Remove(saveId))
            return false;
        string path = SavePath(saveId);
        if (File.Exists(path))
            File.Delete(path);
        if (File.Exists($"{path}.bak"))
            File.Delete($"{path}.bak");
        WriteIndex();
        SaveIndexChanged?.Invoke();
        return true;
    }

    public bool WriteCheckpoint(GameFlowStage stage, int storyPanelIndex = 0,
        CharacterOrigin origin = CharacterOrigin.None, string label = "Autosave")
    {
        if (ActiveSession is null)
            return false;
        ActiveSession.Stage = stage;
        ActiveSession.StoryPanelIndex = storyPanelIndex;
        ActiveSession.Origin = origin;
        return WriteAutosave(label);
    }

    private bool WriteAutosave(string label)
    {
        if (ActiveSession is null)
            return false;
        SaveGameDocument document = Clone(ActiveSession);
        document.SaveId = Guid.NewGuid();
        document.Kind = SaveKind.Autosave;
        document.DisplayName = label;
        document.CreatedUtc = DateTimeOffset.UtcNow;
        document.UpdatedUtc = document.CreatedUtc;
        CapturePlayedTime(document);
        if (!WriteDocument(document))
            return false;
        _documents[document.SaveId] = document;
        PruneAutosaves();
        WriteIndex();
        SaveIndexChanged?.Invoke();
        return true;
    }

    private void PeriodicAutosave()
    {
        if (SettingsService.Instance.AutoSaveEnabled && ActiveSession is not null)
            WriteAutosave("Periodic Autosave");
    }

    private void SettingsChanged(string reason)
    {
        if (reason is "commit" or "rollback")
            RefreshAutosaveTimer();
    }

    private void RefreshAutosaveTimer()
    {
        _autosaveTimer.WaitTime = Math.Clamp(SettingsService.Instance.AutoSaveIntervalSeconds, 60, 3600);
        if (SettingsService.Instance.AutoSaveEnabled && ActiveSession is not null)
            _autosaveTimer.Start();
        else
            _autosaveTimer.Stop();
        PruneAutosaves();
    }

    private void PruneAutosaves()
    {
        int retention = Math.Clamp(SettingsService.Instance.AutoSaveRetention, 1, 10);
        foreach (SaveGameDocument stale in _documents.Values
            .Where(save => save.Kind == SaveKind.Autosave)
            .OrderByDescending(save => save.UpdatedUtc)
            .Skip(retention)
            .ToArray())
        {
            _documents.Remove(stale.SaveId);
            string path = SavePath(stale.SaveId);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private void LoadIndexOrRebuild()
    {
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(SaveDirectory));
        IEnumerable<string> paths = Directory.EnumerateFiles(
            ProjectSettings.GlobalizePath(SaveDirectory), "*.json")
            .Where(path => !path.EndsWith("index.json", StringComparison.OrdinalIgnoreCase));
        foreach (string path in paths)
        {
            try
            {
                SaveGameDocument? save = JsonSerializer.Deserialize<SaveGameDocument>(
                    File.ReadAllText(path), JsonOptions);
                if (save is not null && save.SchemaVersion == 1 && save.SaveId != Guid.Empty)
                    _documents[save.SaveId] = save;
            }
            catch
            {
                string corrupt = $"{path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
                File.Move(path, corrupt, true);
                string backup = $"{path}.bak";
                if (!File.Exists(backup))
                    continue;
                try
                {
                    SaveGameDocument? recovered = JsonSerializer.Deserialize<SaveGameDocument>(
                        File.ReadAllText(backup), JsonOptions);
                    if (recovered is not null && recovered.SchemaVersion == 1)
                    {
                        _documents[recovered.SaveId] = recovered;
                        File.Copy(backup, path, true);
                    }
                }
                catch (Exception backupException)
                {
                    GD.PushWarning($"Save backup recovery failed: {backupException.Message}");
                }
            }
        }
        WriteIndex();
    }

    private bool WriteDocument(SaveGameDocument document)
    {
        try
        {
            string path = SavePath(document.SaveId);
            WriteAtomic(path, JsonSerializer.Serialize(document, JsonOptions));
            return true;
        }
        catch (Exception exception)
        {
            GD.PushError($"Save write failed: {exception.Message}");
            return false;
        }
    }

    private void WriteIndex()
    {
        SaveGameIndex index = new() { SaveIds = _documents.Keys.ToList() };
        WriteAtomic(ProjectSettings.GlobalizePath(IndexPath),
            JsonSerializer.Serialize(index, JsonOptions));
    }

    private static void WriteAtomic(string target, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = $"{target}.{System.Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        string backup = $"{target}.bak";
        string writeLock = $"{target}.lock";

        FileStream? lockStream = null;
        for (int attempt = 0; attempt < 40 && lockStream is null; attempt++)
        {
            try
            {
                lockStream = new FileStream(
                    writeLock, FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 39)
            {
                System.Threading.Thread.Sleep(25);
            }
        }

        if (lockStream is null)
            throw new IOException($"Timed out waiting to write '{Path.GetFileName(target)}'.");

        using (lockStream)
        {
            try
            {
                File.WriteAllText(temporary, content);
                if (File.Exists(target))
                    File.Copy(target, backup, true);
                File.Move(temporary, target, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    private static SaveGameDocument NewDocument(SaveKind kind, string name) => new()
    {
        Kind = kind,
        DisplayName = name,
        ApplicationVersion = ProjectSettings.GetSetting(
            "application/config/version", "unknown").AsString()
    };

    private void CapturePlayedTime(SaveGameDocument document)
    {
        document.PlayedSeconds += Math.Max(0, (DateTimeOffset.UtcNow - _sessionStartedUtc).TotalSeconds);
        _sessionStartedUtc = DateTimeOffset.UtcNow;
        ActiveSession!.PlayedSeconds = document.PlayedSeconds;
    }

    private static SaveGameSummary ToSummary(SaveGameDocument save) => new()
    {
        SaveId = save.SaveId,
        RunId = save.RunId,
        Kind = save.Kind,
        DisplayName = save.DisplayName,
        UpdatedUtc = save.UpdatedUtc,
        Stage = save.Stage,
        Origin = save.Origin,
        PlayedSeconds = save.PlayedSeconds
    };

    private static SaveGameDocument Clone(SaveGameDocument source) =>
        JsonSerializer.Deserialize<SaveGameDocument>(
            JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;

    private static string SavePath(Guid id) =>
        ProjectSettings.GlobalizePath($"{SaveDirectory}/{id:N}.json");
}
