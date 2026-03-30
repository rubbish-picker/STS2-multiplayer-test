using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CocoRelics;

public partial class CocoPreviewOverlay : Control
{
    private PanelContainer _previewHost = null!;
    private Label _titleLabel = null!;
    private Button _closeButton = null!;
    private Control _loadingMask = null!;
    private Node? _previewNode;

    public static CocoPreviewOverlay Create()
    {
        CocoPreviewOverlay overlay = new();
        overlay.Name = nameof(CocoPreviewOverlay);
        overlay.Visible = false;
        overlay.MouseFilter = MouseFilterEnum.Stop;
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.BuildUi();
        return overlay;
    }

    private void BuildUi()
    {
        ColorRect backdrop = new()
        {
            Color = new Color(0f, 0f, 0f, 0.84f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                ClosePreview();
            }
        };
        AddChild(backdrop);

        PanelContainer shell = new()
        {
            MouseFilter = MouseFilterEnum.Stop,
        };
        shell.SetAnchorsPreset(LayoutPreset.FullRect);
        shell.OffsetLeft = 72f;
        shell.OffsetTop = 36f;
        shell.OffsetRight = -72f;
        shell.OffsetBottom = -36f;
        AddChild(shell);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        shell.AddChild(margin);

        VBoxContainer root = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);

        HBoxContainer header = new();
        _titleLabel = new Label
        {
            Text = "房间预览",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.AddChild(_titleLabel);

        _closeButton = new Button
        {
            Text = "关闭预览",
            CustomMinimumSize = new Vector2(150f, 42f),
        };
        _closeButton.Pressed += ClosePreview;
        header.AddChild(_closeButton);
        root.AddChild(header);

        _previewHost = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
        };
        root.AddChild(_previewHost);

        _loadingMask = new Control
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _loadingMask.SetAnchorsPreset(LayoutPreset.FullRect);
        ColorRect loadingBg = new()
        {
            Color = new Color(0f, 0f, 0f, 0.25f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        loadingBg.SetAnchorsPreset(LayoutPreset.FullRect);
        _loadingMask.AddChild(loadingBg);
        _previewHost.AddChild(_loadingMask);
    }

    public async Task OpenPreviewAsync(MapPoint point)
    {
        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null)
        {
            return;
        }

        ObservedRoomInfo info = CocoRelicsState.GetOrObserve(point, runState);
        Visible = true;
        MoveToFront();
        SetLoading(true);

        try
        {
            CloseCurrentNode();
            _titleLabel.Text = BuildTitle(info, point);
            Control previewContent = await CreatePreviewContentAsync(runState, info);
            previewContent.SetAnchorsPreset(LayoutPreset.FullRect);
            previewContent.MouseFilter = MouseFilterEnum.Ignore;
            _previewHost.AddChild(previewContent);
            _previewHost.MoveChild(previewContent, 0);
            _previewNode = previewContent;
            bool hasNativeClose = ConfigureNativeClose(previewContent);
            _closeButton.Visible = !hasNativeClose;
            DisableInteractionsRecursively(previewContent, hasNativeClose ? GetProceedButton(previewContent) : null);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[CocoRelics] failed to open room preview: {ex}");
            Label fallback = new()
            {
                Text = $"预览失败：{ex.Message}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            fallback.SetAnchorsPreset(LayoutPreset.FullRect);
            _previewHost.AddChild(fallback);
            _previewNode = fallback;
            _closeButton.Visible = true;
        }
        finally
        {
            SetLoading(false);
        }
    }

    public void ClosePreview()
    {
        CloseCurrentNode();
        _closeButton.Visible = true;
        Visible = false;
    }

    private void CloseCurrentNode()
    {
        if (_previewNode != null && GodotObject.IsInstanceValid(_previewNode))
        {
            _previewNode.QueueFree();
        }

        _previewNode = null;
        foreach (Node child in _previewHost.GetChildren())
        {
            if (child != _loadingMask)
            {
                child.QueueFree();
            }
        }
    }

    private void SetLoading(bool loading)
    {
        _loadingMask.Visible = loading;
        _closeButton.Disabled = loading;
    }

    private static string BuildTitle(ObservedRoomInfo info, MapPoint point)
    {
        return $"{point.coord}  {info.RoomType}";
    }

    private static async Task<Control> CreatePreviewContentAsync(RunState runState, ObservedRoomInfo info)
    {
        return info.RoomType switch
        {
            RoomType.Monster or RoomType.Elite or RoomType.Boss => await CreateCombatPreviewAsync(runState, info),
            RoomType.Event => await CreateEventPreviewAsync(runState, info),
            RoomType.Shop => await CreateShopPreviewAsync(runState),
            RoomType.Treasure => CreateTreasurePreview(runState),
            RoomType.RestSite => CreateRestSitePreview(runState),
            _ => CreateFallbackPreview(info.RoomType),
        };
    }

    private static async Task<Control> CreateCombatPreviewAsync(RunState runState, ObservedRoomInfo info)
    {
        EncounterModel encounter = ModelDb.GetById<EncounterModel>(info.ModelId!).ToMutable();
        RunState previewRun = RunState.CreateForTest(
            players: Array.Empty<Player>(),
            acts: runState.Acts.Select(act => act.ToMutable()).ToList(),
            modifiers: runState.Modifiers.ToList(),
            ascensionLevel: runState.AscensionLevel,
            seed: $"{runState.Rng.StringSeed}-coco-preview-{info.ModelId}");
        previewRun.CurrentActIndex = runState.CurrentActIndex;

        CombatRoom room = new(encounter, previewRun) { ShouldCreateCombat = false };
        encounter.GenerateMonstersWithSlots(previewRun);
        foreach ((MonsterModel monster, string? slot) in encounter.MonstersWithSlots)
        {
            Creature enemy = room.CombatState.CreateCreature(monster, CombatSide.Enemy, slot);
            room.CombatState.AddCreature(enemy);
        }

        await PreloadManager.LoadRoomCombatAssets(encounter, previewRun);

        PreviewCombatVisuals visuals = new()
        {
            Encounter = encounter,
            Enemies = room.CombatState.Enemies.ToList(),
            Act = previewRun.Act,
        };

        NCombatRoom? preview = NCombatRoom.Create(visuals, CombatRoomMode.VisualOnly);
        if (preview == null)
        {
            return CreateFallbackPreview(info.RoomType);
        }

        preview.ProceedButton.Visible = false;
        return preview;
    }

    private static async Task<Control> CreateEventPreviewAsync(RunState runState, ObservedRoomInfo info)
    {
        CharacterModel character = LocalContext.GetMe(runState)?.Character ?? runState.Players.First().Character;
        Player previewPlayer = Player.CreateForNewRun(character, runState.UnlockState, 1uL);
        RunState previewRun = RunState.CreateForTest(
            players: new[] { previewPlayer },
            acts: runState.Acts.Select(act => act.ToMutable()).ToList(),
            modifiers: runState.Modifiers.ToList(),
            ascensionLevel: runState.AscensionLevel,
            seed: $"{runState.Rng.StringSeed}-coco-event-{info.ModelId}");
        previewRun.CurrentActIndex = runState.CurrentActIndex;

        EventModel model = ModelDb.GetById<EventModel>(info.ModelId!).ToMutable();
        await model.BeginEvent(previewPlayer, false);
        NEventRoom? preview = NEventRoom.Create(model, previewRun, false);
        if (preview == null)
        {
            return CreateFallbackPreview(info.RoomType);
        }

        await Task.Delay(50);
        preview.Layout?.DisableEventOptions();
        return preview;
    }

    private static async Task<Control> CreateShopPreviewAsync(RunState runState)
    {
        Player player = LocalContext.GetMe(runState) ?? runState.Players.First();
        MerchantRoom room = new();
        MerchantInventory inventory = MerchantInventory.CreateForNormalMerchant(player);
        PropertyInfo? inventoryProperty = typeof(MerchantRoom).GetProperty(nameof(MerchantRoom.Inventory), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        inventoryProperty?.SetValue(room, inventory);
        await PreloadManager.LoadRoomMerchantAssets();
        NMerchantRoom? preview = NMerchantRoom.Create(room, runState.Players);
        return preview ?? CreateFallbackPreview(RoomType.Shop);
    }

    private static Control CreateTreasurePreview(IRunState runState)
    {
        NTreasureRoom? preview = NTreasureRoom.Create(new TreasureRoom(runState.CurrentActIndex), runState);
        return preview ?? CreateFallbackPreview(RoomType.Treasure);
    }

    private static Control CreateRestSitePreview(IRunState runState)
    {
        NRestSiteRoom? preview = NRestSiteRoom.Create(new RestSiteRoom(), runState);
        return preview ?? CreateFallbackPreview(RoomType.RestSite);
    }

    private static Control CreateFallbackPreview(RoomType roomType)
    {
        Label label = new()
        {
            Text = $"已观测房间：{roomType}",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetAnchorsPreset(LayoutPreset.FullRect);
        return label;
    }

    private bool ConfigureNativeClose(Node node)
    {
        NProceedButton? proceedButton = GetProceedButton(node);
        if (proceedButton == null)
        {
            return false;
        }

        DisconnectAllReleasedSignals(proceedButton);
        proceedButton.Visible = true;
        proceedButton.Enable();
        proceedButton.SetPulseState(isPulsing: false);
        proceedButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => ClosePreview()));
        return true;
    }

    private static NProceedButton? GetProceedButton(Node node)
    {
        return node switch
        {
            IRoomWithProceedButton roomWithProceedButton => roomWithProceedButton.ProceedButton,
            _ => null,
        };
    }

    private static void DisconnectAllReleasedSignals(NButton button)
    {
        foreach (Godot.Collections.Dictionary connection in button.GetSignalConnectionList(NClickableControl.SignalName.Released))
        {
            Variant callableVariant = connection["callable"];
            Callable callable = callableVariant.As<Callable>();
            if (button.IsConnected(NClickableControl.SignalName.Released, callable))
            {
                button.Disconnect(NClickableControl.SignalName.Released, callable);
            }
        }
    }

    private static void DisableInteractionsRecursively(Node node, Control? allowedControl)
    {
        if (node is NButton button)
        {
            if (!ReferenceEquals(button, allowedControl))
            {
                button.Disable();
            }
        }

        if (node is Control control)
        {
            if (!ReferenceEquals(control, allowedControl))
            {
                control.MouseFilter = MouseFilterEnum.Ignore;
                control.FocusMode = FocusModeEnum.None;
            }
            else
            {
                control.MouseFilter = MouseFilterEnum.Stop;
                control.FocusMode = FocusModeEnum.All;
            }
        }

        foreach (Node child in node.GetChildren())
        {
            DisableInteractionsRecursively(child, allowedControl);
        }
    }
}
