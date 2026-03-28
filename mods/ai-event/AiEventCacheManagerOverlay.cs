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
    private Label _pageLabel = null!;
    private Button _prevPageButton = null!;
    private Button _nextPageButton = null!;

    private Control _editModal = null!;
    private Label _editTitleLabel = null!;
    private TextEdit _jsonEditor = null!;

    private Control _previewModal = null!;
    private PanelContainer _previewHost = null!;
    private Button _previewCloseButton = null!;
    private Control _confirmClearModal = null!;

    private Control _busyOverlay = null!;
    private Label _busyLabel = null!;
    private bool _isBusy;

    private const int PageSize = 40;
    private readonly List<AiEventPoolEntrySummary> _entries = new();
    private readonly List<AiEventPoolEntrySummary> _currentEntries = new();
    private int _totalEntryCount;
    private int _currentPage;
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

        _runStatsLabel = CreateRichText(string.Empty, 15);
        _runStatsLabel.CustomMinimumSize = new Vector2(0f, 74f);
        _runStatsLabel.Visible = false;
        root.AddChild(_runStatsLabel);

        root.AddChild(CreateToolbarRow());
        root.AddChild(CreateEntryList());
        root.AddChild(CreatePaginationRow());

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

        _confirmClearModal = CreateConfirmClearModal();
        AddChild(_confirmClearModal);

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

        row.AddChild(CreateActionButton("清空缓存", OpenClearConfirmModal, 140f));

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

    private Control CreatePaginationRow()
    {
        HBoxContainer row = new();
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddThemeConstantOverride("separation", 10);

        _prevPageButton = CreateActionButton("上一页", PrevPage, 120f);
        row.AddChild(_prevPageButton);

        _pageLabel = new Label
        {
            Text = "第 0 / 0 页",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(220f, 42f),
        };
        row.AddChild(_pageLabel);

        _nextPageButton = CreateActionButton("下一页", NextPage, 120f);
        row.AddChild(_nextPageButton);

        return row;
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

    private Control CreateConfirmClearModal()
    {
        Control modal = new()
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop,
        };
        modal.SetAnchorsPreset(LayoutPreset.FullRect);

        ColorRect backdrop = new()
        {
            Color = new Color(0f, 0f, 0f, 0.82f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        modal.AddChild(backdrop);

        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(560f, 220f),
        };
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.Position = new Vector2(-280f, -110f);
        modal.AddChild(panel);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
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

        root.AddChild(CreateRichText("[b]确定要清空全部 AI 事件缓存吗？[/b]", 24));

        Label body = new()
        {
            Text = "这会删除当前缓存数据库里的所有 AI 事件。此操作不可撤销。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        root.AddChild(body);

        HBoxContainer footer = new();
        footer.Alignment = BoxContainer.AlignmentMode.End;
        footer.AddThemeConstantOverride("separation", 8);
        footer.AddChild(CreateActionButton("取消", CloseClearConfirmModal, 120f));
        footer.AddChild(CreateActionButton("确定清空", () => _ = ConfirmClearPoolAsync(), 140f));
        root.AddChild(footer);

        return modal;
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
            (int totalCount, List<AiEventPoolEntrySummary> pageEntries) = await Task.Run(() =>
            {
                AiEventRepository.Initialize();
                int totalCount = AiEventRepository.GetPoolEntrySummaryCount();
                List<AiEventPoolEntrySummary> pageEntries = AiEventRepository.GetPoolEntrySummariesPage(0, PageSize).ToList();
                return (totalCount, pageEntries);
            });

            _currentEntries.Clear();
            _currentEntries.AddRange(pageEntries);
            _entries.Clear();
            _entries.AddRange(pageEntries);
            _totalEntryCount = totalCount;
            _currentPage = 0;

            RefreshRunStats();
            await LoadPageAsync(_currentPage);

            if (_totalEntryCount == 0)
            {
                SetStatus("当前还没有缓存事件。");
                return;
            }

            SetStatus($"已加载 {_totalEntryCount} 个缓存事件，当前显示第 {GetDisplayPageNumber()} 页。");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RefreshRunStats()
    {
        AiEventRunStatsSummary summary = AiEventRuntimeService.GetRunStatsSummary();
        AiEventRunStats stats = summary.CurrentContextIsMultiplayer ? summary.Multiplayer : summary.Singleplayer;
        if (!summary.Singleplayer.HasActiveRun && !summary.Multiplayer.HasActiveRun)
        {
            _runStatsLabel.Visible = false;
            _runStatsLabel.Text = string.Empty;
            return;
        }

        _runStatsLabel.Visible = true;
        _runStatsLabel.Text = BuildRunStatsText(summary);
        return;
        _runStatsLabel.Text =
            $"[b]当前进行中的存档[/b]  Seed: {EscapeBb(stats.Seed)}\n" +
            $"已经历 llm 事件数 [b]{stats.ExperiencedCount}[/b]    " +
            $"已生成 llm 事件数 [b]{stats.GeneratedCount}[/b]    " +
            $"待生成 llm 事件数 [b]{stats.PendingCount}[/b]    " +
            $"已丢弃 [b]{stats.DiscardedCount}[/b]    " +
            $"后台生成状态 [b]{(stats.IsGenerating ? "生成中" : "未生成中")}[/b]";
    }

    private static string BuildRunStatsText(AiEventRunStatsSummary summary)
    {
        return
            $"[b]当前进行中的存档[/b]  {(summary.CurrentContextIsMultiplayer ? "(当前在多人上下文)" : "(当前在单人/主菜单上下文)")}\n" +
            $"{FormatRunStatsLine("单人", summary.Singleplayer)}\n" +
            $"{FormatRunStatsLine("多人", summary.Multiplayer)}";
    }

    private static string FormatRunStatsLine(string label, AiEventRunStats stats)
    {
        if (!stats.HasActiveRun)
        {
            return $"[b]{label}[/b] 无进行中存档";
        }

        return $"[b]{label}[/b] 种子:{EscapeBb(stats.Seed)}  经历:[b]{stats.ExperiencedCount}[/b]  已生成:[b]{stats.GeneratedCount}[/b]  待生成:[b]{stats.PendingCount}[/b]  丢弃:[b]{stats.DiscardedCount}[/b]  状态:[b]{(stats.IsGenerating ? "生成中" : "未生成中")}[/b]";
    }

    private async Task RebuildEntryRowsAsync()
    {
        foreach (Node child in _entryRows.GetChildren())
        {
            child.QueueFree();
        }

        RefreshPaginationUi();

        if (_currentEntries.Count == 0)
        {
            Label emptyLabel = new()
            {
                Text = "还没有缓存事件，点“新建”即可手动创建一条。",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            _entryRows.AddChild(emptyLabel);
            return;
        }

        for (int i = 0; i < _currentEntries.Count; i++)
        {
            _entryRows.AddChild(CreateEntryRow(_currentEntries[i]));
            if ((i + 1) % 20 == 0)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
    }

    private Control CreateEntryRow(AiEventPoolEntrySummary entry)
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

    private void OpenEditModal(AiEventPoolEntrySummary entry)
    {
        AiEventPoolEntry? fullEntry = AiEventRepository.GetPoolEntryById(entry.EntryId);
        if (fullEntry == null)
        {
            SetStatus("未找到要编辑的事件。");
            return;
        }

        OpenEditModal(fullEntry);
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
            _currentEntries.RemoveAll(entry => string.Equals(entry.EntryId, entryId, StringComparison.OrdinalIgnoreCase));
            _totalEntryCount = Math.Max(0, _totalEntryCount - 1);
            ClampCurrentPage();
            RefreshRunStats();
            await RebuildEntryRowsAsync();
            SetStatus("已删除所选缓存事件。");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenClearConfirmModal()
    {
        if (_isBusy)
        {
            return;
        }

        _confirmClearModal.Visible = true;
        _confirmClearModal.MoveToFront();
    }

    private void CloseClearConfirmModal()
    {
        _confirmClearModal.Visible = false;
    }

    private async Task ConfirmClearPoolAsync()
    {
        if (_isBusy)
        {
            return;
        }

        CloseClearConfirmModal();
        SetBusy(true, "正在清空 AI 事件缓存...");

        try
        {
            await Task.Run(() =>
            {
                AiEventRepository.Initialize();
                AiEventRepository.ClearPoolEntries();
            });

            _entries.Clear();
            _currentEntries.Clear();
            _totalEntryCount = 0;
            _currentPage = 0;
            RefreshRunStats();
            await RebuildEntryRowsAsync();
            SetStatus("已清空 AI 事件缓存数据库。");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task OpenPreviewAsync(AiEventPoolEntrySummary entry)
    {
        AiEventPoolEntry? fullEntry = await Task.Run(() =>
        {
            AiEventRepository.Initialize();
            return AiEventRepository.GetPoolEntryById(entry.EntryId);
        });

        if (fullEntry == null)
        {
            SetStatus("未找到要预览的事件。");
            return;
        }

        await OpenPreviewAsync(fullEntry);
    }

    private async Task OpenPreviewAsync(AiEventPoolEntry entry)
    {
        try
        {
            ClosePreviewModal();
            PreloadPreviewAssets(entry.Payload.Slot);
            AiEventRepository.Initialize();

            _previewRestorePayload = AiEventRepository.Get(entry.Payload.Slot);
            AiEventRepository.SetActive(ClonePreviewPayload(entry));
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

    private static AiGeneratedEventPayload ClonePreviewPayload(AiEventPoolEntry entry)
    {
        AiGeneratedEventPayload payload = AiEventRepository.DeserializePoolEntry(AiEventRepository.SerializePoolEntry(entry)).Payload;
        string prefix = entry.Source?.Trim().ToLowerInvariant() switch
        {
            "llm_dynamic" => "[lb]llm dynamic[rb]",
            "llm_cache" => "[lb]llm cache[rb]",
            _ => string.Empty,
        };

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            payload.Eng.Title = PrefixTitle(payload.Eng.Title, prefix);
            payload.Zhs.Title = PrefixTitle(payload.Zhs.Title, prefix);
        }

        return payload;
    }

    private static string PrefixTitle(string title, string prefix)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return prefix;
        }

        return title.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)
            ? title
            : $"{prefix} {title}";
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

            button.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ => ClosePreviewModal()));
            button.SetMeta("ai_event_preview_close_hooked", true);
        }
    }

    private static void PreloadPreviewAssets(AiEventSlot slot)
    {
        try
        {
            ResourceLoader.Load<PackedScene>("res://scenes/events/default_event_layout.tscn");
            ResourceLoader.Load<Texture2D>($"res://images/events/{AiEventRegistry.GetImageFileName(slot)}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[ai-event] failed to preload preview assets for {slot}: {ex.Message}");
        }
    }

    private Control BuildPreviewContent(AiEventPoolEntry entry)
    {
        AiGeneratedEventPayload payload = ClonePreviewPayload(entry);
        bool useChinese = string.Equals(LocManager.Instance?.Language, "zhs", StringComparison.OrdinalIgnoreCase);
        AiLocalizedEventText text = useChinese ? payload.Zhs : payload.Eng;
        AiLocalizedEventText fallbackText = useChinese ? payload.Eng : payload.Zhs;

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 48);
        margin.AddThemeConstantOverride("margin_right", 48);
        margin.AddThemeConstantOverride("margin_top", 36);
        margin.AddThemeConstantOverride("margin_bottom", 36);

        ScrollContainer scroll = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        margin.AddChild(scroll);

        VBoxContainer root = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(0f, 720f),
        };
        root.AddThemeConstantOverride("separation", 18);
        scroll.AddChild(root);

        MegaRichTextLabel title = CreateRichText($"[center][b]{EscapeBb(FirstNonEmpty(text.Title, fallbackText.Title, GetDisplayTitle(entry)))}[/b][/center]", 28);
        title.CustomMinimumSize = new Vector2(0f, 56f);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        root.AddChild(title);

        MegaRichTextLabel meta = CreateRichText($"[center]{EscapeBb(BuildRowMeta(entry))}[/center]", 16);
        meta.CustomMinimumSize = new Vector2(0f, 52f);
        meta.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        root.AddChild(meta);

        PanelContainer descriptionPanel = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 220f),
        };
        root.AddChild(descriptionPanel);

        MarginContainer descriptionMargin = new();
        descriptionMargin.AddThemeConstantOverride("margin_left", 28);
        descriptionMargin.AddThemeConstantOverride("margin_right", 28);
        descriptionMargin.AddThemeConstantOverride("margin_top", 24);
        descriptionMargin.AddThemeConstantOverride("margin_bottom", 24);
        descriptionPanel.AddChild(descriptionMargin);

        MegaRichTextLabel description = CreateRichText(EscapeBb(FirstNonEmpty(text.InitialDescription, fallbackText.InitialDescription)), 24);
        description.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        description.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        descriptionMargin.AddChild(description);

        VBoxContainer options = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        options.AddThemeConstantOverride("separation", 12);
        root.AddChild(options);

        for (int i = 0; i < payload.Options.Count; i++)
        {
            AiEventOptionPayload optionPayload = payload.Options[i];
            AiLocalizedOptionText optionText = GetOptionText(optionPayload.Key, text, fallbackText);

            Button optionButton = new()
            {
                Text = BuildPreviewOptionLabel(i, optionText),
                Alignment = HorizontalAlignment.Left,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(0f, 84f),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            optionButton.Pressed += ClosePreviewModal;
            optionButton.TooltipText = BuildPreviewOptionTooltip(optionText, optionPayload);
            options.AddChild(optionButton);
        }

        return margin;
    }

    private static AiLocalizedOptionText GetOptionText(string key, AiLocalizedEventText preferred, AiLocalizedEventText fallback)
    {
        return preferred.Options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? fallback.Options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? new AiLocalizedOptionText { Key = key };
    }

    private static string BuildPreviewOptionLabel(int index, AiLocalizedOptionText optionText)
    {
        string body = FirstNonEmpty(optionText.Title, optionText.Description, optionText.Key);
        return $"[{index + 1}] {body}";
    }

    private static string BuildPreviewOptionTooltip(AiLocalizedOptionText optionText, AiEventOptionPayload optionPayload)
    {
        List<string> parts = new();
        string description = FirstNonEmpty(optionText.Description, optionText.ResultDescription);
        if (!string.IsNullOrWhiteSpace(description))
        {
            parts.Add(description);
        }

        string effects = string.Join(" | ", optionPayload.Effects.Select(FormatEffectSummary).Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(effects))
        {
            parts.Add(effects);
        }

        return string.Join("\n", parts);
    }

    private static string FormatEffectSummary(AiEventEffectPayload effect)
    {
        return effect.Type switch
        {
            "gain_gold" => $"获得 {effect.Amount} 金币",
            "lose_gold" => $"失去 {effect.Amount} 金币",
            "heal" => $"回复 {effect.Amount} 生命",
            "damage_self" => $"失去 {effect.Amount} 生命",
            "gain_max_hp" => $"获得 {effect.Amount} 最大生命",
            "lose_max_hp" => $"失去 {effect.Amount} 最大生命",
            "upgrade_cards" => $"升级 {effect.Count} 张牌",
            "upgrade_random" => $"随机升级 {effect.Count} 张牌",
            "remove_cards" => $"移除 {effect.Count} 张牌",
            "add_curse" => $"加入 {effect.Count} 张诅咒牌: {FirstNonEmpty(effect.CardId, "unknown")}",
            "obtain_random_relic" => $"获得 {effect.Count} 个随机遗物",
            _ => effect.Type,
        };
    }

    private async Task LoadPageAsync(int pageIndex)
    {
        ClampCurrentPage();
        int targetPage = Math.Max(0, pageIndex);
        int lastPageIndex = Math.Max(0, GetTotalPages() - 1);
        targetPage = Math.Min(targetPage, lastPageIndex);

        List<AiEventPoolEntrySummary> pageEntries = await Task.Run(() =>
        {
            AiEventRepository.Initialize();
            return AiEventRepository.GetPoolEntrySummariesPage(targetPage, PageSize).ToList();
        });

        _currentPage = targetPage;
        _currentEntries.Clear();
        _currentEntries.AddRange(pageEntries);
        _entries.Clear();
        _entries.AddRange(pageEntries);
        await RebuildEntryRowsAsync();
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

    private static string GetDisplayTitle(AiEventPoolEntrySummary entry)
    {
        bool useChinese = string.Equals(LocManager.Instance?.Language, "zhs", StringComparison.OrdinalIgnoreCase);
        return FirstNonEmpty(
            useChinese ? entry.ZhsTitle : entry.EngTitle,
            useChinese ? entry.EngTitle : entry.ZhsTitle,
            entry.EventKey,
            entry.EntryId);
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

    private static string BuildRowMeta(AiEventPoolEntrySummary entry)
    {
        string slotName = AiEventRegistry.GetActName(entry.Slot);
        string source = FirstNonEmpty(entry.Source, "unknown");
        string theme = FirstNonEmpty(entry.Theme, "未记录主题");
        string time = entry.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        string summary = FirstNonEmpty(
            entry.ZhsInitialDescription,
            entry.EngInitialDescription,
            "无描述");

        return $"区域: {slotName}  |  来源: {source}  |  主题: {theme}  |  时间: {time}\n{summary}";
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

    private static string BuildTooltip(AiEventPoolEntrySummary entry)
    {
        return $"{GetDisplayTitle(entry)}\nSeed: {FirstNonEmpty(entry.Seed, "(empty)")}\nEntryId: {entry.EntryId}";
    }

    private static string BuildTooltip(AiEventPoolEntry entry)
    {
        return $"{GetDisplayTitle(entry)}\nSeed: {FirstNonEmpty(entry.Seed, "(empty)")}\nEntryId: {entry.EntryId}";
    }

    private void CloseOverlay()
    {
        CloseEditModal();
        ClosePreviewModal();
        CloseClearConfirmModal();
        Visible = false;
    }

    private void PrevPage()
    {
        if (_currentPage <= 0 || _isBusy)
        {
            return;
        }

        _ = LoadPageAsync(_currentPage - 1);
        SetStatus($"已切换到第 {GetDisplayPageNumber()} 页。");
    }

    private void NextPage()
    {
        if (_isBusy)
        {
            return;
        }

        int lastPageIndex = Math.Max(0, GetTotalPages() - 1);
        if (_currentPage >= lastPageIndex)
        {
            return;
        }

        _ = LoadPageAsync(_currentPage + 1);
        SetStatus($"已切换到第 {GetDisplayPageNumber()} 页。");
    }

    private void ClampCurrentPage()
    {
        int totalPages = GetTotalPages();
        _currentPage = totalPages <= 0 ? 0 : Math.Clamp(_currentPage, 0, totalPages - 1);
    }

    private void RefreshPaginationUi()
    {
        int totalPages = GetTotalPages();
        int currentDisplayPage = totalPages == 0 ? 0 : _currentPage + 1;

        _pageLabel.Text = $"第 {currentDisplayPage} / {totalPages} 页  每页 {PageSize} 条";
        _prevPageButton.Disabled = _isBusy || _currentPage <= 0 || totalPages == 0;
        _nextPageButton.Disabled = _isBusy || totalPages == 0 || _currentPage >= totalPages - 1;
    }

    private int GetTotalPages()
    {
        return _totalEntryCount == 0 ? 0 : (int)Math.Ceiling(_totalEntryCount / (double)PageSize);
    }

    private int GetDisplayPageNumber()
    {
        return GetTotalPages() == 0 ? 0 : _currentPage + 1;
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
        RefreshPaginationUi();
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
