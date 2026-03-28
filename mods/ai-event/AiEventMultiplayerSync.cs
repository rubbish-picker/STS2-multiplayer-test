using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace AiEvent;

public static class AiEventMultiplayerSync
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<RunLocation, Queue<AiEventSelectionDecision>> PendingSelections = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    private static INetGameService? _netService;
    private static bool _registered;
    private static bool _configRegistered;

    public static void InitializeForRun()
    {
        RunManager manager = RunManager.Instance;
        INetGameService? netService = manager.NetService;
        if (netService == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_registered && !ReferenceEquals(_netService, netService) && _netService != null)
            {
                _netService.UnregisterMessageHandler<AiEventSelectionMessage>(HandleSelectionMessage);
                _registered = false;
            }

            if (_configRegistered && !ReferenceEquals(_netService, netService) && _netService != null)
            {
                _netService.UnregisterMessageHandler<AiEventConfigMessage>(HandleConfigMessage);
                _configRegistered = false;
            }

            _netService = netService;

            if (_registered)
            {
                return;
            }

            _netService.RegisterMessageHandler<AiEventSelectionMessage>(HandleSelectionMessage);
            _registered = true;
            PendingSelections.Clear();

            if (!_configRegistered)
            {
                _netService.RegisterMessageHandler<AiEventConfigMessage>(HandleConfigMessage);
                _configRegistered = true;
            }
        }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            if (_registered && _netService != null)
            {
                _netService.UnregisterMessageHandler<AiEventSelectionMessage>(HandleSelectionMessage);
            }

            if (_configRegistered && _netService != null)
            {
                _netService.UnregisterMessageHandler<AiEventConfigMessage>(HandleConfigMessage);
            }

            PendingSelections.Clear();
            _registered = false;
            _configRegistered = false;
            _netService = null;
        }

        AiEventConfigService.ClearHostConfig();
    }

    public static bool IsClientControlled()
    {
        return RunManager.Instance.NetService?.Type == NetGameType.Client;
    }

    public static bool IsHostControlled()
    {
        return RunManager.Instance.NetService?.Type == NetGameType.Host;
    }

    public static void BroadcastConfig()
    {
        INetGameService? netService = RunManager.Instance.NetService;
        if (netService?.Type != NetGameType.Host)
        {
            return;
        }

        AiEventRuntimeConfig config = AiEventConfigService.GetEffectiveConfig();
        netService.SendMessage(new AiEventConfigMessage
        {
            configJson = JsonSerializer.Serialize(config, JsonOptions),
        });

        MainFile.Logger.Info($"[ai-event] broadcast host config: mode={config.Mode}.");
    }

    public static void BroadcastSelection(AiEventSelectionDecision decision)
    {
        RunManager manager = RunManager.Instance;
        INetGameService? netService = manager.NetService;
        RunState? state = manager.DebugOnlyGetState();
        if (netService?.Type != NetGameType.Host || state == null)
        {
            return;
        }

        string payloadJson = decision.Payload == null ? string.Empty : JsonSerializer.Serialize(decision.Payload, JsonOptions);
        AiEventSelectionMessage message = new()
        {
            location = state.CurrentLocation,
            useVanilla = decision.UseVanilla,
            titlePrefix = decision.TitlePrefix ?? string.Empty,
            payloadJson = payloadJson,
        };

        netService.SendMessage(message);
    }

    public static bool TryConsumeSelection(RunLocation location, out AiEventSelectionDecision decision, int timeoutMs = 0)
    {
        DateTime deadline = timeoutMs > 0
            ? DateTime.UtcNow.AddMilliseconds(timeoutMs)
            : DateTime.UtcNow;

        while (true)
        {
            decision = default;
            lock (SyncRoot)
            {
                if (PendingSelections.TryGetValue(location, out Queue<AiEventSelectionDecision>? queue) && queue.Count > 0)
                {
                    decision = queue.Dequeue();
                    if (queue.Count == 0)
                    {
                        PendingSelections.Remove(location);
                    }

                    return true;
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            try
            {
                (_netService ?? RunManager.Instance.NetService)?.Update();
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[ai-event] failed while pumping multiplayer messages during host selection wait: {ex.Message}");
            }

            Thread.Sleep(25);
        }
    }

    private static void HandleSelectionMessage(AiEventSelectionMessage message, ulong senderId)
    {
        lock (SyncRoot)
        {
            INetGameService? netService = _netService ?? RunManager.Instance.NetService;
            if (netService?.Type != NetGameType.Client)
            {
                return;
            }

            AiEventSelectionDecision decision = new()
            {
                UseVanilla = message.useVanilla,
                TitlePrefix = message.titlePrefix ?? string.Empty,
                Payload = string.IsNullOrWhiteSpace(message.payloadJson)
                    ? null
                    : JsonSerializer.Deserialize<AiGeneratedEventPayload>(message.payloadJson, JsonOptions),
            };

            if (!PendingSelections.TryGetValue(message.location, out Queue<AiEventSelectionDecision>? queue))
            {
                queue = new Queue<AiEventSelectionDecision>();
                PendingSelections[message.location] = queue;
            }

            queue.Enqueue(decision);
            MainFile.Logger.Info($"[ai-event] received host event selection for {message.location} from {senderId}.");
        }
    }

    private static void HandleConfigMessage(AiEventConfigMessage message, ulong senderId)
    {
        lock (SyncRoot)
        {
            INetGameService? netService = _netService ?? RunManager.Instance.NetService;
            if (netService?.Type != NetGameType.Client)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(message.configJson))
            {
                return;
            }

            AiEventRuntimeConfig? config = JsonSerializer.Deserialize<AiEventRuntimeConfig>(message.configJson, JsonOptions);
            if (config == null)
            {
                return;
            }

            AiEventConfigService.ApplyHostConfig(config);
            MainFile.Logger.Info($"[ai-event] received host config from {senderId}: mode={config.Mode}.");
        }
    }
}

public struct AiEventSelectionDecision
{
    public bool UseVanilla { get; init; }

    public string TitlePrefix { get; init; }

    public AiGeneratedEventPayload? Payload { get; init; }
}

public struct AiEventSelectionMessage : INetMessage
{
    public RunLocation location;

    public bool useVanilla;

    public string titlePrefix;

    public string payloadJson;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(location);
        writer.WriteBool(useVanilla);
        writer.WriteString(titlePrefix ?? string.Empty);
        writer.WriteString(payloadJson ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        location = reader.Read<RunLocation>();
        useVanilla = reader.ReadBool();
        titlePrefix = reader.ReadString();
        payloadJson = reader.ReadString();
    }

    public override string ToString()
    {
        return $"{nameof(AiEventSelectionMessage)} location: {location} useVanilla: {useVanilla}";
    }
}

public struct AiEventConfigMessage : INetMessage
{
    public string configJson;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(configJson ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        configJson = reader.ReadString();
    }
}
