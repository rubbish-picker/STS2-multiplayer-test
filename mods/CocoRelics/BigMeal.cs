using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib;
using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace CocoRelics;

[Pool(typeof(SharedRelicPool))]
public sealed class BigMeal : CustomRelicModel
{
    private const string CustomIconPath = "res://CocoRelics/images/relics/pin_coco.png";
    private const string SurviveRoundsKey = "SurviveRounds";

    private bool _isActivating;
    private int _roundsSurvived;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool ShowCounter => true;

    public override int DisplayAmount => _isActivating ? DynamicVars[SurviveRoundsKey].IntValue : RemainingRounds;

    public override string PackedIconPath => CustomIconPath;

    protected override string PackedIconOutlinePath => CustomIconPath;

    protected override string BigIconPath => CustomIconPath;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(3),
        new DynamicVar(SurviveRoundsKey, 7m)
    };

    protected override IEnumerable<IHoverTip> ExtraHoverTips => new[] { HoverTipFactory.ForEnergy(this) };

    [SavedProperty]
    public int RoundsSurvived
    {
        get => _roundsSurvived;
        set
        {
            AssertMutable();
            _roundsSurvived = value;
            InvokeDisplayAmountChanged();
        }
    }

    private int RemainingRounds => System.Math.Max(0, DynamicVars[SurviveRoundsKey].IntValue - RoundsSurvived);

    public override bool IsAllowed(IRunState runState)
    {
        return false;
    }

    public override async Task AfterSideTurnStart(CombatSide side, CombatState combatState)
    {
        if (side != Owner.Creature.Side)
        {
            return;
        }

        if (RoundsSurvived >= DynamicVars[SurviveRoundsKey].IntValue)
        {
            Flash();
            await CreatureCmd.Kill(Owner.Creature, force: true);
            return;
        }

        RoundsSurvived++;
        Status = RemainingRounds <= 1 ? RelicStatus.Active : RelicStatus.Normal;
        _ = TaskHelper.RunSafely(DoActivateVisuals());
        await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue, Owner);
    }

    public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
    {
        if (wasRemovalPrevented || creature != Owner.Creature || Owner.RunState == null)
        {
            return;
        }

        List<Player> aliveTeammates = Owner.RunState.Players
            .Where(player => player != Owner && player.Creature.IsAlive)
            .ToList();
        if (aliveTeammates.Count == 0)
        {
            return;
        }

        Player inheritor = Owner.RunState.Rng.Niche.NextItem(aliveTeammates)!;
        await CocoRelicsMealService.TransferBigMealAsync(this, inheritor);
    }

    public override Task AfterCombatEnd(CombatRoom _)
    {
        RoundsSurvived = 0;
        Status = RelicStatus.Normal;
        return Task.CompletedTask;
    }

    private async Task DoActivateVisuals()
    {
        _isActivating = true;
        InvokeDisplayAmountChanged();
        Flash();
        await Cmd.Wait(1f);
        _isActivating = false;
        InvokeDisplayAmountChanged();
    }
}
