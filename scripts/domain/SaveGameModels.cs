using System.Text.Json.Serialization;

namespace SolarixFailure.Domain;

public enum SaveKind
{
    Autosave,
    Manual
}

public enum GameFlowStage
{
    StoryPrologue,
    OriginSelection,
    GameplayCanvas,
    PrologueComplete
}

public enum CharacterOrigin
{
    None,
    Human
}

public sealed class SaveGameDocument
{
    public int SchemaVersion { get; set; } = 1;
    public Guid SaveId { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<SaveKind>))]
    public SaveKind Kind { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ApplicationVersion { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter<GameFlowStage>))]
    public GameFlowStage Stage { get; set; } = GameFlowStage.StoryPrologue;
    public int StoryPanelIndex { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<CharacterOrigin>))]
    public CharacterOrigin Origin { get; set; }
    public double PlayedSeconds { get; set; }
}

public sealed class SaveGameSummary
{
    public required Guid SaveId { get; init; }
    public required Guid RunId { get; init; }
    public required SaveKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset UpdatedUtc { get; init; }
    public required GameFlowStage Stage { get; init; }
    public required CharacterOrigin Origin { get; init; }
    public required double PlayedSeconds { get; init; }
}

public sealed class SaveGameIndex
{
    public int SchemaVersion { get; set; } = 1;
    public List<Guid> SaveIds { get; set; } = [];
}

public readonly record struct SaveQuery(Guid? RunId, SaveKind? Kind);
