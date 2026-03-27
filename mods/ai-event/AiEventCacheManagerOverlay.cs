using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.addons.mega_text;

namespace AiEvent;

public partial class AiEventCacheManagerOverlay : Control
{
    private ItemList _entryList = null!;
    private TextEdit _jsonEditor = null!;
    private MegaRichTextLabel _summaryLabel = null!;
    private Label _statusLabel = null!;

    private readonly List<AiEventPoolEntry> _entries = new();
    private string? _selectedEntryId;
    private bool _isRefreshingSelection;

    public static AiEventCacheManagerOverlay Create()
    {
        AiEventCacheManagerOverlay overlay = new();
        overlay.BuildUi();
        overlay.Visible = false;
        overlay.MouseFilter = MouseFilterEnum.Stop;
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        return overlay;
    }

    public void Open()
    {
        RefreshEntries();
        Visible = true;
        MoveToFront();
    }

    private void BuildUi()
    {
        ColorRect backdrop = new()
        {
            Color = new Color(0f, 0f, 0f, 0.72f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        PanelContainer panel = new()
        {
            MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(1480f, 860f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.Position = new Vector2(-740f, -430f);
        AddChild(panel);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 22);
        margin.AddThemeConstantOverride("margin_right", 22);
        margin.AddThemeConstantOverride("margin_top", 18);
        margin.AddThemeConstantOverride("margin_bottom", 18);
        panel.AddChild(margin);

        VBoxContainer root = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);

        root.AddChild(CreateHeaderRow());
        root.AddChild(CreateToolbarRow());
        root.AddChild(CreateBodyRow());

        _statusLabel = new Label
        {
            Text = "Ready",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        root.AddChild(_statusLabel);
    }

    private Control CreateHeaderRow()
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 12);

        MegaRichTextLabel title = CreateRichText("[b]AI Event Cache Manager[/b]");
        title.CustomMinimumSize = new Vector2(0f, 48f);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(title);

        Button closeButton = new()
        {
            Text = "Close",
            CustomMinimumSize = new Vector2(120f, 48f),
        };
        closeButton.Pressed += CloseOverlay;
        row.AddChild(closeButton);

        return row;
    }

    private Control CreateToolbarRow()
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 8);

        row.AddChild(CreateActionButton("Refresh", RefreshEntries));
        row.AddChild(CreateActionButton("New", CreateNewEntry));
        row.AddChild(CreateActionButton("Duplicate", DuplicateSelectedEntry));
        row.AddChild(CreateActionButton("Save", SaveCurrentEntry));
        row.AddChild(CreateActionButton("Delete", DeleteSelectedEntry));

        Label tip = new()
        {
            Text = "Select a cached event on the left, edit JSON on the right, then save it back to the pool.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.AddChild(tip);

        return row;
    }

    private Control CreateBodyRow()
    {
        HSplitContainer split = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        split.SplitOffset = 420;

        split.AddChild(CreateLeftPane());
        split.AddChild(CreateRightPane());
        return split;
    }

    private Control CreateLeftPane()
    {
        VBoxContainer pane = new();
        pane.AddThemeConstantOverride("separation", 8);
        pane.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pane.SizeFlagsVertical = SizeFlags.ExpandFill;

        pane.AddChild(CreateRichText("[b]Cached Events[/b]"));

        _entryList = new ItemList
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
            AllowReselect = true,
            AllowRmbSelect = true,
        };
        _entryList.ItemSelected += OnEntrySelected;
        pane.AddChild(_entryList);

        return pane;
    }

    private Control CreateRightPane()
    {
        VBoxContainer pane = new();
        pane.AddThemeConstantOverride("separation", 8);
        pane.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pane.SizeFlagsVertical = SizeFlags.ExpandFill;

        pane.AddChild(CreateRichText("[b]Details And Editor[/b]"));

        _summaryLabel = CreateRichText(string.Empty);
        _summaryLabel.CustomMinimumSize = new Vector2(0f, 140f);
        pane.AddChild(_summaryLabel);

        _jsonEditor = new TextEdit
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            PlaceholderText = "The selected event JSON will appear here. You can edit it directly.",
        };
        pane.AddChild(_jsonEditor);

        return pane;
    }

    private Button CreateActionButton(string text, Action action)
    {
        Button button = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(110f, 42f),
        };
        button.Pressed += action;
        return button;
    }

    private static MegaRichTextLabel CreateRichText(string text)
    {
        Font normal = PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_regular_shared.tres");
        Font bold = PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_bold_shared.tres");

        MegaRichTextLabel label = new()
        {
            BbcodeEnabled = true,
            ScrollActive = false,
            FitContent = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = text,
        };
        label.AddThemeFontOverride("normal_font", normal);
        label.AddThemeFontOverride("bold_font", bold);
        label.AddThemeFontSizeOverride("normal_font_size", 24);
        label.AddThemeFontSizeOverride("bold_font_size", 24);
        label.AddThemeFontSizeOverride("italics_font_size", 24);
        label.AddThemeFontSizeOverride("bold_italics_font_size", 24);
        label.AddThemeFontSizeOverride("mono_font_size", 24);
        return label;
    }

    private void RefreshEntries()
    {
        AiEventRepository.Initialize();

        _entries.Clear();
        _entries.AddRange(AiEventRepository.GetAllPoolEntries());

        _isRefreshingSelection = true;
        _entryList.Clear();
        foreach (AiEventPoolEntry entry in _entries)
        {
            string title = GetDisplayTitle(entry);
            string text = $"[{entry.Payload.Slot}] {title}";
            _entryList.AddItem(text);
            _entryList.SetItemTooltip(_entryList.ItemCount - 1, BuildTooltip(entry));
        }
        _isRefreshingSelection = false;

        if (_entries.Count == 0)
        {
            _selectedEntryId = null;
            _summaryLabel.Text = "No cached events yet.";
            _jsonEditor.Text = string.Empty;
            SetStatus("The cache is currently empty.");
            return;
        }

        int index = Math.Max(0, _entries.FindIndex(entry => string.Equals(entry.EntryId, _selectedEntryId, StringComparison.OrdinalIgnoreCase)));
        if (index >= _entries.Count)
        {
            index = 0;
        }

        _entryList.Select(index);
        ShowEntry(index);
        SetStatus($"Loaded {_entries.Count} cached events.");
    }

    private void OnEntrySelected(long index)
    {
        if (_isRefreshingSelection)
        {
            return;
        }

        ShowEntry((int)index);
    }

    private void ShowEntry(int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return;
        }

        AiEventPoolEntry entry = _entries[index];
        _selectedEntryId = entry.EntryId;
        _summaryLabel.Text = BuildSummary(entry);
        _jsonEditor.Text = AiEventRepository.SerializePoolEntry(entry);
    }

    private void CreateNewEntry()
    {
        AiGeneratedEventPayload payload = AiEventFallbacks.Create(AiEventSlot.Shared);
        payload.EntryId = Guid.NewGuid().ToString("N");
        payload.Eng.Title = "New AI Event";
        payload.Zhs.Title = "New AI Event";
        payload.Eng.InitialDescription = "Edit this event in the cache manager.";
        payload.Zhs.InitialDescription = "Edit this event in the cache manager.";

        AiEventPoolEntry entry = AiEventRepository.CreatePoolEntry(payload, "manual", "manual");
        _selectedEntryId = entry.EntryId;
        _summaryLabel.Text = BuildSummary(entry);
        _jsonEditor.Text = AiEventRepository.SerializePoolEntry(entry);
        SetStatus("Created a new template. Edit it and press Save to add it to the cache.");
    }

    private void DuplicateSelectedEntry()
    {
        if (!TryReadEditorEntry(out AiEventPoolEntry? entry, out _))
        {
            return;
        }

        entry!.EntryId = Guid.NewGuid().ToString("N");
        entry.Payload.EntryId = entry.EntryId;
        entry.GeneratedAtUtc = DateTime.UtcNow;
        entry.Source = "manual_copy";

        _selectedEntryId = entry.EntryId;
        _summaryLabel.Text = BuildSummary(entry);
        _jsonEditor.Text = AiEventRepository.SerializePoolEntry(entry);
        SetStatus("Duplicated the current event. Save it to store it as a new cached entry.");
    }

    private void SaveCurrentEntry()
    {
        if (!TryReadEditorEntry(out AiEventPoolEntry? entry, out string error))
        {
            SetStatus(error);
            return;
        }

        entry = NormalizeEntry(entry!);
        AiEventRepository.AddPoolEntry(entry);
        _selectedEntryId = entry.EntryId;
        RefreshEntries();
        SetStatus($"Saved cached event: {GetDisplayTitle(entry)}");
    }

    private void DeleteSelectedEntry()
    {
        string? entryId = _selectedEntryId;
        if (string.IsNullOrWhiteSpace(entryId))
        {
            SetStatus("Select a cached event on the left first.");
            return;
        }

        AiEventRepository.DeletePoolEntry(entryId);
        _selectedEntryId = null;
        RefreshEntries();
        SetStatus("Deleted the selected cached event.");
    }

    private bool TryReadEditorEntry(out AiEventPoolEntry? entry, out string error)
    {
        entry = null;
        error = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(_jsonEditor.Text))
            {
                error = "JSON cannot be empty.";
                return false;
            }

            entry = AiEventRepository.DeserializePoolEntry(_jsonEditor.Text);
            if (entry.Payload == null)
            {
                error = "JSON is missing payload.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"JSON parse failed: {ex.Message}";
            return false;
        }
    }

    private static AiEventPoolEntry NormalizeEntry(AiEventPoolEntry entry)
    {
        entry.Payload ??= AiEventFallbacks.Create(AiEventSlot.Shared);
        if (string.IsNullOrWhiteSpace(entry.EntryId))
        {
            entry.EntryId = Guid.NewGuid().ToString("N");
        }

        entry.Payload.EntryId = entry.EntryId;
        entry.GeneratedAtUtc = entry.GeneratedAtUtc == default ? DateTime.UtcNow : entry.GeneratedAtUtc;
        entry.Source = string.IsNullOrWhiteSpace(entry.Source) ? "manual" : entry.Source;
        entry.Seed ??= string.Empty;
        entry.Payload.EventKey = AiEventRegistry.GetEventKey(entry.Payload.Slot);
        entry.Payload.Eng ??= AiEventFallbacks.Create(entry.Payload.Slot).Eng;
        entry.Payload.Zhs ??= AiEventFallbacks.Create(entry.Payload.Slot).Zhs;
        entry.Payload.Options ??= new List<AiEventOptionPayload>();
        return entry;
    }

    private static string GetDisplayTitle(AiEventPoolEntry entry)
    {
        return FirstNonEmpty(
            entry.Payload.Zhs?.Title,
            entry.Payload.Eng?.Title,
            entry.Payload.EventKey,
            entry.EntryId);
    }

    private static string BuildTooltip(AiEventPoolEntry entry)
    {
        return $"{GetDisplayTitle(entry)}\nSource: {entry.Source}\nSlot: {entry.Payload.Slot}\nTime: {entry.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}\nSeed: {entry.Seed}";
    }

    private static string BuildSummary(AiEventPoolEntry entry)
    {
        string zhsTitle = FirstNonEmpty(entry.Payload.Zhs?.Title, "No ZHS title");
        string engTitle = FirstNonEmpty(entry.Payload.Eng?.Title, "No English title");
        string zhsBody = FirstNonEmpty(entry.Payload.Zhs?.InitialDescription, "No ZHS description");
        string engBody = FirstNonEmpty(entry.Payload.Eng?.InitialDescription, "No English description");

        return "[b]ZHS Title[/b] " + zhsTitle +
               "\n[b]ENG Title[/b] " + engTitle +
               "\n[b]Slot[/b] " + entry.Payload.Slot +
               "\n[b]Source[/b] " + entry.Source +
               "\n[b]Generated At[/b] " + entry.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") +
               "\n[b]Seed[/b] " + FirstNonEmpty(entry.Seed, "(empty)") +
               "\n[b]ZHS Summary[/b] " + zhsBody +
               "\n[b]ENG Summary[/b] " + engBody;
    }

    private void CloseOverlay()
    {
        Visible = false;
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
