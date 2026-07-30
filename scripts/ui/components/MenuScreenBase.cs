using Godot;

namespace SolarixFailure.UI;

public abstract partial class MenuScreenBase : Control
{
    private readonly List<Button> _focusButtons = [];
    private MenuFocusController _focusController = null!;

    protected VBoxContainer BuildScreen(string titleKey, string? subtitleKey = null,
        float width = 760)
    {
        ColorRect background = new()
        {
            Color = new Color(0.0015f, 0.0045f, 0.0025f, 1),
            MouseFilter = MouseFilterEnum.Ignore
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);

        Panel frame = new() { MouseFilter = MouseFilterEnum.Ignore };
        frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.OffsetLeft = 24;
        frame.OffsetTop = 22;
        frame.OffsetRight = -24;
        frame.OffsetBottom = -22;
        StyleBoxFlat frameStyle = new()
        {
            BgColor = new Color(0.002f, 0.008f, 0.004f, 0.76f),
            BorderColor = new Color(0.22f, 0.35f, 0.14f, 0.94f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8
        };
        frame.AddThemeStyleboxOverride("panel", frameStyle);
        AddChild(frame);

        CenterContainer center = new();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        center.OffsetLeft = 48;
        center.OffsetRight = -48;
        center.OffsetTop = 40;
        center.OffsetBottom = -40;
        AddChild(center);

        VBoxContainer content = new()
        {
            Alignment = BoxContainer.AlignmentMode.Center
        };
        void ResizeContent()
        {
            float available = Math.Max(320, GetViewportRect().Size.X - 112);
            content.CustomMinimumSize = new Vector2(Math.Min(width, available), 0);
        }
        Resized += ResizeContent;
        ResizeContent();
        content.AddThemeConstantOverride("separation", 16);
        center.AddChild(content);

        if (!string.IsNullOrEmpty(titleKey))
        {
            Label title = new()
            {
                Text = Tr(titleKey),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            title.AddThemeFontSizeOverride("font_size", 34);
            title.AddThemeColorOverride("font_color", new Color("a6c94a"));
            content.AddChild(title);
        }
        if (!string.IsNullOrEmpty(subtitleKey))
        {
            Label subtitle = new()
            {
                Text = Tr(subtitleKey),
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            subtitle.Modulate = new Color(0.68f, 0.7f, 0.66f);
            content.AddChild(subtitle);
        }
        ColorRect rule = new()
        {
            Color = new Color(0.52f, 0.68f, 0.18f, 0.82f),
            CustomMinimumSize = new Vector2(0, 2),
            MouseFilter = MouseFilterEnum.Ignore
        };
        content.AddChild(rule);

        _focusController = new MenuFocusController();
        AddChild(_focusController);
        return content;
    }

    protected Button AddMenuButton(Container parent, string textKey,
        Action? action = null, bool enabled = true)
    {
        Button button = new()
        {
            Text = Tr(textKey),
            CustomMinimumSize = new Vector2(0, 58),
            Disabled = !enabled
        };
        if (action is not null)
            button.Pressed += action;
        parent.AddChild(button);
        _focusButtons.Add(button);
        return button;
    }

    protected Label AddBody(Container parent, string textKey, int fontSize = 20)
    {
        Label label = new()
        {
            Text = Tr(textKey),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        parent.AddChild(label);
        return label;
    }

    protected void ActivateMenuFocus() => _focusController.Configure(_focusButtons);
    protected void RegisterFocusButton(Button button) => _focusButtons.Add(button);
    protected void ReplaceFocusButtons(IEnumerable<Button> buttons)
    {
        _focusButtons.Clear();
        _focusButtons.AddRange(buttons);
        ActivateMenuFocus();
    }

    protected static void FireAndForget(Task task) => _ = task;
}
