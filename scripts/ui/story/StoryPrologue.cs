using Godot;
using SolarixFailure.Domain;

namespace SolarixFailure;

public partial class StoryPrologue : Control
{
    private const double SkipHoldSeconds = 1.25;
    private const double SkipPromptVisibleSeconds = 3.0;

    private VideoStreamPlayer _player = null!;
    private Control _skipPrompt = null!;
    private CircularHoldIndicator _skipProgress = null!;
    private Tween? _skipTween;
    private Tween? _promptTween;
    private Tween? _promptHideTween;
    private bool _skipHeld;
    private bool _transitionStarted;

    public override void _Ready()
    {
        _player = GetNode<VideoStreamPlayer>("PrologueVideo");
        _skipPrompt = GetNode<Control>("%SkipPrompt");
        _skipProgress = GetNode<CircularHoldIndicator>("%SkipProgress");
        GetNode<Label>("%SkipLabel").Text = Tr("PROLOGUE_SKIP_PROMPT");
        GetNode<Label>("%SkipKey").Text = Tr("PROLOGUE_SKIP_KEY");
        _player.Finished += FinishPrologue;
        _skipPrompt.Visible = false;
        _skipPrompt.Modulate = new Color(1, 1, 1, 0);

        if (SaveGameService.Instance.ActiveSession is null)
            SaveGameService.Instance.StartNewRun();

        if (_player.Stream is null)
        {
            GD.PushError("The Solarix prologue video is missing.");
            return;
        }

        _player.Play();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_transitionStarted)
            return;

        if (IsPromptRevealInput(inputEvent))
            RevealSkipPrompt();

        if (inputEvent is InputEventKey { Echo: false } key
            && (key.Keycode == Key.E || key.PhysicalKeycode == Key.E))
        {
            if (key.Pressed)
                StartSkipHold();
            else
                CancelSkipHold();

            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        _skipTween?.Kill();
        _promptTween?.Kill();
        _promptHideTween?.Kill();
        if (IsInstanceValid(_player))
            _player.Finished -= FinishPrologue;
    }

    private void RevealSkipPrompt()
    {
        _promptTween?.Kill();
        _promptHideTween?.Kill();
        _skipPrompt.Visible = true;

        _promptTween = CreateTween();
        _promptTween.SetTrans(Tween.TransitionType.Cubic);
        _promptTween.SetEase(Tween.EaseType.Out);
        _promptTween.TweenProperty(_skipPrompt, "modulate:a", 1.0, 0.2);
        if (!_skipHeld)
            SchedulePromptHide();
    }

    private void StartSkipHold()
    {
        if (_skipHeld)
            return;

        _skipHeld = true;
        RevealSkipPrompt();
        _promptHideTween?.Kill();
        _skipTween?.Kill();
        double remainingDuration = SkipHoldSeconds * (1.0 - (_skipProgress.Value / 100.0));
        _skipTween = CreateTween();
        _skipTween.SetTrans(Tween.TransitionType.Sine);
        _skipTween.SetEase(Tween.EaseType.InOut);
        _skipTween.TweenMethod(
            Callable.From<double>(SetSkipProgress),
            _skipProgress.Value,
            100.0,
            remainingDuration);
        _skipTween.TweenCallback(Callable.From(FinishPrologue));
    }

    private void CancelSkipHold()
    {
        if (!_skipHeld)
            return;

        _skipHeld = false;
        _skipTween?.Kill();
        _skipTween = CreateTween();
        _skipTween.SetTrans(Tween.TransitionType.Cubic);
        _skipTween.SetEase(Tween.EaseType.Out);
        _skipTween.TweenMethod(
            Callable.From<double>(SetSkipProgress),
            _skipProgress.Value,
            0.0,
            0.18);
        SchedulePromptHide(1.2);
    }

    private void SchedulePromptHide(double delay = SkipPromptVisibleSeconds)
    {
        _promptHideTween?.Kill();
        _promptHideTween = CreateTween();
        _promptHideTween.TweenInterval(delay);
        _promptHideTween.SetTrans(Tween.TransitionType.Cubic);
        _promptHideTween.SetEase(Tween.EaseType.In);
        _promptHideTween.TweenProperty(_skipPrompt, "modulate:a", 0.0, 0.28);
        _promptHideTween.TweenCallback(Callable.From(() => _skipPrompt.Visible = false));
    }

    private void SetSkipProgress(double value) => _skipProgress.Value = value;

    private static bool IsPromptRevealInput(InputEvent inputEvent) => inputEvent switch
    {
        InputEventKey { Pressed: true, Echo: false } => true,
        InputEventMouseButton { Pressed: true } => true,
        InputEventJoypadButton { Pressed: true } => true,
        _ => false
    };

    private void FinishPrologue()
    {
        if (_transitionStarted)
            return;

        _transitionStarted = true;
        _skipTween?.Kill();
        _promptTween?.Kill();
        _promptHideTween?.Kill();
        _player.Stop();
        SaveGameService.Instance.WriteCheckpoint(
            GameFlowStage.OriginSelection, label: Tr("SAVE_PROLOGUE_COMPLETE"));
        _ = SceneFlowService.Instance.GoToAsync(GameRoute.OriginSelection);
    }
}
