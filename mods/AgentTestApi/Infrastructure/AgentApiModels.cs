using System.Text.Json.Serialization;

namespace AgentTestApi.Infrastructure;

internal sealed class AgentApiEnvelope
{
    public required bool Ok { get; init; }

    public object? Data { get; init; }

    public AgentApiError? Error { get; init; }
}

internal sealed class AgentApiError
{
    public required string Message { get; init; }

    public string? Details { get; init; }
}

internal sealed class AgentHttpRequest
{
    public required string Method { get; init; }

    public required string Path { get; init; }

    public required string HttpVersion { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    public required byte[] BodyBytes { get; init; }

    public string BodyText => BodyBytes.Length == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(BodyBytes);
}

internal sealed class AgentHttpResponse
{
    public required int StatusCode { get; init; }

    public required string ReasonPhrase { get; init; }

    public required string ContentType { get; init; }

    public required byte[] BodyBytes { get; init; }
}

internal sealed class AgentHealthResponse
{
    public required string ModId { get; init; }

    public required string Version { get; init; }

    public required string BaseUrl { get; init; }

    public required int ProcessId { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }
}

internal sealed class AgentStateResponse
{
    public required DateTimeOffset TimestampUtc { get; init; }

    public string? CurrentScreenType { get; init; }

    public string? CurrentScreenPath { get; init; }

    public string? FocusOwnerPath { get; init; }

    public required bool ConsoleVisible { get; init; }

    public AgentRunStateData? Run { get; init; }

    public AgentCombatStateData? Combat { get; init; }
}

internal sealed class AgentRunStateData
{
    public required bool IsInProgress { get; init; }

    public required bool IsGameOver { get; init; }

    public required int ActIndex { get; init; }

    public required int ActNumber { get; init; }

    public required int ActFloor { get; init; }

    public required int TotalFloor { get; init; }

    public required int AscensionLevel { get; init; }

    public string? CurrentRoomType { get; init; }

    public string? CurrentRoomClass { get; init; }

    public AgentMapCoordData? CurrentMapCoord { get; init; }

    public required List<AgentPlayerStateData> Players { get; init; }
}

internal sealed class AgentCombatStateData
{
    public required bool IsInProgress { get; init; }

    public required int RoundNumber { get; init; }

    public string? CurrentSide { get; init; }

    public string? EncounterId { get; init; }

    public string? EncounterType { get; init; }

    public required bool PlayerActionsDisabled { get; init; }

    public required bool IsEnding { get; init; }

    public required List<AgentCreatureStateData> Players { get; init; }

    public required List<AgentCreatureStateData> Enemies { get; init; }
}

internal sealed class AgentPlayerStateData
{
    public required ulong NetId { get; init; }

    public required bool IsLocalPlayer { get; init; }

    public string? CharacterId { get; init; }

    public string? CharacterType { get; init; }

    public required int Gold { get; init; }

    public required int MaxEnergy { get; init; }

    public int? Energy { get; init; }

    public int? Stars { get; init; }

    public required int DeckCount { get; init; }

    public required int PotionCount { get; init; }

    public required int RelicCount { get; init; }

    public required AgentCreatureStateData Creature { get; init; }
}

internal sealed class AgentCreatureStateData
{
    public uint? CombatId { get; init; }

    public string? ModelId { get; init; }

    public string? Name { get; init; }

    public string? Side { get; init; }

    public required bool IsPlayer { get; init; }

    public required bool IsAlive { get; init; }

    public required bool IsLocalPlayer { get; init; }

    public required int CurrentHp { get; init; }

    public required int MaxHp { get; init; }

    public required int Block { get; init; }

    public required List<AgentPowerStateData> Powers { get; init; }
}

internal sealed class AgentPowerStateData
{
    public string? Id { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? Type { get; init; }

    public string? StackType { get; init; }

    public required int Amount { get; init; }

    public required int DisplayAmount { get; init; }
}

internal sealed class AgentMapCoordData
{
    public required int Col { get; init; }

    public required int Row { get; init; }
}

internal sealed class AgentDynamicVarStateData
{
    public required decimal BaseValue { get; init; }

    public required decimal EnchantedValue { get; init; }

    public required decimal PreviewValue { get; init; }

    public required int IntValue { get; init; }
}

internal sealed class AgentCardStateData
{
    public string? Id { get; init; }

    public string? Title { get; init; }

    public required List<string> Keywords { get; init; }

    public string? Pile { get; init; }

    public int? HandIndex { get; init; }

    public required int CurrentUpgradeLevel { get; init; }

    public required int MaxUpgradeLevel { get; init; }

    public required bool IsUpgraded { get; init; }

    public required bool IsPlayable { get; init; }

    public string? TargetType { get; init; }

    public string? Description { get; init; }

    public required int EnergyCostCanonical { get; init; }

    public required int EnergyCostBase { get; init; }

    public required int EnergyCostLocal { get; init; }

    public required int EnergyCostCurrent { get; init; }

    public required bool CostsX { get; init; }

    public required int StarCostCurrent { get; init; }

    public required Dictionary<string, AgentDynamicVarStateData> DynamicVars { get; init; }
}

internal sealed class AgentPileCardsResponse
{
    public required string Pile { get; init; }

    public required int Count { get; init; }

    public required List<AgentCardStateData> Cards { get; init; }
}

internal sealed class AgentRunStartRequest
{
    public string? Character { get; set; }

    public string? Seed { get; set; }

    public int AscensionLevel { get; set; }

    public bool ResetToMainMenu { get; set; } = true;

    public bool ShouldSave { get; set; }
}

internal sealed class AgentRunStartResponse
{
    public required string CharacterId { get; init; }

    public required string Seed { get; init; }

    public required int AscensionLevel { get; init; }

    public required AgentStateResponse State { get; init; }
}

internal sealed class AgentResetRunResponse
{
    public required string Message { get; init; }

    public required AgentStateResponse State { get; init; }
}

internal sealed class AgentFightRequest
{
    public string? Encounter { get; set; }
}

internal sealed class AgentFightResponse
{
    public required string EncounterId { get; init; }

    public required AgentCombatStateData Combat { get; init; }
}

internal sealed class AgentPileQueryRequest
{
    public string? Pile { get; set; }
}

internal sealed class AgentSpawnCardsRequest
{
    public string? CardId { get; set; }

    public string? Pile { get; set; }

    public string? Position { get; set; }

    public int Count { get; set; } = 1;

    public int UpgradeCount { get; set; }
}

internal sealed class AgentSpawnCardsResponse
{
    public required string CardId { get; init; }

    public required string Pile { get; init; }

    public required int Count { get; init; }

    public required List<AgentCardStateData> Cards { get; init; }
}

internal sealed class AgentPlayCardRequest
{
    public int? HandIndex { get; set; }

    public string? CardId { get; set; }

    public int Occurrence { get; set; }

    public uint? TargetCombatId { get; set; }

    public int? EnemyIndex { get; set; }

    public int? AllyIndex { get; set; }

    public bool TargetSelf { get; set; }

    public List<int>? SelectionIndexes { get; set; }

    public List<string>? SelectionCardIds { get; set; }

    public bool WaitForResolution { get; set; } = true;

    public int TimeoutMs { get; set; } = 10000;
}

internal sealed class AgentPlayCardResponse
{
    public required bool Success { get; init; }

    public required string Message { get; init; }

    public AgentCardStateData? CardBefore { get; init; }

    public AgentCardStateData? CardAfter { get; init; }

    public AgentCombatStateData? CombatBefore { get; init; }

    public AgentCombatStateData? CombatAfter { get; init; }
}

internal sealed class AgentEndTurnResponse
{
    public required string Message { get; init; }

    public AgentCombatStateData? Combat { get; init; }
}

internal sealed class AgentDrawCardsRequest
{
    public int Count { get; set; } = 1;

    public bool FromHandDraw { get; set; }
}

internal sealed class AgentDrawCardsResponse
{
    public required int Count { get; init; }

    public required List<AgentCardStateData> Cards { get; init; }
}

internal sealed class AgentCardOperationContext
{
    [JsonIgnore]
    public required object Card { get; init; }

    [JsonIgnore]
    public IDisposable? SelectionScope { get; init; }

    public required AgentCardStateData CardBefore { get; init; }

    public required AgentCombatStateData? CombatBefore { get; init; }
}

internal sealed class AgentConsoleRequest
{
    public string? Command { get; set; }
}

internal sealed class AgentConsoleCommandResponse
{
    public required bool Success { get; init; }

    public required string Message { get; init; }

    public required bool HasPendingTask { get; init; }

    [JsonIgnore]
    public Task? PendingTask { get; init; }
}

internal sealed class AgentScreenshotRequest
{
    public string? Path { get; set; }
}

internal sealed class AgentScreenshotSavedResponse
{
    public required string Path { get; init; }

    public required int ByteCount { get; init; }
}

internal sealed class AgentInputActionRequest
{
    public string? Action { get; set; }

    public string? Mode { get; set; }

    public float Strength { get; set; } = 1f;

    public bool FocusDefault { get; set; }
}

internal sealed class AgentInputKeyRequest
{
    public string? Keycode { get; set; }

    public int? KeycodeValue { get; set; }

    public string? PhysicalKeycode { get; set; }

    public int? PhysicalKeycodeValue { get; set; }

    public long? Unicode { get; set; }

    public string? Mode { get; set; }

    public bool Shift { get; set; }

    public bool Ctrl { get; set; }

    public bool Alt { get; set; }

    public bool Meta { get; set; }

    public bool FocusDefault { get; set; }
}

internal sealed class AgentInputResponse
{
    public string? Action { get; init; }

    public string? Keycode { get; init; }

    public required string Mode { get; init; }

    public required string Message { get; init; }
}
