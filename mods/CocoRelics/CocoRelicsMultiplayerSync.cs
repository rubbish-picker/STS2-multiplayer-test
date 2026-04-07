using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace CocoRelics;

public static class CocoRelicsMultiplayerSync
{
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
    };
    private static readonly Dictionary<int, TaskCompletionSource<ObservedRoomInfo?>> PendingRequests = new();

    private static INetGameService? _netService;
    private static bool _registered;
    private static int _broadcastGeneration;
    private static int _nextRequestId;

    public static void InitializeForRun()
    {
        INetGameService? netService = RunManager.Instance.NetService;
        if (netService == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_registered && !ReferenceEquals(_netService, netService) && _netService != null)
            {
                _netService.UnregisterMessageHandler<CocoRelicsConfigMessage>(HandleConfigMessage);
                _netService.UnregisterMessageHandler<CocoRelicsObserveRoomRequestMessage>(HandleObserveRoomRequestMessage);
                _netService.UnregisterMessageHandler<CocoRelicsObserveRoomResponseMessage>(HandleObserveRoomResponseMessage);
                _registered = false;
            }

            _netService = netService;
            if (_registered)
            {
                return;
            }

            _netService.RegisterMessageHandler<CocoRelicsConfigMessage>(HandleConfigMessage);
            _netService.RegisterMessageHandler<CocoRelicsObserveRoomRequestMessage>(HandleObserveRoomRequestMessage);
            _netService.RegisterMessageHandler<CocoRelicsObserveRoomResponseMessage>(HandleObserveRoomResponseMessage);
            _registered = true;
        }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            _broadcastGeneration++;
            if (_registered && _netService != null)
            {
                _netService.UnregisterMessageHandler<CocoRelicsConfigMessage>(HandleConfigMessage);
                _netService.UnregisterMessageHandler<CocoRelicsObserveRoomRequestMessage>(HandleObserveRoomRequestMessage);
                _netService.UnregisterMessageHandler<CocoRelicsObserveRoomResponseMessage>(HandleObserveRoomResponseMessage);
            }

            _registered = false;
            _netService = null;
            foreach (TaskCompletionSource<ObservedRoomInfo?> pending in PendingRequests.Values)
            {
                pending.TrySetResult(null);
            }
            PendingRequests.Clear();
        }

        CocoRelicsConfigService.ClearHostConfig();
    }

    public static void BroadcastCurrentConfig()
    {
        INetGameService? netService = RunManager.Instance.NetService;
        if (netService?.Type != NetGameType.Host)
        {
            return;
        }

        int generation;
        lock (SyncRoot)
        {
            generation = ++_broadcastGeneration;
        }

        BroadcastCurrentConfigInternal(netService);
        TaskHelper.RunSafely(BroadcastCurrentConfigWithRetriesAsync(generation));
    }

    private static void BroadcastCurrentConfigInternal(INetGameService netService)
    {
        CocoRelicsRuntimeConfig config = CocoRelicsConfigService.GetEffectiveConfig();
        netService.SendMessage(new CocoRelicsConfigMessage
        {
            configJson = JsonSerializer.Serialize(config, JsonOptions),
        });
    }

    private static async Task BroadcastCurrentConfigWithRetriesAsync(int generation)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            await Task.Delay(1000 + attempt * 750);

            INetGameService? netService;
            lock (SyncRoot)
            {
                if (generation != _broadcastGeneration)
                {
                    return;
                }

                netService = _netService ?? RunManager.Instance.NetService;
            }

            if (netService?.Type != NetGameType.Host)
            {
                return;
            }

            BroadcastCurrentConfigInternal(netService);
        }
    }

    private static void HandleConfigMessage(CocoRelicsConfigMessage message, ulong senderId)
    {
        INetGameService? netService = _netService ?? RunManager.Instance.NetService;
        if (netService?.Type != NetGameType.Client || string.IsNullOrWhiteSpace(message.configJson))
        {
            return;
        }

        CocoRelicsRuntimeConfig? config = JsonSerializer.Deserialize<CocoRelicsRuntimeConfig>(message.configJson, JsonOptions);
        if (config == null)
        {
            return;
        }

        CocoRelicsConfigService.ApplyHostConfig(config);
        MainFile.Logger.Info($"[CocoRelics] received host config from {senderId}: mode={config.Mode} high_probability_bonus_chance={config.HighProbabilityBonusChance} preview_path_mode={config.PreviewPathMode} debug_start_relic={config.DebugStartRelic}.");
        CocoRelicsPatches.TryReconcileDebugRelicAfterHostConfigSync();
    }

    public static bool WaitForHostConfig(int timeoutMs = 5000)
    {
        INetGameService? netService = _netService ?? RunManager.Instance.NetService;
        if (netService?.Type != NetGameType.Client)
        {
            return true;
        }

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));
        while (DateTime.UtcNow < deadline)
        {
            if (CocoRelicsConfigService.SyncedFromHost != null)
            {
                return true;
            }

            try
            {
                netService.Update();
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[CocoRelics] failed while pumping multiplayer messages during host config wait: {ex.Message}");
            }

            Thread.Sleep(25);
        }

        return CocoRelicsConfigService.SyncedFromHost != null;
    }

    public static async Task<ObservedRoomInfo> GetOrRequestObservedRoomAsync(MapPoint point, RunState runState)
    {
        INetGameService? netService = RunManager.Instance.NetService;
        if (netService?.Type != NetGameType.Client || point.PointType == MapPointType.Shop)
        {
            if (netService?.Type == NetGameType.Client && point.PointType == MapPointType.Shop)
            {
                MainFile.Logger.Info($"[CocoRelics] using local shop preview for {point.coord} on client {netService.NetId}.");
            }

            return await CocoRelicsState.GetOrObserveAsync(point, runState);
        }

        if (CocoRelicsState.TryGet(point.coord, runState.CurrentActIndex, out ObservedRoomInfo existing))
        {
            return existing;
        }

        int requestId;
        TaskCompletionSource<ObservedRoomInfo?> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (SyncRoot)
        {
            requestId = ++_nextRequestId;
            PendingRequests[requestId] = pending;
        }

        netService.SendMessage(new CocoRelicsObserveRoomRequestMessage
        {
            requestId = requestId,
            actIndex = runState.CurrentActIndex,
            coord = point.coord,
        });

        ObservedRoomInfo? response = await pending.Task;
        lock (SyncRoot)
        {
            PendingRequests.Remove(requestId);
        }

        if (response == null)
        {
            throw new InvalidOperationException("Failed to receive authoritative room preview from host.");
        }

        return response;
    }

    private static void HandleObserveRoomResponseMessage(CocoRelicsObserveRoomResponseMessage message, ulong _)
    {
        INetGameService? netService = _netService ?? RunManager.Instance.NetService;
        if (netService?.Type != NetGameType.Client)
        {
            return;
        }

        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null || string.IsNullOrWhiteSpace(message.roomJson))
        {
            return;
        }

        CocoObservedRoomInfoSave? roomSave = JsonSerializer.Deserialize<CocoObservedRoomInfoSave>(message.roomJson, JsonOptions);
        if (roomSave == null)
        {
            return;
        }

        if (!CocoRelicsStorage.TryFromSave(roomSave, runState, out ObservedRoomInfo? info) || info == null)
        {
            lock (SyncRoot)
            {
                if (PendingRequests.TryGetValue(message.requestId, out TaskCompletionSource<ObservedRoomInfo?>? pendingFailed))
                {
                    pendingFailed.TrySetResult(null);
                }
            }
            return;
        }

        CocoRelicsState.SetObserved(roomSave.Coord, roomSave.ActIndex, info, runState);

        lock (SyncRoot)
        {
            if (PendingRequests.TryGetValue(message.requestId, out TaskCompletionSource<ObservedRoomInfo?>? pending))
            {
                pending.TrySetResult(info);
            }
        }
    }

    private static void HandleObserveRoomRequestMessage(CocoRelicsObserveRoomRequestMessage message, ulong senderId)
    {
        INetGameService? netService = _netService ?? RunManager.Instance.NetService;
        if (netService?.Type != NetGameType.Host)
        {
            return;
        }

        RunState? runState = CocoRelicsState.GetRunState();
        MapPoint? point = runState?.Map.GetPoint(message.coord);
        if (runState == null || point == null)
        {
            return;
        }

        TaskHelper.RunSafely(SendObservedRoomResponseAsync(netService, senderId, message.requestId, point, runState));
    }

    private static async Task SendObservedRoomResponseAsync(INetGameService netService, ulong senderId, int requestId, MapPoint point, RunState runState)
    {
        ObservedRoomInfo info = await CocoRelicsState.GetOrObserveAsync(point, runState);
        CocoObservedRoomInfoSave save = CocoRelicsStorage.CreateRoomSave(new ObservedRoomKey(runState.CurrentActIndex, point.coord), info);
        netService.SendMessage(new CocoRelicsObserveRoomResponseMessage
        {
            requestId = requestId,
            roomJson = JsonSerializer.Serialize(save, JsonOptions),
        }, senderId);
    }
}

public struct CocoRelicsConfigMessage : INetMessage
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

public struct CocoRelicsObserveRoomRequestMessage : INetMessage
{
    public int requestId;
    public int actIndex;
    public MapCoord coord;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(requestId);
        writer.WriteInt(actIndex);
        writer.Write(coord);
    }

    public void Deserialize(PacketReader reader)
    {
        requestId = reader.ReadInt();
        actIndex = reader.ReadInt();
        coord = reader.Read<MapCoord>();
    }
}

public struct CocoRelicsObserveRoomResponseMessage : INetMessage
{
    public int requestId;
    public string roomJson;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(requestId);
        writer.WriteString(roomJson ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        requestId = reader.ReadInt();
        roomJson = reader.ReadString();
    }
}
