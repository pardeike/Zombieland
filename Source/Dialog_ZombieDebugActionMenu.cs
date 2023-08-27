using HarmonyLib;
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

			var zombie = ZombieGenerator.SpawnZombie(cell, map, type);
			if (Current.ProgramState != ProgramState.Playing)
				return;

			if (appearDirectly)
			{
				zombie.rubbleCounter = Constants.RUBBLE_AMOUNT;
				zombie.state = ZombieState.Wandering;
			}
			zombie.Rotation = Rot4.South;

			var tickManager = Find.CurrentMap.GetComponent<TickManager>();
			_ = tickManager.allZombiesCached.Add(zombie);
		}

		[DebugAction("Zombieland", "Spawn: Zombie (dig out)", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnZombieDigOut()
		{
			SpawnZombie(ZombieType.Normal, false);
		}

		[DebugAction("Zombieland", "Spawn: Zombie (standing)", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnZombieStanding()
		{
			SpawnZombie(ZombieType.Normal, true);
		}

		[DebugAction("Zombieland", "Spawn: Suicide bomber", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnSuicideBomber()
		{
			SpawnZombie(ZombieType.SuicideBomber, true);
		}

		[DebugAction("Zombieland", "Spawn: Toxic Splasher", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnToxicSplasher()
		{
			SpawnZombie(ZombieType.ToxicSplasher, true);
		}

		[DebugAction("Zombieland", "Spawn: Tanky Operator", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnTankyOperator()
		{
			SpawnZombie(ZombieType.TankyOperator, true);
		}

		[DebugAction("Zombieland", "Spawn: Miner", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnMiner()
		{
			SpawnZombie(ZombieType.Miner, true);
		}

		[DebugAction("Zombieland", "Spawn: Electrifier", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnElectrifier()
		{
			SpawnZombie(ZombieType.Electrifier, true);
		}

		[DebugAction("Zombieland", "Spawn: Albino", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnAlbino()
		{
			SpawnZombie(ZombieType.Albino, true);
		}

		[DebugAction("Zombieland", "Spawn: Dark Slimer", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnDarkSlimer()
		{
			SpawnZombie(ZombieType.DarkSlimer, true);
		}

		[DebugAction("Zombieland", "Spawn: Healer", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnHealer()
		{
			SpawnZombie(ZombieType.Healer, true);
		}

		[DebugAction("Zombieland", "Spawn: Random zombie", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnRandomZombie()
		{
			SpawnZombie(ZombieType.Random, true);
		}

		[DebugAction("Zombieland", "Trigger: Incident", false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void TriggerZombieIncident()
		{
			var tm = Find.CurrentMap.GetComponent<TickManager>();
			var size = tm.incidentInfo.parameters.incidentSize;
			if (size > 0)
			{
				var success = ZombiesRising.TryExecute(Find.CurrentMap, size, IntVec3.Invalid, false, false);
				if (success == false)
					Log.Error("Incident creation failed. Most likely no valid spawn point found.");
			}
		}

		[DebugAction("Zombieland", "Trigger: Spitter Event", false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnZombieSpitterEvent()
		{
			ZombieSpitter.Spawn(Find.CurrentMap);
		}

		[DebugAction("Zombieland", "Spawn: Incident (4)", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnZombieIncident_4()
		{
			_ = ZombiesRising.TryExecute(Find.CurrentMap, 4, UI.MouseCell(), false, true);
		}

		[DebugAction("Zombieland", "Spawn: Incident (25)", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnZombieIncident_25()
		{
			_ = ZombiesRising.TryExecute(Find.CurrentMap, 25, UI.MouseCell(), false, true);
		}

		[DebugAction("Zombieland", "Spawn: Incident (100)", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnZombieIncident_100()
		{
			_ = ZombiesRising.TryExecute(Find.CurrentMap, 100, UI.MouseCell(), false, true);
		}

		[DebugAction("Zombieland", "Spawn: Incident (200)", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnZombieIncident_200()
		{
			_ = ZombiesRising.TryExecute(Find.CurrentMap, 200, UI.MouseCell(), false, true);
		}

		[DebugAction("Zombieland", "Spawn: Zombie Spitter", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SpawnZombieSpitterOnCell()
		{
			ZombieSpitter.Spawn(Find.CurrentMap, UI.MouseCell());
		}

		[DebugAction("Zombieland", "Remove: All Zombies", false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void RemoveAllZombies()
		{
			var things = Find.CurrentMap.listerThings.AllThings.ToArray();
			foreach (var thing in things)
			{
				if (thing is Zombie || thing is ZombieSpitter)
					thing.Destroy();
			}
		}

		[DebugAction("Zombieland", "Convert: Make Zombie", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void ConvertToZombie()
		{
			var map = Find.CurrentMap;
			foreach (var thing in map.thingGrid.ThingsAt(UI.MouseCell()))
			{
				if (thing is not Pawn pawn || pawn is Zombie || pawn is ZombieSpitter)
					continue;
				Tools.ConvertToZombie(pawn, map, true);
			}
		}

		[DebugAction("Zombieland", "Apply: Trigger rotting", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void ApplyTriggerRotting()
		{
			foreach (var thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
			{
				var compRottable = thing.TryGetComp<CompRottable>();
				if (compRottable != null)
					compRottable.RotProgress = compRottable.PropsRot.TicksToRotStart;
			}
		}

		[DebugAction("Zombieland", "Apply: Add zombie bite", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void ApplyAddZombieBite()
		{
			foreach (var thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
			{
				if (thing is not Pawn pawn || pawn is Zombie || pawn is ZombieSpitter)
					continue;

				var bodyPart = pawn.health.hediffSet
						  .GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside, null, null)
					.Where(r => r.def.IsSolid(r, pawn.health.hediffSet.hediffs) == false)
					.SafeRandomElement();
				if (bodyPart == null)
					continue;

				var def = HediffDef.Named("ZombieBite");
				var bite = (Hediff_Injury_ZombieBite)HediffMaker.MakeHediff(def, pawn, bodyPart);
				if (bite.TendDuration?.ZombieInfector == null)
					continue;

				bite.mayBecomeZombieWhenDead = true;
				bite.TendDuration.ZombieInfector.MakeHarmfull();
				var damageInfo = new DamageInfo(CustomDefs.ZombieBite, 2);
				pawn.health.AddHediff(bite, bodyPart, damageInfo);
			}
		}

		[DebugAction("Zombieland", "Apply: Remove infections", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void ApplyRemoveInfections()
		{
			var tmpHediffInjuryZombieBites = new List<Hediff_Injury_ZombieBite>();
			foreach (var thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
			{
				if (thing is not Pawn pawn || pawn is Zombie || pawn is ZombieSpitter)
					continue;
				tmpHediffInjuryZombieBites.Clear();
				pawn.health.hediffSet.GetHediffs(ref tmpHediffInjuryZombieBites);
				tmpHediffInjuryZombieBites.Do(bite =>
					{
						bite.mayBecomeZombieWhenDead = false;
						var tendDuration = bite.TryGetComp<HediffComp_Zombie_TendDuration>();
						tendDuration.ZombieInfector.MakeHarmless();
					});

				_ = pawn.health.hediffSet.hediffs.RemoveAll(hediff => hediff is Hediff_ZombieInfection);
			}
		}

		[DebugAction("Zombieland", "Apply: Zombie raging", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void ApplyZombieRaging()
		{
			foreach (var thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
			{
				if (thing is not Zombie zombie)
					continue;
				ZombieStateHandler.StartRage(zombie);
			}
		}

		[DebugAction("Zombieland", "Apply: Add 1% bloodloss", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
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

		[DebugAction("Zombieland", "Create Decontamination Quest", false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
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
					.DoIf(t => seen.Contains(t) == false && t.def == floodfillThingDef, t => { seen.Add(t); t.AddContamination(value, null); });
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

		[DebugAction("Zombieland", "Apply: Add 0.01 contamination", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void AddVeryLittleContamination()
		{
			FloodFillContamination(UI.MouseCell(), 0.01f, 500);
		}

		[DebugAction("Zombieland", "Apply: Add 0.1 contamination", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void AddLittleContamination()
		{
			FloodFillContamination(UI.MouseCell(), 0.1f, 500);
		}

		[DebugAction("Zombieland", "Apply: Add 1.0 contamination", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void AddSomeContamination()
		{
			FloodFillContamination(UI.MouseCell(), 1f, 500);
		}

		[DebugAction("Zombieland", "Apply: Clear contamination", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void ClearContamination()
		{
			var cell = UI.MouseCell();
			var map = Find.CurrentMap;
			if (cell.InBounds(map))
				map.thingGrid.ThingsAt(cell).Do(thing => thing.ClearContamination());
		}

		[DebugAction("Zombieland", "Apply: Add 0.1 floor contamination", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
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

		[DebugAction("Zombieland", "Apply: Clear floor contamination", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void ClearFloorContamination()
		{
			var cell = UI.MouseCell();
			var map = Find.CurrentMap;
			if (cell.InBounds(map))
				map.SetContamination(cell, 0);
		}
	}
}