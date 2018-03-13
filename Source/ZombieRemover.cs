using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Profile;
using static ZombieLand.Patches;

namespace ZombieLand
{
	static class ZombieRemover
	{
		public static void RemoveZombieland(string filename)
		{
			if (Current.Game == null || Current.Game.Maps == null || Find.World == null) return;
			Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;

			// note: order is kind of important here

			Find.BattleLog.RawEntries.RemoveAll(RemoveItem);
			Find.World.components.RemoveAll(component => component.IsZombieType());
			Current.Game.Maps.Do(CleanMap);
			Current.Game.Maps.Do(map => PawnsOfType<Pawn>(map).Do(RemovePawnRelatedStuff));
			RemoveWorldPawns();
			RemoveZombieFaction();
			RemoveOutfits();
			SaveGameWithoutZombieland(filename);

			MemoryUtility.ClearAllMapsAndWorld();
			GenScene.GoToMainMenu();
		}

		static bool IsZombieType(this object obj)
		{
			if (obj == null) return false;
			return obj.GetType().Namespace == Tools.zlNamespace;
		}

		static bool IsZombieThing(this Thing thing)
		{
			if (thing == null) return false;
			if (thing.GetType().Namespace == Tools.zlNamespace) return true;
			if (thing.def.IsZombieThingDef()) return true;
			return false;
		}

		public static bool IsZombieThingDef(this ThingDef thingdef)
		{
			if (thingdef.GetType().Namespace == Tools.zlNamespace) return true;
			if (thingdef.thingClass.Namespace == Tools.zlNamespace) return true;
			if (thingdef.defName.StartsWith("Zombie_", StringComparison.Ordinal)) return true;
			return false;
		}

		static T[] PawnsOfType<T>(Map map)
		{
			return map.mapPawns.AllPawns.OfType<T>().ToArray();
		}

		static void CleanMap(Map map)
		{
			map.components.RemoveAll(component => component.IsZombieType());
			PathFinder_FindPath_Patch.tickManagerCache = new Dictionary<Map, TickManager>();

			var zombies = PawnsOfType<Zombie>(map);
			foreach (var zombie in zombies)
				zombie.Destroy();

			var things = map.listerThings.AllThings.Where(thing => thing.IsZombieThing()).ToArray();
			foreach (var thing in things) // includes corpses
				thing.Destroy();

			map.zoneManager.AllZones.OfType<Zone_Stockpile>()
				.Select(pile => pile?.settings?.filter).ToList()
				.Do(RemoveFromFilter);

			map.slotGroupManager.AllGroups
				.Select(slot => slot.Settings.filter)
				.Do(RemoveFromFilter);

			map.listerThings.AllThings.OfType<Building_WorkTable>().SelectMany(table => table?.billStack?.Bills ?? new List<Bill>())
				.Select(bill => bill?.ingredientFilter).ToList()
				.Do(RemoveFromFilter);
		}

		static void RemovePawnRelatedStuff(Pawn pawn)
		{
			var hediffs1 = pawn.health.hediffSet.GetHediffs<Hediff_Injury_ZombieBite>().ToList();
			var hediffs2 = pawn.health.hediffSet.GetHediffs<Hediff_MissingPart>().Where(hediff => hediff.lastInjury.IsZombieHediff()).ToList();
			var hediffs3 = pawn.health.hediffSet.GetHediffs<Hediff_Injury>().Where(hediff => hediff.source.IsZombieType()).ToList();

			foreach (var hediff in hediffs1)
				pawn.health.RemoveHediff(hediff);
			foreach (var hediff in hediffs2)
				pawn.health.RemoveHediff(hediff);
			foreach (var hediff in hediffs3)
				pawn.health.RemoveHediff(hediff);

			var carriedFilth = Traverse.Create(pawn.filth).Field("carriedFilth").GetValue<List<Filth>>();
			carriedFilth?.RemoveAll(filth => filth.IsZombieThing());
		}

		static void RemoveWorldPawns()
		{
			var fieldNames = new string[] { "pawnsAlive", "pawnsMothballed", "pawnsDead", "pawnsForcefullyKeptAsWorldPawns" };
			var trvWorldPawns = Traverse.Create(Current.Game.World.worldPawns);
			foreach (var fieldName in fieldNames)
			{
				var pawnSet = trvWorldPawns.Field(fieldName).GetValue<HashSet<Pawn>>();
				pawnSet.RemoveWhere(pawn => pawn is Zombie);
				foreach (var pawn in pawnSet)
					RemovePawnRelatedStuff(pawn);
			}
		}

		static Faction GetZombieFaction()
		{
			return Find.World.factionManager.AllFactions.First(faction => faction.def == ZombieDefOf.Zombies);
		}

		static void RemoveZombieFaction()
		{
			var zombieFaction = GetZombieFaction();
			Current.Game.Maps.Do(map =>
			{
				var zombies = PawnsOfType<Zombie>(map);
				foreach (var zombie in zombies)
					map.pawnDestinationReservationManager.ReleaseAllClaimedBy(zombie);

				var trv = Traverse.Create(map.pawnDestinationReservationManager).Field("reservedDestinations");
				var reservedDestinations = trv.GetValue<Dictionary<Faction, PawnDestinationReservationManager.PawnDestinationSet>>();
				var reservedDestinations2 = new Dictionary<Faction, PawnDestinationReservationManager.PawnDestinationSet>();
				foreach (var key in reservedDestinations.Keys.Where(faction => faction != zombieFaction))
					reservedDestinations2.Add(key, reservedDestinations[key]);
				trv.SetValue(reservedDestinations2);
			});

			zombieFaction.RemoveAllRelations();
			var factions = Find.World.factionManager.AllFactions as List<Faction>;
			factions.Remove(zombieFaction);
		}

		static void RemoveOutfits()
		{
			Current.Game.outfitDatabase.AllOutfits.RemoveAll(item => item.IsZombieType());
			Current.Game.outfitDatabase.AllOutfits
				.Select(outfit => outfit.filter).ToList()
				.Do(RemoveFromFilter);
		}

		static void RemoveFromFilter(ThingFilter filter)
		{
			if (filter == null) return;
			var defs = filter.AllowedThingDefs.Where(def => def.IsZombieThingDef()).ToList();
			foreach (var def in defs)
				filter.SetAllow(def, false);
		}

		static bool RemoveItem(object obj)
		{
			if (obj == null) return false;
			if (obj.IsZombieType()) return true;

			var f_def = AccessTools.Field(obj.GetType(), "def");
			if (f_def != null)
			{
				var def = f_def.GetValue(obj);
				var f_defName = AccessTools.Field(def.GetType(), "defName");
				if (f_defName != null)
				{
					var defName = f_defName.GetValue(def) as string;
					if (defName != null && defName.StartsWith("Zombie_", StringComparison.Ordinal))
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
			var me = runningMods.First(mod => mod.Identifier == ZombielandMod.Identifier);
			var myIndex = runningMods.IndexOf(me);
			runningMods.Remove(me);
			GameDataSaveLoader.SaveGame(filename);
			runningMods.Insert(myIndex, me);
		}
	}
}