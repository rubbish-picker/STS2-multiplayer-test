using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace BetaDirectConnect;

public struct DirectConnectIdentityRequestMessage : INetMessage, IPacketSerializable
{
    public ulong clientId;
    public bool hasRequestedNetId;
    public ulong requestedNetId;
    public string displayId;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(clientId);
        writer.WriteBool(hasRequestedNetId);
        if (hasRequestedNetId)
        {
            writer.WriteULong(requestedNetId);
        }

        writer.WriteString(displayId ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        clientId = reader.ReadULong();
        hasRequestedNetId = reader.ReadBool();
        if (hasRequestedNetId)
        {
            requestedNetId = reader.ReadULong();
        }
        else
        {
            requestedNetId = 0UL;
        }

        displayId = reader.ReadString();
    }
}

public struct DirectConnectIdentityAssignedMessage : INetMessage, IPacketSerializable
{
    public ulong clientId;
    public ulong assignedNetId;
    public string displayId;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(clientId);
        writer.WriteULong(assignedNetId);
        writer.WriteString(displayId ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        clientId = reader.ReadULong();
        assignedNetId = reader.ReadULong();
        displayId = reader.ReadString();
    }
}

public struct DirectConnectDisplayIdentityMessage : INetMessage, IPacketSerializable
{
    public ulong clientId;
    public ulong netId;
    public string displayId;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(clientId);
        writer.WriteULong(netId);
        writer.WriteString(displayId ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        clientId = reader.ReadULong();
        netId = reader.ReadULong();
        displayId = reader.ReadString();
    }
}
