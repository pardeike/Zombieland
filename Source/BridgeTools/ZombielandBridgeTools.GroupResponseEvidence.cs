using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Verse;
using Verse.AI.Group;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		const int GroupResponseEventLimit = 512;
		static readonly SemaphoreSlim groupResponseMatrixGate = new(1, 1);
		static GroupResponseTrialState activeGroupResponseTrial;

		sealed class GroupResponseTrialEvent
		{
			public int tick { get; set; }
			public string kind { get; set; }
			public string mode { get; set; }
			public string previousMode { get; set; }
			public string actor { get; set; }
			public string target { get; set; }
			public string anchor { get; set; }
			public int? harmAge { get; set; }
			public int? capableShooters { get; set; }
			public int? zombiePressure { get; set; }
			public float? confidence { get; set; }
			public float? threshold { get; set; }
			public int relevantZombies { get; set; }
		}

		sealed class GroupResponseTrialState
		{
			public string trialId;
			public string relationship;
			public ZombieResponsePolicy policy;
			public Map map;
			public Lord lord;
			public IntVec3 center;
			public List<Pawn> members = new();
			public List<Zombie> zombies = new();
			public int ordinaryZombieCount;
			public int tankyZombieCount;
			public int spawnRadius;
			public int stagedTick;
			public int activatedTick;
			public int initialMemberCount;
			public int cacheHits;
			public int cacheMisses;
			public int responseModeTransitions;
			public int responseStutters;
			public int rangedShots;
			public int meleeAttacks;
			public int zombieAttackAttempts;
			public int zombieLandedHits;
			public int? firstZombieAttackTick;
			public int? firstZombieHarmTick;
			public int? firstFullTick;
			public int? firstRangedShotTick;
			public int? firstReturnToMinimalTick;
			public GroupResponseMode? lastObservedMode;
			public bool awaitingStutterRecovery;
			public string incidentDef;
			public bool incidentExecuted;
			public bool incidentFallback;
			public int incidentPawnCount;
			public Dictionary<Pawn, float> initialHealth = new();
			public Dictionary<Pawn, int> initialInjuries = new();
			public List<GroupResponseTrialEvent> events = new();

			public int RelevantZombieCount()
			{
				var count = 0;
				for (var i = 0; i < zombies.Count; i++)
				{
					var zombie = zombies[i];
					if (zombie?.Spawned == true && zombie.Dead == false && zombie.Downed == false && zombie.IsRopedOrConfused == false)
						count++;
				}
				return count;
			}

			public int DeadZombieCount()
			{
				return zombies.Count(zombie => zombie == null || zombie.Destroyed || zombie.Dead);
			}
		}

		public sealed class GroupResponseTrialResult
		{
			public bool success { get; set; }
			public string error { get; set; }
			public string trialId { get; set; }
			public int trialIndex { get; set; }
			public int seed { get; set; }
			public string relationship { get; set; }
			public string policy { get; set; }
			public int requestedShooters { get; set; }
			public int actualMembers { get; set; }
			public int ordinaryZombies { get; set; }
			public int tankyZombies { get; set; }
			public int elapsedTicks { get; set; }
			public int survivors { get; set; }
			public int spawnedSurvivors { get; set; }
			public int downed { get; set; }
			public int killed { get; set; }
			public int injuredSurvivors { get; set; }
			public int zombiesKilled { get; set; }
			public int zombiesRemaining { get; set; }
			public int rangedShots { get; set; }
			public int meleeAttacks { get; set; }
			public int zombieAttackAttempts { get; set; }
			public int zombieLandedHits { get; set; }
			public int cacheHits { get; set; }
			public int cacheMisses { get; set; }
			public int responseModeTransitions { get; set; }
			public int responseStutters { get; set; }
			public int? firstZombieAttackTick { get; set; }
			public int? firstZombieHarmTick { get; set; }
			public int? firstFullTick { get; set; }
			public int? firstRangedShotTick { get; set; }
			public int? firstReturnToMinimalTick { get; set; }
			public int? ticksToFirstRangedResponse { get; set; }
			public string completionReason { get; set; }
			public string requestedSpeed { get; set; }
			public string sampledSpeed { get; set; }
			public string incidentDef { get; set; }
			public bool incidentExecuted { get; set; }
			public bool incidentFallback { get; set; }
			public int incidentPawnCount { get; set; }
			public bool lordStillPresent { get; set; }
			public bool groupDispersed { get; set; }
			public object stage { get; set; }
			public object playSamples { get; set; }
			public object warnings { get; set; }
			public object cleanup { get; set; }
			public object events { get; set; }
		}

		sealed class GroupResponseMatrixException : Exception
		{
			public readonly string stage;

			public GroupResponseMatrixException(string stage, string message) : base(message)
			{
				this.stage = stage;
			}
		}

		[Tool("zombieland/group_response_stage_trial", Description = "Trigger and normalize one real visitor or raid incident into a reusable response encounter, then optionally activate its zombies.")]
		public static object GroupResponseStageTrial(
			[ToolParameter(Description = "Unique trial label.", Required = false, DefaultValue = "manual")] string trialId = "manual",
			[ToolParameter(Description = "friendly or enemy.", Required = false, DefaultValue = "friendly")] string relationship = "friendly",
			[ToolParameter(Description = "Minimal, Adaptive, or Full.", Required = false, DefaultValue = "Adaptive")] string policy = "Adaptive",
			[ToolParameter(Description = "Exact capable-shooter count after incident normalization.", Required = false, DefaultValue = 3)] int shooters = 3,
			[ToolParameter(Description = "Ordinary zombies to place around the group.", Required = false, DefaultValue = 4)] int ordinaryZombies = 4,
			[ToolParameter(Description = "Tanky zombies to place around the group.", Required = false, DefaultValue = 0)] int tankyZombies = 0,
			[ToolParameter(Description = "Maximum zombie placement radius, clamped to 2..16.", Required = false, DefaultValue = 12)] int spawnRadius = 12,
			[ToolParameter(Description = "Deterministic RimWorld random seed for staging.", Required = false, DefaultValue = 1701)] int seed = 1701,
			[ToolParameter(Description = "Spawn zombies and start telemetry immediately.", Required = false, DefaultValue = true)] bool activate = true)
		{
			return StageGroupResponseTrial(trialId, relationship, policy, shooters, ordinaryZombies, tankyZombies, spawnRadius, seed, activate);
		}

		[Tool("zombieland/group_response_trial_state", Description = "Read the bounded live telemetry and survival state for the active response trial.")]
		public static object ReadGroupResponseTrialState()
		{
			return DescribeGroupResponseTrialState(activeGroupResponseTrial);
		}

		[Tool("zombieland/group_response_activate_trial", Description = "Spawn the configured zombies and attach response/combat observers after an optional incident warm-up.")]
		public static object GroupResponseActivateTrial()
		{
			return ActivateGroupResponseTrial(activeGroupResponseTrial);
		}

		static object StageGroupResponseTrial(string trialId, string relationship, string policyName, int shooters, int ordinaryZombies, int tankyZombies, int spawnRadius, int seed, bool activate)
		{
			ClearGroupResponseTrialObservers();
			activeGroupResponseTrial = null;
			var map = CurrentMap;
			if (map?.GetComponent<TickManager>()?.RuntimeReady != true)
				return new { success = false, error = "A loaded playable map with a runtime-ready Zombieland TickManager is required." };
			var relation = (relationship ?? "friendly").Trim().ToLowerInvariant();
			if (relation != "friendly" && relation != "enemy")
				return new { success = false, error = "relationship must be friendly or enemy." };
			if (Enum.TryParse(policyName, true, out ZombieResponsePolicy policy) == false)
				return new { success = false, error = "policy must be Minimal, Adaptive, or Full." };
			if (shooters < 1 || shooters > 20 || ordinaryZombies < 0 || ordinaryZombies > 100 || tankyZombies < 0 || tankyZombies > 30)
				return new { success = false, error = "shooters must be 1..20, ordinaryZombies 0..100, and tankyZombies 0..30." };

			var faction = Find.FactionManager.AllFactionsVisible.FirstOrDefault(candidate => candidate != null
				&& candidate != Faction.OfPlayer
				&& candidate.def?.humanlikeFaction == true
				&& candidate.deactivated == false
				&& candidate.HostileTo(Faction.OfPlayer) == (relation == "enemy"));
			if (faction == null)
				return new { success = false, error = $"No active {relation} humanlike faction was available." };

			if (TryFindGroupResponseArenaCenter(map, out var center, out var arena, out var cellError) == false)
				return cellError;

			Rand.PushState(seed);
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				ApplyZombieSettingsOverride(settings =>
				{
					settings.attackMode = AttackMode.Everything;
					settings.zombiesDieOnZeroThreat = false;
					settings.zombieFreeEvents = false;
					settings.daysBeforeZombiesCome = 0;
					settings.zombiesDieVeryEasily = false;
					settings.doubleTapRequired = true;
					if (relation == "friendly")
					{
						settings.friendlyZombieResponse = policy;
						settings.enemyZombieResponse = ZombieResponsePolicy.Minimal;
					}
					else
					{
						settings.friendlyZombieResponse = ZombieResponsePolicy.Minimal;
						settings.enemyZombieResponse = policy;
					}
				});

				var beforePawns = map.mapPawns.AllPawnsSpawned.ToHashSet();
				var incidentDef = relation == "enemy"
					? IncidentDefOf.RaidEnemy
					: DefDatabase<IncidentDef>.GetNamed("VisitorGroup", false);
				var incidentExecuted = TryExecuteGroupResponseIncident(map, relation, faction, shooters, incidentDef);
				Find.TickManager.Pause();
				var incidentPawns = map.mapPawns.AllPawnsSpawned
					.Where(pawn => beforePawns.Contains(pawn) == false && pawn?.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer) == (relation == "enemy"))
					.ToList();
				var lord = incidentPawns.Where(pawn => pawn.RaceProps?.Humanlike == true)
					.GroupBy(pawn => pawn.GetLord())
					.Where(group => group.Key != null)
					.OrderByDescending(group => group.Count())
					.Select(group => group.Key)
					.FirstOrDefault();
				var incidentFallback = lord == null;
				if (lord == null)
				{
					LordJob lordJob = relation == "enemy"
						? new LordJob_AssaultColony(faction, true, true, false, false, true, false, true)
						: new LordJob_DefendPoint(center, 10f, 14f, false, false);
					lord = LordMaker.MakeNewLord(faction, lordJob, map);
				}

				var members = new List<Pawn>();
				foreach (var pawn in lord.ownedPawns.Where(pawn => pawn?.RaceProps?.Humanlike == true && pawn.Dead == false).ToArray())
				{
					EnsureGroupResponseRifle(pawn);
					if (GroupZombieResponse.IsCapableShooter(pawn, pawn))
						members.Add(pawn);
					if (members.Count == shooters)
						break;
				}
				var generationAttempts = 0;
				while (members.Count < shooters && generationAttempts++ < shooters * 20)
				{
					var generated = GenerateAreaWorkflowPawn(faction, true);
					GenSpawn.Spawn(generated, CellFinder.RandomClosewalkCellNear(center, map, 4), map, Rot4.South);
					lord.AddPawn(generated);
					incidentPawns.Add(generated);
					EnsureGroupResponseRifle(generated);
					if (GroupZombieResponse.IsCapableShooter(generated, generated))
						members.Add(generated);
				}

				for (var i = 0; i < incidentPawns.Count; i++)
					if (members.Contains(incidentPawns[i]) == false && incidentPawns[i]?.Destroyed == false)
						incidentPawns[i].Destroy(DestroyMode.Vanish);

				for (var i = 0; i < members.Count; i++)
				{
					var pawn = members[i];
					EnsureGroupResponseRifle(pawn);
					pawn.Name = new NameSingle($"ZL_Response_{trialId}_{i + 1}");
					pawn.jobs?.StopAll(false, true);
					var cell = FindGroupResponsePlacementCell(map, center, 1 + i / 8, i);
					if (pawn.Spawned)
						pawn.DeSpawn(DestroyMode.Vanish);
					GenSpawn.Spawn(pawn, cell, map, Rot4.West);
				}

				// Normalize the real incident Lord to an arena-centered defend phase after
				// relocating it. VisitorGroup already owns this toil; raids receive one in
				// their existing graph. This preserves incident pawn generation and Lord
				// identity while preventing either group from walking out of the fixture.
				var defendToil = lord.Graph?.lordToils?.OfType<LordToil_DefendPoint>().FirstOrDefault();
				if (defendToil == null && lord.Graph != null)
				{
					defendToil = new LordToil_DefendPoint(center);
					lord.Graph.AddToil(defendToil);
					defendToil.lord = lord;
				}
				if (defendToil != null && lord.CurLordToil != defendToil)
					lord.GotoToil(defendToil);

				var state = new GroupResponseTrialState
				{
					trialId = trialId,
					relationship = relation,
					policy = policy,
					map = map,
					lord = lord,
					center = center,
					members = members,
					ordinaryZombieCount = ordinaryZombies,
					tankyZombieCount = tankyZombies,
					spawnRadius = Math.Max(2, Math.Min(16, spawnRadius)),
					stagedTick = Find.TickManager.TicksGame,
					initialMemberCount = members.Count,
					incidentDef = incidentDef?.defName,
					incidentExecuted = incidentExecuted,
					incidentFallback = incidentFallback,
					incidentPawnCount = incidentPawns.Count
				};
				for (var i = 0; i < members.Count; i++)
				{
					var pawn = members[i];
					state.initialHealth[pawn] = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 0f;
					state.initialInjuries[pawn] = CountGroupResponseInjuries(pawn);
				}
				activeGroupResponseTrial = state;
				var activation = activate ? ActivateGroupResponseTrial(state) : null;
				return new
				{
					success = state.members.Count == shooters && (incidentExecuted || incidentFallback),
					trialId,
					relationship = relation,
					policy = policy.ToString(),
					faction = faction.def.defName,
					center = ZombieRuntimeActions.DescribeCell(center),
					arena,
					requestedShooters = shooters,
					actualMembers = state.members.Count,
					incident = new
					{
						definition = state.incidentDef,
						executed = state.incidentExecuted,
						fallback = state.incidentFallback,
						spawnedPawns = state.incidentPawnCount,
						lordJob = lord.LordJob?.GetType().FullName,
						lordToil = lord.CurLordToil?.GetType().FullName
					},
					members = state.members.Select(DescribeGroupResponseMember).ToArray(),
					activation
				};
			}
			finally
			{
				Rand.PopState();
			}
		}

		static bool TryFindGroupResponseArenaCenter(Map map, out IntVec3 center, out object diagnostics, out object error)
		{
			center = IntVec3.Invalid;
			diagnostics = null;
			error = null;
			var existingPawns = map.mapPawns.AllPawnsSpawned.Where(pawn => pawn?.Destroyed == false).Select(pawn => pawn.Position).ToArray();
			var playerBuildings = map.listerThings.AllThings
				.Where(thing => thing is Building && thing.Faction == Faction.OfPlayer && thing.Destroyed == false)
				.Select(thing => thing.Position)
				.ToArray();
			var avoidanceAnchors = existingPawns.Concat(playerBuildings).ToArray();
			var bestSeparation = -1;
			var bestOpenCells = -1;
			const int margin = 22;
			const int step = 8;
			for (var x = margin; x < map.Size.x - margin; x += step)
				for (var z = margin; z < map.Size.z - margin; z += step)
				{
					var candidate = new IntVec3(x, 0, z);
					if (candidate.Standable(map) == false || candidate.GetFirstPawn(map) != null || candidate.GetEdifice(map) != null)
						continue;
					var openCells = GenRadial.RadialCellsAround(candidate, 18f, true)
						.Count(cell => cell.InBounds(map) && cell.Standable(map) && cell.GetEdifice(map) == null);
					if (openCells < 600)
						continue;
					var separation = avoidanceAnchors.Length == 0
						? int.MaxValue
						: avoidanceAnchors.Min(anchor => anchor.DistanceToSquared(candidate));
					if (separation < bestSeparation || (separation == bestSeparation && openCells <= bestOpenCells))
						continue;
					center = candidate;
					bestSeparation = separation;
					bestOpenCells = openCells;
				}

			if (center.IsValid == false)
			{
				error = new { success = false, error = "No isolated open response arena was found on the current map." };
				return false;
			}
			diagnostics = new
			{
				openStandableCellsWithin18 = bestOpenCells,
				nearestPreExistingPawnOrPlayerBuildingDistance = bestSeparation == int.MaxValue ? (float?)null : (float)Math.Sqrt(bestSeparation),
				preExistingPawns = existingPawns.Length,
				playerBuildings = playerBuildings.Length
			};
			return true;
		}

		static bool TryExecuteGroupResponseIncident(Map map, string relationship, Faction faction, int shooters, IncidentDef incidentDef)
		{
			if (incidentDef?.Worker == null)
				return false;
			IncidentParms parms;
			if (relationship == "enemy")
			{
				parms = new IncidentParms
				{
					target = map,
					points = Math.Max(100f, shooters * 80f),
					faction = faction,
					forced = true,
					bypassStorytellerSettings = true,
					silent = true,
					sendLetter = false,
					raidStrategy = RaidStrategyDefOf.ImmediateAttack,
					raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
					pawnGroupKind = PawnGroupKindDefOf.Combat,
					canTimeoutOrFlee = false,
					canSteal = false,
					canKidnap = false,
					raidNeverFleeIndividual = true
				};
			}
			else
			{
				parms = StorytellerUtility.DefaultParmsNow(incidentDef.category, map);
				parms.target = map;
				parms.points = Math.Max(parms.points, shooters * 60f);
				parms.faction = faction;
				parms.forced = true;
				parms.bypassStorytellerSettings = true;
				parms.silent = true;
				parms.sendLetter = false;
			}
			try
			{
				GroupZombieResponse.disablePawnRelationsForEvidence = true;
				return incidentDef.Worker.TryExecute(parms);
			}
			catch (Exception ex)
			{
				Log.Warning($"Zombieland response evidence incident fallback after {incidentDef.defName}: {ex.GetType().Name}: {ex.Message}");
				return false;
			}
			finally
			{
				GroupZombieResponse.disablePawnRelationsForEvidence = false;
			}
		}

		static void EnsureGroupResponseRifle(Pawn pawn)
		{
			pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
				?? DefDatabase<ThingDef>.GetNamed("Gun_AssaultRifle", false)
				?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
			var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef) as ThingWithComps;
			if (weapon != null)
				pawn.equipment?.AddEquipment(weapon);
		}

		static IntVec3 FindGroupResponsePlacementCell(Map map, IntVec3 center, int radius, int ordinal)
		{
			var cells = GenRadial.RadialCellsAround(center, radius, true).ToArray();
			for (var offset = 0; offset < cells.Length; offset++)
			{
				var cell = cells[(ordinal + offset) % cells.Length];
				if (cell.InBounds(map) && cell.Standable(map) && cell.GetFirstPawn(map) == null)
					return cell;
			}
			return CellFinder.RandomClosewalkCellNear(center, map, Math.Max(2, radius + 2));
		}

		static object ActivateGroupResponseTrial(GroupResponseTrialState state)
		{
			if (state?.map == null || state.map != CurrentMap)
				return new { success = false, error = "No staged response trial exists on the current map." };
			if (state.activatedTick > 0)
				return new { success = false, error = "The staged response trial is already active." };

			var map = state.map;
			var tickManager = map.GetComponent<TickManager>();
			var total = state.ordinaryZombieCount + state.tankyZombieCount;
			var candidateCells = GenRadial.RadialCellsAround(state.center, state.spawnRadius, true)
				.Where(cell => cell.InBounds(map)
					&& cell.Standable(map)
					&& cell.GetFirstPawn(map) == null
					&& cell.GetThingList(map).Any(thing => thing is Building || thing is Fire) == false)
				.OrderBy(cell => cell.DistanceToSquared(state.center))
				.ToList();
			var outerBandMinimum = Math.Max(4, Math.Min(10, state.spawnRadius - 2));
			var outerCells = candidateCells
				.Where(cell => cell.DistanceToSquared(state.center) >= outerBandMinimum * outerBandMinimum)
				.ToList();
			if (outerCells.Count == 0)
				outerCells = candidateCells;
			for (var i = 0; i < total && i < candidateCells.Count; i++)
			{
				var cell = i == 0
					? candidateCells.FirstOrDefault(candidate => candidate.DistanceToSquared(state.center) <= 2)
					: outerCells[Math.Min(outerCells.Count - 1, (i - 1) * outerCells.Count / Math.Max(1, total - 1))];
				if (cell.IsValid == false)
					cell = candidateCells[i];
				var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
				if (zombie == null)
					continue;
				zombie.SetState(ZombieState.Tracking);
				zombie.wanderDestination = state.center;
				if (i >= state.ordinaryZombieCount)
				{
					zombie.hasTankyHelmet = 1f;
					zombie.hasTankySuit = 1f;
				}
				state.zombies.Add(zombie);
				_ = tickManager.allZombiesCached.Add(zombie);
			}

			// Use the real attraction surface that movement and gunshots feed. This
			// keeps staged zombies converging through their ordinary Stumble behavior.
			RefreshGroupResponseAttraction(state);

			state.activatedTick = Find.TickManager.TicksGame;
			GroupZombieResponse.ResetMapOwnedState();
			AttachGroupResponseTrialObservers(state);
			return new
			{
				success = state.zombies.Count == total,
				activatedTick = state.activatedTick,
				requestedZombies = total,
				spawnedZombies = state.zombies.Count,
				ordinary = state.zombies.Count(zombie => zombie.IsTanky == false),
				tanky = state.zombies.Count(zombie => zombie.IsTanky),
				center = ZombieRuntimeActions.DescribeCell(state.center),
				spawnRadius = state.spawnRadius
			};
		}

		static void RefreshGroupResponseAttraction(GroupResponseTrialState state)
		{
			if (state?.map == null || state.map != CurrentMap)
				return;
			var now = Tools.Ticks();
			var grid = state.map.GetGrid();
			foreach (var offset in Tools.GetCircle(state.spawnRadius + 2f))
				grid.BumpTimestamp(state.center + offset, now - offset.LengthHorizontalSquared);
			for (var i = 0; i < state.zombies.Count; i++)
			{
				var zombie = state.zombies[i];
				if (zombie?.Spawned != true || zombie.Dead || zombie.Downed || zombie.IsRopedOrConfused)
					continue;
				zombie.wanderDestination = state.center;
			}
		}

		static void AttachGroupResponseTrialObservers(GroupResponseTrialState state)
		{
			GroupZombieResponse.evaluationObserver = evaluation =>
			{
				if (activeGroupResponseTrial != state || evaluation.lord != state.lord)
					return;
				if (evaluation.cacheHit)
				{
					state.cacheHits++;
					return;
				}
				state.cacheMisses++;
				var relevant = state.RelevantZombieCount();
				var isTransition = evaluation.previousMode != evaluation.mode;
				if (isTransition)
				{
					state.responseModeTransitions++;
					if (evaluation.mode == GroupResponseMode.Full)
					{
						state.firstFullTick ??= evaluation.tick;
						if (state.awaitingStutterRecovery && relevant > 0)
							state.responseStutters++;
						state.awaitingStutterRecovery = false;
					}
					else if (evaluation.previousMode == GroupResponseMode.Full)
					{
						state.firstReturnToMinimalTick ??= evaluation.tick;
						state.awaitingStutterRecovery = relevant > 0;
					}
				}
				state.lastObservedMode = evaluation.mode;
				if (state.events.Count < GroupResponseEventLimit && (isTransition || state.cacheMisses <= 32))
					state.events.Add(new GroupResponseTrialEvent
					{
						tick = evaluation.tick,
						kind = isTransition ? "modeTransition" : "evaluation",
						mode = evaluation.mode.ToString(),
						previousMode = evaluation.previousMode.ToString(),
						anchor = evaluation.anchor?.Name?.ToStringShort,
						harmAge = evaluation.harmAge,
						capableShooters = evaluation.capableShooters,
						zombiePressure = evaluation.zombiePressure,
						confidence = evaluation.confidence,
						threshold = evaluation.threshold,
						relevantZombies = relevant
					});
			};

			GroupZombieResponse.combatObserver = observation =>
			{
				if (activeGroupResponseTrial != state)
					return;
				var actorMember = state.members.Contains(observation.actor);
				var targetMember = state.members.Contains(observation.target);
				var targetZombie = observation.target is Zombie zombie && state.zombies.Contains(zombie);
				if (actorMember == false && targetMember == false && targetZombie == false)
					return;
				switch (observation.kind)
				{
					case GroupCombatObservationKind.ZombieMeleeAttempt when targetMember:
						state.zombieAttackAttempts++;
						state.firstZombieAttackTick ??= observation.tick;
						break;
					case GroupCombatObservationKind.ZombieLandedHarm when targetMember:
						state.zombieLandedHits++;
						state.firstZombieHarmTick ??= observation.tick;
						break;
					case GroupCombatObservationKind.GroupMeleeAttack when actorMember:
						state.meleeAttacks++;
						break;
					case GroupCombatObservationKind.GroupRangedShot when actorMember:
						state.rangedShots++;
						state.firstRangedShotTick ??= observation.tick;
						break;
					case GroupCombatObservationKind.ZombieDeath when targetZombie:
						break;
					default:
						return;
				}
				if (state.events.Count < GroupResponseEventLimit)
					state.events.Add(new GroupResponseTrialEvent
					{
						tick = observation.tick,
						kind = observation.kind.ToString(),
						actor = observation.actor?.Name?.ToStringShort,
						target = observation.target?.Name?.ToStringShort,
						relevantZombies = state.RelevantZombieCount()
					});
			};
		}

		static object DescribeGroupResponseTrialState(GroupResponseTrialState state)
		{
			if (state == null)
				return new { success = false, error = "No active group-response trial exists." };
			var survivors = state.members.Count(pawn => pawn?.Dead == false);
			var spawnedSurvivors = state.members.Count(pawn => pawn?.Spawned == true && pawn.Dead == false);
			var downed = state.members.Count(pawn => pawn?.Dead == false && pawn.Downed);
			var remaining = state.RelevantZombieCount();
			return new
			{
				success = true,
				state.trialId,
				state.relationship,
				policy = state.policy.ToString(),
				stagedTick = state.stagedTick,
				activatedTick = state.activatedTick,
				currentTick = Find.TickManager?.TicksGame ?? -1,
				elapsedTicks = state.activatedTick <= 0 ? 0 : Find.TickManager.TicksGame - state.activatedTick,
				initialMembers = state.initialMemberCount,
				survivors,
				spawnedSurvivors,
				downed,
				zombiesRemaining = remaining,
				zombiesKilled = state.DeadZombieCount(),
				state.rangedShots,
				state.meleeAttacks,
				state.zombieAttackAttempts,
				state.zombieLandedHits,
				state.cacheHits,
				state.cacheMisses,
				state.responseModeTransitions,
				state.responseStutters,
				state.firstZombieAttackTick,
				state.firstZombieHarmTick,
				state.firstFullTick,
				state.firstRangedShotTick,
				state.firstReturnToMinimalTick,
				groupEliminated = survivors == 0,
				zombiesEliminated = remaining == 0,
				lordStillPresent = state.lord != null && state.map?.lordManager?.lords?.Contains(state.lord) == true,
				groupDispersed = spawnedSurvivors < survivors,
				eventCount = state.events.Count,
				eventsTruncated = state.events.Count >= GroupResponseEventLimit,
				members = state.members.Select(pawn => new
				{
					name = pawn?.Name?.ToStringShort,
					spawned = pawn?.Spawned,
					dead = pawn?.Dead,
					downed = pawn?.Downed,
					health = pawn?.health?.summaryHealth?.SummaryHealthPercent,
					job = pawn?.CurJobDef?.defName,
					target = pawn?.CurJob?.targetA.Thing?.LabelShort
				}).ToArray(),
				zombies = state.zombies.Select(zombie => new
				{
					id = ZombieRuntimeActions.StableThingId(zombie),
					spawned = zombie?.Spawned,
					dead = zombie?.Dead,
					downed = zombie?.Downed,
					state = zombie?.state.ToString(),
					job = zombie?.CurJobDef?.defName
				}).ToArray(),
				events = state.events.ToArray()
			};
		}

		static GroupResponseTrialResult FinishGroupResponseTrial(int trialIndex, int seed, int requestedShooters, string requestedSpeed, string sampledSpeed, string completionReason, object stage, object playSamples, object warnings)
		{
			var state = activeGroupResponseTrial;
			if (state == null)
				return new GroupResponseTrialResult { success = false, error = "No active group-response trial exists." };
			var survivors = state.members.Count(pawn => pawn?.Dead == false);
			var spawnedSurvivors = state.members.Count(pawn => pawn?.Spawned == true && pawn.Dead == false);
			var downed = state.members.Count(pawn => pawn?.Dead == false && pawn.Downed);
			var remaining = state.RelevantZombieCount();
			var injuredSurvivors = state.members.Count(pawn => pawn?.Dead == false
				&& ((pawn.health?.summaryHealth?.SummaryHealthPercent ?? 0f) < InitialGroupResponseHealth(state, pawn)
					|| CountGroupResponseInjuries(pawn) > InitialGroupResponseInjuries(state, pawn)));
			var result = new GroupResponseTrialResult
			{
				success = true,
				trialId = state.trialId,
				trialIndex = trialIndex,
				seed = seed,
				relationship = state.relationship,
				policy = state.policy.ToString(),
				requestedShooters = requestedShooters,
				actualMembers = state.initialMemberCount,
				ordinaryZombies = state.ordinaryZombieCount,
				tankyZombies = state.tankyZombieCount,
				elapsedTicks = Math.Max(0, (Find.TickManager?.TicksGame ?? state.activatedTick) - state.activatedTick),
				survivors = survivors,
				spawnedSurvivors = spawnedSurvivors,
				downed = downed,
				killed = state.initialMemberCount - survivors,
				injuredSurvivors = injuredSurvivors,
				zombiesKilled = state.DeadZombieCount(),
				zombiesRemaining = remaining,
				rangedShots = state.rangedShots,
				meleeAttacks = state.meleeAttacks,
				zombieAttackAttempts = state.zombieAttackAttempts,
				zombieLandedHits = state.zombieLandedHits,
				cacheHits = state.cacheHits,
				cacheMisses = state.cacheMisses,
				responseModeTransitions = state.responseModeTransitions,
				responseStutters = state.responseStutters,
				firstZombieAttackTick = state.firstZombieAttackTick,
				firstZombieHarmTick = state.firstZombieHarmTick,
				firstFullTick = state.firstFullTick,
				firstRangedShotTick = state.firstRangedShotTick,
				firstReturnToMinimalTick = state.firstReturnToMinimalTick,
				ticksToFirstRangedResponse = state.firstRangedShotTick.HasValue ? state.firstRangedShotTick.Value - state.activatedTick : (int?)null,
				completionReason = completionReason,
				requestedSpeed = requestedSpeed,
				sampledSpeed = sampledSpeed,
				incidentDef = state.incidentDef,
				incidentExecuted = state.incidentExecuted,
				incidentFallback = state.incidentFallback,
				incidentPawnCount = state.incidentPawnCount,
				lordStillPresent = state.lord != null && state.map?.lordManager?.lords?.Contains(state.lord) == true,
				groupDispersed = spawnedSurvivors < survivors,
				stage = stage,
				playSamples = playSamples,
				warnings = warnings,
				cleanup = new { observersCleared = true, mapResetByNextTrial = true },
				events = state.events.ToArray()
			};
			ClearGroupResponseTrialObservers();
			activeGroupResponseTrial = null;
			return result;
		}

		static int CountGroupResponseInjuries(Pawn pawn)
		{
			return pawn?.health?.hediffSet?.hediffs?.Count(hediff => hediff is Hediff_Injury) ?? 0;
		}

		static float InitialGroupResponseHealth(GroupResponseTrialState state, Pawn pawn)
		{
			return state.initialHealth.TryGetValue(pawn, out var value) ? value : 0f;
		}

		static int InitialGroupResponseInjuries(GroupResponseTrialState state, Pawn pawn)
		{
			return state.initialInjuries.TryGetValue(pawn, out var value) ? value : 0;
		}

		static object DescribeGroupResponseMember(Pawn pawn)
		{
			var verb = pawn?.equipment?.PrimaryEq?.PrimaryVerb;
			return new
			{
				id = ZombieRuntimeActions.StableThingId(pawn),
				name = pawn?.Name?.ToStringShort,
				pawnKind = pawn?.kindDef?.defName,
				weapon = pawn?.equipment?.Primary?.def?.defName,
				verb = verb?.GetType().Name,
				ranged = verb != null && verb.IsMeleeAttack == false,
				health = pawn?.health?.summaryHealth?.SummaryHealthPercent,
				moving = pawn?.health?.capacities?.GetLevel(PawnCapacityDefOf.Moving),
				lordJob = pawn?.GetLord()?.LordJob?.GetType().FullName
			};
		}

		static void ClearGroupResponseTrialObservers()
		{
			GroupZombieResponse.evaluationObserver = null;
			GroupZombieResponse.combatObserver = null;
		}

		[Tool("zombieland/group_response_survival_matrix", Description = "Reload a reusable base save, trigger real visitor/raid incidents, run repeated normal 3x response encounters asynchronously, and return raw plus aggregate survival evidence.")]
		public static async Task<object> GroupResponseSurvivalMatrix(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			[ToolParameter(Description = "Reusable all-DLC base save without .rws.", Required = false, DefaultValue = "ZL_Group_Response_Base")] string saveName = "ZL_Group_Response_Base",
			[ToolParameter(Description = "Comma-separated friendly and/or enemy relationships.", Required = false, DefaultValue = "friendly")] string relationships = "friendly",
			[ToolParameter(Description = "Comma-separated Minimal, Adaptive, and/or Full policies.", Required = false, DefaultValue = "Adaptive")] string policies = "Adaptive",
			[ToolParameter(Description = "Comma-separated exact capable-shooter counts.", Required = false, DefaultValue = "1,3,5")] string shooterCounts = "1,3,5",
			[ToolParameter(Description = "Comma-separated ordinary-zombie counts.", Required = false, DefaultValue = "1,5,12")] string ordinaryZombieCounts = "1,5,12",
			[ToolParameter(Description = "Comma-separated tanky-zombie counts combined with every ordinary count.", Required = false, DefaultValue = "0")] string tankyZombieCounts = "0",
			[ToolParameter(Description = "Independent repetitions of every matrix row.", Required = false, DefaultValue = 1)] int repetitions = 1,
			[ToolParameter(Description = "Maximum zombie placement radius.", Required = false, DefaultValue = 12)] int spawnRadius = 12,
			[ToolParameter(Description = "Requested normal gameplay speed. Superfast is RimWorld's third speed setting.", Required = false, DefaultValue = "Superfast")] string speed = "Superfast",
			[ToolParameter(Description = "Real-time incident-only warm-up before zombies are placed.", Required = false, DefaultValue = 250)] int warmupMs = 250,
			[ToolParameter(Description = "Real-time duration of each asynchronous observation slice.", Required = false, DefaultValue = 500)] int sampleMs = 500,
			[ToolParameter(Description = "Maximum real-time gameplay duration per trial.", Required = false, DefaultValue = 6000)] int maxDurationMs = 6000,
			[ToolParameter(Description = "Base deterministic seed; each trial receives a stable offset.", Required = false, DefaultValue = 1701)] int seed = 1701,
			[ToolParameter(Description = "Optional exact JSON output path.", Required = false, DefaultValue = "")] string outputPath = "")
		{
			if (await groupResponseMatrixGate.WaitAsync(0, cancellationToken) == false)
				return new { success = false, stage = "gate", error = "A group-response survival matrix is already running." };

			var rows = new List<GroupResponseTrialResult>();
			try
			{
				if (string.IsNullOrWhiteSpace(saveName))
					throw new GroupResponseMatrixException("validate", "saveName is required.");
				var relationValues = ParseGroupResponseRelationships(relationships);
				var policyValues = ParseGroupResponsePolicies(policies);
				var shooterValues = ParseGroupResponseCounts(shooterCounts, "shooterCounts", 1, 20);
				var ordinaryValues = ParseGroupResponseCounts(ordinaryZombieCounts, "ordinaryZombieCounts", 0, 100);
				var tankyValues = ParseGroupResponseCounts(tankyZombieCounts, "tankyZombieCounts", 0, 30);
				if (repetitions < 1 || repetitions > 20)
					throw new GroupResponseMatrixException("validate", "repetitions must be between 1 and 20.");
				if (warmupMs < 0 || warmupMs > 10000 || sampleMs < 100 || sampleMs > 10000 || maxDurationMs < sampleMs || maxDurationMs > 120000)
					throw new GroupResponseMatrixException("validate", "warmupMs must be 0..10000, sampleMs 100..10000, and maxDurationMs between sampleMs and 120000.");
				if (Enum.TryParse(speed, true, out TimeSpeed parsedSpeed) == false || parsedSpeed == TimeSpeed.Paused || parsedSpeed == TimeSpeed.Ultrafast)
					throw new GroupResponseMatrixException("validate", "speed must be Normal, Fast, or Superfast.");
				var totalTrials = relationValues.Length * policyValues.Length * shooterValues.Length * ordinaryValues.Length * tankyValues.Length * repetitions;
				if (totalTrials > 200)
					throw new GroupResponseMatrixException("validate", $"The requested matrix has {totalTrials} trials; the safety limit is 200.");

				var trialIndex = 0;
				foreach (var relationship in relationValues)
					foreach (var policy in policyValues)
						foreach (var shooters in shooterValues)
							foreach (var ordinary in ordinaryValues)
								foreach (var tanky in tankyValues)
									for (var repetition = 0; repetition < repetitions; repetition++)
									{
										trialIndex++;
										var trialSeed = seed + trialIndex * 7919;
										var trialId = $"{relationship}-{policy}-{shooters}s-{ordinary}o-{tanky}t-r{repetition + 1}";
										try
										{
											rows.Add(await RunGroupResponseTrialAsync(ctx, cancellationToken, saveName, trialId, trialIndex, trialSeed, relationship, policy, shooters, ordinary, tanky, spawnRadius, parsedSpeed.ToString(), warmupMs, sampleMs, maxDurationMs));
										}
										catch (Exception ex)
										{
											await ctx.MainThread.InvokeAsync(() =>
											{
												ClearGroupResponseTrialObservers();
												activeGroupResponseTrial = null;
											}, CancellationToken.None);
											rows.Add(new GroupResponseTrialResult
											{
												success = false,
												error = ex.InnerException?.Message ?? ex.Message,
												trialId = trialId,
												trialIndex = trialIndex,
												seed = trialSeed,
												relationship = relationship,
												policy = policy.ToString(),
												requestedShooters = shooters,
												ordinaryZombies = ordinary,
												tankyZombies = tanky,
												requestedSpeed = parsedSpeed.ToString(),
												completionReason = "error"
											});
										}
									}

				var aggregates = AggregateGroupResponseTrials(rows);
				var result = new
				{
					success = rows.Count == totalTrials && rows.All(row => row.success),
					baseSave = saveName,
					requestedSpeed = parsedSpeed.ToString(),
					trialCount = rows.Count,
					successfulTrials = rows.Count(row => row.success),
					failedTrials = rows.Count(row => row.success == false),
					parameters = new
					{
						relationships = relationValues,
						policies = policyValues.Select(value => value.ToString()).ToArray(),
						shooters = shooterValues,
						ordinaryZombies = ordinaryValues,
						tankyZombies = tankyValues,
						repetitions,
						spawnRadius,
						warmupMs,
						sampleMs,
						maxDurationMs,
						seed
					},
					aggregates,
					rows = rows.ToArray(),
					outputPath = string.IsNullOrWhiteSpace(outputPath) ? null : Path.GetFullPath(outputPath)
				};
				if (string.IsNullOrWhiteSpace(outputPath) == false)
				{
					var fullPath = Path.GetFullPath(outputPath);
					var directory = Path.GetDirectoryName(fullPath);
					if (string.IsNullOrWhiteSpace(directory) == false)
						Directory.CreateDirectory(directory);
					File.WriteAllText(fullPath, JsonConvert.SerializeObject(result, Formatting.Indented));
				}
				return result;
			}
			catch (GroupResponseMatrixException ex)
			{
				return new { success = false, stage = ex.stage, error = ex.Message, rows = rows.ToArray() };
			}
			finally
			{
				try
				{
					await ctx.MainThread.InvokeAsync(() =>
					{
						ClearGroupResponseTrialObservers();
						activeGroupResponseTrial = null;
					}, CancellationToken.None);
					if (string.IsNullOrWhiteSpace(saveName) == false)
						_ = await ctx.Tools.CallAsync("rimworld/load_game_ready", new { saveName, readiness = "visual", pauseIfNeeded = true, timeoutMs = 120000 }, cancellationToken: CancellationToken.None);
				}
				catch
				{
				}
				groupResponseMatrixGate.Release();
			}
		}

		static async Task<GroupResponseTrialResult> RunGroupResponseTrialAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			string saveName,
			string trialId,
			int trialIndex,
			int seed,
			string relationship,
			ZombieResponsePolicy policy,
			int shooters,
			int ordinary,
			int tanky,
			int spawnRadius,
			string speed,
			int warmupMs,
			int sampleMs,
			int maxDurationMs)
		{
			await RequireGroupResponseCallAsync(ctx, cancellationToken, $"{trialId}.load", "rimworld/load_game_ready", new
			{
				saveName,
				readiness = "visual",
				pauseIfNeeded = true,
				timeoutMs = 120000
			});
			var logBefore = await RequireGroupResponseCallAsync(ctx, cancellationToken, $"{trialId}.logs.before", "rimbridge/list_logs", new { minimumLevel = "warning", limit = 200 });
			var logCursor = MaxGroupResponseLogSequence(logBefore.Result);

			var stage = await ctx.MainThread.InvokeAsync(() => StageGroupResponseTrial(trialId, relationship, policy.ToString(), shooters, ordinary, tanky, spawnRadius, seed, false), cancellationToken);
			if (ObjectSuccess(stage) == false)
				throw new GroupResponseMatrixException($"{trialId}.stage", JsonConvert.SerializeObject(stage));

			if (warmupMs > 0)
				await RequireGroupResponseCallAsync(ctx, cancellationToken, $"{trialId}.warmup", "rimworld/play_for", new { speed, durationMs = warmupMs, forceRequestedSpeed = false });
			var activation = await ctx.MainThread.InvokeAsync(() => ActivateGroupResponseTrial(activeGroupResponseTrial), cancellationToken);
			if (ObjectSuccess(activation) == false)
				throw new GroupResponseMatrixException($"{trialId}.activate", JsonConvert.SerializeObject(activation));

			var playSamples = new List<object>();
			var completionReason = "duration";
			var sampledSpeed = speed;
			var elapsedMs = 0;
			while (elapsedMs < maxDurationMs)
			{
				await ctx.MainThread.InvokeAsync(() => RefreshGroupResponseAttraction(activeGroupResponseTrial), cancellationToken);
				var duration = Math.Min(sampleMs, maxDurationMs - elapsedMs);
				var play = await RequireGroupResponseCallAsync(ctx, cancellationToken, $"{trialId}.play", "rimworld/play_for", new { speed, durationMs = duration, forceRequestedSpeed = false });
				elapsedMs += duration;
				playSamples.Add(CompactGroupResponsePlaySample(play.Result));
				var state = await ctx.MainThread.InvokeAsync(() => DescribeGroupResponseTrialState(activeGroupResponseTrial), cancellationToken);
				var stateJson = JObject.FromObject(state);
				if (stateJson.Value<bool?>("groupEliminated") == true)
				{
					completionReason = "groupEliminated";
					break;
				}
				if (stateJson.Value<bool?>("zombiesEliminated") == true)
				{
					completionReason = "zombiesEliminated";
					break;
				}
			}

			var warningCall = await RequireGroupResponseCallAsync(ctx, cancellationToken, $"{trialId}.logs.after", "rimbridge/list_logs", new { afterSequence = logCursor, minimumLevel = "warning", limit = 100 });
			return await ctx.MainThread.InvokeAsync(() => FinishGroupResponseTrial(trialIndex, seed, shooters, speed, sampledSpeed, completionReason, new { setup = stage, activation }, playSamples.ToArray(), warningCall.Result), cancellationToken);
		}

		static async Task<RimBridgeToolCallResult<object>> RequireGroupResponseCallAsync(IRimBridgeContext ctx, CancellationToken cancellationToken, string stage, string tool, object arguments = null)
		{
			var call = await ctx.Tools.CallAsync(tool, arguments, cancellationToken: cancellationToken);
			if (call.Succeeded())
				return call;
			var error = call?.Error;
			throw new GroupResponseMatrixException(stage, error == null ? $"{tool} returned status '{call?.Status ?? "unknown"}'." : $"{error.Code}: {error.Message}");
		}

		static int MaxGroupResponseLogSequence(object result)
		{
			if (result == null)
				return 0;
			var logs = JObject.FromObject(result)["logs"] as JArray;
			return logs?.Select(token => token.Value<int?>("Sequence") ?? token.Value<int?>("sequence") ?? 0).DefaultIfEmpty(0).Max() ?? 0;
		}

		static object CompactGroupResponsePlaySample(object result)
		{
			if (result == null)
				return null;
			var json = JObject.FromObject(result);
			return new
			{
				requestedSpeed = json.SelectToken("requestedSpeed")?.ToString() ?? json.SelectToken("speed")?.ToString(),
				actualSpeed = json.SelectToken("timeSpeed")?.ToString() ?? json.SelectToken("actualSpeed")?.ToString() ?? json.SelectToken("state.timeSpeed")?.ToString(),
				durationMs = json.SelectToken("requestedDurationMs")?.Value<int?>() ?? json.SelectToken("durationMs")?.Value<int?>(),
				ticksAdvanced = json.SelectToken("advancedTicks")?.Value<int?>() ?? json.SelectToken("ticksAdvanced")?.Value<int?>() ?? json.SelectToken("elapsedTicks")?.Value<int?>(),
				completed = json.SelectToken("completed")?.Value<bool?>() ?? true
			};
		}

		static string[] ParseGroupResponseRelationships(string value)
		{
			var result = SplitGroupResponseCsv(value).Select(item => item.ToLowerInvariant()).Distinct().ToArray();
			if (result.Length == 0 || result.Any(item => item != "friendly" && item != "enemy"))
				throw new GroupResponseMatrixException("validate", "relationships must contain friendly and/or enemy.");
			return result;
		}

		static ZombieResponsePolicy[] ParseGroupResponsePolicies(string value)
		{
			var result = new List<ZombieResponsePolicy>();
			foreach (var item in SplitGroupResponseCsv(value))
			{
				if (Enum.TryParse(item, true, out ZombieResponsePolicy policy) == false)
					throw new GroupResponseMatrixException("validate", $"Unknown response policy '{item}'.");
				if (result.Contains(policy) == false)
					result.Add(policy);
			}
			if (result.Count == 0)
				throw new GroupResponseMatrixException("validate", "At least one response policy is required.");
			return result.ToArray();
		}

		static int[] ParseGroupResponseCounts(string value, string name, int minimum, int maximum)
		{
			var result = new List<int>();
			foreach (var item in SplitGroupResponseCsv(value))
			{
				if (int.TryParse(item, out var count) == false || count < minimum || count > maximum)
					throw new GroupResponseMatrixException("validate", $"{name} contains invalid value '{item}'; expected {minimum}..{maximum}.");
				if (result.Contains(count) == false)
					result.Add(count);
			}
			if (result.Count == 0)
				throw new GroupResponseMatrixException("validate", $"{name} must contain at least one value.");
			return result.ToArray();
		}

		static string[] SplitGroupResponseCsv(string value)
		{
			return (value ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(item => item.Trim())
				.Where(item => item.Length > 0)
				.ToArray();
		}

		static object AggregateGroupResponseTrials(List<GroupResponseTrialResult> rows)
		{
			var successful = rows.Where(row => row.success).ToArray();
			var groups = successful
				.GroupBy(row => new { row.relationship, row.policy, row.requestedShooters, row.ordinaryZombies, row.tankyZombies })
				.Select(group => DescribeGroupResponseAggregate(group.Key.relationship, group.Key.policy, group.Key.requestedShooters, group.Key.ordinaryZombies, group.Key.tankyZombies, group.ToArray()))
				.ToArray();
			return new
			{
				overall = DescribeGroupResponseAggregate("all", "all", -1, -1, -1, successful),
				byScenario = groups
			};
		}

		static object DescribeGroupResponseAggregate(string relationship, string policy, int shooters, int ordinary, int tanky, GroupResponseTrialResult[] rows)
		{
			var count = rows.Length;
			var survivorCounts = rows.Select(row => row.survivors).OrderBy(value => value).ToArray();
			var rangedResponseTicks = rows.Where(row => row.ticksToFirstRangedResponse.HasValue).Select(row => row.ticksToFirstRangedResponse.Value).ToArray();
			return new
			{
				relationship,
				policy,
				shooters = shooters < 0 ? (int?)null : shooters,
				ordinaryZombies = ordinary < 0 ? (int?)null : ordinary,
				tankyZombies = tanky < 0 ? (int?)null : tanky,
				trials = count,
				survivalRate = count == 0 ? 0f : rows.Sum(row => row.survivors) / (float)Math.Max(1, rows.Sum(row => row.actualMembers)),
				fullGroupSurvivalRate = count == 0 ? 0f : rows.Count(row => row.survivors == row.actualMembers) / (float)count,
				meanSurvivors = count == 0 ? 0f : rows.Average(row => row.survivors),
				medianSurvivors = MedianGroupResponseValue(survivorCounts),
				meanZombiesKilled = count == 0 ? 0f : rows.Average(row => row.zombiesKilled),
				meanTicksToFirstRangedResponse = rangedResponseTicks.Length == 0 ? (float?)null : (float)rangedResponseTicks.Average(),
				meanModeTransitions = count == 0 ? 0f : rows.Average(row => row.responseModeTransitions),
				responseStutters = rows.Sum(row => row.responseStutters),
				trialsWithStutter = rows.Count(row => row.responseStutters > 0),
				trialsWithRangedResponse = rangedResponseTicks.Length,
				meanZombieLandedHits = count == 0 ? 0f : rows.Average(row => row.zombieLandedHits)
			};
		}

		static float MedianGroupResponseValue(int[] sortedValues)
		{
			if (sortedValues.Length == 0)
				return 0f;
			var middle = sortedValues.Length / 2;
			return sortedValues.Length % 2 == 0
				? (sortedValues[middle - 1] + sortedValues[middle]) / 2f
				: sortedValues[middle];
		}
	}
}
