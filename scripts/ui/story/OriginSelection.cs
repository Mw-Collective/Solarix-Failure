using Godot;
using SolarixFailure.Domain;
using SolarixFailure.UI;

namespace SolarixFailure;

public partial class OriginSelection : MenuScreenBase
{
    private sealed record OriginOption(
        string ArchiveId,
        string NameKey,
        string TagKey,
        string DescriptionKey,
        string PortraitPath,
        bool Available,
        bool Animated = false);

    private readonly List<OriginCarouselCard> _cards = [];
    private readonly List<Label> _dots = [];
    private readonly List<OriginOption> _options = [];
    private Control _carousel = null!;
    private Button _confirmButton = null!;
    private Tween? _layoutTween;
    private int _selectedIndex;
    private bool _joyAxisLatched;

    public override void _Ready()
    {
        VBoxContainer content = BuildScreen("ORIGIN_TITLE", "ORIGIN_SUBTITLE", 1180);
        content.AddThemeConstantOverride("separation", 9);
        AddAmbientArchiveLayer();

        Label archiveLabel = new()
        {
            Text = Tr("ORIGIN_ARCHIVE"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        archiveLabel.AddThemeFontSizeOverride("font_size", 10);
        archiveLabel.AddThemeColorOverride("font_color", new Color(0.43f, 0.54f, 0.31f));
        content.AddChild(archiveLabel);
        content.MoveChild(archiveLabel, 0);

        _options.AddRange(
        [
            new OriginOption("BIO // 01", "ORIGIN_HUMAN", "ORIGIN_HUMAN_TAG",
                "ORIGIN_HUMAN_BODY",
                "res://assets/characters/human/idle_breathing_sprite_sheet.png",
                true,
                true),
            new OriginOption("BIO // 02", "ORIGIN_FOX", "ORIGIN_FOX_TAG",
                "ORIGIN_FOX_BODY", "res://assets/characters/origins/fox_survivor.svg", false),
            new OriginOption("BIO // 03", "ORIGIN_CAT", "ORIGIN_CAT_TAG",
                "ORIGIN_CAT_BODY", "res://assets/characters/origins/cat_survivor.svg", false)
        ]);

        _carousel = new Control
        {
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Pass
        };
        content.AddChild(_carousel);

        for (int index = 0; index < _options.Count; index++)
            CreateCard(index);

        Button previous = CreateArrowButton("◀");
        previous.Pressed += Previous;
        previous.SetAnchorsPreset(LayoutPreset.CenterLeft);
        previous.OffsetLeft = 8;
        previous.OffsetTop = -34;
        previous.OffsetRight = 62;
        previous.OffsetBottom = 34;
        previous.ZIndex = 20;
        _carousel.AddChild(previous);
        RegisterFocusButton(previous);

        Button next = CreateArrowButton("▶");
        next.Pressed += Next;
        next.SetAnchorsPreset(LayoutPreset.CenterRight);
        next.OffsetLeft = -62;
        next.OffsetTop = -34;
        next.OffsetRight = -8;
        next.OffsetBottom = 34;
        next.ZIndex = 20;
        _carousel.AddChild(next);
        RegisterFocusButton(next);

        HBoxContainer pagination = new()
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        pagination.AddThemeConstantOverride("separation", 8);
        content.AddChild(pagination);
        for (int index = 0; index < _options.Count; index++)
        {
            Label dot = new()
            {
                Text = "•",
                MouseFilter = MouseFilterEnum.Ignore
            };
            dot.AddThemeFontSizeOverride("font_size", 18);
            pagination.AddChild(dot);
            _dots.Add(dot);
        }

        Label hint = new()
        {
            Text = Tr("ORIGIN_NAV_HINT"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.42f, 0.47f, 0.4f));
        content.AddChild(hint);

        HBoxContainer actions = new()
        {
            Alignment = BoxContainer.AlignmentMode.Center
        };
        actions.AddThemeConstantOverride("separation", 12);
        content.AddChild(actions);
        AddMenuButton(actions, "MENU_BACK",
            () => FireAndForget(SceneFlowService.Instance.GoToAsync(GameRoute.PlayMenu)));
        _confirmButton = AddMenuButton(actions, "ORIGIN_CONFIRM", Confirm);

        Resized += UpdateResponsiveLayout;
        _carousel.Resized += () => LayoutCards(false);
        UpdateResponsiveLayout();
        CallDeferred(MethodName.ApplyInitialLayout);
        ActivateMenuFocus();
    }

    public override void _Input(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                Previous();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                Next();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false } key
                when key.Keycode is Key.Left or Key.A:
                Previous();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false } key
                when key.Keycode is Key.Right or Key.D:
                Next();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.DpadLeft }:
                Previous();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.DpadRight }:
                Next();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventJoypadMotion { Axis: JoyAxis.LeftX } motion:
                HandleJoyAxis(motion.AxisValue);
                break;
        }
    }

    private void CreateCard(int index)
    {
        OriginOption option = _options[index];
        OriginCarouselCard card = new();
        card.Configure(
            option.ArchiveId,
            Tr(option.NameKey),
            Tr(option.TagKey),
            Tr(option.DescriptionKey),
            GD.Load<Texture2D>(option.PortraitPath),
            option.Available,
            option.Animated);
        card.GuiInput += inputEvent =>
        {
            if (inputEvent is InputEventMouseButton
                {
                    Pressed: true,
                    ButtonIndex: MouseButton.Left
                })
            {
                Select(index);
                GetViewport().SetInputAsHandled();
            }
        };
        _carousel.AddChild(card);
        _cards.Add(card);
    }

    private void ApplyInitialLayout() => LayoutCards(false);

    private void UpdateResponsiveLayout()
    {
        float height = Math.Clamp(GetViewportRect().Size.Y * 0.56f, 400, 620);
        _carousel.CustomMinimumSize = new Vector2(0, height);
        LayoutCards(false);
    }

    private void LayoutCards(bool animate)
    {
        if (_carousel.Size.X <= 0 || _carousel.Size.Y <= 0 || _cards.Count == 0)
            return;

        _layoutTween?.Kill();
        if (animate)
        {
            _layoutTween = CreateTween().SetParallel();
            _layoutTween.SetTrans(Tween.TransitionType.Cubic);
            _layoutTween.SetEase(Tween.EaseType.Out);
        }

        float cardHeight = Math.Max(300, _carousel.Size.Y - 8);
        float cardWidth = Math.Clamp(cardHeight * 0.58f, 190, 290);
        float spacing = Math.Max(210, cardWidth * 1.02f);
        spacing = Math.Min(spacing, Math.Max(170, (_carousel.Size.X - cardWidth) * 0.42f));
        Vector2 center = _carousel.Size * 0.5f;

        for (int index = 0; index < _cards.Count; index++)
        {
            OriginCarouselCard card = _cards[index];
            int relative = (index - _selectedIndex + _cards.Count) % _cards.Count;
            if (relative > _cards.Count / 2)
                relative -= _cards.Count;

            card.Size = new Vector2(cardWidth, cardHeight);
            card.PivotOffset = card.Size * 0.5f;
            Vector2 targetPosition = new(
                center.X + (relative * spacing) - (cardWidth * 0.5f),
                center.Y - (cardHeight * 0.5f));
            Vector2 targetScale = Vector2.One * (relative == 0 ? 1.0f : 0.82f);
            Color targetColor = relative == 0
                ? Colors.White
                : new Color(0.7f, 0.74f, 0.68f, 0.72f);

            card.ZIndex = relative == 0 ? 10 : 2;
            card.SetSelected(relative == 0);
            if (_layoutTween is not null)
            {
                _layoutTween.TweenProperty(card, "position", targetPosition, 0.32);
                _layoutTween.TweenProperty(card, "scale", targetScale, 0.32);
                _layoutTween.TweenProperty(card, "modulate", targetColor, 0.26);
            }
            else
            {
                card.Position = targetPosition;
                card.Scale = targetScale;
                card.Modulate = targetColor;
            }
        }

        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        OriginOption selected = _options[_selectedIndex];
        _confirmButton.Disabled = !selected.Available;
        _confirmButton.Text = Tr(selected.Available
            ? "ORIGIN_CONFIRM"
            : "ORIGIN_LOCKED_ACTION");

        for (int index = 0; index < _dots.Count; index++)
        {
            _dots[index].AddThemeColorOverride("font_color", index == _selectedIndex
                ? new Color(0.62f, 0.78f, 0.2f)
                : new Color(0.2f, 0.27f, 0.17f));
        }
    }

    private Button CreateArrowButton(string symbol)
    {
        Button button = new()
        {
            Text = symbol,
            CustomMinimumSize = new Vector2(54, 68),
            FocusMode = FocusModeEnum.All
        };
        button.AddThemeFontSizeOverride("font_size", 20);
        return button;
    }

    private void Select(int index)
    {
        int wrapped = (index + _options.Count) % _options.Count;
        if (wrapped == _selectedIndex)
            return;

        _selectedIndex = wrapped;
        LayoutCards(true);
    }

    private void Previous() => Select(_selectedIndex - 1);
    private void Next() => Select(_selectedIndex + 1);

    private void HandleJoyAxis(float value)
    {
        if (Math.Abs(value) < 0.35f)
        {
            _joyAxisLatched = false;
            return;
        }
        if (_joyAxisLatched || Math.Abs(value) < 0.7f)
            return;

        _joyAxisLatched = true;
        if (value < 0)
            Previous();
        else
            Next();
    }

    private void Confirm()
    {
        if (!_options[_selectedIndex].Available)
            return;

        SaveGameService.Instance.WriteCheckpoint(
            GameFlowStage.GameplayCanvas, origin: CharacterOrigin.Human,
            label: Tr("SAVE_ORIGIN_CONFIRMED"));
        FireAndForget(SceneFlowService.Instance.GoToAsync(GameRoute.GameplayCanvas));
    }

    private void AddAmbientArchiveLayer()
    {
        Shader shader = new()
        {
            Code = """
                shader_type canvas_item;

                void fragment() {
                    vec2 p = UV - vec2(0.5, 0.54);
                    p.x *= 1.65;
                    float distance_from_center = length(p);
                    float halo = 1.0 - smoothstep(0.08, 0.72, distance_from_center);
                    float ring_a = 1.0 - smoothstep(0.002, 0.009, abs(distance_from_center - 0.29));
                    float ring_b = 1.0 - smoothstep(0.002, 0.008, abs(distance_from_center - 0.43));
                    float vertical = 1.0 - smoothstep(0.0, 0.002, abs(p.x));
                    vec3 color = vec3(0.14, 0.24, 0.07) * halo * 0.26;
                    color += vec3(0.3, 0.48, 0.1) * (ring_a * 0.055 + ring_b * 0.035 + vertical * 0.025);
                    COLOR = vec4(color, halo * 0.52 + ring_a * 0.06 + ring_b * 0.04);
                }
                """
        };
        ColorRect ambient = new()
        {
            Material = new ShaderMaterial { Shader = shader },
            MouseFilter = MouseFilterEnum.Ignore
        };
        ambient.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(ambient);
        MoveChild(ambient, 1);
    }
}
