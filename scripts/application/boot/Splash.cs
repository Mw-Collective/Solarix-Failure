using Godot;

namespace SolarixFailure;

public partial class Splash : Control
{
    private const double CreditHoldSeconds = 1.8;
    private const double GameLogoHoldSeconds = 1.6;

    private static readonly PackedScene TitleMenuScene =
        GD.Load<PackedScene>("res://scenes/menus/title_menu.tscn");

    private AnimationPlayer _animationPlayer = null!;
    private AudioStreamPlayer _mwIdentSound = null!;
    private AudioStreamPlayer _solarixRevealSound = null!;
    private bool _transitionStarted;

    public override async void _Ready()
    {
        _animationPlayer = GetNode<AnimationPlayer>("%AnimationPlayer");
        _mwIdentSound = GetNode<AudioStreamPlayer>("%MwIdentSound");
        _solarixRevealSound = GetNode<AudioStreamPlayer>("%SolarixRevealSound");
        _animationPlayer.Play("RESET");
        _animationPlayer.Advance(0);

        try
        {
            _mwIdentSound.Play();
            await PlayAnimation("studio_ident");
            await ToSignal(GetTree().CreateTimer(CreditHoldSeconds), SceneTreeTimer.SignalName.Timeout);
            await PlayAnimation("studio_out");
            _solarixRevealSound.Play();
            await PlayAnimation("game_logo_in");
            await ToSignal(GetTree().CreateTimer(GameLogoHoldSeconds), SceneTreeTimer.SignalName.Timeout);
            await CrossfadeToMainMenu();
        }
        catch (Exception exception)
        {
            GD.PushError($"Launch sequence failed: {exception}");
            Modulate = Colors.White;
        }
    }

    private async Task PlayAnimation(StringName animationName)
    {
        _animationPlayer.Play(animationName);
        await ToSignal(_animationPlayer, AnimationMixer.SignalName.AnimationFinished);
    }

    private async Task CrossfadeToMainMenu()
    {
        if (_transitionStarted)
            return;

        _transitionStarted = true;
        if (TitleMenuScene is null)
            throw new InvalidOperationException("Title menu scene could not be loaded.");

        Control? menu = TitleMenuScene.InstantiateOrNull<Control>();
        if (menu is null)
            throw new InvalidOperationException("Title menu scene does not have a Control root.");

        menu.Modulate = new Color(1, 1, 1, 0);
        menu.ProcessMode = ProcessModeEnum.Disabled;
        menu.MouseFilter = MouseFilterEnum.Ignore;
        GetTree().Root.AddChild(menu);

        Tween transition = CreateTween().SetParallel();
        transition.SetTrans(Tween.TransitionType.Sine);
        transition.SetEase(Tween.EaseType.InOut);
        transition.TweenProperty(this, "modulate:a", 0.0, 0.46);
        transition.TweenProperty(menu, "modulate:a", 1.0, 0.46).SetDelay(0.29);
        await ToSignal(transition, Tween.SignalName.Finished);

        GetTree().CurrentScene = menu;
        menu.Modulate = Colors.White;
        menu.ProcessMode = ProcessModeEnum.Inherit;
        menu.MouseFilter = MouseFilterEnum.Stop;
        QueueFree();
    }
}
