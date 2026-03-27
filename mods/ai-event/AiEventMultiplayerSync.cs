using System;
using System.Collections.Generic;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
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

    private static RunLocationTargetedMessageBuffer? _buffer;
    private static INetGameService? _netService;
    private static bool _registered;

    public static void InitializeForRun()
    {
        RunManager manager = RunManager.Instance;
        INetGameService? netService = manager.NetService;
        RunLocationTargetedMessageBuffer? buffer = manager.RunLocationTargetedBuffer;
        if (netService == null || buffer == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_registered && !ReferenceEquals(_buffer, buffer))
            {
                _buffer!.UnregisterMessageHandler<AiEventSelectionMessage>(HandleSelectionMessage);
                _registered = false;
            }

            _buffer = buffer;
            _netService = netService;

            if (_registered)
            {
                return;
            }

            _buffer.RegisterMessageHandler<AiEventSelectionMessage>(HandleSelectionMessage);
            _registered = true;
            PendingSelections.Clear();
        }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            if (_registered && _buffer != null)
            {
                _buffer.UnregisterMessageHandler<AiEventSelectionMessage>(HandleSelectionMessage);
            }

            PendingSelections.Clear();
            _registered = false;
            _buffer = null;
            _netService = null;
        }
    }

    public static bool IsClientControlled()
    {
        return RunManager.Instance.NetService?.Type == NetGameType.Client;
    }

    public static bool IsHostControlled()
    {
        return RunManager.Instance.NetService?.Type == NetGameType.Host;
    }

    public static void BroadcastSelection(AiEventSelectionDecision decision)
    {
        RunManager manager = RunManager.Instance;
        INetGameService? netService = manager.NetService;
        RunLocationTargetedMessageBuffer? buffer = manager.RunLocationTargetedBuffer;
        if (netService?.Type != NetGameType.Host || buffer == null)
        {
            return;
        }

        string payloadJson = decision.Payload == null ? string.Empty : JsonSerializer.Serialize(decision.Payload, JsonOptions);
        AiEventSelectionMessage message = new()
        {
            location = buffer.CurrentLocation,
            useVanilla = decision.UseVanilla,
            titlePrefix = decision.TitlePrefix ?? string.Empty,
            payloadJson = payloadJson,
        };

        netService.SendMessage(message);
    }

    public static bool TryConsumeSelection(out AiEventSelectionDecision decision)
    {
        decision = default;

        RunLocationTargetedMessageBuffer? buffer = RunManager.Instance.RunLocationTargetedBuffer;
        if (buffer == null)
        {
            return false;
        }

        RunLocation currentLocation = buffer.CurrentLocation;
        lock (SyncRoot)
        {
            if (!PendingSelections.TryGetValue(currentLocation, out Queue<AiEventSelectionDecision>? queue) || queue.Count == 0)
            {
                return false;
            }

            decision = queue.Dequeue();
            if (queue.Count == 0)
            {
                PendingSelections.Remove(currentLocation);
            }

            return true;
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

            if (!PendingSelections.TryGetValue(message.Location, out Queue<AiEventSelectionDecision>? queue))
            {
                queue = new Queue<AiEventSelectionDecision>();
                PendingSelections[message.Location] = queue;
            }

            queue.Enqueue(decision);
            MainFile.Logger.Info($"[ai-event] received host event selection for {message.Location} from {senderId}.");
        }
    }
}

public struct AiEventSelectionDecision
{
    public bool UseVanilla { get; init; }

    public string TitlePrefix { get; init; }

    public AiGeneratedEventPayload? Payload { get; init; }
}

public struct AiEventSelectionMessage : INetMessage, IRunLocationTargetedMessage
{
    public RunLocation location;

    public bool useVanilla;

    public string titlePrefix;

    public string payloadJson;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public RunLocation Location => location;

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
