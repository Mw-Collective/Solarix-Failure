using Godot;
using SolarixFailure.Core;
using System.Globalization;

namespace SolarixFailure;

public partial class SettingsMenu : Control
{
    public event Action? Closed;

    private SettingsService _service = null!;
    private GameSettings _working = null!;
    private List<InputBinding> _bindings = null!;
    private OptionButton _profileSelect = null!;
    private OptionButton _presetSelect = null!;
    private TabContainer _tabs = null!;
    private HBoxContainer _categoryButtons = null!;
    private bool _dirty;
    private bool _refreshing;
    private string? _capturingAction;
    private Button? _capturingButton;
    private string? _pendingProfile;
    private GraphicsSettings? _displayRollback;
    private ConfirmationDialog? _displayDialog;
    private Godot.Timer? _displayTimer;
    private int _displaySecondsRemaining;

    public override void _Ready()
    {
        _service = SettingsService.Instance;
        _working = _service.CreateWorkingCopy();
        _bindings = _service.CreateBindingWorkingCopy();
        _tabs = GetNode<TabContainer>("%Tabs");
        _categoryButtons = GetNode<HBoxContainer>("%CategoryButtons");
        _service.SettingsChanged += ExternalSettingsChanged;
        GetNode<Button>("%ApplyButton").Pressed += ApplyAndSave;
        GetNode<Button>("%ResetButton").Pressed += ResetToDefaults;
        GetNode<Button>("%BackButton").Pressed += RequestClose;

        RebuildTabs();
        GetNode<Button>("%BackButton").GrabFocus();
    }

    public override void _ExitTree()
    {
        if (_service is not null)
        {
            _service.SettingsChanged -= ExternalSettingsChanged;
            _service.IsCapturingBinding = false;
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_capturingAction is null || _capturingButton is null)
            return;

        InputBinding? captured = inputEvent switch
        {
            InputEventKey { Pressed: true, Echo: false } key when key.Keycode == Key.Escape => null,
            InputEventKey { Pressed: true, Echo: false } key => new InputBinding
            {
                Action = _capturingAction,
                Device = InputDeviceKind.Keyboard,
                Code = (long)key.Keycode
            },
            InputEventMouseButton { Pressed: true } mouse => new InputBinding
            {
                Action = _capturingAction,
                Device = InputDeviceKind.Mouse,
                Code = (long)mouse.ButtonIndex,
                DeviceId = mouse.Device
            },
            InputEventJoypadButton { Pressed: true } joypad => new InputBinding
            {
                Action = _capturingAction,
                Device = InputDeviceKind.Gamepad,
                Code = (long)joypad.ButtonIndex,
                DeviceId = joypad.Device
            },
            _ => null
        };

        if (inputEvent is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            FinishCapture();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (captured is null)
            return;

        InputBinding? conflict = _bindings.FirstOrDefault(binding =>
            binding.Action != captured.Action &&
            binding.Device == captured.Device &&
            binding.Code == captured.Code &&
            (binding.Device != InputDeviceKind.Gamepad || binding.DeviceId == captured.DeviceId));

        if (conflict is not null)
        {
            ShowMessage("Binding conflict",
                $"{DescribeBinding(captured)} is already assigned to {Humanize(conflict.Action)}.");
            FinishCapture();
            GetViewport().SetInputAsHandled();
            return;
        }

        _bindings.RemoveAll(binding =>
            binding.Action == captured.Action && binding.Device == captured.Device);
        _bindings.Add(captured);
        _dirty = true;
        _service.Preview(_working, _bindings);
        FinishCapture();
        RebuildTabs();
        GetViewport().SetInputAsHandled();
    }

    private void RebuildTabs()
    {
        string selectedCategory = _tabs.GetChildCount() > 0
            ? _tabs.GetChild(_tabs.CurrentTab).Name.ToString()
            : "General";

        _refreshing = true;
        foreach (Node child in _tabs.GetChildren())
        {
            _tabs.RemoveChild(child);
            child.QueueFree();
        }

        AddGeneralTab();
        AddAudioTab();
        AddGraphicsTab();
        AddAccessibilityTab();
        AddGameplayTab();
        AddControlsTab();

        int selectedIndex = 0;
        for (int index = 0; index < _tabs.GetChildCount(); index++)
        {
            if (_tabs.GetChild(index).Name.ToString() == selectedCategory)
            {
                selectedIndex = index;
                break;
            }
        }
        _tabs.CurrentTab = selectedIndex;
        RebuildCategoryButtons(selectedIndex);
        _refreshing = false;
    }

    private void ExternalSettingsChanged(string reason)
    {
        if (reason != "fullscreen_shortcut")
            return;
        _working.Graphics.Fullscreen =
            _service.CreateWorkingCopy().Graphics.Fullscreen;
        RebuildTabs();
    }

    private void AddGeneralTab()
    {
        VBoxContainer page = CreatePage("General");
        (VBoxContainer profilesColumn, VBoxContainer applicationColumn) =
            CreateTwoColumnLayout(page, "PROFILES", "APPLICATION");

        HBoxContainer profileControls = new();
        profileControls.AddThemeConstantOverride("separation", 8);

        _profileSelect = new OptionButton
        {
            CustomMinimumSize = new Vector2(190, 42)
        };
        profileControls.AddChild(_profileSelect);

        Button newProfile = CreateCompactButton("NEW", 90);
        Button deleteProfile = CreateCompactButton("DELETE", 90);
        Button saveProfile = CreateCompactButton("SAVE", 90);
        profileControls.AddChild(newProfile);
        profileControls.AddChild(deleteProfile);
        profileControls.AddChild(saveProfile);

        AddRow(profilesColumn, "Settings profile",
            "Create and manage independent settings and control configurations.",
            profileControls);

        AddCheck(applicationColumn, "Auto save", "Emit periodic save requests.",
            _working.Gameplay.AutoSave, value => _working.Gameplay.AutoSave = value);
        AddSlider(applicationColumn, "Auto-save interval", "Seconds between save requests.",
            _working.Gameplay.AutoSaveIntervalSeconds, 60, 600, 30,
            value => _working.Gameplay.AutoSaveIntervalSeconds = (int)value);
        AddSlider(applicationColumn, "Autosave History",
            "Number of recent autosaves retained across all runs.",
            _working.Gameplay.AutoSaveRetention, 1, 10, 1,
            value => _working.Gameplay.AutoSaveRetention = (int)value);
        AddCheck(applicationColumn, "Show FPS", "Display a lightweight frame-rate counter.",
            _working.Gameplay.ShowFpsCounter, value => _working.Gameplay.ShowFpsCounter = value);
        AddCheck(applicationColumn, "Pause when unfocused",
            "Pause gameplay when switching to another application.",
            _working.Gameplay.PauseWhenUnfocused,
            value => _working.Gameplay.PauseWhenUnfocused = value);

        PopulateProfiles();
        _profileSelect.ItemSelected += ProfileSelected;
        newProfile.Pressed += ShowNewProfileDialog;
        deleteProfile.Pressed += DeleteSelectedProfile;
        saveProfile.Pressed += SaveCurrentProfile;
    }

    private void AddAudioTab()
    {
        VBoxContainer page = CreatePage("Audio");
        (VBoxContainer mixColumn, VBoxContainer outputColumn) =
            CreateTwoColumnLayout(page, "MIX", "OUTPUT & BEHAVIOR");

        AddSlider(mixColumn, "Master volume", "Overall output volume.",
            _working.Audio.MasterVolume, 0, 1, 0.01,
            value => _working.Audio.MasterVolume = (float)value);
        AddSlider(mixColumn, "Music volume", "Music bus volume.",
            _working.Audio.MusicVolume, 0, 1, 0.01,
            value => _working.Audio.MusicVolume = (float)value);
        AddSlider(mixColumn, "SFX volume", "Sound-effects bus volume.",
            _working.Audio.SfxVolume, 0, 1, 0.01,
            value => _working.Audio.SfxVolume = (float)value);
        AddSlider(mixColumn, "UI volume", "Interface sound bus volume.",
            _working.Audio.UiVolume, 0, 1, 0.01,
            value => _working.Audio.UiVolume = (float)value);
        AddSlider(mixColumn, "Dialogue volume", "Voices and spoken dialogue.",
            _working.Audio.DialogueVolume, 0, 1, 0.01,
            value => _working.Audio.DialogueVolume = (float)value);
        AddSlider(mixColumn, "Ambient volume", "Environmental beds and world ambience.",
            _working.Audio.AmbientVolume, 0, 1, 0.01,
            value => _working.Audio.AmbientVolume = (float)value);

        string[] outputDevices = AudioServer.GetOutputDeviceList();
        if (outputDevices.Length == 0)
            outputDevices = ["Default"];
        int selectedDevice = Array.FindIndex(outputDevices, device =>
            string.Equals(device, _working.Audio.OutputDevice, StringComparison.OrdinalIgnoreCase));
        string[] availableDevices = outputDevices;
        AddOption(outputColumn, "Output device", "Audio device used by the game.",
            availableDevices, Math.Max(0, selectedDevice),
            index => _working.Audio.OutputDevice = availableDevices[index]);
        AddCheck(outputColumn, "Mute in background",
            "Mute game audio while the application is not focused.",
            _working.Audio.MuteWhenUnfocused,
            value => _working.Audio.MuteWhenUnfocused = value);
        AddCheck(outputColumn, "3D audio", "Enable spatial-audio behavior for world sounds.",
            _working.Audio.Enable3DAudio, value => _working.Audio.Enable3DAudio = value);
    }

    private void AddGraphicsTab()
    {
        VBoxContainer page = CreatePage("Graphics");
        (VBoxContainer displayColumn, VBoxContainer qualityColumn) =
            CreateTwoColumnLayout(page, "DISPLAY", "QUALITY");

        HBoxContainer presetControls = new();
        presetControls.AddThemeConstantOverride("separation", 8);
        _presetSelect = new OptionButton
        {
            CustomMinimumSize = new Vector2(180, 42)
        };
        Button applyPreset = CreateCompactButton("APPLY", 100);
        presetControls.AddChild(_presetSelect);
        presetControls.AddChild(applyPreset);
        AddRow(qualityColumn, "Graphics preset",
            "Apply a supported performance, balanced, quality, or ultra graphics configuration.",
            presetControls);
        PopulatePresets();
        applyPreset.Pressed += ApplySelectedPreset;

        OptionButton resolution = AddOption(displayColumn, "Resolution",
            "Windowed resolution. Native mobile platforms use their display resolution.",
            ["1280 × 720", "1600 × 900", "1920 × 1080", "2560 × 1440"],
            ResolutionIndex(_working.Graphics.ResolutionWidth, _working.Graphics.ResolutionHeight),
            index =>
            {
                (int width, int height) = index switch
                {
                    0 => (1280, 720),
                    1 => (1600, 900),
                    3 => (2560, 1440),
                    _ => (1920, 1080)
                };
                BeginDisplayPreview();
                _working.Graphics.ResolutionWidth = width;
                _working.Graphics.ResolutionHeight = height;
            });
        resolution.Disabled = !_service.IsDesktopDisplayControlSupported();
        resolution.TooltipText = resolution.Disabled ? "Managed by the native mobile display." : string.Empty;

        CheckBox fullscreen = AddCheck(displayColumn, "Fullscreen", "Use a borderless fullscreen window.",
            _working.Graphics.Fullscreen, value =>
            {
                BeginDisplayPreview();
                _working.Graphics.Fullscreen = value;
            });
        fullscreen.Disabled = !_service.IsDesktopDisplayControlSupported();

        AddCheck(displayColumn, "V-Sync", "Synchronize presentation with the display.",
            _working.Graphics.VSync, value => _working.Graphics.VSync = value);
        AddSlider(displayColumn, "Frame-rate limit", "Maximum rendered frames per second.",
            _working.Graphics.FrameRateLimit, 30, 240, 5,
            value => _working.Graphics.FrameRateLimit = (int)value);
        AddOption(qualityColumn, "Texture filtering", "Filtering used by 2D canvas textures.",
            ["Nearest", "Linear"], (int)_working.Graphics.TextureFilter,
            index => _working.Graphics.TextureFilter = (TextureFilterMode)index);
        AddOption(qualityColumn, "Anti-aliasing", "Multisample anti-aliasing for 2D rendering.",
            ["Off", "2× MSAA", "4× MSAA", "8× MSAA"], (int)_working.Graphics.AntiAliasing,
            index => _working.Graphics.AntiAliasing = (AntiAliasingMode)index);
        AddOption(qualityColumn, "Shadow quality", "Positional shadow-atlas resolution.",
            ["Low", "Medium", "High", "Ultra"], ShadowIndex(_working.Graphics.ShadowAtlasSize),
            index => _working.Graphics.ShadowAtlasSize = index switch
            {
                0 => 512,
                1 => 1024,
                3 => 4096,
                _ => 2048
            });
        AddSlider(qualityColumn, "Bloom", "Compatibility-safe glow foundation for future environments.",
            _working.Graphics.BloomIntensity, 0, 1, 0.05,
            value => _working.Graphics.BloomIntensity = (float)value);
        HSlider motionBlur = AddSlider(qualityColumn, "Motion blur",
            "Requires a rendering backend and camera that expose motion data.",
            _working.Graphics.MotionBlur, 0, 1, 0.05,
            value => _working.Graphics.MotionBlur = (float)value);
        motionBlur.Editable = _service.IsMotionBlurSupported();
        motionBlur.TooltipText = motionBlur.Editable
            ? string.Empty
            : "Unavailable with the Compatibility renderer.";
    }

    private void AddAccessibilityTab()
    {
        VBoxContainer page = CreatePage("Accessibility");
        (VBoxContainer visualColumn, VBoxContainer supportColumn) =
            CreateTwoColumnLayout(page, "VISION & TEXT", "SUBTITLES & MOTION");

        AddCheck(visualColumn, "Color-vision filter", "Enable the global color-vision adjustment.",
            _working.Accessibility.EnableColorVisionFilter,
            value => _working.Accessibility.EnableColorVisionFilter = value);
        AddOption(visualColumn, "Color-vision mode", "Select the adjustment matrix.",
            ["Deuteranopia", "Protanopia", "Tritanopia"],
            (int)_working.Accessibility.ColorVisionMode,
            index => _working.Accessibility.ColorVisionMode = (ColorVisionMode)index);
        AddSlider(visualColumn, "Text scale", "Scale interface typography.",
            _working.Accessibility.TextScale, 0.8, 1.5, 0.05,
            value => _working.Accessibility.TextScale = (float)value);
        AddCheck(visualColumn, "High contrast", "Use stronger UI contrast.",
            _working.Accessibility.HighContrast,
            value => _working.Accessibility.HighContrast = value);
        AddCheck(visualColumn, "Dyslexia-friendly text",
            "Increase spacing and favor simple text presentation.",
            _working.Accessibility.DyslexiaFriendlyText,
            value => _working.Accessibility.DyslexiaFriendlyText = value);

        AddCheck(supportColumn, "Screen reader",
            "Announce supported interface changes with operating-system TTS.",
            _working.Accessibility.EnableScreenReader,
            value => _working.Accessibility.EnableScreenReader = value);
        AddCheck(supportColumn, "Subtitles", "Allow dialogue systems to show subtitles.",
            _working.Accessibility.EnableSubtitles,
            value => _working.Accessibility.EnableSubtitles = value);
        AddSlider(supportColumn, "Subtitle scale", "Scale subtitle presentation.",
            _working.Accessibility.SubtitleScale, 0.8, 1.5, 0.05,
            value => _working.Accessibility.SubtitleScale = (float)value);
        AddSlider(supportColumn, "Subtitle background", "Opacity behind subtitle text.",
            _working.Accessibility.SubtitleBackgroundOpacity, 0, 1, 0.05,
            value => _working.Accessibility.SubtitleBackgroundOpacity = (float)value);
        AddCheck(supportColumn, "Reduce motion",
            "Disable camera shake and camera bob movement.",
            _working.Accessibility.ReduceMotion,
            value => _working.Accessibility.ReduceMotion = value);
        AddSlider(supportColumn, "Screen shake", "Camera-shake intensity multiplier.",
            _working.Accessibility.ScreenShakeIntensity, 0, 1, 0.05,
            value => _working.Accessibility.ScreenShakeIntensity = (float)value);
        AddCheck(supportColumn, "Reduce flashing",
            "Ask effects systems to avoid rapid flashes and intense flicker.",
            _working.Accessibility.ReduceFlashing,
            value => _working.Accessibility.ReduceFlashing = value);
    }

    private void AddGameplayTab()
    {
        VBoxContainer page = CreatePage("Gameplay");
        (VBoxContainer gameColumn, VBoxContainer interfaceColumn) =
            CreateTwoColumnLayout(page, "GAME", "GUIDANCE & ASSISTS");

        AddOption(gameColumn, "Difficulty", "Difficulty used by gameplay systems.",
            ["Easy", "Normal", "Hard", "Hardcore"], (int)_working.Gameplay.Difficulty,
            index => _working.Gameplay.Difficulty = (DifficultyLevel)index);
        AddSlider(gameColumn, "Game speed", "Global simulation speed multiplier.",
            _working.Gameplay.GameSpeed, 0.5, 2, 0.1,
            value => _working.Gameplay.GameSpeed = (float)value);
        AddSlider(gameColumn, "Camera bob", "Movement-driven camera bob intensity.",
            _working.Gameplay.CameraBobIntensity, 0, 1.5, 0.05,
            value => _working.Gameplay.CameraBobIntensity = (float)value);

        AddCheck(interfaceColumn, "Tutorials", "Show tutorial guidance.",
            _working.Gameplay.EnableTutorials, value => _working.Gameplay.EnableTutorials = value);
        AddCheck(interfaceColumn, "Interaction prompts",
            "Show contextual interaction prompts.",
            _working.Gameplay.ShowInteractionPrompts,
            value => _working.Gameplay.ShowInteractionPrompts = value);
        AddCheck(interfaceColumn, "Objective markers",
            "Show navigation markers for active objectives.",
            _working.Gameplay.ShowObjectiveMarkers,
            value => _working.Gameplay.ShowObjectiveMarkers = value);
        AddCheck(interfaceColumn, "Highlight interactables",
            "Allow gameplay to outline nearby usable objects.",
            _working.Gameplay.HighlightInteractables,
            value => _working.Gameplay.HighlightInteractables = value);
        AddSlider(interfaceColumn, "Aim assist", "Controller aim-assist strength.",
            _working.Gameplay.AimAssistStrength, 0, 1, 0.05,
            value => _working.Gameplay.AimAssistStrength = (float)value);
    }

    private void AddControlsTab()
    {
        VBoxContainer page = CreatePage("Controls");
        (VBoxContainer inputColumn, VBoxContainer bindingsColumn) =
            CreateTwoColumnLayout(page, "INPUT", "KEY BINDINGS");

        AddCheck(inputColumn, "Gamepad", "Apply gamepad bindings and preferences.",
            _working.Controls.GamepadEnabled, value => _working.Controls.GamepadEnabled = value);
        AddSlider(inputColumn, "Gamepad sensitivity", "Gamepad look and camera multiplier.",
            _working.Controls.GamepadSensitivity, 0.1, 3, 0.1,
            value => _working.Controls.GamepadSensitivity = (float)value);
        AddSlider(inputColumn, "Mouse sensitivity", "Mouse look and camera multiplier.",
            _working.Controls.MouseSensitivity, 0.1, 3, 0.1,
            value => _working.Controls.MouseSensitivity = (float)value);
        AddCheck(inputColumn, "Invert Y axis", "Invert vertical look input.",
            _working.Controls.InvertYAxis, value => _working.Controls.InvertYAxis = value);
        AddSlider(inputColumn, "Controller deadzone", "InputMap action deadzone.",
            _working.Controls.ControllerDeadzone, 0, 0.5, 0.01,
            value => _working.Controls.ControllerDeadzone = (float)value);
        AddCheck(inputColumn, "Controller vibration", "Enable gamepad vibration feedback.",
            _working.Controls.GamepadVibration,
            value => _working.Controls.GamepadVibration = value);
        AddSlider(inputColumn, "Vibration strength", "Maximum gamepad vibration intensity.",
            _working.Controls.VibrationStrength, 0, 1, 0.05,
            value => _working.Controls.VibrationStrength = (float)value);
        AddCheck(inputColumn, "Mouse smoothing",
            "Expose smoothed pointer input to camera systems.",
            _working.Controls.MouseSmoothing,
            value => _working.Controls.MouseSmoothing = value);
        AddCheck(inputColumn, "Toggle sprint",
            "Press sprint once to toggle instead of holding it.",
            _working.Controls.ToggleSprint,
            value => _working.Controls.ToggleSprint = value);

        foreach (string action in _bindings.Select(binding => binding.Action).Distinct())
        {
            Button button = new()
            {
                Text = string.Join("  /  ", _bindings.Where(binding => binding.Action == action)
                    .Select(DescribeBinding)),
                CustomMinimumSize = new Vector2(260, 42)
            };
            string capturedAction = action;
            button.Pressed += () => StartCapture(capturedAction, button);
            AddRow(bindingsColumn, Humanize(action),
                "Select, then press a key, mouse button, or gamepad button.", button);
        }
    }

    private VBoxContainer CreatePage(string title)
    {
        ScrollContainer scroll = new()
        {
            Name = title,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        VBoxContainer content = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        content.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(content);
        _tabs.AddChild(scroll);
        return content;
    }

    private static (VBoxContainer Left, VBoxContainer Right) CreateTwoColumnLayout(
        VBoxContainer page, string leftTitle, string rightTitle)
    {
        GridContainer columns = new()
        {
            Columns = 3,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        columns.AddThemeConstantOverride("h_separation", 18);
        columns.AddThemeConstantOverride("v_separation", 18);
        page.AddChild(columns);

        VBoxContainer left = CreateSettingsColumn(leftTitle);
        VBoxContainer right = CreateSettingsColumn(rightTitle);
        ColorRect divider = new()
        {
            Color = new Color(0.18f, 0.29f, 0.12f, 0.72f),
            CustomMinimumSize = new Vector2(1, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        columns.AddChild(left);
        columns.AddChild(divider);
        columns.AddChild(right);
        void UpdateColumns()
        {
            bool wide = page.GetViewportRect().Size.X >= 1100;
            columns.Columns = wide ? 3 : 1;
            divider.Visible = wide;
        }
        page.Resized += UpdateColumns;
        UpdateColumns();
        return (left, right);
    }

    private static VBoxContainer CreateSettingsColumn(string title)
    {
        VBoxContainer column = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.0f,
            CustomMinimumSize = new Vector2(420, 0)
        };
        column.AddThemeConstantOverride("separation", 10);

        HBoxContainer heading = new();
        heading.AddThemeConstantOverride("separation", 12);
        Label label = new()
        {
            Text = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(title.ToLowerInvariant()),
            VerticalAlignment = VerticalAlignment.Center
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_color", new Color("a6c94a"));

        ColorRect rule = new()
        {
            Color = new Color(0.35f, 0.48f, 0.16f, 0.8f),
            CustomMinimumSize = new Vector2(0, 1),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore
        };
        heading.AddChild(label);
        heading.AddChild(rule);
        column.AddChild(heading);
        return column;
    }

    private void RebuildCategoryButtons(int selectedIndex)
    {
        foreach (Node child in _categoryButtons.GetChildren())
        {
            _categoryButtons.RemoveChild(child);
            child.QueueFree();
        }

        ButtonGroup group = new() { AllowUnpress = false };
        for (int index = 0; index < _tabs.GetChildCount(); index++)
        {
            int tabIndex = index;
            Button button = new()
            {
                Text = _tabs.GetChild(index).Name.ToString(),
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = index == selectedIndex,
                CustomMinimumSize = new Vector2(128, 44)
            };
            button.Pressed += () => _tabs.CurrentTab = tabIndex;
            _categoryButtons.AddChild(button);
        }
    }

    private static Button CreateCompactButton(string text, float width) => new()
    {
        Text = text,
        CustomMinimumSize = new Vector2(width, 42)
    };

    private HSlider AddSlider(VBoxContainer page, string title, string description,
        double value, double minimum, double maximum, double step, Action<double> changed)
    {
        VBoxContainer wrapper = new();
        HBoxContainer valueRow = new();
        HSlider slider = new()
        {
            MinValue = minimum,
            MaxValue = maximum,
            Step = step,
            Value = value,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(260, 34)
        };
        Label valueLabel = new()
        {
            Text = FormatValue(value, step),
            CustomMinimumSize = new Vector2(72, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        valueRow.AddChild(slider);
        valueRow.AddChild(valueLabel);
        wrapper.AddChild(valueRow);
        AddRow(page, title, description, wrapper);
        slider.ValueChanged += newValue =>
        {
            if (_refreshing)
                return;
            changed(newValue);
            valueLabel.Text = FormatValue(newValue, step);
            MarkChanged();
        };
        return slider;
    }

    private CheckBox AddCheck(VBoxContainer page, string title, string description,
        bool value, Action<bool> changed)
    {
        CheckBox check = new() { ButtonPressed = value, Text = value ? "ON" : "OFF" };
        AddRow(page, title, description, check);
        check.Toggled += enabled =>
        {
            if (_refreshing)
                return;
            check.Text = enabled ? "ON" : "OFF";
            changed(enabled);
            MarkChanged();
        };
        return check;
    }

    private OptionButton AddOption(VBoxContainer page, string title, string description,
        IReadOnlyList<string> options, int selected, Action<int> changed)
    {
        OptionButton option = new() { CustomMinimumSize = new Vector2(260, 42) };
        foreach (string text in options)
            option.AddItem(text);
        option.Select(Math.Clamp(selected, 0, Math.Max(0, options.Count - 1)));
        AddRow(page, title, description, option);
        option.ItemSelected += index =>
        {
            if (_refreshing)
                return;
            changed((int)index);
            MarkChanged();
        };
        return option;
    }

    private static void AddRow(VBoxContainer page, string title, string description, Control editor)
    {
        PanelContainer panel = new();
        panel.AddThemeStyleboxOverride("panel", CreateRowStyle());
        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        panel.AddChild(margin);
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 24);
        margin.AddChild(row);
        VBoxContainer copy = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Label titleLabel = new()
        {
            Text = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(title)
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 17);
        Label descriptionLabel = new()
        {
            Text = description,
            Modulate = new Color(0.62f, 0.62f, 0.66f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        copy.AddChild(titleLabel);
        copy.AddChild(descriptionLabel);
        row.AddChild(copy);
        editor.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(editor);
        page.AddChild(panel);
    }

    private void MarkChanged()
    {
        _dirty = true;
        _service.Preview(_working, _bindings);
    }

    private void ApplyAndSave()
    {
        bool saved = _service.Commit(_working, _bindings);
        _dirty = !saved;
        if (!saved)
            ShowMessage("Save failed", "Settings could not be saved.");
        PopulateProfiles();
    }

    private void ResetToDefaults()
    {
        _service.ResetWorkingCopy(out _working, out _bindings);
        _dirty = true;
        RebuildTabs();
    }

    private void RequestClose()
    {
        if (!_dirty)
        {
            Close();
            return;
        }

        ConfirmationDialog dialog = new()
        {
            Title = "Discard changes?",
            DialogText = "Unsaved settings will be restored to the last applied values.",
            OkButtonText = "Discard"
        };
        dialog.Confirmed += () =>
        {
            _service.RollbackPreview();
            Close();
        };
        dialog.Canceled += dialog.QueueFree;
        dialog.Confirmed += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(440, 180));
    }

    private void Close()
    {
        Closed?.Invoke();
        QueueFree();
    }

    private void PopulateProfiles()
    {
        _refreshing = true;
        _profileSelect.Clear();
        int selected = 0;
        int index = 0;
        foreach (string profile in _service.ProfileNames)
        {
            _profileSelect.AddItem(profile);
            _profileSelect.SetItemMetadata(index, profile);
            if (string.Equals(profile, _service.CurrentProfileName, StringComparison.OrdinalIgnoreCase))
                selected = index;
            index++;
        }
        _profileSelect.Select(selected);
        _refreshing = false;
    }

    private void PopulatePresets()
    {
        _presetSelect.Clear();
        foreach (string preset in new[] { "Performance", "Balanced", "Quality", "Ultra" })
            _presetSelect.AddItem(preset);
        _presetSelect.Select(1);
    }

    private void ProfileSelected(long index)
    {
        if (_refreshing)
            return;

        string profile = _profileSelect.GetItemMetadata((int)index).AsString();
        if (!_dirty)
        {
            LoadProfile(profile);
            return;
        }

        _pendingProfile = profile;
        ConfirmationDialog dialog = new()
        {
            Title = "Switch profile?",
            DialogText = "Discard unsaved changes and load the selected profile?",
            OkButtonText = "Switch"
        };
        dialog.Confirmed += () =>
        {
            if (_pendingProfile is not null)
                LoadProfile(_pendingProfile);
            dialog.QueueFree();
        };
        dialog.Canceled += () =>
        {
            PopulateProfiles();
            dialog.QueueFree();
        };
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(440, 180));
    }

    private void LoadProfile(string profile)
    {
        if (!_service.SelectProfile(profile))
        {
            ShowMessage("Profile error", "The selected profile could not be loaded.");
            PopulateProfiles();
            return;
        }

        _working = _service.CreateWorkingCopy();
        _bindings = _service.CreateBindingWorkingCopy();
        _dirty = false;
        PopulateProfiles();
        RebuildTabs();
    }

    private void ShowNewProfileDialog()
    {
        AcceptDialog dialog = new()
        {
            Title = "New profile",
            DialogText = "Create a profile from the current working settings."
        };
        LineEdit name = new()
        {
            PlaceholderText = "Profile name",
            CustomMinimumSize = new Vector2(360, 42)
        };
        dialog.AddChild(name);
        dialog.Confirmed += () =>
        {
            if (!_service.CreateProfile(name.Text, _working, _bindings, out string error))
            {
                ShowMessage("Profile error", error);
            }
            else
            {
                _dirty = false;
                PopulateProfiles();
            }
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(460, 220));
        name.GrabFocus();
    }

    private void DeleteSelectedProfile()
    {
        string profile = _profileSelect.GetItemMetadata(_profileSelect.Selected).AsString();
        ConfirmationDialog dialog = new()
        {
            Title = "Delete profile?",
            DialogText = $"Delete profile “{profile}”? This cannot be undone.",
            OkButtonText = "Delete"
        };
        dialog.Confirmed += () =>
        {
            if (!_service.DeleteProfile(profile, out string error))
                ShowMessage("Profile error", error);
            _working = _service.CreateWorkingCopy();
            _bindings = _service.CreateBindingWorkingCopy();
            _dirty = false;
            PopulateProfiles();
            RebuildTabs();
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(440, 180));
    }

    private void SaveCurrentProfile()
    {
        bool saved = _service.OverwriteCurrentProfile(_working, _bindings);
        _dirty = !saved;
        if (!saved)
            ShowMessage("Save failed", "The current profile could not be saved.");
        PopulateProfiles();
    }

    private void ApplySelectedPreset()
    {
        string preset = _presetSelect.GetItemText(_presetSelect.Selected);
        _working = _service.ApplyPreset(preset, _working);
        _dirty = true;
        RebuildTabs();
    }

    private void BeginDisplayPreview()
    {
        _displayRollback ??= _service.CreateWorkingCopy().Graphics;
        CallDeferred(MethodName.ShowDisplayConfirmation);
    }

    private void ShowDisplayConfirmation()
    {
        if (_displayDialog is not null && IsInstanceValid(_displayDialog))
            return;

        _displaySecondsRemaining = 10;
        _displayDialog = new ConfirmationDialog
        {
            Title = "Keep display settings?",
            OkButtonText = "Keep"
        };
        _displayDialog.DialogText = $"Reverting in {_displaySecondsRemaining} seconds.";
        _displayDialog.Confirmed += KeepDisplayPreview;
        _displayDialog.Canceled += RevertDisplayPreview;
        AddChild(_displayDialog);
        _displayDialog.PopupCentered(new Vector2I(420, 180));

        _displayTimer = new Godot.Timer { WaitTime = 1, OneShot = false };
        _displayTimer.Timeout += DisplayCountdown;
        AddChild(_displayTimer);
        _displayTimer.Start();
    }

    private void DisplayCountdown()
    {
        _displaySecondsRemaining--;
        if (_displayDialog is not null)
            _displayDialog.DialogText = $"Reverting in {_displaySecondsRemaining} seconds.";
        if (_displaySecondsRemaining <= 0)
            RevertDisplayPreview();
    }

    private void KeepDisplayPreview()
    {
        _displayRollback = null;
        DisposeDisplayConfirmation();
    }

    private void RevertDisplayPreview()
    {
        if (_displayRollback is not null)
            _working.Graphics = _displayRollback;
        _displayRollback = null;
        _service.Preview(_working, _bindings);
        DisposeDisplayConfirmation();
        RebuildTabs();
    }

    private void DisposeDisplayConfirmation()
    {
        _displayTimer?.Stop();
        _displayTimer?.QueueFree();
        _displayTimer = null;
        _displayDialog?.QueueFree();
        _displayDialog = null;
    }

    private void StartCapture(string action, Button button)
    {
        _service.IsCapturingBinding = true;
        _capturingAction = action;
        _capturingButton = button;
        button.Text = "PRESS A KEY OR BUTTON — ESC TO CANCEL";
        SetProcessUnhandledInput(true);
    }

    private void FinishCapture()
    {
        _service.IsCapturingBinding = false;
        _capturingAction = null;
        _capturingButton = null;
        SetProcessUnhandledInput(false);
    }

    private void ShowMessage(string title, string message)
    {
        AcceptDialog dialog = new() { Title = title, DialogText = message };
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(440, 180));
    }

    private static StyleBoxFlat CreateRowStyle() => new()
    {
        BgColor = new Color(0.012f, 0.013f, 0.016f, 0.96f),
        BorderColor = new Color(0.18f, 0.29f, 0.12f, 0.92f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 3,
        CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3,
        CornerRadiusBottomRight = 3
    };

    private static int ResolutionIndex(int width, int height) => (width, height) switch
    {
        (1280, 720) => 0,
        (1600, 900) => 1,
        (2560, 1440) => 3,
        _ => 2
    };

    private static int ShadowIndex(int size) => size switch
    {
        <= 512 => 0,
        <= 1024 => 1,
        >= 4096 => 3,
        _ => 2
    };

    private static string FormatValue(double value, double step) =>
        step < 1 ? value.ToString("0.00") : value.ToString("0");

    private static string Humanize(string value) =>
        string.Join(" ", value.Split('_').Select(word =>
            string.IsNullOrEmpty(word) ? word : char.ToUpperInvariant(word[0]) + word[1..]));

    private static string DescribeBinding(InputBinding binding) => binding.Device switch
    {
        InputDeviceKind.Keyboard => OS.GetKeycodeString((Key)binding.Code),
        InputDeviceKind.Mouse => $"Mouse {(MouseButton)binding.Code}",
        InputDeviceKind.Gamepad => $"Gamepad {(JoyButton)binding.Code}",
        _ => "Unbound"
    };
}
