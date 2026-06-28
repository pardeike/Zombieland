using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/symbiant_pawn_stress_state", Description = "Set up or read a high map-pawn stress fixture for symbiant performance comparison.")]
		public static object SymbiantPawnStressState(
			[ToolParameter(Description = "Mode: read or setup.", Required = false, DefaultValue = "read")] string mode = "read",
			[ToolParameter(Description = "Target total spawned free colonists after setup.", Required = false, DefaultValue = 160)] int colonists = 160,
			[ToolParameter(Description = "Target total spawned player animals after setup.", Required = false, DefaultValue = 600)] int animals = 600,
			[ToolParameter(Description = "PawnKindDef defName for spawned player animals.", Required = false, DefaultValue = "Chicken")] string animalKindDefName = "Chicken",
			[ToolParameter(Description = "Raid points for the edge-walk-in immediate attack raid.", Required = false, DefaultValue = 10000f)] float raidPoints = 10000f,
			[ToolParameter(Description = "When true, trigger the max-points edge raid during setup.", Required = false, DefaultValue = true)] bool triggerRaid = true,
			[ToolParameter(Description = "When true, relocate/spawn player colonists inside the intended symbiant footprint for worst-case interaction testing.", Required = false, DefaultValue = false)] bool colonistsInsideSymbiant = false,
			[ToolParameter(Description = "Symbiant-center x coordinate used for colonist clustering. Use -1 with symbiantCenterZ -1 for map center.", Required = false, DefaultValue = -1)] int symbiantCenterX = -1,
			[ToolParameter(Description = "Symbiant-center z coordinate used for colonist clustering. Use -1 with symbiantCenterX -1 for map center.", Required = false, DefaultValue = -1)] int symbiantCenterZ = -1,
			[ToolParameter(Description = "Radius used to place colonists inside the future symbiant footprint.", Required = false, DefaultValue = 16f)] float colonistClusterRadius = 16f)
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			mode = (mode ?? "read").Trim().ToLowerInvariant();
			if (mode != "read" && mode != "setup")
			{
				return new
				{
					success = false,
					error = "mode must be read or setup.",
					mode
				};
			}

			object action = null;
			var actionSucceeded = true;
			if (mode == "setup")
			{
				var sw = Stopwatch.StartNew();
				var setup = SetupSymbiantPawnStressFixture(
					map,
					Math.Max(0, colonists),
					Math.Max(0, animals),
					animalKindDefName,
					Math.Max(1f, raidPoints),
					triggerRaid,
					colonistsInsideSymbiant,
					symbiantCenterX,
					symbiantCenterZ,
					Math.Max(4f, colonistClusterRadius));
				actionSucceeded = (bool)(setup.GetType().GetProperty("success")?.GetValue(setup) ?? false);
				sw.Stop();
				Find.TickManager?.Pause();
				action = new
				{
					setup,
					elapsedMs = sw.ElapsedMilliseconds,
					paused = Find.TickManager?.Paused ?? false
				};
			}

			var state = DescribeSymbiantPawnStressState(map);
			return new
			{
				success = mode == "read" || actionSucceeded,
				mode,
				action,
				state
			};
		}

		static object SetupSymbiantPawnStressFixture(
			Map map,
			int targetColonists,
			int targetAnimals,
			string animalKindDefName,
			float raidPoints,
			bool triggerRaid,
			bool colonistsInsideSymbiant,
			int symbiantCenterX,
			int symbiantCenterZ,
			float colonistClusterRadius)
		{
			var before = DescribeSymbiantPawnStressState(map);
			var existingColonistPawns = map.mapPawns.FreeColonistsSpawned
				.Where(pawn => pawn != null && pawn.Destroyed == false && pawn.Dead == false)
				.ToList();
			var existingColonists = existingColonistPawns.Count;
			var existingAnimals = CountPlayerAnimals(map);
			var colonistsToSpawn = Math.Max(0, targetColonists - existingColonists);
			var animalsToSpawn = Math.Max(0, targetAnimals - existingAnimals);
			List<IntVec3> colonistCells = null;
			HashSet<IntVec3> reservedCells = null;
			var symbiantCenter = symbiantCenterX >= 0 && symbiantCenterZ >= 0 ? new IntVec3(symbiantCenterX, 0, symbiantCenterZ) : map.Center;
			if (colonistsInsideSymbiant && targetColonists > 0)
			{
				colonistCells = CollectStressSpawnCellsAround(map, symbiantCenter, targetColonists, colonistClusterRadius, out var clusterError);
				if (colonistCells.Count < targetColonists)
				{
					return new
					{
						success = false,
						error = $"Only found {colonistCells.Count} clear clustered colonist cells for {targetColonists} requested colonists.",
						clusterError,
						symbiantCenter = ZombieRuntimeActions.DescribeCell(symbiantCenter),
						colonistClusterRadius,
						before
					};
				}
				reservedCells = new HashSet<IntVec3>(colonistCells);
			}

			var generalSpawnCount = colonistsInsideSymbiant ? animalsToSpawn : colonistsToSpawn + animalsToSpawn;
			var spawnCells = CollectStressSpawnCells(map, generalSpawnCount, out var cellError, reservedCells);
			if (spawnCells.Count < generalSpawnCount)
			{
				return new
				{
					success = false,
					error = $"Only found {spawnCells.Count} clear spawn cells for {generalSpawnCount} requested friendly pawns.",
					cellError,
					reservedCells = reservedCells?.Count ?? 0,
					before
				};
			}

			var animalKindDef = ResolveStressAnimalKind(animalKindDefName);
			if (animalKindDef == null && animalsToSpawn > 0)
			{
				return new
				{
					success = false,
					error = $"Could not resolve animal PawnKindDef '{animalKindDefName}' and no flesh animal fallback was available.",
					before
				};
			}

			var relocatedColonists = 0;
			var failedRelocations = new List<string>();
			if (colonistsInsideSymbiant)
			{
				var existingToRelocate = Math.Min(existingColonistPawns.Count, targetColonists);
				for (var i = 0; i < existingToRelocate; i++)
				{
					var pawn = existingColonistPawns[i];
					if (TryRelocateStressPawn(pawn, map, colonistCells[i], out var error))
					{
						DisablePawnWork(pawn);
						relocatedColonists++;
					}
					else
						failedRelocations.Add(error);
				}
			}

			var spawnedColonists = 0;
			var spawnedAnimals = 0;
			var cellIndex = 0;
			for (var i = 0; i < colonistsToSpawn; i++)
			{
				var pawn = GenerateStressPawn(PawnKindDefOf.Colonist, Faction.OfPlayer, true);
				if (pawn.Name is NameTriple)
					pawn.Name = new NameTriple("ZLStressColonist", (i + existingColonists + 1).ToString(), "Fixture");
				var cell = colonistsInsideSymbiant ? colonistCells[Math.Min(existingColonists, targetColonists) + i] : spawnCells[cellIndex++];
				GenSpawn.Spawn(pawn, cell, map, Rot4.South);
				DisablePawnWork(pawn);
				spawnedColonists++;
			}

			for (var i = 0; i < animalsToSpawn; i++)
			{
				var pawn = GenerateStressPawn(animalKindDef, Faction.OfPlayer, false);
				GenSpawn.Spawn(pawn, spawnCells[cellIndex++], map, Rot4.South);
				spawnedAnimals++;
			}

			var raid = triggerRaid ? TriggerStressRaid(map, raidPoints) : new { success = true, skipped = true };
			var after = DescribeSymbiantPawnStressState(map);
			return new
			{
				success = spawnedColonists == colonistsToSpawn
					&& failedRelocations.Count == 0
					&& spawnedAnimals == animalsToSpawn
					&& (bool)(raid.GetType().GetProperty("success")?.GetValue(raid) ?? false),
				targetColonists,
				targetAnimals,
				animalKindDef = animalKindDef?.defName,
				spawnedColonists,
				relocatedColonists,
				failedRelocations,
				spawnedAnimals,
				colonistsInsideSymbiant,
				symbiantCenter = colonistsInsideSymbiant ? ZombieRuntimeActions.DescribeCell(symbiantCenter) : null,
				colonistClusterRadius = colonistsInsideSymbiant ? colonistClusterRadius : 0f,
				raid,
				before,
				after
			};
		}

		static Pawn GenerateStressPawn(PawnKindDef kindDef, Faction faction, bool humanlike)
		{
			var request = new PawnGenerationRequest(
				kindDef,
				faction,
				PawnGenerationContext.NonPlayer,
				forceGenerateNewPawn: true,
				allowDead: false,
				allowDowned: false,
				canGeneratePawnRelations: false,
				mustBeCapableOfViolence: false,
				colonistRelationChanceFactor: 0f,
				allowPregnant: false,
				allowFood: false,
				allowAddictions: false,
				forceNoIdeo: true,
				forceNoBackstory: humanlike == false,
				developmentalStages: DevelopmentalStage.Adult,
				dontGiveWeapon: true,
				forceNoGear: true);
			return PawnGenerator.GeneratePawn(request);
		}

		static PawnKindDef ResolveStressAnimalKind(string animalKindDefName)
		{
			var named = DefDatabase<PawnKindDef>.GetNamedSilentFail(animalKindDefName ?? "");
			if (named?.RaceProps?.Animal == true)
				return named;
			return DefDatabase<PawnKindDef>.AllDefs
				.Where(def => def?.RaceProps?.Animal == true && def.RaceProps.IsFlesh)
				.OrderBy(def => def.race?.race?.baseBodySize ?? 999f)
				.FirstOrDefault();
		}

		static bool TryRelocateStressPawn(Pawn pawn, Map map, IntVec3 cell, out string error)
		{
			error = null;
			if (pawn == null)
			{
				error = "Pawn was null.";
				return false;
			}
			if (pawn.Destroyed || pawn.Dead)
			{
				error = $"{ZombieRuntimeActions.StableThingId(pawn)} was destroyed or dead.";
				return false;
			}
			if (pawn.Spawned == false || pawn.Map != map)
			{
				error = $"{ZombieRuntimeActions.StableThingId(pawn)} was not spawned on the target map.";
				return false;
			}
			if (cell.InBounds(map) == false || cell.Standable(map) == false || cell.Fogged(map))
			{
				error = $"Target cell {cell} was not a clear standable cell.";
				return false;
			}
			var occupyingPawn = cell.GetFirstPawn(map);
			if (occupyingPawn != null && occupyingPawn != pawn)
			{
				error = $"Target cell {cell} was occupied by {ZombieRuntimeActions.StableThingId(occupyingPawn)}.";
				return false;
			}
			if (pawn.Position == cell)
				return true;

			pawn.DeSpawn(DestroyMode.Vanish);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South, WipeMode.Vanish, respawningAfterLoad: false, forbidLeavings: false);
			if (pawn.Spawned == false || pawn.Map != map || pawn.Position != cell)
			{
				error = $"{ZombieRuntimeActions.StableThingId(pawn)} failed to respawn at {cell}.";
				return false;
			}
			pawn.Notify_Teleported(endCurrentJob: true, resetTweenedPos: true);
			return true;
		}

		static List<IntVec3> CollectStressSpawnCellsAround(Map map, IntVec3 center, int requiredCount, float radius, out string error)
		{
			error = null;
			var result = new List<IntVec3>(Math.Max(0, requiredCount));
			if (requiredCount <= 0)
				return result;

			var seen = new HashSet<IntVec3>();
			foreach (var cell in GenRadial.RadialCellsAround(center, radius, true))
			{
				if (result.Count >= requiredCount)
					break;
				if (seen.Add(cell)
					&& cell.InBounds(map)
					&& cell.Standable(map)
					&& cell.Fogged(map) == false
					&& cell.GetFirstPawn(map) == null)
					result.Add(cell);
			}

			if (result.Count < requiredCount)
				error = $"Found {result.Count} clear clustered spawn cells within radius {radius}; required {requiredCount}.";
			return result;
		}

		static List<IntVec3> CollectStressSpawnCells(Map map, int requiredCount, out string error, HashSet<IntVec3> reservedCells = null)
		{
			error = null;
			var result = new List<IntVec3>(Math.Max(0, requiredCount));
			if (requiredCount <= 0)
				return result;

			var seen = new HashSet<IntVec3>();
			void TryAdd(IntVec3 cell)
			{
				if (result.Count >= requiredCount || seen.Add(cell) == false)
					return;
				if (reservedCells?.Contains(cell) == true)
					return;
				if (cell.InBounds(map) && cell.Standable(map) && cell.Fogged(map) == false && cell.GetFirstPawn(map) == null)
					result.Add(cell);
			}

			var center = map.Center;
			foreach (var cell in CellRect.CenteredOn(center, 96, 96).ClipInsideMap(map).Cells)
				TryAdd(cell);
			foreach (var cell in map.AllCells)
				TryAdd(cell);

			if (result.Count < requiredCount)
				error = $"Found {result.Count} clear spawn cells; required {requiredCount}.";
			return result;
		}

		static object TriggerStressRaid(Map map, float raidPoints)
		{
			var raidDef = IncidentDefOf.RaidEnemy;
			var hostileFaction = Find.FactionManager.AllFactionsVisible
				.FirstOrDefault(faction => faction != null && faction.HostileTo(Faction.OfPlayer) && faction.def?.humanlikeFaction == true && faction.deactivated == false)
				?? Find.FactionManager.AllFactionsVisible.FirstOrDefault(faction => faction != null && faction.HostileTo(Faction.OfPlayer) && faction.deactivated == false);
			if (raidDef?.Worker == null || hostileFaction == null)
			{
				return new
				{
					success = false,
					error = raidDef?.Worker == null ? "IncidentDefOf.RaidEnemy has no worker." : "No active hostile faction was available."
				};
			}

			var hostilesBefore = CountHostileHumanlikePawns(map);
			var attackTargets = map.mapPawns.FreeColonistsSpawned
				.Take(16)
				.Cast<Thing>()
				.ToList();
			var parms = new IncidentParms
			{
				target = map,
				points = raidPoints,
				faction = hostileFaction,
				forced = true,
				bypassStorytellerSettings = true,
				silent = true,
				sendLetter = false,
				raidStrategy = RaidStrategyDefOf.ImmediateAttack,
				raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
				pawnGroupKind = PawnGroupKindDefOf.Combat,
				attackTargets = attackTargets,
				canTimeoutOrFlee = false,
				canSteal = false,
				canKidnap = false,
				raidNeverFleeIndividual = true
			};

			var executed = raidDef.Worker.TryExecute(parms);
			Find.TickManager?.Pause();
			var hostilesAfter = CountHostileHumanlikePawns(map);
			return new
			{
				success = executed && hostilesAfter > hostilesBefore,
				executed,
				points = raidPoints,
				faction = hostileFaction.def?.defName,
				raidStrategy = parms.raidStrategy?.defName,
				raidArrivalMode = parms.raidArrivalMode?.defName,
				spawnCenter = parms.spawnCenter.IsValid ? ZombieRuntimeActions.DescribeCell(parms.spawnCenter) : null,
				hostilesBefore,
				hostilesAfter,
				spawnedHostiles = hostilesAfter - hostilesBefore
			};
		}

		static object DescribeSymbiantPawnStressState(Map map)
		{
			var pawns = map.mapPawns.AllPawnsSpawned;
			var playerFaction = Faction.OfPlayer;
			var playerColonists = map.mapPawns.FreeColonistsSpawnedCount;
			var playerAnimals = CountPlayerAnimals(map);
			var hostileHumanlikes = CountHostileHumanlikePawns(map);
			var hostileAnimals = pawns.Count(pawn => pawn?.Faction != null
				&& pawn.Faction.HostileTo(playerFaction)
				&& pawn.RaceProps?.Animal == true
				&& pawn.Dead == false);
			var zombies = pawns.Count(pawn => pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter);
			var symbiant = ZombieSymbiant.ActiveSymbiant(map);
			var playerColonistsOnSymbiant = symbiant == null ? 0 : map.mapPawns.FreeColonistsSpawned.Count(pawn => pawn?.Dead == false && symbiant.ContainsCell(pawn.Position));
			var playerAnimalsOnSymbiant = symbiant == null ? 0 : pawns.Count(pawn => pawn?.Faction == playerFaction
				&& pawn.RaceProps?.Animal == true
				&& pawn.Dead == false
				&& symbiant.ContainsCell(pawn.Position));
			var hostileHumanlikesOnSymbiant = symbiant == null ? 0 : pawns.Count(pawn => pawn?.Faction != null
				&& pawn.Faction.HostileTo(playerFaction)
				&& pawn.RaceProps?.Humanlike == true
				&& pawn is not Zombie
				&& pawn is not ZombieSymbiant
				&& pawn is not ZombieSpitter
				&& pawn.Dead == false
				&& symbiant.ContainsCell(pawn.Position));
			var allSpawnedPawnsOnSymbiant = symbiant == null ? 0 : pawns.Count(pawn => pawn != null && pawn.Dead == false && symbiant.ContainsCell(pawn.Position));
			return new
			{
				mapId = map.uniqueID,
				tick = Find.TickManager?.TicksGame ?? -1,
				paused = Find.TickManager?.Paused ?? false,
				allSpawnedPawns = pawns.Count,
				playerColonists,
				playerAnimals,
				hostileHumanlikes,
				hostileAnimals,
				zombielandPawns = zombies,
				symbiant = symbiant == null ? null : new
					{
						id = ZombieRuntimeActions.StableThingId(symbiant),
						cellCount = symbiant.CellCount,
						maxCells = ZombieSymbiant.MaxCells,
						debugMaxCellsOverride = ZombieSymbiant.DebugMaxCellsOverride,
						occupancy = new
						{
							playerColonistsOnSymbiant,
							playerAnimalsOnSymbiant,
							hostileHumanlikesOnSymbiant,
							allSpawnedPawnsOnSymbiant
						}
					},
				downedOrDeadPlayerPawns = pawns.Count(pawn => pawn?.Faction == playerFaction && (pawn.Dead || pawn.Downed)),
				downedOrDeadHostiles = pawns.Count(pawn => pawn?.Faction != null && pawn.Faction.HostileTo(playerFaction) && (pawn.Dead || pawn.Downed))
			};
		}

		static int CountPlayerAnimals(Map map)
		{
			return map.mapPawns.AllPawnsSpawned.Count(pawn => pawn?.Faction == Faction.OfPlayer
				&& pawn.RaceProps?.Animal == true
				&& pawn.Dead == false);
		}

		static int CountHostileHumanlikePawns(Map map)
		{
			var playerFaction = Faction.OfPlayer;
			return map.mapPawns.AllPawnsSpawned.Count(pawn => pawn?.Faction != null
				&& pawn.Faction.HostileTo(playerFaction)
				&& pawn.RaceProps?.Humanlike == true
				&& pawn is not Zombie
				&& pawn is not ZombieSymbiant
				&& pawn is not ZombieSpitter
				&& pawn.Dead == false);
		}
	}
}
