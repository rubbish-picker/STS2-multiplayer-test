using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CocoRelics;

public partial class CocoPreviewOverlay : Control
{
    private static readonly MethodInfo? AdjustCombatLayoutMethod = typeof(NCombatRoom).GetMethod("AdjustCreatureScaleForAspectRatio", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Vector2I PreviewViewportSize = new(1920, 1080);
    private static readonly Vector2 PreviewViewportSizeFloat = new(1920f, 1080f);
    private PanelContainer _previewHost = null!;
    private Label _titleLabel = null!;
    private Button _closeButton = null!;
    private NBackButton? _backButton;
    private Control _loadingMask = null!;
    private Node? _previewNode;

    public static CocoPreviewOverlay Create(NBackButton? backButtonTemplate = null)
    {
        CocoPreviewOverlay overlay = new();
        overlay.Name = nameof(CocoPreviewOverlay);
        overlay.Visible = false;
        overlay.MouseFilter = MouseFilterEnum.Stop;
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.BuildUi();
        overlay.BuildBackButton(backButtonTemplate);
        return overlay;
    }

    private void BuildBackButton(NBackButton? backButtonTemplate)
    {
        if (backButtonTemplate == null)
        {
            return;
        }

        _backButton = backButtonTemplate.Duplicate() as NBackButton;
        if (_backButton == null)
        {
            return;
        }

        _backButton.Name = "PreviewBack";
        _backButton.Visible = true;
        DisconnectAllReleasedSignals(_backButton);
        _backButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => ClosePreview()));
        _backButton.Disable();
        AddChild(_backButton);
        MoveChild(_backButton, GetChildCount() - 1);
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

        ObservedRoomInfo info = await CocoRelicsState.GetOrObserveAsync(point, runState);
        Visible = true;
        MoveToFront();
        SetLoading(true);
        _backButton?.Enable();

        try
        {
            CloseCurrentNode();
            _titleLabel.Text = BuildTitle(info, point);
            Control previewContent = await CreatePreviewContentAsync(runState, info);
            Control hostedPreview = WrapPreviewForHost(previewContent);
            hostedPreview.SetAnchorsPreset(LayoutPreset.FullRect);
            hostedPreview.MouseFilter = MouseFilterEnum.Ignore;
            bool isRestSitePreview = previewContent is NRestSiteRoom;
            CocoRelicsPatches.SetWatcherRestSitePreviewCompatibility(isRestSitePreview);
            _previewHost.AddChild(hostedPreview);
            CocoRelicsPatches.SetWatcherRestSitePreviewCompatibility(false);
            _previewHost.MoveChild(hostedPreview, 0);
            _previewNode = hostedPreview;
            PrepareHostedPreview(previewContent);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            PrepareHostedPreview(previewContent);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            FinalizeNativePreview(previewContent, runState);
            HidePreviewPlayerVisuals(previewContent);
            HidePreviewNavigation(previewContent);
            _closeButton.Visible = _backButton == null;
            DisableInteractionsRecursively(previewContent, null);
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
            CocoRelicsPatches.SetWatcherRestSitePreviewCompatibility(false);
            SetLoading(false);
        }
    }

    public void ClosePreview()
    {
        CloseCurrentNode();
        _closeButton.Visible = true;
        _backButton?.Disable();
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
            RoomType.Shop => await CreateShopPreviewAsync(runState, info),
            RoomType.Treasure => await CreateTreasurePreviewAsync(runState, info),
            RoomType.RestSite => await CreateRestSitePreviewAsync(runState),
            _ => CreateFallbackPreview(info.RoomType),
        };
    }

    private static async Task<Control> CreateCombatPreviewAsync(RunState runState, ObservedRoomInfo info)
    {
        EncounterModel encounter = ModelDb.GetById<EncounterModel>(info.ModelId!).ToMutable();
        CharacterModel character = LocalContext.GetMe(runState)?.Character ?? runState.Players.First().Character;
        Player previewPlayer = Player.CreateForNewRun(character, runState.UnlockState, 1uL);
        MainFile.Logger.Info($"[CocoRelics] combat preview step 1: creating preview run for encounter {encounter.Id}.");
        RunState previewRun = RunState.CreateForNewRun(
            players: new[] { previewPlayer },
            acts: runState.Acts.Select(act => (ActModel)act.ClonePreservingMutability()).ToList(),
            modifiers: runState.Modifiers.ToList(),
            ascensionLevel: runState.AscensionLevel,
            seed: $"{runState.Rng.StringSeed}-coco-preview-{info.ModelId}");
        previewRun.CurrentActIndex = runState.CurrentActIndex;

        MainFile.Logger.Info($"[CocoRelics] combat preview step 2: generating monsters for encounter {encounter.Id}.");
        encounter.GenerateMonstersWithSlots(previewRun);
        CombatState previewCombatState = new(encounter, previewRun, previewRun.Modifiers);
        previewCombatState.AddPlayer(previewPlayer);
        foreach (var (monster, slot) in encounter.MonstersWithSlots)
        {
            Creature creature = previewCombatState.CreateCreature(monster, CombatSide.Enemy, slot);
            previewCombatState.AddCreature(creature);
        }
        MainFile.Logger.Info($"[CocoRelics] combat preview step 3: preloading combat assets for encounter {encounter.Id}.");
        await PreloadManager.LoadRoomCombatAssets(encounter, previewRun);

        PreviewCombatVisuals visuals = new()
        {
            Encounter = encounter,
            Allies = previewCombatState.Allies,
            Enemies = previewCombatState.Enemies,
            Act = previewRun.Act,
        };

        MainFile.Logger.Info($"[CocoRelics] combat preview step 4: creating visual-only room for encounter {encounter.Id}.");
        NCombatRoom? preview = NCombatRoom.Create(visuals, CombatRoomMode.VisualOnly);
        if (preview == null)
        {
            return CreateFallbackPreview(info.RoomType);
        }

        return preview;
    }

    private static async Task<Control> CreateEventPreviewAsync(RunState runState, ObservedRoomInfo info)
    {
        CharacterModel character = LocalContext.GetMe(runState)?.Character ?? runState.Players.First().Character;
        Player previewPlayer = Player.CreateForNewRun(character, runState.UnlockState, 1uL);
        RunState previewRun = RunState.CreateForNewRun(
            players: new[] { previewPlayer },
            acts: runState.Acts.Select(act => (ActModel)act.ClonePreservingMutability()).ToList(),
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

        return preview;
    }

    private static async Task<Control> CreateShopPreviewAsync(RunState runState, ObservedRoomInfo info)
    {
        Player player = LocalContext.GetMe(runState) ?? runState.Players.First();
        MerchantRoom room = new();
        MerchantInventory inventory = info.ShopInventory ?? MerchantInventory.CreateForNormalMerchant(player);
        PropertyInfo? inventoryProperty = typeof(MerchantRoom).GetProperty(nameof(MerchantRoom.Inventory), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        inventoryProperty?.SetValue(room, inventory);
        await PreloadManager.LoadRoomMerchantAssets();
        NMerchantRoom? preview = NMerchantRoom.Create(room, runState.Players);
        return preview ?? CreateFallbackPreview(RoomType.Shop);
    }

    private static async Task<Control> CreateTreasurePreviewAsync(RunState runState, ObservedRoomInfo info)
    {
        if (info.TreasurePreview == null)
        {
            return CreateFallbackPreview(RoomType.Treasure);
        }

        List<Reward> previewRewards = new()
        {
            new GoldReward(info.TreasurePreview.GoldAmount, LocalContext.GetMe(runState) ?? runState.Players.First()),
        };
        previewRewards.AddRange(info.TreasurePreview.RelicIds.Select(id => (Reward)new RelicReward(ModelDb.GetById<RelicModel>(id).ToMutable(), LocalContext.GetMe(runState) ?? runState.Players.First())));
        previewRewards.AddRange(info.TreasurePreview.ExtraRewards);
        await PopulateRewardsAsync(previewRewards);
        return CreateRewardsPreview(runState, previewRewards);
    }

    private static async Task<Control> CreateRestSitePreviewAsync(IRunState runState)
    {
        Player player = LocalContext.GetMe(runState) ?? runState.Players.First();
        List<RestSiteOption> options = RestSiteOption.Generate(player);
        await PreloadManager.LoadRoomRestSite(runState.Act, options);
        RunManager.Instance.RestSiteSynchronizer.BeginRestSite();
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

    private static void HidePreviewNavigation(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            HidePreviewNavigation(child);
        }

        switch (node)
        {
            case NProceedButton proceedButton:
                DisconnectAllReleasedSignals(proceedButton);
                proceedButton.Visible = false;
                proceedButton.Disable();
                break;
            case NBackButton backButton:
                DisconnectAllReleasedSignals(backButton);
                backButton.Visible = false;
                backButton.Disable();
                break;
            case Button button when button != null:
                if (string.Equals(button.Text, "Proceed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(button.Text, "Skip", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(button.Text, "继续", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(button.Text, "前进", StringComparison.OrdinalIgnoreCase))
                {
                    button.Visible = false;
                    button.Disabled = true;
                }
                break;
        }
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

    private static void HidePreviewPlayerVisuals(Node node)
    {
        if (node is not NCombatRoom combatRoom)
        {
            return;
        }

        if (combatRoom.GetNodeOrNull<Control>("%AllyContainer") is { } allyContainer)
        {
            allyContainer.Visible = false;
        }

        if (combatRoom.GetNodeOrNull<Control>("%EnemyContainer") is { } enemyContainer)
        {
            enemyContainer.Visible = true;
        }

        foreach (var creatureNode in combatRoom.CreatureNodes)
        {
            if (creatureNode.Entity.IsPlayer || creatureNode.Entity.PetOwner != null)
            {
                creatureNode.Visible = false;
            }
            else
            {
                creatureNode.Visible = true;
            }
        }

        int enemyCount = combatRoom.CreatureNodes.Count(node => node.Entity.IsEnemy);
        MainFile.Logger.Info($"[CocoRelics] combat preview finalize: creatureNodes={combatRoom.CreatureNodes.Count()} enemyNodes={enemyCount}.");
    }

    private static void FinalizeNativePreview(Control previewContent, RunState runState)
    {
        switch (previewContent)
        {
            case NCombatRoom combatRoom:
                combatRoom.Size = PreviewViewportSizeFloat;
                if (combatRoom.Background == null)
                {
                    combatRoom.SetUpBackground(runState);
                }
                AdjustCombatLayoutMethod?.Invoke(combatRoom, Array.Empty<object>());
                break;
            case NMerchantRoom merchantRoom:
                merchantRoom.OpenInventory();
                break;
        }
    }

    private static async Task PopulateRewardsAsync(IEnumerable<Reward> rewards)
    {
        foreach (Reward reward in rewards)
        {
            await reward.Populate();
        }
    }

    private static Control CreateRewardsPreview(RunState runState, IReadOnlyList<Reward> rewards)
    {
        return PreviewRewardsScreen.Create(runState, rewards);
    }

    private static Control WrapPreviewForHost(Control previewContent)
    {
        if (previewContent is not (NCombatRoom or PreviewRewardsScreen))
        {
            previewContent.SetAnchorsPreset(LayoutPreset.FullRect);
            previewContent.Position = Vector2.Zero;
            return previewContent;
        }

        SubViewportContainer container = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Stretch = true,
            MouseFilter = MouseFilterEnum.Stop,
        };
        container.SetAnchorsPreset(LayoutPreset.FullRect);

        SubViewport viewport = new()
        {
            Size = PreviewViewportSize,
            TransparentBg = true,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = true,
        };
        container.AddChild(viewport);

        previewContent.SetAnchorsPreset(LayoutPreset.FullRect);
        previewContent.Position = Vector2.Zero;
        previewContent.CustomMinimumSize = PreviewViewportSizeFloat;
        previewContent.Size = PreviewViewportSizeFloat;
        viewport.AddChild(previewContent);
        return container;
    }

    private static void PrepareHostedPreview(Control previewContent)
    {
        previewContent.Position = Vector2.Zero;
        previewContent.Size = PreviewViewportSizeFloat;
        previewContent.CustomMinimumSize = PreviewViewportSizeFloat;
        if (previewContent is NCombatRoom combatRoom)
        {
            combatRoom.Position = Vector2.Zero;
            combatRoom.Size = PreviewViewportSizeFloat;
        }
    }
}
