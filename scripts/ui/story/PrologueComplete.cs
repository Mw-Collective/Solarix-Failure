using Godot;
using SolarixFailure.Domain;
using SolarixFailure.UI;

namespace SolarixFailure;

public partial class PrologueComplete : MenuScreenBase
{
    public override void _Ready()
    {
        VBoxContainer content = BuildScreen("COMPLETE_TITLE", "COMPLETE_SUBTITLE", 760);
        AddBody(content, "COMPLETE_ORIGIN", 22);
        AddBody(content, "COMPLETE_BODY", 18);
        AddMenuButton(content, "COMPLETE_RETURN",
            () => FireAndForget(SceneFlowService.Instance.GoToAsync(GameRoute.PlayMenu)));
        ActivateMenuFocus();
    }
}
