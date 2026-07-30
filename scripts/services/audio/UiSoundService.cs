using Godot;

namespace SolarixFailure;

public partial class UiSoundService : Node
{
    private const string HoverSoundPath = "res://assets/audio/ui/button_hover.wav";
    private const string FocusSoundPath = "res://assets/audio/ui/button_focus.wav";
    private const string PressSoundPath = "res://assets/audio/ui/button_press.wav";

    private readonly HashSet<ulong> _wiredButtons = [];
    private AudioStreamPlayer _hoverPlayer = null!;
    private AudioStreamPlayer _focusPlayer = null!;
    private AudioStreamPlayer _pressPlayer = null!;
    private InputMode _inputMode = InputMode.Pointer;

    private enum InputMode
    {
        Pointer,
        Navigation
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _hoverPlayer = CreatePlayer(HoverSoundPath, -5.0f);
        _focusPlayer = CreatePlayer(FocusSoundPath, -4.0f);
        _pressPlayer = CreatePlayer(PressSoundPath, -2.5f);

        GetTree().NodeAdded += WireNode;
        WireTree(GetTree().Root);
    }

    public override void _ExitTree()
    {
        if (GetTree() is { } tree)
            tree.NodeAdded -= WireNode;
    }

    public override void _Input(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventMouseMotion:
            case InputEventMouseButton:
                _inputMode = InputMode.Pointer;
                break;
            case InputEventJoypadButton { Pressed: true }:
            case InputEventJoypadMotion { AxisValue: > 0.45f or < -0.45f }:
                _inputMode = InputMode.Navigation;
                break;
            case InputEventKey { Pressed: true } key when IsNavigationKey(key.Keycode):
                _inputMode = InputMode.Navigation;
                break;
        }
    }

    private AudioStreamPlayer CreatePlayer(string streamPath, float volumeDb)
    {
        AudioStream? stream = GD.Load<AudioStream>(streamPath);
        if (stream is null)
            GD.PushError($"UI sound could not be loaded: {streamPath}");

        AudioStreamPlayer player = new()
        {
            Stream = stream,
            Bus = "UI",
            VolumeDb = volumeDb,
            ProcessMode = ProcessModeEnum.Always
        };
        AddChild(player);
        return player;
    }

    private void WireTree(Node node)
    {
        WireNode(node);
        foreach (Node child in node.GetChildren())
            WireTree(child);
    }

    private void WireNode(Node node)
    {
        if (node is not BaseButton button || !_wiredButtons.Add(button.GetInstanceId()))
            return;

        button.MouseEntered += () => PlayHover(button);
        button.FocusEntered += () => PlayFocus(button);
        button.Pressed += PlayPress;
        button.TreeExiting += () => _wiredButtons.Remove(button.GetInstanceId());
    }

    private void PlayHover(BaseButton button)
    {
        if (_inputMode == InputMode.Pointer && IsInteractive(button))
            _hoverPlayer.Play();
    }

    private void PlayFocus(BaseButton button)
    {
        if (_inputMode == InputMode.Navigation && IsInteractive(button))
            _focusPlayer.Play();
    }

    private void PlayPress() => _pressPlayer.Play();

    private static bool IsInteractive(BaseButton button) =>
        !button.Disabled && button.IsVisibleInTree();

    private static bool IsNavigationKey(Key key) =>
        key is Key.Up or Key.Down or Key.Left or Key.Right
            or Key.Tab or Key.Enter or Key.KpEnter or Key.Space;
}
