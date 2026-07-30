using Godot;

namespace SolarixFailure;

public partial class AnimatedSpriteSheetView : TextureRect
{
    [Export] public Texture2D? SpriteSheet { get; set; }
    [Export(PropertyHint.Range, "1,32,1")] public int Columns { get; set; } = 1;
    [Export(PropertyHint.Range, "1,32,1")] public int Rows { get; set; } = 1;
    [Export(PropertyHint.Range, "1,1024,1")] public int FrameCount { get; set; } = 1;
    [Export(PropertyHint.Range, "1,60,0.1")] public double FramesPerSecond { get; set; } = 6.0;

    private AtlasTexture _frameTexture = null!;
    private Godot.Timer _frameTimer = null!;
    private int _frame;

    public void Configure(Texture2D spriteSheet, int columns, int rows,
        int frameCount, double framesPerSecond)
    {
        SpriteSheet = spriteSheet;
        Columns = columns;
        Rows = rows;
        FrameCount = frameCount;
        FramesPerSecond = framesPerSecond;
    }

    public void SetSpriteSheet(Texture2D spriteSheet, bool restart = true)
    {
        SpriteSheet = spriteSheet;
        if (_frameTexture is null)
            return;

        _frameTexture.Atlas = spriteSheet;
        if (restart)
            _frame = 0;
        ShowFrame(_frame);
    }

    public void SetFramesPerSecond(double framesPerSecond)
    {
        FramesPerSecond = Math.Max(1.0, framesPerSecond);
        if (_frameTimer is not null)
            _frameTimer.WaitTime = 1.0 / FramesPerSecond;
    }

    public override void _Ready()
    {
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.KeepAspectCentered;
        MouseFilter = MouseFilterEnum.Ignore;

        if (SpriteSheet is null || Columns <= 0 || Rows <= 0)
        {
            GD.PushError("Animated sprite-sheet view is missing a valid sheet or grid.");
            return;
        }

        FrameCount = Math.Clamp(FrameCount, 1, Columns * Rows);
        _frameTexture = new AtlasTexture
        {
            Atlas = SpriteSheet,
            FilterClip = true
        };
        Texture = _frameTexture;
        ShowFrame(0);

        _frameTimer = new Godot.Timer
        {
            WaitTime = 1.0 / Math.Max(1.0, FramesPerSecond),
            OneShot = false,
            Autostart = true
        };
        _frameTimer.Timeout += AdvanceFrame;
        AddChild(_frameTimer);
    }

    private void AdvanceFrame()
    {
        _frame = (_frame + 1) % FrameCount;
        ShowFrame(_frame);
    }

    private void ShowFrame(int frame)
    {
        if (SpriteSheet is null)
            return;

        Vector2 frameSize = SpriteSheet.GetSize() / new Vector2(Columns, Rows);
        int column = frame % Columns;
        int row = frame / Columns;
        _frameTexture.Region = new Rect2(
            new Vector2(column * frameSize.X, row * frameSize.Y),
            frameSize);
    }
}
