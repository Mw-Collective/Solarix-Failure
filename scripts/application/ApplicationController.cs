using Godot;

namespace SolarixFailure;

public partial class ApplicationController : Node
{
    private Vector2I _windowedSize;
    private Vector2I _windowedPosition;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Window window = GetTree().Root;
        _windowedSize = window.Size;
        _windowedPosition = window.Position;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } key
            || SettingsService.Instance.IsCapturingBinding
            || !SettingsService.Instance.IsDesktopDisplayControlSupported())
        {
            return;
        }

        bool fullscreenShortcut = key.Keycode == Key.F11
            || (key.AltPressed && key.Keycode is Key.Enter or Key.KpEnter);
        if (!fullscreenShortcut)
            return;

        ToggleFullscreen();
        GetViewport().SetInputAsHandled();
    }

    private void ToggleFullscreen()
    {
        Window window = GetTree().Root;
        bool entering = window.Mode == Window.ModeEnum.Windowed;
        if (entering)
        {
            _windowedSize = window.Size;
            _windowedPosition = window.Position;
        }

        SettingsService.Instance.SetFullscreenAndSave(entering);
        if (!entering)
        {
            window.Size = _windowedSize;
            window.Position = _windowedPosition;
        }
    }
}
