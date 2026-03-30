using System.Collections.Generic;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rewards;

namespace CocoRelics;

public partial class PreviewRewardsScreen : Control
{
    private static readonly AccessTools.FieldRef<NRewardsScreen, IRunState>? RewardsScreenRunStateRef =
        AccessTools.FieldRefAccess<NRewardsScreen, IRunState>("_runState");
    private static readonly AccessTools.FieldRef<NRewardsScreen, bool>? RewardsScreenIsTerminalRef =
        AccessTools.FieldRefAccess<NRewardsScreen, bool>("_isTerminal");

    private RunState _runState = null!;
    private IReadOnlyList<Reward> _rewards = null!;

    public static Control Create(RunState runState, IReadOnlyList<Reward> rewards)
    {
        if (RewardsScreenRunStateRef == null || RewardsScreenIsTerminalRef == null)
        {
            return new Label { Text = "奖励预览不可用" };
        }

        return new PreviewRewardsScreen
        {
            _runState = runState,
            _rewards = rewards,
        };
    }

    public override void _Ready()
    {
        const string rewardsScreenScenePath = "res://scenes/screens/rewards_screen.tscn";
        NRewardsScreen screen = PreloadManager.Cache.GetScene(rewardsScreenScenePath).Instantiate<NRewardsScreen>(PackedScene.GenEditState.Disabled);
        RewardsScreenRunStateRef!(screen) = _runState;
        RewardsScreenIsTerminalRef!(screen) = false;
        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(screen);
        screen.SetRewards(_rewards);
        CallDeferred(nameof(ShowScreenContents), screen);
    }

    private static void ShowScreenContents(NRewardsScreen screen)
    {
        screen.AfterOverlayShown();
    }
}
