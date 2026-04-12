using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace BetaDirectConnect;

public struct DirectConnectIdentityRequestMessage : INetMessage, IPacketSerializable
{
    public bool hasRequestedNetId;
    public ulong requestedNetId;
    public string displayId;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteBool(hasRequestedNetId);
        if (hasRequestedNetId)
        {
            writer.WriteULong(requestedNetId);
        }

        writer.WriteString(displayId ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
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
    public ulong assignedNetId;
    public string displayId;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(assignedNetId);
        writer.WriteString(displayId ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        assignedNetId = reader.ReadULong();
        displayId = reader.ReadString();
    }
}

public struct DirectConnectDisplayIdentityMessage : INetMessage, IPacketSerializable
{
    public ulong netId;
    public string displayId;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(netId);
        writer.WriteString(displayId ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        netId = reader.ReadULong();
        displayId = reader.ReadString();
    }
}
