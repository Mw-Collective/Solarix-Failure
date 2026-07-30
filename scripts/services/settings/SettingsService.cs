using System.Text.RegularExpressions;
using Godot;
using SolarixFailure.Core;

namespace SolarixFailure;

public partial class SettingsService : Node
{
    public const int SchemaVersion = 2;
    private static readonly Regex ProfileNamePattern =
        new(@"^[\p{L}\p{N} _-]{1,32}$", RegexOptions.Compiled);

    public static SettingsService Instance { get; private set; } = null!;

    public event Action<string>? SettingsChanged;
    public event Action<string>? ProfileChanged;
    public event Action<string>? PresetApplied;
    public event Action<AccessibilitySettings>? AccessibilityChanged;

    private SettingsDocument _document = new();
    private readonly SettingsRepository _repository = new();
    private Godot.Timer _fpsTimer = null!;
    private Label _fpsLabel = null!;
    private Label _subtitleLabel = null!;
    private CanvasLayer _runtimeOverlay = null!;
    private CanvasLayer _visualEffectsLayer = null!;
    private ColorRect _accessibilityFilter = null!;
    private ShaderMaterial _accessibilityMaterial = null!;
    private WorldEnvironment _worldEnvironment = null!;
    private Godot.Environment _environment = null!;
    private bool _isShuttingDown;
    private bool _applicationFocused = true;
    private bool _pausedForFocusLoss;
    private AudioSettings _appliedAudio = new();

    public string CurrentProfileName => _document.CurrentProfile;
    public IReadOnlyList<string> ProfileNames =>
        _document.Profiles.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public float ScreenShakeMultiplier => Current.Settings.Accessibility.ReduceMotion
        ? 0f
        : Current.Settings.Accessibility.ScreenShakeIntensity;
    public float MouseSensitivity => Current.Settings.Controls.MouseSensitivity;
    public float GamepadSensitivity => Current.Settings.Controls.GamepadSensitivity;
    public bool InvertYAxis => Current.Settings.Controls.InvertYAxis;
    public bool MouseSmoothing => Current.Settings.Controls.MouseSmoothing;
    public bool ToggleSprint => Current.Settings.Controls.ToggleSprint;
    public float GamepadVibrationStrength => Current.Settings.Controls.GamepadVibration
        ? Current.Settings.Controls.VibrationStrength
        : 0f;
    public DifficultyLevel Difficulty => Current.Settings.Gameplay.Difficulty;
    public bool TutorialsEnabled => Current.Settings.Gameplay.EnableTutorials;
    public bool InteractionPromptsEnabled => Current.Settings.Gameplay.ShowInteractionPrompts;
    public bool ObjectiveMarkersEnabled => Current.Settings.Gameplay.ShowObjectiveMarkers;
    public bool HighlightInteractables => Current.Settings.Gameplay.HighlightInteractables;
    public float AimAssistStrength => Current.Settings.Gameplay.AimAssistStrength;
    public float CameraBobMultiplier => Current.Settings.Accessibility.ReduceMotion
        ? 0f
        : Current.Settings.Gameplay.CameraBobIntensity;
    public bool ReduceFlashing => Current.Settings.Accessibility.ReduceFlashing;
    public bool SpatialAudioEnabled => Current.Settings.Audio.Enable3DAudio;
    public bool AutoSaveEnabled => Current.Settings.Gameplay.AutoSave;
    public int AutoSaveIntervalSeconds => Current.Settings.Gameplay.AutoSaveIntervalSeconds;
    public int AutoSaveRetention => Current.Settings.Gameplay.AutoSaveRetention;
    public bool IsCapturingBinding { get; set; }

    private SettingsProfile Current => _document.Profiles[_document.CurrentProfile];

    public override void _EnterTree()
    {
        if (Instance is not null && IsInstanceValid(Instance) && Instance != this)
        {
            GD.PushError("Only one SettingsService autoload may exist.");
            QueueFree();
            return;
        }

        Instance = this;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _document = _repository.LoadOrCreate();
        CreateRuntimeFoundations();
        Apply(Current.Settings, Current.Bindings);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn)
        {
            _applicationFocused = true;
            ApplyFocusAudioMute();
            RestoreFocusPause();
        }
        else if (what == NotificationApplicationFocusOut)
        {
            _applicationFocused = false;
            ApplyFocusAudioMute();
            ApplyFocusPause();
        }

        if (what == NotificationWMCloseRequest && !_isShuttingDown)
        {
            _isShuttingDown = true;
            Save();
            GetTree().Quit();
        }
    }

    public GameSettings CreateWorkingCopy() => Current.Settings.DeepClone();

    public List<InputBinding> CreateBindingWorkingCopy() =>
        Current.Bindings.Select(binding => binding.DeepClone()).ToList();

    public void Preview(GameSettings settings, IReadOnlyList<InputBinding>? bindings = null)
    {
        Apply(settings, bindings ?? Current.Bindings);
        SettingsChanged?.Invoke("preview");
    }

    public void RollbackPreview()
    {
        Apply(Current.Settings, Current.Bindings);
        SettingsChanged?.Invoke("rollback");
    }

    public bool Commit(GameSettings settings, IReadOnlyList<InputBinding> bindings)
    {
        Current.Settings = settings.DeepClone();
        Current.Bindings = bindings.Select(binding => binding.DeepClone()).ToList();
        Current.UpdatedUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Apply(Current.Settings, Current.Bindings);
        SettingsChanged?.Invoke("commit");
        return Save();
    }

    public void ResetWorkingCopy(out GameSettings settings, out List<InputBinding> bindings)
    {
        settings = SettingsDefaults.Create();
        bindings = SettingsDefaults.CreateBindings();
        Preview(settings, bindings);
    }

    public bool CreateProfile(string requestedName, GameSettings settings, IReadOnlyList<InputBinding> bindings,
        out string error)
    {
        string name = requestedName.Trim();
        if (!ProfileNamePattern.IsMatch(name))
        {
            error = "Use 1–32 letters, numbers, spaces, hyphens, or underscores.";
            return false;
        }

        if (_document.Profiles.ContainsKey(name))
        {
            error = "A profile with that name already exists.";
            return false;
        }

        _document.Profiles[name] = new SettingsProfile
        {
            Settings = settings.DeepClone(),
            Bindings = bindings.Select(binding => binding.DeepClone()).ToList()
        };
        _document.CurrentProfile = name;
        Apply(_document.Profiles[name].Settings, _document.Profiles[name].Bindings);
        Save();
        ProfileChanged?.Invoke(name);
        error = string.Empty;
        return true;
    }

    public bool SelectProfile(string name)
    {
        if (!_document.Profiles.TryGetValue(name, out SettingsProfile? profile))
            return false;

        _document.CurrentProfile = _document.Profiles.Keys.First(key =>
            string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
        Apply(profile.Settings, profile.Bindings);
        Save();
        ProfileChanged?.Invoke(_document.CurrentProfile);
        return true;
    }

    public bool DeleteProfile(string name, out string error)
    {
        if (string.Equals(name, SettingsDefaults.DefaultProfileName, StringComparison.OrdinalIgnoreCase))
        {
            error = "The Default profile cannot be deleted.";
            return false;
        }

        if (!_document.Profiles.Remove(name))
        {
            error = "Profile not found.";
            return false;
        }

        if (string.Equals(_document.CurrentProfile, name, StringComparison.OrdinalIgnoreCase))
        {
            _document.CurrentProfile = SettingsDefaults.DefaultProfileName;
            Apply(Current.Settings, Current.Bindings);
        }

        Save();
        ProfileChanged?.Invoke(_document.CurrentProfile);
        error = string.Empty;
        return true;
    }

    public bool OverwriteCurrentProfile(GameSettings settings, IReadOnlyList<InputBinding> bindings) =>
        Commit(settings, bindings);

    public bool SetFullscreenAndSave(bool fullscreen)
    {
        Current.Settings.Graphics.Fullscreen = fullscreen;
        ApplyGraphics(Current.Settings.Graphics);
        SettingsChanged?.Invoke("fullscreen_shortcut");
        return Save();
    }

    public GameSettings ApplyPreset(string presetName, GameSettings working)
    {
        GraphicsSettings graphics = working.Graphics;
        switch (presetName.ToLowerInvariant())
        {
            case "performance":
                graphics.TextureFilter = TextureFilterMode.Nearest;
                graphics.ShadowAtlasSize = 512;
                graphics.AntiAliasing = AntiAliasingMode.Off;
                graphics.FrameRateLimit = 60;
                graphics.MotionBlur = 0;
                graphics.BloomIntensity = 0;
                break;
            case "balanced":
                graphics.TextureFilter = TextureFilterMode.Linear;
                graphics.ShadowAtlasSize = 1024;
                graphics.AntiAliasing = AntiAliasingMode.Off;
                graphics.FrameRateLimit = 120;
                graphics.MotionBlur = 0;
                graphics.BloomIntensity = 0.2f;
                break;
            case "quality":
                graphics.TextureFilter = TextureFilterMode.Linear;
                graphics.ShadowAtlasSize = 2048;
                graphics.AntiAliasing = AntiAliasingMode.Msaa4X;
                graphics.FrameRateLimit = 144;
                graphics.MotionBlur = 0.25f;
                graphics.BloomIntensity = 0.4f;
                break;
            case "ultra":
                graphics.TextureFilter = TextureFilterMode.Linear;
                graphics.ShadowAtlasSize = 4096;
                graphics.AntiAliasing = AntiAliasingMode.Msaa8X;
                graphics.FrameRateLimit = 240;
                graphics.MotionBlur = 0.5f;
                graphics.BloomIntensity = 0.65f;
                break;
            default:
                return working;
        }

        Preview(working);
        PresetApplied?.Invoke(presetName);
        return working;
    }

    public bool IsMotionBlurSupported() =>
        ProjectSettings.GetSetting("rendering/renderer/rendering_method", "gl_compatibility").AsString()
            != "gl_compatibility";

    public bool IsDesktopDisplayControlSupported() =>
        !OS.HasFeature("mobile");

    public void ShowSubtitle(string text, double durationSeconds = 3.0)
    {
        if (!Current.Settings.Accessibility.EnableSubtitles)
            return;

        _subtitleLabel.Text = text;
        _subtitleLabel.Visible = true;
        Tween tween = CreateTween();
        tween.TweenInterval(Math.Max(0.1, durationSeconds));
        tween.TweenCallback(Callable.From(() =>
        {
            if (IsInstanceValid(_subtitleLabel))
                _subtitleLabel.Visible = false;
        }));
    }

    public void Announce(string text)
    {
        if (!Current.Settings.Accessibility.EnableScreenReader || string.IsNullOrWhiteSpace(text))
            return;

        if (DisplayServer.HasFeature(DisplayServer.Feature.TextToSpeech))
            DisplayServer.TtsSpeak(text, string.Empty);
    }

    public bool Save()
        => _repository.Save(_document);

    private void Apply(GameSettings settings, IReadOnlyList<InputBinding> bindings)
    {
        ApplyAudio(settings.Audio);
        ApplyGraphics(settings.Graphics);
        ApplyAccessibility(settings.Accessibility);
        ApplyGameplay(settings.Gameplay);
        ApplyControls(settings.Controls, bindings);
    }

    private void ApplyAudio(AudioSettings audio)
    {
        _appliedAudio = audio;
        ApplyBus("Master", audio.MasterVolume);
        ApplyBus("Music", audio.MusicVolume);
        ApplyBus("SFX", audio.SfxVolume);
        ApplyBus("UI", audio.UiVolume);
        ApplyBus("Dialogue", audio.DialogueVolume);
        ApplyBus("Ambient", audio.AmbientVolume);

        string[] devices = AudioServer.GetOutputDeviceList();
        if (devices.Contains(audio.OutputDevice, StringComparer.OrdinalIgnoreCase))
            AudioServer.OutputDevice = audio.OutputDevice;

        ApplyFocusAudioMute();
    }

    private static void ApplyBus(string busName, float linearVolume)
    {
        int index = AudioServer.GetBusIndex(busName);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, busName);
        }

        float value = Math.Clamp(linearVolume, 0f, 1f);
        AudioServer.SetBusMute(index, value <= 0.0001f);
        AudioServer.SetBusVolumeDb(index, value <= 0.0001f ? -80f : Mathf.LinearToDb(value));
    }

    private void ApplyGraphics(GraphicsSettings graphics)
    {
        Window root = GetTree().Root;
        if (IsDesktopDisplayControlSupported())
        {
            root.Mode = graphics.Fullscreen ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;
            if (!graphics.Fullscreen)
                root.Size = new Vector2I(
                    Math.Clamp(graphics.ResolutionWidth, 640, 7680),
                    Math.Clamp(graphics.ResolutionHeight, 360, 4320));
        }

        DisplayServer.WindowSetVsyncMode(graphics.VSync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = Math.Clamp(graphics.FrameRateLimit, 30, 1000);
        root.Msaa2D = graphics.AntiAliasing switch
        {
            AntiAliasingMode.Msaa2X => Viewport.Msaa.Msaa2X,
            AntiAliasingMode.Msaa4X => Viewport.Msaa.Msaa4X,
            AntiAliasingMode.Msaa8X => Viewport.Msaa.Msaa8X,
            _ => Viewport.Msaa.Disabled
        };
        root.CanvasItemDefaultTextureFilter = graphics.TextureFilter == TextureFilterMode.Nearest
            ? Viewport.DefaultCanvasItemTextureFilter.Nearest
            : Viewport.DefaultCanvasItemTextureFilter.Linear;
        root.PositionalShadowAtlasSize = Math.Clamp(graphics.ShadowAtlasSize, 256, 8192);
        _environment.GlowEnabled = graphics.BloomIntensity > 0.001f;
        _environment.GlowIntensity = Math.Clamp(graphics.BloomIntensity, 0f, 1f);
    }

    private void ApplyAccessibility(AccessibilitySettings accessibility)
    {
        Window root = GetTree().Root;
        Theme theme = root.Theme ?? new Theme();
        theme.DefaultFontSize = Mathf.RoundToInt(
            16 * accessibility.TextScale * (accessibility.DyslexiaFriendlyText ? 1.05f : 1.0f));
        theme.SetConstant("outline_size", "Label", accessibility.HighContrast ? 1 : 0);
        theme.SetColor("font_outline_color", "Label", Colors.Black);
        root.Theme = theme;

        _subtitleLabel?.AddThemeFontSizeOverride("font_size",
            Mathf.RoundToInt(26 * accessibility.SubtitleScale));
        if (_subtitleLabel is not null)
        {
            StyleBoxFlat background = new()
            {
                BgColor = new Color(0f, 0f, 0f,
                    Math.Clamp(accessibility.SubtitleBackgroundOpacity, 0f, 1f)),
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                ContentMarginLeft = 18,
                ContentMarginRight = 18,
                ContentMarginTop = 8,
                ContentMarginBottom = 8
            };
            _subtitleLabel.AddThemeStyleboxOverride("normal", background);
        }
        if (_accessibilityFilter is not null)
        {
            _accessibilityFilter.Visible =
                accessibility.EnableColorVisionFilter || accessibility.HighContrast;
            _accessibilityMaterial.SetShaderParameter("contrast",
                accessibility.HighContrast ? 1.28f : 1.0f);
            _accessibilityMaterial.SetShaderParameter("color_matrix",
                accessibility.EnableColorVisionFilter
                    ? ColorVisionMatrix(accessibility.ColorVisionMode)
                    : Basis.Identity);
        }
        AccessibilityChanged?.Invoke(accessibility);
    }

    private void ApplyGameplay(GameplaySettings gameplay)
    {
        Engine.TimeScale = Math.Clamp(gameplay.GameSpeed, 0.5f, 2f);
        _fpsLabel.Visible = gameplay.ShowFpsCounter;
        if (gameplay.ShowFpsCounter && _fpsTimer.IsStopped())
            _fpsTimer.Start();
        else if (!gameplay.ShowFpsCounter)
            _fpsTimer.Stop();

        if (!gameplay.PauseWhenUnfocused)
            RestoreFocusPause();
        else if (!_applicationFocused)
            ApplyFocusPause();
    }

    private static void ApplyControls(ControlSettings controls, IReadOnlyList<InputBinding> bindings)
    {
        foreach (string action in bindings.Select(binding => binding.Action).Distinct())
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action, controls.ControllerDeadzone);

            InputMap.ActionSetDeadzone(action, controls.ControllerDeadzone);
            InputMap.ActionEraseEvents(action);
            foreach (InputBinding binding in bindings.Where(binding => binding.Action == action))
            {
                InputEvent? inputEvent = binding.Device switch
                {
                    InputDeviceKind.Keyboard => new InputEventKey { Keycode = (Key)binding.Code },
                    InputDeviceKind.Mouse => new InputEventMouseButton
                    {
                        ButtonIndex = (MouseButton)binding.Code,
                        Device = binding.DeviceId
                    },
                    InputDeviceKind.Gamepad when controls.GamepadEnabled => new InputEventJoypadButton
                    {
                        ButtonIndex = (JoyButton)binding.Code,
                        Device = binding.DeviceId
                    },
                    _ => null
                };

                if (inputEvent is not null)
                    InputMap.ActionAddEvent(action, inputEvent);
            }
        }
    }

    private void CreateRuntimeFoundations()
    {
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Canvas,
            GlowEnabled = true,
            GlowIntensity = 0.35f
        };
        _worldEnvironment = new WorldEnvironment
        {
            Name = "GlobalVisualEffectsEnvironment",
            Environment = _environment
        };
        AddChild(_worldEnvironment);

        _fpsTimer = new Godot.Timer { Name = "FpsUpdateTimer", WaitTime = 0.25, OneShot = false };
        _fpsTimer.Timeout += () => _fpsLabel.Text = $"{Engine.GetFramesPerSecond()} FPS";
        AddChild(_fpsTimer);

        _runtimeOverlay = new CanvasLayer { Name = "RuntimeAccessibilityOverlay", Layer = 90 };
        AddChild(_runtimeOverlay);

        _visualEffectsLayer = new CanvasLayer { Name = "AccessibilityVisualEffects", Layer = 80 };
        AddChild(_visualEffectsLayer);
        Shader accessibilityShader = new()
        {
            Code = """
                shader_type canvas_item;
                uniform sampler2D screen_texture : hint_screen_texture, filter_linear;
                uniform mat3 color_matrix = mat3(1.0);
                uniform float contrast = 1.0;

                void fragment() {
                    vec4 source = texture(screen_texture, SCREEN_UV);
                    vec3 adjusted = (source.rgb - vec3(0.5)) * contrast + vec3(0.5);
                    adjusted = color_matrix * adjusted;
                    COLOR = vec4(clamp(adjusted, vec3(0.0), vec3(1.0)), source.a);
                }
                """
        };
        _accessibilityMaterial = new ShaderMaterial { Shader = accessibilityShader };
        _accessibilityFilter = new ColorRect
        {
            Name = "ColorVisionAndContrastFilter",
            Material = _accessibilityMaterial,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        _accessibilityFilter.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _visualEffectsLayer.AddChild(_accessibilityFilter);

        _fpsLabel = new Label
        {
            Name = "FpsCounter",
            Visible = false,
            Position = new Vector2(18, 16),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _fpsLabel.AddThemeFontSizeOverride("font_size", 18);
        _runtimeOverlay.AddChild(_fpsLabel);

        _subtitleLabel = new Label
        {
            Name = "SubtitlePresenter",
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _subtitleLabel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _subtitleLabel.OffsetLeft = -480;
        _subtitleLabel.OffsetRight = 480;
        _subtitleLabel.OffsetTop = -130;
        _subtitleLabel.OffsetBottom = -55;
        _subtitleLabel.AddThemeColorOverride("font_color", Colors.White);
        _subtitleLabel.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _subtitleLabel.AddThemeConstantOverride("shadow_offset_x", 2);
        _subtitleLabel.AddThemeConstantOverride("shadow_offset_y", 2);
        _runtimeOverlay.AddChild(_subtitleLabel);
    }

    private void ApplyFocusAudioMute()
    {
        int masterIndex = AudioServer.GetBusIndex("Master");
        if (masterIndex < 0)
            return;

        bool volumeMuted = _appliedAudio.MasterVolume <= 0.0001f;
        AudioServer.SetBusMute(masterIndex,
            volumeMuted || (_appliedAudio.MuteWhenUnfocused && !_applicationFocused));
    }

    private void ApplyFocusPause()
    {
        if (_applicationFocused
            || !Current.Settings.Gameplay.PauseWhenUnfocused
            || GetTree().Paused)
        {
            return;
        }

        _pausedForFocusLoss = true;
        GetTree().Paused = true;
    }

    private void RestoreFocusPause()
    {
        if (!_pausedForFocusLoss)
            return;

        _pausedForFocusLoss = false;
        GetTree().Paused = false;
    }

    private static Basis ColorVisionMatrix(ColorVisionMode mode) => mode switch
    {
        ColorVisionMode.Protanopia => new Basis(
            new Vector3(0.567f, 0.558f, 0f),
            new Vector3(0.433f, 0.442f, 0.242f),
            new Vector3(0f, 0f, 0.758f)),
        ColorVisionMode.Tritanopia => new Basis(
            new Vector3(0.95f, 0f, 0f),
            new Vector3(0.05f, 0.433f, 0.475f),
            new Vector3(0f, 0.567f, 0.525f)),
        _ => new Basis(
            new Vector3(0.625f, 0.7f, 0f),
            new Vector3(0.375f, 0.3f, 0.3f),
            new Vector3(0f, 0f, 0.7f))
    };
}
