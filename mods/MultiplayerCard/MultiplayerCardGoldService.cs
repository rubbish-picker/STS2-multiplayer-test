using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace MultiplayerCard;

public static class MultiplayerCardGoldService
{
    private const int BaseFriendFeeDivisor = 4;
    private const int UpgradedFriendFeeDivisor = 2;
    private const int BaseFriendFeeBonusMultiplier = 2;
    private const int UpgradedFriendFeeBonusMultiplier = 3;

    private static readonly Dictionary<string, FriendFeeSnapshot> Snapshots = new();

    public static void CaptureFriendFeeSnapshot(CombatRoom room)
    {
        string key = GetRoomKey(room.CombatState.RunState);
        if (Snapshots.ContainsKey(key))
        {
            return;
        }

        Dictionary<ulong, HolderPowerCounts> holderPowerCounts = room.CombatState.Players
            .Select(player => new
            {
                player.NetId,
                BasePower = player.Creature.GetPower<FriendFeePower>(),
                UpgradedPower = player.Creature.GetPower<FriendFeePlusPower>(),
            })
            .Select(item => new
            {
                item.NetId,
                Counts = new HolderPowerCounts(
                    item.BasePower?.Amount ?? 0,
                    item.UpgradedPower?.Amount ?? 0),
            })
            .Where(item => item.Counts.BaseCount > 0 || item.Counts.UpgradedCount > 0)
            .ToDictionary(item => item.NetId, item => item.Counts);

        if (holderPowerCounts.Count == 0)
        {
            return;
        }

        Snapshots[key] = new FriendFeeSnapshot(holderPowerCounts);
    }

    public static void RewriteRewardsForFriendFee(IEnumerable<Reward> rewards)
    {
        List<Reward> rewardList = rewards as List<Reward> ?? rewards.ToList();
        if (rewardList.Count == 0)
        {
            return;
        }

        Player player = rewardList[0].Player;
        if (player.RunState?.CurrentRoom is not CombatRoom room)
        {
            return;
        }

        string key = GetRoomKey(player.RunState);
        if (!Snapshots.TryGetValue(key, out FriendFeeSnapshot? snapshot))
        {
            return;
        }

        if (!snapshot.MarkAdjusted(player.NetId))
        {
            return;
        }

        int goldRewardIndex = rewardList.FindIndex(static reward => reward is GoldReward);
        if (goldRewardIndex < 0 || rewardList[goldRewardIndex] is not GoldReward originalGoldReward)
        {
            return;
        }

        int localBaseGold = originalGoldReward.Amount;
        FriendFeeResolution resolution = ResolveForPlayer(player, room, snapshot, localBaseGold);
        if (resolution.FinalGold == localBaseGold)
        {
            return;
        }

        rewardList[goldRewardIndex] = new GoldReward(resolution.FinalGold, player);
        MainFile.Logger.Info(
            $"[MultiplayerCard] Friend Fee rewrote gold reward for player {player.NetId}: " +
            $"base={localBaseGold}, victimLoss={resolution.VictimLoss}, holderGain={resolution.HolderGain}, final={resolution.FinalGold}.");
    }

    public static void Clear()
    {
        Snapshots.Clear();
    }

    private static FriendFeeResolution ResolveForPlayer(Player player, CombatRoom room, FriendFeeSnapshot snapshot, int localBaseGold)
    {
        Dictionary<ulong, int> baseGoldByPlayer = room.CombatState.Players.ToDictionary(
            participant => participant.NetId,
            participant => participant.NetId == player.NetId
                ? localBaseGold
                : SimulateBaseCombatGold(participant, room));

        Dictionary<ulong, int> holderGainByPlayer = room.CombatState.Players.ToDictionary(participant => participant.NetId, _ => 0);
        Dictionary<ulong, int> victimLossByPlayer = room.CombatState.Players.ToDictionary(participant => participant.NetId, _ => 0);

        foreach (KeyValuePair<ulong, int> victimEntry in baseGoldByPlayer.OrderBy(static entry => entry.Key))
        {
            ulong victimId = victimEntry.Key;
            int baseGold = victimEntry.Value;

            List<FeeApplication> applications = snapshot.HolderPowerCounts
                .Where(entry => entry.Key != victimId)
                .OrderBy(static entry => entry.Key)
                .SelectMany(static entry => CreateApplications(entry.Key, entry.Value))
                .ToList();

            if (baseGold <= 0 || applications.Count == 0)
            {
                continue;
            }

            int remainingGold = baseGold;
            foreach (FeeApplication application in applications)
            {
                int reduction = remainingGold / application.Divisor;
                if (reduction <= 0)
                {
                    continue;
                }

                remainingGold -= reduction;
                victimLossByPlayer[victimId] += reduction;
                holderGainByPlayer[application.HolderId] += reduction * application.BonusMultiplier;
            }
        }

        int finalGold = Math.Max(0, localBaseGold - victimLossByPlayer[player.NetId] + holderGainByPlayer[player.NetId]);
        return new FriendFeeResolution(finalGold, victimLossByPlayer[player.NetId], holderGainByPlayer[player.NetId]);
    }

    private static int SimulateBaseCombatGold(Player player, CombatRoom room)
    {
        (int minGold, int maxGold) = GetBaseCombatGoldRange(player, room);
        if (maxGold <= 0 || maxGold < minGold)
        {
            return 0;
        }

        Rng rewardsRng = player.PlayerRng.Rewards;
        Rng clonedRng = new(rewardsRng.Seed, rewardsRng.Counter);
        return clonedRng.NextInt(minGold, maxGold + 1);
    }

    private static (int minGold, int maxGold) GetBaseCombatGoldRange(Player player, CombatRoom room)
    {
        return room.RoomType switch
        {
            RoomType.Monster when room.GoldProportion > 0f => (
                (int)Math.Round(room.Encounter.MinGoldReward * room.GoldProportion),
                (int)Math.Round(room.Encounter.MaxGoldReward * room.GoldProportion)
            ),
            RoomType.Elite => (room.Encounter.MinGoldReward, room.Encounter.MaxGoldReward),
            RoomType.Boss => (room.Encounter.MinGoldReward, room.Encounter.MaxGoldReward),
            _ => (0, 0),
        };
    }

    private static string GetRoomKey(IRunState runState)
    {
        return $"{runState.CurrentActIndex}:{runState.TotalFloor}:{runState.CurrentMapCoord}";
    }

    private sealed class FriendFeeSnapshot
    {
        private readonly HashSet<ulong> _adjustedPlayers = new();

        public FriendFeeSnapshot(Dictionary<ulong, HolderPowerCounts> holderPowerCounts)
        {
            HolderPowerCounts = holderPowerCounts;
        }

        public Dictionary<ulong, HolderPowerCounts> HolderPowerCounts { get; }

        public bool MarkAdjusted(ulong playerId)
        {
            return _adjustedPlayers.Add(playerId);
        }
    }

    private static IEnumerable<FeeApplication> CreateApplications(ulong holderId, HolderPowerCounts counts)
    {
        for (int i = 0; i < counts.BaseCount; i++)
        {
            yield return new FeeApplication(holderId, BaseFriendFeeDivisor, BaseFriendFeeBonusMultiplier, false, i);
        }

        for (int i = 0; i < counts.UpgradedCount; i++)
        {
            yield return new FeeApplication(holderId, UpgradedFriendFeeDivisor, UpgradedFriendFeeBonusMultiplier, true, i);
        }
    }

    private readonly record struct HolderPowerCounts(int BaseCount, int UpgradedCount);
    private readonly record struct FeeApplication(ulong HolderId, int Divisor, int BonusMultiplier, bool IsUpgraded, int Sequence);
    private readonly record struct FriendFeeResolution(int FinalGold, int VictimLoss, int HolderGain);
}

[HarmonyPatch(typeof(NRewardsScreen), nameof(NRewardsScreen.SetRewards))]
public static class FriendFeeRewardsScreenPatch
{
    private static void Prefix(IEnumerable<Reward> rewards)
    {
        if (LocalContext.NetId == null)
        {
            return;
        }

        MultiplayerCardGoldService.RewriteRewardsForFriendFee(rewards);
    }
}
