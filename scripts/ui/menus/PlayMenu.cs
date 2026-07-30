using Godot;
using SolarixFailure.Domain;
using SolarixFailure.UI;

namespace SolarixFailure;

public partial class PlayMenu : MenuScreenBase
{
    public override void _Ready()
    {
        VBoxContainer content = BuildScreen("PLAY_TITLE", "PLAY_SUBTITLE", 680);
        VBoxContainer buttons = new();
        buttons.AddThemeConstantOverride("separation", 10);
        content.AddChild(buttons);
        bool hasSaves = SaveGameService.Instance.HasValidSaves;
        Button continueButton = AddMenuButton(buttons, "PLAY_CONTINUE", Continue, hasSaves);
        continueButton.Visible = hasSaves;
        AddMenuButton(buttons, "PLAY_NEW_GAME", NewGame);
        AddMenuButton(buttons, "PLAY_LOAD_GAME",
            () => FireAndForget(SceneFlowService.Instance.GoToAsync(GameRoute.LoadGame)), hasSaves);
        Button multiplayer = AddMenuButton(buttons, "PLAY_MULTIPLAYER", enabled: false);
        multiplayer.TooltipText = Tr("PLAY_MULTIPLAYER_TOOLTIP");
        AddMenuButton(buttons, "MENU_BACK",
            () => FireAndForget(SceneFlowService.Instance.GoToAsync(GameRoute.TitleMenu)));
        ActivateMenuFocus();
    }

    private void Continue()
    {
        SaveGameDocument? save = SaveGameService.Instance.GetMostRecent();
        if (save is null || !SaveGameService.Instance.Load(save.SaveId))
            return;
        FireAndForget(SceneFlowService.Instance.ResumeAsync(save));
    }

    private void NewGame()
    {
        if (!SaveGameService.Instance.HasValidSaves)
        {
            BeginNewGame();
            return;
        }
        ConfirmationDialog dialog = new()
        {
            Title = Tr("NEW_GAME_CONFIRM_TITLE"),
            DialogText = Tr("NEW_GAME_CONFIRM_BODY"),
            OkButtonText = Tr("NEW_GAME_CONFIRM_ACTION")
        };
        dialog.Confirmed += BeginNewGame;
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(520, 210));
    }

    private void BeginNewGame()
    {
        SaveGameService.Instance.StartNewRun();
        FireAndForget(SceneFlowService.Instance.GoToAsync(GameRoute.StoryPrologue));
    }
}
