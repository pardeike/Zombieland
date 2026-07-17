using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		const string FleshmassScenarioPrefix = "ZL Fleshmass Scenario";
		const int MinimumLiveCascadeDestroyedCells = 5;
		static readonly SemaphoreSlim fleshmassScenarioGate = new(1, 1);

		sealed class FleshmassLiveAttackFixture
		{
			public string id;
			public Zombie zombie;
			public Building target;
			public List<Thing> spawned = new();
			public int hitPointsBefore;
			public int hitPointsAtFirstObservation;
			public int damageBaseline;
			public int startTick;
			public bool expectAction;
			public bool expectArmed;
			public bool sawAttackStatic;
			public bool sawArmed;
			public bool damageObserved;
			public FleshmassZombieCategory category;
			public bool categoryEnabled;
			public int attackStarts;
			public string[] jobs = Array.Empty<string>();
			public IntVec3 routeParentBefore = IntVec3.Invalid;
			public IntVec3 routeDestination = IntVec3.Invalid;
		}

		sealed class FleshmassLiveResponseFixture
		{
			public string id;
			public Map map;
			public Building_FleshmassHeart heart;
			public CompGrowsFleshmassTendrils grower;
			public Zombie zombie;
			public Building target;
			public Building unrelatedBuilding;
			public List<Thing> spawned = new();
			public List<Building> field = new();
			public HashSet<string> baselineFleshbeasts = new();
			public HashSet<Letter> baselineLetters = new();
			public int startTick;
			public int responseBefore;
			public int targetHitPointsBefore;
			public int targetHitPointsAtFirstObservation;
			public int targetDamageBaseline;
			public int prunedDefenders;
			public bool suicide;
			public bool sawAttackStatic;
			public bool sawArmed;
			public bool damageObserved;
			public bool restartedAttack;
		}

		sealed class FleshmassDenseFixture
		{
			public Map map;
			public IntVec3 root;
			public Building_FleshmassHeart firstHeart;
			public Building_FleshmassHeart secondHeart;
			public CompGrowsFleshmassTendrils firstGrower;
			public CompGrowsFleshmassTendrils secondGrower;
			public List<Thing> spawned = new();
			public List<Building> firstField = new();
			public List<Building> secondField = new();
			public List<Zombie> zombies = new();
			public List<Building> fortress = new();
			public List<Pawn> colonists = new();
			public HashSet<string> baselineFleshbeasts = new();
			public HashSet<Letter> baselineLetters = new();
			public int startTick;
			public int firstResponseBefore;
			public int secondResponseBefore;
		}

		[Tool("zombieland/fleshmass_collision_scenario", Description = "Run reusable asynchronous Fleshmass collision stages on a real map. Stages cover live attack jobs and category gates, zombie and suicide response emergence/letter suppression, source loss during an in-flight attack, save-load during AttackStatic with settings persistence, and a dense two-heart fortress battle. The all stage reloads the clean base before each substage.")]
		public static async Task<object> FleshmassCollisionScenario(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			[ToolParameter(Description = "Stage: all, attacks, response, sourceLoss, saveLoad, or dense.", Required = false, DefaultValue = "all")] string stage = "all",
			[ToolParameter(Description = "Compatible clean save loaded before the requested stage or all-stage run.", Required = false, DefaultValue = "EMPTY")] string baseSaveName = "EMPTY",
			[ToolParameter(Description = "Reusable fixture save written during the saveLoad stage.", Required = false, DefaultValue = "ZL_Fleshmass_Collision_00")] string fixtureSaveName = "ZL_Fleshmass_Collision_00",
			[ToolParameter(Description = "Maximum real-time wait per asynchronous observation in milliseconds.", Required = false, DefaultValue = 180000)] int timeoutMs = 180000,
			[ToolParameter(Description = "RimWorld speed while observing live jobs: Normal, Fast, Superfast, or Ultrafast.", Required = false, DefaultValue = "Superfast")] string speed = "Superfast",
			[ToolParameter(Description = "Zombie count for the dense stage. Clamped to 40..180.", Required = false, DefaultValue = 120)] int denseZombieCount = 120,
			[ToolParameter(Description = "Destroy the current-map fixture after each completed stage. The saveLoad fixture file is preserved.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			if (ctx == null)
				return new { success = false, error = "RimBridge context was not injected." };
			if (TryParseFleshmassScenarioStage(stage, out var normalizedStage, out var stageError) == false)
				return new { success = false, error = stageError };
			if (TryParseFleshmassScenarioSpeed(speed, out var parsedSpeed, out var speedError) == false)
				return new { success = false, error = speedError };

			var clampedTimeoutMs = Math.Max(5000, Math.Min(timeoutMs, 600000));
			var clampedDenseCount = Math.Max(40, Math.Min(denseZombieCount, 180));
			await fleshmassScenarioGate.WaitAsync(cancellationToken);
			try
			{
				var load = await RequireFleshmassScenarioToolCallAsync(ctx, cancellationToken, "base.load", "rimworld/load_game_ready", new
				{
					saveName = baseSaveName,
					readiness = "visual",
					pauseIfNeeded = true,
					timeoutMs = 120000
				}, 150000);
				var stageLoads = new List<object>
				{
					new { stage = normalizedStage == "all" ? "attacks" : normalizedStage, load }
				};
				async Task ReloadAllStageAsync(string stageName)
				{
					if (normalizedStage != "all")
						return;
					var stageLoad = await RequireFleshmassScenarioToolCallAsync(ctx, cancellationToken, $"base.reload.{stageName}", "rimworld/load_game_ready", new
					{
						saveName = baseSaveName,
						readiness = "visual",
						pauseIfNeeded = true,
						timeoutMs = 120000
					}, 150000);
					stageLoads.Add(new { stage = stageName, load = stageLoad });
				}

				var preflight = await ctx.MainThread.InvokeAsync(() =>
				{
					var map = CurrentMap;
					return new
					{
						success = map != null && Current.ProgramState == ProgramState.Playing && ModsConfig.AnomalyActive,
						hasMap = map != null,
						programState = Current.ProgramState.ToString(),
						anomalyActive = ModsConfig.AnomalyActive,
						mapId = map?.uniqueID,
						gameVersion = VersionControl.CurrentVersionStringWithRev,
						assemblyMvid = typeof(CompFleshmass).Module.ModuleVersionId.ToString("N")
					};
				}, cancellationToken);
				if (preflight.success == false)
					return new { success = false, stage = normalizedStage, baseSaveName, load, preflight };

				var results = new List<object>();
				if (normalizedStage is "all" or "attacks")
					results.Add(await RunFleshmassAttackStageAsync(ctx, cancellationToken, parsedSpeed, clampedTimeoutMs, cleanup));
				await ReloadAllStageAsync("response");
				if (normalizedStage is "all" or "response")
					results.Add(await RunFleshmassResponseStageAsync(ctx, cancellationToken, parsedSpeed, clampedTimeoutMs, cleanup));
				await ReloadAllStageAsync("sourceLoss");
				if (normalizedStage is "all" or "sourceloss")
					results.Add(await RunFleshmassSourceLossStageAsync(ctx, cancellationToken, parsedSpeed, clampedTimeoutMs, cleanup));
				await ReloadAllStageAsync("saveLoad");
				if (normalizedStage == "sourceloss" && cleanup)
				{
					var restoreLoad = await RequireFleshmassScenarioToolCallAsync(ctx, cancellationToken, "base.restore.sourceLoss", "rimworld/load_game_ready", new
					{
						saveName = baseSaveName,
						readiness = "visual",
						pauseIfNeeded = true,
						timeoutMs = 120000
					}, 150000);
					stageLoads.Add(new { stage = "sourceLoss-cleanup", load = restoreLoad });
				}
				if (normalizedStage is "all" or "saveload")
					results.Add(await RunFleshmassSaveLoadStageAsync(ctx, cancellationToken, parsedSpeed, clampedTimeoutMs, fixtureSaveName, cleanup));
				await ReloadAllStageAsync("dense");
				if (normalizedStage is "all" or "dense")
					results.Add(await RunFleshmassDenseStageAsync(ctx, cancellationToken, parsedSpeed, clampedTimeoutMs, clampedDenseCount, cleanup));

				return new
				{
					success = results.Count > 0 && results.All(ObjectSuccess),
					stage = normalizedStage,
					baseSaveName,
					fixtureSaveName,
					timeoutMs = clampedTimeoutMs,
					speed = parsedSpeed.ToString(),
					denseZombieCount = clampedDenseCount,
					cleanup,
					load,
					stageLoads = stageLoads.ToArray(),
					preflight,
					results = results.ToArray(),
					logNote = "Run rimbridge/list_logs with minimumLevel=warning after the scenario for the warning-and-error-clean gate."
				};
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				return new
				{
					success = false,
					stage = normalizedStage,
					error = $"{ex.GetType().Name}: {ex.Message}",
					stackTrace = ex.StackTrace
				};
			}
			finally
			{
				try
				{
					await ctx.MainThread.InvokeAsync(() =>
					{
						if (Find.TickManager != null)
							Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
					}, CancellationToken.None);
				}
				catch
				{
				}
				fleshmassScenarioGate.Release();
			}
		}

		static bool TryParseFleshmassScenarioStage(string value, out string stage, out string error)
		{
			stage = (value ?? "all").Trim().ToLowerInvariant().Replace("-", "").Replace("_", "");
			if (stage is "all" or "attacks" or "response" or "sourceloss" or "saveload" or "dense")
			{
				error = null;
				return true;
			}
			error = "Unsupported stage. Use all, attacks, response, sourceLoss, saveLoad, or dense.";
			return false;
		}

		static bool TryParseFleshmassScenarioSpeed(string value, out TimeSpeed speed, out string error)
		{
			if (Enum.TryParse(value ?? "Superfast", true, out speed) && speed != TimeSpeed.Paused)
			{
				error = null;
				return true;
			}
			speed = TimeSpeed.Superfast;
			error = "Unsupported speed. Use Normal, Fast, Superfast, or Ultrafast.";
			return false;
		}

		static async Task<object> RequireFleshmassScenarioToolCallAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			string stage,
			string tool,
			object arguments,
			int timeoutMs = 180000)
		{
			var call = await ctx.Tools.CallAsync(tool, arguments, new RimBridgeToolCallOptions { TimeoutMs = timeoutMs }, cancellationToken);
			if (call.Succeeded())
				return call.Result;
			var message = call?.Error == null
				? $"{tool} returned status '{call?.Status ?? "unknown"}'."
				: $"{call.Error.Code}: {call.Error.Message}";
			throw new InvalidOperationException($"{stage}: {message}");
		}

		static object DescribeFleshmassWait(RimBridgeWaitResult wait)
		{
			return wait == null ? null : new
			{
				wait.Success,
				wait.Status,
				wait.Message,
				wait.ElapsedFrames,
				wait.StartTicksGame,
				wait.EndTicksGame,
				wait.AdvancedTicks
			};
		}

		static async Task<object> RunFleshmassAttackStageAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			TimeSpeed speed,
			int timeoutMs,
			bool cleanup)
		{
			var settingsSnapshot = await ctx.MainThread.InvokeAsync(SnapshotZombieSettings, cancellationToken);
			var beforeLetters = await ctx.MainThread.InvokeAsync(() => (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet(), cancellationToken);
			var stageSpawned = new List<Thing>();
			try
			{
				var setup = await ctx.MainThread.InvokeAsync(() =>
				{
					var map = CurrentMap;
					if (map == null)
						return (success: false, map: (Map)null, root: IntVec3.Invalid, heart: (Building_FleshmassHeart)null, error: (object)new { error = "No current map." });
					if (TryFindFleshmassContractRoot(map, 52, 42, out var root, out var rootError) == false)
						return (success: false, map, root: IntVec3.Invalid, heart: (Building_FleshmassHeart)null, error: rootError);
					var baselineFleshbeasts = CurrentFleshbeastIds(map);
					var heart = SpawnFleshmassBuilding("FleshmassHeart", root + new IntVec3(-23, 0, 16), map, Faction.OfEntities, null, stageSpawned) as Building_FleshmassHeart;
					SuppressFleshmassScenarioHeartDefenders(heart);
					DestroyNewFleshbeasts(map, baselineFleshbeasts);
					var colonist = SpawnFleshmassScenarioColonist(root + new IntVec3(22, 0, 16), map, $"{FleshmassScenarioPrefix} attack observer", stageSpawned);
					return (success: heart != null && colonist != null, map, root, heart, error: heart == null || colonist == null ? (object)new { error = "Could not spawn attack-stage heart and observer." } : null);
				}, cancellationToken);
				if (setup.success == false)
					return new { success = false, stage = "attacks", setup.error };

				var specs = new[]
				{
					new { id = "ordinary-enabled-doors", type = ZombieType.Normal, offset = new IntVec3(-18, 0, -15), attackMode = AttackMode.Everything, smashMode = SmashMode.DoorsOnly, ordinary = true, tank = false, special = false, former = false, tankRoute = false, expectAction = true, expectArmed = false },
					new { id = "ordinary-disabled-doors", type = ZombieType.Normal, offset = new IntVec3(-9, 0, -15), attackMode = AttackMode.Everything, smashMode = SmashMode.DoorsOnly, ordinary = false, tank = true, special = true, former = false, tankRoute = false, expectAction = false, expectArmed = false },
					new { id = "ordinary-only-colonists", type = ZombieType.Normal, offset = new IntVec3(0, 0, -15), attackMode = AttackMode.OnlyColonists, smashMode = SmashMode.DoorsOnly, ordinary = true, tank = true, special = true, former = false, tankRoute = false, expectAction = false, expectArmed = false },
					new { id = "former-enabled-doors", type = ZombieType.Normal, offset = new IntVec3(9, 0, -15), attackMode = AttackMode.Everything, smashMode = SmashMode.DoorsOnly, ordinary = false, tank = false, special = true, former = true, tankRoute = false, expectAction = true, expectArmed = false },
					new { id = "former-nothing", type = ZombieType.Normal, offset = new IntVec3(18, 0, -15), attackMode = AttackMode.Everything, smashMode = SmashMode.Nothing, ordinary = true, tank = true, special = true, former = true, tankRoute = false, expectAction = false, expectArmed = false },
					new { id = "miner-enabled-any", type = ZombieType.Miner, offset = new IntVec3(-18, 0, -5), attackMode = AttackMode.Everything, smashMode = SmashMode.AnyBuilding, ordinary = false, tank = false, special = true, former = false, tankRoute = false, expectAction = true, expectArmed = false },
					new { id = "miner-disabled-any", type = ZombieType.Miner, offset = new IntVec3(-9, 0, -5), attackMode = AttackMode.Everything, smashMode = SmashMode.AnyBuilding, ordinary = true, tank = true, special = false, former = false, tankRoute = false, expectAction = false, expectArmed = false },
					new { id = "suicide-enabled-flesh", type = ZombieType.SuicideBomber, offset = new IntVec3(0, 0, -5), attackMode = AttackMode.Everything, smashMode = SmashMode.Nothing, ordinary = false, tank = true, special = false, former = false, tankRoute = false, expectAction = true, expectArmed = true },
					new { id = "suicide-disabled-flesh", type = ZombieType.SuicideBomber, offset = new IntVec3(9, 0, -5), attackMode = AttackMode.Everything, smashMode = SmashMode.Nothing, ordinary = true, tank = false, special = true, former = false, tankRoute = false, expectAction = false, expectArmed = false },
					new { id = "tank-enabled-route", type = ZombieType.TankyOperator, offset = new IntVec3(-14, 0, 7), attackMode = AttackMode.Everything, smashMode = SmashMode.Nothing, ordinary = false, tank = true, special = false, former = false, tankRoute = true, expectAction = true, expectArmed = false },
					new { id = "tank-disabled-route", type = ZombieType.TankyOperator, offset = new IntVec3(0, 0, 7), attackMode = AttackMode.Everything, smashMode = SmashMode.Nothing, ordinary = true, tank = false, special = true, former = false, tankRoute = true, expectAction = false, expectArmed = false }
				};

				var cases = new List<object>();
				foreach (var spec in specs)
				{
					cancellationToken.ThrowIfCancellationRequested();
					cases.Add(await RunFleshmassLiveAttackCaseAsync(
						ctx,
						cancellationToken,
						speed,
						timeoutMs,
						setup.map,
						setup.heart,
						setup.root + spec.offset,
						spec.id,
						spec.type,
						spec.attackMode,
						spec.smashMode,
						spec.ordinary,
						spec.tank,
						spec.special,
						spec.former,
						spec.tankRoute,
						spec.expectAction,
						spec.expectArmed,
						cleanup));
				}

				var heartCase = await RunFleshmassHeartBlockCaseAsync(ctx, cancellationToken, speed, timeoutMs, setup.map, setup.heart, cleanup);
				cases.Add(heartCase);
				return new
				{
					success = cases.All(ObjectSuccess),
					stage = "attacks",
					root = ZombieRuntimeActions.DescribeCell(setup.root),
					caseCount = cases.Count,
					passed = cases.Count(ObjectSuccess),
					cases = cases.ToArray()
				};
			}
			finally
			{
				await ctx.MainThread.InvokeAsync(() =>
				{
					RestoreZombieSettings(settingsSnapshot);
					if (cleanup)
					{
						foreach (var thing in stageSpawned.AsEnumerable().Reverse())
							CleanupFleshmassContractThing(thing);
						RemoveNewLetters(beforeLetters);
						PruneDestroyedContractZombies(CurrentMap);
					}
				}, CancellationToken.None);
			}
		}

		static async Task<object> RunFleshmassLiveAttackCaseAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			TimeSpeed speed,
			int timeoutMs,
			Map map,
			Building_FleshmassHeart heart,
			IntVec3 targetCell,
			string id,
			ZombieType type,
			AttackMode attackMode,
			SmashMode smashMode,
			bool ordinary,
			bool tank,
			bool special,
			bool former,
			bool tankRoute,
			bool expectAction,
			bool expectArmed,
			bool cleanup)
		{
			var fixture = new FleshmassLiveAttackFixture
			{
				id = id,
				expectAction = expectAction,
				expectArmed = expectArmed
			};
			try
			{
				await ctx.MainThread.InvokeAsync(() =>
				{
					SetFleshmassContractSettings(attackMode, smashMode, ordinary, tank, special);
					fixture.startTick = Find.TickManager.TicksGame;
					fixture.target = SpawnFleshmassBuilding("Fleshmass_Active", targetCell, map, Faction.OfEntities, heart, fixture.spawned);
					var zombieCell = targetCell + IntVec3.West;
					fixture.zombie = SpawnFleshmassScenarioZombie(type, zombieCell, map, $"{FleshmassScenarioPrefix} {id}", fixture.spawned);
					if (fixture.zombie != null)
						fixture.zombie.wasMapPawnBefore = former;
					fixture.category = FleshmassCollision.CategoryFor(fixture.zombie);
					fixture.categoryEnabled = FleshmassCollision.CategoryEnabled(fixture.zombie);
					fixture.hitPointsBefore = ScenarioHitPoints(fixture.target);
					if (tankRoute && fixture.zombie != null && fixture.target != null)
					{
						fixture.routeDestination = fixture.target.Position;
						fixture.routeParentBefore = PrepareFleshmassTankRoute(fixture.zombie, fixture.routeDestination);
					}
					StartFleshmassScenarioStumble(fixture.zombie);
				}, cancellationToken);

				if (fixture.target == null || fixture.zombie == null)
					return new { success = false, id, error = "Could not create the live attack fixture." };

				var jobSamples = new HashSet<string>(StringComparer.Ordinal);
				var deadlineTick = fixture.startTick + (expectAction
					? 1800
					: type == ZombieType.SuicideBomber
						? 90
						: tankRoute
							? 420
							: 260);
				await SetFleshmassScenarioSpeedAsync(ctx, cancellationToken, speed);
				var wait = await ctx.Game.RunUntilAsync(() =>
				{
					_ = PruneFleshmassScenarioDefenders(map);
					var job = fixture.zombie?.CurJobDef?.defName ?? "none";
					jobSamples.Add(job);
					var targetsFixture = fixture.zombie?.CurJob?.targetA.Thing == fixture.target;
					fixture.sawAttackStatic |= job == JobDefOf.AttackStatic.defName && targetsFixture;
					fixture.sawArmed |= fixture.zombie?.bombWillGoOff == true;
					fixture.damageObserved |= fixture.target.Destroyed || ScenarioHitPoints(fixture.target) < fixture.hitPointsBefore;
					if (expectArmed)
						return fixture.sawArmed || Find.TickManager.TicksGame >= deadlineTick;
					if (expectAction)
						return fixture.sawAttackStatic || fixture.damageObserved || Find.TickManager.TicksGame >= deadlineTick;
					return fixture.sawAttackStatic || fixture.sawArmed || Find.TickManager.TicksGame >= deadlineTick;
				}, new RimBridgeWaitOptions { TimeoutMs = timeoutMs, FailIfBusy = true }, cancellationToken);
				await PauseFleshmassScenarioAsync(ctx, CancellationToken.None);

				var resumeWait = null as RimBridgeWaitResult;
				var resumed = false;
				await ctx.MainThread.InvokeAsync(() =>
				{
					fixture.hitPointsAtFirstObservation = ScenarioHitPoints(fixture.target);
					fixture.damageObserved |= fixture.target.Destroyed
						|| fixture.hitPointsAtFirstObservation < fixture.hitPointsBefore;
				}, cancellationToken);
				if (expectAction
					&& expectArmed == false
					&& fixture.zombie.Destroyed == false
					&& fixture.zombie.Spawned
					&& (fixture.sawAttackStatic || fixture.damageObserved))
				{
					await ctx.MainThread.InvokeAsync(() =>
					{
						var attacking = fixture.zombie.CurJobDef == JobDefOf.AttackStatic
							&& fixture.zombie.CurJob?.targetA.Thing == fixture.target;
						if (fixture.damageObserved == false && fixture.target?.def?.useHitPoints == true && fixture.target.Destroyed == false)
							fixture.target.HitPoints = Math.Min(fixture.target.HitPoints, 1);
						fixture.damageBaseline = fixture.damageObserved ? fixture.hitPointsBefore : ScenarioHitPoints(fixture.target);
						fixture.attackStarts = fixture.sawAttackStatic ? 1 : 0;
						resumed = fixture.damageObserved && attacking == false;
					}, cancellationToken);
					if (resumed == false)
					{
						var resumeDeadline = Find.TickManager.TicksGame + 1800;
						var wasAttacking = fixture.zombie.CurJobDef == JobDefOf.AttackStatic
							&& fixture.zombie.CurJob?.targetA.Thing == fixture.target;
						await SetFleshmassScenarioSpeedAsync(ctx, cancellationToken, speed);
						resumeWait = await ctx.Game.RunUntilAsync(() =>
						{
							_ = PruneFleshmassScenarioDefenders(map);
							var attacking = fixture.zombie?.CurJobDef == JobDefOf.AttackStatic
								&& fixture.zombie.CurJob?.targetA.Thing == fixture.target;
							if (attacking && wasAttacking == false)
								fixture.attackStarts++;
							if (attacking == false && wasAttacking)
								resumed = true;
							wasAttacking = attacking;
							fixture.damageObserved |= fixture.target.Destroyed || ScenarioHitPoints(fixture.target) < fixture.damageBaseline;
							return (fixture.damageObserved && resumed) || Find.TickManager.TicksGame >= resumeDeadline;
						}, new RimBridgeWaitOptions { TimeoutMs = timeoutMs, FailIfBusy = true }, cancellationToken);
						await PauseFleshmassScenarioAsync(ctx, CancellationToken.None);
					}
				}

				var result = await ctx.MainThread.InvokeAsync(() =>
				{
					fixture.jobs = jobSamples.OrderBy(value => value).ToArray();
					var hitPointsAfter = ScenarioHitPoints(fixture.target);
					var deadlineReached = Find.TickManager.TicksGame >= deadlineTick;
					var actionObserved = expectArmed
						? fixture.sawArmed
						: fixture.damageObserved;
					var routePrepared = tankRoute == false
						|| fixture.routeParentBefore.IsValid && fixture.routeParentBefore == fixture.routeDestination;
					var success = routePrepared
						&& (expectAction
						? actionObserved && (expectArmed || resumed)
						: deadlineReached
							&& fixture.sawAttackStatic == false
							&& fixture.sawArmed == false
							&& (type == ZombieType.SuicideBomber || hitPointsAfter == fixture.hitPointsBefore));
					return new
					{
						success,
						id,
						expectAction,
						expectArmed,
						category = fixture.category.ToString(),
						fixture.categoryEnabled,
						attackMode = attackMode.ToString(),
						smashMode = smashMode.ToString(),
						fixture.sawAttackStatic,
						fixture.sawArmed,
						fixture.damageObserved,
						fixture.attackStarts,
						resumed,
						deadlineReached,
						routePrepared,
						hitPointsBefore = fixture.hitPointsBefore,
						hitPointsAtFirstObservation = fixture.hitPointsAtFirstObservation,
						damageBaseline = fixture.damageBaseline,
						hitPointsAfter,
						targetDestroyed = fixture.target.Destroyed,
						routeParentBefore = ZombieRuntimeActions.DescribeCell(fixture.routeParentBefore),
						routeDestination = ZombieRuntimeActions.DescribeCell(fixture.routeDestination),
						zombie = fixture.zombie.Destroyed ? null : DescribeZombie(fixture.zombie),
						jobs = fixture.jobs,
						wait = DescribeFleshmassWait(wait),
						resumeWait = DescribeFleshmassWait(resumeWait)
					};
				}, cancellationToken);

				return result;
			}
			finally
			{
				if (cleanup)
				{
					await ctx.MainThread.InvokeAsync(() =>
					{
						if (fixture.zombie?.IsSuicideBomber == true)
						{
							fixture.zombie.bombWillGoOff = false;
							fixture.zombie.bombTickingInterval = -1f;
						}
						foreach (var thing in fixture.spawned.AsEnumerable().Reverse())
							CleanupFleshmassContractThing(thing);
						PruneDestroyedContractZombies(map);
					}, CancellationToken.None);
				}
			}
		}

		static async Task<object> RunFleshmassHeartBlockCaseAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			TimeSpeed speed,
			int timeoutMs,
			Map map,
			Building_FleshmassHeart heart,
			bool cleanup)
		{
			var spawned = new List<Thing>();
			Zombie tank = null;
			var routeParent = IntVec3.Invalid;
			var routeDestination = IntVec3.Invalid;
			var startTick = 0;
			try
			{
				await ctx.MainThread.InvokeAsync(() =>
				{
					SetFleshmassContractSettings(AttackMode.Everything, SmashMode.Nothing, ordinary: true, tankSuicide: true, special: true);
					var occupied = heart.OccupiedRect();
					routeDestination = new IntVec3(occupied.minX, 0, (occupied.minZ + occupied.maxZ) / 2);
					var start = routeDestination + IntVec3.West;
					tank = SpawnFleshmassScenarioZombie(ZombieType.TankyOperator, start, map, $"{FleshmassScenarioPrefix} tank-heart", spawned);
					routeParent = PrepareFleshmassTankRoute(tank, routeDestination);
					StartFleshmassScenarioStumble(tank);
					startTick = Find.TickManager.TicksGame;
				}, cancellationToken);
				if (tank == null)
					return new { success = false, id = "tank-heart-blocked", error = "Could not spawn the tank." };

				var sawAttack = false;
				var jobs = new HashSet<string>(StringComparer.Ordinal);
				var deadline = startTick + 480;
				await SetFleshmassScenarioSpeedAsync(ctx, cancellationToken, speed);
				var wait = await ctx.Game.RunUntilAsync(() =>
				{
					_ = PruneFleshmassScenarioDefenders(map);
					var job = tank.CurJobDef?.defName ?? "none";
					jobs.Add(job);
					sawAttack |= job == JobDefOf.AttackStatic.defName && tank.CurJob?.targetA.Thing == heart;
					return sawAttack || Find.TickManager.TicksGame >= deadline;
				}, new RimBridgeWaitOptions { TimeoutMs = timeoutMs, FailIfBusy = true }, cancellationToken);
				await PauseFleshmassScenarioAsync(ctx, CancellationToken.None);

				var result = await ctx.MainThread.InvokeAsync(() =>
			{
				var routePrepared = routeParent.IsValid && routeParent == routeDestination;
				return new
				{
					success = routePrepared
						&& Find.TickManager.TicksGame >= deadline
						&& sawAttack == false
						&& heart.Spawned
						&& heart.Destroyed == false,
					id = "tank-heart-blocked",
					sawAttackStatic = sawAttack,
					heartSpawned = heart.Spawned,
					heartDestroyable = heart.def.destroyable,
					heartUseHitPoints = heart.def.useHitPoints,
					heartTargetable = heart.def.building?.isTargetable,
					routePrepared,
					routeParentBefore = ZombieRuntimeActions.DescribeCell(routeParent),
					routeDestination = ZombieRuntimeActions.DescribeCell(routeDestination),
					distanceToHeart = tank.Position.DistanceTo(heart.Position),
					jobs = jobs.OrderBy(value => value).ToArray(),
					wait = DescribeFleshmassWait(wait)
				};
			}, cancellationToken);
				return result;
			}
			finally
			{
				if (cleanup)
				{
					await ctx.MainThread.InvokeAsync(() =>
					{
						foreach (var thing in spawned.AsEnumerable().Reverse())
							CleanupFleshmassContractThing(thing);
						PruneDestroyedContractZombies(map);
					}, CancellationToken.None);
				}
			}
		}

		static async Task<object> RunFleshmassResponseStageAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			TimeSpeed speed,
			int timeoutMs,
			bool cleanup)
		{
			var settingsSnapshot = await ctx.MainThread.InvokeAsync(SnapshotZombieSettings, cancellationToken);
			try
			{
				var rootResult = await ctx.MainThread.InvokeAsync(() =>
				{
					var map = CurrentMap;
					if (map == null)
						return (success: false, map, root: IntVec3.Invalid, error: (object)new { error = "No current map." });
					if (TryFindFleshmassContractRoot(map, 68, 42, out var root, out var error) == false)
						return (success: false, map, root: IntVec3.Invalid, error);
					return (success: true, map, root, error: (object)null);
				}, cancellationToken);
				if (rootResult.success == false)
					return new { success = false, stage = "response", rootResult.error };

				var zombieResponse = await RunFleshmassLiveResponseCaseAsync(
					ctx,
					cancellationToken,
					speed,
					timeoutMs,
					rootResult.map,
					rootResult.root + new IntVec3(-22, 0, -5),
					"zombie-faction-response",
					suicide: false,
					cleanup);
				var suicideResponse = await RunFleshmassLiveResponseCaseAsync(
					ctx,
					cancellationToken,
					speed,
					timeoutMs,
					rootResult.map,
					rootResult.root + new IntVec3(14, 0, -5),
					"suicide-collateral-response",
					suicide: true,
					cleanup);

				return new
				{
					success = ObjectSuccess(zombieResponse) && ObjectSuccess(suicideResponse),
					stage = "response",
					root = ZombieRuntimeActions.DescribeCell(rootResult.root),
					zombieResponse,
					suicideResponse
				};
			}
			finally
			{
				await ctx.MainThread.InvokeAsync(() => RestoreZombieSettings(settingsSnapshot), CancellationToken.None);
			}
		}

		static async Task<object> RunFleshmassLiveResponseCaseAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			TimeSpeed speed,
			int timeoutMs,
			Map map,
			IntVec3 fieldStart,
			string id,
			bool suicide,
			bool cleanup)
		{
			var fixture = new FleshmassLiveResponseFixture
			{
				id = id,
				map = map,
				suicide = suicide
			};
			HashSet<string> cleanupFleshbeastBaseline = null;
			HashSet<Letter> cleanupLetterBaseline = null;
			try
			{
				await ctx.MainThread.InvokeAsync(() =>
				{
					fixture.startTick = Find.TickManager.TicksGame;
					cleanupFleshbeastBaseline = CurrentFleshbeastIds(map);
					cleanupLetterBaseline = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
					fixture.baselineFleshbeasts = cleanupFleshbeastBaseline;
					fixture.baselineLetters = cleanupLetterBaseline;
					fixture.heart = SpawnFleshmassBuilding("FleshmassHeart", fieldStart + new IntVec3(2, 0, 15), map, Faction.OfEntities, null, fixture.spawned) as Building_FleshmassHeart;
					SuppressFleshmassScenarioHeartDefenders(fixture.heart);
					DestroyNewFleshbeasts(map, fixture.baselineFleshbeasts);
					fixture.grower = fixture.heart?.GetComp<CompGrowsFleshmassTendrils>();
					fixture.field.AddRange(SpawnFleshmassScenarioField(map, fieldStart, 7, 7, fixture.heart, fixture.spawned));
					fixture.target = fixture.field.FirstOrDefault(building => building.Position == fieldStart + new IntVec3(0, 0, 3)) ?? fixture.field.FirstOrDefault();
					fixture.targetHitPointsBefore = ScenarioHitPoints(fixture.target);
					fixture.targetDamageBaseline = fixture.targetHitPointsBefore;
					if (fixture.grower != null)
					{
						SetResponseRemaining(fixture.grower, 1);
						fixture.responseBefore = ResponseRemaining(fixture.grower);
					}
					var zombieCell = fixture.target?.Position + IntVec3.West ?? IntVec3.Invalid;
					fixture.zombie = SpawnFleshmassScenarioZombie(suicide ? ZombieType.SuicideBomber : ZombieType.Normal, zombieCell, map, $"{FleshmassScenarioPrefix} {id}", fixture.spawned);
					if (suicide)
					{
						SetFleshmassContractSettings(AttackMode.Everything, SmashMode.Nothing, ordinary: false, tankSuicide: false, special: false);
						fixture.unrelatedBuilding = SpawnContractWall(zombieCell + IntVec3.West, map, Faction.OfPlayer, fixture.spawned);
					}
					else
					{
						SetFleshmassContractSettings(AttackMode.Everything, SmashMode.DoorsOnly, ordinary: true, tankSuicide: false, special: false);
					}
					_ = SpawnFleshmassScenarioColonist(fieldStart + new IntVec3(3, 0, -10), map, $"{FleshmassScenarioPrefix} {id} observer", fixture.spawned);
					// Heart setup may synchronously create a defender. Baseline after all inert
					// fixture creation so only the response assault is counted as new evidence.
					fixture.baselineFleshbeasts = CurrentFleshbeastIds(map);
					fixture.baselineLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
					fixture.startTick = Find.TickManager.TicksGame;
					StartFleshmassScenarioStumble(fixture.zombie);
				}, cancellationToken);

				if (fixture.heart == null || fixture.grower == null || fixture.target == null || fixture.zombie == null || fixture.field.Count < 20)
					return new { success = false, id, error = "Could not create the response fixture." };

				RimBridgeWaitResult actionWait = null;
				var actionDeadline = fixture.startTick + 1800;
				if (suicide)
				{
					await SetFleshmassScenarioSpeedAsync(ctx, cancellationToken, speed);
					actionWait = await ctx.Game.RunUntilAsync(() =>
					{
						fixture.prunedDefenders += PruneFleshmassScenarioDefenders(map);
						fixture.sawArmed |= fixture.zombie?.bombWillGoOff == true;
						return fixture.sawArmed || Find.TickManager.TicksGame >= actionDeadline;
					}, new RimBridgeWaitOptions { TimeoutMs = timeoutMs, FailIfBusy = true }, cancellationToken);
					await PauseFleshmassScenarioAsync(ctx, CancellationToken.None);
					if (fixture.sawArmed)
					{
						await ctx.MainThread.InvokeAsync(() => fixture.zombie.bombTickingInterval = 0f, cancellationToken);
					}
				}
				else
				{
					await SetFleshmassScenarioSpeedAsync(ctx, cancellationToken, speed);
					actionWait = await ctx.Game.RunUntilAsync(() =>
					{
						fixture.prunedDefenders += PruneFleshmassScenarioDefenders(map);
						fixture.sawAttackStatic |= fixture.zombie?.CurJobDef == JobDefOf.AttackStatic
							&& fixture.zombie.CurJob?.targetA.Thing == fixture.target;
						fixture.damageObserved |= fixture.target.Destroyed
							|| ScenarioHitPoints(fixture.target) < fixture.targetHitPointsBefore;
						return fixture.sawAttackStatic || fixture.damageObserved || Find.TickManager.TicksGame >= actionDeadline;
					}, new RimBridgeWaitOptions { TimeoutMs = timeoutMs, FailIfBusy = true }, cancellationToken);
					await PauseFleshmassScenarioAsync(ctx, CancellationToken.None);
					await ctx.MainThread.InvokeAsync(() =>
					{
						fixture.targetHitPointsAtFirstObservation = ScenarioHitPoints(fixture.target);
						if (fixture.target?.def?.useHitPoints == true && fixture.target.Destroyed == false)
							fixture.target.HitPoints = Math.Min(fixture.target.HitPoints, 1);
						fixture.targetDamageBaseline = ScenarioHitPoints(fixture.target);
						if (fixture.target.Destroyed == false)
						{
							StartFleshmassScenarioStumble(fixture.zombie);
							fixture.restartedAttack = true;
						}
					}, cancellationToken);
				}

				var observedNewBeasts = false;
				var responseDeadline = Find.TickManager.TicksGame + 3600;
				await SetFleshmassScenarioSpeedAsync(ctx, cancellationToken, speed);
				var wait = await ctx.Game.RunUntilAsync(() =>
				{
					fixture.prunedDefenders += PruneFleshmassScenarioDefenders(map);
					var job = fixture.zombie?.CurJobDef?.defName;
					fixture.sawAttackStatic |= job == JobDefOf.AttackStatic.defName && fixture.zombie?.CurJob?.targetA.Thing == fixture.target;
					fixture.sawArmed |= fixture.zombie?.bombWillGoOff == true;
					fixture.damageObserved |= fixture.target.Destroyed
						|| ScenarioHitPoints(fixture.target) < fixture.targetDamageBaseline;
					observedNewBeasts |= map.mapPawns.AllPawnsSpawned
						.Where(pawn => fixture.baselineFleshbeasts.Contains(ZombieRuntimeActions.StableThingId(pawn)) == false)
						.Any(IsFleshmassAssaultPawn);
					return (observedNewBeasts && fixture.field.Any(building => building.Destroyed || building.Spawned == false))
						|| Find.TickManager.TicksGame >= responseDeadline;
				}, new RimBridgeWaitOptions { TimeoutMs = timeoutMs, FailIfBusy = true }, cancellationToken);
				await PauseFleshmassScenarioAsync(ctx, CancellationToken.None);

				var result = await ctx.MainThread.InvokeAsync(() =>
				{
					var destroyed = fixture.field.Where(building => building.Destroyed || building.Spawned == false).ToArray();
					var newBeasts = map.mapPawns.AllPawnsSpawned
						.Where(pawn => FleshbeastUtility.IsFleshBeast(pawn.kindDef))
						.Where(pawn => fixture.baselineFleshbeasts.Contains(ZombieRuntimeActions.StableThingId(pawn)) == false)
						.ToArray();
					var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
						.Where(letter => fixture.baselineLetters.Contains(letter) == false)
						.ToArray();
					var expectedLabel = "FleshmassResponseLabel".Translate().ToString();
					var leakedResponseLetters = newLetters
						.Where(letter => letter.def == LetterDefOf.ThreatBig && letter.Label == expectedLabel)
						.ToArray();
					var responseAfter = ResponseRemaining(fixture.grower);
					var sourceCreditedDeaths = destroyed.Count(building => ReferenceEquals(building.TryGetComp<CompFleshmass>()?.source, fixture.heart));
					var targetDestroyed = fixture.target.Destroyed || fixture.target.Spawned == false;
					// RimWorld's installed cascade removes 4-8 connected cells in addition
					// to the killing root. The ordinary live case must prove that behavior,
					// not merely that the selected cell received damage or died.
					var liveCascadeObserved = suicide == false
						&& targetDestroyed
						&& destroyed.Length >= MinimumLiveCascadeDestroyedCells;
					var suicideExplosionObserved = suicide
						&& fixture.zombie.Destroyed
						&& fixture.unrelatedBuilding?.Destroyed == true
						&& destroyed.Length > 0;
					return new
					{
						success = (suicide ? destroyed.Length > 0 : liveCascadeObserved)
							&& newBeasts.Length > 0
							&& newBeasts.Any(IsFleshmassAssaultPawn)
							&& responseAfter > 1
							&& leakedResponseLetters.Length == 0
							&& (suicide ? fixture.sawArmed || suicideExplosionObserved : fixture.sawAttackStatic && fixture.damageObserved),
						id,
						suicide,
						fixture.sawAttackStatic,
						fixture.sawArmed,
						suicideExplosionObserved,
						fixture.damageObserved,
						fixture.restartedAttack,
						targetHitPointsBefore = fixture.targetHitPointsBefore,
						targetHitPointsAtFirstObservation = fixture.targetHitPointsAtFirstObservation,
						targetDamageBaseline = fixture.targetDamageBaseline,
						responseBefore = fixture.responseBefore,
						responseAfter,
						fieldCellsBefore = fixture.field.Count,
						destroyedCells = destroyed.Length,
						targetDestroyed,
						minimumLiveCascadeDestroyedCells = suicide ? (int?)null : MinimumLiveCascadeDestroyedCells,
						liveCascadeObserved,
						sourceCreditedDeaths,
						newFleshbeasts = newBeasts.Select(pawn => new
						{
							id = ZombieRuntimeActions.StableThingId(pawn),
							kind = pawn.kindDef?.defName,
							position = ZombieRuntimeActions.DescribeCell(pawn.Position),
							lordJob = pawn.GetLord()?.LordJob?.GetType().FullName
						}).ToArray(),
						newLetters = newLetters.Select(letter => new { label = letter.Label, def = letter.def?.defName }).ToArray(),
						leakedResponseLetterCount = leakedResponseLetters.Length,
						fixture.prunedDefenders,
						actionWait = DescribeFleshmassWait(actionWait),
						wait = DescribeFleshmassWait(wait)
					};
				}, cancellationToken);

				return result;
			}
			finally
			{
				if (cleanup)
				{
					await ctx.MainThread.InvokeAsync(() =>
					{
						if (fixture.zombie?.IsSuicideBomber == true)
						{
							fixture.zombie.bombWillGoOff = false;
							fixture.zombie.bombTickingInterval = -1f;
						}
						if (cleanupFleshbeastBaseline != null)
							DestroyNewFleshbeasts(map, cleanupFleshbeastBaseline);
						foreach (var thing in fixture.spawned.AsEnumerable().Reverse())
							CleanupFleshmassContractThing(thing);
						if (cleanupLetterBaseline != null)
							RemoveNewLetters(cleanupLetterBaseline);
						PruneDestroyedContractZombies(map);
					}, CancellationToken.None);
				}
			}
		}

		static async Task<object> RunFleshmassSourceLossStageAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			TimeSpeed speed,
			int timeoutMs,
			bool cleanup)
		{
			var settingsSnapshot = await ctx.MainThread.InvokeAsync(SnapshotZombieSettings, cancellationToken);
			var spawned = new List<Thing>();
			HashSet<Letter> beforeLetters = null;
			try
			{
				var fixture = await ctx.MainThread.InvokeAsync(() =>
				{
					var map = CurrentMap;
					if (map == null)
						return (success: false, map, root: IntVec3.Invalid, heart: (Building_FleshmassHeart)null, grower: (CompGrowsFleshmassTendrils)null, target: (Building)null, zombie: (Zombie)null, decay: Array.Empty<Building>(), startTick: 0, error: (object)new { error = "No current map." });
					if (TryFindFleshmassContractRoot(map, 42, 32, out var root, out var rootError) == false)
						return (success: false, map, root: IntVec3.Invalid, heart: (Building_FleshmassHeart)null, grower: (CompGrowsFleshmassTendrils)null, target: (Building)null, zombie: (Zombie)null, decay: Array.Empty<Building>(), startTick: 0, error: rootError);
					SetFleshmassContractSettings(AttackMode.Everything, SmashMode.DoorsOnly, ordinary: true, tankSuicide: true, special: true);
					var baselineFleshbeasts = CurrentFleshbeastIds(map);
					var heart = SpawnFleshmassBuilding("FleshmassHeart", root + new IntVec3(13, 0, 8), map, Faction.OfEntities, null, spawned) as Building_FleshmassHeart;
					SuppressFleshmassScenarioHeartDefenders(heart);
					DestroyNewFleshbeasts(map, baselineFleshbeasts);
					var grower = heart?.GetComp<CompGrowsFleshmassTendrils>();
					var target = SpawnFleshmassBuilding("Fleshmass_Active", root, map, Faction.OfEntities, heart, spawned);
					var decay = SpawnFleshmassScenarioField(map, root + new IntVec3(4, 0, -3), 4, 2, heart, spawned);
					if (grower != null)
						SetResponseRemaining(grower, 1000);
					if (target?.def?.useHitPoints == true)
						target.HitPoints = target.MaxHitPoints;
					var zombie = SpawnFleshmassScenarioZombie(ZombieType.Normal, root + IntVec3.West, map, $"{FleshmassScenarioPrefix} source-loss", spawned);
					_ = SpawnFleshmassScenarioColonist(root + new IntVec3(15, 0, -10), map, $"{FleshmassScenarioPrefix} source-loss observer", spawned);
					beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
					StartFleshmassScenarioStumble(zombie);
					return (success: heart != null && grower != null && target != null && zombie != null && decay.Length == 8, map, root, heart, grower, target, zombie, decay, startTick: Find.TickManager.TicksGame, error: (object)null);
				}, cancellationToken);
				if (fixture.success == false)
					return new { success = false, stage = "sourceLoss", fixture.error };

				var sawAttackStatic = false;
				var sawInFlightAttack = false;
				var lossTriggered = false;
				var responseBeforeLoss = 0;
				var sourceSpawnedAtLoss = true;
				var sourceDestroyedAtLoss = false;
				var targetStillReferencesSourceAtLoss = false;
				var targetHitPointsAtLoss = 0;
				var zombieDamageAppliedAtLoss = false;
				var targetDestroyedByZombieDamageAtLoss = false;
				var attackDeadline = fixture.startTick + 1800;
				await SetFleshmassScenarioSpeedAsync(ctx, cancellationToken, speed);
				var attackWait = await ctx.Game.RunUntilAsync(() =>
				{
					_ = PruneFleshmassScenarioDefenders(fixture.map);
					var attackingTarget = fixture.zombie.CurJobDef == JobDefOf.AttackStatic
						&& fixture.zombie.CurJob?.targetA.Thing == fixture.target;
					sawAttackStatic |= attackingTarget;
					var inFlightAttack = attackingTarget && fixture.zombie.stances?.FullBodyBusy == true;
					sawInFlightAttack |= inFlightAttack;
					if (inFlightAttack && lossTriggered == false)
					{
						responseBeforeLoss = ResponseRemaining(fixture.grower);
						var heartWasDestroyable = fixture.heart.def.destroyable;
						try
						{
							fixture.heart.def.destroyable = true;
							fixture.heart.Destroy(DestroyMode.KillFinalize);
						}
						finally
						{
							fixture.heart.def.destroyable = heartWasDestroyable;
						}
						if (fixture.target.def.useHitPoints)
							fixture.target.HitPoints = Math.Min(fixture.target.HitPoints, 1);
						sourceSpawnedAtLoss = fixture.heart.Spawned;
						sourceDestroyedAtLoss = fixture.heart.Destroyed;
						targetStillReferencesSourceAtLoss = ReferenceEquals(fixture.target.TryGetComp<CompFleshmass>()?.source, fixture.heart);
						targetHitPointsAtLoss = ScenarioHitPoints(fixture.target);
						_ = fixture.target.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 99999f, 0f, -1f, fixture.zombie));
						zombieDamageAppliedAtLoss = true;
						targetDestroyedByZombieDamageAtLoss = fixture.target.Destroyed;
						lossTriggered = true;
					}
					return lossTriggered || Find.TickManager.TicksGame >= attackDeadline;
				}, new RimBridgeWaitOptions { TimeoutMs = timeoutMs, FailIfBusy = true }, cancellationToken);
				if (lossTriggered == false)
				{
					await PauseFleshmassScenarioAsync(ctx, CancellationToken.None);
					return new
					{
						success = false,
						stage = "sourceLoss",
						error = "The zombie did not enter AttackStatic before the game-tick deadline.",
						attackWait = DescribeFleshmassWait(attackWait)
					};
				}
				var loss = new
				{
					responseBefore = responseBeforeLoss,
					sourceSpawned = sourceSpawnedAtLoss,
					sourceDestroyed = sourceDestroyedAtLoss,
					targetStillReferencesSource = targetStillReferencesSourceAtLoss,
					targetHitPoints = targetHitPointsAtLoss,
					zombieDamageApplied = zombieDamageAppliedAtLoss,
					targetDestroyedByZombieDamage = targetDestroyedByZombieDamageAtLoss
				};

				var killDeadline = Find.TickManager.TicksGame + 1800;
				await SetFleshmassScenarioSpeedAsync(ctx, cancellationToken, speed);
				var killWait = await ctx.Game.RunUntilAsync(() =>
				{
					_ = PruneFleshmassScenarioDefenders(fixture.map);
					return fixture.target.Destroyed
						|| fixture.target.Spawned == false
						|| Find.TickManager.TicksGame >= killDeadline;
				},
					new RimBridgeWaitOptions { TimeoutMs = timeoutMs, FailIfBusy = true }, cancellationToken);
				await PauseFleshmassScenarioAsync(ctx, CancellationToken.None);

				var result = await ctx.MainThread.InvokeAsync(() =>
				{
					var afterAttack = ResponseRemaining(fixture.grower);
					foreach (var cell in fixture.decay.Where(cell => cell.Destroyed == false).ToArray())
						cell.Destroy(DestroyMode.KillFinalize);
					var afterDecay = ResponseRemaining(fixture.grower);
					var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).Count(letter => beforeLetters.Contains(letter) == false);
					return new
					{
						success = sawAttackStatic
							&& sawInFlightAttack
							&& loss.sourceSpawned == false
							&& loss.sourceDestroyed
							&& loss.targetStillReferencesSource
							&& loss.zombieDamageApplied
							&& loss.targetDestroyedByZombieDamage
							&& fixture.target.Destroyed
							&& afterAttack == loss.responseBefore
							&& afterDecay == loss.responseBefore
							&& newLetters == 0,
						stage = "sourceLoss",
						root = ZombieRuntimeActions.DescribeCell(fixture.root),
						sawAttackStatic,
						sawInFlightAttack,
						loss,
						afterAttack,
						afterDecay,
						decayCells = fixture.decay.Length,
						newLetters,
						attackWait = DescribeFleshmassWait(attackWait),
						killWait = DescribeFleshmassWait(killWait)
					};
				}, cancellationToken);
				return result;
			}
			finally
			{
				await ctx.MainThread.InvokeAsync(() =>
				{
					RestoreZombieSettings(settingsSnapshot);
					if (cleanup)
					{
						foreach (var thing in spawned.AsEnumerable().Reverse())
							CleanupFleshmassContractThing(thing);
						if (beforeLetters != null)
							RemoveNewLetters(beforeLetters);
						PruneDestroyedContractZombies(CurrentMap);
					}
				}, CancellationToken.None);
			}
		}

		static async Task<object> RunFleshmassSaveLoadStageAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			TimeSpeed speed,
			int timeoutMs,
			string fixtureSaveName,
			bool cleanup)
		{
			var settingsSnapshot = await ctx.MainThread.InvokeAsync(SnapshotZombieSettings, cancellationToken);
			var oldGameSpawned = new List<Thing>();
			try
			{
				var fixture = await ctx.MainThread.InvokeAsync(() =>
				{
					var map = CurrentMap;
					if (map == null)
						return (success: false, map, root: IntVec3.Invalid, heart: (Building_FleshmassHeart)null, target: (Building)null, zombie: (Zombie)null, colonist: (Pawn)null, startTick: 0, error: (object)new { error = "No current map." });
					if (TryFindFleshmassContractRoot(map, 42, 32, out var root, out var rootError) == false)
						return (success: false, map, root: IntVec3.Invalid, heart: (Building_FleshmassHeart)null, target: (Building)null, zombie: (Zombie)null, colonist: (Pawn)null, startTick: 0, error: rootError);

					var lower = new SettingsGroup
					{
						attackMode = AttackMode.Everything,
						smashMode = SmashMode.DoorsOnly,
						smashOnlyWhenAgitated = false,
						ordinaryZombiesAttackFleshmass = true,
						tankyAndSuicideZombiesAttackFleshmass = false,
						formerColonistAndSpecialZombiesAttackFleshmass = true,
						zombiesDieOnZeroThreat = false,
						zombieFreeEvents = false
					};
					var upper = lower.MakeCopy();
					upper.ordinaryZombiesAttackFleshmass = false;
					upper.tankyAndSuicideZombiesAttackFleshmass = true;
					upper.formerColonistAndSpecialZombiesAttackFleshmass = false;
					ZombieSettings.Values = lower.MakeCopy();
					ZombieSettings.ValuesOverTime = new List<SettingsKeyFrame>
					{
						new() { amount = 0, unit = SettingsKeyFrame.Unit.Days, values = lower },
						new() { amount = 2, unit = SettingsKeyFrame.Unit.Days, values = upper }
					};

					var baselineFleshbeasts = CurrentFleshbeastIds(map);
					var heart = SpawnFleshmassBuilding("FleshmassHeart", root + new IntVec3(13, 0, 8), map, Faction.OfEntities, null, oldGameSpawned) as Building_FleshmassHeart;
					SuppressFleshmassScenarioHeartDefenders(heart);
					DestroyNewFleshbeasts(map, baselineFleshbeasts);
					var target = SpawnFleshmassBuilding("Fleshmass_Active", root, map, Faction.OfEntities, heart, oldGameSpawned);
					if (target?.def?.useHitPoints == true)
						target.HitPoints = target.MaxHitPoints;
					var zombie = SpawnFleshmassScenarioZombie(ZombieType.Normal, root + IntVec3.West, map, $"{FleshmassScenarioPrefix} save-load", oldGameSpawned);
					var colonist = SpawnFleshmassScenarioColonist(root + new IntVec3(15, 0, -10), map, $"{FleshmassScenarioPrefix} save-load observer", oldGameSpawned);
					StartFleshmassScenarioStumble(zombie);
					return (success: heart != null && target != null && zombie != null && colonist != null, map, root, heart, target, zombie, colonist, startTick: Find.TickManager.TicksGame, error: (object)null);
				}, cancellationToken);
				if (fixture.success == false)
					return new { success = false, stage = "saveLoad", fixture.error };

				var sawAttackBeforeSave = false;
				var beforeSaveDeadline = fixture.startTick + 1800;
				await SetFleshmassScenarioSpeedAsync(ctx, cancellationToken, speed);
				var beforeSaveWait = await ctx.Game.RunUntilAsync(() =>
				{
					_ = PruneFleshmassScenarioDefenders(fixture.map);
					var attackingTarget = fixture.zombie.CurJobDef == JobDefOf.AttackStatic
						&& fixture.zombie.CurJob?.targetA.Thing == fixture.target;
					var inFlightAttack = attackingTarget && fixture.zombie.stances?.FullBodyBusy == true;
					sawAttackBeforeSave |= inFlightAttack;
					if (inFlightAttack)
						Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
					return sawAttackBeforeSave || Find.TickManager.TicksGame >= beforeSaveDeadline;
				}, new RimBridgeWaitOptions { TimeoutMs = timeoutMs, FailIfBusy = true }, cancellationToken);
				await PauseFleshmassScenarioAsync(ctx, CancellationToken.None);
				if (sawAttackBeforeSave == false)
				{
					if (cleanup)
					{
						await ctx.MainThread.InvokeAsync(() =>
						{
							foreach (var thing in oldGameSpawned.AsEnumerable().Reverse())
								CleanupFleshmassContractThing(thing);
							PruneDestroyedContractZombies(fixture.map);
						}, CancellationToken.None);
					}
					return new
					{
						success = false,
						stage = "saveLoad",
						error = "The zombie did not enter AttackStatic before the game-tick deadline.",
						beforeSaveWait = DescribeFleshmassWait(beforeSaveWait)
					};
				}

				var beforeSave = await ctx.MainThread.InvokeAsync(() => new
				{
					zombieId = ZombieRuntimeActions.StableThingId(fixture.zombie),
					targetId = ZombieRuntimeActions.StableThingId(fixture.target),
					heartId = ZombieRuntimeActions.StableThingId(fixture.heart),
					colonistId = ZombieRuntimeActions.StableThingId(fixture.colonist),
					job = fixture.zombie.CurJobDef?.defName,
					jobTargetId = fixture.zombie.CurJob?.targetA.Thing == null ? null : ZombieRuntimeActions.StableThingId(fixture.zombie.CurJob.targetA.Thing),
					targetHitPoints = ScenarioHitPoints(fixture.target),
					settings = DescribeFleshmassSettings(ZombieSettings.Values),
					day1 = DescribeFleshmassSettings(ZombieSettings.CalculateInterpolation(ZombieSettings.ValuesOverTime, GenDate.TicksPerDay)),
					day2 = DescribeFleshmassSettings(ZombieSettings.CalculateInterpolation(ZombieSettings.ValuesOverTime, 2 * GenDate.TicksPerDay))
				}, cancellationToken);

				var save = await RequireFleshmassScenarioToolCallAsync(ctx, cancellationToken, "fixture.save", "rimworld/save_game", new { saveName = fixtureSaveName }, 150000);
				var load = await RequireFleshmassScenarioToolCallAsync(ctx, cancellationToken, "fixture.reload", "rimworld/load_game_ready", new
				{
					saveName = fixtureSaveName,
					readiness = "visual",
					pauseIfNeeded = true,
					timeoutMs = 120000
				}, 150000);

				var loaded = await ctx.MainThread.InvokeAsync(() =>
				{
					var map = CurrentMap;
					var zombie = FindFleshmassScenarioThing(map, beforeSave.zombieId) as Zombie;
					var target = FindFleshmassScenarioThing(map, beforeSave.targetId) as Building;
					var heart = FindFleshmassScenarioThing(map, beforeSave.heartId) as Building_FleshmassHeart;
					var colonist = FindFleshmassScenarioThing(map, beforeSave.colonistId) as Pawn;
					var frames = ZombieSettings.ValuesOverTime;
					var day1 = frames == null ? null : ZombieSettings.CalculateInterpolation(frames, GenDate.TicksPerDay);
					var day2 = frames == null ? null : ZombieSettings.CalculateInterpolation(frames, 2 * GenDate.TicksPerDay);
					var settingsPersisted = frames?.Count == 2
						&& ZombieSettings.Values?.ordinaryZombiesAttackFleshmass == true
						&& ZombieSettings.Values?.tankyAndSuicideZombiesAttackFleshmass == false
						&& ZombieSettings.Values?.formerColonistAndSpecialZombiesAttackFleshmass == true
						&& day1?.ordinaryZombiesAttackFleshmass == true
						&& day1?.tankyAndSuicideZombiesAttackFleshmass == false
						&& day1?.formerColonistAndSpecialZombiesAttackFleshmass == true
						&& day2?.ordinaryZombiesAttackFleshmass == false
						&& day2?.tankyAndSuicideZombiesAttackFleshmass == true
						&& day2?.formerColonistAndSpecialZombiesAttackFleshmass == false;
					var jobPersisted = zombie?.CurJobDef == JobDefOf.AttackStatic && zombie.CurJob?.targetA.Thing == target;
					var inFlightStancePersisted = zombie?.stances?.FullBodyBusy == true;
					var loadedJob = zombie?.CurJobDef?.defName;
					var loadedJobTargetId = zombie?.CurJob?.targetA.Thing == null ? null : ZombieRuntimeActions.StableThingId(zombie.CurJob.targetA.Thing);
					if (target?.def?.useHitPoints == true)
						target.HitPoints = Math.Min(target.HitPoints, 1);
					var restartedAfterLoad = false;
					if (jobPersisted && target?.Destroyed == false)
					{
						StartFleshmassScenarioStumble(zombie);
						restartedAfterLoad = true;
					}
					return new
					{
						map,
						zombie,
						target,
						heart,
						colonist,
						settingsPersisted,
						jobPersisted,
						inFlightStancePersisted,
						restartedAfterLoad,
						job = loadedJob,
						jobTargetId = loadedJobTargetId,
						settings = DescribeFleshmassSettings(ZombieSettings.Values),
						day1 = DescribeFleshmassSettings(day1),
						day2 = DescribeFleshmassSettings(day2)
					};
				}, cancellationToken);
				if (loaded.map == null || loaded.zombie == null || loaded.target == null || loaded.heart == null || loaded.colonist == null)
					return new { success = false, stage = "saveLoad", error = "One or more fixture entities did not survive save-load.", beforeSave, loaded, save, load };

				var sawAttackAfterLoad = loaded.jobPersisted;
				var afterLoadDeadline = Find.TickManager.TicksGame + 1800;
				await SetFleshmassScenarioSpeedAsync(ctx, cancellationToken, speed);
				var afterLoadWait = await ctx.Game.RunUntilAsync(() =>
				{
					_ = PruneFleshmassScenarioDefenders(loaded.map);
					sawAttackAfterLoad |= loaded.zombie.CurJobDef == JobDefOf.AttackStatic && loaded.zombie.CurJob?.targetA.Thing == loaded.target;
					return loaded.target.Destroyed
						|| loaded.target.Spawned == false
						|| Find.TickManager.TicksGame >= afterLoadDeadline;
				}, new RimBridgeWaitOptions { TimeoutMs = timeoutMs, FailIfBusy = true }, cancellationToken);
				await PauseFleshmassScenarioAsync(ctx, CancellationToken.None);

				var result = await ctx.MainThread.InvokeAsync(() => new
				{
					success = sawAttackBeforeSave
						&& loaded.jobPersisted
						&& sawAttackAfterLoad
						&& loaded.target.Destroyed
						&& loaded.colonist.Destroyed == false
						&& loaded.colonist.Dead == false
						&& loaded.colonist.Spawned
						&& loaded.settingsPersisted,
					stage = "saveLoad",
					fixtureSaveName,
					root = ZombieRuntimeActions.DescribeCell(fixture.root),
					sawAttackBeforeSave,
					sawAttackAfterLoad,
					beforeSave,
					loaded = new
					{
						loaded.settingsPersisted,
						loaded.jobPersisted,
						loaded.inFlightStancePersisted,
						loaded.restartedAfterLoad,
						loaded.job,
						loaded.jobTargetId,
						loaded.settings,
						loaded.day1,
						loaded.day2,
						colonistAlive = loaded.colonist.Destroyed == false && loaded.colonist.Dead == false && loaded.colonist.Spawned,
						targetDestroyed = loaded.target.Destroyed
					},
					save,
					load,
					beforeSaveWait = DescribeFleshmassWait(beforeSaveWait),
					afterLoadWait = DescribeFleshmassWait(afterLoadWait)
				}, cancellationToken);

				if (cleanup)
				{
					await ctx.MainThread.InvokeAsync(() =>
					{
						CleanupFleshmassContractThing(loaded.zombie);
						CleanupFleshmassContractThing(loaded.target);
						CleanupFleshmassContractThing(loaded.heart);
						CleanupFleshmassContractThing(loaded.colonist);
						PruneDestroyedContractZombies(loaded.map);
					}, CancellationToken.None);
				}
				return result;
			}
			finally
			{
				await ctx.MainThread.InvokeAsync(() => RestoreZombieSettings(settingsSnapshot), CancellationToken.None);
			}
		}

		static async Task<object> RunFleshmassDenseStageAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			TimeSpeed speed,
			int timeoutMs,
			int zombieCount,
			bool cleanup)
		{
			var settingsSnapshot = await ctx.MainThread.InvokeAsync(SnapshotZombieSettings, cancellationToken);
			FleshmassDenseFixture fixture = null;
			try
			{
				fixture = await ctx.MainThread.InvokeAsync(() => BuildFleshmassDenseFixture(zombieCount), cancellationToken);
				if (fixture?.map == null || fixture.firstHeart == null || fixture.secondHeart == null || fixture.zombies.Count < zombieCount)
				{
					return new
					{
						success = false,
						stage = "dense",
						error = "Could not create the requested dense two-heart fortress fixture.",
						spawnedZombies = fixture?.zombies.Count ?? 0,
						requestedZombies = zombieCount
					};
				}

				var sawAttack = new HashSet<string>(StringComparer.Ordinal);
				var resumedAfterAttack = new HashSet<string>(StringComparer.Ordinal);
				var maxConcurrentActions = 0;
				var maxFleshbeasts = 0;
				var deadlineTick = fixture.startTick + 9000;
				var targetDestroyed = Math.Min(70, Math.Max(30, (fixture.firstField.Count + fixture.secondField.Count) / 5));
				await SetFleshmassScenarioSpeedAsync(ctx, cancellationToken, speed);
				var wait = await ctx.Game.RunUntilAsync(() =>
				{
					var liveZombies = fixture.zombies.Where(zombie => zombie != null && zombie.Destroyed == false && zombie.Spawned).ToArray();
					var concurrent = 0;
					foreach (var zombie in liveZombies)
					{
						var id = ZombieRuntimeActions.StableThingId(zombie);
						var acting = zombie.CurJobDef == JobDefOf.AttackStatic || zombie.bombWillGoOff;
						if (acting)
						{
							concurrent++;
							sawAttack.Add(id);
						}
						else if (sawAttack.Contains(id))
						{
							resumedAfterAttack.Add(id);
						}
					}
					maxConcurrentActions = Math.Max(maxConcurrentActions, concurrent);
					var newAssaultFleshbeasts = fixture.map.mapPawns.AllPawnsSpawned
						.Where(pawn => fixture.baselineFleshbeasts.Contains(ZombieRuntimeActions.StableThingId(pawn)) == false)
						.Count(IsFleshmassAssaultPawn);
					maxFleshbeasts = Math.Max(maxFleshbeasts, newAssaultFleshbeasts);
					var firstDestroyed = fixture.firstField.Count(building => building.Destroyed || building.Spawned == false);
					var secondDestroyed = fixture.secondField.Count(building => building.Destroyed || building.Spawned == false);
					var totalDestroyed = firstDestroyed + secondDestroyed;
					return (totalDestroyed >= targetDestroyed
						&& firstDestroyed > 0
						&& secondDestroyed > 0
						&& maxConcurrentActions >= 5
						&& resumedAfterAttack.Count >= 5
						&& maxFleshbeasts > 0)
						|| Find.TickManager.TicksGame >= deadlineTick;
				}, new RimBridgeWaitOptions { TimeoutMs = timeoutMs, FailIfBusy = true }, cancellationToken);
				await PauseFleshmassScenarioAsync(ctx, CancellationToken.None);

				var result = await ctx.MainThread.InvokeAsync(() =>
				{
					var firstDestroyed = fixture.firstField.Count(building => building.Destroyed || building.Spawned == false);
					var secondDestroyed = fixture.secondField.Count(building => building.Destroyed || building.Spawned == false);
					var newBeasts = fixture.map.mapPawns.AllPawnsSpawned
						.Where(pawn => FleshbeastUtility.IsFleshBeast(pawn.kindDef))
						.Where(pawn => fixture.baselineFleshbeasts.Contains(ZombieRuntimeActions.StableThingId(pawn)) == false)
						.ToArray();
					var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
						.Where(letter => fixture.baselineLetters.Contains(letter) == false)
						.ToArray();
					var expectedLabel = "FleshmassResponseLabel".Translate().ToString();
					var leakedResponseLetters = newLetters.Count(letter => letter.def == LetterDefOf.ThreatBig && letter.Label == expectedLabel);
					var livingScenarioColonists = fixture.colonists.Count(pawn => pawn.Destroyed == false && pawn.Dead == false && pawn.Spawned);
					var fortressRemaining = fixture.fortress.Count(building => building.Destroyed == false && building.Spawned);
					var totalDestroyed = firstDestroyed + secondDestroyed;
					return new
					{
						success = totalDestroyed >= targetDestroyed
							&& firstDestroyed > 0
							&& secondDestroyed > 0
							&& maxConcurrentActions >= 5
							&& resumedAfterAttack.Count >= 5
							&& newBeasts.Length > 0
							&& newBeasts.Any(IsFleshmassAssaultPawn)
							&& leakedResponseLetters == 0,
						stage = "dense",
						root = ZombieRuntimeActions.DescribeCell(fixture.root),
						zombieCount = fixture.zombies.Count,
						zombieTypes = fixture.zombies.GroupBy(DescribeFleshmassScenarioZombieType).ToDictionary(group => group.Key, group => group.Count()),
						firstFieldCells = fixture.firstField.Count,
						secondFieldCells = fixture.secondField.Count,
						firstDestroyed,
						secondDestroyed,
						totalDestroyed,
						targetDestroyed,
						firstResponseBefore = fixture.firstResponseBefore,
						firstResponseAfter = ResponseRemaining(fixture.firstGrower),
						secondResponseBefore = fixture.secondResponseBefore,
						secondResponseAfter = ResponseRemaining(fixture.secondGrower),
						maxConcurrentActions,
						sawActionCount = sawAttack.Count,
						resumedAfterActionCount = resumedAfterAttack.Count,
						newFleshbeastCount = newBeasts.Length,
						fleshbeastKinds = newBeasts.GroupBy(pawn => pawn.kindDef?.defName ?? "unknown").ToDictionary(group => group.Key, group => group.Count()),
						fleshbeastsWithAssaultLord = newBeasts.Count(IsFleshmassAssaultPawn),
						leakedResponseLetters,
						newLetters = newLetters.Select(letter => new { label = letter.Label, def = letter.def?.defName }).ToArray(),
						fortress = new
						{
							initialBuildings = fixture.fortress.Count,
							remainingBuildings = fortressRemaining,
							initialColonists = fixture.colonists.Count,
							livingColonists = livingScenarioColonists
						},
						wait = DescribeFleshmassWait(wait)
					};
				}, cancellationToken);
				return result;
			}
			finally
			{
				await ctx.MainThread.InvokeAsync(() =>
				{
					RestoreZombieSettings(settingsSnapshot);
					if (cleanup && fixture != null)
					{
						DestroyNewFleshbeasts(fixture.map, fixture.baselineFleshbeasts);
						foreach (var thing in fixture.spawned.AsEnumerable().Reverse())
							CleanupFleshmassContractThing(thing);
						RemoveNewLetters(fixture.baselineLetters);
						PruneDestroyedContractZombies(fixture.map);
					}
				}, CancellationToken.None);
			}
		}

		static FleshmassDenseFixture BuildFleshmassDenseFixture(int zombieCount)
		{
			var map = CurrentMap;
			if (map == null || TryFindFleshmassContractRoot(map, 84, 70, out var root, out _) == false)
				return null;
			var fixture = new FleshmassDenseFixture
			{
				map = map,
				root = root,
				startTick = Find.TickManager.TicksGame,
				baselineFleshbeasts = CurrentFleshbeastIds(map),
				baselineLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet()
			};
			var settings = new SettingsGroup
			{
				attackMode = AttackMode.Everything,
				smashMode = SmashMode.DoorsOnly,
				smashOnlyWhenAgitated = false,
				enemiesAttackZombies = true,
				animalsAttackZombies = true,
				anomalyAttacksZombies = AnomalyTargetingOverride.Allow,
				ordinaryZombiesAttackFleshmass = true,
				tankyAndSuicideZombiesAttackFleshmass = true,
				formerColonistAndSpecialZombiesAttackFleshmass = true,
				zombiesDieOnZeroThreat = false,
				zombieFreeEvents = false,
				maximumNumberOfZombies = Math.Max(500, zombieCount + 100)
			};
			ZombieSettings.Values = settings;
			ZombieSettings.ValuesOverTime = new List<SettingsKeyFrame>
			{
				new() { amount = 0, unit = SettingsKeyFrame.Unit.Days, values = settings.MakeCopy() }
			};

			fixture.firstHeart = SpawnFleshmassBuilding("FleshmassHeart", root + new IntVec3(-32, 0, 20), map, Faction.OfEntities, null, fixture.spawned) as Building_FleshmassHeart;
			fixture.secondHeart = SpawnFleshmassBuilding("FleshmassHeart", root + new IntVec3(30, 0, 20), map, Faction.OfEntities, null, fixture.spawned) as Building_FleshmassHeart;
			fixture.firstGrower = fixture.firstHeart?.GetComp<CompGrowsFleshmassTendrils>();
			fixture.secondGrower = fixture.secondHeart?.GetComp<CompGrowsFleshmassTendrils>();
			if (fixture.firstGrower == null || fixture.secondGrower == null)
				return fixture;

			var fieldStart = root + new IntVec3(-14, 0, 5);
			for (var z = 0; z < 10; z++)
			{
				for (var x = 0; x < 28; x++)
				{
					var first = x < 14;
					var source = first ? (Thing)fixture.firstHeart : fixture.secondHeart;
					var building = SpawnFleshmassBuilding("Fleshmass_Active", fieldStart + new IntVec3(x, 0, z), map, Faction.OfEntities, source, fixture.spawned);
					if (building == null)
						continue;
					if (building.def.useHitPoints)
						building.HitPoints = Math.Min(building.HitPoints, 12);
					(first ? fixture.firstField : fixture.secondField).Add(building);
				}
			}
			SetResponseRemaining(fixture.firstGrower, 25);
			SetResponseRemaining(fixture.secondGrower, 25);
			fixture.firstResponseBefore = ResponseRemaining(fixture.firstGrower);
			fixture.secondResponseBefore = ResponseRemaining(fixture.secondGrower);
			BuildFleshmassScenarioFortress(fixture, root + new IntVec3(0, 0, -15));
			fixture.baselineFleshbeasts = CurrentFleshbeastIds(map);
			fixture.baselineLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();

			var spawnCandidates = FleshmassDenseZombieCells(map, fieldStart, 28, 10).Take(zombieCount).ToArray();
			for (var i = 0; i < spawnCandidates.Length; i++)
			{
				var type = DenseFleshmassZombieType(i);
				var zombie = SpawnFleshmassScenarioZombie(type, spawnCandidates[i], map, $"{FleshmassScenarioPrefix} dense {i:000}", fixture.spawned);
				if (zombie == null)
					continue;
				if (type == ZombieType.Normal && i % 9 == 0)
					zombie.wasMapPawnBefore = true;
				fixture.zombies.Add(zombie);
			}

			var routeTargets = fixture.firstField.Concat(fixture.secondField).Select(building => building.Position).ToArray();
			foreach (var zombie in fixture.zombies)
			{
				if (zombie.IsTanky && routeTargets.Length > 0)
					zombie.tankDestination = routeTargets.OrderBy(cell => cell.DistanceToSquared(zombie.Position)).First();
			}
			var info = ZombieWanderer.GetMapInfo(map);
			var recalc = info.RecalculateAll(routeTargets, CurrentZombies(map).OfType<Zombie>());
			var steps = 0;
			while (steps++ < 16384 && recalc.MoveNext())
			{
			}
			fixture.startTick = Find.TickManager.TicksGame;
			foreach (var zombie in fixture.zombies)
			{
				StartFleshmassScenarioStumble(zombie);
			}
			return fixture;
		}

		static Building[] SpawnFleshmassScenarioField(Map map, IntVec3 start, int width, int height, Thing source, List<Thing> spawned)
		{
			var buildings = new List<Building>();
			for (var z = 0; z < height; z++)
			{
				for (var x = 0; x < width; x++)
				{
					var building = SpawnFleshmassBuilding("Fleshmass_Active", start + new IntVec3(x, 0, z), map, Faction.OfEntities, source, spawned);
					if (building != null)
						buildings.Add(building);
				}
			}
			return buildings.ToArray();
		}

		static HashSet<string> CurrentFleshbeastIds(Map map)
		{
			return map?.mapPawns?.AllPawnsSpawned
				.Where(pawn => FleshbeastUtility.IsFleshBeast(pawn.kindDef))
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet() ?? new HashSet<string>();
		}

		static bool IsFleshmassAssaultPawn(Pawn pawn)
		{
			return pawn?.GetLord()?.LordJob?.GetType().FullName == "RimWorld.LordJob_FleshbeastAssault";
		}

		static void SuppressFleshmassScenarioHeartDefenders(Building_FleshmassHeart heart)
		{
			var comp = heart?.GetComp<CompFleshmassHeart>();
			if (comp != null && heartNextFleshbeastField != null)
				heartNextFleshbeastField.SetValue(comp, int.MaxValue);
			var grower = heart?.GetComp<CompGrowsFleshmassTendrils>();
			if (grower != null && growthPointsField != null)
				growthPointsField.SetValue(grower, 0);
		}

		static int PruneFleshmassScenarioDefenders(Map map)
		{
			if (map?.mapPawns == null)
				return 0;
			var defenders = map.mapPawns.AllPawnsSpawned
				.Where(pawn => FleshbeastUtility.IsFleshBeast(pawn.kindDef))
				.Where(pawn => pawn.GetLord()?.LordJob?.GetType().FullName == "RimWorld.LordJob_DefendFleshmassHeart")
				.ToArray();
			foreach (var defender in defenders)
				defender.Destroy(DestroyMode.Vanish);
			return defenders.Length;
		}

		static Pawn SpawnFleshmassScenarioColonist(IntVec3 cell, Map map, string name, List<Thing> spawned)
		{
			var colonist = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
			if (colonist == null)
				return null;
			colonist.Name = new NameSingle(name);
			GenSpawn.Spawn(colonist, cell, map, Rot4.South);
			DisablePawnWork(colonist);
			spawned?.Add(colonist);
			return colonist;
		}

		static void DestroyNewFleshbeasts(Map map, HashSet<string> baseline)
		{
			if (map == null)
				return;
			foreach (var pawn in map.mapPawns.AllPawnsSpawned
				.Where(pawn => FleshbeastUtility.IsFleshBeast(pawn.kindDef))
				.Where(pawn => baseline.Contains(ZombieRuntimeActions.StableThingId(pawn)) == false)
				.ToArray())
			{
				pawn.Destroy(DestroyMode.Vanish);
			}
		}

		static Thing FindFleshmassScenarioThing(Map map, string stableId)
		{
			if (map == null || string.IsNullOrWhiteSpace(stableId))
				return null;
			return map.listerThings.AllThings.FirstOrDefault(thing => ZombieRuntimeActions.StableThingId(thing) == stableId)
				?? map.mapPawns.AllPawns.FirstOrDefault(pawn => ZombieRuntimeActions.StableThingId(pawn) == stableId);
		}

		static void BuildFleshmassScenarioFortress(FleshmassDenseFixture fixture, IntVec3 center)
		{
			var map = fixture.map;
			var minX = center.x - 7;
			var maxX = center.x + 7;
			var minZ = center.z - 5;
			var maxZ = center.z + 5;
			for (var x = minX; x <= maxX; x++)
			{
				SpawnFortressEdge(new IntVec3(x, 0, minZ));
				SpawnFortressEdge(new IntVec3(x, 0, maxZ));
			}
			for (var z = minZ + 1; z < maxZ; z++)
			{
				SpawnFortressEdge(new IntVec3(minX, 0, z));
				SpawnFortressEdge(new IntVec3(maxX, 0, z));
			}

			var doorCell = new IntVec3(center.x, 0, maxZ);
			var existingWall = doorCell.GetEdifice(map);
			if (existingWall != null)
			{
				fixture.fortress.Remove(existingWall);
				fixture.spawned.Remove(existingWall);
				existingWall.Destroy(DestroyMode.Vanish);
			}
			var door = ThingMaker.MakeThing(ThingDefOf.Door, ThingDefOf.Steel) as Building;
			if (door != null)
			{
				door.SetFaction(Faction.OfPlayer);
				GenSpawn.Spawn(door, doorCell, map, Rot4.North);
				fixture.fortress.Add(door);
				fixture.spawned.Add(door);
			}

			for (var i = 0; i < 2; i++)
			{
				var colonist = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
				colonist.Name = new NameSingle($"{FleshmassScenarioPrefix} colonist {i + 1}");
				GenSpawn.Spawn(colonist, center + new IntVec3(i == 0 ? -2 : 2, 0, 0), map, Rot4.South);
				DisablePawnWork(colonist);
				fixture.colonists.Add(colonist);
				fixture.spawned.Add(colonist);
			}

			var turretDef = DefDatabase<ThingDef>.GetNamedSilentFail("Turret_MiniTurret");
			var turret = turretDef == null ? null : ThingMaker.MakeThing(turretDef, ThingDefOf.Steel) as Building;
			if (turret != null)
			{
				turret.SetFaction(Faction.OfPlayer);
				GenSpawn.Spawn(turret, center + new IntVec3(0, 0, 2), map, Rot4.North);
				fixture.fortress.Add(turret);
				fixture.spawned.Add(turret);
			}
			return;

			void SpawnFortressEdge(IntVec3 cell)
			{
				var wall = SpawnContractWall(cell, map, Faction.OfPlayer, fixture.spawned);
				if (wall != null)
					fixture.fortress.Add(wall);
			}
		}

		static IEnumerable<IntVec3> FleshmassDenseZombieCells(Map map, IntVec3 fieldStart, int width, int height)
		{
			var candidates = new List<IntVec3>();
			for (var ring = 1; ring <= 5; ring++)
			{
				var minX = fieldStart.x - ring;
				var maxX = fieldStart.x + width - 1 + ring;
				var minZ = fieldStart.z - ring;
				var maxZ = fieldStart.z + height - 1 + ring;
				for (var x = minX; x <= maxX; x++)
				{
					candidates.Add(new IntVec3(x, 0, minZ));
					candidates.Add(new IntVec3(x, 0, maxZ));
				}
				for (var z = minZ + 1; z < maxZ; z++)
				{
					candidates.Add(new IntVec3(minX, 0, z));
					candidates.Add(new IntVec3(maxX, 0, z));
				}
			}
			return candidates
				.Distinct()
				.Where(cell => cell.InBounds(map) && cell.Standable(map) && cell.Fogged(map) == false)
				.Where(cell => cell.GetEdifice(map) == null && cell.GetThingList(map).Any(thing => thing is Pawn) == false);
		}

		static ZombieType DenseFleshmassZombieType(int index)
		{
			if (index % 19 == 0)
				return ZombieType.TankyOperator;
			if (index % 17 == 0)
				return ZombieType.SuicideBomber;
			if (index % 13 == 0)
				return ZombieType.Electrifier;
			if (index % 11 == 0)
				return ZombieType.Miner;
			if (index % 29 == 0)
				return ZombieType.DarkSlimer;
			if (index % 31 == 0)
				return ZombieType.Healer;
			if (index % 23 == 0)
				return ZombieType.ToxicSplasher;
			return ZombieType.Normal;
		}

		static string DescribeFleshmassScenarioZombieType(Zombie zombie)
		{
			if (zombie.IsTanky)
				return "TankyOperator";
			if (zombie.IsSuicideBomber)
				return "SuicideBomber";
			if (zombie.isToxicSplasher)
				return "ToxicSplasher";
			if (zombie.isMiner)
				return "Miner";
			if (zombie.isElectrifier)
				return "Electrifier";
			if (zombie.isDarkSlimer)
				return "DarkSlimer";
			if (zombie.isHealer)
				return "Healer";
			if (zombie.wasMapPawnBefore)
				return "FormerColonist";
			return "Normal";
		}

		static Zombie SpawnFleshmassScenarioZombie(ZombieType type, IntVec3 cell, Map map, string name, List<Thing> spawned)
		{
			var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, type, true);
			if (zombie == null)
				return null;
			zombie.Name = new NameSingle(name);
			zombie.state = ZombieState.Tracking;
			zombie.raging = 0;
			zombie.checkSmashable = true;
			zombie.bombWillGoOff = false;
			if (type == ZombieType.TankyOperator)
			{
				// Keep scenario categorization deterministic even if the normal generator
				// defers or randomizes visual armor initialization.
				zombie.hasTankyShield = 1f;
				zombie.hasTankyHelmet = 1f;
				zombie.hasTankySuit = 1f;
				zombie.state = ZombieState.Wandering;
			}
			spawned?.Add(zombie);
			return zombie;
		}

		static void StartFleshmassScenarioStumble(Zombie zombie)
		{
			if (zombie == null || zombie.Destroyed || zombie.Spawned == false)
				return;
			zombie.pather?.StopDead();
			zombie.stances?.CancelBusyStanceHard();
			zombie.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			zombie.jobs?.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
			if (zombie.jobs?.curDriver is JobDriver_Stumble driver)
				driver.destination = IntVec3.Invalid;
		}

		static IntVec3 PrepareFleshmassTankRoute(Zombie tank, IntVec3 destination)
		{
			if (tank?.Map == null || tank.IsTanky == false)
				return IntVec3.Invalid;
			tank.tankDestination = destination;
			var info = ZombieWanderer.GetMapInfo(tank.Map);
			var recalc = info.RecalculateAll(new[] { destination }, CurrentZombies(tank.Map).OfType<Zombie>());
			var steps = 0;
			while (steps++ < 4096 && recalc.MoveNext())
			{
			}
			return info.GetParent(tank.Position, true);
		}

		static int ScenarioHitPoints(Building building)
		{
			return building == null || building.Destroyed || building.def?.useHitPoints != true ? 0 : building.HitPoints;
		}

		static async Task SetFleshmassScenarioSpeedAsync(IRimBridgeContext ctx, CancellationToken cancellationToken, TimeSpeed speed)
		{
			await ctx.MainThread.InvokeAsync(() =>
			{
				if (Find.TickManager == null)
					return;
				Find.TickManager.CurTimeSpeed = speed;
				if (Find.TickManager.Paused)
					Find.TickManager.TogglePaused();
			}, cancellationToken);
		}

		static async Task PauseFleshmassScenarioAsync(IRimBridgeContext ctx, CancellationToken cancellationToken)
		{
			await ctx.MainThread.InvokeAsync(() =>
			{
				if (Find.TickManager != null)
					Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
			}, cancellationToken);
		}
	}
}
