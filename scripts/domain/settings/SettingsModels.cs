using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolarixFailure.Core;

public enum DifficultyLevel
{
    Easy,
    Normal,
    Hard,
    Hardcore
}

public enum ColorVisionMode
{
    Deuteranopia,
    Protanopia,
    Tritanopia
}

public enum InputDeviceKind
{
    Keyboard,
    Mouse,
    Gamepad
}

public enum AntiAliasingMode
{
    Off,
    Msaa2X,
    Msaa4X,
    Msaa8X
}

public enum TextureFilterMode
{
    Nearest,
    Linear
}

public sealed class AudioSettings
{
    public float MasterVolume { get; set; } = 0.8f;
    public float MusicVolume { get; set; } = 0.7f;
    public float SfxVolume { get; set; } = 0.8f;
    public float UiVolume { get; set; } = 0.6f;
    public float DialogueVolume { get; set; } = 0.85f;
    public float AmbientVolume { get; set; } = 0.75f;
    public string OutputDevice { get; set; } = "Default";
    public bool MuteWhenUnfocused { get; set; }
    public bool Enable3DAudio { get; set; } = true;
}

public sealed class GraphicsSettings
{
    public int ResolutionWidth { get; set; } = 1920;
    public int ResolutionHeight { get; set; } = 1080;
    public bool Fullscreen { get; set; }
    public bool VSync { get; set; } = true;
    public TextureFilterMode TextureFilter { get; set; } = TextureFilterMode.Linear;
    public int ShadowAtlasSize { get; set; } = 2048;
    public AntiAliasingMode AntiAliasing { get; set; } = AntiAliasingMode.Off;
    public int FrameRateLimit { get; set; } = 144;
    public float MotionBlur { get; set; }
    public float BloomIntensity { get; set; } = 0.35f;
}

public sealed class AccessibilitySettings
{
    public bool EnableColorVisionFilter { get; set; }
    public ColorVisionMode ColorVisionMode { get; set; } = ColorVisionMode.Deuteranopia;
    public float TextScale { get; set; } = 1.0f;
    public bool EnableScreenReader { get; set; }
    public bool EnableSubtitles { get; set; } = true;
    public float SubtitleScale { get; set; } = 1.0f;
    public bool HighContrast { get; set; }
    public float ScreenShakeIntensity { get; set; } = 1.0f;
    public bool DyslexiaFriendlyText { get; set; }
    public float SubtitleBackgroundOpacity { get; set; } = 0.72f;
    public bool ReduceMotion { get; set; }
    public bool ReduceFlashing { get; set; }
}

public sealed class GameplaySettings
{
    public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Normal;
    public bool AutoSave { get; set; } = true;
    public int AutoSaveIntervalSeconds { get; set; } = 300;
    public int AutoSaveRetention { get; set; } = 3;
    public bool ShowFpsCounter { get; set; }
    public float GameSpeed { get; set; } = 1.0f;
    public bool EnableTutorials { get; set; } = true;
    public bool PauseWhenUnfocused { get; set; } = true;
    public bool ShowInteractionPrompts { get; set; } = true;
    public bool ShowObjectiveMarkers { get; set; } = true;
    public float CameraBobIntensity { get; set; } = 1.0f;
    public bool HighlightInteractables { get; set; } = true;
    public float AimAssistStrength { get; set; } = 0.5f;
}

public sealed class ControlSettings
{
    public bool GamepadEnabled { get; set; } = true;
    public float GamepadSensitivity { get; set; } = 1.0f;
    public float MouseSensitivity { get; set; } = 1.0f;
    public bool InvertYAxis { get; set; }
    public float ControllerDeadzone { get; set; } = 0.15f;
    public bool GamepadVibration { get; set; } = true;
    public float VibrationStrength { get; set; } = 1.0f;
    public bool MouseSmoothing { get; set; }
    public bool ToggleSprint { get; set; }
}

public sealed class GameSettings
{
    public AudioSettings Audio { get; set; } = new();
    public GraphicsSettings Graphics { get; set; } = new();
    public AccessibilitySettings Accessibility { get; set; } = new();
    public GameplaySettings Gameplay { get; set; } = new();
    public ControlSettings Controls { get; set; } = new();

    public GameSettings DeepClone() =>
        JsonSerializer.Deserialize<GameSettings>(
            JsonSerializer.Serialize(this, SettingsJson.Options),
            SettingsJson.Options) ?? new GameSettings();
}

public sealed class InputBinding
{
    public string Action { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter<InputDeviceKind>))]
    public InputDeviceKind Device { get; set; }
    public long Code { get; set; }
    public int DeviceId { get; set; } = -1;

    public InputBinding DeepClone() => new()
    {
        Action = Action,
        Device = Device,
        Code = Code,
        DeviceId = DeviceId
    };
}

public sealed class SettingsProfile
{
    public GameSettings Settings { get; set; } = new();
    public List<InputBinding> Bindings { get; set; } = SettingsDefaults.CreateBindings();
    public long UpdatedUnixTime { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public SettingsProfile DeepClone() => new()
    {
        Settings = Settings.DeepClone(),
        Bindings = Bindings.Select(binding => binding.DeepClone()).ToList(),
        UpdatedUnixTime = UpdatedUnixTime
    };
}

public sealed class SettingsDocument
{
    public int SchemaVersion { get; set; } = 2;
    public string CurrentProfile { get; set; } = SettingsDefaults.DefaultProfileName;
    public Dictionary<string, SettingsProfile> Profiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [SettingsDefaults.DefaultProfileName] = new SettingsProfile()
        };
}

public static class SettingsDefaults
{
    public const string DefaultProfileName = "Default";

    public static GameSettings Create() => new();

    public static List<InputBinding> CreateBindings() =>
    [
        Key("move_up", Godot.Key.W),
        Key("move_down", Godot.Key.S),
        Key("move_left", Godot.Key.A),
        Key("move_right", Godot.Key.D),
        Key("action_primary", Godot.Key.Space),
        Key("action_secondary", Godot.Key.E),
        Key("pause", Godot.Key.Escape),
        Key("interact", Godot.Key.F),
        Key("sprint", Godot.Key.Shift),
        Key("reload", Godot.Key.R),
        Pad("move_up", Godot.JoyButton.DpadUp),
        Pad("move_down", Godot.JoyButton.DpadDown),
        Pad("move_left", Godot.JoyButton.DpadLeft),
        Pad("move_right", Godot.JoyButton.DpadRight),
        Pad("action_primary", Godot.JoyButton.A),
        Pad("action_secondary", Godot.JoyButton.X),
        Pad("pause", Godot.JoyButton.Start),
        Pad("interact", Godot.JoyButton.Y),
        Pad("sprint", Godot.JoyButton.LeftShoulder),
        Pad("reload", Godot.JoyButton.RightShoulder)
    ];

    private static InputBinding Key(string action, Godot.Key key) => new()
    {
        Action = action,
        Device = InputDeviceKind.Keyboard,
        Code = (long)key
    };

    private static InputBinding Pad(string action, Godot.JoyButton button) => new()
    {
        Action = action,
        Device = InputDeviceKind.Gamepad,
        Code = (long)button
    };
}

public static class SettingsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };
}
