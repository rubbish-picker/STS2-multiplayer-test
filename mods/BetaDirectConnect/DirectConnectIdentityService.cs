using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Quality;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace BetaDirectConnect;

public static class DirectConnectIdentityService
{
    private enum HostSessionMode
    {
        NewLobby,
        LoadedLobby,
        Running,
    }

    private sealed class HostSessionContext
    {
        public required NetHostGameService Service { get; init; }
        public required ulong LocalNetId { get; set; }
        public required string LocalDisplayId { get; set; }
        public required HostSessionMode Mode { get; set; }
        public HashSet<ulong> EligibleNetIds { get; } = [];
        public StartRunLobby? StartRunLobby { get; set; }
        public LoadRunLobby? LoadRunLobby { get; set; }
    }

    private sealed class ClientIdentityState
    {
        public TaskCompletionSource<ulong>? AssignmentCompletion { get; set; }
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<INetGameService, HostSessionContext> HostSessions = [];
    private static readonly Dictionary<INetGameService, ClientIdentityState> ClientStates = [];
    private static readonly Dictionary<ulong, string> DisplayNames = [];

    private static readonly FieldInfo ENetHostConnectionsField =
        AccessTools.Field(typeof(ENetHost), "_connectedPeers")
        ?? throw new InvalidOperationException("Could not find ENetHost._connectedPeers.");

    private static readonly FieldInfo ENetClientNetIdField =
        AccessTools.Field(typeof(ENetClient), "_netId")
        ?? throw new InvalidOperationException("Could not find ENetClient._netId.");

    private static readonly FieldInfo NetHostGameServiceConnectedPeersField =
        AccessTools.Field(typeof(NetHostGameService), "_connectedPeers")
        ?? throw new InvalidOperationException("Could not find NetHostGameService._connectedPeers.");

    private static readonly FieldInfo NetHostGameServiceQualityTrackerField =
        AccessTools.Field(typeof(NetHostGameService), "_qualityTracker")
        ?? throw new InvalidOperationException("Could not find NetHostGameService._qualityTracker.");

    private static readonly FieldInfo NetQualityTrackerStatsField =
        AccessTools.Field(typeof(NetQualityTracker), "_stats")
        ?? throw new InvalidOperationException("Could not find NetQualityTracker._stats.");

    public static ulong PrepareLocalHostNetId(IEnumerable<ulong>? preferredEligibleNetIds = null)
    {
        ulong? requested = BetaDirectConnectConfigService.Current.NetIdOverride ?? BetaDirectConnectConfigService.GetRequestedNetId();
        if (preferredEligibleNetIds != null)
        {
            HashSet<ulong> eligible = preferredEligibleNetIds.Where(id => id > 0UL).ToHashSet();
            if (requested.HasValue && eligible.Contains(requested.Value))
            {
                BetaDirectConnectConfigService.UpdateLastAssignedNetId(requested.Value);
                return requested.Value;
            }

            ulong fallback = eligible.FirstOrDefault();
            if (fallback > 0UL)
            {
                BetaDirectConnectConfigService.UpdateLastAssignedNetId(fallback);
                return fallback;
            }
        }

        ulong localNetId = requested ?? BetaDirectConnectConfigService.CreateStableAutomaticNetIdSeed();
        BetaDirectConnectConfigService.UpdateLastAssignedNetId(localNetId);
        return localNetId;
    }

    public static void RegisterNewLobbyHost(NetHostGameService service, ulong localNetId, string displayId)
    {
        RegisterHostInternal(service, localNetId, displayId, HostSessionMode.NewLobby, null);
    }

    public static void RegisterLoadedLobbyHost(NetHostGameService service, ulong localNetId, string displayId, IEnumerable<ulong> eligibleNetIds)
    {
        RegisterHostInternal(service, localNetId, displayId, HostSessionMode.LoadedLobby, eligibleNetIds);
    }

    public static void RegisterRunningHost(INetGameService service, IEnumerable<ulong> eligibleNetIds)
    {
        lock (Sync)
        {
            if (!HostSessions.TryGetValue(service, out HostSessionContext? session))
            {
                return;
            }

            session.Mode = HostSessionMode.Running;
            session.EligibleNetIds.Clear();
            foreach (ulong netId in eligibleNetIds.Where(id => id > 0UL))
            {
                session.EligibleNetIds.Add(netId);
            }
        }
    }

    public static void AttachStartRunLobby(StartRunLobby lobby)
    {
        lock (Sync)
        {
            if (HostSessions.TryGetValue(lobby.NetService, out HostSessionContext? session))
            {
                session.StartRunLobby = lobby;
            }
        }
    }

    public static void AttachLoadRunLobby(LoadRunLobby lobby)
    {
        lock (Sync)
        {
            if (HostSessions.TryGetValue(lobby.NetService, out HostSessionContext? session))
            {
                session.LoadRunLobby = lobby;
            }
        }
    }

    public static ulong GetEffectiveHostNetId()
    {
        ulong? overrideNetId = BetaDirectConnectConfigService.Current.NetIdOverride;
        if (overrideNetId.HasValue)
        {
            return overrideNetId.Value;
        }

        if (BetaDirectConnectConfigService.Current.LastAssignedNetId > 0UL)
        {
            return BetaDirectConnectConfigService.Current.LastAssignedNetId;
        }

        return BetaDirectConnectConfigService.CreateStableAutomaticNetIdSeed();
    }

    public static string GetDisplayName(ulong netId)
    {
        lock (Sync)
        {
            return DisplayNames.TryGetValue(netId, out string? displayId) ? displayId : netId.ToString();
        }
    }

    public static bool TryGetDisplayName(ulong netId, out string displayId)
    {
        lock (Sync)
        {
            if (DisplayNames.TryGetValue(netId, out string? value))
            {
                displayId = value;
                return true;
            }
        }

        displayId = string.Empty;
        return false;
    }

    public static bool HasDisplayName(ulong netId)
    {
        lock (Sync)
        {
            return DisplayNames.ContainsKey(netId);
        }
    }

    public static void RegisterClient(NetClientGameService service)
    {
        lock (Sync)
        {
            if (ClientStates.ContainsKey(service))
            {
                return;
            }

            ClientStates[service] = new ClientIdentityState();
        }

        service.RegisterMessageHandler<DirectConnectIdentityAssignedMessage>(HandleAssignedMessage);
        service.RegisterMessageHandler<DirectConnectDisplayIdentityMessage>(HandleDisplayIdentityMessage);
    }

    public static void UnregisterClient(NetClientGameService service)
    {
        service.UnregisterMessageHandler<DirectConnectIdentityAssignedMessage>(HandleAssignedMessage);
        service.UnregisterMessageHandler<DirectConnectDisplayIdentityMessage>(HandleDisplayIdentityMessage);
        lock (Sync)
        {
            ClientStates.Remove(service);
        }
    }

    public static void ClearRuntimeState()
    {
        lock (Sync)
        {
            HostSessions.Clear();
            ClientStates.Clear();
            DisplayNames.Clear();
        }
    }

    public static async Task EnsureClientIdentityAssigned(NetClientGameService service)
    {
        ClientIdentityState state;
        lock (Sync)
        {
            if (!ClientStates.TryGetValue(service, out state!))
            {
                throw new InvalidOperationException("Client identity state is not registered.");
            }

            if (state.AssignmentCompletion == null || state.AssignmentCompletion.Task.IsCompleted)
            {
                state.AssignmentCompletion = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
                SendIdentityRequest(service);
            }
        }

        ulong assignedNetId = await state.AssignmentCompletion.Task;
        MainFile.Logger.Info($"Direct-connect client logical netId assigned: {assignedNetId}");
    }

    public static ulong GenerateTemporaryTransportNetId()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        ulong value = BitConverter.ToUInt64(bytes);
        value &= 0x7FFFFFFFFFFFFFFFUL;
        if (value <= 1UL)
        {
            value += 2UL;
        }

        if (value == BetaDirectConnectConfigService.GetRequestedNetId())
        {
            value += 7919UL;
        }

        return value;
    }

    private static void RegisterHostInternal(NetHostGameService service, ulong localNetId, string displayId, HostSessionMode mode, IEnumerable<ulong>? eligibleNetIds)
    {
        lock (Sync)
        {
            if (HostSessions.TryGetValue(service, out HostSessionContext? existing))
            {
                existing.LocalNetId = localNetId;
                existing.LocalDisplayId = displayId;
                existing.Mode = mode;
                existing.EligibleNetIds.Clear();
                if (eligibleNetIds != null)
                {
                    foreach (ulong netId in eligibleNetIds.Where(id => id > 0UL))
                    {
                        existing.EligibleNetIds.Add(netId);
                    }
                }
            }
            else
            {
                HostSessionContext session = new()
                {
                    Service = service,
                    LocalNetId = localNetId,
                    LocalDisplayId = displayId,
                    Mode = mode,
                };
                if (eligibleNetIds != null)
                {
                    foreach (ulong netId in eligibleNetIds.Where(id => id > 0UL))
                    {
                        session.EligibleNetIds.Add(netId);
                    }
                }

                HostSessions[service] = session;
                service.RegisterMessageHandler<DirectConnectIdentityRequestMessage>(HandleIdentityRequestMessage);
            }

            DisplayNames[localNetId] = displayId;
        }
    }

    private static void SendIdentityRequest(NetClientGameService service)
    {
        ulong? requestedNetId = BetaDirectConnectConfigService.GetRequestedNetId();
        string displayId = BetaDirectConnectConfigService.NormalizeDisplayId(BetaDirectConnectConfigService.Current.DisplayId);
        DirectConnectIdentityRequestMessage message = new()
        {
            hasRequestedNetId = requestedNetId.HasValue,
            requestedNetId = requestedNetId ?? 0UL,
            displayId = displayId,
        };
        MainFile.Logger.Info($"Requesting host-assigned direct-connect identity. requestedNetId={requestedNetId?.ToString() ?? "<auto>"} displayId={displayId}");
        service.SendMessage(message);
    }

    private static void HandleIdentityRequestMessage(DirectConnectIdentityRequestMessage message, ulong senderId)
    {
        if (!TryGetHostSessionForRequest(senderId, out HostSessionContext? session, out ulong transportPeerId))
        {
            MainFile.Logger.Warn($"Received direct-connect identity request from {senderId}, but no host session was available.");
            return;
        }

        ulong assignedNetId = AssignLogicalNetId(session!, transportPeerId, message);
        string displayId = BetaDirectConnectConfigService.NormalizeDisplayId(message.displayId);

        UpdateHostTransportPeerId(session!.Service, transportPeerId, assignedNetId);
        UpdateConnectedPlayerId(session, transportPeerId, assignedNetId);

        lock (Sync)
        {
            DisplayNames[assignedNetId] = displayId;
        }

        MainFile.Logger.Info($"Assigned logical netId {assignedNetId} to transport peer {transportPeerId} with displayId={displayId}");

        session.Service.SendMessage(new DirectConnectIdentityAssignedMessage
        {
            assignedNetId = assignedNetId,
            displayId = displayId,
        }, assignedNetId);

        BroadcastDisplayIdentity(session.Service, assignedNetId, displayId, exceptPeerId: assignedNetId);
        SendKnownDisplayNamesToPeer(session.Service, assignedNetId);
    }

    private static bool TryGetHostSessionForRequest(ulong senderId, out HostSessionContext? session, out ulong transportPeerId)
    {
        lock (Sync)
        {
            foreach ((INetGameService _, HostSessionContext context) in HostSessions)
            {
                if (ContainsTransportPeer(context.Service, senderId))
                {
                    session = context;
                    transportPeerId = senderId;
                    return true;
                }
            }
        }

        session = null;
        transportPeerId = 0UL;
        return false;
    }

    private static ulong AssignLogicalNetId(HostSessionContext session, ulong transportPeerId, DirectConnectIdentityRequestMessage request)
    {
        HashSet<ulong> usedIds = GetUsedLogicalIds(session.Service);
        usedIds.Add(session.LocalNetId);

        ulong? requestedNetId = request.hasRequestedNetId ? request.requestedNetId : null;
        if (session.Mode != HostSessionMode.NewLobby)
        {
            if (requestedNetId.HasValue && session.EligibleNetIds.Contains(requestedNetId.Value) && !usedIds.Contains(requestedNetId.Value))
            {
                return requestedNetId.Value;
            }

            ulong availableEligible = session.EligibleNetIds
                .Where(id => id != session.LocalNetId && !usedIds.Contains(id))
                .OrderBy(id => id)
                .FirstOrDefault();
            if (availableEligible > 0UL)
            {
                return availableEligible;
            }
        }
        else if (requestedNetId.HasValue && !usedIds.Contains(requestedNetId.Value) && requestedNetId.Value > 1UL)
        {
            return requestedNetId.Value;
        }

        ulong next = Math.Max(session.LocalNetId + 1UL, 2UL);
        while (usedIds.Contains(next))
        {
            next++;
        }

        return next;
    }

    private static HashSet<ulong> GetUsedLogicalIds(NetHostGameService service)
    {
        List<NetClientData> peers = GetConnectedPeers(service);
        return peers.Select(peer => peer.peerId).Where(id => id > 0UL).ToHashSet();
    }

    private static void UpdateHostTransportPeerId(NetHostGameService service, ulong transportPeerId, ulong assignedNetId)
    {
        List<NetClientData> peers = GetConnectedPeers(service);
        for (int index = 0; index < peers.Count; index++)
        {
            if (peers[index].peerId == transportPeerId)
            {
                NetClientData updated = peers[index];
                updated.peerId = assignedNetId;
                peers[index] = updated;
                break;
            }
        }

        NetQualityTracker qualityTracker = (NetQualityTracker)NetHostGameServiceQualityTrackerField.GetValue(service)!;
        List<ConnectionStats> stats = (List<ConnectionStats>)NetQualityTrackerStatsField.GetValue(qualityTracker)!;
        foreach (ConnectionStats stat in stats.Where(stat => stat.PeerId == transportPeerId))
        {
            typeof(ConnectionStats).GetProperty(nameof(ConnectionStats.PeerId), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(stat, assignedNetId);
        }

        if (service.NetHost is ENetHost eNetHost)
        {
            IList transportConnections = (IList)ENetHostConnectionsField.GetValue(eNetHost)!;
            for (int index = 0; index < transportConnections.Count; index++)
            {
                object entry = transportConnections[index]!;
                FieldInfo? netIdField = entry.GetType().GetField("netId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (netIdField == null || (ulong)netIdField.GetValue(entry)! != transportPeerId)
                {
                    continue;
                }

                netIdField.SetValue(entry, assignedNetId);
                transportConnections[index] = entry;
                break;
            }
        }
    }

    private static void UpdateConnectedPlayerId(HostSessionContext session, ulong transportPeerId, ulong assignedNetId)
    {
        if (session.StartRunLobby != null)
        {
            RewriteConnectingPlayerIds(session.StartRunLobby, transportPeerId, assignedNetId);
        }

        if (session.LoadRunLobby != null)
        {
            RewriteConnectingPlayerIds(session.LoadRunLobby, transportPeerId, assignedNetId);
        }
    }

    private static void RewriteConnectingPlayerIds(object lobby, ulong oldPlayerId, ulong newPlayerId)
    {
        FieldInfo? connectingPlayersField = AccessTools.Field(lobby.GetType(), "_connectingPlayers");
        if (connectingPlayersField?.GetValue(lobby) is not IList connectingPlayers)
        {
            return;
        }

        for (int index = 0; index < connectingPlayers.Count; index++)
        {
            object entry = connectingPlayers[index]!;
            FieldInfo? idField = entry.GetType().GetField("id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (idField == null || (ulong)idField.GetValue(entry)! != oldPlayerId)
            {
                continue;
            }

            idField.SetValue(entry, newPlayerId);
            connectingPlayers[index] = entry;
            break;
        }
    }

    private static bool ContainsTransportPeer(NetHostGameService service, ulong peerId)
    {
        return GetConnectedPeers(service).Any(peer => peer.peerId == peerId);
    }

    private static List<NetClientData> GetConnectedPeers(NetHostGameService service)
    {
        return (List<NetClientData>)NetHostGameServiceConnectedPeersField.GetValue(service)!;
    }

    private static void BroadcastDisplayIdentity(NetHostGameService service, ulong netId, string displayId, ulong exceptPeerId)
    {
        foreach (NetClientData peer in GetConnectedPeers(service))
        {
            if (!peer.readyForBroadcasting || peer.peerId == exceptPeerId)
            {
                continue;
            }

            service.SendMessage(new DirectConnectDisplayIdentityMessage
            {
                netId = netId,
                displayId = displayId,
            }, peer.peerId);
        }
    }

    private static void SendKnownDisplayNamesToPeer(NetHostGameService service, ulong peerId)
    {
        KeyValuePair<ulong, string>[] snapshot;
        lock (Sync)
        {
            snapshot = DisplayNames.ToArray();
        }

        foreach (KeyValuePair<ulong, string> pair in snapshot)
        {
            service.SendMessage(new DirectConnectDisplayIdentityMessage
            {
                netId = pair.Key,
                displayId = pair.Value,
            }, peerId);
        }
    }

    private static void HandleAssignedMessage(DirectConnectIdentityAssignedMessage message, ulong _)
    {
        NetClientGameService? service = FindClientServiceByPendingAssignment();
        if (service?.NetClient is ENetClient eNetClient)
        {
            ENetClientNetIdField.SetValue(eNetClient, message.assignedNetId);
        }

        lock (Sync)
        {
            DisplayNames[message.assignedNetId] = message.displayId;
            BetaDirectConnectConfigService.UpdateLastAssignedNetId(message.assignedNetId);

            if (service != null && ClientStates.TryGetValue(service, out ClientIdentityState? state))
            {
                state.AssignmentCompletion?.TrySetResult(message.assignedNetId);
            }
        }
    }

    private static void HandleDisplayIdentityMessage(DirectConnectDisplayIdentityMessage message, ulong _)
    {
        lock (Sync)
        {
            DisplayNames[message.netId] = message.displayId;
        }
    }

    private static NetClientGameService? FindClientServiceByPendingAssignment()
    {
        lock (Sync)
        {
            foreach ((INetGameService service, ClientIdentityState state) in ClientStates)
            {
                if (state.AssignmentCompletion != null && !state.AssignmentCompletion.Task.IsCompleted)
                {
                    return service as NetClientGameService;
                }
            }
        }

        return null;
    }
}
