using Godot;
using SolarixFailure.Domain;
using SolarixFailure.UI;

namespace SolarixFailure;

public partial class TitleMenu : MenuScreenBase
{
    private static readonly PackedScene SettingsScene =
        GD.Load<PackedScene>("res://scenes/overlays/settings_menu.tscn");
    private Control? _settings;

    public override void _Ready()
    {
        VBoxContainer content = BuildScreen(string.Empty, width: 920);
        TextureRect logo = new()
        {
            Texture = GD.Load<Texture2D>("res://assets/branding/solarix_failure_official.svg"),
            CustomMinimumSize = new Vector2(0, 300),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        content.AddChild(logo);
        VBoxContainer buttons = new()
        {
            CustomMinimumSize = new Vector2(520, 0),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter
        };
        buttons.AddThemeConstantOverride("separation", 10);
        content.AddChild(buttons);
        AddMenuButton(buttons, "MENU_PLAY",
            () => FireAndForget(SceneFlowService.Instance.GoToAsync(GameRoute.PlayMenu)));
        AddMenuButton(buttons, "MENU_SETTINGS", OpenSettings);
        AddMenuButton(buttons, "MENU_QUIT", Quit);
        string version = ProjectSettings.GetSetting("application/config/version", "unknown").AsString();
        Label versionLabel = new()
        {
            Text = $"{Tr("MENU_VERSION")} {version}",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        versionLabel.AddThemeFontSizeOverride("font_size", 13);
        content.AddChild(versionLabel);
        ActivateMenuFocus();
    }

    private void OpenSettings()
    {
        if (_settings is not null && IsInstanceValid(_settings))
            return;
        SettingsMenu? menu = SettingsScene.InstantiateOrNull<SettingsMenu>();
        if (menu is null)
            return;
        _settings = menu;
        menu.Closed += () => _settings = null;
        AddChild(menu);
    }

    private static void Quit()
    {
        SettingsService.Instance.Save();
        SaveGameService.Instance?.WriteCheckpoint(
            SaveGameService.Instance.ActiveSession?.Stage ?? GameFlowStage.StoryPrologue);
        ((SceneTree)Engine.GetMainLoop()).Quit();
    }
}
