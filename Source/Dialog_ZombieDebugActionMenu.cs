using HarmonyLib;
using LudeonTK;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public static class ZombieDebugActions
	{
		static void SpawnZombie(ZombieType type, bool appearDirectly)
		{
			var map = Find.CurrentMap;
			if (map == null)
				return;
			var cell = UI.MouseCell();
			if (cell.InBounds(map) == false)
				return;

			_ = ZombieRuntimeActions.SpawnZombie(cell, map, type, appearDirectly);
		}

		[DebugAction("Zombieland", "Spawn: Zombie (dig out)", actionType = DebugActionType.ToolMap)]
		private static void SpawnZombieDigOut()
		{
			SpawnZombie(ZombieType.Normal, false);
		}

		[DebugAction("Zombieland", "Spawn: Zombie (standing)", actionType = DebugActionType.ToolMap)]
		private static void SpawnZombieStanding()
		{
			SpawnZombie(ZombieType.Normal, true);
		}

		[DebugAction("Zombieland", "Spawn: Suicide bomber", actionType = DebugActionType.ToolMap)]
		private static void SpawnSuicideBomber()
		{
			SpawnZombie(ZombieType.SuicideBomber, true);
		}

		[DebugAction("Zombieland", "Spawn: Toxic Splasher", actionType = DebugActionType.ToolMap)]
		private static void SpawnToxicSplasher()
		{
			SpawnZombie(ZombieType.ToxicSplasher, true);
		}

		[DebugAction("Zombieland", "Spawn: Tanky Operator", actionType = DebugActionType.ToolMap)]
		private static void SpawnTankyOperator()
		{
			SpawnZombie(ZombieType.TankyOperator, true);
		}

		[DebugAction("Zombieland", "Spawn: Miner", actionType = DebugActionType.ToolMap)]
		private static void SpawnMiner()
		{
			SpawnZombie(ZombieType.Miner, true);
		}

		[DebugAction("Zombieland", "Spawn: Electrifier", actionType = DebugActionType.ToolMap)]
		private static void SpawnElectrifier()
		{
			SpawnZombie(ZombieType.Electrifier, true);
		}

		[DebugAction("Zombieland", "Spawn: Albino", actionType = DebugActionType.ToolMap)]
		private static void SpawnAlbino()
		{
			SpawnZombie(ZombieType.Albino, true);
		}

		[DebugAction("Zombieland", "Spawn: Dark Slimer", actionType = DebugActionType.ToolMap)]
		private static void SpawnDarkSlimer()
		{
			SpawnZombie(ZombieType.DarkSlimer, true);
		}

		[DebugAction("Zombieland", "Spawn: Healer", actionType = DebugActionType.ToolMap)]
		private static void SpawnHealer()
		{
			SpawnZombie(ZombieType.Healer, true);
		}

		[DebugAction("Zombieland", "Spawn: Random zombie", actionType = DebugActionType.ToolMap)]
		private static void SpawnRandomZombie()
		{
			SpawnZombie(ZombieType.Random, true);
		}

		[DebugAction("Zombieland", "Trigger: Incident")]
		private static void TriggerZombieIncident()
		{
			var map = Find.CurrentMap;
			var tm = map?.GetComponent<TickManager>();
			if (tm?.RuntimeReady != true || tm.incidentInfo?.parameters == null)
				return;
			var size = tm.incidentInfo.parameters.incidentSize;
			if (size > 0)
			{
				var success = ZombiesRising.TryExecute(map, size, IntVec3.Invalid, false, false);
				if (success == false)
					Log.Error("Incident creation failed. Most likely no valid spawn point found.");
			}
		}

		[DebugAction("Zombieland", "Trigger: Spitter Event")]
		private static void SpawnZombieSpitterEvent()
		{
			ZombieSpitter.Spawn(Find.CurrentMap);
		}

		[DebugAction("Zombieland", "Create Zombie Symbiant Event", allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void CreateZombieSymbiantEvent()
		{
			if (ZombieSymbiant.TrySpawnInBestRoom(Find.CurrentMap, false) == false)
				Log.Warning("Could not create a Zombie Symbiant event. The map may already have a Symbiant, no eligible host, or no valid room spawn cell.");
		}

		[DebugAction("Zombieland", "Spawn: Incident (4)", actionType = DebugActionType.ToolMap)]
		private static void SpawnZombieIncident_4()
		{
			_ = ZombiesRising.TryExecute(Find.CurrentMap, 4, UI.MouseCell(), false, true);
		}

		[DebugAction("Zombieland", "Spawn: Incident (25)", actionType = DebugActionType.ToolMap)]
		private static void SpawnZombieIncident_25()
		{
			_ = ZombiesRising.TryExecute(Find.CurrentMap, 25, UI.MouseCell(), false, true);
		}

		[DebugAction("Zombieland", "Spawn: Incident (100)", actionType = DebugActionType.ToolMap)]
		private static void SpawnZombieIncident_100()
		{
			_ = ZombiesRising.TryExecute(Find.CurrentMap, 100, UI.MouseCell(), false, true);
		}

		[DebugAction("Zombieland", "Spawn: Incident (200)", actionType = DebugActionType.ToolMap)]
		private static void SpawnZombieIncident_200()
		{
			_ = ZombiesRising.TryExecute(Find.CurrentMap, 200, UI.MouseCell(), false, true);
		}

		[DebugAction("Zombieland", "Spawn: Zombie Symbiant", actionType = DebugActionType.ToolMap)]
		private static void SpawnZombieSymbiant()
		{
			ZombieSymbiant.Spawn(Find.CurrentMap, UI.MouseCell());
		}

		[DebugAction("Zombieland", "Spawn: Zombie Spitter", actionType = DebugActionType.ToolMap)]
		private static void SpawnZombieSpitterOnCell()
		{
			ZombieSpitter.Spawn(Find.CurrentMap, UI.MouseCell());
		}

		[DebugAction("Zombieland", "Remove: All Zombies")]
		private static void RemoveAllZombies()
		{
			_ = ZombieRuntimeActions.DestroyZombies(Find.CurrentMap);
		}

		[DebugAction("Zombieland", "Convert: Make Zombie", actionType = DebugActionType.ToolMap)]
		private static void ConvertToZombie()
		{
			var map = Find.CurrentMap;
			foreach (var thing in map.thingGrid.ThingsAt(UI.MouseCell()))
			{
				if (thing is not Pawn pawn || pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter)
					continue;
				ZombieRuntimeActions.ConvertPawnToZombie(pawn, map, true);
			}
		}

		[DebugAction("Zombieland", "Apply: Trigger rotting", actionType = DebugActionType.ToolMap)]
		private static void ApplyTriggerRotting()
		{
			foreach (var thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
			{
				var compRottable = thing.TryGetComp<CompRottable>();
				if (compRottable != null)
					compRottable.RotProgress = compRottable.PropsRot.TicksToRotStart;
			}
		}

		[DebugAction("Zombieland", "Apply: Add zombie bite", actionType = DebugActionType.ToolMap)]
		private static void ApplyAddZombieBite()
		{
			foreach (var thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
			{
				if (thing is not Pawn pawn || pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter)
					continue;
				_ = ZombieRuntimeActions.AddZombieBite(pawn, "harmful", out _, out _);
			}
		}

		[DebugAction("Zombieland", "Apply: Remove infections", actionType = DebugActionType.ToolMap)]
		private static void ApplyRemoveInfections()
		{
			foreach (var thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
			{
				if (thing is not Pawn pawn || pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter)
					continue;
				_ = ZombieRuntimeActions.RemoveZombieInfections(pawn);
			}
		}

		[DebugAction("Zombieland", "Apply: Zombie raging", actionType = DebugActionType.ToolMap)]
		private static void ApplyZombieRaging()
		{
			foreach (var thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
			{
				if (thing is not Zombie zombie)
					continue;
				ZombieStateHandler.StartRage(zombie);
			}
		}

		[DebugAction("Zombieland", "Apply: Add 1% bloodloss", actionType = DebugActionType.ToolMap)]
		private static void ApplyHalfConsciousness()
		{
			foreach (var thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
			{
				if (thing is not Pawn pawn)
					continue;

				var hediff1 = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, pawn);
				hediff1.Severity = 0.1f;
				pawn.health.hediffSet.AddDirect(hediff1);
				var hediff2 = HediffMaker.MakeHediff(HediffDefOf.Anesthetic, pawn);
				hediff2.Severity = 0.1f;
				pawn.health.hediffSet.AddDirect(hediff2);
			}
		}

		[DebugAction("Zombieland", "Create Decontamination Quest")]
		private static void CreateDecontaminationQuest()
		{
			ContaminationManager.Instance.DecontaminationQuest();
		}

		static void FloodFillContamination(IntVec3 cell, float value, int maxCells)
		{
			ThingDef floodfillThingDef = null;
			var seen = new HashSet<Thing>();

			bool validator(IntVec3 cell)
			{
				if (floodfillThingDef == null)
				{
					var thing = Find.CurrentMap.thingGrid.ThingsAt(cell).First();
					floodfillThingDef = thing.def;
					return true;
				}
				return Find.CurrentMap.thingGrid.ThingsAt(cell).Any(t => t.def == floodfillThingDef);
			}

			void contaminate(IntVec3 cell)
			{
				Find.CurrentMap.thingGrid.ThingsAt(cell)
					.DoIf(t => seen.Contains(t) == false && t.def == floodfillThingDef, t => { seen.Add(t); t.AddContamination(value); });
			}

			// wrap this because if we click on "nothing" it causes an error
			try
			{
				var filler = new FloodFiller(Find.CurrentMap);
				filler.FloodFill(cell, validator, contaminate, maxCells);
			}
			catch
			{
			}
		}

		[DebugAction("Zombieland", "Apply: Add 0.01 contamination", actionType = DebugActionType.ToolMap)]
		private static void AddVeryLittleContamination()
		{
			FloodFillContamination(UI.MouseCell(), 0.01f, 500);
		}

		[DebugAction("Zombieland", "Apply: Add 0.1 contamination", actionType = DebugActionType.ToolMap)]
		private static void AddLittleContamination()
		{
			FloodFillContamination(UI.MouseCell(), 0.1f, 500);
		}

		[DebugAction("Zombieland", "Apply: Add 1.0 contamination", actionType = DebugActionType.ToolMap)]
		private static void AddSomeContamination()
		{
			FloodFillContamination(UI.MouseCell(), 1f, 500);
		}

		[DebugAction("Zombieland", "Apply: Clear contamination", actionType = DebugActionType.ToolMap)]
		private static void ClearContamination()
		{
			var cell = UI.MouseCell();
			var map = Find.CurrentMap;
			if (cell.InBounds(map))
				map.thingGrid.ThingsAt(cell).Do(thing => thing.ClearContamination());
		}

		[DebugAction("Zombieland", "Apply: Add 0.1 floor contamination", actionType = DebugActionType.ToolMap)]
		private static void AddSomeFloorContamination()
		{
			var cell = UI.MouseCell();
			var map = Find.CurrentMap;
			if (cell.InBounds(map))
			{
				var grid = map.GetContamination();
				grid[cell] = Mathf.Min(1f, grid[cell] + 0.1f);
			}
		}

		[DebugAction("Zombieland", "Apply: Clear floor contamination", actionType = DebugActionType.ToolMap)]
		private static void ClearFloorContamination()
		{
			var cell = UI.MouseCell();
			var map = Find.CurrentMap;
			if (cell.InBounds(map))
				map.SetContamination(cell, 0);
		}

		[DebugAction("Zombieland", "Apply: Contamination effect", actionType = DebugActionType.ToolMap)]
		private static void ContaminationEffect()
		{
			var map = Find.CurrentMap;
			var pawn = map.thingGrid.ThingAt<Pawn>(UI.MouseCell());
			if (pawn == null || pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter)
				return;
			var window = new Dialog_ContaminationDebugSettings(pawn);
			Find.WindowStack.Add(window);
		}
	}
}
