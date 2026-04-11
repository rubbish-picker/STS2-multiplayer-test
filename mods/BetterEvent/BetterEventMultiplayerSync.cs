using System.Text.Json;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace BetterEvent;

public static class BetterEventMultiplayerSync
{
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    private static INetGameService? _netService;
    private static bool _registered;
    private static int _broadcastGeneration;

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
                _netService.UnregisterMessageHandler<BetterEventConfigMessage>(HandleConfigMessage);
                _registered = false;
            }

            _netService = netService;
            if (_registered)
            {
                return;
            }

            _netService.RegisterMessageHandler<BetterEventConfigMessage>(HandleConfigMessage);
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
                _netService.UnregisterMessageHandler<BetterEventConfigMessage>(HandleConfigMessage);
            }

            _registered = false;
            _netService = null;
        }

        BetterEventConfigService.ClearHostConfig();
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
        BetterEventRuntimeConfig config = BetterEventConfigService.GetEffectiveConfig();
        netService.SendMessage(new BetterEventConfigMessage
        {
            configJson = JsonSerializer.Serialize(config, JsonOptions),
        });

        MainFile.Logger.Info($"[BetterEvent] broadcast host config: mode={config.Mode}.");
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

    private static void HandleConfigMessage(BetterEventConfigMessage message, ulong senderId)
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

        BetterEventRuntimeConfig? config = JsonSerializer.Deserialize<BetterEventRuntimeConfig>(message.configJson, JsonOptions);
        if (config == null)
        {
            return;
        }

        BetterEventConfigService.ApplyHostConfig(config);
        MainFile.Logger.Info($"[BetterEvent] received host config from {senderId}: mode={config.Mode}.");
    }
}

public struct BetterEventConfigMessage : INetMessage
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
