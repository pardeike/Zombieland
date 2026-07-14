using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using static ZombieLand.Patches;

namespace ZombieLand
{
	static class ZombieRemover
	{
		public static void RemoveZombieland(string filename)
		{
			// clear caches
			seenBills.Clear();

			if (Current.Game == null || Current.Game.Maps == null || Find.World == null)
				return;
			Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;

			// note: order is kind of important here

			_ = Find.BattleLog.Battles.RemoveAll(battle =>
			{
				_ = battle.Entries.RemoveAll(entry => entry.GetConcerns().Any(th => th is Zombie));
				_ = battle.Entries.RemoveAll(entry => entry.GetConcerns().Any(th => th is ZombieSymbiant));
				_ = battle.Entries.RemoveAll(entry => entry.GetConcerns().Any(th => th is ZombieSpitter));
				return battle.concerns.Any(RemoveItem);
			});
			ClearRemovedBattleReferences();
			_ = Find.TaleManager.AllTalesListForReading.RemoveAll(tale =>
			{
				var singlePawnTale = tale as Tale_SinglePawn;
				if ((singlePawnTale?.pawnData?.pawn as Zombie) != null)
					return true;
				if ((singlePawnTale?.pawnData?.pawn as ZombieSymbiant) != null)
					return true;
				if ((singlePawnTale?.pawnData?.pawn as ZombieSpitter) != null)
					return true;
				var singlePawnDefTale = tale as Tale_SinglePawnAndDef;
				if (singlePawnDefTale?.defData?.def.IsZombieDef() ?? false)
					return true;
				var doublePawnTale = tale as Tale_DoublePawn;
				if ((doublePawnTale?.firstPawnData?.pawn as Zombie) != null)
					return true;
				if ((doublePawnTale?.firstPawnData?.pawn as ZombieSymbiant) != null)
					return true;
				if ((doublePawnTale?.firstPawnData?.pawn as ZombieSpitter) != null)
					return true;
				if ((doublePawnTale?.secondPawnData?.pawn as Zombie) != null)
					return true;
				if ((doublePawnTale?.secondPawnData?.pawn as ZombieSymbiant) != null)
					return true;
				if ((doublePawnTale?.secondPawnData?.pawn as ZombieSpitter) != null)
					return true;
				var doublePawnDefTale = tale as Tale_DoublePawnAndDef;
				if (doublePawnDefTale?.defData?.def.IsZombieDef() ?? false)
					return true;
				return false;
			});
			_ = Find.World.components.RemoveAll(component => component.IsZombieType());
			Current.Game.Maps.Do(CleanMap);
			Current.Game.Maps.Do(map => PawnsOfType<Pawn>(map).Do(RemovePawnRelatedStuff));
			RemoveWorldPawns();
			RemoveZombieFaction();
			RemoveOutfits();
			RemoveFoodRestrictions();
			SaveGameWithoutZombieland(filename);

			GenScene.GoToMainMenu();
		}

		static bool IsZombieType(this object obj)
		{
			if (obj == null)
				return false;
			return obj.GetType().Namespace == Tools.zlNamespace;
		}

		static bool IsZombieThing(this Thing thing)
		{
			if (thing == null)
				return false;
			if (thing.GetType().Namespace == Tools.zlNamespace)
				return true;
			if (thing.def.IsZombieDef())
				return true;
			return false;
		}

		public static bool IsZombieDef(this Def def)
		{
			if (def == null)
				return false;
			if (def.modContentPack?.PackageId == ZombielandMod.Identifier)
				return true;
			if (def.GetType().Namespace == Tools.zlNamespace)
				return true;
			if (def.defName == null)
				return false;
			if (def.defName.EndsWith("_Zombie", StringComparison.Ordinal))
				return true;
			if (def.defName.StartsWith("Zombie_", StringComparison.Ordinal))
				return true;
			if (def is ThingDef thingDef && thingDef.thingClass?.Namespace == Tools.zlNamespace)
				return true;
			return false;
		}

		static T[] PawnsOfType<T>(Map map)
		{
			return map.mapPawns.AllPawns.OfType<T>().ToArray();
		}

		static void CleanMap(Map map)
		{
			var zombies = PawnsOfType<Zombie>(map);
			foreach (var zombie in zombies)
				zombie.Destroy();

			var things = map.listerThings.AllThings.Where(thing => thing.IsZombieThing()).ToArray();
			foreach (var thing in things) // includes corpses
				thing.Destroy();

			map.haulDestinationManager.AllGroups
				.Select(slot => slot.Settings.filter)
				.Do(RemoveFromFilter);

			map.listerThings.AllThings.SelectMany(ContentOfFields<Bill>)
				.SelectMany(AllBills)
				.Select(bill => bill?.ingredientFilter).ToList()
				.Do(RemoveFromFilter);

			map.listerThings.AllThings.SelectMany(ContentOfFields<BillStack>)
				.Distinct()
				.ToList()
				.Do(CleanBillStack);

			map.listerThings.AllThings.SelectMany(ContentOfFields<StorageSettings>)
				.Select(settings => settings?.filter ?? new ThingFilter())
				.Do(RemoveFromFilter);

			_ = map.components.RemoveAll(component => component.IsZombieType());
		}

		static IEnumerable<T> ContentOfFields<T>(object instance) where T : class
		{
			return instance.GetType().GetFields(AccessTools.all)
				.Where(f => typeof(T).IsAssignableFrom(f.FieldType))
				.Select(f => f.GetValue(instance) as T);
		}

		static readonly HashSet<Bill> seenBills = new();

		static IEnumerable<Bill> AllBills(BillStack billStack)
		{
			var bills = billStack?.bills;
			if (bills == null)
				yield break;
			foreach (var bill in bills)
				if (bill != null && seenBills.Contains(bill) == false)
				{
					_ = seenBills.Add(bill);
					foreach (var innerBill in AllBills(bill))
						yield return innerBill;
				}
		}

		static IEnumerable<Bill> AllBills(Bill bill)
		{
			if (bill == null || seenBills.Contains(bill))
				yield break;
			_ = seenBills.Add(bill);
			yield return bill;
			foreach (var innerBill in AllBills(bill.billStack))
				if (bill != null && seenBills.Contains(bill) == false)
				{
					_ = seenBills.Add(bill);
					yield return innerBill;
				}
		}

		static void RemovePawnRelatedStuff(Pawn pawn)
		{
			if (pawn?.health?.hediffSet == null)
				return;

			var hediffs1 = pawn.GetHediffsList<Hediff_Injury_ZombieBite>();
			var hediffs2 = pawn.GetHediffsList<Hediff_MissingPart>().Where(hediff => hediff.lastInjury.IsZombieHediff());
			var hediffs3 = pawn.health.hediffSet.hediffs.Where(hediff => hediff.IsZombieType() || hediff.def.IsZombieDef()).ToArray();

			foreach (var hediff in hediffs1)
				pawn.health.RemoveHediff(hediff);
			foreach (var hediff in hediffs2)
				pawn.health.RemoveHediff(hediff);
			foreach (var hediff in hediffs3)
				pawn.health.RemoveHediff(hediff);

			_ = pawn.filth?.carriedFilth?.RemoveAll(filth => filth.IsZombieThing());
			RemovePawnHeldZombieThings(pawn);
			RemovePawnZombieJobsAndMentalStates(pawn);
			RemovePawnZombieNeeds(pawn);

			pawn.needs?.AllNeeds?.Do(need =>
			{
				var needMood = need as Need_Mood;
				_ = needMood?.thoughts?.memories?.Memories?.RemoveAll(IsZombieMemory);
			});
		}

		static void CleanBillStack(BillStack billStack)
		{
			if (billStack?.bills == null)
				return;

			_ = billStack.bills.RemoveAll(bill => bill?.recipe.IsZombieDef() == true);
			foreach (var bill in billStack.bills.ToArray())
				RemoveFromFilter(bill?.ingredientFilter);
		}

		static bool IsZombieMemory(Thought_Memory memory)
		{
			if (memory == null)
				return false;
			if (memory.IsZombieType() || memory.def.IsZombieDef())
				return true;
			if (memory.otherPawn is Zombie || memory.otherPawn is ZombieSymbiant || memory.otherPawn is ZombieSpitter)
				return true;
			return false;
		}

		static readonly FieldInfo f_PawnRecordsBattleActive = AccessTools.Field(typeof(Pawn_RecordsTracker), "battleActive");
		static readonly FieldInfo f_PawnRecordsBattleExitTick = AccessTools.Field(typeof(Pawn_RecordsTracker), "battleExitTick");
		static readonly FieldInfo f_NeedsTrackerNeeds = AccessTools.Field(typeof(Pawn_NeedsTracker), "needs");
		static readonly FieldInfo f_NeedsTrackerMiscNeeds = AccessTools.Field(typeof(Pawn_NeedsTracker), "needsMisc");

		static void ClearRemovedBattleReferences()
		{
			var remainingBattles = Find.BattleLog?.Battles?.ToHashSet() ?? new HashSet<Battle>();
			foreach (var pawn in Current.Game.Maps.SelectMany(PawnsOfType<Pawn>).Concat(EnumerateWorldPawns()))
			{
				var records = pawn.records;
				if (records == null)
					continue;
				if (f_PawnRecordsBattleActive.GetValue(records) is not Battle battle)
					continue;
				if (remainingBattles.Contains(battle))
					continue;
				f_PawnRecordsBattleActive.SetValue(records, null);
				f_PawnRecordsBattleExitTick.SetValue(records, 0);
			}
		}

		static IEnumerable<Pawn> EnumerateWorldPawns()
		{
			var fieldNames = new string[] { nameof(RimWorld.Planet.WorldPawns.pawnsAlive), nameof(RimWorld.Planet.WorldPawns.pawnsMothballed), nameof(RimWorld.Planet.WorldPawns.pawnsDead), nameof(RimWorld.Planet.WorldPawns.pawnsForcefullyKeptAsWorldPawns) };
			var trvWorldPawns = Traverse.Create(Current.Game.World.worldPawns);
			foreach (var fieldName in fieldNames)
				if (trvWorldPawns.Field(fieldName).GetValue<HashSet<Pawn>>() is HashSet<Pawn> pawnSet)
					foreach (var pawn in pawnSet)
						yield return pawn;
		}

		static void RemovePawnZombieNeeds(Pawn pawn)
		{
			var needs = pawn?.needs;
			if (needs == null)
				return;

			RemoveZombieNeedsFromList(f_NeedsTrackerNeeds.GetValue(needs) as List<Need>);
			RemoveZombieNeedsFromList(f_NeedsTrackerMiscNeeds.GetValue(needs) as List<Need>);
		}

		static void RemoveZombieNeedsFromList(List<Need> needs)
		{
			_ = needs?.RemoveAll(need => need.IsZombieType() || need.def.IsZombieDef());
		}

		static void RemovePawnZombieJobsAndMentalStates(Pawn pawn)
		{
			if (pawn.jobs?.AllJobs()?.Any(job => job?.def.IsZombieDef() == true) == true)
				pawn.jobs.StopAll(false, true);

			var mentalStateHandler = pawn.mindState?.mentalStateHandler;
			if (mentalStateHandler?.CurStateDef.IsZombieDef() == true || mentalStateHandler?.CurState.IsZombieType() == true)
				mentalStateHandler.Reset();
		}

		static void RemovePawnHeldZombieThings(Pawn pawn)
		{
			foreach (var apparel in pawn.apparel?.WornApparel?.Where(apparel => apparel.IsZombieThing()).ToArray() ?? Array.Empty<Apparel>())
			{
				pawn.apparel.Remove(apparel);
				if (apparel.Destroyed == false)
					apparel.Destroy();
			}

			foreach (var equipment in pawn.equipment?.AllEquipmentListForReading?.Where(equipment => equipment.IsZombieThing()).ToArray() ?? Array.Empty<ThingWithComps>())
				pawn.equipment.DestroyEquipment(equipment);

			RemoveZombieThingsFromOwner(pawn.inventory?.innerContainer);
			RemoveZombieThingsFromOwner(pawn.carryTracker?.innerContainer);
		}

		static void RemoveZombieThingsFromOwner(ThingOwner owner)
		{
			if (owner == null)
				return;
			foreach (var thing in owner.Where(thing => thing.IsZombieThing()).ToArray())
			{
				owner.Remove(thing);
				if (thing.Destroyed == false)
					thing.Destroy();
			}
		}

		static void RemoveWorldPawns()
		{
			var fieldNames = new string[] { nameof(RimWorld.Planet.WorldPawns.pawnsAlive), nameof(RimWorld.Planet.WorldPawns.pawnsMothballed), nameof(RimWorld.Planet.WorldPawns.pawnsDead), nameof(RimWorld.Planet.WorldPawns.pawnsForcefullyKeptAsWorldPawns) };
			var trvWorldPawns = Traverse.Create(Current.Game.World.worldPawns);
			foreach (var fieldName in fieldNames)
			{
				var pawnSet = trvWorldPawns.Field(fieldName).GetValue<HashSet<Pawn>>();
				_ = pawnSet.RemoveWhere(pawn => pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter);
				foreach (var pawn in pawnSet)
					RemovePawnRelatedStuff(pawn);
			}
		}

		static void RemoveZombieFaction()
		{
			var zombieFaction = Tools.GetZombieFaction();
			Current.Game.Maps.Do(map =>
			{
				var zombies = PawnsOfType<Zombie>(map);
				foreach (var zombie in zombies)
					map.pawnDestinationReservationManager.ReleaseAllClaimedBy(zombie);
				_ = map.pawnDestinationReservationManager.reservedDestinations.RemoveAll(pair => pair.Key == zombieFaction);
			});

			zombieFaction.RemoveAllRelations();
			var factions = Find.World.factionManager.AllFactions as List<Faction>;
			_ = factions.Remove(zombieFaction);
		}

		static void RemoveOutfits()
		{
			_ = Current.Game.outfitDatabase.AllOutfits.RemoveAll(item => item.IsZombieType());
			Current.Game.outfitDatabase.AllOutfits
				.Select(outfit => outfit.filter).ToList()
				.Do(RemoveFromFilter);
		}

		static void RemoveFoodRestrictions()
		{
			_ = Current.Game.foodRestrictionDatabase.AllFoodRestrictions.RemoveAll(item => item.IsZombieType());
			Current.Game.foodRestrictionDatabase.AllFoodRestrictions
				.Select(restriction => restriction.filter).ToList()
				.Do(RemoveFromFilter);

		}

		static void RemoveFromFilter(ThingFilter filter)
		{
			if (filter == null)
				return;
			var defs = filter.AllowedThingDefs.Where(def => def.IsZombieDef()).ToList();
			foreach (var def in defs)
				filter.SetAllow(def, false);
		}

		static bool RemoveItem(object obj)
		{
			if (obj == null)
				return false;
			if (obj.IsZombieType())
				return true;

			var f_def = AccessTools.Field(obj.GetType(), "def");
			if (f_def != null)
			{
				var def = f_def.GetValue(obj);
				var f_defName = AccessTools.Field(def.GetType(), "defName");
				if (f_defName != null)
				{
					if (f_defName.GetValue(def) is string defName && defName.StartsWith("Zombie_", StringComparison.Ordinal))
						return true;
				}
			}

			var remove = false;
			if (obj != null)
				Traverse.IterateFields(obj, field => { if (IsZombieType(field.GetValue())) remove = true; });
			return remove;
		}

		static void SaveGameWithoutZombieland(string filename)
		{
			var runningMods = LoadedModManager.RunningMods as List<ModContentPack>;
			var me = runningMods.First(mod => mod.PackageId == ZombielandMod.Identifier);
			var myIndex = runningMods.IndexOf(me);
			_ = runningMods.Remove(me);
			GameDataSaveLoader.SaveGame(filename);
			runningMods.Insert(myIndex, me);
		}
	}
}
