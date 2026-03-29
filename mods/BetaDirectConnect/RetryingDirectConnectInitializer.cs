using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;

namespace BetaDirectConnect;

public sealed class RetryingDirectConnectInitializer : IClientConnectionInitializer
{
    private readonly string _playerIdText;
    private readonly string _ip;
    private readonly ushort _port;

    public RetryingDirectConnectInitializer(string playerIdText, string ip, ushort port)
    {
        _playerIdText = playerIdText;
        _ip = ip;
        _port = port;
    }

    public async Task<NetErrorInfo?> Connect(NetClientGameService gameService, CancellationToken cancelToken = default)
    {
        bool isNumeric = BetaDirectConnectConfigService.IsNumericPlayerIdText(_playerIdText);
        int maxAttempts = isNumeric ? 1 : 16;
        NetErrorInfo? lastError = null;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            string candidateText = attempt == 0 ? _playerIdText : $"{_playerIdText}#{attempt}";
            ulong playerId = BetaDirectConnectConfigService.ParsePlayerIdInput(candidateText);
            MainFile.Logger.Info($"Direct join handshake attempt {attempt + 1}/{maxAttempts}. playerIdText={_playerIdText}, candidate={candidateText}, playerId={playerId}");

            ENetClientConnectionInitializer initializer = new(playerId, _ip, _port);
            NetErrorInfo? error = await initializer.Connect(gameService, cancelToken);
            if (!error.HasValue)
            {
                if (attempt > 0)
                {
                    MainFile.Logger.Info($"Resolved player ID collision by retrying with candidate {candidateText} -> {playerId}");
                }

                return null;
            }

            lastError = error;
            if (isNumeric || error.Value.GetReason() != NetError.Kicked)
            {
                return error;
            }

            MainFile.Logger.Warn($"Join attempt was rejected with {error.Value.GetReason()}, retrying next mapped ulong.");
        }

        return lastError;
    }
}
