using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.addons.mega_text;

namespace AiEvent;

public partial class AiEventCacheManagerOverlay : Control
{
    private VBoxContainer _entryRows = null!;
    private MegaRichTextLabel _runStatsLabel = null!;
    private Label _statusLabel = null!;

    private Control _editModal = null!;
    private Label _editTitleLabel = null!;
    private TextEdit _jsonEditor = null!;

    private Control _previewModal = null!;
    private PanelContainer _previewHost = null!;
    private Button _previewCloseButton = null!;

    private Control _busyOverlay = null!;
    private Label _busyLabel = null!;
    private bool _isBusy;

    private readonly List<AiEventPoolEntry> _entries = new();
    private string? _editingEntryId;
    private NEventRoom? _previewRoom;
    private AiGeneratedEventPayload? _previewRestorePayload;

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
        Visible = true;
        MoveToFront();
        _ = RefreshEntriesAsync();
    }

    private void BuildUi()
    {
        ColorRect backdrop = new()
        {
            Color = new Color(0f, 0f, 0f, 0.76f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        PanelContainer panel = new()
        {
            MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(1380f, 880f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.Position = new Vector2(-690f, -440f);
        AddChild(panel);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        panel.AddChild(margin);

        VBoxContainer root = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddThemeConstantOverride("separation", 14);
        margin.AddChild(root);

        root.AddChild(CreateHeaderRow());

        _runStatsLabel = CreateRichText(string.Empty, 18);
        _runStatsLabel.CustomMinimumSize = new Vector2(0f, 108f);
        _runStatsLabel.Visible = false;
        root.AddChild(_runStatsLabel);

        root.AddChild(CreateToolbarRow());
        root.AddChild(CreateEntryList());

        _statusLabel = new Label
        {
            Text = "就绪。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        root.AddChild(_statusLabel);

        _editModal = CreateEditModal();
        AddChild(_editModal);

        _previewModal = CreatePreviewModal();
        AddChild(_previewModal);

        _busyOverlay = CreateBusyOverlay();
        AddChild(_busyOverlay);
    }

    private Control CreateHeaderRow()
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 12);

        MegaRichTextLabel title = CreateRichText("[b]AI事件缓存管理[/b]", 24);
        title.CustomMinimumSize = new Vector2(0f, 52f);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(title);

        row.AddChild(CreateActionButton("关闭", CloseOverlay, 140f));
        return row;
    }

    private Control CreateToolbarRow()
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 10);

        row.AddChild(CreateActionButton("刷新", () => _ = RefreshEntriesAsync()));
        row.AddChild(CreateActionButton("新建", CreateNewEntry));

        Label tip = new()
        {
            Text = "列表支持查看、修改、删除。首次加载较大的缓存时会显示加载提示。",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        row.AddChild(tip);

        return row;
    }

    private Control CreateEntryList()
    {
        ScrollContainer scroll = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };

        _entryRows = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _entryRows.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_entryRows);

        return scroll;
    }

    private Control CreateEditModal()
    {
        Control modal = new()
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop,
        };
        modal.SetAnchorsPreset(LayoutPreset.FullRect);

        ColorRect backdrop = new()
        {
            Color = new Color(0f, 0f, 0f, 0.84f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        modal.AddChild(backdrop);

        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(1180f, 760f),
        };
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.Position = new Vector2(-590f, -380f);
        modal.AddChild(panel);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_top", 18);
        margin.AddThemeConstantOverride("margin_bottom", 18);
        panel.AddChild(margin);

        VBoxContainer root = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        _editTitleLabel = new Label
        {
            Text = "修改事件 JSON",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        root.AddChild(_editTitleLabel);

        _jsonEditor = new TextEdit
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        root.AddChild(_jsonEditor);

        HBoxContainer footer = new();
        footer.Alignment = BoxContainer.AlignmentMode.End;
        footer.AddThemeConstantOverride("separation", 8);
        footer.AddChild(CreateActionButton("取消", CloseEditModal, 120f));
        footer.AddChild(CreateActionButton("保存", SaveEditedEntry, 120f));
        root.AddChild(footer);

        return modal;
    }

    private Control CreatePreviewModal()
    {
        Control modal = new()
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop,
        };
        modal.SetAnchorsPreset(LayoutPreset.FullRect);

        ColorRect backdrop = new()
        {
            Color = new Color(0f, 0f, 0f, 0.88f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        modal.AddChild(backdrop);

        _previewHost = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _previewHost.SetAnchorsPreset(LayoutPreset.FullRect);
        _previewHost.OffsetLeft = 80f;
        _previewHost.OffsetTop = 48f;
        _previewHost.OffsetRight = -80f;
        _previewHost.OffsetBottom = -48f;
        modal.AddChild(_previewHost);

        _previewCloseButton = CreateActionButton("关闭预览", ClosePreviewModal, 150f);
        _previewCloseButton.SetAnchorsPreset(LayoutPreset.TopRight);
        _previewCloseButton.Position = new Vector2(-190f, 24f);
        _previewCloseButton.MouseFilter = MouseFilterEnum.Stop;
        modal.AddChild(_previewCloseButton);

        return modal;
    }

    private Control CreateBusyOverlay()
    {
        Control overlay = new()
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop,
        };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);

        ColorRect backdrop = new()
        {
            Color = new Color(0f, 0f, 0f, 0.55f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(backdrop);

        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(420f, 120f),
        };
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.Position = new Vector2(-210f, -60f);
        overlay.AddChild(panel);

        _busyLabel = new Label
        {
            Text = "加载中...",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _busyLabel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddChild(_busyLabel);

        return overlay;
    }

    private Button CreateActionButton(string text, Action action, float minWidth = 110f)
    {
        Button button = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(minWidth, 42f),
        };
        button.Pressed += action;
        return button;
    }

    private static MegaRichTextLabel CreateRichText(string text, int fontSize)
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
        label.AddThemeFontSizeOverride("normal_font_size", fontSize);
        label.AddThemeFontSizeOverride("bold_font_size", fontSize);
        label.AddThemeFontSizeOverride("italics_font_size", fontSize);
        label.AddThemeFontSizeOverride("bold_italics_font_size", fontSize);
        label.AddThemeFontSizeOverride("mono_font_size", fontSize);
        return label;
    }

    private async Task RefreshEntriesAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true, "正在加载 AI 事件缓存...");

        try
        {
            List<AiEventPoolEntry> loadedEntries = await Task.Run(() =>
            {
                AiEventRepository.Initialize();
                return AiEventRepository.GetAllPoolEntries().ToList();
            });

            _entries.Clear();
            _entries.AddRange(loadedEntries);

            RefreshRunStats();
            await RebuildEntryRowsAsync();

            if (_entries.Count == 0)
            {
                SetStatus("当前还没有缓存事件。");
                return;
            }

            SetStatus($"已加载 {_entries.Count} 个缓存事件。");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RefreshRunStats()
    {
        AiEventRunStats stats = AiEventRuntimeService.GetRunStats();
        if (!stats.HasActiveRun)
        {
            _runStatsLabel.Visible = false;
            _runStatsLabel.Text = string.Empty;
            return;
        }

        _runStatsLabel.Visible = true;
        _runStatsLabel.Text =
            $"[b]当前进行中的存档[/b]  Seed: {EscapeBb(stats.Seed)}\n" +
            $"已经历 llm 事件数 [b]{stats.ExperiencedCount}[/b]    " +
            $"已生成 llm 事件数 [b]{stats.GeneratedCount}[/b]    " +
            $"待生成 llm 事件数 [b]{stats.PendingCount}[/b]    " +
            $"已丢弃 [b]{stats.DiscardedCount}[/b]    " +
            $"后台生成状态 [b]{(stats.IsGenerating ? "生成中" : "未生成中")}[/b]";
    }

    private async Task RebuildEntryRowsAsync()
    {
        foreach (Node child in _entryRows.GetChildren())
        {
            child.QueueFree();
        }

        if (_entries.Count == 0)
        {
            Label emptyLabel = new()
            {
                Text = "还没有缓存事件，点“新建”即可手动创建一条。",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            _entryRows.AddChild(emptyLabel);
            return;
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            _entryRows.AddChild(CreateEntryRow(_entries[i]));
            if ((i + 1) % 20 == 0)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
    }

    private Control CreateEntryRow(AiEventPoolEntry entry)
    {
        PanelContainer panel = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 110f),
            TooltipText = BuildTooltip(entry),
        };

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(margin);

        HBoxContainer row = new();
        row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddThemeConstantOverride("separation", 14);
        margin.AddChild(row);

        VBoxContainer info = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        info.AddThemeConstantOverride("separation", 6);
        row.AddChild(info);

        MegaRichTextLabel title = CreateRichText("[b]" + EscapeBb(GetDisplayTitle(entry)) + "[/b]", 24);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.CustomMinimumSize = new Vector2(0f, 34f);
        info.AddChild(title);

        MegaRichTextLabel meta = CreateRichText(EscapeBb(BuildRowMeta(entry)), 18);
        meta.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        meta.CustomMinimumSize = new Vector2(0f, 48f);
        info.AddChild(meta);

        HBoxContainer actions = new();
        actions.Alignment = BoxContainer.AlignmentMode.End;
        actions.AddThemeConstantOverride("separation", 8);
        row.AddChild(actions);

        actions.AddChild(CreateActionButton("查看", () => _ = OpenPreviewAsync(entry), 96f));
        actions.AddChild(CreateActionButton("修改", () => OpenEditModal(entry), 96f));
        actions.AddChild(CreateActionButton("删除", () => _ = DeleteEntryAsync(entry.EntryId), 96f));

        return panel;
    }

    private void CreateNewEntry()
    {
        AiGeneratedEventPayload payload = AiEventFallbacks.Create(AiEventSlot.Shared);
        payload.EntryId = Guid.NewGuid().ToString("N");
        payload.Eng.Title = "New AI Event";
        payload.Zhs.Title = "新的 AI 事件";
        payload.Eng.InitialDescription = "Edit this event in the cache manager.";
        payload.Zhs.InitialDescription = "在缓存管理器里编辑这个事件。";

        AiEventPoolEntry entry = AiEventRepository.CreatePoolEntry(payload, "manual", "manual");
        OpenEditModal(entry);
        SetStatus("已创建新模板，编辑后点击保存即可写入缓存。");
    }

    private void OpenEditModal(AiEventPoolEntry entry)
    {
        _editingEntryId = entry.EntryId;
        _editTitleLabel.Text = $"修改事件 JSON: {GetDisplayTitle(entry)}";
        _jsonEditor.Text = AiEventRepository.SerializePoolEntry(entry);
        _editModal.Visible = true;
        _editModal.MoveToFront();
    }

    private void CloseEditModal()
    {
        _editingEntryId = null;
        _editModal.Visible = false;
    }

    private void SaveEditedEntry()
    {
        if (!TryReadEditorEntry(out AiEventPoolEntry? entry, out string error))
        {
            SetStatus(error);
            return;
        }

        entry = NormalizeEntry(entry!);
        AiEventRepository.AddPoolEntry(entry);
        CloseEditModal();
        _ = RefreshEntriesAsync();
        SetStatus($"已保存缓存事件 {GetDisplayTitle(entry)}");
    }

    private async Task DeleteEntryAsync(string entryId)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true, "正在删除缓存事件...");
        try
        {
            await Task.Run(() => AiEventRepository.DeletePoolEntry(entryId));
            _entries.RemoveAll(entry => string.Equals(entry.EntryId, entryId, StringComparison.OrdinalIgnoreCase));
            RefreshRunStats();
            await RebuildEntryRowsAsync();
            SetStatus("已删除所选缓存事件。");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task OpenPreviewAsync(AiEventPoolEntry entry)
    {
        try
        {
            ClosePreviewModal();
            AiEventRepository.Initialize();

            _previewRestorePayload = AiEventRepository.Get(entry.Payload.Slot);
            AiEventRepository.SetActive(entry.Payload);
            AiEventLocalization.ApplyCurrentLanguage();

            RunState previewRun = RunState.CreateForTest(seed: $"ai-event-preview-{entry.EntryId}");
            EventModel previewModel = AiEventRegistry.GetModelForSlot(entry.Payload.Slot).ToMutable();
            await previewModel.BeginEvent(previewRun.Players[0], false);

            NEventRoom? room = NEventRoom.Create(previewModel, previewRun, false);
            if (room == null)
            {
                throw new InvalidOperationException("原生事件房间创建失败。");
            }

            room.SetAnchorsPreset(LayoutPreset.FullRect);
            room.MouseFilter = MouseFilterEnum.Stop;
            _previewHost.AddChild(room);
            _previewRoom = room;

            _previewModal.Visible = true;
            _previewModal.MoveToFront();
            _previewCloseButton.MoveToFront();

            await ToSignal(GetTree().CreateTimer(0.35f), SceneTreeTimer.SignalName.Timeout);
            HookPreviewOptionButtons();
        }
        catch (Exception ex)
        {
            ClosePreviewModal();
            SetStatus($"事件预览失败: {ex.Message}");
            MainFile.Logger.Error($"[ai-event] failed to preview cached event: {ex}");
        }
    }

    private void ClosePreviewModal()
    {
        if (_previewRoom != null)
        {
            _previewRoom.QueueFree();
            _previewRoom = null;
        }

        foreach (Node child in _previewHost.GetChildren())
        {
            child.QueueFree();
        }

        if (_previewRestorePayload != null)
        {
            AiEventRepository.SetActive(_previewRestorePayload);
            AiEventLocalization.ApplyCurrentLanguage();
            _previewRestorePayload = null;
        }

        _previewModal.Visible = false;
    }

    private void HookPreviewOptionButtons()
    {
        if (_previewRoom?.Layout == null)
        {
            return;
        }

        foreach (NEventOptionButton button in _previewRoom.Layout.OptionButtons)
        {
            if (button.GetMeta("ai_event_preview_close_hooked", false).AsBool())
            {
                continue;
            }

            button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => ClosePreviewModal()));
            button.SetMeta("ai_event_preview_close_hooked", true);
        }
    }

    private bool TryReadEditorEntry(out AiEventPoolEntry? entry, out string error)
    {
        entry = null;
        error = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(_jsonEditor.Text))
            {
                error = "JSON 不能为空。";
                return false;
            }

            entry = AiEventRepository.DeserializePoolEntry(_jsonEditor.Text);
            if (entry.Payload == null)
            {
                error = "JSON 缺少 payload。";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_editingEntryId) && string.IsNullOrWhiteSpace(entry.EntryId))
            {
                entry.EntryId = _editingEntryId;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"JSON 解析失败: {ex.Message}";
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
        entry.Theme ??= string.Empty;
        entry.Payload.EventKey = AiEventRegistry.GetEventKey(entry.Payload.Slot);
        entry.Payload.Eng ??= AiEventFallbacks.Create(entry.Payload.Slot).Eng;
        entry.Payload.Zhs ??= AiEventFallbacks.Create(entry.Payload.Slot).Zhs;
        entry.Payload.Options ??= new List<AiEventOptionPayload>();
        return entry;
    }

    private static string GetDisplayTitle(AiEventPoolEntry entry)
    {
        bool useChinese = string.Equals(LocManager.Instance?.Language, "zhs", StringComparison.OrdinalIgnoreCase);
        return FirstNonEmpty(
            useChinese ? entry.Payload.Zhs?.Title : entry.Payload.Eng?.Title,
            useChinese ? entry.Payload.Eng?.Title : entry.Payload.Zhs?.Title,
            entry.Payload.EventKey,
            entry.EntryId);
    }

    private static string BuildRowMeta(AiEventPoolEntry entry)
    {
        string slotName = AiEventRegistry.GetActName(entry.Payload.Slot);
        string source = FirstNonEmpty(entry.Source, "unknown");
        string theme = FirstNonEmpty(entry.Theme, "未记录主题");
        string time = entry.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        string summary = FirstNonEmpty(
            entry.Payload.Zhs?.InitialDescription,
            entry.Payload.Eng?.InitialDescription,
            "无描述");

        return $"区域: {slotName}  |  来源: {source}  |  主题: {theme}  |  时间: {time}\n{summary}";
    }

    private static string BuildTooltip(AiEventPoolEntry entry)
    {
        return $"{GetDisplayTitle(entry)}\nSeed: {FirstNonEmpty(entry.Seed, "(empty)")}\nEntryId: {entry.EntryId}";
    }

    private void CloseOverlay()
    {
        CloseEditModal();
        ClosePreviewModal();
        Visible = false;
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }

    private void SetBusy(bool isBusy, string text = "加载中...")
    {
        _isBusy = isBusy;
        _busyLabel.Text = text;
        _busyOverlay.Visible = isBusy;
        if (isBusy)
        {
            _busyOverlay.MoveToFront();
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string EscapeBb(string value)
    {
        return value.Replace("[", "[lb]").Replace("]", "[rb]");
    }
}
