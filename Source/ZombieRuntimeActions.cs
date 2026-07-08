using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	static class ZombieRuntimeActions
	{
		public static string StableThingId(Thing thing)
		{
			return thing == null ? null : $"Thing_{thing.ThingID}";
		}

		public static object DescribeCell(IntVec3 cell)
		{
			if (cell.IsValid == false)
				return null;

			return new
			{
				x = cell.x,
				z = cell.z
			};
		}

		public static object DescribeCellRect(CellRect rect)
		{
			return new
			{
				minX = rect.minX,
				minZ = rect.minZ,
				maxX = rect.maxX,
				maxZ = rect.maxZ,
				width = rect.maxX - rect.minX + 1,
				height = rect.maxZ - rect.minZ + 1
			};
		}

		public static bool TryFindPawn(Map map, string target, out Pawn pawn, out string error)
		{
			pawn = null;
			error = null;
			if (map == null)
			{
				error = "No current map is loaded.";
				return false;
			}
			if (string.IsNullOrWhiteSpace(target))
			{
				error = "A pawn id, ThingID, label, or short name is required.";
				return false;
			}

			var query = target.Trim();
			pawn = map.mapPawns.AllPawnsSpawned.FirstOrDefault(candidate =>
				string.Equals(StableThingId(candidate), query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(candidate.ThingID, query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(candidate.LabelShortCap, query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(candidate.Name?.ToStringShort, query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(candidate.Name?.ToStringFull, query, StringComparison.OrdinalIgnoreCase));
			if (pawn != null)
				return true;

			error = $"No spawned pawn matched '{target}'.";
			return false;
		}

		public static Zombie SpawnZombie(IntVec3 cell, Map map, ZombieType zombieType, bool appearDirectly)
		{
			var zombie = ZombieGenerator.SpawnZombie(cell, map, zombieType);
			if (zombie == null)
				return null;

			if (appearDirectly && Current.ProgramState == ProgramState.Playing)
			{
				zombie.rubbleCounter = Constants.RUBBLE_AMOUNT;
				zombie.SetState(ZombieState.Wandering);
			}
			zombie.Rotation = Rot4.South;

			var tickManager = map.GetComponent<TickManager>();
			_ = tickManager?.allZombiesCached?.Add(zombie);
			return zombie;
		}

		public static int DestroyZombies(Map map)
		{
			var things = map.listerThings.AllThings
				.Where(thing => thing is Zombie || thing is ZombieSymbiant || thing is ZombieSpitter)
				.ToArray();
			foreach (var thing in things)
				thing.Destroy(DestroyMode.Vanish);

			var tickManager = map.GetComponent<TickManager>();
			tickManager?.allZombiesCached?.Clear();
			return things.Length;
		}

		public static bool AddZombieBite(Pawn pawn, string stage, out Hediff_Injury_ZombieBite bite, out string error)
		{
			bite = null;
			error = null;
			if (pawn == null || pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter)
			{
				error = "Target must be a non-zombie pawn.";
				return false;
			}
			if (pawn.health?.hediffSet == null)
			{
				error = "Target pawn has no health tracker.";
				return false;
			}

			var bodyPart = pawn.health.hediffSet
				.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
				.Where(part => part.def.IsSolid(part, pawn.health.hediffSet.hediffs) == false)
				.SafeRandomElement();
			if (bodyPart == null)
			{
				error = "No valid non-solid body part was found for a zombie bite.";
				return false;
			}

			bite = HediffMaker.MakeHediff(HediffDef.Named("ZombieBite"), pawn, bodyPart) as Hediff_Injury_ZombieBite;
			if (bite == null)
			{
				error = "Zombie bite hediff did not create the expected Hediff_Injury_ZombieBite instance.";
				return false;
			}
			if (bite.TendDuration?.ZombieInfector == null)
			{
				error = "Zombie bite hediff has no infector comp.";
				return false;
			}

			bite.mayBecomeZombieWhenDead = true;
			ApplyBiteStage(bite, stage);
			var damageInfo = new DamageInfo(CustomDefs.ZombieBite, 2);
			pawn.health.AddHediff(bite, bodyPart, damageInfo);
			ApplyBiteStage(bite, stage);
			return true;
		}

		public static int RemoveZombieInfections(Pawn pawn)
		{
			if (pawn?.health?.hediffSet == null)
				return 0;

			var bites = new List<Hediff_Injury_ZombieBite>();
			pawn.health.hediffSet.GetHediffs(ref bites);
			foreach (var bite in bites)
			{
				bite.mayBecomeZombieWhenDead = false;
				bite.TendDuration?.ZombieInfector?.MakeHarmless();
			}

			return pawn.health.hediffSet.hediffs.RemoveAll(hediff => hediff is Hediff_ZombieInfection);
		}

		public static void ConvertPawnToZombie(Pawn pawn, Map map, bool force)
		{
			Tools.ConvertToZombie(pawn, map, force);
		}

		public static bool KillPawnToCorpse(Pawn pawn, out Corpse corpse, out string error)
		{
			corpse = null;
			error = null;
			if (pawn == null || pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter)
			{
				error = "Target must be a non-zombie pawn.";
				return false;
			}

			if (pawn.Dead == false)
			{
				var previousProgramState = Current.ProgramState;
				try
				{
					Current.ProgramState = ProgramState.Entry;
					pawn.Kill(null);
				}
				finally
				{
					Current.ProgramState = previousProgramState;
				}
				Find.ColonistBar.MarkColonistsDirty();
			}

			corpse = pawn.Corpse;
			if (corpse == null || corpse.Destroyed)
			{
				error = "Killing the pawn did not leave a live corpse.";
				return false;
			}
			return true;
		}

		public static bool TriggerCorpseRotStageChanged(Corpse corpse, out RotStage before, out RotStage after, out string error)
		{
			before = RotStage.Fresh;
			after = RotStage.Fresh;
			error = null;
			if (corpse == null || corpse.Destroyed)
			{
				error = "Target corpse is missing or destroyed.";
				return false;
			}

			var compRottable = corpse.TryGetComp<CompRottable>();
			if (compRottable == null)
			{
				error = "Target corpse has no rottable comp.";
				return false;
			}

			before = corpse.GetRotStage();
			compRottable.RotProgress = Math.Max(compRottable.RotProgress, compRottable.PropsRot.TicksToRotStart);
			corpse.RotStageChanged();
			after = corpse.GetRotStage();
			return true;
		}

		public static bool RunQueuedConversion(Map map, ThingWithComps target, out int queueCountBefore, out int queueCountAfter, out string error)
		{
			queueCountBefore = 0;
			queueCountAfter = 0;
			error = null;
			var queue = map?.GetComponent<TickManager>()?.colonistsToConvert;
			if (queue == null)
			{
				error = "The current map has no Zombieland conversion queue.";
				return false;
			}

			queueCountBefore = queue.Count;
			var queued = false;
			var items = queue.ToArray();
			queue.Clear();
			foreach (var item in items)
			{
				if (queued == false && ReferenceEquals(item, target))
				{
					queued = true;
					continue;
				}
				queue.Enqueue(item);
			}

			queueCountAfter = queue.Count;
			if (queued == false)
			{
				error = "The target was not queued for zombie conversion.";
				return false;
			}

			Tools.ConvertToZombie(target, map);
			return true;
		}

		public static void ApplyBiteStage(Hediff_Injury_ZombieBite bite, string stage)
		{
			var infector = bite.TendDuration?.ZombieInfector;
			if (infector == null)
				return;

			switch ((stage ?? "harmful").Trim().ToLowerInvariant())
			{
				case "harmless":
					infector.MakeHarmless();
					break;
				case "final":
					infector.ForceFinalStage();
					break;
				default:
					infector.MakeHarmfull();
					break;
			}
		}

		public static InfectionState MaxInfectionState(Pawn pawn, List<Hediff_Injury_ZombieBite> bites)
		{
			var maxState = InfectionState.None;
			foreach (var bite in bites)
			{
				var state = bite.TendDuration?.GetInfectionState() ?? InfectionState.None;
				if (state > maxState)
					maxState = state;
			}
			return maxState;
		}

		public static object DescribePawnInfection(Pawn pawn)
		{
			var bites = new List<Hediff_Injury_ZombieBite>();
			pawn?.health?.hediffSet?.GetHediffs(ref bites);
			var maxState = pawn == null ? InfectionState.None : MaxInfectionState(pawn, bites);

			return new
			{
				pawnId = StableThingId(pawn),
				thingId = pawn?.ThingID,
				label = pawn?.LabelCap,
				dead = pawn?.Dead ?? false,
				spawned = pawn?.Spawned ?? false,
				position = pawn == null ? null : DescribeCell(pawn.Position),
				infectionState = maxState.ToString(),
				zombieInfectionHediffs = pawn?.health?.hediffSet?.hediffs?.Count(hediff => hediff is Hediff_ZombieInfection) ?? 0,
				biteCount = bites.Count,
				bites = bites.Select(bite =>
				{
					var tend = bite.TendDuration;
					var infector = tend?.ZombieInfector;
					return new
					{
						label = bite.LabelCap,
						part = bite.Part?.Label,
						severity = bite.Severity,
						mayBecomeZombieWhenDead = bite.mayBecomeZombieWhenDead,
						state = tend?.GetInfectionState().ToString(),
						infectionKnownDelay = infector?.infectionKnownDelay ?? 0,
						infectionStartTime = infector?.infectionStartTime ?? 0,
						infectionEndTime = infector?.infectionEndTime ?? 0,
						ticksBeforeStart = infector == null ? 0 : tend.TicksBeforeStartOfInfection(),
						ticksBeforeEnd = infector == null ? 0 : tend.TicksBeforeEndOfInfection()
					};
				}).ToArray()
			};
		}
	}
}
