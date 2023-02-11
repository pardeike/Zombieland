using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
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

		[DebugAction("Zombieland", "Convert: Make Zombie", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void ConvertToZombie()
		{
			var map = Find.CurrentMap;
			foreach (var thing in map.thingGrid.ThingsAt(UI.MouseCell()))
			{
				if (thing is not Pawn pawn || pawn is Zombie)
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
				if (thing is not Pawn pawn || pawn is Zombie)
					continue;

				var bodyPart = pawn.health.hediffSet
                    .GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside, null, null)
					.Where(r => r.def.IsSolid(r, pawn.health.hediffSet.hediffs) == false)
					.SafeRandomElement();
				if (bodyPart == null)
					continue;

				var def = HediffDef.Named("ZombieBite");
				var bite = (Hediff_Injury_ZombieBite)HediffMaker.MakeHediff(def, pawn, bodyPart);

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
				if (thing is not Pawn pawn || pawn is Zombie)
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
	}
}