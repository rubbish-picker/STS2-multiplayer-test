using System.Reflection;
using BaseLib.Extensions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace BalanceTheSpire;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "BalanceTheSpire"; // At the moment, this is used only for the Logger and harmony names.
    private static bool _startupDiagnosticsRan;

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        Harmony harmony = new(ModId);

        harmony.TryPatchAll(Assembly.GetExecutingAssembly());
    }

    public static void RunStartupDiagnosticsOnce()
    {
        if (_startupDiagnosticsRan)
        {
            return;
        }

        _startupDiagnosticsRan = RunStartupDiagnostics();
    }

    private static bool RunStartupDiagnostics()
    {
        try
        {
            LogCardStateSafe<SpoilsOfBattle>("SpoilsOfBattle", card => $"forge={card.DynamicVars.Forge.BaseValue}");
            LogCardStateSafe<MinionDiveBomb>("MinionDiveBomb", card => $"cost={card.EnergyCost.GetWithModifiers(CostModifiers.Local)} damage={card.DynamicVars.Damage.BaseValue}");
            LogCardStateSafe<CollisionCourse>("CollisionCourse", card => $"damage={card.DynamicVars.Damage.BaseValue}");
            LogCardStateSafe<HeirloomHammer>("HeirloomHammer", card => $"damage={card.DynamicVars.Damage.BaseValue}");
            LogCardStateSafe<GatherLight>("GatherLight", card => $"block={card.DynamicVars.Block.BaseValue}");
            LogCardStateSafe<BundleOfJoy>("BundleOfJoy", card => $"cost={card.EnergyCost.GetWithModifiers(CostModifiers.Local)}");
            LogCardStateSafe<IAmInvincible>("IAmInvincible", card => $"block={card.DynamicVars.Block.BaseValue}");
            LogCardStateSafe<KinglyKick>("KinglyKick", card => $"damage={card.DynamicVars.Damage.BaseValue}");
            LogCardStateSafe<KinglyPunch>("KinglyPunch", card => $"damage={card.DynamicVars.Damage.BaseValue} increase={card.DynamicVars["Increase"].BaseValue}");
            LogCardStateSafe<SolarStrike>("SolarStrike", card => $"damage={card.DynamicVars.Damage.BaseValue}");
            LogCardStateSafe<Patter>("Patter", card => $"block={card.DynamicVars.Block.BaseValue}");
            LogCardStateSafe<FallingStar>("FallingStar", card => $"damage={card.DynamicVars.Damage.BaseValue}");
            LogCardStateSafe<WroughtInWar>("WroughtInWar", card => $"forge={card.DynamicVars.Forge.BaseValue}");
            LogCardStateSafe<Parry>("Parry", card => $"parry={card.DynamicVars.Power<ParryPower>().BaseValue}");
            LogCardStateSafe<Glitterstream>("Glitterstream", card => $"block={card.DynamicVars.Block.BaseValue} nextTurnBlock={card.DynamicVars["BlockNextTurn"].BaseValue}");
            LogCardStateSafe<RefineBlade>("RefineBlade", card => $"forge={card.DynamicVars.Forge.BaseValue}");
            LogCardStateSafe<Arsenal>("Arsenal", card => $"innate={card.Keywords.Contains(CardKeyword.Innate)}");
            LogCardStateSafe<CelestialMight>("CelestialMight", card => $"damage={card.DynamicVars.Damage.BaseValue} repeat={card.DynamicVars.Repeat.BaseValue}");
            return true;
        }
        catch (System.Exception ex)
        {
            Logger.Error($"[BalanceStartup] diagnostic failed: {ex}");
            return false;
        }
    }

    private static void LogCardStateSafe<T>(string label, Func<T, string> describe) where T : CardModel
    {
        try
        {
            LogCardState(label, describe);
        }
        catch (System.Exception ex)
        {
            Logger.Error($"[BalanceStartup] {label} diagnostic failed: {ex.Message}");
        }
    }

    private static void LogCardState<T>(string label, Func<T, string> describe) where T : CardModel
    {
        T card = (T)ModelDb.Card<T>().ToMutable();
        Logger.Info($"[BalanceStartup] {label} base {describe(card)}");

        if (!card.IsUpgradable)
        {
            return;
        }

        card.UpgradeInternal();
        card.FinalizeUpgradeInternal();
        Logger.Info($"[BalanceStartup] {label}+ {describe(card)}");
    }
}
