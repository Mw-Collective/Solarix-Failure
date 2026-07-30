using Godot;
using SolarixFailure.Domain;

namespace SolarixFailure;

public partial class SceneFlowService : Node
{
    private static readonly IReadOnlyDictionary<GameRoute, string> Routes =
        new Dictionary<GameRoute, string>
        {
            [GameRoute.TitleMenu] = "res://scenes/menus/title_menu.tscn",
            [GameRoute.PlayMenu] = "res://scenes/menus/play_menu.tscn",
            [GameRoute.LoadGame] = "res://scenes/menus/load_game_menu.tscn",
            [GameRoute.StoryPrologue] = "res://scenes/story/story_prologue.tscn",
            [GameRoute.OriginSelection] = "res://scenes/story/origin_selection.tscn",
            [GameRoute.GameplayCanvas] = "res://scenes/gameplay/gameplay_canvas.tscn",
            [GameRoute.PrologueComplete] = "res://scenes/story/prologue_complete.tscn"
        };

    public static SceneFlowService Instance { get; private set; } = null!;
    private bool _transitioning;

    public override void _EnterTree() => Instance = this;

    public async Task<bool> GoToAsync(GameRoute route, double duration = 0.28)
    {
        if (_transitioning || !Routes.TryGetValue(route, out string? path))
            return false;
        _transitioning = true;
        try
        {
            PackedScene? packed = ResourceLoader.Load<PackedScene>(path);
            Node? next = packed?.Instantiate();
            if (next is null)
                throw new InvalidOperationException($"Could not instantiate route {route}.");

            CanvasLayer layer = new() { Layer = 1000 };
            ColorRect fade = new()
            {
                Color = new Color(0.0015f, 0.0045f, 0.0025f, 0),
                MouseFilter = Control.MouseFilterEnum.Stop
            };
            fade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(fade);
            GetTree().Root.AddChild(layer);
            Tween fadeOut = CreateTween();
            fadeOut.TweenProperty(fade, "color:a", 1.0, duration);
            await ToSignal(fadeOut, Tween.SignalName.Finished);

            Node? previous = GetTree().CurrentScene;
            GetTree().Root.AddChild(next);
            GetTree().CurrentScene = next;
            previous?.QueueFree();

            Tween fadeIn = CreateTween();
            fadeIn.TweenProperty(fade, "color:a", 0.0, duration);
            await ToSignal(fadeIn, Tween.SignalName.Finished);
            layer.QueueFree();
            return true;
        }
        catch (Exception exception)
        {
            GD.PushError($"Scene transition failed: {exception.Message}");
            return false;
        }
        finally
        {
            _transitioning = false;
        }
    }

    public Task<bool> ResumeAsync(SaveGameDocument save) => GoToAsync(save.Stage switch
    {
        GameFlowStage.StoryPrologue => GameRoute.StoryPrologue,
        GameFlowStage.OriginSelection => GameRoute.OriginSelection,
        GameFlowStage.GameplayCanvas => GameRoute.GameplayCanvas,
        GameFlowStage.PrologueComplete => GameRoute.GameplayCanvas,
        _ => GameRoute.PlayMenu
    });
}
