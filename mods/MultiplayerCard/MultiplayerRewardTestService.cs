using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace MultiplayerCard;

public static class MultiplayerRewardTestService
{
    public static string RunPreviewForAllPlayers(IRunState runState, RoomType roomType, int count)
    {
        List<string> lines = new()
        {
            $"[MultiplayerCard] Previewing {count} {roomType} reward roll(s) for {runState.Players.Count} player(s)."
        };

        foreach (Player player in runState.Players)
        {
            for (int i = 0; i < count; i++)
            {
                RewardPreview preview = PreviewReward(player, roomType);
                string cardSummary = string.Join(", ", preview.Cards.Select(static card => card.Title));
                lines.Add($"P{player.NetId} roll {i + 1}: [{cardSummary}] mod_cards={preview.ModCardCount} exact_one={preview.ModCardCount == 1}");
            }
        }

        string message = string.Join(Environment.NewLine, lines);
        MainFile.Logger.Info(message);
        return message;
    }

    private static RewardPreview PreviewReward(Player player, RoomType roomType)
    {
        SerializablePlayerRngSet rngSnapshot = player.PlayerRng.ToSerializable();
        SerializablePlayerOddsSet oddsSnapshot = player.PlayerOdds.ToSerializable();

        try
        {
            CardCreationOptions options = CardCreationOptions.ForRoom(player, roomType);
            List<CardModel> cards = CardFactory.CreateForReward(player, 3, options)
                .Select(static result => result.Card)
                .ToList();

            return new RewardPreview(cards, cards.Count(MultiplayerCardConfigService.IsOurCard));
        }
        finally
        {
            player.PlayerRng.LoadFromSerializable(rngSnapshot);
            player.PlayerOdds.LoadFromSerializable(oddsSnapshot);
        }
    }

    private sealed record RewardPreview(IReadOnlyList<CardModel> Cards, int ModCardCount);
}
