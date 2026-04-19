using System.Collections.Concurrent;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using AgentTestApi.Infrastructure;

namespace AgentTestApi;

public partial class AgentTestApiNode : Node
{
    public const string NodeName = "AgentTestApiNode";

    private static readonly FieldInfo? DevConsoleField = AccessTools.Field(typeof(NDevConsole), "_devConsole");
    private static readonly FieldInfo? DevConsoleInstanceField = AccessTools.Field(typeof(NDevConsole), "_instance");

    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
    private int _mainThreadId;
    private AgentApiServer? _server;
    private AgentApiOptions? _options;

    public static AgentTestApiNode? Instance { get; private set; }

    private sealed class PreparedCardSelector : ICardSelector
    {
        private readonly IReadOnlyList<int>? _selectionIndexes;
        private readonly IReadOnlyList<string>? _selectionCardIds;

        public PreparedCardSelector(IReadOnlyList<int>? selectionIndexes, IReadOnlyList<string>? selectionCardIds)
        {
            _selectionIndexes = selectionIndexes;
            _selectionCardIds = selectionCardIds;
        }

        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            List<CardModel> optionList = options.ToList();
            List<CardModel> selectedCards;

            if (_selectionCardIds is { Count: > 0 })
            {
                selectedCards = new List<CardModel>(_selectionCardIds.Count);
                List<CardModel> remaining = optionList.ToList();
                foreach (string rawCardId in _selectionCardIds)
                {
                    string normalized = NormalizeLookup(rawCardId);
                    CardModel? match = remaining.FirstOrDefault(card =>
                        NormalizeLookup(card.Id.Entry.ToString()) == normalized ||
                        NormalizeLookup(card.Title) == normalized ||
                        NormalizeLookup(card.GetType().Name) == normalized);

                    if (match == null)
                    {
                        throw new InvalidOperationException($"Prepared selection could not find '{rawCardId}' in current options.");
                    }

                    selectedCards.Add(match);
                    remaining.Remove(match);
                }
            }
            else if (_selectionIndexes is { Count: > 0 })
            {
                selectedCards = new List<CardModel>(_selectionIndexes.Count);
                foreach (int selectionIndex in _selectionIndexes)
                {
                    if (selectionIndex < 0 || selectionIndex >= optionList.Count)
                    {
                        throw new InvalidOperationException($"Prepared selection index {selectionIndex} is out of range.");
                    }

                    selectedCards.Add(optionList[selectionIndex]);
                }
            }
            else
            {
                throw new InvalidOperationException("A prepared card selector requires selectionIndexes or selectionCardIds.");
            }

            if (selectedCards.Count < minSelect || selectedCards.Count > maxSelect)
            {
                throw new InvalidOperationException($"Prepared selection count {selectedCards.Count} is outside allowed range {minSelect}-{maxSelect}.");
            }

            return Task.FromResult<IEnumerable<CardModel>>(selectedCards);
        }

        public CardModel? GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
        {
            if (_selectionCardIds is { Count: > 0 })
            {
                string normalized = NormalizeLookup(_selectionCardIds[0]);
                CardCreationResult? match = options.FirstOrDefault(option =>
                    NormalizeLookup(option.Card.Id.Entry.ToString()) == normalized ||
                    NormalizeLookup(option.Card.Title) == normalized ||
                    NormalizeLookup(option.Card.GetType().Name) == normalized);
                return match?.Card;
            }

            if (_selectionIndexes is { Count: > 0 })
            {
                int selectionIndex = _selectionIndexes[0];
                if (selectionIndex < 0 || selectionIndex >= options.Count)
                {
                    throw new InvalidOperationException($"Prepared card reward index {selectionIndex} is out of range.");
                }

                return options[selectionIndex].Card;
            }

            return options.FirstOrDefault()?.Card;
        }
    }

    public static void AttachTo(NGame game)
    {
        if (Instance != null && GodotObject.IsInstanceValid(Instance))
        {
            return;
        }

        if (game.GetNodeOrNull<AgentTestApiNode>(NodeName) != null)
        {
            return;
        }

        AgentTestApiNode node = new()
        {
            Name = NodeName
        };

        game.CallDeferred(Node.MethodName.AddChild, node);
    }

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this && GodotObject.IsInstanceValid(Instance))
        {
            MainFile.Logger.Warn("[AgentTestApi] Duplicate runtime node detected, removing new instance.");
            QueueFree();
            return;
        }

        Instance = this;
    }

    public override void _Ready()
    {
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        SetProcess(true);

        _options = AgentApiOptions.FromCommandLine();
        _server = new AgentApiServer(this, _options);

        try
        {
            _server.Start();
            MainFile.Logger.Info($"[AgentTestApi] Listening on {_options.BaseUrl}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[AgentTestApi] Failed to start API server on {_options.BaseUrl}: {ex}");
        }
    }

    public override void _ExitTree()
    {
        _server?.Dispose();
        _server = null;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void _Process(double delta)
    {
        while (_mainThreadQueue.TryDequeue(out Action? action))
        {
            action();
        }
    }

    internal Task<T> RunOnMainThreadAsync<T>(Func<T> action)
    {
        if (System.Environment.CurrentManagedThreadId == _mainThreadId)
        {
            return Task.FromResult(action());
        }

        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _mainThreadQueue.Enqueue(() =>
        {
            try
            {
                tcs.TrySetResult(action());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    internal Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> action)
    {
        if (System.Environment.CurrentManagedThreadId == _mainThreadId)
        {
            return action();
        }

        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _mainThreadQueue.Enqueue(() =>
        {
            try
            {
                Task<T> task = action();
                task.ContinueWith(static (completedTask, state) =>
                {
                    TaskCompletionSource<T> completionSource = (TaskCompletionSource<T>)state!;
                    if (completedTask.IsCanceled)
                    {
                        completionSource.TrySetCanceled();
                    }
                    else if (completedTask.IsFaulted)
                    {
                        completionSource.TrySetException(completedTask.Exception!.InnerExceptions);
                    }
                    else
                    {
                        completionSource.TrySetResult(completedTask.Result);
                    }
                }, tcs, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    internal AgentHealthResponse BuildHealthSnapshot()
    {
        return new AgentHealthResponse
        {
            ModId = MainFile.ModId,
            Version = "v0.1.0",
            BaseUrl = _options?.BaseUrl ?? string.Empty,
            ProcessId = System.Environment.ProcessId,
            TimestampUtc = DateTimeOffset.UtcNow
        };
    }

    internal AgentStateResponse BuildStateSnapshot()
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        Player? localPlayer = LocalContext.GetMe(runState) ?? runState?.Players.FirstOrDefault();
        Control? focusOwner = NGame.Instance?.GetViewport()?.GuiGetFocusOwner();
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        NDevConsole? consoleNode = TryGetConsoleNode();

        return new AgentStateResponse
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            CurrentScreenType = currentScreen?.GetType().FullName,
            CurrentScreenPath = currentScreen is Node screenNode ? screenNode.GetPath().ToString() : null,
            FocusOwnerPath = focusOwner?.GetPath().ToString(),
            ConsoleVisible = consoleNode?.Visible ?? false,
            Run = runState == null ? null : BuildRunState(runState, localPlayer),
            Combat = combatState == null ? null : BuildCombatState(combatState)
        };
    }

    internal AgentConsoleCommandResponse ExecuteConsoleCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("command is required.", nameof(command));
        }

        DevConsole console = TryGetConsoleService()
            ?? throw new InvalidOperationException("Dev console is not available yet.");

        CmdResult result = console.ProcessCommand(command.Trim());
        return new AgentConsoleCommandResponse
        {
            Success = result.success,
            Message = result.msg,
            HasPendingTask = result.task != null,
            PendingTask = result.task
        };
    }

    internal byte[] CaptureScreenshotBytes()
    {
        Viewport viewport = NGame.Instance?.GetViewport()
            ?? throw new InvalidOperationException("Game viewport is not available.");

        Image image = viewport.GetTexture().GetImage();
        return image.SavePngToBuffer();
    }

    internal AgentInputResponse FocusDefaultControl()
    {
        ActiveScreenContext.Instance.FocusOnDefaultControl();
        return new AgentInputResponse
        {
            Mode = "focus-default",
            Message = "Focused current screen default control."
        };
    }

    internal AgentInputResponse InjectAction(AgentInputActionRequest request)
    {
        if (request.FocusDefault)
        {
            ActiveScreenContext.Instance.FocusOnDefaultControl();
        }

        string action = AgentApiInput.ResolveActionName(request.Action);
        string mode = AgentApiInput.NormalizeMode(request.Mode);
        float strength = request.Strength <= 0f ? 1f : request.Strength;

        switch (mode)
        {
            case "press":
                EmitAction(action, true, strength);
                break;
            case "release":
                EmitAction(action, false, 0f);
                break;
            case "tap":
                EmitAction(action, true, strength);
                EmitAction(action, false, 0f);
                break;
        }

        return new AgentInputResponse
        {
            Action = action,
            Mode = mode,
            Message = $"Injected action '{action}' with mode '{mode}'."
        };
    }

    internal AgentInputResponse InjectKey(AgentInputKeyRequest request)
    {
        if (request.FocusDefault)
        {
            ActiveScreenContext.Instance.FocusOnDefaultControl();
        }

        Key keycode = AgentApiInput.ParseKey(request.Keycode, request.KeycodeValue, nameof(request.Keycode));
        Key physicalKeycode = AgentApiInput.ParseOptionalKey(request.PhysicalKeycode, request.PhysicalKeycodeValue)
            ?? keycode;
        string mode = AgentApiInput.NormalizeMode(request.Mode);

        switch (mode)
        {
            case "press":
                EmitKey(request, keycode, physicalKeycode, true);
                break;
            case "release":
                EmitKey(request, keycode, physicalKeycode, false);
                break;
            case "tap":
                EmitKey(request, keycode, physicalKeycode, true);
                EmitKey(request, keycode, physicalKeycode, false);
                break;
        }

        return new AgentInputResponse
        {
            Keycode = keycode.ToString(),
            Mode = mode,
            Message = $"Injected key '{keycode}' with mode '{mode}'."
        };
    }

    internal async Task<AgentRunStartResponse> StartSingleplayerRunAsync(AgentRunStartRequest request)
    {
        if (NGame.Instance == null)
        {
            throw new InvalidOperationException("Game is not ready.");
        }

        if (request.ResetToMainMenu && RunManager.Instance.IsInProgress)
        {
            await NGame.Instance.ReturnToMainMenu();
        }
        else if (RunManager.Instance.IsInProgress)
        {
            throw new InvalidOperationException("A run is already in progress. Reset to main menu first.");
        }

        if (NGame.Instance.MainMenu == null)
        {
            throw new InvalidOperationException("Game is not at the main menu yet. Wait for startup to finish.");
        }

        CharacterModel character = ResolveCharacter(request.Character);
        string seed = string.IsNullOrWhiteSpace(request.Seed) ? SeedHelper.GetRandomSeed() : request.Seed.Trim();
        int ascensionLevel = Math.Max(0, request.AscensionLevel);

        await NGame.Instance.StartNewSingleplayerRun(
            character,
            request.ShouldSave,
            ActModel.GetDefaultList(),
            Array.Empty<ModifierModel>(),
            seed,
            ascensionLevel);

        return new AgentRunStartResponse
        {
            CharacterId = character.Id.Entry.ToString(),
            Seed = seed,
            AscensionLevel = ascensionLevel,
            State = BuildStateSnapshot()
        };
    }

    internal async Task<AgentResetRunResponse> ReturnToMainMenuAsync()
    {
        if (NGame.Instance == null)
        {
            throw new InvalidOperationException("Game is not ready.");
        }

        if (RunManager.Instance.IsInProgress)
        {
            await NGame.Instance.ReturnToMainMenu();
        }

        return new AgentResetRunResponse
        {
            Message = "Returned to main menu.",
            State = BuildStateSnapshot()
        };
    }

    internal async Task<string> EnterFightAsync(AgentFightRequest request)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            throw new InvalidOperationException("A run is not in progress.");
        }

        EncounterModel encounter = ResolveEncounter(request.Encounter);
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, MapPointType.Unassigned, encounter.ToMutable());
        return encounter.Id.Entry.ToString();
    }

    internal AgentPileCardsResponse BuildPileSnapshot(AgentPileQueryRequest request)
    {
        Player player = ResolveLocalPlayer(requireRun: true);
        PileType pileType = ResolvePileType(request.Pile, defaultPile: PileType.Hand);
        CardPile pile = pileType.GetPile(player);

        return new AgentPileCardsResponse
        {
            Pile = pileType.ToString(),
            Count = pile.Cards.Count,
            Cards = pile.Cards.Select(BuildCardState).ToList()
        };
    }

    internal async Task<AgentSpawnCardsResponse> SpawnCardsAsync(AgentSpawnCardsRequest request)
    {
        Player player = ResolveLocalPlayer(requireRun: true);
        CardModel canonicalCard = ResolveCardModel(request.CardId);
        PileType pileType = ResolvePileType(request.Pile, defaultPile: PileType.Hand);
        CardPilePosition position = ResolvePilePosition(request.Position, defaultPosition: CardPilePosition.Bottom);
        int count = Math.Max(1, request.Count);
        int upgradeCount = Math.Max(0, request.UpgradeCount);

        ICardScope scope = pileType.IsCombatPile()
            ? CombatManager.Instance.DebugOnlyGetState() ?? throw new InvalidOperationException("Combat is not active.")
            : RunManager.Instance.DebugOnlyGetState() ?? throw new InvalidOperationException("Run is not active.");

        List<CardModel> spawnedCards = new(count);
        for (int i = 0; i < count; i++)
        {
            CardModel card = scope.CreateCard(canonicalCard, player);
            for (int upgradeIndex = 0; upgradeIndex < upgradeCount && card.IsUpgradable; upgradeIndex++)
            {
                card.UpgradeInternal();
                card.FinalizeUpgradeInternal();
            }

            await CardPileCmd.Add(card, pileType, position);
            spawnedCards.Add(card);
        }

        return new AgentSpawnCardsResponse
        {
            CardId = canonicalCard.Id.Entry.ToString(),
            Pile = pileType.ToString(),
            Count = spawnedCards.Count,
            Cards = spawnedCards.Select(BuildCardState).ToList()
        };
    }

    internal AgentCardOperationContext BeginManualPlay(AgentPlayCardRequest request)
    {
        if (!CombatManager.Instance.IsInProgress)
        {
            throw new InvalidOperationException("Combat is not active.");
        }

        Player player = ResolveLocalPlayer(requireRun: true);
        CardModel card = ResolveHandCard(player, request);
        Creature? target = ResolvePlayTarget(card, player, request);
        IDisposable? selectionScope = CreatePreparedSelectionScope(request);
        AgentCardStateData cardBefore = BuildCardState(card);
        AgentCombatStateData? combatBefore = CombatManager.Instance.DebugOnlyGetState() is { } combatState
            ? BuildCombatState(combatState)
            : null;

        if (!card.TryManualPlay(target))
        {
            selectionScope?.Dispose();
            throw new InvalidOperationException($"Failed to manually play '{card.Id.Entry}'.");
        }

        return new AgentCardOperationContext
        {
            Card = card,
            SelectionScope = selectionScope,
            CardBefore = cardBefore,
            CombatBefore = combatBefore
        };
    }

    internal bool IsCardOperationSettled(AgentCardOperationContext context)
    {
        CardModel card = (CardModel)context.Card;
        bool cardLeftHand = card.Pile?.Type != PileType.Hand;
        bool queuesIdle = RunManager.Instance.ActionQueueSet?.IsEmpty ?? true;
        return cardLeftHand && queuesIdle;
    }

    internal AgentCardStateData? TryBuildCardState(AgentCardOperationContext context)
    {
        return context.Card is CardModel card ? BuildCardState(card) : null;
    }

    internal AgentCombatStateData? TryBuildCombatState()
    {
        return CombatManager.Instance.DebugOnlyGetState() is { } combatState
            ? BuildCombatState(combatState)
            : null;
    }

    internal void CompleteCardOperation(AgentCardOperationContext context)
    {
        context.SelectionScope?.Dispose();
    }

    internal async Task<AgentDrawCardsResponse> DrawCardsAsync(AgentDrawCardsRequest request)
    {
        if (!CombatManager.Instance.IsInProgress)
        {
            throw new InvalidOperationException("Combat is not active.");
        }

        Player player = ResolveLocalPlayer(requireRun: true);
        int count = Math.Max(1, request.Count);
        BlockingPlayerChoiceContext choiceContext = new();
        List<CardModel> drawnCards = (await CardPileCmd.Draw(choiceContext, count, player, request.FromHandDraw)).ToList();

        return new AgentDrawCardsResponse
        {
            Count = drawnCards.Count,
            Cards = drawnCards.Select(BuildCardState).ToList()
        };
    }

    internal AgentEndTurnResponse EndTurn()
    {
        if (!CombatManager.Instance.IsInProgress)
        {
            throw new InvalidOperationException("Combat is not active.");
        }

        Player player = ResolveLocalPlayer(requireRun: true);
        PlayerCmd.EndTurn(player, canBackOut: false);
        return new AgentEndTurnResponse
        {
            Message = $"Ended turn for player {player.NetId}.",
            Combat = TryBuildCombatState()
        };
    }

    private static AgentRunStateData BuildRunState(RunState runState, Player? localPlayer)
    {
        return new AgentRunStateData
        {
            IsInProgress = RunManager.Instance.IsInProgress,
            IsGameOver = RunManager.Instance.IsGameOver,
            ActIndex = runState.CurrentActIndex,
            ActNumber = runState.CurrentActIndex + 1,
            ActFloor = runState.ActFloor,
            TotalFloor = runState.TotalFloor,
            AscensionLevel = runState.AscensionLevel,
            CurrentRoomType = runState.CurrentRoom?.RoomType.ToString(),
            CurrentRoomClass = runState.CurrentRoom?.GetType().FullName,
            CurrentMapCoord = runState.CurrentMapCoord.HasValue
                ? new AgentMapCoordData
                {
                    Col = runState.CurrentMapCoord.Value.col,
                    Row = runState.CurrentMapCoord.Value.row
                }
                : null,
            Players = runState.Players.Select(player => BuildPlayerState(player, localPlayer)).ToList()
        };
    }

    private static AgentCombatStateData BuildCombatState(CombatState combatState)
    {
        return new AgentCombatStateData
        {
            IsInProgress = CombatManager.Instance.IsInProgress,
            RoundNumber = combatState.RoundNumber,
            CurrentSide = combatState.CurrentSide.ToString(),
            EncounterId = combatState.Encounter?.Id.Entry.ToString(),
            EncounterType = combatState.Encounter?.GetType().FullName,
            PlayerActionsDisabled = CombatManager.Instance.PlayerActionsDisabled,
            IsEnding = CombatManager.Instance.IsEnding,
            Players = combatState.Players.Select(player => BuildCombatCreatureState(player.Creature, isLocalPlayer: LocalContext.IsMe(player))).ToList(),
            Enemies = combatState.Enemies.Select(enemy => BuildCombatCreatureState(enemy, isLocalPlayer: false)).ToList()
        };
    }

    private static AgentPlayerStateData BuildPlayerState(Player player, Player? localPlayer)
    {
        return new AgentPlayerStateData
        {
            NetId = player.NetId,
            IsLocalPlayer = player == localPlayer,
            CharacterId = player.Character.Id.Entry.ToString(),
            CharacterType = player.Character.GetType().FullName,
            Gold = player.Gold,
            MaxEnergy = player.MaxEnergy,
            Energy = player.PlayerCombatState?.Energy,
            Stars = player.PlayerCombatState?.Stars,
            DeckCount = player.Deck.Cards.Count,
            PotionCount = player.Potions.Count(),
            RelicCount = player.Relics.Count,
            Creature = BuildCombatCreatureState(player.Creature, player == localPlayer)
        };
    }

    private static AgentCreatureStateData BuildCombatCreatureState(Creature creature, bool isLocalPlayer)
    {
        return new AgentCreatureStateData
        {
            CombatId = creature.CombatId,
            ModelId = creature.ModelId.Entry.ToString(),
            Name = creature.Name,
            Side = creature.Side.ToString(),
            IsPlayer = creature.IsPlayer,
            IsAlive = creature.IsAlive,
            CurrentHp = creature.CurrentHp,
            MaxHp = creature.MaxHp,
            Block = creature.Block,
            IsLocalPlayer = isLocalPlayer,
            Powers = creature.Powers.Select(BuildPowerState).ToList()
        };
    }

    private static AgentPowerStateData BuildPowerState(PowerModel power)
    {
        return new AgentPowerStateData
        {
            Id = power.Id.Entry.ToString(),
            Title = power.Title.GetFormattedText(),
            Description = power.DumbHoverTip.Description,
            Type = power.TypeForCurrentAmount.ToString(),
            StackType = power.StackType.ToString(),
            Amount = power.Amount,
            DisplayAmount = power.DisplayAmount
        };
    }

    private static AgentCardStateData BuildCardState(CardModel card)
    {
        CardPile? pile = card.Pile;
        PileType pileType = pile?.Type ?? PileType.None;
        bool canPlay = card.CanPlay(out _, out _);
        int? handIndex = null;
        if (pileType == PileType.Hand && pile != null)
        {
            for (int i = 0; i < pile.Cards.Count; i++)
            {
                if (ReferenceEquals(pile.Cards[i], card))
                {
                    handIndex = i;
                    break;
                }
            }
        }

        Dictionary<string, AgentDynamicVarStateData> dynamicVars = card.DynamicVars.ToDictionary(
            static pair => pair.Key,
            static pair => new AgentDynamicVarStateData
            {
                BaseValue = pair.Value.BaseValue,
                EnchantedValue = pair.Value.EnchantedValue,
                PreviewValue = pair.Value.PreviewValue,
                IntValue = pair.Value.IntValue
            });

        return new AgentCardStateData
        {
            Id = card.Id.Entry.ToString(),
            Title = card.Title,
            Keywords = card.Keywords.Select(keyword => keyword.ToString()).ToList(),
            Pile = pileType.ToString(),
            HandIndex = handIndex,
            CurrentUpgradeLevel = card.CurrentUpgradeLevel,
            MaxUpgradeLevel = card.MaxUpgradeLevel,
            IsUpgraded = card.IsUpgraded,
            IsPlayable = canPlay,
            TargetType = card.TargetType.ToString(),
            Description = card.GetDescriptionForPile(pileType),
            EnergyCostCanonical = card.EnergyCost.Canonical,
            EnergyCostBase = card.EnergyCost.GetWithModifiers(CostModifiers.None),
            EnergyCostLocal = card.EnergyCost.GetWithModifiers(CostModifiers.Local),
            EnergyCostCurrent = card.EnergyCost.GetResolved(),
            CostsX = card.EnergyCost.CostsX,
            StarCostCurrent = Math.Max(0, card.GetStarCostWithModifiers()),
            DynamicVars = dynamicVars
        };
    }

    private static Player ResolveLocalPlayer(bool requireRun)
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (requireRun && runState == null)
        {
            throw new InvalidOperationException("A run is not active.");
        }

        Player? player = LocalContext.GetMe(runState) ?? runState?.Players.FirstOrDefault();
        return player ?? throw new InvalidOperationException("Local player is not available.");
    }

    private static CharacterModel ResolveCharacter(string? rawCharacter)
    {
        if (string.IsNullOrWhiteSpace(rawCharacter))
        {
            return ModelDb.AllCharacters.First();
        }

        string normalized = NormalizeLookup(rawCharacter);
        CharacterModel? character = ModelDb.AllCharacters.FirstOrDefault(candidate =>
            NormalizeLookup(candidate.Id.Entry.ToString()) == normalized ||
            NormalizeLookup(candidate.GetType().Name) == normalized ||
            NormalizeLookup(candidate.Title.GetFormattedText()) == normalized);

        return character ?? throw new ArgumentException($"Character '{rawCharacter}' was not found.");
    }

    private static EncounterModel ResolveEncounter(string? rawEncounter)
    {
        if (string.IsNullOrWhiteSpace(rawEncounter))
        {
            throw new ArgumentException("encounter is required.");
        }

        string normalized = NormalizeLookup(rawEncounter);
        EncounterModel? encounter = ModelDb.AllEncounters.FirstOrDefault(candidate =>
            NormalizeLookup(candidate.Id.Entry.ToString()) == normalized);

        return encounter ?? throw new ArgumentException($"Encounter '{rawEncounter}' was not found.");
    }

    private static CardModel ResolveCardModel(string? rawCardId)
    {
        if (string.IsNullOrWhiteSpace(rawCardId))
        {
            throw new ArgumentException("cardId is required.");
        }

        string normalized = NormalizeLookup(rawCardId);
        CardModel? card = ModelDb.AllCards.FirstOrDefault(candidate =>
            NormalizeLookup(candidate.Id.Entry.ToString()) == normalized ||
            NormalizeLookup(candidate.GetType().Name) == normalized ||
            NormalizeLookup(candidate.Title) == normalized);

        return card ?? throw new ArgumentException($"Card '{rawCardId}' was not found.");
    }

    private static PileType ResolvePileType(string? rawPile, PileType defaultPile)
    {
        if (string.IsNullOrWhiteSpace(rawPile))
        {
            return defaultPile;
        }

        if (Enum.TryParse(rawPile.Trim(), ignoreCase: true, out PileType pileType))
        {
            return pileType;
        }

        throw new ArgumentException($"Pile '{rawPile}' is invalid.");
    }

    private static CardPilePosition ResolvePilePosition(string? rawPosition, CardPilePosition defaultPosition)
    {
        if (string.IsNullOrWhiteSpace(rawPosition))
        {
            return defaultPosition;
        }

        if (Enum.TryParse(rawPosition.Trim(), ignoreCase: true, out CardPilePosition position))
        {
            return position;
        }

        throw new ArgumentException($"Position '{rawPosition}' is invalid.");
    }

    private static CardModel ResolveHandCard(Player player, AgentPlayCardRequest request)
    {
        CardPile hand = PileType.Hand.GetPile(player);
        if (request.HandIndex.HasValue)
        {
            int handIndex = request.HandIndex.Value;
            if (handIndex < 0 || handIndex >= hand.Cards.Count)
            {
                throw new ArgumentException($"handIndex {handIndex} is out of range.");
            }

            return hand.Cards[handIndex];
        }

        if (string.IsNullOrWhiteSpace(request.CardId))
        {
            throw new ArgumentException("Either handIndex or cardId is required.");
        }

        string normalizedCardId = NormalizeLookup(request.CardId);
        List<CardModel> matches = hand.Cards
            .Where(card => NormalizeLookup(card.Id.Entry.ToString()) == normalizedCardId)
            .ToList();

        if (matches.Count == 0)
        {
            throw new ArgumentException($"No hand card matched '{request.CardId}'.");
        }

        int occurrence = Math.Max(0, request.Occurrence);
        if (occurrence >= matches.Count)
        {
            throw new ArgumentException($"Occurrence {occurrence} is out of range for '{request.CardId}'.");
        }

        return matches[occurrence];
    }

    private static Creature? ResolvePlayTarget(CardModel card, Player player, AgentPlayCardRequest request)
    {
        CombatState combatState = card.CombatState ?? CombatManager.Instance.DebugOnlyGetState()
            ?? throw new InvalidOperationException("Combat state is unavailable.");

        if (request.TargetCombatId.HasValue)
        {
            Creature? explicitTarget = combatState.GetCreature(request.TargetCombatId);
            return explicitTarget ?? throw new ArgumentException($"No creature with combatId {request.TargetCombatId.Value} exists.");
        }

        return card.TargetType switch
        {
            TargetType.AnyEnemy => ResolveEnemyTarget(combatState, request),
            TargetType.AnyAlly => ResolveAllyTarget(combatState, player, request),
            _ => null
        };
    }

    private static Creature ResolveEnemyTarget(CombatState combatState, AgentPlayCardRequest request)
    {
        List<Creature> enemies = combatState.HittableEnemies.ToList();
        if (enemies.Count == 0)
        {
            throw new InvalidOperationException("No valid enemy target exists.");
        }

        if (request.EnemyIndex.HasValue)
        {
            int enemyIndex = request.EnemyIndex.Value;
            if (enemyIndex < 0 || enemyIndex >= enemies.Count)
            {
                throw new ArgumentException($"enemyIndex {enemyIndex} is out of range.");
            }

            return enemies[enemyIndex];
        }

        return enemies[0];
    }

    private static Creature ResolveAllyTarget(CombatState combatState, Player player, AgentPlayCardRequest request)
    {
        if (request.TargetSelf)
        {
            return player.Creature;
        }

        List<Creature> allies = combatState.PlayerCreatures
            .Where(creature => creature.IsAlive && !ReferenceEquals(creature, player.Creature))
            .ToList();
        if (allies.Count == 0)
        {
            throw new InvalidOperationException("No valid ally target exists.");
        }

        if (request.AllyIndex.HasValue)
        {
            int allyIndex = request.AllyIndex.Value;
            if (allyIndex < 0 || allyIndex >= allies.Count)
            {
                throw new ArgumentException($"allyIndex {allyIndex} is out of range.");
            }

            return allies[allyIndex];
        }

        return allies[0];
    }

    private static string NormalizeLookup(string rawValue)
    {
        return rawValue
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
    }

    private static IDisposable? CreatePreparedSelectionScope(AgentPlayCardRequest request)
    {
        bool hasSelectionIndexes = request.SelectionIndexes is { Count: > 0 };
        bool hasSelectionCardIds = request.SelectionCardIds is { Count: > 0 };
        if (!hasSelectionIndexes && !hasSelectionCardIds)
        {
            return null;
        }

        PreparedCardSelector selector = new(request.SelectionIndexes, request.SelectionCardIds);
        return CardSelectCmd.PushSelector(selector);
    }

    private static void EmitAction(string action, bool pressed, float strength)
    {
        InputEventAction inputEvent = new()
        {
            Action = new StringName(action),
            Pressed = pressed,
            Strength = pressed ? strength : 0f
        };

        Input.ParseInputEvent(inputEvent);
    }

    private static void EmitKey(AgentInputKeyRequest request, Key keycode, Key physicalKeycode, bool pressed)
    {
        InputEventKey inputEvent = new()
        {
            Pressed = pressed,
            Echo = false,
            Keycode = keycode,
            PhysicalKeycode = physicalKeycode,
            Unicode = request.Unicode ?? 0,
            ShiftPressed = request.Shift,
            CtrlPressed = request.Ctrl,
            AltPressed = request.Alt,
            MetaPressed = request.Meta
        };

        Input.ParseInputEvent(inputEvent);
    }

    private static NDevConsole? TryGetConsoleNode()
    {
        return DevConsoleInstanceField?.GetValue(null) as NDevConsole;
    }

    private static DevConsole? TryGetConsoleService()
    {
        NDevConsole? consoleNode = TryGetConsoleNode();
        return consoleNode == null ? null : DevConsoleField?.GetValue(consoleNode) as DevConsole;
    }
}
