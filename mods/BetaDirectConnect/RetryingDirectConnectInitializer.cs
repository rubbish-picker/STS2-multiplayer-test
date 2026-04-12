using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;

namespace BetaDirectConnect;

public sealed class RetryingDirectConnectInitializer : IClientConnectionInitializer
{
    private readonly string _ip;
    private readonly ushort _port;

    public RetryingDirectConnectInitializer(string ip, ushort port)
    {
        _ip = ip;
        _port = port;
    }

    public async Task<NetErrorInfo?> Connect(NetClientGameService gameService, CancellationToken cancelToken = default)
    {
        ulong transportNetId = DirectConnectIdentityService.GenerateTemporaryTransportNetId();
        MainFile.Logger.Info($"Direct join handshake using temporary transport netId={transportNetId} ip={_ip} port={_port}");
        ENetClientConnectionInitializer initializer = new(transportNetId, _ip, _port);
        return await initializer.Connect(gameService, cancelToken);
    }
}
