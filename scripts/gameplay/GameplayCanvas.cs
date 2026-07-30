using Godot;

namespace SolarixFailure;

public partial class GameplayCanvas : Control
{
    [Export] public Texture2D? FrontIdleSheet { get; set; }
    [Export] public Texture2D? BackIdleSheet { get; set; }
    [Export] public Texture2D? SideIdleSheet { get; set; }
    [Export] public Texture2D? FrontWalkSheet { get; set; }
    [Export] public Texture2D? BackWalkSheet { get; set; }
    [Export] public Texture2D? SideWalkSheet { get; set; }
    [Export(PropertyHint.Range, "50,600,5")] public float WalkSpeed { get; set; } = 220.0f;

    private Camera2D _camera = null!;
    private AnimatedSpriteSheetView _human = null!;
    private FacingDirection _facing = FacingDirection.Front;
    private bool _isWalking;

    private enum FacingDirection
    {
        Front,
        Back,
        Left,
        Right
    }

    public override void _Ready()
    {
        _camera = GetNode<Camera2D>("%Camera");
        _human = GetNode<AnimatedSpriteSheetView>("%Human");
        Resized += CenterCamera;
        CenterCamera();
    }

    public override void _Process(double delta)
    {
        Vector2 movement = Input.GetVector(
            "move_left",
            "move_right",
            "move_up",
            "move_down");

        bool isWalking = !movement.IsZeroApprox();
        if (isWalking)
        {
            _human.Position += movement * WalkSpeed * (float)delta;
            ClampCharacterToViewport();
        }

        if (_isWalking != isWalking)
        {
            _isWalking = isWalking;
            ApplyAnimationSheet();
        }
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed("move_up"))
            SetFacing(FacingDirection.Back);
        else if (inputEvent.IsActionPressed("move_down"))
            SetFacing(FacingDirection.Front);
        else if (inputEvent.IsActionPressed("move_left"))
            SetFacing(FacingDirection.Left);
        else if (inputEvent.IsActionPressed("move_right"))
            SetFacing(FacingDirection.Right);
    }

    public override void _ExitTree()
    {
        Resized -= CenterCamera;
    }

    private void CenterCamera()
    {
        _camera.Position = Size * 0.5f;
        ClampCharacterToViewport();
    }

    private void SetFacing(FacingDirection direction)
    {
        if (_facing == direction)
            return;

        _facing = direction;
        ApplyAnimationSheet();
    }

    private void ApplyAnimationSheet()
    {
        Texture2D? sheet = (_isWalking, _facing) switch
        {
            (true, FacingDirection.Front) => FrontWalkSheet,
            (true, FacingDirection.Back) => BackWalkSheet,
            (true, FacingDirection.Left or FacingDirection.Right) => SideWalkSheet,
            (false, FacingDirection.Front) => FrontIdleSheet,
            (false, FacingDirection.Back) => BackIdleSheet,
            (false, FacingDirection.Left or FacingDirection.Right) => SideIdleSheet,
            _ => null
        };
        if (sheet is null)
        {
            GD.PushError($"Missing {_facing} {(_isWalking ? "walk" : "idle")} sprite sheet.");
            return;
        }

        _human.SetSpriteSheet(sheet);
        _human.SetFramesPerSecond(_isWalking ? 10.0 : 6.0);
        _human.FlipH = _facing == FacingDirection.Left;
    }

    private void ClampCharacterToViewport()
    {
        if (_human is null)
            return;

        Vector2 maximum = (Size - _human.Size).Max(Vector2.Zero);
        _human.Position = _human.Position.Clamp(Vector2.Zero, maximum);
    }
}
