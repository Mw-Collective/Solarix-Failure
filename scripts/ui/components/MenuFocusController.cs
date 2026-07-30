using Godot;

namespace SolarixFailure.UI;

public partial class MenuFocusController : Node
{
    private const double KeyboardTimeout = 10.0;
    private Button[] _buttons = [];
    private Godot.Timer _timer = null!;
    private bool _visualsVisible = true;
    private bool _controllerMode;

    public override void _Ready()
    {
        _timer = new Godot.Timer { OneShot = true, WaitTime = KeyboardTimeout };
        _timer.Timeout += () =>
        {
            if (!_controllerMode)
                Hide(releaseFocus: true);
        };
        AddChild(_timer);
    }

    public void Configure(IEnumerable<Button> buttons)
    {
        _buttons = buttons.Where(IsInstanceValid).ToArray();
        Hide(releaseFocus: true);
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_buttons.Length == 0)
            return;
        switch (inputEvent)
        {
            case InputEventMouseMotion motion:
                _controllerMode = false;
                _timer.Stop();
                Hide(motion.ButtonMask == (MouseButtonMask)0);
                break;
            case InputEventMouseButton:
                _controllerMode = false;
                _timer.Stop();
                Hide(releaseFocus: false);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                Key code = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
                if (code is Key.Up or Key.Down)
                {
                    _controllerMode = false;
                    Show();
                    EnsureFocus(code == Key.Up ? _buttons[^1] : _buttons[0]);
                    _timer.Start();
                }
                break;
            case InputEventJoypadButton { Pressed: true }:
            case InputEventJoypadMotion { AxisValue: > 0.45f or < -0.45f }:
                _controllerMode = true;
                _timer.Stop();
                Show();
                EnsureFocus(_buttons[0]);
                break;
        }
    }

    private void EnsureFocus(Button fallback)
    {
        Control? owner = GetViewport().GuiGetFocusOwner();
        if (owner is null || !_buttons.Contains(owner))
            fallback.GrabFocus();
    }

    private void Hide(bool releaseFocus)
    {
        if (_visualsVisible)
        {
            _visualsVisible = false;
            foreach (Button button in _buttons)
                button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        }
        if (releaseFocus && GetViewport().GuiGetFocusOwner() is Control owner
            && _buttons.Contains(owner))
            owner.ReleaseFocus();
    }

    private void Show()
    {
        if (_visualsVisible)
            return;
        _visualsVisible = true;
        foreach (Button button in _buttons)
            button.AddThemeStyleboxOverride("focus",
                (StyleBox)button.GetThemeStylebox("hover").Duplicate());
    }
}
