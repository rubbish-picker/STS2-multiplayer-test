using System;
using System.Linq;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;

namespace MultiplayerCard;

public sealed class MultiplayerRewardTestConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "mp_reward_test";

    public override string Args => "[monster|elite|boss] [count:int]";

    public override string Description => "Preview encounter card rewards for every player and log whether exactly one MultiplayerCard card appears.";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (issuingPlayer?.RunState == null)
        {
            return new CmdResult(success: false, "Run this command while a run is in progress.");
        }

        RoomType roomType = RoomType.Monster;
        if (args.Length >= 1 && !TryParseRoomType(args[0], out roomType))
        {
            return new CmdResult(success: false, $"Unknown room type '{args[0]}'. Use monster, elite, or boss.");
        }

        int count = 1;
        if (args.Length >= 2 && (!int.TryParse(args[1], out count) || count <= 0))
        {
            return new CmdResult(success: false, $"Invalid count '{args[1]}'. Use a positive integer.");
        }

        if (count > 20)
        {
            return new CmdResult(success: false, "Count too large. Please use 20 or less.");
        }

        string message = MultiplayerRewardTestService.RunPreviewForAllPlayers(issuingPlayer.RunState, roomType, count);
        return new CmdResult(success: true, message);
    }

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        if (args.Length <= 1)
        {
            return CompleteArgument(new[] { "monster", "elite", "boss" }, Array.Empty<string>(), args.FirstOrDefault() ?? "");
        }

        return base.GetArgumentCompletions(player, args);
    }

    private static bool TryParseRoomType(string raw, out RoomType roomType)
    {
        roomType = raw.Trim().ToLowerInvariant() switch
        {
            "monster" => RoomType.Monster,
            "elite" => RoomType.Elite,
            "boss" => RoomType.Boss,
            _ => RoomType.Unassigned,
        };

        return roomType != RoomType.Unassigned;
    }
}
