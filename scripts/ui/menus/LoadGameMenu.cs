using Godot;
using SolarixFailure.Domain;
using SolarixFailure.UI;

namespace SolarixFailure;

public partial class LoadGameMenu : MenuScreenBase
{
    private OptionButton _typeFilter = null!;
    private OptionButton _runFilter = null!;
    private VBoxContainer _list = null!;
    private readonly List<Guid?> _runIds = [];
    private readonly List<Button> _saveButtons = [];
    private Button _backButton = null!;

    public override void _Ready()
    {
        VBoxContainer content = BuildScreen("LOAD_TITLE", "LOAD_SUBTITLE", 1180);
        HBoxContainer filters = new();
        filters.AddThemeConstantOverride("separation", 10);
        content.AddChild(filters);
        _typeFilter = new OptionButton { CustomMinimumSize = new Vector2(220, 44) };
        _typeFilter.AddItem(Tr("LOAD_FILTER_ALL"));
        _typeFilter.AddItem(Tr("LOAD_FILTER_MANUAL"));
        _typeFilter.AddItem(Tr("LOAD_FILTER_AUTOSAVE"));
        _typeFilter.ItemSelected += _ => RebuildList();
        filters.AddChild(_typeFilter);
        _runFilter = new OptionButton { CustomMinimumSize = new Vector2(280, 44) };
        _runFilter.ItemSelected += _ => RebuildList();
        filters.AddChild(_runFilter);

        ScrollContainer scroll = new()
        {
            CustomMinimumSize = new Vector2(0, 480),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        content.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_list);
        _backButton = AddMenuButton(content, "MENU_BACK",
            () => FireAndForget(SceneFlowService.Instance.GoToAsync(GameRoute.PlayMenu)));
        SaveGameService.Instance.SaveIndexChanged += Refresh;
        Refresh();
        ActivateMenuFocus();
    }

    public override void _ExitTree()
    {
        if (SaveGameService.Instance is not null)
            SaveGameService.Instance.SaveIndexChanged -= Refresh;
    }

    private void Refresh()
    {
        Guid? selected = _runFilter.Selected >= 0 && _runFilter.Selected < _runIds.Count
            ? _runIds[_runFilter.Selected]
            : null;
        _runFilter.Clear();
        _runIds.Clear();
        _runFilter.AddItem(Tr("LOAD_ALL_RUNS"));
        _runIds.Add(null);
        foreach (Guid runId in SaveGameService.Instance.ListSaves()
            .Select(save => save.RunId).Distinct())
        {
            _runFilter.AddItem($"{Tr("LOAD_RUN")} {runId.ToString("N")[..8].ToUpperInvariant()}");
            _runIds.Add(runId);
        }
        int index = _runIds.FindIndex(value => value == selected);
        _runFilter.Select(Math.Max(0, index));
        RebuildList();
    }

    private void RebuildList()
    {
        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }
        _saveButtons.Clear();
        SaveKind? kind = _typeFilter.Selected switch
        {
            1 => SaveKind.Manual,
            2 => SaveKind.Autosave,
            _ => null
        };
        Guid? run = _runFilter.Selected >= 0 && _runFilter.Selected < _runIds.Count
            ? _runIds[_runFilter.Selected] : null;
        IReadOnlyList<SaveGameSummary> saves =
            SaveGameService.Instance.ListSaves(new SaveQuery(run, kind));
        if (saves.Count == 0)
        {
            AddBody(_list, "LOAD_EMPTY", 18);
            ReplaceFocusButtons([_backButton]);
            return;
        }
        foreach (SaveGameSummary save in saves)
            AddSaveRow(save);
        ReplaceFocusButtons(_saveButtons.Append(_backButton));
    }

    private void AddSaveRow(SaveGameSummary save)
    {
        PanelContainer panel = new();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.012f, 0.02f, 0.014f, 0.98f),
            BorderColor = new Color(0.2f, 0.31f, 0.14f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 14, ContentMarginRight = 14,
            ContentMarginTop = 10, ContentMarginBottom = 10
        });
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 10);
        panel.AddChild(row);
        VBoxContainer info = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        info.AddChild(new Label { Text = save.DisplayName });
        info.AddChild(new Label
        {
            Text = $"{save.Kind}  •  {save.UpdatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  •  {save.Stage}",
            Modulate = new Color(0.63f, 0.66f, 0.61f)
        });
        row.AddChild(info);
        Button load = new() { Text = Tr("LOAD_ACTION"), CustomMinimumSize = new Vector2(100, 42) };
        load.Pressed += () => Load(save);
        row.AddChild(load);
        _saveButtons.Add(load);
        if (save.Kind == SaveKind.Manual)
        {
            Button rename = new() { Text = Tr("LOAD_RENAME"), CustomMinimumSize = new Vector2(110, 42) };
            rename.Pressed += () => Rename(save);
            row.AddChild(rename);
            _saveButtons.Add(rename);
        }
        Button delete = new() { Text = Tr("LOAD_DELETE"), CustomMinimumSize = new Vector2(100, 42) };
        delete.Pressed += () => ConfirmDelete(save);
        row.AddChild(delete);
        _saveButtons.Add(delete);
        _list.AddChild(panel);
    }

    private void Load(SaveGameSummary save)
    {
        if (SaveGameService.Instance.Load(save.SaveId)
            && SaveGameService.Instance.ActiveSession is { } active)
            FireAndForget(SceneFlowService.Instance.ResumeAsync(active));
    }

    private void Rename(SaveGameSummary save)
    {
        AcceptDialog dialog = new() { Title = Tr("LOAD_RENAME_TITLE") };
        LineEdit input = new() { Text = save.DisplayName, CustomMinimumSize = new Vector2(380, 44) };
        dialog.AddChild(input);
        dialog.Confirmed += () =>
        {
            if (!SaveGameService.Instance.RenameManualSave(save.SaveId, input.Text, out string error))
                GD.PushWarning(error);
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(480, 200));
    }

    private void ConfirmDelete(SaveGameSummary save)
    {
        ConfirmationDialog dialog = new()
        {
            Title = Tr("LOAD_DELETE_TITLE"),
            DialogText = Tr("LOAD_DELETE_BODY"),
            OkButtonText = Tr("LOAD_DELETE")
        };
        dialog.Confirmed += () => SaveGameService.Instance.DeleteSave(save.SaveId);
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(480, 200));
    }
}
