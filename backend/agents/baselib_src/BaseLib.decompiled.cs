using System;
using System.Buffers;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Permissions;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generated;
using System.Threading;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using BaseLib.Cards.Variables;
using BaseLib.Config;
using BaseLib.Config.UI;
using BaseLib.Extensions;
using BaseLib.Patches.Content;
using BaseLib.Patches.UI;
using BaseLib.Utils;
using BaseLib.Utils.Patching;
using Godot;
using Godot.Bridge;
using Godot.Collections;
using Godot.NativeInterop;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Localization.Formatters;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Pooling;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.addons.mega_text;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.Default | DebuggableAttribute.DebuggingModes.DisableOptimizations | DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints | DebuggableAttribute.DebuggingModes.EnableEditAndContinue)]
[assembly: TargetFramework(".NETCoreApp,Version=v9.0", FrameworkDisplayName = ".NET 9.0")]
[assembly: AssemblyCompany("Alchyr")]
[assembly: AssemblyConfiguration("Debug")]
[assembly: AssemblyDescription("Mod for Slay the Spire 2 providing utilities and features for other mods.")]
[assembly: AssemblyFileVersion("0.1.3.0")]
[assembly: AssemblyInformationalVersion("0.1.3+650f5068ae236b619dc91d7e98b5a093ae501342")]
[assembly: AssemblyProduct("BaseLib")]
[assembly: AssemblyTitle("BaseLib")]
[assembly: AssemblyMetadata("RepositoryUrl", "https://github.com/Alchyr/BaseLib-StS2")]
[assembly: AssemblyHasScripts(new Type[]
{
	typeof(NConfigButton),
	typeof(NConfigDropdown),
	typeof(NConfigDropdownItem),
	typeof(NConfigSlider),
	typeof(NConfigTickbox),
	typeof(NModConfigPopup)
})]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
[assembly: AssemblyVersion("0.1.3.0")]
[module: UnverifiableCode]
[module: RefSafetyRules(11)]
namespace GodotPlugins.Game
{
	internal static class Main
	{
		[UnmanagedCallersOnly(EntryPoint = "godotsharp_game_main_init")]
		private static godot_bool InitializeFromGameProject(nint godotDllHandle, nint outManagedCallbacks, nint unmanagedCallbacks, int unmanagedCallbacksSize)
		{
			//IL_0063: Unknown result type (might be due to invalid IL or missing references)
			//IL_0068: Unknown result type (might be due to invalid IL or missing references)
			//IL_0003: Unknown result type (might be due to invalid IL or missing references)
			//IL_0013: Expected O, but got Unknown
			//IL_0051: Unknown result type (might be due to invalid IL or missing references)
			//IL_006b: Unknown result type (might be due to invalid IL or missing references)
			try
			{
				DllImportResolver resolver = new GodotDllImportResolver((IntPtr)godotDllHandle).OnResolveDllImport;
				Assembly assembly = typeof(GodotObject).Assembly;
				NativeLibrary.SetDllImportResolver(assembly, resolver);
				NativeFuncs.Initialize((IntPtr)unmanagedCallbacks, unmanagedCallbacksSize);
				ManagedCallbacks.Create((IntPtr)outManagedCallbacks);
				ScriptManagerBridge.LookupScriptsInAssembly(typeof(Main).Assembly);
				return (godot_bool)1;
			}
			catch (Exception value)
			{
				Console.Error.WriteLine(value);
				return GodotBoolExtensions.ToGodotBool(false);
			}
		}
	}
}
namespace BaseLib
{
	[ModInitializer("Initialize")]
	public static class MainFile
	{
		public const string ModId = "BaseLib";

		private static nint _holder;

		public static Logger Logger { get; } = new Logger("BaseLib", (LogType)0);

		public static void Initialize()
		{
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0022: Expected O, but got Unknown
			Libgcc();
			ModConfigRegistry.Register("BaseLib", new BaseLibConfig());
			Harmony val = new Harmony("BaseLib");
			GetCustomLocKey.Patch(val);
			TheBigPatchToCardPileCmdAdd.Patch(val);
			val.PatchAll();
		}

		[DllImport("libdl.so.2")]
		private static extern nint dlopen(string filename, int flags);

		[DllImport("libdl.so.2")]
		private static extern nint dlerror();

		[DllImport("libdl.so.2")]
		private static extern nint dlsym(nint handle, string symbol);

		private static void Libgcc()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				Logger.Info("Running on Linux, manually dlopen libgcc for Harmony", 1);
				_holder = dlopen("libgcc_s.so.1", 258);
				if (_holder == IntPtr.Zero)
				{
					Logger.Info("Or Nor: " + Marshal.PtrToStringAnsi(dlerror()), 1);
				}
			}
		}
	}
}
namespace BaseLib.Utils
{
	public static class AncientDialogueUtil
	{
		private const string ArchitectKey = "THE_ARCHITECT";

		private const string AttackKey = "-attack";

		private const string VisitIndexKey = "-visit";

		public static string SfxPath(string dialogueLoc)
		{
			LocString ifExists = LocString.GetIfExists("ancients", dialogueLoc + ".sfx");
			return ((ifExists != null) ? ifExists.GetRawText() : null) ?? "";
		}

		public static string BaseLocKey(string ancientId, string charId)
		{
			return ancientId + ".talk." + charId + ".";
		}

		public static List<AncientDialogue> GetDialoguesForKey(string locTable, string baseKey, StringBuilder? log = null)
		{
			//IL_01d7: Unknown result type (might be due to invalid IL or missing references)
			//IL_0244: Unknown result type (might be due to invalid IL or missing references)
			//IL_0249: Unknown result type (might be due to invalid IL or missing references)
			//IL_0256: Unknown result type (might be due to invalid IL or missing references)
			//IL_0257: Unknown result type (might be due to invalid IL or missing references)
			//IL_0264: Expected O, but got Unknown
			//IL_01e2: Unknown result type (might be due to invalid IL or missing references)
			//IL_0237: Unknown result type (might be due to invalid IL or missing references)
			//IL_0239: Unknown result type (might be due to invalid IL or missing references)
			if (log != null)
			{
				StringBuilder stringBuilder = log;
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(37, 2, stringBuilder);
				handler.AppendLiteral("Looking for dialogues for '");
				handler.AppendFormatted(baseKey);
				handler.AppendLiteral("' in ");
				handler.AppendFormatted(locTable);
				handler.AppendLiteral(".json");
				stringBuilder2.AppendLine(ref handler);
			}
			List<AncientDialogue> list = new List<AncientDialogue>();
			bool flag = baseKey.StartsWith("THE_ARCHITECT");
			int i = 0;
			int num = 0;
			for (; DialogueExists(locTable, baseKey, i); i++)
			{
				if (log != null)
				{
					StringBuilder stringBuilder = log;
					StringBuilder stringBuilder3 = stringBuilder;
					StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder);
					handler.AppendLiteral("Found dialogue '");
					handler.AppendFormatted(i);
					handler.AppendLiteral("'");
					stringBuilder3.Append(ref handler);
				}
				if (flag)
				{
					num = i;
				}
				else
				{
					if (1 == 0)
					{
					}
					int num2 = i switch
					{
						0 => 0, 
						1 => 1, 
						2 => 4, 
						_ => num + 3, 
					};
					if (1 == 0)
					{
					}
					num = num2;
				}
				LocString ifExists = LocString.GetIfExists(locTable, $"{baseKey}{i}{"-visit"}");
				if (ifExists != null)
				{
					num = int.Parse(ifExists.GetRawText());
				}
				List<string> list2 = new List<string>();
				for (string text = ExistingLine(locTable, baseKey, i, list2.Count); text != null; text = ExistingLine(locTable, baseKey, i, list2.Count))
				{
					list2.Add(SfxPath(text));
				}
				if (log != null)
				{
					StringBuilder stringBuilder = log;
					StringBuilder stringBuilder4 = stringBuilder;
					StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder);
					handler.AppendLiteral(" with ");
					handler.AppendFormatted(list2.Count);
					handler.AppendLiteral(" lines");
					stringBuilder4.AppendLine(ref handler);
				}
				ArchitectAttackers endAttackers = (ArchitectAttackers)0;
				if (flag)
				{
					endAttackers = (ArchitectAttackers)2;
					LocString ifExists2 = LocString.GetIfExists(locTable, $"{baseKey}{i}{"-attack"}");
					if (Enum.TryParse<ArchitectAttackers>((ifExists2 != null) ? ifExists2.GetRawText() : null, ignoreCase: true, out ArchitectAttackers result))
					{
						endAttackers = result;
					}
				}
				AncientDialogue val = new AncientDialogue(list2.ToArray());
				val.set_VisitIndex((int?)num);
				val.set_EndAttackers(endAttackers);
				list.Add(val);
			}
			return list;
		}

		private static bool DialogueExists(string locTable, string baseKey, int index)
		{
			return LocString.Exists(locTable, $"{baseKey}{index}-0.ancient") || LocString.Exists(locTable, $"{baseKey}{index}-0r.ancient") || LocString.Exists(locTable, $"{baseKey}{index}-0.char") || LocString.Exists(locTable, $"{baseKey}{index}-0r.char");
		}

		private static string? ExistingLine(string locTable, string baseKey, int dialogueIndex, int lineIndex)
		{
			string text = $"{baseKey}{dialogueIndex}-{lineIndex}r.ancient";
			if (LocString.Exists(locTable, text))
			{
				return text;
			}
			text = $"{baseKey}{dialogueIndex}-{lineIndex}r.char";
			if (LocString.Exists(locTable, text))
			{
				return text;
			}
			text = $"{baseKey}{dialogueIndex}-{lineIndex}.ancient";
			if (LocString.Exists(locTable, text))
			{
				return text;
			}
			text = $"{baseKey}{dialogueIndex}-{lineIndex}.char";
			if (LocString.Exists(locTable, text))
			{
				return text;
			}
			return null;
		}
	}
	public abstract class AncientOption(int weight) : IWeighted
	{
		private class BasicAncientOption : AncientOption
		{
			public override IEnumerable<RelicModel> AllVariants { get; }

			public override RelicModel ModelForOption => <model>P.ToMutable();

			public BasicAncientOption(RelicModel model, int weight)
			{
				<model>P = model;
				AllVariants = new <>z__ReadOnlySingleElementList<RelicModel>(<model>P.ToMutable());
				base..ctor(weight);
			}
		}

		public int Weight { get; } = weight;

		public abstract IEnumerable<RelicModel> AllVariants { get; }

		public abstract RelicModel ModelForOption { get; }

		public static explicit operator AncientOption(RelicModel model)
		{
			return new BasicAncientOption(model, 1);
		}
	}
	public class AncientOption<T> : AncientOption where T : RelicModel
	{
		private readonly T _model = ModelDb.Relic<T>();

		public Func<T, RelicModel>? ModelPrep { get; init; }

		public Func<T, IEnumerable<RelicModel>>? Variants { get; init; }

		public override IEnumerable<RelicModel> AllVariants
		{
			get
			{
				IEnumerable<RelicModel> result;
				if (Variants != null)
				{
					result = Variants(_model);
				}
				else
				{
					IEnumerable<RelicModel> enumerable = new <>z__ReadOnlySingleElementList<RelicModel>(((RelicModel)_model).ToMutable());
					result = enumerable;
				}
				return result;
			}
		}

		public override RelicModel ModelForOption => (ModelPrep == null) ? ((RelicModel)_model).ToMutable() : ModelPrep(((T)(object)/*isinst with value type is only supported in some contexts*/) ?? _model);

		public AncientOption(int weight)
			: base(weight)
		{
		}
	}
	public static class CommonActions
	{
		public static AttackCommand CardAttack(CardModel card, CardPlay play, int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
		{
			return CardAttack(card, play.Target, hitCount, vfx, sfx, tmpSfx);
		}

		public static AttackCommand CardAttack(CardModel card, Creature? target, int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
		{
			decimal baseValue = ((DynamicVar)card.DynamicVars.Damage).BaseValue;
			return CardAttack(card, target, baseValue, hitCount, vfx, sfx, tmpSfx);
		}

		public static AttackCommand CardAttack(CardModel card, Creature? target, decimal damage, int hitCount = 1, string? vfx = null, string? sfx = null, string? tmpSfx = null)
		{
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0021: Unknown result type (might be due to invalid IL or missing references)
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			//IL_0038: Expected I4, but got Unknown
			//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
			AttackCommand val = DamageCmd.Attack(damage).WithHitCount(hitCount).FromCard(card);
			CombatState combatState = card.CombatState;
			TargetType targetType = card.TargetType;
			TargetType val2 = targetType;
			switch (val2 - 2)
			{
			case 0:
				if (target == null)
				{
					return val;
				}
				val.Targeting(target);
				break;
			case 1:
				if (combatState == null)
				{
					return val;
				}
				val.TargetingAllOpponents(combatState);
				break;
			case 2:
				if (combatState == null)
				{
					return val;
				}
				val.TargetingRandomOpponents(combatState, true);
				break;
			default:
				throw new Exception($"Unsupported AttackCommand target type {card.TargetType} for card {card.Title}");
			}
			if (vfx != null || sfx != null || tmpSfx != null)
			{
				val.WithHitFx(vfx, sfx, tmpSfx);
			}
			return val;
		}

		public static async Task<decimal> CardBlock(CardModel card, CardPlay play)
		{
			return await CardBlock(card, card.DynamicVars.Block, play);
		}

		public static async Task<decimal> CardBlock(CardModel card, BlockVar blockVar, CardPlay play)
		{
			return await CreatureCmd.GainBlock(card.Owner.Creature, blockVar, play, false);
		}

		public static async Task<IEnumerable<CardModel>> Draw(CardModel card, PlayerChoiceContext context)
		{
			return await CardPileCmd.Draw(context, ((DynamicVar)card.DynamicVars.Cards).BaseValue, card.Owner, false);
		}

		public static async Task<T?> Apply<T>(Creature target, CardModel? card, decimal amount, bool silent = false) where T : PowerModel
		{
			return await PowerCmd.Apply<T>(target, amount, (card != null) ? card.Owner.Creature : null, card, silent);
		}

		public static async Task<T?> ApplySelf<T>(CardModel card, decimal amount, bool silent = false) where T : PowerModel
		{
			return await PowerCmd.Apply<T>(card.Owner.Creature, amount, card.Owner.Creature, card, silent);
		}

		public static async Task<IEnumerable<CardModel>> SelectCards(CardModel card, LocString selectionPrompt, PlayerChoiceContext context, PileType pileType, int count = 1)
		{
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			CardSelectorPrefs prefs = new CardSelectorPrefs(selectionPrompt, count);
			CardPile pile = PileTypeExtensions.GetPile(pileType, card.Owner);
			return await CardSelectCmd.FromSimpleGrid(context, pile.Cards, card.Owner, prefs);
		}

		public static async Task<IEnumerable<CardModel>> SelectCards(CardModel card, LocString selectionPrompt, PlayerChoiceContext context, PileType pileType, int minCount, int maxCount)
		{
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			CardSelectorPrefs prefs = new CardSelectorPrefs(selectionPrompt, minCount, maxCount);
			CardPile pile = PileTypeExtensions.GetPile(pileType, card.Owner);
			return await CardSelectCmd.FromSimpleGrid(context, pile.Cards, card.Owner, prefs);
		}

		public static async Task<CardModel?> SelectSingleCard(CardModel card, LocString selectionPrompt, PlayerChoiceContext context, PileType pileType)
		{
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			CardSelectorPrefs prefs = new CardSelectorPrefs(selectionPrompt, 1);
			CardPile pile = PileTypeExtensions.GetPile(pileType, card.Owner);
			return (await CardSelectCmd.FromSimpleGrid(context, pile.Cards, card.Owner, prefs)).FirstOrDefault();
		}
	}
	public class GeneratedNodePool
	{
		private static Dictionary<Type, INodePool>? _pools;

		internal static readonly Variant NameStr = Variant.CreateFrom("name");

		internal static readonly Variant CallableStr = Variant.CreateFrom("callable");

		internal static readonly Variant SignalStr = Variant.CreateFrom("signal");

		public static GeneratedNodePool<T> Init<T>(Func<T> constructor, int prewarmCount) where T : Node, IPoolable
		{
			Type typeFromHandle = typeof(T);
			if (_pools == null)
			{
				_pools = (Dictionary<Type, INodePool>)AccessTools.DeclaredField(typeof(NodePool), "_pools").GetValue(null);
			}
			if (_pools == null)
			{
				throw new Exception("Failed to access _pools from NodePool");
			}
			if (_pools.TryGetValue(typeFromHandle, out INodePool _))
			{
				throw new InvalidOperationException($"Tried to init GeneratedNodePool for type {typeof(T)} but it's already initialized!");
			}
			GeneratedNodePool<T> generatedNodePool = new GeneratedNodePool<T>(constructor, prewarmCount);
			_pools[typeFromHandle] = (INodePool)(object)generatedNodePool;
			return generatedNodePool;
		}
	}
	public class GeneratedNodePool<T> : INodePool where T : Node, IPoolable
	{
		private readonly Func<T> _constructor;

		private readonly List<T> _freeObjects = new List<T>();

		private readonly HashSet<T> _usedObjects = new HashSet<T>();

		public IReadOnlyList<T> DebugFreeObjects => _freeObjects;

		public GeneratedNodePool(Func<T> constructor, int prewarmCount = 0)
		{
			_constructor = constructor;
			for (int i = 0; i < prewarmCount; i++)
			{
				_freeObjects.Add(Instantiate());
			}
		}

		IPoolable INodePool.Get()
		{
			return (IPoolable)(object)Get();
		}

		void INodePool.Free(IPoolable poolable)
		{
			Free((T)(object)poolable);
		}

		public T Get()
		{
			T val;
			if (_freeObjects.Count > 0)
			{
				List<T> freeObjects = _freeObjects;
				val = freeObjects[freeObjects.Count - 1];
				_freeObjects.RemoveAt(_freeObjects.Count - 1);
			}
			else
			{
				val = Instantiate();
			}
			_usedObjects.Add(val);
			((IPoolable)val).OnReturnedFromPool();
			return val;
		}

		public void Free(T obj)
		{
			if (!_usedObjects.Contains(obj))
			{
				if (_freeObjects.Contains(obj))
				{
					Log.Error($"Tried to free object {obj} ({((object)obj).GetType()}) back to pool {typeof(GeneratedNodePool<T>)} but it's already been freed!", 2);
				}
				else
				{
					Log.Error($"Tried to free object {obj} ({((object)obj).GetType()}) back to pool {typeof(GeneratedNodePool<T>)} but it's not part of the pool!", 2);
				}
				GodotTreeExtensions.QueueFreeSafelyNoPool((Node)(object)obj);
			}
			else
			{
				DisconnectIncomingAndOutgoingSignals((Node)(object)obj);
				_usedObjects.Remove(obj);
				_freeObjects.Add(obj);
				((IPoolable)obj).OnFreedToPool();
			}
		}

		private T Instantiate()
		{
			T val = _constructor();
			((IPoolable)val).OnInstantiated();
			return val;
		}

		private void DisconnectIncomingAndOutgoingSignals(Node obj)
		{
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0021: Unknown result type (might be due to invalid IL or missing references)
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0051: Unknown result type (might be due to invalid IL or missing references)
			//IL_0056: Unknown result type (might be due to invalid IL or missing references)
			//IL_0059: Unknown result type (might be due to invalid IL or missing references)
			//IL_005e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0062: Unknown result type (might be due to invalid IL or missing references)
			//IL_0067: Unknown result type (might be due to invalid IL or missing references)
			//IL_006c: Unknown result type (might be due to invalid IL or missing references)
			//IL_006f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0074: Unknown result type (might be due to invalid IL or missing references)
			//IL_0077: Unknown result type (might be due to invalid IL or missing references)
			//IL_0079: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
			Variant val;
			foreach (Dictionary signal3 in ((GodotObject)obj).GetSignalList())
			{
				val = signal3[GeneratedNodePool.NameStr];
				StringName val2 = ((Variant)(ref val)).AsStringName();
				foreach (Dictionary signalConnection in ((GodotObject)obj).GetSignalConnectionList(val2))
				{
					val = signalConnection[GeneratedNodePool.CallableStr];
					Callable callable = ((Variant)(ref val)).AsCallable();
					val = signalConnection[GeneratedNodePool.SignalStr];
					Signal signal = ((Variant)(ref val)).AsSignal();
					DisconnectSignal(callable, signal);
				}
			}
			foreach (Dictionary incomingConnection in ((GodotObject)obj).GetIncomingConnections())
			{
				val = incomingConnection[GeneratedNodePool.CallableStr];
				Callable callable2 = ((Variant)(ref val)).AsCallable();
				val = incomingConnection[GeneratedNodePool.SignalStr];
				Signal signal2 = ((Variant)(ref val)).AsSignal();
				DisconnectSignal(callable2, signal2);
			}
			for (int i = 0; i < obj.GetChildCount(false); i++)
			{
				DisconnectIncomingAndOutgoingSignals(obj.GetChild(i, false));
			}
		}

		private void DisconnectSignal(Callable callable, Signal signal)
		{
			//IL_006c: Unknown result type (might be due to invalid IL or missing references)
			//IL_007e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0099: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
			GodotObject target = ((Callable)(ref callable)).Target;
			if (target == null && ((Callable)(ref callable)).Method == (StringName)null)
			{
				return;
			}
			StringName name = ((Signal)(ref signal)).Name;
			Node val = (Node)(object)((target is Node) ? target : null);
			if (val == null || val.IsInsideTree())
			{
				GodotObject owner = ((Signal)(ref signal)).Owner;
				Node val2 = (Node)(object)((owner is Node) ? owner : null);
				if (val != null && ((GodotObject)val).HasSignal(name) && ((GodotObject)val).IsConnected(name, callable))
				{
					((GodotObject)val).Disconnect(name, callable);
				}
				else if (val2 != null && ((GodotObject)val2).HasSignal(name) && ((GodotObject)val2).IsConnected(name, callable))
				{
					((GodotObject)val2).Disconnect(name, callable);
				}
			}
		}
	}
	public static class GodotUtils
	{
		public static NCreatureVisuals CreatureVisualsFromScene(string path)
		{
			//IL_004b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0051: Expected O, but got Unknown
			Node val = PreloadManager.Cache.GetScene(path).Instantiate((GenEditState)0);
			NCreatureVisuals val2 = (NCreatureVisuals)(object)((val is NCreatureVisuals) ? val : null);
			if (val2 != null)
			{
				MainFile.Logger.Info("Visuals are NCreatureVisuals, returning directly", 1);
				return val2;
			}
			MainFile.Logger.Info("Visuals are not NCreatureVisuals, attempting conversion", 1);
			NCreatureVisuals val3 = new NCreatureVisuals();
			TransferNodes((Node)(object)val3, val, "Visuals", "Bounds", "IntentPos", "CenterPos", "OrbPos", "TalkPos");
			return val3;
		}

		public static T TransferAllNodes<T>(this T obj, string sourceScene, params string[] uniqueNames) where T : Node
		{
			TransferNodes((Node)(object)obj, PreloadManager.Cache.GetScene(sourceScene).Instantiate((GenEditState)0), uniqueNames);
			return obj;
		}

		private static void TransferNodes(Node target, Node source, params string[] names)
		{
			TransferNodes(target, source, uniqueNames: true, names);
		}

		private static void TransferNodes(Node target, Node source, bool uniqueNames, params string[] names)
		{
			target.Name = source.Name;
			List<string> list = names.ToList();
			foreach (Node child in source.GetChildren(false))
			{
				source.RemoveChild(child);
				if (list.Remove(StringName.op_Implicit(child.Name)) && uniqueNames)
				{
					child.UniqueNameInOwner = true;
				}
				target.AddChild(child, false, (InternalMode)0);
				child.Owner = target;
				SetChildrenOwner(target, child);
			}
			if (list.Count > 0)
			{
				MainFile.Logger.Warn("Created " + ((object)target).GetType().FullName + " missing required children " + string.Join(" ", list), 1);
			}
			source.QueueFree();
		}

		private static void SetChildrenOwner(Node target, Node child)
		{
			foreach (Node child2 in child.GetChildren(false))
			{
				child2.Owner = target;
				SetChildrenOwner(target, child2);
			}
		}
	}
	public class OptionPools
	{
		private WeightedList<AncientOption>[] _pools;

		public IEnumerable<AncientOption> AllOptions => _pools.SelectMany((WeightedList<AncientOption> pool) => pool);

		public OptionPools(WeightedList<AncientOption> pool1, WeightedList<AncientOption> pool2, WeightedList<AncientOption> pool3)
		{
			_pools = new WeightedList<AncientOption>[3] { pool1, pool2, pool3 };
		}

		public OptionPools(WeightedList<AncientOption> pool12, WeightedList<AncientOption> pool3)
		{
			_pools = new WeightedList<AncientOption>[3] { pool12, pool12, pool3 };
		}

		public OptionPools(WeightedList<AncientOption> pool)
		{
			_pools = new WeightedList<AncientOption>[3] { pool, pool, pool };
		}

		public List<AncientOption> Roll(Rng rng)
		{
			List<AncientOption> list = new List<AncientOption>();
			WeightedList<AncientOption> weightedList = _pools[0];
			WeightedList<AncientOption> weightedList2 = new WeightedList<AncientOption>();
			foreach (AncientOption item in weightedList)
			{
				weightedList2.Add(item);
			}
			WeightedList<AncientOption> weightedList3 = weightedList2;
			list.Add(weightedList3.GetRandom(rng, remove: true));
			if (weightedList != _pools[1])
			{
				weightedList = _pools[1];
				WeightedList<AncientOption> weightedList4 = new WeightedList<AncientOption>();
				foreach (AncientOption item2 in weightedList)
				{
					weightedList4.Add(item2);
				}
				weightedList3 = weightedList4;
			}
			list.Add(weightedList3.GetRandom(rng, remove: true));
			if (weightedList != _pools[2])
			{
				weightedList = _pools[2];
				WeightedList<AncientOption> weightedList5 = new WeightedList<AncientOption>();
				foreach (AncientOption item3 in weightedList)
				{
					weightedList5.Add(item3);
				}
				weightedList3 = weightedList5;
			}
			list.Add(weightedList3.GetRandom(rng, remove: true));
			return list;
		}
	}
	[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
	public sealed class PoolAttribute(Type poolType) : Attribute()
	{
		public Type PoolType { get; } = poolType;
	}
	public class ShaderUtils
	{
		public static ShaderMaterial GenerateHsv(float h, float s, float v)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_0021: Expected O, but got Unknown
			//IL_0023: Expected O, but got Unknown
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0046: Unknown result type (might be due to invalid IL or missing references)
			//IL_005d: Unknown result type (might be due to invalid IL or missing references)
			ShaderMaterial val = new ShaderMaterial
			{
				Shader = (Shader)((Resource)GD.Load<Shader>("res://shaders/hsv.gdshader")).Duplicate(false)
			};
			val.SetShaderParameter(StringName.op_Implicit("h"), Variant.op_Implicit(h));
			val.SetShaderParameter(StringName.op_Implicit("s"), Variant.op_Implicit(s));
			val.SetShaderParameter(StringName.op_Implicit("v"), Variant.op_Implicit(v));
			return val;
		}
	}
	public class SpireField<TKey, TVal> where TKey : class
	{
		private readonly ConditionalWeakTable<TKey, object?> _table = new ConditionalWeakTable<TKey, object>();

		private readonly Func<TKey, TVal?> _defaultVal;

		public TVal? this[TKey obj]
		{
			get
			{
				return Get(obj);
			}
			set
			{
				Set(obj, value);
			}
		}

		public SpireField(Func<TVal?> defaultVal)
		{
			_defaultVal = (TKey _) => defaultVal();
		}

		public SpireField(Func<TKey, TVal?> defaultVal)
		{
			_defaultVal = defaultVal;
		}

		public TVal? Get(TKey obj)
		{
			if (_table.TryGetValue(obj, out object value))
			{
				return (TVal)value;
			}
			_table.Add(obj, value = _defaultVal(obj));
			return (TVal)value;
		}

		public void Set(TKey obj, TVal? val)
		{
			_table.AddOrUpdate(obj, val);
		}
	}
	public interface IWeighted
	{
		int Weight { get; }
	}
	public class WeightedList<T> : IList<T>, ICollection<T>, IEnumerable<T>, IEnumerable
	{
		private class WeightedItem
		{
			public int Weight { get; }

			public T Val { get; set; }

			public WeightedItem(T val, int weight)
			{
				Weight = weight;
				Val = val;
			}
		}

		private readonly List<WeightedItem> _items = new List<WeightedItem>();

		private int _totalWeight;

		public int Count => _items.Count;

		public bool IsReadOnly => false;

		public T this[int index]
		{
			get
			{
				return _items[index].Val;
			}
			set
			{
				_items[index].Val = value;
			}
		}

		public T GetRandom(Rng rng)
		{
			return GetRandom(rng, remove: false);
		}

		public T GetRandom(Rng rng, bool remove)
		{
			if (Count == 0)
			{
				throw new IndexOutOfRangeException("Attempted to roll on empty WeightedList");
			}
			int num = rng.NextInt(_totalWeight);
			int num2 = 0;
			WeightedItem weightedItem = null;
			foreach (WeightedItem item in _items)
			{
				if (num2 + item.Weight >= num)
				{
					weightedItem = item;
					break;
				}
				num2 += item.Weight;
			}
			if (weightedItem != null)
			{
				if (remove)
				{
					_items.Remove(weightedItem);
					_totalWeight -= weightedItem.Weight;
				}
				return weightedItem.Val;
			}
			throw new Exception($"Roll {num} failed to get a value in list of total weight {_totalWeight}");
		}

		public IEnumerator<T> GetEnumerator()
		{
			return _items.Select((WeightedItem item) => item.Val).GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public void Add(T item)
		{
			Add(item, (!(item is IWeighted weighted)) ? 1 : weighted.Weight);
		}

		public void Add(T item, int weight)
		{
			_totalWeight += weight;
			_items.Add(new WeightedItem(item, weight));
		}

		public void Clear()
		{
			_items.Clear();
			_totalWeight = 0;
		}

		public bool Contains(T val)
		{
			return _items.Any((WeightedItem item) => object.Equals(item.Val, val));
		}

		public void CopyTo(T[] array, int arrayIndex)
		{
			_items.Select((WeightedItem item) => item.Val).ToList().CopyTo(array, arrayIndex);
		}

		public bool Remove(T val)
		{
			WeightedItem weightedItem = _items.Find((WeightedItem item) => object.Equals(item.Val, val));
			if (weightedItem != null)
			{
				_items.Remove(weightedItem);
				_totalWeight -= weightedItem.Weight;
				return true;
			}
			return false;
		}

		public int IndexOf(T val)
		{
			return ListExtensions.FirstIndex<WeightedItem>((IReadOnlyList<WeightedItem>)_items, (Predicate<WeightedItem>)((WeightedItem item) => object.Equals(item.Val, val)));
		}

		public void Insert(int index, T item)
		{
			Insert(index, item, 1);
		}

		public void Insert(int index, T item, int weight)
		{
			_items.Insert(index, new WeightedItem(item, weight));
			_totalWeight += weight;
		}

		public void RemoveAt(int index)
		{
			WeightedItem weightedItem = _items[index];
			_items.RemoveAt(index);
			_totalWeight -= weightedItem.Weight;
		}
	}
}
namespace BaseLib.Utils.Patching
{
	public interface IMatcher
	{
		bool Match(List<string> log, List<CodeInstruction> code, int startIndex, out int matchStart, out int matchEnd);
	}
	public class InstructionMatcher : IMatcher
	{
		private readonly List<CodeInstruction> _target = new List<CodeInstruction>();

		public bool Match(List<string> log, List<CodeInstruction> code, int startIndex, out int matchStart, out int matchEnd)
		{
			log.Add("Starting InstructionMatcher");
			matchStart = startIndex;
			matchEnd = matchStart;
			int num = 0;
			for (int i = startIndex; i < code.Count; i++)
			{
				if (code[i].opcode == _target[num].opcode)
				{
					if (_target[num].operand == null || object.Equals(ComparisonOperand(code[i]), _target[num].operand))
					{
						log.Add($"Instruction match {code[i]}");
						num++;
						if (num >= _target.Count)
						{
							matchEnd = i + 1;
							matchStart = matchEnd - _target.Count;
							return true;
						}
						continue;
					}
					log.Add($"Opcode match but operand mismatch {code[i].opcode} | [{code[i].operand?.GetType() ?? null}]{code[i].operand} vs {_target[num].operand}");
				}
				if (num > 0)
				{
					log.Add($"Match ended, opcodes do not match ({code[i].opcode}, {_target[num].opcode})");
					num = 0;
				}
			}
			return false;
		}

		private object ComparisonOperand(CodeInstruction codeInstruction)
		{
			short value = codeInstruction.opcode.Value;
			short num = value;
			if ((uint)(num - 17) <= 2u)
			{
				return ((LocalBuilder)codeInstruction.operand).LocalIndex;
			}
			return codeInstruction.operand;
		}

		public override string ToString()
		{
			return "InstructionMatcher:\n" + _target.AsReadable("\n");
		}

		public InstructionMatcher opcode(OpCode opCode)
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_0013: Expected O, but got Unknown
			_target.Add(new CodeInstruction(opCode, (object)null));
			return this;
		}

		public InstructionMatcher nop()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Nop, (object)null));
			return this;
		}

		public InstructionMatcher Break()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Break, (object)null));
			return this;
		}

		public InstructionMatcher ldarg_0()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldarg_0, (object)null));
			return this;
		}

		public InstructionMatcher ldarg_1()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldarg_1, (object)null));
			return this;
		}

		public InstructionMatcher ldarg_2()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldarg_2, (object)null));
			return this;
		}

		public InstructionMatcher ldarg_3()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldarg_3, (object)null));
			return this;
		}

		public InstructionMatcher ldloc_0()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldloc_0, (object)null));
			return this;
		}

		public InstructionMatcher ldloc_1()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldloc_1, (object)null));
			return this;
		}

		public InstructionMatcher ldloc_2()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldloc_2, (object)null));
			return this;
		}

		public InstructionMatcher ldloc_3()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldloc_3, (object)null));
			return this;
		}

		public InstructionMatcher stloc_0()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Stloc_0, (object)null));
			return this;
		}

		public InstructionMatcher stloc_1()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Stloc_1, (object)null));
			return this;
		}

		public InstructionMatcher stloc_2()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Stloc_2, (object)null));
			return this;
		}

		public InstructionMatcher stloc_3()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Stloc_3, (object)null));
			return this;
		}

		public InstructionMatcher ldloc_s(int index)
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldloc_S, (object)index));
			return this;
		}

		public InstructionMatcher ldloca_s(int index)
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldloca_S, (object)index));
			return this;
		}

		public InstructionMatcher stloc_s(int index)
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Stloc_S, (object)index));
			return this;
		}

		public InstructionMatcher stloc_s()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Stloc_S, (object)null));
			return this;
		}

		public InstructionMatcher ldnull()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldnull, (object)null));
			return this;
		}

		public InstructionMatcher ldc_i4_m1()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldc_I4_M1, (object)null));
			return this;
		}

		public InstructionMatcher ldc_i4_0()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldc_I4_0, (object)null));
			return this;
		}

		public InstructionMatcher ldc_i4_1()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldc_I4_1, (object)null));
			return this;
		}

		public InstructionMatcher ldc_i4_2()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldc_I4_2, (object)null));
			return this;
		}

		public InstructionMatcher ldc_i4_3()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldc_I4_3, (object)null));
			return this;
		}

		public InstructionMatcher ldc_i4_4()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldc_I4_4, (object)null));
			return this;
		}

		public InstructionMatcher ldc_i4_5()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldc_I4_5, (object)null));
			return this;
		}

		public InstructionMatcher ldc_i4_6()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldc_I4_6, (object)null));
			return this;
		}

		public InstructionMatcher ldc_i4_7()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldc_I4_7, (object)null));
			return this;
		}

		public InstructionMatcher ldc_i4_8()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldc_I4_8, (object)null));
			return this;
		}

		public InstructionMatcher dup()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Dup, (object)null));
			return this;
		}

		public InstructionMatcher pop()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Pop, (object)null));
			return this;
		}

		public InstructionMatcher call(Type declaringType, string methodName, Type[]? parameters = null, Type[]? generics = null)
		{
			return call(AccessTools.Method(declaringType, methodName, parameters, generics));
		}

		public InstructionMatcher call(MethodInfo? method)
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Call, (object)method));
			return this;
		}

		public InstructionMatcher ret()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ret, (object)null));
			return this;
		}

		public InstructionMatcher br_s(Label label)
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Br_S, (object)label));
			return this;
		}

		public InstructionMatcher br_s()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Br_S, (object)null));
			return this;
		}

		public InstructionMatcher brfalse_s(Label label)
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Brfalse_S, (object)label));
			return this;
		}

		public InstructionMatcher brfalse_s()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Brfalse_S, (object)null));
			return this;
		}

		public InstructionMatcher brtrue_s(Label label)
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Brtrue_S, (object)label));
			return this;
		}

		public InstructionMatcher brtrue_s()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Brtrue_S, (object)null));
			return this;
		}

		public InstructionMatcher beq_s(Label label)
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Beq_S, (object)label));
			return this;
		}

		public InstructionMatcher beq_s()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Beq_S, (object)null));
			return this;
		}

		public InstructionMatcher ble_un_s(Label label)
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ble_Un_S, (object)label));
			return this;
		}

		public InstructionMatcher ble_un_s()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ble_Un_S, (object)null));
			return this;
		}

		public InstructionMatcher br(Label label)
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Br, (object)label));
			return this;
		}

		public InstructionMatcher br()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Br, (object)null));
			return this;
		}

		public InstructionMatcher switch_()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Switch, (object)null));
			return this;
		}

		public InstructionMatcher add()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Add, (object)null));
			return this;
		}

		public InstructionMatcher sub()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Sub, (object)null));
			return this;
		}

		public InstructionMatcher mul()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Mul, (object)null));
			return this;
		}

		public InstructionMatcher div()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Div, (object)null));
			return this;
		}

		public InstructionMatcher callvirt(Type declaringType, string methodName, Type[]? parameters = null, Type[]? generics = null)
		{
			return callvirt(AccessTools.Method(declaringType, methodName, parameters, generics));
		}

		public InstructionMatcher callvirt(MethodInfo? method)
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Callvirt, (object)method));
			return this;
		}

		public InstructionMatcher ldfld(Type declaringType, string fieldName)
		{
			return ldfld(AccessTools.Field(declaringType, fieldName));
		}

		public InstructionMatcher ldfld(FieldInfo? field)
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Ldfld, (object)field));
			return this;
		}

		public InstructionMatcher stfld(Type declaringType, string fieldName)
		{
			return stfld(AccessTools.Field(declaringType, fieldName));
		}

		public InstructionMatcher stfld(FieldInfo? field)
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Stfld, (object)field));
			return this;
		}

		public InstructionMatcher newarr(Type? type)
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Newarr, (object)type));
			return this;
		}

		public InstructionMatcher stelem_ref()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			_target.Add(new CodeInstruction(OpCodes.Stelem_Ref, (object)null));
			return this;
		}
	}
	public class InstructionPatcher
	{
		private readonly List<CodeInstruction> code = instructions.ToList();

		private int index = -1;

		private int lastMatchStart = -1;

		public readonly List<string> Log = new List<string>();

		public InstructionPatcher(IEnumerable<CodeInstruction> instructions)
		{
		}

		public static implicit operator List<CodeInstruction>(InstructionPatcher locator)
		{
			return locator.code;
		}

		public InstructionPatcher ResetPosition()
		{
			index = -1;
			lastMatchStart = -1;
			return this;
		}

		public InstructionPatcher Match(params IMatcher[] matchers)
		{
			return Match(DefaultMatchFailure, matchers);
		}

		public InstructionPatcher Match(Action<IMatcher[]> onFailMatch, params IMatcher[] matchers)
		{
			index = 0;
			foreach (IMatcher matcher in matchers)
			{
				if (!matcher.Match(Log, code, index, out lastMatchStart, out index))
				{
					onFailMatch(matchers);
					return this;
				}
			}
			Log.Add("Found end of match at " + index + "; last match starts at " + lastMatchStart);
			return this;
		}

		public InstructionPatcher Step(int amt = 1)
		{
			if (index < 0)
			{
				throw new Exception("Attempted to Step without any match found");
			}
			index += amt;
			Log.Add("Stepped to " + index);
			return this;
		}

		public InstructionPatcher GetLabels(out List<Label> labels)
		{
			if (index < 0)
			{
				throw new Exception("Attempted to GetLabels without any match found");
			}
			labels = code[index].labels;
			if (labels.Count == 0)
			{
				if (code[index].operand is Label)
				{
					throw new Exception("Code instruction " + ((object)code[index]).ToString() + " has no labels. Did you mean to use GetOperandLabel instead?");
				}
				throw new Exception("Code instruction " + ((object)code[index]).ToString() + " has no labels");
			}
			return this;
		}

		public InstructionPatcher GetOperandLabel(out Label label)
		{
			if (index < 0)
			{
				throw new Exception("Attempted to GetOperandLabel without any match found");
			}
			if (code[index].operand is Label label2)
			{
				label = label2;
				return this;
			}
			throw new Exception("Code instruction " + ((object)code[index]).ToString() + " does not have a Label parameter");
		}

		public InstructionPatcher GetOperand(out object operand)
		{
			if (index < 0)
			{
				throw new Exception("Attempted to GetOperand without any match found");
			}
			operand = code[index].operand;
			return this;
		}

		public InstructionPatcher ReplaceLastMatch(IEnumerable<CodeInstruction> replacement)
		{
			if (lastMatchStart < 0)
			{
				throw new Exception("Attempted to ReplaceLastMatch without any match found");
			}
			int num = 0;
			foreach (CodeInstruction item in replacement)
			{
				code[lastMatchStart + num] = item;
				num++;
			}
			code.RemoveRange(lastMatchStart + num, index - (lastMatchStart + num));
			index = lastMatchStart + num;
			return this;
		}

		public InstructionPatcher Replace(CodeInstruction replacement, bool keepLabels = true)
		{
			if (index < 0)
			{
				throw new Exception("Attempted to Replace without any match found");
			}
			if (keepLabels)
			{
				CodeInstructionExtensions.MoveLabelsFrom(replacement, code[index]);
			}
			Log.Add($"{code[index]} => {replacement}");
			code[index] = replacement;
			return this;
		}

		public InstructionPatcher IncrementIntPush()
		{
			//IL_0074: Unknown result type (might be due to invalid IL or missing references)
			//IL_007f: Expected O, but got Unknown
			//IL_008c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0097: Expected O, but got Unknown
			//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00af: Expected O, but got Unknown
			//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c7: Expected O, but got Unknown
			//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00df: Expected O, but got Unknown
			//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f7: Expected O, but got Unknown
			//IL_0104: Unknown result type (might be due to invalid IL or missing references)
			//IL_010f: Expected O, but got Unknown
			//IL_011c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0127: Expected O, but got Unknown
			//IL_0131: Unknown result type (might be due to invalid IL or missing references)
			//IL_013c: Expected O, but got Unknown
			if (index < 0)
			{
				throw new Exception("Attempted to Replace without any match found");
			}
			return code[index].opcode.Value switch
			{
				21 => Replace(new CodeInstruction(OpCodes.Ldc_I4_0, (object)null)), 
				22 => Replace(new CodeInstruction(OpCodes.Ldc_I4_1, (object)null)), 
				23 => Replace(new CodeInstruction(OpCodes.Ldc_I4_2, (object)null)), 
				24 => Replace(new CodeInstruction(OpCodes.Ldc_I4_3, (object)null)), 
				25 => Replace(new CodeInstruction(OpCodes.Ldc_I4_4, (object)null)), 
				26 => Replace(new CodeInstruction(OpCodes.Ldc_I4_5, (object)null)), 
				27 => Replace(new CodeInstruction(OpCodes.Ldc_I4_6, (object)null)), 
				28 => Replace(new CodeInstruction(OpCodes.Ldc_I4_7, (object)null)), 
				29 => Replace(new CodeInstruction(OpCodes.Ldc_I4_8, (object)null)), 
				30 => throw new Exception("Instruction " + ((object)code[index])?.ToString() + " cannot be incremented"), 
				_ => throw new Exception("Instruction " + ((object)code[index])?.ToString() + " is not an int push instruction that can be incremented"), 
			};
		}

		public InstructionPatcher IncrementIntPush(out CodeInstruction replacedPush)
		{
			//IL_0074: Unknown result type (might be due to invalid IL or missing references)
			//IL_007a: Expected O, but got Unknown
			//IL_0081: Unknown result type (might be due to invalid IL or missing references)
			//IL_008c: Expected O, but got Unknown
			//IL_0099: Unknown result type (might be due to invalid IL or missing references)
			//IL_009f: Expected O, but got Unknown
			//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b1: Expected O, but got Unknown
			//IL_00be: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c4: Expected O, but got Unknown
			//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d6: Expected O, but got Unknown
			//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e9: Expected O, but got Unknown
			//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fb: Expected O, but got Unknown
			//IL_0108: Unknown result type (might be due to invalid IL or missing references)
			//IL_010e: Expected O, but got Unknown
			//IL_0115: Unknown result type (might be due to invalid IL or missing references)
			//IL_0120: Expected O, but got Unknown
			//IL_012d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0133: Expected O, but got Unknown
			//IL_013a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0145: Expected O, but got Unknown
			//IL_0152: Unknown result type (might be due to invalid IL or missing references)
			//IL_0158: Expected O, but got Unknown
			//IL_015f: Unknown result type (might be due to invalid IL or missing references)
			//IL_016a: Expected O, but got Unknown
			//IL_0177: Unknown result type (might be due to invalid IL or missing references)
			//IL_017d: Expected O, but got Unknown
			//IL_0184: Unknown result type (might be due to invalid IL or missing references)
			//IL_018f: Expected O, but got Unknown
			//IL_0199: Unknown result type (might be due to invalid IL or missing references)
			//IL_019f: Expected O, but got Unknown
			//IL_01a6: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b1: Expected O, but got Unknown
			//IL_01bb: Unknown result type (might be due to invalid IL or missing references)
			//IL_01c1: Expected O, but got Unknown
			//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d9: Expected O, but got Unknown
			if (index < 0)
			{
				throw new Exception("Attempted to Replace without any match found");
			}
			switch (code[index].opcode.Value)
			{
			case 21:
				replacedPush = new CodeInstruction(OpCodes.Ldc_I4_M1, (object)null);
				return Replace(new CodeInstruction(OpCodes.Ldc_I4_0, (object)null));
			case 22:
				replacedPush = new CodeInstruction(OpCodes.Ldc_I4_0, (object)null);
				return Replace(new CodeInstruction(OpCodes.Ldc_I4_1, (object)null));
			case 23:
				replacedPush = new CodeInstruction(OpCodes.Ldc_I4_1, (object)null);
				return Replace(new CodeInstruction(OpCodes.Ldc_I4_2, (object)null));
			case 24:
				replacedPush = new CodeInstruction(OpCodes.Ldc_I4_2, (object)null);
				return Replace(new CodeInstruction(OpCodes.Ldc_I4_3, (object)null));
			case 25:
				replacedPush = new CodeInstruction(OpCodes.Ldc_I4_3, (object)null);
				return Replace(new CodeInstruction(OpCodes.Ldc_I4_4, (object)null));
			case 26:
				replacedPush = new CodeInstruction(OpCodes.Ldc_I4_4, (object)null);
				return Replace(new CodeInstruction(OpCodes.Ldc_I4_5, (object)null));
			case 27:
				replacedPush = new CodeInstruction(OpCodes.Ldc_I4_5, (object)null);
				return Replace(new CodeInstruction(OpCodes.Ldc_I4_6, (object)null));
			case 28:
				replacedPush = new CodeInstruction(OpCodes.Ldc_I4_6, (object)null);
				return Replace(new CodeInstruction(OpCodes.Ldc_I4_7, (object)null));
			case 29:
				replacedPush = new CodeInstruction(OpCodes.Ldc_I4_7, (object)null);
				return Replace(new CodeInstruction(OpCodes.Ldc_I4_8, (object)null));
			case 30:
				replacedPush = new CodeInstruction(OpCodes.Ldc_I4_8, (object)null);
				return Replace(new CodeInstruction(OpCodes.Ldc_I4_S, (object)(sbyte)9));
			default:
				throw new Exception("Instruction " + ((object)code[index])?.ToString() + " is not an int push instruction that can be incremented");
			}
		}

		public InstructionPatcher Insert(CodeInstruction instruction)
		{
			if (index < 0)
			{
				throw new Exception("Attempted to Insert without any match found");
			}
			code.Insert(index, instruction);
			index++;
			return this;
		}

		public InstructionPatcher Insert(IEnumerable<CodeInstruction> insert)
		{
			if (index < 0)
			{
				throw new Exception("Attempted to Insert without any match found");
			}
			code.InsertRange(index, insert);
			index += insert.Count();
			return this;
		}

		public InstructionPatcher InsertCopy(int startOffset, int copyLength)
		{
			if (index < 0)
			{
				throw new Exception("Attempted to InsertCopy without any match found");
			}
			int num = index + startOffset;
			if (num < 0)
			{
				throw new Exception($"startIndex of InsertCopy less than 0 ({num})");
			}
			List<CodeInstruction> list = new List<CodeInstruction>();
			for (int i = 0; i < copyLength; i++)
			{
				Log.Add("Inserting Copy: " + (object)code[num + i]);
				list.Add(code[num + i].Clone());
			}
			return Insert((IEnumerable<CodeInstruction>)list);
		}

		public InstructionPatcher PrintLog(Logger logger)
		{
			logger.Info(Log.AsReadable("\n"), 1);
			return this;
		}

		public InstructionPatcher PrintResult(Logger logger)
		{
			logger.Info("----- RESULT -----\n" + ((List<CodeInstruction>)this).NumberedLines(), 1);
			return this;
		}

		private void DefaultMatchFailure(IMatcher[] matchers)
		{
			throw new Exception("Failed to find match:\n" + matchers.AsReadable("\n---------\n") + "\nLOG:\n" + Log.AsReadable("\n"));
		}
	}
}
namespace BaseLib.Patches.UI
{
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	public class AutoKeywordText
	{
		public static readonly List<CardKeyword> AdditionalBeforeKeywords = new List<CardKeyword>();

		public static readonly List<CardKeyword> AdditionalAfterKeywords = new List<CardKeyword>();

		[HarmonyPostfix]
		private static void Postfix(ref CardKeyword[] ___beforeDescription, ref CardKeyword[] ___afterDescription)
		{
			CardKeyword[] array = ___beforeDescription;
			List<CardKeyword> additionalBeforeKeywords = AdditionalBeforeKeywords;
			int num = 0;
			CardKeyword[] array2 = (CardKeyword[])(object)new CardKeyword[array.Length + additionalBeforeKeywords.Count];
			ReadOnlySpan<CardKeyword> readOnlySpan = new ReadOnlySpan<CardKeyword>(array);
			readOnlySpan.CopyTo(new Span<CardKeyword>(array2).Slice(num, readOnlySpan.Length));
			num += readOnlySpan.Length;
			Span<CardKeyword> span = CollectionsMarshal.AsSpan(additionalBeforeKeywords);
			span.CopyTo(new Span<CardKeyword>(array2).Slice(num, span.Length));
			num += span.Length;
			___beforeDescription = array2;
			array2 = ___afterDescription;
			additionalBeforeKeywords = AdditionalAfterKeywords;
			num = 0;
			array = (CardKeyword[])(object)new CardKeyword[array2.Length + additionalBeforeKeywords.Count];
			readOnlySpan = new ReadOnlySpan<CardKeyword>(array2);
			readOnlySpan.CopyTo(new Span<CardKeyword>(array).Slice(num, readOnlySpan.Length));
			num += readOnlySpan.Length;
			span = CollectionsMarshal.AsSpan(additionalBeforeKeywords);
			span.CopyTo(new Span<CardKeyword>(array).Slice(num, span.Length));
			num += span.Length;
			___afterDescription = array;
		}
	}
	[HarmonyPatch(typeof(NCardLibrary), "_Ready")]
	public class CustomPoolFilters
	{
		private const float baseSize = 64f;

		[HarmonyTranspiler]
		private static List<CodeInstruction> AddFilters(IEnumerable<CodeInstruction> instructions)
		{
			//IL_006e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0074: Expected O, but got Unknown
			//IL_0099: Unknown result type (might be due to invalid IL or missing references)
			//IL_009f: Expected O, but got Unknown
			return new InstructionPatcher(instructions).Match(new InstructionMatcher().ldfld(AccessTools.DeclaredField(typeof(NCardLibrary), "_regentFilter")).callvirt(null)).Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[7]
			{
				CodeInstruction.LoadArgument(0, false),
				CodeInstruction.LoadArgument(0, false),
				new CodeInstruction(OpCodes.Ldfld, (object)AccessTools.DeclaredField(typeof(NCardLibrary), "_poolFilters")),
				CodeInstruction.LoadArgument(0, false),
				new CodeInstruction(OpCodes.Ldfld, (object)AccessTools.DeclaredField(typeof(NCardLibrary), "_cardPoolFilters")),
				CodeInstruction.LoadLocal(0, false),
				CodeInstruction.Call(typeof(CustomPoolFilters), "GenerateCustomFilters", (Type[])null, (Type[])null)
			}));
		}

		public static void GenerateCustomFilters(NCardLibrary library, Dictionary<NCardPoolFilter, Func<CardModel, bool>> filtering, Dictionary<CharacterModel, NCardPoolFilter> characterFilters, Callable updateFilter)
		{
			//IL_013b: Unknown result type (might be due to invalid IL or missing references)
			//IL_013d: Unknown result type (might be due to invalid IL or missing references)
			//IL_015c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0162: Unknown result type (might be due to invalid IL or missing references)
			if (characterFilters.Count == 0)
			{
				throw new Exception("Attempted to generate custom filters at wrong time");
			}
			object? value = AccessTools.DeclaredField(typeof(NCardLibrary), "_miscPoolFilter").GetValue(library);
			NCardPoolFilter val = (NCardPoolFilter)((value is NCardPoolFilter) ? value : null);
			if (val == null)
			{
				throw new Exception("Failed to get _miscPoolFilter");
			}
			Func<CardModel, bool> oldFilter = filtering[val];
			filtering[val] = (CardModel c) => false || oldFilter(c);
			Node parent = ((Node)characterFilters[(CharacterModel)(object)ModelDb.Character<Ironclad>()]).GetParent();
			FieldInfo lastHovered = AccessTools.DeclaredField(typeof(NCardLibrary), "_lastHoveredControl");
			foreach (CustomCharacterModel customCharacter in ModelDbCustomCharacters.CustomCharacters)
			{
				NCardPoolFilter filter = GenerateFilter(customCharacter);
				parent.AddChild((Node)(object)filter, true, (InternalMode)0);
				characterFilters.Add((CharacterModel)(object)customCharacter, filter);
				CardPoolModel pool = ((CharacterModel)customCharacter).CardPool;
				filtering.Add(filter, (CardModel c) => pool.AllCardIds.Contains(((AbstractModel)c).Id));
				((GodotObject)filter).Connect(SignalName.Toggled, updateFilter, 0u);
				((GodotObject)filter).Connect(SignalName.FocusEntered, Callable.From((Action)delegate
				{
					lastHovered.SetValue(library, filter);
				}), 0u);
			}
		}

		private static NCardPoolFilter GenerateFilter(CustomCharacterModel character)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_002e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0039: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
			//IL_004f: Unknown result type (might be due to invalid IL or missing references)
			//IL_005b: Expected O, but got Unknown
			//IL_0062: Unknown result type (might be due to invalid IL or missing references)
			//IL_0067: Unknown result type (might be due to invalid IL or missing references)
			//IL_0078: Unknown result type (might be due to invalid IL or missing references)
			//IL_0080: Unknown result type (might be due to invalid IL or missing references)
			//IL_0089: Unknown result type (might be due to invalid IL or missing references)
			//IL_0092: Unknown result type (might be due to invalid IL or missing references)
			//IL_009d: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00be: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00df: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
			//IL_0106: Expected O, but got Unknown
			//IL_0106: Unknown result type (might be due to invalid IL or missing references)
			//IL_010b: Unknown result type (might be due to invalid IL or missing references)
			//IL_011c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0124: Unknown result type (might be due to invalid IL or missing references)
			//IL_012d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0136: Unknown result type (might be due to invalid IL or missing references)
			//IL_0141: Unknown result type (might be due to invalid IL or missing references)
			//IL_014c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0157: Unknown result type (might be due to invalid IL or missing references)
			//IL_0162: Unknown result type (might be due to invalid IL or missing references)
			//IL_016d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0178: Unknown result type (might be due to invalid IL or missing references)
			//IL_0180: Unknown result type (might be due to invalid IL or missing references)
			//IL_0181: Unknown result type (might be due to invalid IL or missing references)
			//IL_0186: Unknown result type (might be due to invalid IL or missing references)
			//IL_0194: Unknown result type (might be due to invalid IL or missing references)
			//IL_019d: Expected O, but got Unknown
			NCardPoolFilter val = new NCardPoolFilter
			{
				Name = StringName.op_Implicit("FILTER-" + (object)((AbstractModel)character).Id),
				Size = new Vector2(64f, 64f),
				CustomMinimumSize = new Vector2(64f, 64f)
			};
			Texture2D iconTexture = ((CharacterModel)character).IconTexture;
			TextureRect val2 = new TextureRect
			{
				Name = StringName.op_Implicit("Image"),
				Texture = iconTexture,
				ExpandMode = (ExpandModeEnum)1,
				StretchMode = (StretchModeEnum)5,
				Size = new Vector2(56f, 56f),
				Position = new Vector2(4f, 4f),
				Scale = new Vector2(0.9f, 0.9f),
				PivotOffset = new Vector2(28f, 28f),
				Material = (Material)(object)ShaderUtils.GenerateHsv(1f, 1f, 1f)
			};
			TextureRect val3 = new TextureRect
			{
				Name = StringName.op_Implicit("Shadow"),
				Texture = iconTexture,
				ExpandMode = (ExpandModeEnum)1,
				StretchMode = (StretchModeEnum)5,
				Size = new Vector2(56f, 56f),
				Position = new Vector2(4f, 3f),
				PivotOffset = new Vector2(28f, 28f),
				ShowBehindParent = true
			};
			Color black = Colors.Black;
			black.A = 0.25f;
			((CanvasItem)val3).Modulate = black;
			TextureRect val4 = val3;
			((Node)val2).AddChild((Node)(object)val4, false, (InternalMode)0);
			NSelectionReticle val5 = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/selection_reticle")).Instantiate<NSelectionReticle>((GenEditState)0);
			((Node)val5).Name = StringName.op_Implicit("SelectionReticle");
			((Node)val5).UniqueNameInOwner = true;
			((Node)val).AddChild((Node)(object)val2, false, (InternalMode)0);
			((Node)val2).Owner = (Node)(object)val;
			((Node)val).AddChild((Node)(object)val5, false, (InternalMode)0);
			((Node)val5).Owner = (Node)(object)val;
			return val;
		}

		[HarmonyPostfix]
		private static void AdjustFilterScales(NCardLibrary __instance, Dictionary<NCardPoolFilter, Func<CardModel, bool>> ____poolFilters)
		{
			//IL_003b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0040: Unknown result type (might be due to invalid IL or missing references)
			//IL_0048: Unknown result type (might be due to invalid IL or missing references)
			//IL_006a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0077: Unknown result type (might be due to invalid IL or missing references)
			//IL_007c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0082: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
			//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
			//IL_011f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0124: Unknown result type (might be due to invalid IL or missing references)
			//IL_0125: Unknown result type (might be due to invalid IL or missing references)
			//IL_0133: Unknown result type (might be due to invalid IL or missing references)
			//IL_0138: Unknown result type (might be due to invalid IL or missing references)
			//IL_0139: Unknown result type (might be due to invalid IL or missing references)
			//IL_0147: Unknown result type (might be due to invalid IL or missing references)
			//IL_014c: Unknown result type (might be due to invalid IL or missing references)
			//IL_014d: Unknown result type (might be due to invalid IL or missing references)
			//IL_016b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0170: Unknown result type (might be due to invalid IL or missing references)
			//IL_0171: Unknown result type (might be due to invalid IL or missing references)
			//IL_017f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0184: Unknown result type (might be due to invalid IL or missing references)
			//IL_0185: Unknown result type (might be due to invalid IL or missing references)
			//IL_0193: Unknown result type (might be due to invalid IL or missing references)
			//IL_0198: Unknown result type (might be due to invalid IL or missing references)
			//IL_0199: Unknown result type (might be due to invalid IL or missing references)
			//IL_01a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_01af: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b4: Unknown result type (might be due to invalid IL or missing references)
			//IL_01be: Unknown result type (might be due to invalid IL or missing references)
			//IL_0243: Unknown result type (might be due to invalid IL or missing references)
			//IL_0248: Unknown result type (might be due to invalid IL or missing references)
			//IL_0249: Unknown result type (might be due to invalid IL or missing references)
			//IL_0257: Unknown result type (might be due to invalid IL or missing references)
			//IL_025c: Unknown result type (might be due to invalid IL or missing references)
			//IL_025d: Unknown result type (might be due to invalid IL or missing references)
			//IL_026b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0270: Unknown result type (might be due to invalid IL or missing references)
			//IL_0271: Unknown result type (might be due to invalid IL or missing references)
			//IL_027f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0284: Unknown result type (might be due to invalid IL or missing references)
			//IL_0285: Unknown result type (might be due to invalid IL or missing references)
			//IL_01f6: Unknown result type (might be due to invalid IL or missing references)
			//IL_01fb: Unknown result type (might be due to invalid IL or missing references)
			//IL_01fc: Unknown result type (might be due to invalid IL or missing references)
			//IL_020a: Unknown result type (might be due to invalid IL or missing references)
			//IL_020f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0210: Unknown result type (might be due to invalid IL or missing references)
			//IL_021e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0223: Unknown result type (might be due to invalid IL or missing references)
			//IL_0224: Unknown result type (might be due to invalid IL or missing references)
			Control parentControl = ((Control)____poolFilters.First().Key).GetParentControl();
			GridContainer val = (GridContainer)(object)((parentControl is GridContainer) ? parentControl : null);
			if (val == null)
			{
				throw new Exception("Failed to find grid container for PoolFilters");
			}
			int childCount = ((Node)val).GetChildCount(false);
			Vector2 one = Vector2.One;
			int num = 4;
			float num2 = 64f * one.Y * MathF.Ceiling((float)childCount / (float)num);
			float num3 = 192f;
			while (num2 > num3)
			{
				num++;
				one = Vector2.One * (4f / (float)num);
				num2 = 64f * one.Y * MathF.Ceiling((float)childCount / (float)num);
			}
			FieldInfo fieldInfo = AccessTools.Field(typeof(NCardPoolFilter), "_image");
			FieldInfo fieldInfo2 = AccessTools.Field(typeof(NCardPoolFilter), "_controllerSelectionReticle");
			one = Vector2.One * (4f / (float)num);
			foreach (Node child2 in ((Node)val).GetChildren(false))
			{
				NCardPoolFilter val2 = (NCardPoolFilter)(object)((child2 is NCardPoolFilter) ? child2 : null);
				if (val2 == null)
				{
					continue;
				}
				((Control)val2).CustomMinimumSize = ((Control)val2).CustomMinimumSize * one;
				((Control)val2).Size = ((Control)val2).Size * one;
				((Control)val2).PivotOffset = ((Control)val2).PivotOffset * one;
				object? value = fieldInfo.GetValue(val2);
				Control val3 = (Control)((value is Control) ? value : null);
				val3.CustomMinimumSize *= one;
				val3.Size *= one;
				val3.PivotOffset *= one;
				val3.Position = (((Control)val2).Size - val3.Size) * 0.5f;
				if (((Node)val3).GetChildCount(false) > 0)
				{
					Node child = ((Node)val3).GetChild(0, false);
					Control val4 = (Control)(object)((child is Control) ? child : null);
					if (val4 != null)
					{
						val4.CustomMinimumSize *= one;
						val4.Size *= one;
						val4.PivotOffset *= one;
					}
				}
				object? value2 = fieldInfo2.GetValue(val2);
				NSelectionReticle val5 = (NSelectionReticle)((value2 is NSelectionReticle) ? value2 : null);
				((Control)val5).CustomMinimumSize = ((Control)val5).CustomMinimumSize * one;
				((Control)val5).Size = ((Control)val5).Size * one;
				((Control)val5).PivotOffset = ((Control)val5).PivotOffset * one;
				((Control)val5).Position = ((Control)val5).Position * one;
			}
			val.Columns = num;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	public class ExtraTooltips
	{
		[HarmonyTranspiler]
		private static List<CodeInstruction> AddCustomTips(IEnumerable<CodeInstruction> instructions)
		{
			return new InstructionPatcher(instructions).Match(new InstructionMatcher().ldarg_0().callvirt(AccessTools.PropertyGetter(typeof(CardModel), "ExtraHoverTips")).call(null)
				.stloc_0()).Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[3]
			{
				CodeInstruction.LoadLocal(0, false),
				CodeInstruction.LoadArgument(0, false),
				CodeInstruction.Call(typeof(ExtraTooltips), "AddTips", (Type[])null, (Type[])null)
			}));
		}

		public static void AddTips(List<IHoverTip> tips, CardModel card)
		{
			foreach (DynamicVar value in card.DynamicVars.Values)
			{
				Func<IHoverTip> func = DynamicVarExtensions.DynamicVarTips[value];
				IHoverTip val = DynamicVarExtensions.DynamicVarTips[value]?.Invoke();
				if (val != null)
				{
					tips.Add(val);
				}
			}
		}
	}
	[HarmonyPatch(typeof(DynamicVar), "Clone")]
	internal class CloneTooltips
	{
		[HarmonyPostfix]
		private static DynamicVar Copy(DynamicVar __result, DynamicVar __instance)
		{
			Func<IHoverTip> func = DynamicVarExtensions.DynamicVarTips[__instance];
			if (func != null)
			{
				DynamicVarExtensions.DynamicVarTips[__result] = func;
			}
			return __result;
		}
	}
	[HarmonyPatch(typeof(NModInfoContainer), "_Ready")]
	public static class ModConfigButtonPatch
	{
		public static readonly SpireField<NModInfoContainer, Control> ConfigButton = new SpireField<NModInfoContainer, Control>((NModInfoContainer node) => NConfigButton.Create("ConfigButton", node));

		[HarmonyPostfix]
		public static void PrepButton(NModInfoContainer __instance)
		{
			ConfigButton.Get(__instance);
		}
	}
	[HarmonyPatch(typeof(NModInfoContainer), "Fill")]
	public static class ModConfigFillPatch
	{
		public static Mod? CurrentMod { get; private set; }

		public static void Postfix(NModInfoContainer __instance, Mod mod)
		{
			CurrentMod = mod;
			Control val = ModConfigButtonPatch.ConfigButton.Get(__instance);
			if (val != null)
			{
				if (mod.wasLoaded && mod.manifest != null && ModConfigRegistry.Get(mod.manifest.id) != null)
				{
					((CanvasItem)val).Show();
				}
				else
				{
					((CanvasItem)val).Hide();
				}
			}
		}
	}
}
namespace BaseLib.Patches.Features
{
	[HarmonyPatch(typeof(CardModel), "GetResultPileType")]
	public static class ExhaustivePatch
	{
		private static void Postfix(CardModel __instance, ref PileType __result)
		{
			if (GetExhaustive(__instance) == 1)
			{
				__result = (PileType)4;
			}
		}

		public static int GetExhaustive(CardModel card)
		{
			DynamicVar val = default(DynamicVar);
			int baseExhaustive = (card.DynamicVars.TryGetValue("Exhaustive", ref val) ? val.IntValue : 0);
			return ExhaustiveVar.ExhaustiveCount(card, baseExhaustive);
		}
	}
	[HarmonyPatch(typeof(CardModel), "GetResultPileType")]
	public static class PersistPatch
	{
		[HarmonyTranspiler]
		private static List<CodeInstruction> AltDestination(IEnumerable<CodeInstruction> instructions)
		{
			return new InstructionPatcher(instructions).Match(new InstructionMatcher().ldc_i4_4().ret().ldc_i4_3()).Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[2]
			{
				CodeInstruction.LoadArgument(0, false),
				CodeInstruction.Call(typeof(PersistPatch), "NormalOrPersist", (Type[])null, (Type[])null)
			}));
		}

		private static PileType NormalOrPersist(PileType dest, CardModel model)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0003: Invalid comparison between Unknown and I4
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_001b: Unknown result type (might be due to invalid IL or missing references)
			if ((int)dest == 3 && model.IsPersist())
			{
				return (PileType)2;
			}
			return dest;
		}

		public static bool IsPersist(this CardModel card)
		{
			DynamicVar val = default(DynamicVar);
			int basePersist = (card.DynamicVars.TryGetValue("Persist", ref val) ? val.IntValue : 0);
			return PersistVar.PersistCount(card, basePersist) > 0;
		}
	}
	[HarmonyPatch(typeof(Hook), "AfterCardPlayed")]
	public static class RefundPatch
	{
		public static async void Postfix(CombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)
		{
			DynamicVar val = default(DynamicVar);
			int refundAmount = (cardPlay.Card.DynamicVars.TryGetValue("Refund", ref val) ? val.IntValue : 0);
			ResourceInfo resources;
			int num;
			if (refundAmount > 0)
			{
				resources = cardPlay.Resources;
				num = ((((ResourceInfo)(ref resources)).EnergySpent > 0) ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			if (num != 0)
			{
				resources = cardPlay.Resources;
				await PlayerCmd.GainEnergy((decimal)Math.Min(refundAmount, ((ResourceInfo)(ref resources)).EnergySpent), cardPlay.Card.Owner);
			}
		}
	}
}
namespace BaseLib.Patches.Content
{
	[HarmonyPatch(typeof(AncientDialogueSet), "PopulateLocKeys")]
	internal class AddAncientDialogues
	{
		[HarmonyPrefix]
		private static void AddCharacterDefinedInteractions(AncientDialogueSet __instance, string ancientEntry)
		{
			MainFile.Logger.Info("Checking for additional interactions with " + ancientEntry, 1);
			Dictionary<string, IReadOnlyList<AncientDialogue>> characterDialogues = __instance.CharacterDialogues;
			foreach (CharacterModel allCharacter in ModelDb.AllCharacters)
			{
				if (!(allCharacter is CustomCharacterModel))
				{
					continue;
				}
				string text = AncientDialogueUtil.BaseLocKey(ancientEntry, ((AbstractModel)allCharacter).Id.Entry);
				IReadOnlyList<AncientDialogue> valueOrDefault = characterDialogues.GetValueOrDefault(text, Array.Empty<AncientDialogue>());
				List<AncientDialogue> dialoguesForKey = AncientDialogueUtil.GetDialoguesForKey("ancients", text);
				if (dialoguesForKey.Count <= 0)
				{
					continue;
				}
				Dictionary<string, IReadOnlyList<AncientDialogue>> dictionary = characterDialogues;
				string entry = ((AbstractModel)allCharacter).Id.Entry;
				IReadOnlyList<AncientDialogue> readOnlyList = valueOrDefault;
				List<AncientDialogue> list = dialoguesForKey;
				int num = 0;
				AncientDialogue[] array = (AncientDialogue[])(object)new AncientDialogue[readOnlyList.Count + list.Count];
				foreach (AncientDialogue item in readOnlyList)
				{
					array[num] = item;
					num++;
				}
				Span<AncientDialogue> span = CollectionsMarshal.AsSpan(list);
				span.CopyTo(new Span<AncientDialogue>(array).Slice(num, span.Length));
				num += span.Length;
				dictionary[entry] = new <>z__ReadOnlyArray<AncientDialogue>(array);
				MainFile.Logger.Info($"Found {dialoguesForKey.Count} additional dialogues for {ancientEntry} with {((AbstractModel)allCharacter).Id.Entry}, total {characterDialogues[((AbstractModel)allCharacter).Id.Entry].Count}", 1);
			}
		}
	}
	[HarmonyPatch(typeof(ModelDb), "InitIds")]
	public static class CustomContentDictionary
	{
		private static readonly Dictionary<Type, int> CustomModelCounts;

		private static readonly Dictionary<Type, Type> PoolTypes;

		public static readonly List<CustomAncientModel> CustomAncients;

		static CustomContentDictionary()
		{
			CustomModelCounts = new Dictionary<Type, int>();
			PoolTypes = new Dictionary<Type, Type>();
			CustomAncients = new List<CustomAncientModel>();
			PoolTypes.Add(typeof(CardPoolModel), typeof(CardModel));
			PoolTypes.Add(typeof(RelicPoolModel), typeof(RelicModel));
			PoolTypes.Add(typeof(PotionPoolModel), typeof(PotionModel));
		}

		public static void AddModel(Type modelType)
		{
			PoolAttribute poolAttribute = modelType.GetCustomAttribute<PoolAttribute>() ?? throw new Exception("Model " + modelType.FullName + " must be marked with a PoolAttribute to determine which pool to add it to.");
			if (!IsValidPool(modelType, poolAttribute.PoolType))
			{
				throw new Exception($"Model {modelType.FullName} is assigned to incorrect type of pool {poolAttribute.PoolType.FullName}.");
			}
			int valueOrDefault = CustomModelCounts.GetValueOrDefault(poolAttribute.PoolType, 0);
			CustomModelCounts[poolAttribute.PoolType] = valueOrDefault + 1;
			ModHelper.AddModelToPool(poolAttribute.PoolType, modelType);
		}

		public static void AddAncient(CustomAncientModel ancient)
		{
			int valueOrDefault = CustomModelCounts.GetValueOrDefault(typeof(CustomAncientModel), 0);
			CustomModelCounts[typeof(CustomAncientModel)] = valueOrDefault + 1;
			CustomAncients.Add(ancient);
		}

		private static bool IsValidPool(Type modelType, Type poolType)
		{
			Type baseType = poolType.BaseType;
			while (baseType != null)
			{
				if (PoolTypes.TryGetValue(baseType, out Type value))
				{
					return modelType.IsAssignableTo(value);
				}
				baseType = baseType.BaseType;
			}
			throw new Exception($"Model {modelType.FullName} is assigned to {poolType.FullName} which is not a valid pool type.");
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class CustomAncientExistence
	{
		[HarmonyPostfix]
		private static IEnumerable<AncientEventModel> AddCustomAncientForCompendium(IEnumerable<AncientEventModel> __result)
		{
			List<AncientEventModel> list = new List<AncientEventModel>();
			list.AddRange(__result);
			foreach (CustomAncientModel customAncient in CustomContentDictionary.CustomAncients)
			{
				list.Add((AncientEventModel)(object)customAncient);
			}
			return new <>z__ReadOnlyList<AncientEventModel>(list);
		}
	}
	[HarmonyPatch(typeof(RunManager), "GenerateRooms")]
	public class CurrentGeneratingRunState
	{
		private static readonly MethodInfo StateGetter = AccessTools.PropertyGetter(typeof(RunManager), "State");

		public static RunState? State { get; private set; }

		[HarmonyPrefix]
		private static void GetState(RunManager __instance)
		{
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_001b: Expected O, but got Unknown
			State = (RunState)StateGetter.Invoke(__instance, Array.Empty<object>());
		}

		[HarmonyPostfix]
		private static void ClearState()
		{
			State = null;
		}
	}
	[HarmonyPatch(typeof(ActModel), "GenerateRooms")]
	internal class AddCustomAncientsToPool
	{
		private static readonly FieldInfo RoomSet = AccessTools.Field(typeof(ActModel), "_rooms");

		[HarmonyPrefix]
		private static void AddToModelPool(ActModel __instance, List<AncientEventModel>? ____sharedAncientSubset)
		{
			if (____sharedAncientSubset == null)
			{
				return;
			}
			____sharedAncientSubset.RemoveAll(((IEnumerable<AncientEventModel>)CustomContentDictionary.CustomAncients).Contains<AncientEventModel>);
			List<CustomAncientModel> list = CustomContentDictionary.CustomAncients.ToList();
			list.Sort((CustomAncientModel a, CustomAncientModel b) => string.Compare(((AbstractModel)a).Id.Entry, ((AbstractModel)b).Id.Entry, StringComparison.Ordinal));
			list.RemoveAll((CustomAncientModel ancient) => !ancient.IsValidForAct(__instance) || ____sharedAncientSubset.Contains((AncientEventModel)(object)ancient));
			RunState? state = CurrentGeneratingRunState.State;
			foreach (ActModel item2 in ((state != null) ? state.Acts : null) ?? Array.Empty<ActModel>())
			{
				object? value = RoomSet.GetValue(item2);
				RoomSet val = (RoomSet)((value is RoomSet) ? value : null);
				if (val != null && val.HasAncient && item2 != __instance && item2.Ancient is CustomAncientModel item)
				{
					list.Remove(item);
				}
			}
			____sharedAncientSubset.AddRange((IEnumerable<AncientEventModel>)list);
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class ModelDbSharedCardPoolsPatch
	{
		private static readonly List<CardPoolModel> CustomSharedPools = new List<CardPoolModel>();

		[HarmonyPostfix]
		private static IEnumerable<CardPoolModel> AddCustomPools(IEnumerable<CardPoolModel> __result)
		{
			List<CardPoolModel> list = new List<CardPoolModel>();
			list.AddRange(__result);
			list.AddRange(CustomSharedPools);
			return new <>z__ReadOnlyList<CardPoolModel>(list);
		}

		public static void Register(CustomCardPoolModel pool)
		{
			CustomSharedPools.Add((CardPoolModel)(object)pool);
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class ModelDbSharedRelicPoolsPatch
	{
		private static readonly List<RelicPoolModel> customSharedPools = new List<RelicPoolModel>();

		[HarmonyPostfix]
		private static IEnumerable<RelicPoolModel> AddCustomPools(IEnumerable<RelicPoolModel> __result)
		{
			List<RelicPoolModel> list = new List<RelicPoolModel>();
			list.AddRange(__result);
			list.AddRange(customSharedPools);
			return new <>z__ReadOnlyList<RelicPoolModel>(list);
		}

		public static void Register(CustomRelicPoolModel pool)
		{
			customSharedPools.Add((RelicPoolModel)(object)pool);
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class ModelDbSharedPotionPoolsPatch
	{
		private static readonly List<PotionPoolModel> customSharedPools = new List<PotionPoolModel>();

		[HarmonyPostfix]
		private static IEnumerable<PotionPoolModel> AddCustomPools(IEnumerable<PotionPoolModel> __result)
		{
			List<PotionPoolModel> list = new List<PotionPoolModel>();
			list.AddRange(__result);
			list.AddRange(customSharedPools);
			return new <>z__ReadOnlyList<PotionPoolModel>(list);
		}

		public static void Register(CustomPotionPoolModel pool)
		{
			customSharedPools.Add((PotionPoolModel)(object)pool);
		}
	}
	[HarmonyPatch(typeof(ActModel), "GenerateRooms")]
	internal class ActModelGenerateRoomsPatch
	{
		[HarmonyPostfix]
		private static void ForceAncientToSpawn(ActModel __instance)
		{
			RoomSet value = Traverse.Create((object)__instance).Field<RoomSet>("_rooms").Value;
			if (value.HasAncient)
			{
				AncientEventModel rngChosenAncient = value.Ancient;
				CustomAncientModel customAncientModel = CustomContentDictionary.CustomAncients.Find((CustomAncientModel a) => a.ShouldForceSpawn(__instance, rngChosenAncient));
				if (customAncientModel != null)
				{
					value.Ancient = (AncientEventModel)(object)customAncientModel;
				}
			}
		}
	}
	public class CustomEnergyIconPatches
	{
		[HarmonyPatch(typeof(EnergyIconHelper), "GetPath", new Type[] { typeof(string) })]
		private static class IconPatch
		{
			private static bool Prefix(string prefix, ref string __result)
			{
				//IL_003f: Unknown result type (might be due to invalid IL or missing references)
				//IL_0049: Expected O, but got Unknown
				int num = prefix.IndexOf('∴');
				if (num < 0)
				{
					return true;
				}
				string text = prefix.Substring(0, num);
				int num2 = num + 1;
				AbstractModel byId = ModelDb.GetById<AbstractModel>(new ModelId(text, prefix.Substring(num2, prefix.Length - num2)));
				if (byId is ICustomEnergyIconPool customEnergyIconPool)
				{
					string bigEnergyIconPath = customEnergyIconPool.BigEnergyIconPath;
					if (bigEnergyIconPath != null)
					{
						__result = bigEnergyIconPath;
						return false;
					}
				}
				return true;
			}
		}

		[HarmonyPatch(typeof(EnergyIconsFormatter), "TryEvaluateFormat")]
		private static class TextIconPatch
		{
			private static List<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				return new InstructionPatcher(instructions).Match(new InstructionMatcher().call(AccessTools.Method(typeof(string), "Concat", new Type[3]
				{
					typeof(string),
					typeof(string),
					typeof(string)
				}, (Type[])null)).stloc_3()).Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[4]
				{
					CodeInstruction.LoadLocal(0, false),
					CodeInstruction.LoadLocal(3, false),
					CodeInstruction.Call(typeof(CustomEnergyIconPatches), "GetTextIcon", (Type[])null, (Type[])null),
					CodeInstruction.StoreLocal(3)
				}));
			}
		}

		public const char Delimiter = '∴';

		public static string GetEnergyColorName(ModelId id)
		{
			return id.Category + "∴" + id.Entry;
		}

		private static string GetTextIcon(string prefix, string oldText)
		{
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0049: Expected O, but got Unknown
			int num = prefix.IndexOf('∴');
			if (num < 0)
			{
				return oldText;
			}
			string text = prefix.Substring(0, num);
			int num2 = num + 1;
			AbstractModel byId = ModelDb.GetById<AbstractModel>(new ModelId(text, prefix.Substring(num2, prefix.Length - num2)));
			if (byId is ICustomEnergyIconPool customEnergyIconPool)
			{
				string textEnergyIconPath = customEnergyIconPool.TextEnergyIconPath;
				if (textEnergyIconPath != null)
				{
					return "[img]" + textEnergyIconPath + "[/img]";
				}
			}
			return oldText;
		}
	}
	[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	public sealed class CustomEnumAttribute(string? name = null) : Attribute()
	{
		public string? Name { get; } = name;
	}
	[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	public sealed class KeywordPropertiesAttribute(AutoKeywordPosition position) : Attribute()
	{
		public AutoKeywordPosition Position { get; } = position;
	}
	public enum AutoKeywordPosition
	{
		None,
		Before,
		After
	}
	public static class CustomKeywords
	{
		public readonly struct KeywordInfo
		{
			public readonly string Key;

			public readonly AutoKeywordPosition AutoPosition;

			public KeywordInfo(string key, AutoKeywordPosition autoPosition)
			{
				Key = key;
				AutoPosition = autoPosition;
			}

			public static implicit operator string(KeywordInfo info)
			{
				return info.Key;
			}
		}

		public static readonly Dictionary<int, KeywordInfo> KeywordIDs = new Dictionary<int, KeywordInfo>();
	}
	public static class CustomEnums
	{
		private class KeyGenerator
		{
			private static readonly Dictionary<Type, Func<object, object>> Incrementers = new Dictionary<Type, Func<object, object>>
			{
				{
					typeof(byte),
					(object val) => (byte)val + 1
				},
				{
					typeof(sbyte),
					(object val) => (sbyte)val + 1
				},
				{
					typeof(short),
					(object val) => (short)val + 1
				},
				{
					typeof(ushort),
					(object val) => (ushort)val + 1
				},
				{
					typeof(int),
					(object val) => (int)val + 1
				},
				{
					typeof(uint),
					(object val) => (uint)val + 1
				},
				{
					typeof(long),
					(object val) => (long)val + 1
				},
				{
					typeof(ulong),
					(object val) => (ulong)val + 1
				}
			};

			private object _nextKey;

			private readonly Func<object, object> _increment;

			public KeyGenerator(Type t)
			{
				if (!t.IsEnum)
				{
					_increment = (object o) => o;
					throw new ArgumentException("Attempted to construct KeyGenerator with non-enum type " + t.FullName);
				}
				Array enumValuesAsUnderlyingType = t.GetEnumValuesAsUnderlyingType();
				Type underlyingType = Enum.GetUnderlyingType(t);
				_nextKey = Convert.ChangeType(0, underlyingType);
				_increment = Incrementers[underlyingType];
				if (enumValuesAsUnderlyingType.Length > 0)
				{
					foreach (object item in enumValuesAsUnderlyingType)
					{
						if (((IComparable)item).CompareTo(_nextKey) >= 0)
						{
							_nextKey = _increment(item);
						}
					}
				}
				MainFile.Logger.Info($"Generated KeyGenerator for enum {t.FullName} with starting value {_nextKey}", 1);
			}

			public object GetKey()
			{
				object nextKey = _nextKey;
				_nextKey = _increment(_nextKey);
				return nextKey;
			}
		}

		private static readonly Dictionary<Type, KeyGenerator> KeyGenerators = new Dictionary<Type, KeyGenerator>();

		public static object GenerateKey(Type enumType)
		{
			if (!KeyGenerators.TryGetValue(enumType, out KeyGenerator value))
			{
				KeyGenerators.Add(enumType, value = new KeyGenerator(enumType));
			}
			return value.GetKey();
		}
	}
	internal class GetCustomLocKey
	{
		internal static void Patch(Harmony harmony)
		{
			//IL_0034: Unknown result type (might be due to invalid IL or missing references)
			//IL_0041: Expected O, but got Unknown
			Type type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.Cards.CardKeywordExtensions");
			MethodInfo methodInfo = AccessTools.Method(type, "GetLocKeyPrefix", (Type[])null, (Type[])null);
			MethodInfo methodInfo2 = AccessTools.Method(typeof(GetCustomLocKey), "UseCustomKeywordMap", (Type[])null, (Type[])null);
			harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(methodInfo2), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		}

		private static bool UseCustomKeywordMap(CardKeyword keyword, ref string? __result)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000e: Expected I4, but got Unknown
			if (!CustomKeywords.KeywordIDs.TryGetValue((int)keyword, out var value))
			{
				return true;
			}
			__result = value.Key;
			return false;
		}
	}
	[HarmonyPatch(typeof(ModelDb), "Init")]
	internal class GenEnumValues
	{
		[HarmonyPrefix]
		private static void FindAndGenerate()
		{
			//IL_02fd: Unknown result type (might be due to invalid IL or missing references)
			//IL_0217: Unknown result type (might be due to invalid IL or missing references)
			//IL_022b: Unknown result type (might be due to invalid IL or missing references)
			Type[] modTypes = ReflectionHelper.ModTypes;
			foreach (Type type in modTypes)
			{
				IEnumerable<FieldInfo> enumerable = from field in type.GetFields()
					where Attribute.IsDefined(field, typeof(CustomEnumAttribute))
					select field;
				foreach (FieldInfo item in enumerable)
				{
					if (!item.FieldType.IsEnum)
					{
						throw new Exception($"Field {item.DeclaringType?.FullName}.{item.Name} should be an enum type for CustomEnum");
					}
					if (!item.IsStatic)
					{
						throw new Exception($"Field {item.DeclaringType?.FullName}.{item.Name} should be static for CustomEnum");
					}
					if (item.DeclaringType == null)
					{
						continue;
					}
					CustomEnumAttribute customAttribute = item.GetCustomAttribute<CustomEnumAttribute>();
					object obj = CustomEnums.GenerateKey(item.FieldType);
					item.SetValue(null, obj);
					if (item.FieldType == typeof(CardKeyword))
					{
						string key = item.DeclaringType.GetPrefix() + (customAttribute?.Name ?? item.Name).ToUpperInvariant();
						AutoKeywordPosition autoKeywordPosition = item.GetCustomAttribute<KeywordPropertiesAttribute>()?.Position ?? AutoKeywordPosition.None;
						switch (autoKeywordPosition)
						{
						case AutoKeywordPosition.Before:
							AutoKeywordText.AdditionalBeforeKeywords.Add((CardKeyword)obj);
							break;
						case AutoKeywordPosition.After:
							AutoKeywordText.AdditionalAfterKeywords.Add((CardKeyword)obj);
							break;
						}
						CustomKeywords.KeywordIDs.Add((int)obj, new CustomKeywords.KeywordInfo(key, autoKeywordPosition));
					}
					if (!(item.FieldType != typeof(PileType)) && type.IsAssignableTo(typeof(CustomPile)))
					{
						ConstructorInfo constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public, Array.Empty<Type>()) ?? throw new Exception("CustomPile " + type.FullName + " with custom PileType does not have an accessible no-parameter constructor");
						PileType? val = (PileType?)item.GetValue(null);
						if (!val.HasValue)
						{
							throw new Exception("Failed to be set up custom PileType in " + type.FullName);
						}
						CustomPiles.RegisterCustomPile(val.Value, () => (CustomPile)constructor.Invoke(null));
					}
				}
			}
		}
	}
	public class CustomPiles
	{
		public static readonly Dictionary<PileType, Func<CustomPile>> CustomPileProviders = new Dictionary<PileType, Func<CustomPile>>();

		public static readonly SpireField<PlayerCombatState, Dictionary<PileType, CustomPile>> Piles = new SpireField<PlayerCombatState, Dictionary<PileType, CustomPile>>((Func<Dictionary<PileType, CustomPile>?>)delegate
		{
			//IL_0021: Unknown result type (might be due to invalid IL or missing references)
			Dictionary<PileType, CustomPile> dictionary = new Dictionary<PileType, CustomPile>();
			foreach (KeyValuePair<PileType, Func<CustomPile>> customPileProvider in CustomPileProviders)
			{
				dictionary.Add(customPileProvider.Key, customPileProvider.Value());
			}
			return dictionary;
		});

		public static void RegisterCustomPile(PileType pileType, Func<CustomPile> constructor)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			CustomPileProviders.Add(pileType, constructor);
		}

		public static CardPile[] AddCustomPiles(CardPile[] original, PlayerCombatState combatState)
		{
			Dictionary<PileType, CustomPile>.ValueCollection valueCollection = Piles.Get(combatState)?.Values;
			if (valueCollection == null)
			{
				return original;
			}
			Dictionary<PileType, CustomPile>.ValueCollection valueCollection2 = valueCollection;
			int num = 0;
			CardPile[] array = (CardPile[])(object)new CardPile[original.Length + valueCollection2.Count];
			ReadOnlySpan<CardPile> readOnlySpan = new ReadOnlySpan<CardPile>(original);
			readOnlySpan.CopyTo(new Span<CardPile>(array).Slice(num, readOnlySpan.Length));
			num += readOnlySpan.Length;
			foreach (CustomPile item in valueCollection2)
			{
				array[num] = (CardPile)(object)item;
				num++;
			}
			return array;
		}

		public static CustomPile? GetCustomPile(PlayerCombatState? state, PileType type)
		{
			//IL_0020: Unknown result type (might be due to invalid IL or missing references)
			if (state == null)
			{
				return null;
			}
			return Piles.Get(state)?.GetValueOrDefault(type);
		}

		public static bool IsCustomPile(PileType pileType)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			return CustomPileProviders.ContainsKey(pileType);
		}

		public static Vector2 GetPosition(PileType pileType, NCard? card, Vector2 size)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_0013: Unknown result type (might be due to invalid IL or missing references)
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
			//IL_0049: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			//IL_0036: Unknown result type (might be due to invalid IL or missing references)
			//IL_009b: Unknown result type (might be due to invalid IL or missing references)
			//IL_009c: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
			//IL_0073: Unknown result type (might be due to invalid IL or missing references)
			if (!CustomPileProviders.ContainsKey(pileType))
			{
				return Vector2.Zero;
			}
			if (card == null || card.Model == null)
			{
				return Vector2.Zero;
			}
			CustomPile customPile = GetCustomPile(card.Model.Owner.PlayerCombatState, pileType);
			if (customPile == null)
			{
				throw new Exception($"CustomPile {pileType} does not exist");
			}
			return customPile.GetTargetPosition(card.Model, size);
		}

		public static NCard? FindOnTable(CardModel card, PileType pileType)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_0033: Unknown result type (might be due to invalid IL or missing references)
			if (!CustomPileProviders.ContainsKey(pileType))
			{
				return null;
			}
			MainFile.Logger.Info("Looking for NCard in Custom Pile!", 1);
			return GetCustomPile(card.Owner.PlayerCombatState, pileType)?.GetNCard(card);
		}

		public static bool IsCardVisible(CardModel card)
		{
			return false;
		}
	}
	[HarmonyPatch(typeof(CardPile), "Get")]
	internal class GetCombatPile
	{
		[HarmonyPrefix]
		private static bool CheckCustomPile(PileType type, Player player, ref CardPile? __result)
		{
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			__result = (CardPile?)(object)CustomPiles.GetCustomPile(player.PlayerCombatState, type);
			return __result == null;
		}
	}
	[HarmonyPatch(typeof(PileTypeExtensions), "IsCombatPile")]
	internal class IsCombatPile
	{
		[HarmonyPrefix]
		private static bool CustomIsCombat(PileType pileType, ref bool __result)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			if (CustomPiles.CustomPileProviders.ContainsKey(pileType))
			{
				__result = true;
				return false;
			}
			return true;
		}
	}
	[HarmonyPatch(typeof(NCard), "FindOnTable")]
	internal class GetNCardPile
	{
		[HarmonyTranspiler]
		private static List<CodeInstruction> CheckCustomPiles(IEnumerable<CodeInstruction> instructions)
		{
			//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d2: Expected O, but got Unknown
			List<Label> labels;
			return new InstructionPatcher(instructions).Match(new InstructionMatcher().ldloc_1().ret()).Step(-2).GetLabels(out labels)
				.ResetPosition()
				.Match(new InstructionMatcher().stloc_3().ldloc_3())
				.Step(-1)
				.Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[7]
				{
					CodeInstruction.LoadLocal(3, false),
					CodeInstruction.Call(typeof(CustomPiles), "IsCustomPile", (Type[])null, (Type[])null),
					CodeInstruction.LoadArgument(0, false),
					CodeInstruction.LoadLocal(3, false),
					CodeInstruction.Call(typeof(CustomPiles), "FindOnTable", (Type[])null, (Type[])null),
					CodeInstruction.StoreLocal(1),
					new CodeInstruction(OpCodes.Brtrue_S, (object)labels[0])
				}));
		}
	}
	[HarmonyPatch(typeof(PileTypeExtensions), "GetTargetPosition")]
	internal class GetPilePosition
	{
		[HarmonyTranspiler]
		private static List<CodeInstruction> CustomPilePosition(IEnumerable<CodeInstruction> instructions)
		{
			//IL_012d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0133: Unknown result type (might be due to invalid IL or missing references)
			//IL_017a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0180: Expected O, but got Unknown
			List<Label> labels;
			return new InstructionPatcher(instructions).Match(new InstructionMatcher().ldloc_2().ret()).Step(-2).GetLabels(out labels)
				.ResetPosition()
				.Match(new InstructionMatcher().call(AccessTools.PropertyGetter(typeof(Rect2), "Size")).stloc_0().ldarg_0())
				.Step(-1)
				.Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[8]
				{
					CodeInstruction.LoadArgument(0, false),
					CodeInstruction.Call((Expression<Action>)(() => CustomPiles.IsCustomPile((PileType)0))),
					CodeInstruction.LoadArgument(0, false),
					CodeInstruction.LoadArgument(1, false),
					CodeInstruction.LoadLocal(0, false),
					CodeInstruction.Call((Expression<Action>)(() => CustomPiles.GetPosition((PileType)0, null, default(Vector2)))),
					CodeInstruction.StoreLocal(2),
					new CodeInstruction(OpCodes.Brtrue_S, (object)labels[0])
				}));
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class SpecialPileInCombat
	{
		[HarmonyTranspiler]
		private static List<CodeInstruction> AddPile(IEnumerable<CodeInstruction> instructions)
		{
			return new InstructionPatcher(instructions).Match(new InstructionMatcher().stfld(AccessTools.Field(typeof(PlayerCombatState), "_piles"))).Step(-1).Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[2]
			{
				CodeInstruction.LoadArgument(0, false),
				CodeInstruction.Call(typeof(CustomPiles), "AddCustomPiles", (Type[])null, (Type[])null)
			}));
		}
	}
	public class TheBigPatchToCardPileCmdAdd
	{
		private static Type? stateMachineType;

		public static void Patch(Harmony harmony)
		{
			harmony.PatchAsyncMoveNext(AccessTools.Method(typeof(CardPileCmd), "Add", new Type[5]
			{
				typeof(IEnumerable<CardModel>),
				typeof(CardPile),
				typeof(CardPilePosition),
				typeof(AbstractModel),
				typeof(bool)
			}, (Type[])null), out stateMachineType, null, null, HarmonyMethod.op_Implicit(AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), "BigPatch", (Type[])null, (Type[])null)));
		}

		private static List<CodeInstruction> BigPatch(IEnumerable<CodeInstruction> instructions)
		{
			//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ac: Expected O, but got Unknown
			//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d2: Expected O, but got Unknown
			//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f5: Expected O, but got Unknown
			//IL_0102: Unknown result type (might be due to invalid IL or missing references)
			//IL_0108: Expected O, but got Unknown
			//IL_0185: Unknown result type (might be due to invalid IL or missing references)
			//IL_018b: Expected O, but got Unknown
			//IL_01ab: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b1: Expected O, but got Unknown
			//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d4: Expected O, but got Unknown
			//IL_01e7: Unknown result type (might be due to invalid IL or missing references)
			//IL_01ed: Expected O, but got Unknown
			//IL_025b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0261: Expected O, but got Unknown
			//IL_0281: Unknown result type (might be due to invalid IL or missing references)
			//IL_0287: Expected O, but got Unknown
			//IL_02a4: Unknown result type (might be due to invalid IL or missing references)
			//IL_02aa: Expected O, but got Unknown
			//IL_02b7: Unknown result type (might be due to invalid IL or missing references)
			//IL_02bd: Expected O, but got Unknown
			//IL_034f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0355: Expected O, but got Unknown
			//IL_0375: Unknown result type (might be due to invalid IL or missing references)
			//IL_037b: Expected O, but got Unknown
			//IL_0398: Unknown result type (might be due to invalid IL or missing references)
			//IL_039e: Expected O, but got Unknown
			//IL_03ab: Unknown result type (might be due to invalid IL or missing references)
			//IL_03b1: Expected O, but got Unknown
			//IL_04b4: Unknown result type (might be due to invalid IL or missing references)
			//IL_04ba: Expected O, but got Unknown
			//IL_04c8: Unknown result type (might be due to invalid IL or missing references)
			//IL_04ce: Expected O, but got Unknown
			//IL_05ae: Unknown result type (might be due to invalid IL or missing references)
			//IL_05b4: Expected O, but got Unknown
			//IL_05bd: Unknown result type (might be due to invalid IL or missing references)
			//IL_05c3: Expected O, but got Unknown
			//IL_05f5: Unknown result type (might be due to invalid IL or missing references)
			//IL_05fb: Expected O, but got Unknown
			//IL_0609: Unknown result type (might be due to invalid IL or missing references)
			//IL_060f: Expected O, but got Unknown
			if (stateMachineType == null)
			{
				throw new Exception("Failed to get state machine type for async CardPileCmd.Add");
			}
			Label label;
			List<Label> labels;
			Label label2;
			Label label3;
			object operand;
			object operand2;
			Label label4;
			Label label5;
			return new InstructionPatcher(instructions).Match(new InstructionMatcher().ldfld(stateMachineType.FindStateMachineField("isFullHandAdd")).brtrue_s().ldarg_0()
				.ldfld(stateMachineType.FindStateMachineField("oldPile"))
				.brtrue_s()).Step(-1).GetOperandLabel(out label)
				.Step()
				.Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[6]
				{
					CodeInstruction.LoadArgument(0, false),
					new CodeInstruction(OpCodes.Ldfld, (object)stateMachineType.FindStateMachineField("targetPile")),
					CodeInstruction.LoadArgument(0, false),
					new CodeInstruction(OpCodes.Ldfld, (object)stateMachineType.FindStateMachineField("card")),
					new CodeInstruction(OpCodes.Call, (object)AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), "IsPileCustomPileWhereCardShouldBeVisible", (Type[])null, (Type[])null)),
					new CodeInstruction(OpCodes.Brtrue_S, (object)label)
				}))
				.Match(new InstructionMatcher().stloc_s(24).ldloc_s(24).ldc_i4_1()
					.sub()
					.switch_()
					.br_s()
					.ldc_i4_1())
				.Step(-1)
				.GetLabels(out labels)
				.Step(-1)
				.Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[6]
				{
					CodeInstruction.LoadArgument(0, false),
					new CodeInstruction(OpCodes.Ldfld, (object)stateMachineType.FindStateMachineField("oldPile")),
					CodeInstruction.LoadArgument(0, false),
					new CodeInstruction(OpCodes.Ldfld, (object)stateMachineType.FindStateMachineField("card")),
					new CodeInstruction(OpCodes.Call, (object)AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), "IsPileCustomPileWithCardNotVisible", (Type[])null, (Type[])null)),
					new CodeInstruction(OpCodes.Brtrue_S, (object)labels[0])
				}))
				.Match(new InstructionMatcher().stloc_s(24).ldloc_s(24).ldc_i4_1()
					.beq_s())
				.Step(-1)
				.GetOperandLabel(out label2)
				.Step()
				.Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[6]
				{
					CodeInstruction.LoadArgument(0, false),
					new CodeInstruction(OpCodes.Ldfld, (object)stateMachineType.FindStateMachineField("targetPile")),
					CodeInstruction.LoadArgument(0, false),
					new CodeInstruction(OpCodes.Ldfld, (object)stateMachineType.FindStateMachineField("card")),
					new CodeInstruction(OpCodes.Call, (object)AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), "CustomPileWithoutCustomTransition", (Type[])null, (Type[])null)),
					new CodeInstruction(OpCodes.Brtrue_S, (object)label2)
				}))
				.Match(new InstructionMatcher().ldarg_0().ldfld(AccessTools.Field(stateMachineType, "newPile")).callvirt(AccessTools.PropertyGetter(typeof(CardPile), "Type"))
					.ldc_i4_2()
					.beq_s())
				.Step(-1)
				.GetOperandLabel(out label3)
				.Step()
				.Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[6]
				{
					CodeInstruction.LoadArgument(0, false),
					new CodeInstruction(OpCodes.Ldfld, (object)AccessTools.Field(stateMachineType, "newPile")),
					CodeInstruction.LoadArgument(0, false),
					new CodeInstruction(OpCodes.Ldfld, (object)stateMachineType.FindStateMachineField("card")),
					new CodeInstruction(OpCodes.Call, (object)AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), "IsPileCustomPileWhereCardShouldBeVisible", (Type[])null, (Type[])null)),
					new CodeInstruction(OpCodes.Brtrue_S, (object)label3)
				}))
				.Match(new InstructionMatcher().ldloc_s(35).ldloc_s(35).ldfld(null)
					.ldfld(null))
				.Step(-1)
				.GetOperand(out operand)
				.Step(-1)
				.GetOperand(out operand2)
				.Match(new InstructionMatcher().ldloc_s(35).ldfld(null).callvirt(AccessTools.PropertyGetter(typeof(CardModel), "Pile"))
					.callvirt(AccessTools.PropertyGetter(typeof(CardPile), "Type"))
					.stloc_s(24)
					.ldloc_s(24)
					.ldc_i4_1()
					.sub()
					.ldc_i4_2()
					.ble_un_s())
				.Step(-1)
				.GetOperandLabel(out label4)
				.Step()
				.InsertCopy(-10, 2)
				.Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[2]
				{
					new CodeInstruction(OpCodes.Call, (object)AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), "CustomPileUseGenericTweenForOtherPlayers", (Type[])null, (Type[])null)),
					new CodeInstruction(OpCodes.Brtrue_S, (object)label4)
				}))
				.Match(new InstructionMatcher().callvirt(AccessTools.Method(typeof(Tween), "TweenCallback", (Type[])null, (Type[])null)).pop().br())
				.Step(-1)
				.GetOperandLabel(out label5)
				.Match(new InstructionMatcher().ldloc_s(35).ldfld(null).callvirt(AccessTools.PropertyGetter(typeof(CardModel), "Pile"))
					.callvirt(AccessTools.PropertyGetter(typeof(CardPile), "Type"))
					.stloc_s(40)
					.ldloc_s(40)
					.ldc_i4_2()
					.sub()
					.switch_())
				.InsertCopy(-9, 2)
				.Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[7]
				{
					CodeInstruction.LoadLocal(35, false),
					new CodeInstruction(OpCodes.Ldfld, operand2),
					new CodeInstruction(OpCodes.Ldfld, operand),
					CodeInstruction.LoadLocal(37, false),
					CodeInstruction.LoadLocal(2, false),
					new CodeInstruction(OpCodes.Call, (object)AccessTools.Method(typeof(TheBigPatchToCardPileCmdAdd), "CustomPileUseCustomTween", (Type[])null, (Type[])null)),
					new CodeInstruction(OpCodes.Brtrue_S, (object)label5)
				}));
		}

		public static bool IsPileCustomPileWhereCardShouldBeVisible(CardPile pile, CardModel card)
		{
			return pile is CustomPile customPile && customPile.CardShouldBeVisible(card);
		}

		public static bool IsPileCustomPileWithCardNotVisible(CardPile pile, CardModel card)
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			return pile is CustomPile && NCard.FindOnTable(card, (PileType?)pile.Type) == null;
		}

		public static bool CustomPileWithoutCustomTransition(CardPile pile, CardModel card)
		{
			return pile is CustomPile customPile && !customPile.CardShouldBeVisible(card) && !customPile.NeedsCustomTransitionVisual;
		}

		public static bool CustomPileUseGenericTweenForOtherPlayers(CardModel card)
		{
			CardPile pile = card.Pile;
			return pile is CustomPile customPile && (customPile.CardShouldBeVisible(card) || !customPile.NeedsCustomTransitionVisual);
		}

		public static bool CustomPileUseCustomTween(CardModel card, NCard cardNode, CardPile oldPile, Tween tween)
		{
			CardPile pile = card.Pile;
			if (!(pile is CustomPile customPile))
			{
				return false;
			}
			return customPile.CustomTween(tween, card, cardNode, oldPile);
		}
	}
	[HarmonyPatch(typeof(ModelDb), "GetEntry")]
	public class PrefixIdPatch
	{
		[HarmonyPostfix]
		private static string AdjustID(string __result, Type type)
		{
			if (type.IsAssignableTo(typeof(ICustomModel)))
			{
				return type.GetPrefix() + __result;
			}
			return __result;
		}
	}
	[HarmonyPatch(typeof(TouchOfOrobas), "GetUpgradedStarterRelic")]
	internal class StarterUpgradePatches
	{
		[HarmonyPrefix]
		private static bool CustomStarterUpgrade(RelicModel starterRelic, ref RelicModel? __result)
		{
			if (starterRelic is CustomRelicModel customRelicModel)
			{
				__result = customRelicModel.GetUpgradeReplacement();
				return __result == null;
			}
			return true;
		}
	}
}
namespace BaseLib.Patches.Compatibility
{
	public class UnknownCharacterPatches
	{
		[HarmonyPatch(/*Could not decode attribute arguments.*/)]
		private static class IgnoreUnknownRun
		{
			[HarmonyPostfix]
			private static void SkipUnknownCharacter(SaveManager __instance, ref bool __result)
			{
				if (!__result)
				{
					return;
				}
				ReadSaveResult<SerializableRun> val = __instance.LoadRunSave();
				if (!val.Success || val.SaveData == null)
				{
					return;
				}
				foreach (SerializablePlayer player in val.SaveData.Players)
				{
					if (player.CharacterId == (ModelId)null || ModelDb.GetByIdOrNull<CharacterModel>(player.CharacterId) == null)
					{
						MainFile.Logger.Info($"Ignoring run with unknown character {player.CharacterId}", 1);
						__result = false;
						break;
					}
				}
			}
		}

		[HarmonyPatch(/*Could not decode attribute arguments.*/)]
		private static class IgnoreUnknownCoopRun
		{
			[HarmonyPostfix]
			private static void SkipUnknownCharacter(SaveManager __instance, ref bool __result)
			{
				//IL_0022: Unknown result type (might be due to invalid IL or missing references)
				//IL_0024: Unknown result type (might be due to invalid IL or missing references)
				if (!__result)
				{
					return;
				}
				PlatformType val = (PlatformType)((SteamInitializer.Initialized && !CommandLineHelper.HasArg("fastmp")) ? 1 : 0);
				ReadSaveResult<SerializableRun> val2 = __instance.LoadAndCanonicalizeMultiplayerRunSave(PlatformUtil.GetLocalPlayerId(val));
				if (!val2.Success || val2.SaveData == null)
				{
					return;
				}
				foreach (SerializablePlayer player in val2.SaveData.Players)
				{
					if (player.CharacterId == (ModelId)null || ModelDb.GetByIdOrNull<CharacterModel>(player.CharacterId) == null)
					{
						MainFile.Logger.Info($"Ignoring co-op run with unknown character {player.CharacterId}", 1);
						__result = false;
						break;
					}
				}
			}
		}

		[HarmonyPatch(typeof(ProgressSaveManager), "CheckFifteenBossesDefeatedEpoch")]
		private class SkipBossEpochCheck
		{
			[HarmonyPrefix]
			private static bool SkipIfUnsupported(Player localPlayer)
			{
				return !(localPlayer.Character is ICustomModel);
			}
		}

		[HarmonyPatch(typeof(ProgressSaveManager), "CheckFifteenElitesDefeatedEpoch")]
		private class SkipEliteEpochCheck
		{
			[HarmonyPrefix]
			private static bool SkipIfUnsupported(Player localPlayer)
			{
				return !(localPlayer.Character is ICustomModel);
			}
		}
	}
}
namespace BaseLib.Extensions
{
	public static class ActModelExtensions
	{
		public static int ActNumber(this ActModel actModel)
		{
			if (1 == 0)
			{
			}
			int result = ((actModel is Overgrowth || actModel is Underdocks) ? 1 : ((actModel is Hive) ? 2 : ((!(actModel is Glory)) ? (-1) : 3)));
			if (1 == 0)
			{
			}
			return result;
		}
	}
	public static class ControlExtensions
	{
		public static void DrawDebug(this Control item)
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			((CanvasItem)item).DrawRect(new Rect2(0f, 0f, item.Size), new Color(1f, 1f, 1f, 0.5f), true, -1f, false);
		}

		public static void DrawDebug(this Control artist, Control child)
		{
			//IL_0003: Unknown result type (might be due to invalid IL or missing references)
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			((CanvasItem)artist).DrawRect(new Rect2(child.Position, child.Size), new Color(1f, 1f, 1f, 0.5f), true, -1f, false);
		}
	}
	public static class DynamicVarExtensions
	{
		public static readonly SpireField<DynamicVar, Func<IHoverTip>> DynamicVarTips = new SpireField<DynamicVar, Func<IHoverTip>>(() => (Func<IHoverTip>?)null);

		public static decimal CalculateBlock(this DynamicVar var, Creature creature, ValueProp props, CardPlay? cardPlay = null, CardModel? cardSource = null)
		{
			//IL_004e: Unknown result type (might be due to invalid IL or missing references)
			decimal baseValue = var.BaseValue;
			if (!CombatManager.Instance.IsInProgress)
			{
				return baseValue;
			}
			if (CombatManager.Instance.IsEnding)
			{
				return baseValue;
			}
			CombatState combatState = creature.CombatState;
			if (combatState == null)
			{
				return baseValue;
			}
			IEnumerable<AbstractModel> enumerable = default(IEnumerable<AbstractModel>);
			baseValue = Hook.ModifyBlock(combatState, creature, baseValue, props, cardSource, cardPlay, ref enumerable);
			return Math.Max(baseValue, 0m);
		}

		public static DynamicVar WithTooltip(this DynamicVar var, string locTable = "static_hover_tips")
		{
			string key = ((object)var).GetType().GetPrefix() + StringHelper.Slugify(var.Name);
			DynamicVarTips[var] = delegate
			{
				//IL_0017: Unknown result type (might be due to invalid IL or missing references)
				//IL_001d: Expected O, but got Unknown
				//IL_0033: Unknown result type (might be due to invalid IL or missing references)
				//IL_0039: Expected O, but got Unknown
				//IL_0056: Unknown result type (might be due to invalid IL or missing references)
				LocString val = new LocString(locTable, key + ".title");
				LocString val2 = new LocString(locTable, key + ".description");
				val.Add(var);
				val2.Add(var);
				return (IHoverTip)(object)new HoverTip(val, val2, (Texture2D)null);
			};
			return var;
		}
	}
	public static class HarmonyExtensions
	{
		public static void PatchAsyncMoveNext(this Harmony harmony, MethodInfo asyncMethod, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, HarmonyMethod? transpiler = null, HarmonyMethod? finalizer = null)
		{
			MethodInfo method = asyncMethod.StateMachineType().GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
			harmony.Patch((MethodBase)method, prefix, postfix, transpiler, finalizer);
		}

		public static void PatchAsyncMoveNext(this Harmony harmony, MethodInfo asyncMethod, out Type stateMachineType, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, HarmonyMethod? transpiler = null, HarmonyMethod? finalizer = null)
		{
			AsyncStateMachineAttribute customAttribute = asyncMethod.GetCustomAttribute<AsyncStateMachineAttribute>();
			if (customAttribute == null)
			{
				throw new ArgumentException("MethodInfo " + GeneralExtensions.FullDescription((MethodBase)asyncMethod) + " passed to PatchAsync is not an async method");
			}
			stateMachineType = customAttribute.StateMachineType;
			MethodInfo method = stateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
			harmony.Patch((MethodBase)method, prefix, postfix, transpiler, finalizer);
		}
	}
	public static class IEnumerableExtensions
	{
		public static string AsReadable<T>(this IEnumerable<T> enumerable, string separator = ",")
		{
			return string.Join(separator, enumerable);
		}

		public static string NumberedLines<T>(this IEnumerable<T> enumerable)
		{
			StringBuilder stringBuilder = new StringBuilder();
			int num = 0;
			foreach (T item in enumerable)
			{
				stringBuilder.Append(num).Append(": ").Append(item)
					.AppendLine();
				num++;
			}
			return stringBuilder.ToString();
		}
	}
	public static class MethodInfoExtensions
	{
		public static Type StateMachineType(this MethodInfo methodInfo)
		{
			AsyncStateMachineAttribute customAttribute = methodInfo.GetCustomAttribute<AsyncStateMachineAttribute>();
			if (customAttribute == null)
			{
				throw new ArgumentException("MethodInfo " + GeneralExtensions.FullDescription((MethodBase)methodInfo) + " is not an async method");
			}
			return customAttribute.StateMachineType;
		}
	}
	public static class PublicPropExtensions
	{
		public static bool IsPoweredAttack_(this ValueProp props)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			if (((Enum)props).HasFlag((Enum)(object)(ValueProp)8))
			{
				return !((Enum)props).HasFlag((Enum)(object)(ValueProp)4);
			}
			return false;
		}

		public static bool IsPoweredCardOrMonsterMoveBlock_(this ValueProp props)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			if (((Enum)props).HasFlag((Enum)(object)(ValueProp)8))
			{
				return !((Enum)props).HasFlag((Enum)(object)(ValueProp)4);
			}
			return false;
		}

		public static bool IsCardOrMonsterMove_(this ValueProp props)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return ((Enum)props).HasFlag((Enum)(object)(ValueProp)8);
		}
	}
	public static class StringExtensions
	{
		public static string RemovePrefix(this string id)
		{
			int num = id.IndexOf('-') + 1;
			return id.Substring(num, id.Length - num);
		}
	}
	public static class TypeExtensions
	{
		private static Dictionary<Type, List<FieldInfo>> _declaredFields = new Dictionary<Type, List<FieldInfo>>();

		public static FieldInfo FindStateMachineField(this Type type, string originalFieldName)
		{
			string value = "<" + originalFieldName + ">";
			if (!_declaredFields.TryGetValue(type, out List<FieldInfo> value2))
			{
				value2 = AccessToolsExtensions.GetDeclaredFields(type);
			}
			foreach (FieldInfo item in value2)
			{
				if (item.Name.StartsWith(value))
				{
					return item;
				}
				if (item.Name.Equals(originalFieldName))
				{
					return item;
				}
			}
			throw new ArgumentException($"No matching field found in type {type} for name {originalFieldName}");
		}
	}
	public static class TypePrefix
	{
		public const char PrefixSplitChar = '-';

		public static string GetPrefix(this Type t)
		{
			if (t.Namespace == null)
			{
				return "";
			}
			int num = t.Namespace.IndexOf('.');
			if (num == -1)
			{
				num = t.Namespace.Length;
			}
			return $"{t.Namespace.Substring(0, num).ToUpperInvariant()}{45}";
		}

		public static string GetRootNamespace(this Type t)
		{
			if (t.Namespace == null)
			{
				return "";
			}
			int num = t.Namespace.IndexOf('.');
			if (num == -1)
			{
				num = t.Namespace.Length;
			}
			return t.Namespace.Substring(0, num);
		}
	}
}
namespace BaseLib.Config
{
	internal class BaseLibConfig : SimpleModConfig
	{
		public static bool Test { get; set; } = true;

		public static CardKeyword Keyword { get; set; } = (CardKeyword)0;
	}
	public abstract class ModConfig
	{
		private const string SettingsTheme = "res://themes/settings_screen_line_header.tres";

		private static readonly Font KreonNormal = PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_regular_shared.tres");

		private static readonly Font KreonBold = PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_bold_shared.tres");

		private readonly string _path;

		protected readonly List<PropertyInfo> ConfigProperties = new List<PropertyInfo>();

		private bool _fileActive = false;

		private static readonly FieldInfo DropdownNode = AccessTools.DeclaredField(typeof(NDropdownPositioner), "_dropdownNode");

		public event EventHandler? ConfigChanged;

		public ModConfig(string? filename = null)
		{
			_path = GetType().GetRootNamespace();
			if (_path == "")
			{
				_path = "Unknown";
			}
			_path = SpecialCharRegex().Replace(_path, "");
			filename = ((filename == null) ? _path : SpecialCharRegex().Replace(filename, ""));
			if (!filename.Contains('.'))
			{
				filename += ".cfg";
			}
			string text;
			if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
			{
				text = OS.GetUserDataDir();
			}
			else
			{
				text = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
				if (text == "")
				{
					text = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
				}
			}
			_path = Path.Combine(text, OperatingSystem.IsMacOS() ? "Library" : ".baselib", _path, filename);
			CheckConfigProperties();
			Init();
		}

		public bool HasSettings()
		{
			return ConfigProperties.Count > 0;
		}

		private void CheckConfigProperties()
		{
			Type type = GetType();
			ConfigProperties.Clear();
			PropertyInfo[] properties = type.GetProperties();
			foreach (PropertyInfo propertyInfo in properties)
			{
				if (propertyInfo.CanRead && propertyInfo.CanWrite)
				{
					MethodInfo? getMethod = propertyInfo.GetMethod;
					if ((object)getMethod != null && getMethod.IsStatic)
					{
						ConfigProperties.Add(propertyInfo);
					}
				}
			}
		}

		public abstract void SetupConfigUI(Control optionContainer);

		private void Init()
		{
			if (!File.Exists(_path))
			{
				Save();
			}
			else
			{
				Load();
			}
		}

		public void Changed()
		{
			this.ConfigChanged?.Invoke(this, EventArgs.Empty);
		}

		public async Task Save()
		{
			if (_fileActive)
			{
				return;
			}
			_fileActive = true;
			Dictionary<string, string> values = new Dictionary<string, string>();
			foreach (PropertyInfo property in ConfigProperties)
			{
				object value = property.GetValue(null);
				if (value != null)
				{
					values.Add(property.Name, value.ToString() ?? string.Empty);
				}
			}
			try
			{
				new FileInfo(_path).Directory?.Create();
				await using FileStream fileStream = File.Create(_path);
				await JsonSerializer.SerializeAsync((Stream)fileStream, values, (JsonSerializerOptions?)null, default(CancellationToken));
			}
			catch (Exception ex)
			{
				MainFile.Logger.Error("Failed to save config " + GetType().Name + ";", 1);
				MainFile.Logger.Error(ex.ToString(), 1);
			}
			_fileActive = false;
		}

		public async Task Load()
		{
			if (_fileActive || !File.Exists(_path))
			{
				return;
			}
			_fileActive = true;
			bool hadError = false;
			try
			{
				await using FileStream fileStream = File.OpenRead(_path);
				Dictionary<string, string> values = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(fileStream);
				if (values != null)
				{
					foreach (PropertyInfo property in ConfigProperties)
					{
						if (values.TryGetValue(property.Name, out string value))
						{
							TypeConverter converter = TypeDescriptor.GetConverter(property.PropertyType);
							try
							{
								object configVal = converter.ConvertFromString(value);
								if (configVal == null)
								{
									MainFile.Logger.Warn("Failed to load saved config value \"" + value + "\" for property " + property.Name, 1);
									hadError = true;
									continue;
								}
								object oldVal = property.GetValue(null);
								if (!configVal.Equals(oldVal))
								{
									property.SetValue(null, configVal);
								}
							}
							catch (Exception)
							{
								MainFile.Logger.Warn("Failed to load saved config value \"" + value + "\" for property " + property.Name, 1);
								hadError = true;
							}
						}
						value = null;
					}
					MainFile.Logger.Info("Loaded config " + GetType().Name + " successfully", 1);
				}
			}
			catch (Exception)
			{
				MainFile.Logger.Error("Failed to load config; most likely config types were changed.", 1);
				hadError = true;
			}
			_fileActive = false;
			if (hadError)
			{
				MainFile.Logger.Error("Error occured loading config; saving new config.", 1);
				await Save();
			}
		}

		private string GetLabelText(PropertyInfo property)
		{
			string prefix = GetType().GetPrefix();
			LocString ifExists = LocString.GetIfExists("settings_ui", prefix + StringHelper.Slugify(property.Name) + ".title");
			return (ifExists != null) ? ifExists.GetFormattedText() : property.Name;
		}

		public NConfigTickbox MakeToggleOption(Control parent, PropertyInfo property)
		{
			MarginContainer val = MakeOptionContainer(parent, "Toggle_" + property.Name, GetLabelText(property));
			NConfigTickbox nConfigTickbox = new NConfigTickbox().TransferAllNodes<NConfigTickbox>(SceneHelper.GetScenePath("screens/settings_tickbox"), Array.Empty<string>());
			nConfigTickbox.Initialize(this, property);
			((Node)val).AddChild((Node)(object)nConfigTickbox, false, (InternalMode)0);
			return nConfigTickbox;
		}

		public NDropdownPositioner MakeDropdownOption(Control parent, PropertyInfo property)
		{
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0053: Expected O, but got Unknown
			//IL_005f: Unknown result type (might be due to invalid IL or missing references)
			MarginContainer val = MakeOptionContainer(parent, "Dropdown_" + property.Name, GetLabelText(property));
			NConfigDropdown nConfigDropdown = new NConfigDropdown().TransferAllNodes<NConfigDropdown>(SceneHelper.GetScenePath("screens/settings_dropdown"), Array.Empty<string>());
			int currentIndex;
			List<NConfigDropdownItem.ConfigDropdownItem> items = MakeDropdownItems(property, out currentIndex);
			nConfigDropdown.SetItems(items, currentIndex);
			NDropdownPositioner val2 = new NDropdownPositioner();
			((Control)val2).SetCustomMinimumSize(new Vector2(320f, 64f));
			((Control)val2).FocusMode = (FocusModeEnum)2;
			((Control)val2).SizeFlagsHorizontal = (SizeFlags)8;
			((Control)val2).SizeFlagsVertical = (SizeFlags)1;
			DropdownNode.SetValue(val2, nConfigDropdown);
			((Node)val).GetParent().AddChild((Node)(object)nConfigDropdown, false, (InternalMode)0);
			((Node)val).AddChild((Node)(object)val2, false, (InternalMode)0);
			return val2;
		}

		private List<NConfigDropdownItem.ConfigDropdownItem> MakeDropdownItems(PropertyInfo property, out int currentIndex)
		{
			List<NConfigDropdownItem.ConfigDropdownItem> list = new List<NConfigDropdownItem.ConfigDropdownItem>();
			Type propertyType = property.PropertyType;
			string prefix = GetType().GetPrefix();
			object value = property.GetValue(null);
			int num = 0;
			currentIndex = 0;
			if (propertyType.IsEnum)
			{
				foreach (object value2 in propertyType.GetEnumValues())
				{
					if (value != null && value.Equals(value2))
					{
						currentIndex = num;
					}
					num++;
					LocString ifExists = LocString.GetIfExists("settings_ui", $"{prefix}{StringHelper.Slugify(property.Name)}.{value2}");
					list.Add(new NConfigDropdownItem.ConfigDropdownItem(((ifExists != null) ? ifExists.GetRawText() : null) ?? value2?.ToString() ?? "UNKNOWN", delegate
					{
						property.SetValue(null, value2);
					}));
				}
				return list;
			}
			throw new NotSupportedException("Dropdown only supports enum types currently");
		}

		protected static MarginContainer MakeOptionContainer(Control parent, string name, string labelText)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Expected O, but got Unknown
			//IL_0043: Unknown result type (might be due to invalid IL or missing references)
			//IL_0049: Expected O, but got Unknown
			//IL_007b: Unknown result type (might be due to invalid IL or missing references)
			MarginContainer val = new MarginContainer();
			((Node)val).Name = StringName.op_Implicit(name);
			((Control)val).AddThemeConstantOverride(StringName.op_Implicit("margin_left"), 12);
			((Control)val).AddThemeConstantOverride(StringName.op_Implicit("margin_right"), 12);
			((Control)val).MouseFilter = (MouseFilterEnum)2;
			MegaRichTextLabel val2 = new MegaRichTextLabel();
			((Node)val2).Name = StringName.op_Implicit("Label");
			((Control)val2).Theme = PreloadManager.Cache.GetAsset<Theme>("res://themes/settings_screen_line_header.tres");
			((Control)val2).SetCustomMinimumSize(new Vector2(0f, 64f));
			((Control)val2).AddThemeFontSizeOverride(StringName.op_Implicit("normal_font_size"), 20);
			((Control)val2).AddThemeFontSizeOverride(StringName.op_Implicit("bold_font_size"), 20);
			((Control)val2).AddThemeFontSizeOverride(StringName.op_Implicit("bold_italics_font_size"), 20);
			((Control)val2).AddThemeFontSizeOverride(StringName.op_Implicit("italics_font_size"), 20);
			((Control)val2).AddThemeFontSizeOverride(StringName.op_Implicit("mono_font_size"), 20);
			((Control)val2).AddThemeFontOverride(StringName.op_Implicit("normal_font"), KreonNormal);
			((Control)val2).AddThemeFontOverride(StringName.op_Implicit("bold_font"), KreonBold);
			((Control)val2).MouseFilter = (MouseFilterEnum)2;
			((RichTextLabel)val2).BbcodeEnabled = true;
			((RichTextLabel)val2).ScrollActive = false;
			((RichTextLabel)val2).VerticalAlignment = (VerticalAlignment)1;
			((Node)val).AddChild((Node)(object)val2, false, (InternalMode)0);
			((Node)val2).Owner = (Node)(object)val;
			val2.Text = labelText;
			((Node)parent).AddChild((Node)(object)val, false, (InternalMode)0);
			((Node)val).Owner = (Node)(object)parent;
			return val;
		}

		[GeneratedRegex("[^a-zA-Z0-9_]")]
		[GeneratedCode("System.Text.RegularExpressions.Generator", "9.0.14.6317")]
		private static Regex SpecialCharRegex()
		{
			return <RegexGenerator_g>FF2DA1E499BC624FD7D66A9DDABE991E526D88E11BAA752E1772425438E11A4C9__SpecialCharRegex_0.Instance;
		}
	}
	public static class ModConfigRegistry
	{
		private static readonly Dictionary<string, ModConfig> ModConfigs = new Dictionary<string, ModConfig>();

		public static void Register(string modId, ModConfig config)
		{
			if (config.HasSettings())
			{
				ModConfigs[modId] = config;
			}
		}

		public static ModConfig? Get(string? modId)
		{
			if (modId == null)
			{
				return null;
			}
			return ModConfigs.GetValueOrDefault(modId);
		}
	}
	public class SimpleModConfig : ModConfig
	{
		private static readonly Dictionary<Type, Func<ModConfig, Control, PropertyInfo, Control>> Generators = new Dictionary<Type, Func<ModConfig, Control, PropertyInfo, Control>>
		{
			{
				typeof(bool),
				(ModConfig cfg, Control control, PropertyInfo property) => (Control)(object)cfg.MakeToggleOption(control, property)
			},
			{
				typeof(Enum),
				(ModConfig cfg, Control control, PropertyInfo property) => (Control)(object)cfg.MakeDropdownOption(control, property)
			}
		};

		public override void SetupConfigUI(Control optionContainer)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Expected O, but got Unknown
			//IL_002a: Unknown result type (might be due to invalid IL or missing references)
			VBoxContainer val = new VBoxContainer();
			MainFile.Logger.Info("Setting up SimpleModConfig " + GetType().FullName, 1);
			((Control)val).Size = optionContainer.Size;
			((Control)val).AddThemeConstantOverride(StringName.op_Implicit("separation"), 8);
			((Node)optionContainer).AddChild((Node)(object)val, false, (InternalMode)0);
			Type type = null;
			Control val2 = null;
			try
			{
				foreach (PropertyInfo configProperty in ConfigProperties)
				{
					type = configProperty.PropertyType;
					Control val3 = val2;
					val2 = ((!type.IsEnum) ? Generators[type](this, (Control)(object)val, configProperty) : Generators[typeof(Enum)](this, (Control)(object)val, configProperty));
					if (val3 != null)
					{
						if (val2.FocusNeighborBottom == (NodePath)null)
						{
							MainFile.Logger.Info("NEIGHBOR DEFAULT NULL", 1);
						}
						else
						{
							MainFile.Logger.Info($"NEIGHBOR DEFAULT: {val2.FocusNeighborBottom}", 1);
						}
						NodePath pathTo = ((Node)val2).GetPathTo((Node)(object)val3, false);
						Control val4 = val2;
						if (val4.FocusNeighborLeft == null)
						{
							NodePath val5 = (val4.FocusNeighborLeft = pathTo);
						}
						val4 = val2;
						if (val4.FocusNeighborTop == null)
						{
							NodePath val5 = (val4.FocusNeighborTop = pathTo);
						}
						pathTo = ((Node)val3).GetPathTo((Node)(object)val2, false);
						val4 = val3;
						if (val4.FocusNeighborRight == null)
						{
							NodePath val5 = (val4.FocusNeighborRight = pathTo);
						}
						val4 = val3;
						if (val4.FocusNeighborBottom == null)
						{
							NodePath val5 = (val4.FocusNeighborBottom = pathTo);
						}
					}
				}
			}
			catch (KeyNotFoundException)
			{
				MainFile.Logger.Error("Attempted to construct SimpleModConfig with unsupported type " + type?.FullName, 1);
			}
		}
	}
}
namespace BaseLib.Config.UI
{
	[ScriptPath("res://Config/UI/NConfigButton.cs")]
	public class NConfigButton : NTopBarButton
	{
		public class MethodName : MethodName
		{
			public static readonly StringName Create = StringName.op_Implicit("Create");

			public static readonly StringName _Process = StringName.op_Implicit("_Process");

			public static readonly StringName OnRelease = StringName.op_Implicit("OnRelease");

			public static readonly StringName IsOpen = StringName.op_Implicit("IsOpen");
		}

		public class PropertyName : PropertyName
		{
			public static readonly StringName IsConfigOpen = StringName.op_Implicit("IsConfigOpen");
		}

		public class SignalName : SignalName
		{
		}

		public bool IsConfigOpen { get; set; }

		public static Control Create(string name, NModInfoContainer node)
		{
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0023: Expected O, but got Unknown
			//IL_003d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0043: Expected O, but got Unknown
			//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
			//IL_0104: Unknown result type (might be due to invalid IL or missing references)
			//IL_0113: Unknown result type (might be due to invalid IL or missing references)
			//IL_0118: Unknown result type (might be due to invalid IL or missing references)
			//IL_012e: Unknown result type (might be due to invalid IL or missing references)
			//IL_014e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0159: Unknown result type (might be due to invalid IL or missing references)
			//IL_016f: Unknown result type (might be due to invalid IL or missing references)
			NConfigButton nConfigButton = new NConfigButton();
			((Node)nConfigButton).Name = StringName.op_Implicit(name);
			((Control)nConfigButton).MouseFilter = (MouseFilterEnum)0;
			Control val = new Control();
			((Node)val).Name = StringName.op_Implicit("Control");
			val.MouseFilter = (MouseFilterEnum)2;
			TextureRect val2 = new TextureRect();
			((Node)val2).Name = StringName.op_Implicit("Icon");
			val2.ExpandMode = (ExpandModeEnum)1;
			val2.StretchMode = (StretchModeEnum)5;
			val2.Texture = (Texture2D)(object)PreloadManager.Cache.GetAsset<AtlasTexture>("res://images/atlases/ui_atlas.sprites/top_bar/top_bar_settings.tres");
			((CanvasItem)val2).Material = (Material)(object)ShaderUtils.GenerateHsv(1f, 1f, 0.9f);
			Vector2 val3 = default(Vector2);
			((Vector2)(ref val3))..ctor(64f, 64f);
			((Control)val2).CustomMinimumSize = val3;
			((Control)val2).Size = val3;
			((Control)val2).PivotOffset = ((Control)val2).Size * 0.5f;
			((Node)val).AddChild((Node)(object)val2, false, (InternalMode)0);
			((Node)val2).Owner = (Node)(object)val;
			((Node)nConfigButton).AddChild((Node)(object)val, false, (InternalMode)0);
			((Node)val).Owner = (Node)(object)nConfigButton;
			val.Size = ((Control)val2).Size;
			((Control)nConfigButton).Size = val.Size + new Vector2(16f, 16f);
			val.Position = new Vector2(8f, 8f);
			((Node)node).AddChild((Node)(object)nConfigButton, false, (InternalMode)0);
			((Node)nConfigButton).Owner = (Node)(object)node;
			((Control)nConfigButton).Position = new Vector2(((Control)node).Size.X - (((Control)nConfigButton).Size.X + 8f), 8f);
			((CanvasItem)nConfigButton).Hide();
			return (Control)(object)nConfigButton;
		}

		public override void _Process(double delta)
		{
			((Node)this)._Process(delta);
			if (IsConfigOpen)
			{
				Control icon = base._icon;
				icon.Rotation += (float)delta;
			}
		}

		protected override void OnRelease()
		{
			//IL_00d6: Unknown result type (might be due to invalid IL or missing references)
			((NTopBarButton)this).OnRelease();
			if (((NTopBarButton)this).IsOpen())
			{
				IsConfigOpen = false;
			}
			else
			{
				Mod currentMod = ModConfigFillPatch.CurrentMod;
				if (currentMod == null)
				{
					return;
				}
				ModConfig modConfig = ModConfigRegistry.Get(currentMod.manifest?.id);
				if (modConfig != null)
				{
					Node parent = ((Node)this).GetParent();
					while (parent != null && !(parent is NModdingScreen))
					{
						parent = parent.GetParent();
					}
					NModdingScreen val = (NModdingScreen)(object)((parent is NModdingScreen) ? parent : null);
					if (val == null)
					{
						return;
					}
					NModConfigPopup.ShowModConfig(val, modConfig, this);
					IsConfigOpen = true;
				}
			}
			((NTopBarButton)this).UpdateScreenOpen();
			ShaderMaterial hsv = base._hsv;
			if (hsv != null)
			{
				hsv.SetShaderParameter(StringName.op_Implicit("v"), Variant.op_Implicit(0.9f));
			}
		}

		protected override bool IsOpen()
		{
			return IsConfigOpen;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<MethodInfo> GetGodotMethodList()
		{
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_0034: Expected O, but got Unknown
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0053: Unknown result type (might be due to invalid IL or missing references)
			//IL_007a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0085: Expected O, but got Unknown
			//IL_0080: Unknown result type (might be due to invalid IL or missing references)
			//IL_008c: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
			//IL_0109: Unknown result type (might be due to invalid IL or missing references)
			//IL_0112: Unknown result type (might be due to invalid IL or missing references)
			//IL_0139: Unknown result type (might be due to invalid IL or missing references)
			//IL_0142: Unknown result type (might be due to invalid IL or missing references)
			List<MethodInfo> list = new List<MethodInfo>(4);
			list.Add(new MethodInfo(MethodName.Create, new PropertyInfo((Type)24, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, new StringName("Control"), false), (MethodFlags)33, new List<PropertyInfo>
			{
				new PropertyInfo((Type)4, StringName.op_Implicit("name"), (PropertyHint)0, "", (PropertyUsageFlags)6, false),
				new PropertyInfo((Type)24, StringName.op_Implicit("node"), (PropertyHint)0, "", (PropertyUsageFlags)6, new StringName("Control"), false)
			}, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName._Process, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, new List<PropertyInfo>
			{
				new PropertyInfo((Type)3, StringName.op_Implicit("delta"), (PropertyHint)0, "", (PropertyUsageFlags)6, false)
			}, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName.OnRelease, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName.IsOpen, new PropertyInfo((Type)1, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			return list;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			//IL_0043: Unknown result type (might be due to invalid IL or missing references)
			//IL_0048: Unknown result type (might be due to invalid IL or missing references)
			//IL_0088: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
			//IL_0101: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
			if ((ref method) == MethodName.Create && ((NativeVariantPtrArgs)(ref args)).Count == 2)
			{
				Control val = Create(VariantUtils.ConvertTo<string>(ref ((NativeVariantPtrArgs)(ref args))[0]), VariantUtils.ConvertTo<NModInfoContainer>(ref ((NativeVariantPtrArgs)(ref args))[1]));
				ret = VariantUtils.CreateFrom<Control>(ref val);
				return true;
			}
			if ((ref method) == MethodName._Process && ((NativeVariantPtrArgs)(ref args)).Count == 1)
			{
				((Node)this)._Process(VariantUtils.ConvertTo<double>(ref ((NativeVariantPtrArgs)(ref args))[0]));
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName.OnRelease && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				((NClickableControl)this).OnRelease();
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName.IsOpen && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				bool flag = ((NTopBarButton)this).IsOpen();
				ret = VariantUtils.CreateFrom<bool>(ref flag);
				return true;
			}
			return ((NTopBarButton)this).InvokeGodotClassMethod(ref method, args, ref ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static bool InvokeGodotClassStaticMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			//IL_0052: Unknown result type (might be due to invalid IL or missing references)
			//IL_0043: Unknown result type (might be due to invalid IL or missing references)
			//IL_0048: Unknown result type (might be due to invalid IL or missing references)
			if ((ref method) == MethodName.Create && ((NativeVariantPtrArgs)(ref args)).Count == 2)
			{
				Control val = Create(VariantUtils.ConvertTo<string>(ref ((NativeVariantPtrArgs)(ref args))[0]), VariantUtils.ConvertTo<NModInfoContainer>(ref ((NativeVariantPtrArgs)(ref args))[1]));
				ret = VariantUtils.CreateFrom<Control>(ref val);
				return true;
			}
			ret = default(godot_variant);
			return false;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool HasGodotClassMethod(in godot_string_name method)
		{
			if ((ref method) == MethodName.Create)
			{
				return true;
			}
			if ((ref method) == MethodName._Process)
			{
				return true;
			}
			if ((ref method) == MethodName.OnRelease)
			{
				return true;
			}
			if ((ref method) == MethodName.IsOpen)
			{
				return true;
			}
			return ((NTopBarButton)this).HasGodotClassMethod(ref method);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool SetGodotClassPropertyValue(in godot_string_name name, in godot_variant value)
		{
			if ((ref name) == PropertyName.IsConfigOpen)
			{
				IsConfigOpen = VariantUtils.ConvertTo<bool>(ref value);
				return true;
			}
			return ((NTopBarButton)this).SetGodotClassPropertyValue(ref name, ref value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool GetGodotClassPropertyValue(in godot_string_name name, out godot_variant value)
		{
			//IL_001b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0020: Unknown result type (might be due to invalid IL or missing references)
			if ((ref name) == PropertyName.IsConfigOpen)
			{
				bool isConfigOpen = IsConfigOpen;
				value = VariantUtils.CreateFrom<bool>(ref isConfigOpen);
				return true;
			}
			return ((NTopBarButton)this).GetGodotClassPropertyValue(ref name, ref value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<PropertyInfo> GetGodotPropertyList()
		{
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			List<PropertyInfo> list = new List<PropertyInfo>();
			list.Add(new PropertyInfo((Type)1, PropertyName.IsConfigOpen, (PropertyHint)0, "", (PropertyUsageFlags)4096, false));
			return list;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			((NTopBarButton)this).SaveGodotObjectData(info);
			StringName isConfigOpen = PropertyName.IsConfigOpen;
			bool isConfigOpen2 = IsConfigOpen;
			info.AddProperty(isConfigOpen, Variant.From<bool>(ref isConfigOpen2));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			((NTopBarButton)this).RestoreGodotObjectData(info);
			Variant val = default(Variant);
			if (info.TryGetProperty(PropertyName.IsConfigOpen, ref val))
			{
				IsConfigOpen = ((Variant)(ref val)).As<bool>();
			}
		}
	}
	[ScriptPath("res://Config/UI/NConfigDropdown.cs")]
	public class NConfigDropdown : NSettingsDropdown
	{
		public class MethodName : MethodName
		{
			public static readonly StringName _Ready = StringName.op_Implicit("_Ready");

			public static readonly StringName OnDropdownItemSelected = StringName.op_Implicit("OnDropdownItemSelected");
		}

		public class PropertyName : PropertyName
		{
			public static readonly StringName _currentDisplayIndex = StringName.op_Implicit("_currentDisplayIndex");
		}

		public class SignalName : SignalName
		{
		}

		private List<NConfigDropdownItem.ConfigDropdownItem>? _items;

		private int _currentDisplayIndex = -1;

		public NConfigDropdown()
		{
			//IL_001a: Unknown result type (might be due to invalid IL or missing references)
			((Control)this).SetCustomMinimumSize(new Vector2(324f, 64f));
			((Control)this).SizeFlagsHorizontal = (SizeFlags)8;
			((Control)this).SizeFlagsVertical = (SizeFlags)1;
			((Control)this).FocusMode = (FocusModeEnum)2;
		}

		public void SetItems(List<NConfigDropdownItem.ConfigDropdownItem> items, int initialIndex)
		{
			_items = items;
			_currentDisplayIndex = initialIndex;
		}

		public override void _Ready()
		{
			//IL_005d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0063: Unknown result type (might be due to invalid IL or missing references)
			((NClickableControl)this).ConnectSignals();
			((NDropdown)this).ClearDropdownItems();
			if (_items == null)
			{
				throw new Exception("Created config dropdown without setting items");
			}
			for (int i = 0; i < _items.Count; i++)
			{
				NConfigDropdownItem nConfigDropdownItem = NConfigDropdownItem.Create(_items[i]);
				GodotTreeExtensions.AddChildSafely((Node)(object)((NDropdown)this)._dropdownItems, (Node)(object)nConfigDropdownItem);
				((GodotObject)nConfigDropdownItem).Connect(SignalName.Selected, Callable.From<NDropdownItem>((Action<NDropdownItem>)OnDropdownItemSelected), 0u);
				nConfigDropdownItem.Init(i);
				if (i == _currentDisplayIndex)
				{
					((NDropdown)this)._currentOptionLabel.SetTextAutoSize(nConfigDropdownItem.Data.Text);
				}
			}
			((Node)((NDropdown)this)._dropdownItems).GetParent<NDropdownContainer>().RefreshLayout();
		}

		private void OnDropdownItemSelected(NDropdownItem nDropdownItem)
		{
			if (nDropdownItem is NConfigDropdownItem nConfigDropdownItem && nConfigDropdownItem.DisplayIndex != _currentDisplayIndex)
			{
				((NDropdown)this).CloseDropdown();
				((NDropdown)this)._currentOptionLabel.SetTextAutoSize(nConfigDropdownItem.Data.Text);
				nConfigDropdownItem.Data.OnSet();
			}
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<MethodInfo> GetGodotMethodList()
		{
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			//IL_002d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0054: Unknown result type (might be due to invalid IL or missing references)
			//IL_007c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0087: Expected O, but got Unknown
			//IL_0082: Unknown result type (might be due to invalid IL or missing references)
			//IL_008e: Unknown result type (might be due to invalid IL or missing references)
			List<MethodInfo> list = new List<MethodInfo>(2);
			list.Add(new MethodInfo(MethodName._Ready, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName.OnDropdownItemSelected, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, new List<PropertyInfo>
			{
				new PropertyInfo((Type)24, StringName.op_Implicit("nDropdownItem"), (PropertyHint)0, "", (PropertyUsageFlags)6, new StringName("Control"), false)
			}, (List<Variant>)null));
			return list;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_0072: Unknown result type (might be due to invalid IL or missing references)
			//IL_0066: Unknown result type (might be due to invalid IL or missing references)
			if ((ref method) == MethodName._Ready && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				((Node)this)._Ready();
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName.OnDropdownItemSelected && ((NativeVariantPtrArgs)(ref args)).Count == 1)
			{
				OnDropdownItemSelected(VariantUtils.ConvertTo<NDropdownItem>(ref ((NativeVariantPtrArgs)(ref args))[0]));
				ret = default(godot_variant);
				return true;
			}
			return ((NSettingsDropdown)this).InvokeGodotClassMethod(ref method, args, ref ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool HasGodotClassMethod(in godot_string_name method)
		{
			if ((ref method) == MethodName._Ready)
			{
				return true;
			}
			if ((ref method) == MethodName.OnDropdownItemSelected)
			{
				return true;
			}
			return ((NSettingsDropdown)this).HasGodotClassMethod(ref method);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool SetGodotClassPropertyValue(in godot_string_name name, in godot_variant value)
		{
			if ((ref name) == PropertyName._currentDisplayIndex)
			{
				_currentDisplayIndex = VariantUtils.ConvertTo<int>(ref value);
				return true;
			}
			return ((NSettingsDropdown)this).SetGodotClassPropertyValue(ref name, ref value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool GetGodotClassPropertyValue(in godot_string_name name, out godot_variant value)
		{
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			if ((ref name) == PropertyName._currentDisplayIndex)
			{
				value = VariantUtils.CreateFrom<int>(ref _currentDisplayIndex);
				return true;
			}
			return ((NSettingsDropdown)this).GetGodotClassPropertyValue(ref name, ref value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<PropertyInfo> GetGodotPropertyList()
		{
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			List<PropertyInfo> list = new List<PropertyInfo>();
			list.Add(new PropertyInfo((Type)2, PropertyName._currentDisplayIndex, (PropertyHint)0, "", (PropertyUsageFlags)4096, false));
			return list;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			((NSettingsDropdown)this).SaveGodotObjectData(info);
			info.AddProperty(PropertyName._currentDisplayIndex, Variant.From<int>(ref _currentDisplayIndex));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			((NSettingsDropdown)this).RestoreGodotObjectData(info);
			Variant val = default(Variant);
			if (info.TryGetProperty(PropertyName._currentDisplayIndex, ref val))
			{
				_currentDisplayIndex = ((Variant)(ref val)).As<int>();
			}
		}
	}
	[ScriptPath("res://Config/UI/NConfigDropdownItem.cs")]
	public class NConfigDropdownItem : NDropdownItem
	{
		public class ConfigDropdownItem(string text, Action onSet)
		{
			public readonly string Text = text;

			public readonly Action OnSet = onSet;
		}

		public class MethodName : MethodName
		{
			public static readonly StringName Init = StringName.op_Implicit("Init");
		}

		public class PropertyName : PropertyName
		{
			public static readonly StringName DisplayIndex = StringName.op_Implicit("DisplayIndex");
		}

		public class SignalName : SignalName
		{
		}

		private static readonly string BaseScenePath = SceneHelper.GetScenePath("ui/dropdown_item");

		public required ConfigDropdownItem Data;

		public int DisplayIndex;

		public static NConfigDropdownItem Create(ConfigDropdownItem data)
		{
			//IL_0019: Unknown result type (might be due to invalid IL or missing references)
			NConfigDropdownItem nConfigDropdownItem = new NConfigDropdownItem
			{
				Data = data
			};
			((Control)nConfigDropdownItem).SetCustomMinimumSize(new Vector2(288f, 44f));
			((Control)nConfigDropdownItem).MouseFilter = (MouseFilterEnum)1;
			nConfigDropdownItem.TransferAllNodes<NConfigDropdownItem>(BaseScenePath, Array.Empty<string>());
			return nConfigDropdownItem;
		}

		private NConfigDropdownItem()
		{
		}

		public void Init(int setIndex)
		{
			DisplayIndex = setIndex;
			base._label.SetTextAutoSize(Data.text);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<MethodInfo> GetGodotMethodList()
		{
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			//IL_0047: Unknown result type (might be due to invalid IL or missing references)
			//IL_0053: Unknown result type (might be due to invalid IL or missing references)
			List<MethodInfo> list = new List<MethodInfo>(1);
			list.Add(new MethodInfo(MethodName.Init, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, new List<PropertyInfo>
			{
				new PropertyInfo((Type)2, StringName.op_Implicit("setIndex"), (PropertyHint)0, "", (PropertyUsageFlags)6, false)
			}, (List<Variant>)null));
			return list;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			//IL_0041: Unknown result type (might be due to invalid IL or missing references)
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			if ((ref method) == MethodName.Init && ((NativeVariantPtrArgs)(ref args)).Count == 1)
			{
				Init(VariantUtils.ConvertTo<int>(ref ((NativeVariantPtrArgs)(ref args))[0]));
				ret = default(godot_variant);
				return true;
			}
			return ((NDropdownItem)this).InvokeGodotClassMethod(ref method, args, ref ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool HasGodotClassMethod(in godot_string_name method)
		{
			if ((ref method) == MethodName.Init)
			{
				return true;
			}
			return ((NDropdownItem)this).HasGodotClassMethod(ref method);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool SetGodotClassPropertyValue(in godot_string_name name, in godot_variant value)
		{
			if ((ref name) == PropertyName.DisplayIndex)
			{
				DisplayIndex = VariantUtils.ConvertTo<int>(ref value);
				return true;
			}
			return ((NDropdownItem)this).SetGodotClassPropertyValue(ref name, ref value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool GetGodotClassPropertyValue(in godot_string_name name, out godot_variant value)
		{
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			if ((ref name) == PropertyName.DisplayIndex)
			{
				value = VariantUtils.CreateFrom<int>(ref DisplayIndex);
				return true;
			}
			return ((NDropdownItem)this).GetGodotClassPropertyValue(ref name, ref value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<PropertyInfo> GetGodotPropertyList()
		{
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			List<PropertyInfo> list = new List<PropertyInfo>();
			list.Add(new PropertyInfo((Type)2, PropertyName.DisplayIndex, (PropertyHint)0, "", (PropertyUsageFlags)4096, false));
			return list;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			((NDropdownItem)this).SaveGodotObjectData(info);
			info.AddProperty(PropertyName.DisplayIndex, Variant.From<int>(ref DisplayIndex));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			((NDropdownItem)this).RestoreGodotObjectData(info);
			Variant val = default(Variant);
			if (info.TryGetProperty(PropertyName.DisplayIndex, ref val))
			{
				DisplayIndex = ((Variant)(ref val)).As<int>();
			}
		}
	}
	[ScriptPath("res://Config/UI/NConfigSlider.cs")]
	public class NConfigSlider : NSettingsSlider
	{
		public class MethodName : MethodName
		{
			public static readonly StringName _Ready = StringName.op_Implicit("_Ready");

			public static readonly StringName ConnectSignals = StringName.op_Implicit("ConnectSignals");

			public static readonly StringName OnValueChanged = StringName.op_Implicit("OnValueChanged");

			public static readonly StringName OnFocus = StringName.op_Implicit("OnFocus");

			public static readonly StringName OnUnfocus = StringName.op_Implicit("OnUnfocus");
		}

		public class PropertyName : PropertyName
		{
			public static readonly StringName _valueLabel = StringName.op_Implicit("_valueLabel");

			public static readonly StringName _selectionReticle = StringName.op_Implicit("_selectionReticle");
		}

		public class SignalName : SignalName
		{
		}

		private MegaLabel? _valueLabel;

		private NSelectionReticle? _selectionReticle;

		private static readonly FieldInfo ValueLabel = AccessTools.DeclaredField(typeof(NSettingsSlider), "_valueLabel");

		private static readonly FieldInfo SelectionReticle = AccessTools.DeclaredField(typeof(NSettingsSlider), "_selectionReticle");

		public NConfigSlider()
		{
			//IL_0013: Unknown result type (might be due to invalid IL or missing references)
			((Control)this).SetCustomMinimumSize(new Vector2(324f, 64f));
		}

		public override void _Ready()
		{
			((NSettingsSlider)this).ConnectSignals();
		}

		protected override void ConnectSignals()
		{
			//IL_0079: Unknown result type (might be due to invalid IL or missing references)
			//IL_007f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0097: Unknown result type (might be due to invalid IL or missing references)
			//IL_009d: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
			base._slider = ((Node)this).GetNode<NSlider>(NodePath.op_Implicit("Slider"));
			_valueLabel = ((Node)this).GetNode<MegaLabel>(NodePath.op_Implicit("SliderValue"));
			_selectionReticle = ((Node)this).GetNode<NSelectionReticle>(NodePath.op_Implicit("SelectionReticle"));
			ValueLabel.SetValue(this, _valueLabel);
			SelectionReticle.SetValue(this, _selectionReticle);
			((GodotObject)this).Connect(SignalName.FocusEntered, Callable.From((Action)OnFocus), 0u);
			((GodotObject)this).Connect(SignalName.FocusExited, Callable.From((Action)OnUnfocus), 0u);
			((GodotObject)base._slider).Connect(SignalName.ValueChanged, Callable.From<double>((Action<double>)OnValueChanged), 0u);
			((GodotObject)base._slider).Connect(SignalName.ValueChanged, Callable.From<double>((Action<double>)OnValueChanged), 0u);
			MegaLabel? valueLabel = _valueLabel;
			if (valueLabel != null)
			{
				valueLabel.SetTextAutoSize($"{((Range)base._slider).Value}%");
			}
		}

		private void OnValueChanged(double value)
		{
			float num = (float)value * 0.01f;
			NAudioManager instance = NAudioManager.Instance;
			if (instance != null)
			{
				instance.SetMasterVol(num);
			}
			NDebugAudioManager instance2 = NDebugAudioManager.Instance;
			if (instance2 != null)
			{
				instance2.SetMasterAudioVolume(num);
			}
		}

		private void OnFocus()
		{
			NControllerManager instance = NControllerManager.Instance;
			if (instance != null && instance.IsUsingController)
			{
				_selectionReticle.OnSelect();
			}
		}

		private void OnUnfocus()
		{
			_selectionReticle.OnDeselect();
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<MethodInfo> GetGodotMethodList()
		{
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			//IL_002d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0054: Unknown result type (might be due to invalid IL or missing references)
			//IL_005d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0084: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00da: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
			//IL_010a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0113: Unknown result type (might be due to invalid IL or missing references)
			List<MethodInfo> list = new List<MethodInfo>(5);
			list.Add(new MethodInfo(MethodName._Ready, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName.ConnectSignals, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName.OnValueChanged, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, new List<PropertyInfo>
			{
				new PropertyInfo((Type)3, StringName.op_Implicit("value"), (PropertyHint)0, "", (PropertyUsageFlags)6, false)
			}, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName.OnFocus, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName.OnUnfocus, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			return list;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_005c: Unknown result type (might be due to invalid IL or missing references)
			//IL_009d: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
			//IL_010f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0103: Unknown result type (might be due to invalid IL or missing references)
			if ((ref method) == MethodName._Ready && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				((Node)this)._Ready();
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName.ConnectSignals && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				((NSettingsSlider)this).ConnectSignals();
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName.OnValueChanged && ((NativeVariantPtrArgs)(ref args)).Count == 1)
			{
				OnValueChanged(VariantUtils.ConvertTo<double>(ref ((NativeVariantPtrArgs)(ref args))[0]));
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName.OnFocus && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				OnFocus();
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName.OnUnfocus && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				OnUnfocus();
				ret = default(godot_variant);
				return true;
			}
			return ((NSettingsSlider)this).InvokeGodotClassMethod(ref method, args, ref ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool HasGodotClassMethod(in godot_string_name method)
		{
			if ((ref method) == MethodName._Ready)
			{
				return true;
			}
			if ((ref method) == MethodName.ConnectSignals)
			{
				return true;
			}
			if ((ref method) == MethodName.OnValueChanged)
			{
				return true;
			}
			if ((ref method) == MethodName.OnFocus)
			{
				return true;
			}
			if ((ref method) == MethodName.OnUnfocus)
			{
				return true;
			}
			return ((NSettingsSlider)this).HasGodotClassMethod(ref method);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool SetGodotClassPropertyValue(in godot_string_name name, in godot_variant value)
		{
			if ((ref name) == PropertyName._valueLabel)
			{
				_valueLabel = VariantUtils.ConvertTo<MegaLabel>(ref value);
				return true;
			}
			if ((ref name) == PropertyName._selectionReticle)
			{
				_selectionReticle = VariantUtils.ConvertTo<NSelectionReticle>(ref value);
				return true;
			}
			return ((NSettingsSlider)this).SetGodotClassPropertyValue(ref name, ref value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool GetGodotClassPropertyValue(in godot_string_name name, out godot_variant value)
		{
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			//IL_003d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0042: Unknown result type (might be due to invalid IL or missing references)
			if ((ref name) == PropertyName._valueLabel)
			{
				value = VariantUtils.CreateFrom<MegaLabel>(ref _valueLabel);
				return true;
			}
			if ((ref name) == PropertyName._selectionReticle)
			{
				value = VariantUtils.CreateFrom<NSelectionReticle>(ref _selectionReticle);
				return true;
			}
			return ((NSettingsSlider)this).GetGodotClassPropertyValue(ref name, ref value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<PropertyInfo> GetGodotPropertyList()
		{
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0040: Unknown result type (might be due to invalid IL or missing references)
			List<PropertyInfo> list = new List<PropertyInfo>();
			list.Add(new PropertyInfo((Type)24, PropertyName._valueLabel, (PropertyHint)0, "", (PropertyUsageFlags)4096, false));
			list.Add(new PropertyInfo((Type)24, PropertyName._selectionReticle, (PropertyHint)0, "", (PropertyUsageFlags)4096, false));
			return list;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			((NSettingsSlider)this).SaveGodotObjectData(info);
			info.AddProperty(PropertyName._valueLabel, Variant.From<MegaLabel>(ref _valueLabel));
			info.AddProperty(PropertyName._selectionReticle, Variant.From<NSelectionReticle>(ref _selectionReticle));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			((NSettingsSlider)this).RestoreGodotObjectData(info);
			Variant val = default(Variant);
			if (info.TryGetProperty(PropertyName._valueLabel, ref val))
			{
				_valueLabel = ((Variant)(ref val)).As<MegaLabel>();
			}
			Variant val2 = default(Variant);
			if (info.TryGetProperty(PropertyName._selectionReticle, ref val2))
			{
				_selectionReticle = ((Variant)(ref val2)).As<NSelectionReticle>();
			}
		}
	}
	[ScriptPath("res://Config/UI/NConfigTickbox.cs")]
	public class NConfigTickbox : NSettingsTickbox
	{
		public class MethodName : MethodName
		{
			public static readonly StringName _Ready = StringName.op_Implicit("_Ready");

			public static readonly StringName SetFromProperty = StringName.op_Implicit("SetFromProperty");

			public static readonly StringName OnTick = StringName.op_Implicit("OnTick");

			public static readonly StringName OnUntick = StringName.op_Implicit("OnUntick");
		}

		public class PropertyName : PropertyName
		{
		}

		public class SignalName : SignalName
		{
		}

		private ModConfig? _config;

		private PropertyInfo? _property;

		public NConfigTickbox()
		{
			//IL_0013: Unknown result type (might be due to invalid IL or missing references)
			((Control)this).SetCustomMinimumSize(new Vector2(320f, 64f));
			((Control)this).SizeFlagsHorizontal = (SizeFlags)8;
			((Control)this).SizeFlagsVertical = (SizeFlags)1;
		}

		public override void _Ready()
		{
			if (_property == null)
			{
				throw new Exception("NConfigTickbox added to tree without an assigned property");
			}
			((NClickableControl)this).ConnectSignals();
			SetFromProperty();
		}

		public void Initialize(ModConfig modConfig, PropertyInfo property)
		{
			if (property.PropertyType != typeof(bool))
			{
				throw new ArgumentException("Attempted to assign NConfigTickbox a non-bool property");
			}
			_config = modConfig;
			_property = property;
		}

		private void SetFromProperty()
		{
			((NTickbox)this).IsTicked = (bool?)_property.GetValue(null) == true;
		}

		protected override void OnTick()
		{
			_property?.SetValue(null, true);
			_config?.Changed();
		}

		protected override void OnUntick()
		{
			_property?.SetValue(null, false);
			_config?.Changed();
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<MethodInfo> GetGodotMethodList()
		{
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			//IL_002d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0054: Unknown result type (might be due to invalid IL or missing references)
			//IL_005d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0084: Unknown result type (might be due to invalid IL or missing references)
			//IL_008d: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
			List<MethodInfo> list = new List<MethodInfo>(4);
			list.Add(new MethodInfo(MethodName._Ready, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName.SetFromProperty, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName.OnTick, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName.OnUntick, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			return list;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_005c: Unknown result type (might be due to invalid IL or missing references)
			//IL_008d: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
			if ((ref method) == MethodName._Ready && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				((Node)this)._Ready();
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName.SetFromProperty && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				SetFromProperty();
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName.OnTick && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				((NTickbox)this).OnTick();
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName.OnUntick && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				((NTickbox)this).OnUntick();
				ret = default(godot_variant);
				return true;
			}
			return ((NSettingsTickbox)this).InvokeGodotClassMethod(ref method, args, ref ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool HasGodotClassMethod(in godot_string_name method)
		{
			if ((ref method) == MethodName._Ready)
			{
				return true;
			}
			if ((ref method) == MethodName.SetFromProperty)
			{
				return true;
			}
			if ((ref method) == MethodName.OnTick)
			{
				return true;
			}
			if ((ref method) == MethodName.OnUntick)
			{
				return true;
			}
			return ((NSettingsTickbox)this).HasGodotClassMethod(ref method);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			((NSettingsTickbox)this).SaveGodotObjectData(info);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			((NSettingsTickbox)this).RestoreGodotObjectData(info);
		}
	}
	[ScriptPath("res://Config/UI/NModConfigPopup.cs")]
	public class NModConfigPopup : NClickableControl
	{
		[HarmonyPatch(typeof(NModdingScreen), "_Ready")]
		private static class NModConfigPatch
		{
			[HarmonyPostfix]
			private static void PrepPopup(NModdingScreen __instance)
			{
				ConfigPopup.Get(__instance);
			}
		}

		public class MethodName : MethodName
		{
			public static readonly StringName Create = StringName.op_Implicit("Create");

			public static readonly StringName _Ready = StringName.op_Implicit("_Ready");

			public static readonly StringName ClosePopup = StringName.op_Implicit("ClosePopup");

			public static readonly StringName _Process = StringName.op_Implicit("_Process");

			public static readonly StringName OnRelease = StringName.op_Implicit("OnRelease");

			public static readonly StringName SaveCurrentConfig = StringName.op_Implicit("SaveCurrentConfig");
		}

		public class PropertyName : PropertyName
		{
			public static readonly StringName _optionScrollContainer = StringName.op_Implicit("_optionScrollContainer");

			public static readonly StringName _optionContainer = StringName.op_Implicit("_optionContainer");

			public static readonly StringName _opener = StringName.op_Implicit("_opener");

			public static readonly StringName _saveTimer = StringName.op_Implicit("_saveTimer");
		}

		public class SignalName : SignalName
		{
		}

		public static readonly SpireField<NModdingScreen, NModConfigPopup> ConfigPopup = new SpireField<NModdingScreen, NModConfigPopup>(Create);

		private ModConfig? _currentConfig;

		private NScrollableContainer _optionScrollContainer;

		private Control _optionContainer;

		private NConfigButton? _opener;

		private double _saveTimer;

		private const double AutosaveDelay = 5.0;

		public static void ShowModConfig(NModdingScreen screen, ModConfig config, NConfigButton opener)
		{
			NModConfigPopup nModConfigPopup = ConfigPopup.Get(screen);
			if (nModConfigPopup == null)
			{
				opener.IsConfigOpen = false;
			}
			else
			{
				nModConfigPopup?.ShowMod(config, opener);
			}
		}

		public static NModConfigPopup Create(NModdingScreen screen)
		{
			return new NModConfigPopup((Control)(object)screen);
		}

		private NModConfigPopup(Control futureParent)
		{
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			//IL_003b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0045: Expected O, but got Unknown
			//IL_005f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0075: Unknown result type (might be due to invalid IL or missing references)
			//IL_0085: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
			//IL_010a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0114: Unknown result type (might be due to invalid IL or missing references)
			//IL_011f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0129: Unknown result type (might be due to invalid IL or missing references)
			//IL_012e: Unknown result type (might be due to invalid IL or missing references)
			//IL_019c: Unknown result type (might be due to invalid IL or missing references)
			//IL_01a6: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
			//IL_01cd: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d8: Unknown result type (might be due to invalid IL or missing references)
			//IL_01de: Expected O, but got Unknown
			//IL_01f6: Unknown result type (might be due to invalid IL or missing references)
			//IL_0231: Unknown result type (might be due to invalid IL or missing references)
			//IL_023b: Expected O, but got Unknown
			//IL_0258: Unknown result type (might be due to invalid IL or missing references)
			_saveTimer = -1.0;
			((Control)this).Size = futureParent.Size;
			((Control)this).MouseFilter = (MouseFilterEnum)2;
			_optionScrollContainer = new NScrollableContainer();
			((Control)_optionScrollContainer).MouseFilter = (MouseFilterEnum)0;
			((Control)_optionScrollContainer).Size = new Vector2(Math.Max(480f, ((Control)this).Size.X * 0.5f), ((Control)this).Size.Y * 0.75f);
			Color back = new Color(0.1f, 0.1f, 0.1f, 0.85f);
			Color border = new Color(0.9372549f, 66f / 85f, 31f / 85f, 1f);
			((CanvasItem)_optionScrollContainer).Draw += delegate
			{
				//IL_0021: Unknown result type (might be due to invalid IL or missing references)
				//IL_0026: Unknown result type (might be due to invalid IL or missing references)
				//IL_002c: Unknown result type (might be due to invalid IL or missing references)
				//IL_005e: Unknown result type (might be due to invalid IL or missing references)
				//IL_0063: Unknown result type (might be due to invalid IL or missing references)
				//IL_0069: Unknown result type (might be due to invalid IL or missing references)
				((CanvasItem)_optionScrollContainer).DrawRect(new Rect2(0f, 0f, ((Control)_optionScrollContainer).Size), back, true, -1f, false);
				((CanvasItem)_optionScrollContainer).DrawRect(new Rect2(0f, 0f, ((Control)_optionScrollContainer).Size), border, false, 2f);
			};
			((Node)this).AddChild((Node)(object)_optionScrollContainer, false, (InternalMode)0);
			((Node)_optionScrollContainer).Owner = (Node)(object)this;
			((Control)_optionScrollContainer).Position = ((Control)this).Size * 0.5f - ((Control)_optionScrollContainer).Size * 0.5f;
			NScrollbar val = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/scrollbar")).Instantiate<NScrollbar>((GenEditState)0);
			((Node)val).Name = StringName.op_Implicit("Scrollbar");
			((Node)_optionScrollContainer).AddChild((Node)(object)val, false, (InternalMode)0);
			((Node)val).Owner = (Node)(object)_optionScrollContainer;
			((Control)val).SetAnchorsAndOffsetsPreset((LayoutPreset)11, (LayoutPresetMode)0, 0);
			((Control)val).Size = new Vector2(48f, ((Control)_optionScrollContainer).Size.Y);
			((Control)val).Position = new Vector2(((Control)_optionScrollContainer).Size.X + 4f, 0f);
			Control val2 = new Control();
			((Node)val2).Name = StringName.op_Implicit("Mask");
			val2.Size = ((Control)_optionScrollContainer).Size;
			val2.MouseFilter = (MouseFilterEnum)2;
			((CanvasItem)val2).ClipChildren = (ClipChildrenMode)1;
			((Node)_optionScrollContainer).AddChild((Node)(object)val2, false, (InternalMode)0);
			((Node)val2).Owner = (Node)(object)_optionScrollContainer;
			_optionContainer = new Control();
			((Node)_optionContainer).Name = StringName.op_Implicit("Content");
			_optionContainer.Size = val2.Size;
			val2.MouseFilter = (MouseFilterEnum)2;
			((Node)val2).AddChild((Node)(object)_optionContainer, false, (InternalMode)0);
			((Node)_optionContainer).Owner = (Node)(object)val2;
			((CanvasItem)this).Hide();
			GodotTreeExtensions.AddChildSafely((Node)(object)futureParent, (Node)(object)this);
		}

		public override void _Ready()
		{
			((NClickableControl)this).ConnectSignals();
		}

		private void ShowMod(ModConfig config, NConfigButton opener)
		{
			_opener = opener;
			NHotkeyManager instance = NHotkeyManager.Instance;
			if (instance != null)
			{
				instance.AddBlockingScreen((Node)(object)this);
			}
			((Control)this).MouseFilter = (MouseFilterEnum)0;
			try
			{
				config.SetupConfigUI((Control)(object)_optionScrollContainer);
				_currentConfig = config;
				config.ConfigChanged += OnConfigChanged;
				((CanvasItem)this).Show();
			}
			catch (Exception ex)
			{
				MainFile.Logger.Error(ex.ToString(), 1);
				ClosePopup();
			}
		}

		private void ClosePopup()
		{
			if (_opener != null)
			{
				_opener.IsConfigOpen = false;
			}
			NHotkeyManager instance = NHotkeyManager.Instance;
			if (instance != null)
			{
				instance.RemoveBlockingScreen((Node)(object)this);
			}
			((Control)this).MouseFilter = (MouseFilterEnum)2;
			if (_currentConfig != null)
			{
				_currentConfig.ConfigChanged -= OnConfigChanged;
			}
			((CanvasItem)this).Hide();
			GodotTreeExtensions.FreeChildren((Node)(object)_optionContainer);
			foreach (Node child in ((Node)_optionContainer).GetParent().GetChildren(false))
			{
				if ((object)child != _optionContainer)
				{
					GodotTreeExtensions.QueueFreeSafely(child);
				}
			}
		}

		private void OnConfigChanged(object? sender, EventArgs e)
		{
			_saveTimer = 5.0;
		}

		public override void _Process(double delta)
		{
			((Node)this)._Process(delta);
			if (_saveTimer > 0.0)
			{
				_saveTimer -= delta;
				if (_saveTimer <= 0.0)
				{
					SaveCurrentConfig();
				}
			}
		}

		protected override void OnRelease()
		{
			((NClickableControl)this).OnRelease();
			SaveCurrentConfig();
			ClosePopup();
		}

		private void SaveCurrentConfig()
		{
			_currentConfig?.Save();
			_saveTimer = -1.0;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<MethodInfo> GetGodotMethodList()
		{
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_0034: Expected O, but got Unknown
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0058: Unknown result type (might be due to invalid IL or missing references)
			//IL_0063: Expected O, but got Unknown
			//IL_005e: Unknown result type (might be due to invalid IL or missing references)
			//IL_006a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0091: Unknown result type (might be due to invalid IL or missing references)
			//IL_009a: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
			//IL_0114: Unknown result type (might be due to invalid IL or missing references)
			//IL_0120: Unknown result type (might be due to invalid IL or missing references)
			//IL_0147: Unknown result type (might be due to invalid IL or missing references)
			//IL_0150: Unknown result type (might be due to invalid IL or missing references)
			//IL_0177: Unknown result type (might be due to invalid IL or missing references)
			//IL_0180: Unknown result type (might be due to invalid IL or missing references)
			List<MethodInfo> list = new List<MethodInfo>(6);
			list.Add(new MethodInfo(MethodName.Create, new PropertyInfo((Type)24, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, new StringName("Control"), false), (MethodFlags)33, new List<PropertyInfo>
			{
				new PropertyInfo((Type)24, StringName.op_Implicit("screen"), (PropertyHint)0, "", (PropertyUsageFlags)6, new StringName("Control"), false)
			}, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName._Ready, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName.ClosePopup, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName._Process, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, new List<PropertyInfo>
			{
				new PropertyInfo((Type)3, StringName.op_Implicit("delta"), (PropertyHint)0, "", (PropertyUsageFlags)6, false)
			}, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName.OnRelease, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			list.Add(new MethodInfo(MethodName.SaveCurrentConfig, new PropertyInfo((Type)0, StringName.op_Implicit(""), (PropertyHint)0, "", (PropertyUsageFlags)6, false), (MethodFlags)1, (List<PropertyInfo>)null, (List<Variant>)null));
			return list;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			//IL_0036: Unknown result type (might be due to invalid IL or missing references)
			//IL_003b: Unknown result type (might be due to invalid IL or missing references)
			//IL_006e: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
			//IL_011a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0159: Unknown result type (might be due to invalid IL or missing references)
			//IL_014d: Unknown result type (might be due to invalid IL or missing references)
			if ((ref method) == MethodName.Create && ((NativeVariantPtrArgs)(ref args)).Count == 1)
			{
				NModConfigPopup nModConfigPopup = Create(VariantUtils.ConvertTo<NModdingScreen>(ref ((NativeVariantPtrArgs)(ref args))[0]));
				ret = VariantUtils.CreateFrom<NModConfigPopup>(ref nModConfigPopup);
				return true;
			}
			if ((ref method) == MethodName._Ready && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				((Node)this)._Ready();
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName.ClosePopup && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				ClosePopup();
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName._Process && ((NativeVariantPtrArgs)(ref args)).Count == 1)
			{
				((Node)this)._Process(VariantUtils.ConvertTo<double>(ref ((NativeVariantPtrArgs)(ref args))[0]));
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName.OnRelease && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				((NClickableControl)this).OnRelease();
				ret = default(godot_variant);
				return true;
			}
			if ((ref method) == MethodName.SaveCurrentConfig && ((NativeVariantPtrArgs)(ref args)).Count == 0)
			{
				SaveCurrentConfig();
				ret = default(godot_variant);
				return true;
			}
			return ((NClickableControl)this).InvokeGodotClassMethod(ref method, args, ref ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static bool InvokeGodotClassStaticMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			//IL_0045: Unknown result type (might be due to invalid IL or missing references)
			//IL_0036: Unknown result type (might be due to invalid IL or missing references)
			//IL_003b: Unknown result type (might be due to invalid IL or missing references)
			if ((ref method) == MethodName.Create && ((NativeVariantPtrArgs)(ref args)).Count == 1)
			{
				NModConfigPopup nModConfigPopup = Create(VariantUtils.ConvertTo<NModdingScreen>(ref ((NativeVariantPtrArgs)(ref args))[0]));
				ret = VariantUtils.CreateFrom<NModConfigPopup>(ref nModConfigPopup);
				return true;
			}
			ret = default(godot_variant);
			return false;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool HasGodotClassMethod(in godot_string_name method)
		{
			if ((ref method) == MethodName.Create)
			{
				return true;
			}
			if ((ref method) == MethodName._Ready)
			{
				return true;
			}
			if ((ref method) == MethodName.ClosePopup)
			{
				return true;
			}
			if ((ref method) == MethodName._Process)
			{
				return true;
			}
			if ((ref method) == MethodName.OnRelease)
			{
				return true;
			}
			if ((ref method) == MethodName.SaveCurrentConfig)
			{
				return true;
			}
			return ((NClickableControl)this).HasGodotClassMethod(ref method);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool SetGodotClassPropertyValue(in godot_string_name name, in godot_variant value)
		{
			if ((ref name) == PropertyName._optionScrollContainer)
			{
				_optionScrollContainer = VariantUtils.ConvertTo<NScrollableContainer>(ref value);
				return true;
			}
			if ((ref name) == PropertyName._optionContainer)
			{
				_optionContainer = VariantUtils.ConvertTo<Control>(ref value);
				return true;
			}
			if ((ref name) == PropertyName._opener)
			{
				_opener = VariantUtils.ConvertTo<NConfigButton>(ref value);
				return true;
			}
			if ((ref name) == PropertyName._saveTimer)
			{
				_saveTimer = VariantUtils.ConvertTo<double>(ref value);
				return true;
			}
			return ((NClickableControl)this).SetGodotClassPropertyValue(ref name, ref value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool GetGodotClassPropertyValue(in godot_string_name name, out godot_variant value)
		{
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			//IL_003d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0042: Unknown result type (might be due to invalid IL or missing references)
			//IL_0062: Unknown result type (might be due to invalid IL or missing references)
			//IL_0067: Unknown result type (might be due to invalid IL or missing references)
			//IL_0089: Unknown result type (might be due to invalid IL or missing references)
			//IL_008e: Unknown result type (might be due to invalid IL or missing references)
			if ((ref name) == PropertyName._optionScrollContainer)
			{
				value = VariantUtils.CreateFrom<NScrollableContainer>(ref _optionScrollContainer);
				return true;
			}
			if ((ref name) == PropertyName._optionContainer)
			{
				value = VariantUtils.CreateFrom<Control>(ref _optionContainer);
				return true;
			}
			if ((ref name) == PropertyName._opener)
			{
				value = VariantUtils.CreateFrom<NConfigButton>(ref _opener);
				return true;
			}
			if ((ref name) == PropertyName._saveTimer)
			{
				value = VariantUtils.CreateFrom<double>(ref _saveTimer);
				return true;
			}
			return ((NClickableControl)this).GetGodotClassPropertyValue(ref name, ref value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<PropertyInfo> GetGodotPropertyList()
		{
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0040: Unknown result type (might be due to invalid IL or missing references)
			//IL_0062: Unknown result type (might be due to invalid IL or missing references)
			//IL_0083: Unknown result type (might be due to invalid IL or missing references)
			List<PropertyInfo> list = new List<PropertyInfo>();
			list.Add(new PropertyInfo((Type)24, PropertyName._optionScrollContainer, (PropertyHint)0, "", (PropertyUsageFlags)4096, false));
			list.Add(new PropertyInfo((Type)24, PropertyName._optionContainer, (PropertyHint)0, "", (PropertyUsageFlags)4096, false));
			list.Add(new PropertyInfo((Type)24, PropertyName._opener, (PropertyHint)0, "", (PropertyUsageFlags)4096, false));
			list.Add(new PropertyInfo((Type)3, PropertyName._saveTimer, (PropertyHint)0, "", (PropertyUsageFlags)4096, false));
			return list;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0043: Unknown result type (might be due to invalid IL or missing references)
			//IL_005a: Unknown result type (might be due to invalid IL or missing references)
			((NClickableControl)this).SaveGodotObjectData(info);
			info.AddProperty(PropertyName._optionScrollContainer, Variant.From<NScrollableContainer>(ref _optionScrollContainer));
			info.AddProperty(PropertyName._optionContainer, Variant.From<Control>(ref _optionContainer));
			info.AddProperty(PropertyName._opener, Variant.From<NConfigButton>(ref _opener));
			info.AddProperty(PropertyName._saveTimer, Variant.From<double>(ref _saveTimer));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			((NClickableControl)this).RestoreGodotObjectData(info);
			Variant val = default(Variant);
			if (info.TryGetProperty(PropertyName._optionScrollContainer, ref val))
			{
				_optionScrollContainer = ((Variant)(ref val)).As<NScrollableContainer>();
			}
			Variant val2 = default(Variant);
			if (info.TryGetProperty(PropertyName._optionContainer, ref val2))
			{
				_optionContainer = ((Variant)(ref val2)).As<Control>();
			}
			Variant val3 = default(Variant);
			if (info.TryGetProperty(PropertyName._opener, ref val3))
			{
				_opener = ((Variant)(ref val3)).As<NConfigButton>();
			}
			Variant val4 = default(Variant);
			if (info.TryGetProperty(PropertyName._saveTimer, ref val4))
			{
				_saveTimer = ((Variant)(ref val4)).As<double>();
			}
		}
	}
}
namespace BaseLib.Cards.Variables
{
	public class ExhaustiveVar : DynamicVar
	{
		public const string Key = "Exhaustive";

		public ExhaustiveVar(decimal exhaustiveCount)
			: base("Exhaustive", exhaustiveCount)
		{
			((DynamicVar)(object)this).WithTooltip();
		}

		public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
		{
			((DynamicVar)this).PreviewValue = ExhaustiveCount(card, ((DynamicVar)this).IntValue);
		}

		public static int ExhaustiveCount(CardModel card, int baseExhaustive)
		{
			if (baseExhaustive <= 0)
			{
				return 0;
			}
			int num = CombatManager.Instance.History.CardPlaysFinished.Count((CardPlayFinishedEntry entry) => entry.CardPlay.Card == card);
			return Math.Max(1, baseExhaustive - num);
		}
	}
	public class PersistVar : DynamicVar
	{
		public const string Key = "Persist";

		public PersistVar(decimal persistCount)
			: base("Persist", persistCount)
		{
			((DynamicVar)(object)this).WithTooltip();
		}

		public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
		{
			((DynamicVar)this).PreviewValue = PersistCount(card, ((DynamicVar)this).IntValue);
		}

		public static int PersistCount(CardModel card, int basePersist)
		{
			int num = CombatManager.Instance.History.CardPlaysFinished.Count((CardPlayFinishedEntry entry) => ((CombatHistoryEntry)entry).HappenedThisTurn(card.CombatState) && entry.CardPlay.Card == card);
			return Math.Max(0, basePersist - num);
		}
	}
	public class RefundVar : DynamicVar
	{
		public const string Key = "Refund";

		public RefundVar(decimal persistCount)
			: base("Refund", persistCount)
		{
			((DynamicVar)(object)this).WithTooltip();
		}
	}
}
namespace BaseLib.Abstracts
{
	public abstract class CustomAncientModel : AncientEventModel, ICustomModel
	{
		private readonly bool _logDialogueLoad;

		private OptionPools? _optionPools;

		protected abstract OptionPools MakeOptionPools { get; }

		public OptionPools OptionPools
		{
			get
			{
				if (_optionPools == null)
				{
					_optionPools = MakeOptionPools;
				}
				return _optionPools;
			}
		}

		public override IEnumerable<EventOption> AllPossibleOptions => OptionPools.AllOptions.SelectMany((AncientOption option) => option.AllVariants.Select((RelicModel relic) => ((AncientEventModel)this).RelicOption(relic, "INITIAL", (string)null)));

		public virtual string? CustomScenePath => null;

		public virtual string? CustomMapIconPath => null;

		public virtual string? CustomMapIconOutlinePath => null;

		public virtual Texture2D? CustomRunHistoryIcon => null;

		public virtual Texture2D? CustomRunHistoryIconOutline => null;

		private string FirstVisit => ((AbstractModel)this).Id.Entry + ".talk.firstvisitEver.0-0.ancient";

		public CustomAncientModel(bool autoAdd = true, bool logDialogueLoad = false)
		{
			if (autoAdd)
			{
				CustomContentDictionary.AddAncient(this);
			}
			_logDialogueLoad = logDialogueLoad;
		}

		public virtual bool IsValidForAct(ActModel act)
		{
			return true;
		}

		public virtual bool ShouldForceSpawn(ActModel act, AncientEventModel? rngChosenAncient)
		{
			return false;
		}

		protected override IReadOnlyList<EventOption> GenerateInitialOptions()
		{
			List<AncientOption> source = OptionPools.Roll(((EventModel)this).Rng);
			return source.Select((AncientOption option) => ((AncientEventModel)this).RelicOption(option.ModelForOption, "INITIAL", (string)null)).ToList();
		}

		public static WeightedList<AncientOption> MakePool(params RelicModel[] options)
		{
			WeightedList<AncientOption> weightedList = new WeightedList<AncientOption>();
			foreach (AncientOption item in options.Select((RelicModel model) => (AncientOption)model))
			{
				weightedList.Add(item);
			}
			return weightedList;
		}

		public static WeightedList<AncientOption> MakePool(params AncientOption[] options)
		{
			WeightedList<AncientOption> weightedList = new WeightedList<AncientOption>();
			foreach (AncientOption item in options)
			{
				weightedList.Add(item);
			}
			return weightedList;
		}

		public static AncientOption AncientOption<T>(int weight = 1, Func<T, RelicModel>? relicPrep = null, Func<T, IEnumerable<RelicModel>>? makeAllVariants = null) where T : RelicModel
		{
			return new AncientOption<T>(weight)
			{
				ModelPrep = relicPrep,
				Variants = makeAllVariants
			};
		}

		public override IEnumerable<string> GetAssetPaths(IRunState runState)
		{
			string customScenePath = CustomScenePath;
			IEnumerable<string> result;
			if (customScenePath == null)
			{
				result = ((EventModel)this).GetAssetPaths(runState);
			}
			else
			{
				IEnumerable<string> enumerable = new <>z__ReadOnlySingleElementList<string>(customScenePath);
				result = enumerable;
			}
			return result;
		}

		protected override AncientDialogueSet DefineDialogues()
		{
			//IL_0042: Unknown result type (might be due to invalid IL or missing references)
			//IL_0048: Expected O, but got Unknown
			//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
			//IL_0104: Unknown result type (might be due to invalid IL or missing references)
			//IL_010c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0114: Unknown result type (might be due to invalid IL or missing references)
			//IL_012d: Expected O, but got Unknown
			StringBuilder stringBuilder = (_logDialogueLoad ? new StringBuilder("Prepping dialogue for ancient '" + ((AbstractModel)this).Id.Entry + "'") : null);
			string[] array = new string[1];
			string value = (array[0] = AncientDialogueUtil.SfxPath(FirstVisit));
			AncientDialogue firstVisitEverDialogue = new AncientDialogue(array);
			if (stringBuilder != null)
			{
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(23, 1, stringBuilder2);
				handler.AppendLiteral("First visit with sfx '");
				handler.AppendFormatted(value);
				handler.AppendLiteral("'");
				stringBuilder2.AppendLine(ref handler);
			}
			Dictionary<string, IReadOnlyList<AncientDialogue>> dictionary = new Dictionary<string, IReadOnlyList<AncientDialogue>>();
			foreach (CharacterModel allCharacter in ModelDb.AllCharacters)
			{
				string baseKey = AncientDialogueUtil.BaseLocKey(((AbstractModel)this).Id.Entry, ((AbstractModel)allCharacter).Id.Entry);
				dictionary[((AbstractModel)allCharacter).Id.Entry] = AncientDialogueUtil.GetDialoguesForKey("ancients", baseKey, stringBuilder);
			}
			AncientDialogueSet val = new AncientDialogueSet();
			val.set_FirstVisitEverDialogue(firstVisitEverDialogue);
			val.set_CharacterDialogues(dictionary);
			val.set_AgnosticDialogues((IReadOnlyList<AncientDialogue>)AncientDialogueUtil.GetDialoguesForKey("ancients", "ANY", stringBuilder));
			AncientDialogueSet result = val;
			if (stringBuilder != null)
			{
				MainFile.Logger.Info(stringBuilder.ToString(), 1);
			}
			return result;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class MapIconPath
	{
		[HarmonyPrefix]
		private static bool Custom(AncientEventModel __instance, ref string? __result)
		{
			if (!(__instance is CustomAncientModel customAncientModel))
			{
				return true;
			}
			__result = customAncientModel.CustomMapIconPath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class MapIconOutlinePath
	{
		[HarmonyPrefix]
		private static bool Custom(AncientEventModel __instance, ref string? __result)
		{
			if (!(__instance is CustomAncientModel customAncientModel))
			{
				return true;
			}
			__result = customAncientModel.CustomMapIconOutlinePath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class RunHistoryIcon
	{
		[HarmonyPrefix]
		private static bool Custom(AncientEventModel __instance, ref Texture2D? __result)
		{
			if (!(__instance is CustomAncientModel customAncientModel))
			{
				return true;
			}
			__result = customAncientModel.CustomRunHistoryIcon;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class RunHistoryIconOutline
	{
		[HarmonyPrefix]
		private static bool Custom(AncientEventModel __instance, ref Texture2D? __result)
		{
			if (!(__instance is CustomAncientModel customAncientModel))
			{
				return true;
			}
			__result = customAncientModel.CustomRunHistoryIconOutline;
			return __result == null;
		}
	}
	public abstract class CustomCardModel : CardModel, ICustomModel
	{
		public override bool GainsBlock => ((IEnumerable<KeyValuePair<string, DynamicVar>>)((CardModel)this).DynamicVars).Any(delegate(KeyValuePair<string, DynamicVar> dynVar)
		{
			DynamicVar value = dynVar.Value;
			return (value is BlockVar || value is CalculatedBlockVar) ? true : false;
		});

		public virtual Texture2D? CustomFrame => null;

		public virtual string? CustomPortraitPath => null;

		public CustomCardModel(int baseCost, CardType type, CardRarity rarity, TargetType target, bool showInCardLibrary = true, bool autoAdd = true)
			: base(baseCost, type, rarity, target, showInCardLibrary)
		{
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			//IL_0003: Unknown result type (might be due to invalid IL or missing references)
			//IL_0004: Unknown result type (might be due to invalid IL or missing references)
			if (autoAdd)
			{
				CustomContentDictionary.AddModel(((object)this).GetType());
			}
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class CustomCardFrame
	{
		[HarmonyPrefix]
		private static bool UseAltTexture(CardModel __instance, ref Texture2D? __result)
		{
			if (__instance is CustomCardModel customCardModel)
			{
				__result = customCardModel.CustomFrame;
				if (__result != null)
				{
					return false;
				}
				if (__instance.Pool is CustomCardPoolModel customCardPoolModel)
				{
					__result = customCardPoolModel.CustomFrame(customCardModel);
					if (__result != null)
					{
						return false;
					}
				}
			}
			return true;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class CustomCardPortraitPath
	{
		[HarmonyPrefix]
		private static bool UseAltTexture(CardModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCardModel customCardModel))
			{
				return true;
			}
			__result = customCardModel.CustomPortraitPath;
			return __result == null;
		}
	}
	public abstract class CustomCardPoolModel : CardPoolModel, ICustomModel, ICustomEnergyIconPool
	{
		public override string CardFrameMaterialPath => "card_frame_red";

		public virtual Color ShaderColor => new Color("FFFFFF");

		public virtual float H
		{
			get
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0006: Unknown result type (might be due to invalid IL or missing references)
				Color shaderColor = ShaderColor;
				return ((Color)(ref shaderColor)).H;
			}
		}

		public virtual float S
		{
			get
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0006: Unknown result type (might be due to invalid IL or missing references)
				Color shaderColor = ShaderColor;
				return ((Color)(ref shaderColor)).S;
			}
		}

		public virtual float V
		{
			get
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0006: Unknown result type (might be due to invalid IL or missing references)
				Color shaderColor = ShaderColor;
				return ((Color)(ref shaderColor)).V;
			}
		}

		public virtual bool IsShared => false;

		public override string EnergyColorName => CustomEnergyIconPatches.GetEnergyColorName(((AbstractModel)this).Id);

		public virtual string? BigEnergyIconPath => null;

		public virtual string? TextEnergyIconPath => null;

		public CustomCardPoolModel()
		{
			if (IsShared)
			{
				ModelDbSharedCardPoolsPatch.Register(this);
			}
		}

		public virtual Texture2D? CustomFrame(CustomCardModel card)
		{
			return null;
		}

		protected override CardModel[] GenerateAllCards()
		{
			return Array.Empty<CardModel>();
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class CustomCardPoolMaterialPatch
	{
		[HarmonyPrefix]
		private static bool UseCustomMaterial(CardPoolModel __instance, ref Material __result)
		{
			if (__instance is CustomCardPoolModel customCardPoolModel)
			{
				if (!((CardPoolModel)customCardPoolModel).CardFrameMaterialPath.Equals("card_frame_red"))
				{
					return true;
				}
				__result = (Material)(object)ShaderUtils.GenerateHsv(customCardPoolModel.H, customCardPoolModel.S, customCardPoolModel.V);
				return false;
			}
			return true;
		}
	}
	public abstract class CustomCharacterModel : CharacterModel, ICustomModel
	{
		public virtual string? CustomVisualPath => null;

		public virtual string? CustomTrailPath => null;

		public virtual string? CustomIconTexturePath => null;

		public virtual string? CustomIconPath => null;

		public virtual CustomEnergyCounter? CustomEnergyCounter => null;

		public virtual string? CustomEnergyCounterPath => null;

		public virtual string? CustomRestSiteAnimPath => null;

		public virtual string? CustomMerchantAnimPath => null;

		public virtual string? CustomArmPointingTexturePath => null;

		public virtual string? CustomArmRockTexturePath => null;

		public virtual string? CustomArmPaperTexturePath => null;

		public virtual string? CustomArmScissorsTexturePath => null;

		public virtual string? CustomCharacterSelectBg => null;

		public virtual string? CustomCharacterSelectIconPath => null;

		public virtual string? CustomCharacterSelectLockedIconPath => null;

		public virtual string? CustomCharacterSelectTransitionPath => null;

		public virtual string? CustomMapMarkerPath => null;

		public virtual string? CustomAttackSfx => null;

		public virtual string? CustomCastSfx => null;

		public virtual string? CustomDeathSfx => null;

		public override int StartingGold => 99;

		public override float AttackAnimDelay => 0.15f;

		public override float CastAnimDelay => 0.25f;

		protected override CharacterModel? UnlocksAfterRunAs => null;

		public CustomCharacterModel()
		{
			ModelDbCustomCharacters.Register(this);
		}

		public virtual NCreatureVisuals? CreateCustomVisuals()
		{
			if (CustomVisualPath == null)
			{
				return null;
			}
			return GodotUtils.CreatureVisualsFromScene(CustomVisualPath);
		}

		public virtual CreatureAnimator? SetupCustomAnimationStates(MegaSprite controller)
		{
			return null;
		}

		public static CreatureAnimator SetupAnimationState(MegaSprite controller, string idleName, string? deadName = null, bool deadLoop = false, string? hitName = null, bool hitLoop = false, string? attackName = null, bool attackLoop = false, string? castName = null, bool castLoop = false, string? relaxedName = null, bool relaxedLoop = true)
		{
			//IL_0003: Unknown result type (might be due to invalid IL or missing references)
			//IL_0009: Expected O, but got Unknown
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			//IL_001f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			//IL_0038: Unknown result type (might be due to invalid IL or missing references)
			//IL_003d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0051: Unknown result type (might be due to invalid IL or missing references)
			//IL_0056: Unknown result type (might be due to invalid IL or missing references)
			//IL_007a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0081: Expected O, but got Unknown
			//IL_0093: Unknown result type (might be due to invalid IL or missing references)
			//IL_009a: Expected O, but got Unknown
			AnimState val = new AnimState(idleName, true);
			AnimState val2 = (AnimState)((deadName == null) ? ((object)val) : ((object)new AnimState(deadName, deadLoop)));
			AnimState val3 = (AnimState)((hitName == null) ? ((object)val) : ((object)new AnimState(hitName, hitLoop)
			{
				NextState = val
			}));
			AnimState val4 = (AnimState)((attackName == null) ? ((object)val) : ((object)new AnimState(attackName, attackLoop)
			{
				NextState = val
			}));
			AnimState val5 = (AnimState)((castName == null) ? ((object)val) : ((object)new AnimState(castName, castLoop)
			{
				NextState = val
			}));
			AnimState val6;
			if (relaxedName == null)
			{
				val6 = val;
			}
			else
			{
				val6 = new AnimState(relaxedName, relaxedLoop);
				val6.AddBranch("Idle", val, (Func<bool>)null);
			}
			CreatureAnimator val7 = new CreatureAnimator(val, controller);
			val7.AddAnyState("Idle", val, (Func<bool>)null);
			val7.AddAnyState("Dead", val2, (Func<bool>)null);
			val7.AddAnyState("Hit", val3, (Func<bool>)null);
			val7.AddAnyState("Attack", val4, (Func<bool>)null);
			val7.AddAnyState("Cast", val5, (Func<bool>)null);
			val7.AddAnyState("Relaxed", val6, (Func<bool>)null);
			return val7;
		}
	}
	public readonly struct CustomEnergyCounter
	{
		private readonly Func<int, string> _getPath;

		public readonly Color OutlineColor;

		public readonly Color BurstColor;

		public CustomEnergyCounter(Func<int, string> pathFunc, Color outlineColor, Color burstColor)
		{
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			_getPath = pathFunc;
			OutlineColor = outlineColor;
			BurstColor = burstColor;
		}

		public string LayerImagePath(int layer)
		{
			return _getPath(layer);
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	public class EnergyCounterOutlineColorPatch
	{
		private static readonly FieldInfo? PlayerProp = typeof(NEnergyCounter).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic);

		private static bool Prefix(NEnergyCounter __instance, ref Color __result)
		{
			//IL_0052: Unknown result type (might be due to invalid IL or missing references)
			//IL_0057: Unknown result type (might be due to invalid IL or missing references)
			object? obj = PlayerProp?.GetValue(__instance);
			Player val = (Player)((obj is Player) ? obj : null);
			if (val != null && val.Character is CustomCharacterModel { CustomEnergyCounter: { } customEnergyCounter })
			{
				__result = customEnergyCounter.OutlineColor;
				return false;
			}
			return true;
		}
	}
	[HarmonyPatch(typeof(NEnergyCounter), "Create")]
	internal class EnergyCounterPatch
	{
		private static List<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			return new InstructionPatcher(instructions).Match(new InstructionMatcher().ldc_i4_0().opcode(OpCodes.Conv_I8).callvirt(null)
				.stloc_0()).Insert((IEnumerable<CodeInstruction>)new <>z__ReadOnlyArray<CodeInstruction>((CodeInstruction[])(object)new CodeInstruction[4]
			{
				CodeInstruction.LoadLocal(0, false),
				CodeInstruction.LoadArgument(0, false),
				CodeInstruction.Call(typeof(EnergyCounterPatch), "ChangeIroncladEnergy", (Type[])null, (Type[])null),
				CodeInstruction.StoreLocal(0)
			}));
		}

		private static NEnergyCounter ChangeIroncladEnergy(NEnergyCounter defaultCounter, Player player)
		{
			//IL_0139: Unknown result type (might be due to invalid IL or missing references)
			//IL_0155: Unknown result type (might be due to invalid IL or missing references)
			if (player.Character is CustomCharacterModel { CustomEnergyCounter: var customEnergyCounter })
			{
				CustomEnergyCounter valueOrDefault = default(CustomEnergyCounter);
				int num;
				if (customEnergyCounter.HasValue)
				{
					valueOrDefault = customEnergyCounter.GetValueOrDefault();
					num = 1;
				}
				else
				{
					num = 0;
				}
				if (num != 0)
				{
					AssetCache cache = PreloadManager.Cache;
					string reference = "combat/energy_counters/ironclad_energy_counter";
					NEnergyCounter val = cache.GetScene(SceneHelper.GetScenePath(string.Concat(new ReadOnlySpan<string>(in reference)))).Instantiate<NEnergyCounter>((GenEditState)0);
					((Node)val).GetNode<TextureRect>(NodePath.op_Implicit("%Layers/Layer1")).Texture = ResourceLoader.Load<Texture2D>(valueOrDefault.LayerImagePath(1), (string)null, (CacheMode)1);
					((Node)val).GetNode<TextureRect>(NodePath.op_Implicit("%RotationLayers/Layer2")).Texture = ResourceLoader.Load<Texture2D>(valueOrDefault.LayerImagePath(2), (string)null, (CacheMode)1);
					((Node)val).GetNode<TextureRect>(NodePath.op_Implicit("%RotationLayers/Layer3")).Texture = ResourceLoader.Load<Texture2D>(valueOrDefault.LayerImagePath(3), (string)null, (CacheMode)1);
					((Node)val).GetNode<TextureRect>(NodePath.op_Implicit("%Layers/Layer4")).Texture = ResourceLoader.Load<Texture2D>(valueOrDefault.LayerImagePath(4), (string)null, (CacheMode)1);
					((Node)val).GetNode<TextureRect>(NodePath.op_Implicit("%Layers/Layer5")).Texture = ResourceLoader.Load<Texture2D>(valueOrDefault.LayerImagePath(5), (string)null, (CacheMode)1);
					((Node)val).GetNode<CpuParticles2D>(NodePath.op_Implicit("%BurstBack")).Color = valueOrDefault.BurstColor;
					((Node)val).GetNode<CpuParticles2D>(NodePath.op_Implicit("%BurstFront")).Color = valueOrDefault.BurstColor;
					return val;
				}
			}
			return defaultCounter;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	public class ModelDbCustomCharacters
	{
		public static readonly List<CustomCharacterModel> CustomCharacters = new List<CustomCharacterModel>();

		[HarmonyPostfix]
		public static IEnumerable<CharacterModel> AddCustomPools(IEnumerable<CharacterModel> __result)
		{
			List<CharacterModel> list = new List<CharacterModel>();
			list.AddRange(__result);
			foreach (CustomCharacterModel customCharacter in CustomCharacters)
			{
				list.Add((CharacterModel)(object)customCharacter);
			}
			return new <>z__ReadOnlyList<CharacterModel>(list);
		}

		public static void Register(CustomCharacterModel character)
		{
			CustomCharacters.Add(character);
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class CustomCharacterVisualPath
	{
		[HarmonyPrefix]
		private static bool UseCustomScene(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomVisualPath;
			return __result == null;
		}
	}
	[HarmonyPatch(typeof(CharacterModel), "CreateVisuals")]
	internal class CustomCharacterVisuals
	{
		[HarmonyPrefix]
		private static bool UseCustomVisuals(CharacterModel __instance, ref NCreatureVisuals? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CreateCustomVisuals();
			return __result == null;
		}
	}
	[HarmonyPatch(typeof(CharacterModel), "GenerateAnimator")]
	internal class GenerateAnimatorPatch
	{
		[HarmonyPrefix]
		private static bool CustomAnimator(CharacterModel __instance, MegaSprite controller, ref CreatureAnimator? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.SetupCustomAnimationStates(controller);
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class TrailPath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomTrailPath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class IconTexturePath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomIconTexturePath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class IconPath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomIconPath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class EnergyCounterPath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomEnergyCounterPath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class RestSiteAnimPath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomRestSiteAnimPath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class MerchantAnimPath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomMerchantAnimPath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class ArmPointingTexturePath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomArmPointingTexturePath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class ArmRockTexturePath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomArmRockTexturePath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class ArmPaperTexturePath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomArmPaperTexturePath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class ArmScissorsTexturePath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomArmScissorsTexturePath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class CharacterSelectTransitionPath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomCharacterSelectTransitionPath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class CustomCharacterSelectBg
	{
		[HarmonyPrefix]
		private static bool UseCustomScene(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomCharacterSelectBg;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class CharacterSelectIconPath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomCharacterSelectIconPath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class CharacterSelectLockedIconPath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomCharacterSelectLockedIconPath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class MapMarkerPath
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomMapMarkerPath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class AttackSfx
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomAttackSfx;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class CastSfx
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomCastSfx;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class DeathSfx
	{
		[HarmonyPrefix]
		private static bool Custom(CharacterModel __instance, ref string? __result)
		{
			if (!(__instance is CustomCharacterModel customCharacterModel))
			{
				return true;
			}
			__result = customCharacterModel.CustomDeathSfx;
			return __result == null;
		}
	}
	public abstract class CustomPile : CardPile
	{
		public virtual bool NeedsCustomTransitionVisual => false;

		public CustomPile(PileType pileType)
			: base(pileType)
		{
		}//IL_0001: Unknown result type (might be due to invalid IL or missing references)


		public abstract bool CardShouldBeVisible(CardModel card);

		public abstract Vector2 GetTargetPosition(CardModel model, Vector2 size);

		public virtual NCard? GetNCard(CardModel card)
		{
			return null;
		}

		public virtual bool CustomTween(Tween tween, CardModel card, NCard cardNode, CardPile oldPile)
		{
			return false;
		}
	}
	public abstract class CustomPotionModel : PotionModel, ICustomModel
	{
		[HarmonyPatch(/*Could not decode attribute arguments.*/)]
		private static class ImagePatch
		{
			private static bool Prefix(PotionModel __instance, ref string __result)
			{
				if (__instance is CustomPotionModel customPotionModel)
				{
					string packedImagePath = customPotionModel.PackedImagePath;
					if (packedImagePath != null)
					{
						__result = packedImagePath;
						return false;
					}
				}
				return true;
			}
		}

		[HarmonyPatch(/*Could not decode attribute arguments.*/)]
		private static class OutlinePatch
		{
			private static bool Prefix(PotionModel __instance, ref string __result)
			{
				if (__instance is CustomPotionModel customPotionModel)
				{
					string packedOutlinePath = customPotionModel.PackedOutlinePath;
					if (packedOutlinePath != null)
					{
						__result = packedOutlinePath;
						return false;
					}
				}
				return true;
			}
		}

		public virtual bool AutoAdd => true;

		public virtual string? PackedImagePath => null;

		public virtual string? PackedOutlinePath => null;

		public CustomPotionModel()
		{
			if (AutoAdd)
			{
				CustomContentDictionary.AddModel(((object)this).GetType());
			}
		}
	}
	public abstract class CustomPotionPoolModel : PotionPoolModel, ICustomModel, ICustomEnergyIconPool
	{
		public virtual bool IsShared => false;

		public override string EnergyColorName => CustomEnergyIconPatches.GetEnergyColorName(((AbstractModel)this).Id);

		public virtual string? BigEnergyIconPath => null;

		public virtual string? TextEnergyIconPath => null;

		public CustomPotionPoolModel()
		{
			if (IsShared)
			{
				ModelDbSharedPotionPoolsPatch.Register(this);
			}
		}

		protected override IEnumerable<PotionModel> GenerateAllPotions()
		{
			return Array.Empty<PotionModel>();
		}
	}
	public abstract class CustomPowerModel : PowerModel, ICustomModel
	{
		public virtual string? CustomPackedIconPath => null;

		public virtual string? CustomBigIconPath => null;

		public virtual string? CustomBigBetaIconPath => null;
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class PackedIconPath
	{
		[HarmonyPrefix]
		private static bool Custom(PowerModel __instance, ref string? __result)
		{
			if (!(__instance is CustomPowerModel customPowerModel))
			{
				return true;
			}
			__result = customPowerModel.CustomPackedIconPath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class BigIconPath
	{
		[HarmonyPrefix]
		private static bool Custom(PowerModel __instance, ref string? __result)
		{
			if (!(__instance is CustomPowerModel customPowerModel))
			{
				return true;
			}
			__result = customPowerModel.CustomBigIconPath;
			return __result == null;
		}
	}
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	internal class BigBetaIconPath
	{
		[HarmonyPrefix]
		private static bool Custom(PowerModel __instance, ref string? __result)
		{
			if (!(__instance is CustomPowerModel customPowerModel))
			{
				return true;
			}
			__result = customPowerModel.CustomBigBetaIconPath;
			return __result == null;
		}
	}
	public abstract class CustomRelicModel : RelicModel, ICustomModel
	{
		public CustomRelicModel(bool autoAdd = true)
		{
			if (autoAdd)
			{
				CustomContentDictionary.AddModel(((object)this).GetType());
			}
		}

		public virtual RelicModel? GetUpgradeReplacement()
		{
			return null;
		}
	}
	public abstract class CustomRelicPoolModel : RelicPoolModel, ICustomModel, ICustomEnergyIconPool
	{
		public virtual bool IsShared => false;

		public override string EnergyColorName => CustomEnergyIconPatches.GetEnergyColorName(((AbstractModel)this).Id);

		public virtual string? BigEnergyIconPath => null;

		public virtual string? TextEnergyIconPath => null;

		public CustomRelicPoolModel()
		{
			if (IsShared)
			{
				ModelDbSharedRelicPoolsPatch.Register(this);
			}
		}

		protected override IEnumerable<RelicModel> GenerateAllRelics()
		{
			return Array.Empty<RelicModel>();
		}
	}
	public interface ICustomEnergyIconPool
	{
		string? BigEnergyIconPath { get; }

		string? TextEnergyIconPath { get; }
	}
	public interface ICustomModel
	{
	}
	public abstract class PlaceholderCharacterModel : CustomCharacterModel
	{
		public virtual string PlaceholderID => "ironclad";

		public override string CustomVisualPath => SceneHelper.GetScenePath("creature_visuals/" + PlaceholderID);

		public override string CustomTrailPath => SceneHelper.GetScenePath("vfx/card_trail_" + PlaceholderID);

		public override string? CustomMapMarkerPath => ImageHelper.GetImagePath("packed/map/icons/map_marker_" + PlaceholderID + ".png");

		public override string CustomIconPath => SceneHelper.GetScenePath("ui/character_icons/" + PlaceholderID + "_icon");

		public override string? CustomIconTexturePath => ImageHelper.GetImagePath("ui/top_panel/character_icon_" + PlaceholderID + ".png");

		public override string CustomEnergyCounterPath => SceneHelper.GetScenePath("combat/energy_counters/" + PlaceholderID + "_energy_counter");

		public override string CustomRestSiteAnimPath => SceneHelper.GetScenePath("rest_site/characters/" + PlaceholderID + "_rest_site");

		public override string CustomMerchantAnimPath => SceneHelper.GetScenePath("merchant/characters/" + PlaceholderID + "_merchant");

		public override string CustomArmPointingTexturePath => ImageHelper.GetImagePath("ui/hands/" + PlaceholderID + "_arm_point.png");

		public override string CustomArmRockTexturePath => ImageHelper.GetImagePath("ui/hands/" + PlaceholderID + "_arm_rock.png");

		public override string CustomArmPaperTexturePath => ImageHelper.GetImagePath("ui/hands/" + PlaceholderID + "_arm_paper.png");

		public override string CustomArmScissorsTexturePath => ImageHelper.GetImagePath("ui/hands/" + PlaceholderID + "_arm_scissors.png");

		public override string CustomCharacterSelectBg => SceneHelper.GetScenePath("screens/char_select/char_select_bg_" + PlaceholderID);

		public override string CustomCharacterSelectTransitionPath => "res://materials/transitions/" + PlaceholderID + "_transition_mat.tres";

		public override string? CustomCharacterSelectIconPath => ImageHelper.GetImagePath("packed/character_select/char_select_" + PlaceholderID + ".png");

		public override string? CustomCharacterSelectLockedIconPath => ImageHelper.GetImagePath("packed/character_select/char_select_" + PlaceholderID + "_locked.png");

		public override string CharacterSelectSfx => $"event:/sfx/characters/{PlaceholderID}/{PlaceholderID}_select";

		public override string CustomAttackSfx => $"event:/sfx/characters/{PlaceholderID}/{PlaceholderID}_attack";

		public override string CustomCastSfx => $"event:/sfx/characters/{PlaceholderID}/{PlaceholderID}_cast";

		public override string CustomDeathSfx => $"event:/sfx/characters/{PlaceholderID}/{PlaceholderID}_die";

		public override List<string> GetArchitectAttackVfx()
		{
			int num = 5;
			List<string> list = new List<string>(num);
			CollectionsMarshal.SetCount(list, num);
			Span<string> span = CollectionsMarshal.AsSpan(list);
			int num2 = 0;
			span[num2] = "vfx/vfx_attack_blunt";
			num2++;
			span[num2] = "vfx/vfx_heavy_blunt";
			num2++;
			span[num2] = "vfx/vfx_attack_slash";
			num2++;
			span[num2] = "vfx/vfx_bloody_impact";
			num2++;
			span[num2] = "vfx/vfx_rock_shatter";
			return list;
		}
	}
}
namespace System.Text.RegularExpressions.Generated
{
	[GeneratedCode("System.Text.RegularExpressions.Generator", "9.0.14.6317")]
	[SkipLocalsInit]
	internal sealed class <RegexGenerator_g>FF2DA1E499BC624FD7D66A9DDABE991E526D88E11BAA752E1772425438E11A4C9__SpecialCharRegex_0 : Regex
	{
		private sealed class RunnerFactory : RegexRunnerFactory
		{
			private sealed class Runner : RegexRunner
			{
				protected override void Scan(ReadOnlySpan<char> inputSpan)
				{
					if (TryFindNextPossibleStartingPosition(inputSpan))
					{
						int num = runtextpos;
						Capture(0, num, runtextpos = num + 1);
					}
				}

				private bool TryFindNextPossibleStartingPosition(ReadOnlySpan<char> inputSpan)
				{
					int num = runtextpos;
					if ((uint)num < (uint)inputSpan.Length)
					{
						int num2 = inputSpan.Slice(num).IndexOfAnyExcept(<RegexGenerator_g>FF2DA1E499BC624FD7D66A9DDABE991E526D88E11BAA752E1772425438E11A4C9__Utilities.s_asciiWordChars);
						if (num2 >= 0)
						{
							runtextpos = num + num2;
							return true;
						}
					}
					runtextpos = inputSpan.Length;
					return false;
				}
			}

			protected override RegexRunner CreateInstance()
			{
				return new Runner();
			}
		}

		internal static readonly <RegexGenerator_g>FF2DA1E499BC624FD7D66A9DDABE991E526D88E11BAA752E1772425438E11A4C9__SpecialCharRegex_0 Instance = new <RegexGenerator_g>FF2DA1E499BC624FD7D66A9DDABE991E526D88E11BAA752E1772425438E11A4C9__SpecialCharRegex_0();

		private <RegexGenerator_g>FF2DA1E499BC624FD7D66A9DDABE991E526D88E11BAA752E1772425438E11A4C9__SpecialCharRegex_0()
		{
			pattern = "[^a-zA-Z0-9_]";
			roptions = RegexOptions.None;
			Regex.ValidateMatchTimeout(<RegexGenerator_g>FF2DA1E499BC624FD7D66A9DDABE991E526D88E11BAA752E1772425438E11A4C9__Utilities.s_defaultTimeout);
			internalMatchTimeout = <RegexGenerator_g>FF2DA1E499BC624FD7D66A9DDABE991E526D88E11BAA752E1772425438E11A4C9__Utilities.s_defaultTimeout;
			factory = new RunnerFactory();
			capsize = 1;
		}
	}
	[GeneratedCode("System.Text.RegularExpressions.Generator", "9.0.14.6317")]
	internal static class <RegexGenerator_g>FF2DA1E499BC624FD7D66A9DDABE991E526D88E11BAA752E1772425438E11A4C9__Utilities
	{
		internal static readonly TimeSpan s_defaultTimeout = ((AppContext.GetData("REGEX_DEFAULT_MATCH_TIMEOUT") is TimeSpan timeSpan) ? timeSpan : Regex.InfiniteMatchTimeout);

		internal static readonly bool s_hasTimeout = s_defaultTimeout != Regex.InfiniteMatchTimeout;

		internal static readonly SearchValues<char> s_asciiWordChars = SearchValues.Create("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz");
	}
}
namespace System.Runtime.CompilerServices
{
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	internal sealed class IgnoresAccessChecksToAttribute : Attribute
	{
		public IgnoresAccessChecksToAttribute(string assemblyName)
		{
		}
	}
}
[CompilerGenerated]
internal sealed class <>z__ReadOnlyArray<T> : IEnumerable, ICollection, IList, IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, ICollection<T>, IList<T>
{
	int ICollection.Count => _items.Length;

	bool ICollection.IsSynchronized => false;

	object ICollection.SyncRoot => this;

	object? IList.this[int index]
	{
		get
		{
			return _items[index];
		}
		set
		{
			throw new NotSupportedException();
		}
	}

	bool IList.IsFixedSize => true;

	bool IList.IsReadOnly => true;

	int IReadOnlyCollection<T>.Count => _items.Length;

	T IReadOnlyList<T>.this[int index] => _items[index];

	int ICollection<T>.Count => _items.Length;

	bool ICollection<T>.IsReadOnly => true;

	T IList<T>.this[int index]
	{
		get
		{
			return _items[index];
		}
		set
		{
			throw new NotSupportedException();
		}
	}

	public <>z__ReadOnlyArray(T[] items)
	{
		_items = items;
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return ((IEnumerable)_items).GetEnumerator();
	}

	void ICollection.CopyTo(Array array, int index)
	{
		((ICollection)_items).CopyTo(array, index);
	}

	int IList.Add(object? value)
	{
		throw new NotSupportedException();
	}

	void IList.Clear()
	{
		throw new NotSupportedException();
	}

	bool IList.Contains(object? value)
	{
		return ((IList)_items).Contains(value);
	}

	int IList.IndexOf(object? value)
	{
		return ((IList)_items).IndexOf(value);
	}

	void IList.Insert(int index, object? value)
	{
		throw new NotSupportedException();
	}

	void IList.Remove(object? value)
	{
		throw new NotSupportedException();
	}

	void IList.RemoveAt(int index)
	{
		throw new NotSupportedException();
	}

	IEnumerator<T> IEnumerable<T>.GetEnumerator()
	{
		return ((IEnumerable<T>)_items).GetEnumerator();
	}

	void ICollection<T>.Add(T item)
	{
		throw new NotSupportedException();
	}

	void ICollection<T>.Clear()
	{
		throw new NotSupportedException();
	}

	bool ICollection<T>.Contains(T item)
	{
		return ((ICollection<T>)_items).Contains(item);
	}

	void ICollection<T>.CopyTo(T[] array, int arrayIndex)
	{
		((ICollection<T>)_items).CopyTo(array, arrayIndex);
	}

	bool ICollection<T>.Remove(T item)
	{
		throw new NotSupportedException();
	}

	int IList<T>.IndexOf(T item)
	{
		return ((IList<T>)_items).IndexOf(item);
	}

	void IList<T>.Insert(int index, T item)
	{
		throw new NotSupportedException();
	}

	void IList<T>.RemoveAt(int index)
	{
		throw new NotSupportedException();
	}
}
[CompilerGenerated]
internal sealed class <>z__ReadOnlyList<T> : IEnumerable, ICollection, IList, IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, ICollection<T>, IList<T>
{
	int ICollection.Count => _items.Count;

	bool ICollection.IsSynchronized => false;

	object ICollection.SyncRoot => this;

	object? IList.this[int index]
	{
		get
		{
			return _items[index];
		}
		set
		{
			throw new NotSupportedException();
		}
	}

	bool IList.IsFixedSize => true;

	bool IList.IsReadOnly => true;

	int IReadOnlyCollection<T>.Count => _items.Count;

	T IReadOnlyList<T>.this[int index] => _items[index];

	int ICollection<T>.Count => _items.Count;

	bool ICollection<T>.IsReadOnly => true;

	T IList<T>.this[int index]
	{
		get
		{
			return _items[index];
		}
		set
		{
			throw new NotSupportedException();
		}
	}

	public <>z__ReadOnlyList(List<T> items)
	{
		_items = items;
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return ((IEnumerable)_items).GetEnumerator();
	}

	void ICollection.CopyTo(Array array, int index)
	{
		((ICollection)_items).CopyTo(array, index);
	}

	int IList.Add(object? value)
	{
		throw new NotSupportedException();
	}

	void IList.Clear()
	{
		throw new NotSupportedException();
	}

	bool IList.Contains(object? value)
	{
		return ((IList)_items).Contains(value);
	}

	int IList.IndexOf(object? value)
	{
		return ((IList)_items).IndexOf(value);
	}

	void IList.Insert(int index, object? value)
	{
		throw new NotSupportedException();
	}

	void IList.Remove(object? value)
	{
		throw new NotSupportedException();
	}

	void IList.RemoveAt(int index)
	{
		throw new NotSupportedException();
	}

	IEnumerator<T> IEnumerable<T>.GetEnumerator()
	{
		return ((IEnumerable<T>)_items).GetEnumerator();
	}

	void ICollection<T>.Add(T item)
	{
		throw new NotSupportedException();
	}

	void ICollection<T>.Clear()
	{
		throw new NotSupportedException();
	}

	bool ICollection<T>.Contains(T item)
	{
		return _items.Contains(item);
	}

	void ICollection<T>.CopyTo(T[] array, int arrayIndex)
	{
		_items.CopyTo(array, arrayIndex);
	}

	bool ICollection<T>.Remove(T item)
	{
		throw new NotSupportedException();
	}

	int IList<T>.IndexOf(T item)
	{
		return _items.IndexOf(item);
	}

	void IList<T>.Insert(int index, T item)
	{
		throw new NotSupportedException();
	}

	void IList<T>.RemoveAt(int index)
	{
		throw new NotSupportedException();
	}
}
[CompilerGenerated]
internal sealed class <>z__ReadOnlySingleElementList<T> : IEnumerable, ICollection, IList, IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, ICollection<T>, IList<T>
{
	private sealed class Enumerator : IDisposable, IEnumerator, IEnumerator<T>
	{
		object IEnumerator.Current => _item;

		T IEnumerator<T>.Current => _item;

		public Enumerator(T item)
		{
			_item = item;
		}

		bool IEnumerator.MoveNext()
		{
			return !_moveNextCalled && (_moveNextCalled = true);
		}

		void IEnumerator.Reset()
		{
			_moveNextCalled = false;
		}

		void IDisposable.Dispose()
		{
		}
	}

	int ICollection.Count => 1;

	bool ICollection.IsSynchronized => false;

	object ICollection.SyncRoot => this;

	object? IList.this[int index]
	{
		get
		{
			if (index != 0)
			{
				throw new IndexOutOfRangeException();
			}
			return _item;
		}
		set
		{
			throw new NotSupportedException();
		}
	}

	bool IList.IsFixedSize => true;

	bool IList.IsReadOnly => true;

	int IReadOnlyCollection<T>.Count => 1;

	T IReadOnlyList<T>.this[int index]
	{
		get
		{
			if (index != 0)
			{
				throw new IndexOutOfRangeException();
			}
			return _item;
		}
	}

	int ICollection<T>.Count => 1;

	bool ICollection<T>.IsReadOnly => true;

	T IList<T>.this[int index]
	{
		get
		{
			if (index != 0)
			{
				throw new IndexOutOfRangeException();
			}
			return _item;
		}
		set
		{
			throw new NotSupportedException();
		}
	}

	public <>z__ReadOnlySingleElementList(T item)
	{
		_item = item;
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return new Enumerator(_item);
	}

	void ICollection.CopyTo(Array array, int index)
	{
		array.SetValue(_item, index);
	}

	int IList.Add(object? value)
	{
		throw new NotSupportedException();
	}

	void IList.Clear()
	{
		throw new NotSupportedException();
	}

	bool IList.Contains(object? value)
	{
		return EqualityComparer<T>.Default.Equals(_item, (T)value);
	}

	int IList.IndexOf(object? value)
	{
		return (!EqualityComparer<T>.Default.Equals(_item, (T)value)) ? (-1) : 0;
	}

	void IList.Insert(int index, object? value)
	{
		throw new NotSupportedException();
	}

	void IList.Remove(object? value)
	{
		throw new NotSupportedException();
	}

	void IList.RemoveAt(int index)
	{
		throw new NotSupportedException();
	}

	IEnumerator<T> IEnumerable<T>.GetEnumerator()
	{
		return new Enumerator(_item);
	}

	void ICollection<T>.Add(T item)
	{
		throw new NotSupportedException();
	}

	void ICollection<T>.Clear()
	{
		throw new NotSupportedException();
	}

	bool ICollection<T>.Contains(T item)
	{
		return EqualityComparer<T>.Default.Equals(_item, item);
	}

	void ICollection<T>.CopyTo(T[] array, int arrayIndex)
	{
		array[arrayIndex] = _item;
	}

	bool ICollection<T>.Remove(T item)
	{
		throw new NotSupportedException();
	}

	int IList<T>.IndexOf(T item)
	{
		return (!EqualityComparer<T>.Default.Equals(_item, item)) ? (-1) : 0;
	}

	void IList<T>.Insert(int index, T item)
	{
		throw new NotSupportedException();
	}

	void IList<T>.RemoveAt(int index)
	{
		throw new NotSupportedException();
	}
}
