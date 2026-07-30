using Godot;

namespace SolarixFailure;

public partial class OriginCarouselCard : PanelContainer
{
    private readonly StyleBoxFlat _panelStyle = new();
    private Label _nameLabel = null!;
    private Label _statusLabel = null!;
    private TextureRect _portrait = null!;
    private bool _available;

    public void Configure(string archiveId, string name, string tag, string description,
        Texture2D? portrait, bool available, bool animated = false)
    {
        _available = available;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        ClipContents = true;

        _panelStyle.BgColor = new Color(0.006f, 0.014f, 0.009f, 0.985f);
        _panelStyle.BorderColor = new Color(0.22f, 0.33f, 0.14f, 0.92f);
        _panelStyle.SetBorderWidthAll(1);
        _panelStyle.SetCornerRadiusAll(5);
        _panelStyle.ContentMarginLeft = 14;
        _panelStyle.ContentMarginRight = 14;
        _panelStyle.ContentMarginTop = 12;
        _panelStyle.ContentMarginBottom = 14;
        AddThemeStyleboxOverride("panel", _panelStyle);

        VBoxContainer layout = new();
        layout.AddThemeConstantOverride("separation", 7);
        AddChild(layout);

        HBoxContainer metadata = new();
        layout.AddChild(metadata);
        Label idLabel = CreateLabel(archiveId, 11, new Color(0.44f, 0.53f, 0.38f));
        metadata.AddChild(idLabel);
        Control spacer = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        metadata.AddChild(spacer);
        _statusLabel = CreateLabel(
            available ? Tr("ORIGIN_AVAILABLE") : Tr("ORIGIN_LOCKED"),
            11,
            available ? new Color(0.64f, 0.79f, 0.26f) : new Color(0.62f, 0.48f, 0.22f));
        metadata.AddChild(_statusLabel);

        if (animated && portrait is not null)
        {
            AnimatedSpriteSheetView animatedPortrait = new();
            animatedPortrait.Configure(portrait, 3, 3, 9, 6.0);
            _portrait = animatedPortrait;
        }
        else
        {
            _portrait = new TextureRect
            {
                Texture = portrait,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            };
        }
        _portrait.SizeFlagsVertical = SizeFlags.ExpandFill;
        _portrait.CustomMinimumSize = new Vector2(0, 180);
        _portrait.MouseFilter = MouseFilterEnum.Ignore;
        _portrait.Modulate = available
            ? Colors.White
            : new Color(0.5f, 0.55f, 0.49f, 0.7f);
        layout.AddChild(_portrait);

        ColorRect divider = new()
        {
            Color = available
                ? new Color(0.45f, 0.62f, 0.15f, 0.78f)
                : new Color(0.34f, 0.3f, 0.17f, 0.62f),
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = MouseFilterEnum.Ignore
        };
        layout.AddChild(divider);

        _nameLabel = CreateLabel(name, 29, new Color(0.84f, 0.87f, 0.8f));
        _nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        layout.AddChild(_nameLabel);

        Label tagLabel = CreateLabel(tag.ToUpperInvariant(), 12,
            available ? new Color(0.56f, 0.68f, 0.26f) : new Color(0.53f, 0.46f, 0.28f));
        tagLabel.HorizontalAlignment = HorizontalAlignment.Center;
        layout.AddChild(tagLabel);

        Label bodyLabel = CreateLabel(description, 15, new Color(0.58f, 0.62f, 0.56f));
        bodyLabel.HorizontalAlignment = HorizontalAlignment.Center;
        bodyLabel.VerticalAlignment = VerticalAlignment.Center;
        bodyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        bodyLabel.MaxLinesVisible = 3;
        bodyLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        bodyLabel.CustomMinimumSize = new Vector2(0, 62);
        layout.AddChild(bodyLabel);
    }

    public void SetSelected(bool selected)
    {
        _panelStyle.BorderColor = selected
            ? (_available
                ? new Color(0.61f, 0.78f, 0.2f, 1)
                : new Color(0.66f, 0.5f, 0.2f, 0.95f))
            : new Color(0.2f, 0.29f, 0.14f, 0.76f);
        _panelStyle.SetBorderWidthAll(selected ? 2 : 1);
        _panelStyle.ShadowColor = selected
            ? new Color(0.38f, 0.64f, 0.12f, 0.2f)
            : Colors.Transparent;
        _panelStyle.ShadowSize = selected ? 14 : 0;
        _nameLabel.AddThemeColorOverride("font_color", selected
            ? new Color(0.9f, 0.93f, 0.84f)
            : new Color(0.62f, 0.65f, 0.6f));
        _statusLabel.Modulate = selected ? Colors.White : new Color(1, 1, 1, 0.62f);
    }

    private static Label CreateLabel(string text, int fontSize, Color color)
    {
        Label label = new()
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }
}
