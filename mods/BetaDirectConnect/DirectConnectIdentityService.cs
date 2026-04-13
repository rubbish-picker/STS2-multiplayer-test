using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
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
using MegaCrit.Sts2.Core.Saves;

namespace BetaDirectConnect;

public static class DirectConnectIdentityService
{
    public sealed class IdentitySnapshot
    {
        public required ulong ClientId { get; init; }
        public required ulong NetId { get; init; }
        public required string DisplayId { get; init; }
    }

    private enum HostSessionMode
    {
        NewLobby,
        LoadedLobby,
        Running,
    }

    private sealed class SavedIdentityManifest
    {
        public ulong LocalDefaultNetId { get; set; }
        public List<ulong> AllNetIds { get; set; } = [];
    }

    private sealed class HostSessionContext
    {
        public required NetHostGameService Service { get; init; }
        public required ulong LocalNetId { get; set; }
        public required string LocalDisplayId { get; set; }
        public required HostSessionMode Mode { get; set; }
        public HashSet<ulong> EligibleNetIds { get; } = [];
        public Dictionary<ulong, ulong> TransportToLogicalNetIds { get; } = [];
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
    private static readonly Dictionary<ulong, ulong> ClientIds = [];

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

            if (eligible.Contains(1UL))
            {
                BetaDirectConnectConfigService.UpdateLastAssignedNetId(1UL);
                return 1UL;
            }

            ulong fallback = eligible.FirstOrDefault();
            if (fallback > 0UL)
            {
                BetaDirectConnectConfigService.UpdateLastAssignedNetId(fallback);
                return fallback;
            }
        }

        ulong localNetId = requested ?? 1UL;
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

        return 1UL;
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

    public static bool TryGetClientId(ulong netId, out ulong clientId)
    {
        lock (Sync)
        {
            return ClientIds.TryGetValue(netId, out clientId);
        }
    }

    public static IReadOnlyList<IdentitySnapshot> GetIdentitySnapshots(IEnumerable<ulong> netIds)
    {
        lock (Sync)
        {
            return netIds
                .Distinct()
                .Where(netId => netId > 0UL)
                .Select(netId => new IdentitySnapshot
                {
                    ClientId = ClientIds.TryGetValue(netId, out ulong clientId) ? clientId : 0UL,
                    NetId = netId,
                    DisplayId = DisplayNames.TryGetValue(netId, out string? displayId) ? displayId : netId.ToString(),
                })
                .OrderBy(snapshot => snapshot.NetId)
                .ToArray();
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
            ClientIds.Clear();
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
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
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
            ClientIds[localNetId] = BetaDirectConnectConfigService.EffectiveClientId;
        }
    }

    private static void SendIdentityRequest(NetClientGameService service)
    {
        ulong? requestedNetId = ResolveRequestedNetIdForClientJoin();
        string displayId = BetaDirectConnectConfigService.NormalizeDisplayId(BetaDirectConnectConfigService.Current.DisplayId);
        DirectConnectIdentityRequestMessage message = new()
        {
            clientId = BetaDirectConnectConfigService.EffectiveClientId,
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
        ulong clientId = message.clientId > 0UL ? message.clientId : 1UL;

        UpdateHostTransportPeerId(session!.Service, transportPeerId, assignedNetId);
        UpdateConnectedPlayerId(session, transportPeerId, assignedNetId);

        lock (Sync)
        {
            ClientIds[assignedNetId] = clientId;
            DisplayNames[assignedNetId] = displayId;
            session.TransportToLogicalNetIds[transportPeerId] = assignedNetId;
        }

        MainFile.Logger.Info($"Assigned logical netId {assignedNetId} to transport peer {transportPeerId} with clientId={clientId} displayId={displayId}");

        session.Service.SendMessage(new DirectConnectIdentityAssignedMessage
        {
            clientId = clientId,
            assignedNetId = assignedNetId,
            displayId = displayId,
        }, assignedNetId);

        BroadcastDisplayIdentity(session.Service, clientId, assignedNetId, displayId, exceptPeerId: assignedNetId);
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

    public static bool TryResolveLogicalPeerId(INetGameService service, ulong peerOrLogicalId, out ulong logicalPeerId)
    {
        lock (Sync)
        {
            if (HostSessions.TryGetValue(service, out HostSessionContext? session))
            {
                if (session.TransportToLogicalNetIds.TryGetValue(peerOrLogicalId, out ulong mappedLogicalId))
                {
                    logicalPeerId = mappedLogicalId;
                    return true;
                }

                if (session.LocalNetId == peerOrLogicalId
                    || session.TransportToLogicalNetIds.Values.Contains(peerOrLogicalId)
                    || session.EligibleNetIds.Contains(peerOrLogicalId))
                {
                    logicalPeerId = peerOrLogicalId;
                    return true;
                }
            }
        }

        logicalPeerId = 0UL;
        return false;
    }

    private static ulong AssignLogicalNetId(HostSessionContext session, ulong transportPeerId, DirectConnectIdentityRequestMessage request)
    {
        HashSet<ulong> usedIds = GetUsedLogicalIds(session.Service);
        usedIds.Add(session.LocalNetId);

        ulong? requestedNetId = request.hasRequestedNetId ? request.requestedNetId : null;
        MainFile.Logger.Info(
            $"AssignLogicalNetId mode={session.Mode} transportPeerId={transportPeerId} requestedNetId={requestedNetId?.ToString() ?? "<auto>"} " +
            $"hostNetId={session.LocalNetId} used=[{string.Join(", ", usedIds.OrderBy(id => id))}] eligible=[{string.Join(", ", session.EligibleNetIds.OrderBy(id => id))}]");
        if (session.Mode != HostSessionMode.NewLobby)
        {
            if (requestedNetId.HasValue && session.EligibleNetIds.Contains(requestedNetId.Value) && !usedIds.Contains(requestedNetId.Value))
            {
                MainFile.Logger.Info($"AssignLogicalNetId accepted requested saved netId={requestedNetId.Value} for transportPeerId={transportPeerId}.");
                return requestedNetId.Value;
            }

            ulong availableEligible = session.EligibleNetIds
                .Where(id => id != session.LocalNetId && !usedIds.Contains(id))
                .OrderBy(id => id)
                .FirstOrDefault();
            if (availableEligible > 0UL)
            {
                MainFile.Logger.Info($"AssignLogicalNetId picked next eligible saved netId={availableEligible} for transportPeerId={transportPeerId}.");
                return availableEligible;
            }
        }
        else if (requestedNetId.HasValue && !usedIds.Contains(requestedNetId.Value) && requestedNetId.Value > 1UL)
        {
            MainFile.Logger.Info($"AssignLogicalNetId accepted requested new-lobby netId={requestedNetId.Value} for transportPeerId={transportPeerId}.");
            return requestedNetId.Value;
        }

        ulong next = 1UL;
        while (usedIds.Contains(next))
        {
            next++;
        }

        MainFile.Logger.Info($"AssignLogicalNetId fell back to sequential netId={next} for transportPeerId={transportPeerId}.");
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

    private static void BroadcastDisplayIdentity(NetHostGameService service, ulong clientId, ulong netId, string displayId, ulong exceptPeerId)
    {
        foreach (NetClientData peer in GetConnectedPeers(service))
        {
            if (!peer.readyForBroadcasting || peer.peerId == exceptPeerId)
            {
                continue;
            }

            service.SendMessage(new DirectConnectDisplayIdentityMessage
            {
                clientId = clientId,
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
                clientId = ClientIds.TryGetValue(pair.Key, out ulong clientId) ? clientId : 0UL,
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
            ClientIds[message.assignedNetId] = BetaDirectConnectConfigService.EffectiveClientId;
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
            ClientIds[message.netId] = message.clientId;
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

    private static ulong? ResolveRequestedNetIdForClientJoin()
    {
        if (BetaDirectConnectConfigService.Current.NetIdOverride.HasValue)
        {
            MainFile.Logger.Info($"ResolveRequestedNetIdForClientJoin using explicit override netId={BetaDirectConnectConfigService.Current.NetIdOverride.Value}.");
            return BetaDirectConnectConfigService.Current.NetIdOverride.Value;
        }

        if (TryGetSavedNetIdForLocalClient(out ulong savedNetId))
        {
            MainFile.Logger.Info($"ResolveRequestedNetIdForClientJoin using saved local default netId={savedNetId}.");
            return savedNetId;
        }

        MainFile.Logger.Info("ResolveRequestedNetIdForClientJoin found no explicit override or saved local default netId.");
        return null;
    }

    private static bool TryGetSavedNetIdForLocalClient(out ulong netId)
    {
        netId = 0UL;

        try
        {
            string manifestPath = GetMultiplayerIdentityManifestPath(BetaDirectConnectConfigService.EffectiveClientId);
            if (!File.Exists(manifestPath))
            {
                MainFile.Logger.Info($"TryGetSavedNetIdForLocalClient manifest not found at {manifestPath}.");
                return false;
            }

            string json = File.ReadAllText(manifestPath);
            SavedIdentityManifest? manifest = JsonSerializer.Deserialize<SavedIdentityManifest>(json);
            if (manifest == null)
            {
                MainFile.Logger.Warn($"TryGetSavedNetIdForLocalClient manifest deserialized to null at {manifestPath}.");
                return false;
            }

            manifest.AllNetIds = manifest.AllNetIds
                .Where(savedNetId => savedNetId > 0UL)
                .Distinct()
                .OrderBy(savedNetId => savedNetId)
                .ToList();
            if (manifest.LocalDefaultNetId <= 0UL || !manifest.AllNetIds.Contains(manifest.LocalDefaultNetId))
            {
                MainFile.Logger.Warn(
                    $"TryGetSavedNetIdForLocalClient invalid manifest at {manifestPath}. " +
                    $"localDefaultNetId={manifest.LocalDefaultNetId} allNetIds=[{string.Join(", ", manifest.AllNetIds)}]");
                return false;
            }

            netId = manifest.LocalDefaultNetId;
            MainFile.Logger.Info(
                $"TryGetSavedNetIdForLocalClient resolved localDefaultNetId={netId} from {manifestPath}. " +
                $"allNetIds=[{string.Join(", ", manifest.AllNetIds)}]");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to resolve saved direct-connect netId for local client: {ex.Message}");
            return false;
        }
    }

    private static string GetMultiplayerIdentityManifestPath(ulong localAccountId)
    {
        string godotPath = UserDataPathProvider.GetProfileScopedPath(
            SaveManager.Instance.CurrentProfileId,
            Path.Combine(UserDataPathProvider.SavesDir, "current_run_mp.identities.json"),
            PlatformType.None,
            localAccountId);
        return ProjectSettings.GlobalizePath(godotPath);
    }
}
