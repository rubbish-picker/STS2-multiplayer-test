using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace MultiplayerCard;

public static class MultiplayerCardMultiplayerSync
{
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    private static INetGameService? _netService;
    private static bool _registered;

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
                _netService.UnregisterMessageHandler<MultiplayerCardConfigMessage>(HandleConfigMessage);
                _registered = false;
            }

            _netService = netService;
            if (_registered)
            {
                return;
            }

            _netService.RegisterMessageHandler<MultiplayerCardConfigMessage>(HandleConfigMessage);
            _registered = true;
        }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            if (_registered && _netService != null)
            {
                _netService.UnregisterMessageHandler<MultiplayerCardConfigMessage>(HandleConfigMessage);
            }

            _registered = false;
            _netService = null;
        }

        MultiplayerCardConfigService.ClearHostConfig();
    }

    public static void BroadcastCurrentConfig()
    {
        INetGameService? netService = RunManager.Instance.NetService;
        if (netService?.Type != NetGameType.Host)
        {
            return;
        }

        MultiplayerCardRuntimeConfig config = MultiplayerCardConfigService.Current;
        netService.SendMessage(new MultiplayerCardConfigMessage
        {
            configJson = JsonSerializer.Serialize(config, JsonOptions),
        });
    }

    private static void HandleConfigMessage(MultiplayerCardConfigMessage message, ulong senderId)
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

        MultiplayerCardRuntimeConfig? config = JsonSerializer.Deserialize<MultiplayerCardRuntimeConfig>(message.configJson, JsonOptions);
        if (config == null)
        {
            return;
        }

        MultiplayerCardConfigService.ApplyHostConfig(config);
        MainFile.Logger.Info($"[MultiplayerCard] received host config from {senderId}: mode={config.Mode}, appearance={config.AppearanceMode}.");
    }
}

public struct MultiplayerCardConfigMessage : INetMessage
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
