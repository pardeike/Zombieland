using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		const string UninstallHygienePrefix = "ZL_Uninstall_Hygiene";

		[Tool("zombieland/uninstall_hygiene_state", Description = "Reusable S-Uninstall-Hygiene fixture: setup dirty Zombieland save state, read ZL reference counters, or run ZombieRemover on a copy save.")]
		public static object UninstallHygieneState(
			[ToolParameter(Description = "Mode: read, setup, or remove.", Required = false, DefaultValue = "read")] string actionMode = "read",
			[ToolParameter(Description = "Fixture root X cell. Negative means map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Fixture root Z cell. Negative means map center.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Copy save name to write in remove mode.", Required = false, DefaultValue = "ZL_Uninstall_Hygiene_no_zl")] string outputSaveName = "ZL_Uninstall_Hygiene_no_zl")
		{
			var mode = (actionMode ?? "read").Trim().ToLowerInvariant();
			switch (mode)
			{
				case "read":
					return ReadUninstallHygieneState(CurrentMap);
				case "setup":
					return SetupUninstallHygieneFixture(CurrentMap, x, z);
				case "remove":
					return RunUninstallHygieneRemoval(outputSaveName);
				default:
					return new
					{
						success = false,
						error = "actionMode must be one of: read, setup, remove."
					};
			}
		}

		static object SetupUninstallHygieneFixture(Map map, int x, int z)
		{
			if (map == null)
				return new
				{
					success = false,
					error = "No current map is loaded."
				};

			var errors = new List<string>();
			var spawned = new List<Pawn>();
			var root = new IntVec3(x < 0 ? map.Size.x / 2 : x, 0, z < 0 ? map.Size.z / 2 : z);
			if (root.InBounds(map) == false)
				root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);

			ClearPreviousUninstallFixture(map);
			Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;

			var colonist = SpawnLineupColonist(map, root + new IntVec3(0, 0, 0), $"{UninstallHygienePrefix}_Colonist", spawned, errors);
			var infected = SpawnLineupColonist(map, root + new IntVec3(2, 0, 0), $"{UninstallHygienePrefix}_Infected", spawned, errors);
			var normal = SpawnLineupZombie(map, root + new IntVec3(0, 0, 3), ZombieType.Normal, $"{UninstallHygienePrefix}_Normal", true, spawned, errors);
			var special = SpawnLineupZombie(map, root + new IntVec3(2, 0, 3), ZombieType.DarkSlimer, $"{UninstallHygienePrefix}_Special", true, spawned, errors);
			var corpseSource = SpawnLineupZombie(map, root + new IntVec3(4, 0, 3), ZombieType.Normal, $"{UninstallHygienePrefix}_CorpseSource", true, spawned, errors);
			var spitter = SpawnLineupSpitter(map, root + new IntVec3(6, 0, 3), $"{UninstallHygienePrefix}_Spitter", spawned, errors);
			var symbiant = SpawnLineupSymbiant(map, root + new IntVec3(8, 0, 3), $"{UninstallHygienePrefix}_Symbiant", spawned, errors);

			AddPawnZombieRefs(infected ?? colonist, normal, errors);
			AddHeldZombieThings(colonist, map, errors);
			AddZombieBuildingsAndThings(map, root, errors);
			AddZombieBillAndFilterRefs(map, root, errors);
			AddZombieHistoryRefs(normal, colonist, errors);
			AddWorldZombie(errors);
			AddContaminationRefs(map, root, colonist);

			if (corpseSource != null && corpseSource.Destroyed == false)
			{
				try
				{
					corpseSource.Kill(null);
				}
				catch (Exception ex)
				{
					errors.Add($"Could not kill corpse fixture zombie: {ex.GetType().Name}: {ex.Message}");
				}
			}

			var state = ReadUninstallHygieneState(map);
			return new
			{
				success = ObjectSuccess(state)
					&& colonist != null
					&& infected != null
					&& normal != null
					&& special != null
					&& spitter != null
					&& symbiant != null
					&& errors.Count == 0,
				action = "setup",
				root = ZombieRuntimeActions.DescribeCell(root),
				spawned = spawned.Select(DescribePawn).ToArray(),
				errors = errors.ToArray(),
				state
			};
		}

		static object RunUninstallHygieneRemoval(string outputSaveName)
		{
			var before = ReadUninstallHygieneState(CurrentMap);
			if (string.IsNullOrWhiteSpace(outputSaveName))
				return new
				{
					success = false,
					error = "outputSaveName is required.",
					before
				};

			try
			{
				ZombieRemover.RemoveZombieland(outputSaveName.Trim());
				return new
				{
					success = true,
					action = "remove",
					outputSaveName = outputSaveName.Trim(),
					before
				};
			}
			catch (Exception ex)
			{
				return new
				{
					success = false,
					action = "remove",
					outputSaveName = outputSaveName.Trim(),
					error = $"{ex.GetType().Name}: {ex.Message}",
					before
				};
			}
		}

		static object ReadUninstallHygieneState(Map map)
		{
			if (Current.Game == null)
				return new
				{
					success = false,
					error = "No current game is loaded."
				};

			var allMaps = Current.Game.Maps ?? new List<Map>();
			var pawns = allMaps.SelectMany(currentMap => currentMap.mapPawns?.AllPawns ?? Enumerable.Empty<Pawn>())
				.Concat(WorldPawnSets().SelectMany(set => set))
				.Where(pawn => pawn != null)
				.Distinct()
				.ToArray();
			var mapThings = allMaps.SelectMany(currentMap => currentMap.listerThings?.AllThings ?? Enumerable.Empty<Thing>()).ToArray();
			var filters = allMaps.SelectMany(MapZombieFilters).ToArray();
			var bills = allMaps.SelectMany(MapZombieBills).Distinct().ToArray();
			var zombieBattles = Find.BattleLog?.Battles?
				.Where(BattleHasZombieConcern)
				.ToArray() ?? Array.Empty<Battle>();
			var zombieTales = Find.TaleManager?.AllTalesListForReading?
				.Where(TaleHasZombieReference)
				.ToArray() ?? Array.Empty<Tale>();

			return new
			{
				success = true,
				hasCurrentMap = map != null,
				mapId = map?.uniqueID ?? -1,
				zombieFactionCount = Find.World?.factionManager?.AllFactions?.Count(faction => faction == Tools.GetZombieFaction() || faction.def.IsZombieDef()) ?? 0,
				mapComponents = allMaps.Sum(currentMap => currentMap.components.Count(IsUninstallZombieType)),
				worldComponents = Find.World?.components?.Count(IsUninstallZombieType) ?? 0,
				mapThings = new
				{
					totalZombieThings = mapThings.Count(IsUninstallZombieThing),
					zombies = mapThings.OfType<Zombie>().Count(),
					spitters = mapThings.OfType<ZombieSpitter>().Count(),
					symbiants = mapThings.OfType<ZombieSymbiant>().Count(),
					corpses = mapThings.Count(thing => thing.def == CustomDefs.Corpse_Zombie),
					chainsaws = mapThings.Count(thing => thing.def == CustomDefs.Chainsaw),
					thumpers = mapThings.Count(thing => thing.def == CustomDefs.Thumper),
					zombieExtract = mapThings.Where(thing => thing.def == CustomDefs.ZombieExtract).Sum(thing => thing.stackCount),
					zombieSerum = mapThings.Where(IsZombieSerumThing).Sum(thing => thing.stackCount),
					stickyGoo = mapThings.Count(thing => thing.def == CustomDefs.StickyGoo),
					tarSmoke = mapThings.Count(thing => thing.def == CustomDefs.TarSmoke)
				},
				pawns = new
				{
					total = pawns.Length,
					worldZombiePawns = WorldPawnSets().SelectMany(set => set).Count(pawn => pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter),
					zombieHediffs = pawns.Sum(CountZombieHediffs),
					zombieMemories = pawns.Sum(CountZombieMemories),
					zombieJobs = pawns.Sum(CountZombieJobs),
					zombieMentalStates = pawns.Count(pawn => pawn.mindState?.mentalStateHandler?.CurStateDef.IsZombieDef() == true || IsUninstallZombieType(pawn.mindState?.mentalStateHandler?.CurState)),
					heldZombieThings = pawns.Sum(CountHeldZombieThings)
				},
				bills = new
				{
					total = bills.Length,
					zombieRecipes = bills.Count(bill => bill?.recipe.IsZombieDef() == true),
					zombieIngredientFilters = bills.Count(bill => CountZombieFilterDefs(bill?.ingredientFilter) > 0)
				},
				filters = new
				{
					total = filters.Length,
					withZombieDefs = filters.Count(filter => CountZombieFilterDefs(filter) > 0),
					allowedZombieDefs = filters.Sum(CountZombieFilterDefs)
				},
				history = new
				{
					battles = zombieBattles.Length,
					battleEntries = zombieBattles.Sum(battle => battle.Entries.Count(entry => EntryHasZombieConcern(entry))),
					tales = zombieTales.Length
				}
			};
		}

		static void ClearPreviousUninstallFixture(Map map)
		{
			foreach (var pawn in map.mapPawns.AllPawns
				.Where(pawn => pawn.Name?.ToStringFull?.Contains(UninstallHygienePrefix) == true || pawn.Name?.ToStringShort?.Contains(UninstallHygienePrefix) == true)
				.ToArray())
				pawn.DestroyOrPassToWorld(DestroyMode.Vanish);

			foreach (var thing in map.listerThings.AllThings
				.Where(thing => thing.Label?.Contains(UninstallHygienePrefix) == true || thing.ThingID?.Contains(UninstallHygienePrefix) == true)
				.ToArray())
				thing.Destroy();
		}

		static void AddPawnZombieRefs(Pawn pawn, Pawn zombie, List<string> errors)
		{
			if (pawn == null)
				return;

			if (ZombieRuntimeActions.AddZombieBite(pawn, "final", out _, out var biteError) == false)
				errors.Add($"Could not add zombie bite: {biteError}");

			try
			{
				var part = pawn.health?.hediffSet?.GetNotMissingParts().FirstOrDefault();
				var infection = HediffMaker.MakeHediff(CustomDefs.ZombieInfection, pawn, part);
				pawn.health?.AddHediff(infection, part);
			}
			catch (Exception ex)
			{
				errors.Add($"Could not add zombie infection: {ex.GetType().Name}: {ex.Message}");
			}

			try
			{
				pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(CustomDefs.ZombieScare, zombie);
			}
			catch (Exception ex)
			{
				errors.Add($"Could not add ZombieScare memory: {ex.GetType().Name}: {ex.Message}");
			}

			try
			{
				if (EffectDefs.ContaminationStateMimicing != null)
					_ = pawn.mindState?.mentalStateHandler?.TryStartMentalState(EffectDefs.ContaminationStateMimicing, "uninstall hygiene fixture", true, false);
				if (EffectDefs.ContaminationJobMimic != null)
				{
					var job = JobMaker.MakeJob(EffectDefs.ContaminationJobMimic);
					job.expiryInterval = GenDate.TicksPerHour;
					pawn.jobs?.StartJob(job, JobCondition.Incompletable, null);
				}
			}
			catch (Exception ex)
			{
				errors.Add($"Could not add contamination job/mental state: {ex.GetType().Name}: {ex.Message}");
			}
		}

		static void AddHeldZombieThings(Pawn pawn, Map map, List<string> errors)
		{
			if (pawn == null)
				return;

			try
			{
				var chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as ThingWithComps;
				if (chainsaw != null)
				{
					var cell = pawn.Position;
					GenSpawn.Spawn(chainsaw, cell, map, WipeMode.Vanish);
					chainsaw.DeSpawn();
					pawn.equipment?.AddEquipment(chainsaw);
				}
			}
			catch (Exception ex)
			{
				errors.Add($"Could not equip chainsaw: {ex.GetType().Name}: {ex.Message}");
			}

			TryAddThingToInventory(pawn, CustomDefs.ZombieExtract, 2, errors);
			TryAddThingToInventory(pawn, DefDatabase<ThingDef>.GetNamedSilentFail("ZombieSerumSimple"), 1, errors);
		}

		static void TryAddThingToInventory(Pawn pawn, ThingDef def, int stackCount, List<string> errors)
		{
			if (pawn?.inventory?.innerContainer == null || def == null)
				return;
			try
			{
				var thing = ThingMaker.MakeThing(def);
				thing.stackCount = Math.Min(stackCount, def.stackLimit);
				if (pawn.inventory.innerContainer.TryAdd(thing) == false)
					errors.Add($"Could not add {def.defName} to pawn inventory.");
			}
			catch (Exception ex)
			{
				errors.Add($"Could not add {def.defName} to inventory: {ex.GetType().Name}: {ex.Message}");
			}
		}

		static void AddZombieBuildingsAndThings(Map map, IntVec3 root, List<string> errors)
		{
			TrySpawnThing(map, CustomDefs.ZombieExtract, root + new IntVec3(-2, 0, 1), null, 3, errors);
			TrySpawnThing(map, DefDatabase<ThingDef>.GetNamedSilentFail("ZombieSerumSimple"), root + new IntVec3(-2, 0, 2), null, 1, errors);
			TrySpawnThing(map, CustomDefs.StickyGoo, root + new IntVec3(-2, 0, 3), null, 1, errors);
			TrySpawnThing(map, CustomDefs.TarSmoke, root + new IntVec3(-2, 0, 4), null, 1, errors);

			if (TryFindClearBuildingFootprint(map, CustomDefs.Thumper, root + new IntVec3(10, 0, 0), 16f, out var thumperCell, out var footprintError))
				TrySpawnThing(map, CustomDefs.Thumper, thumperCell, ThingDefOf.Steel, 1, errors);
			else
				errors.Add($"Could not place thumper: {footprintError}");
		}

		static void TrySpawnThing(Map map, ThingDef def, IntVec3 requestedCell, ThingDef stuffDef, int stackCount, List<string> errors)
		{
			if (def == null)
				return;
			try
			{
				if (TryFindClearSpawnCell(map, requestedCell, 8f, out var cell, out var cellError) == false)
				{
					errors.Add($"Could not find cell for {def.defName}: {cellError}");
					return;
				}
				var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? stuffDef ?? ThingDefOf.Steel : null);
				thing.stackCount = Math.Min(stackCount, def.stackLimit);
				GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
			}
			catch (Exception ex)
			{
				errors.Add($"Could not spawn {def.defName}: {ex.GetType().Name}: {ex.Message}");
			}
		}

		static void AddZombieBillAndFilterRefs(Map map, IntVec3 root, List<string> errors)
		{
			var serum = DefDatabase<ThingDef>.GetNamedSilentFail("ZombieSerumSimple");
			try
			{
				if (TryFindClearSpawnCell(map, root + new IntVec3(0, 0, -4), 12f, out var stockpileCell, out var cellError) == false)
				{
					errors.Add($"Could not create stockpile filter fixture: {cellError}");
				}
				else
				{
					var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
					map.zoneManager.RegisterZone(zone);
					zone.AddCell(stockpileCell);
					zone.label = $"{UninstallHygienePrefix}_Stockpile";
					zone.settings.filter.SetAllow(CustomDefs.ZombieExtract, true);
					if (serum != null)
						zone.settings.filter.SetAllow(serum, true);
				}
			}
			catch (Exception ex)
			{
				errors.Add($"Could not create stockpile filter fixture: {ex.GetType().Name}: {ex.Message}");
			}

			try
			{
				var tableDef = DefDatabase<ThingDef>.GetNamedSilentFail("TableMachining")
					?? DefDatabase<ThingDef>.GetNamedSilentFail("ElectricTailoringBench")
					?? DefDatabase<ThingDef>.GetNamedSilentFail("FueledSmithy");
				var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("MakeZombieSerum") ?? CustomDefs.CureZombieInfection;
				if (tableDef == null || recipe == null)
				{
					errors.Add("Could not find a work table or zombie recipe for bill fixture.");
					return;
				}
				if (TryFindClearBuildingFootprint(map, tableDef, root + new IntVec3(4, 0, -4), 16f, out var tableCell, out var footprintError) == false)
				{
					errors.Add($"Could not place bill table: {footprintError}");
					return;
				}

				var table = ThingMaker.MakeThing(tableDef, tableDef.MadeFromStuff ? ThingDefOf.Steel : null) as Building_WorkTable;
				if (table == null)
				{
					errors.Add($"ThingDef {tableDef.defName} did not create a Building_WorkTable.");
					return;
				}
				GenSpawn.Spawn(table, tableCell, map, WipeMode.Vanish);
				var bill = new Bill_Production(recipe);
				bill.ingredientFilter.SetAllow(CustomDefs.ZombieExtract, true);
				if (serum != null)
					bill.ingredientFilter.SetAllow(serum, true);
				table.billStack.AddBill(bill);
			}
			catch (Exception ex)
			{
				errors.Add($"Could not create bill fixture: {ex.GetType().Name}: {ex.Message}");
			}
		}

		static void AddZombieHistoryRefs(Pawn zombie, Pawn colonist, List<string> errors)
		{
			if (zombie == null)
				return;

			try
			{
				Find.BattleLog?.Add(new BattleLogEntry_DamageTaken(colonist ?? zombie, RulePackDefOf.DamageEvent_Fire, zombie));
			}
			catch (Exception ex)
			{
				errors.Add($"Could not add zombie battle-log entry: {ex.GetType().Name}: {ex.Message}");
			}

			try
			{
				var tale = new Tale_DoublePawn(zombie, colonist ?? zombie)
				{
					def = TaleDefOf.KilledBy,
					id = Find.UniqueIDsManager.GetNextTaleID(),
					date = Find.TickManager.TicksAbs
				};
				Find.TaleManager?.Add(tale);
			}
			catch (Exception ex)
			{
				errors.Add($"Could not add zombie tale: {ex.GetType().Name}: {ex.Message}");
			}
		}

		static void AddWorldZombie(List<string> errors)
		{
			try
			{
				var pawn = PawnGenerator.GeneratePawn(ZombieDefOf.Zombie, Tools.GetZombieFaction());
				pawn.Name = new NameSingle($"{UninstallHygienePrefix}_WorldZombie");
				if (pawn.Spawned)
					pawn.DeSpawn();
				Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
			}
			catch (Exception ex)
			{
				errors.Add($"Could not add world zombie: {ex.GetType().Name}: {ex.Message}");
			}
		}

		static void AddContaminationRefs(Map map, IntVec3 root, Pawn pawn)
		{
			try
			{
				if (root.InBounds(map))
					map.SetContamination(root, 0.8f);
				pawn?.SetContamination(0.75f);
			}
			catch
			{
			}
		}

		static IEnumerable<HashSet<Pawn>> WorldPawnSets()
		{
			if (Current.Game?.World?.worldPawns == null)
				yield break;

			var traverse = Traverse.Create(Current.Game.World.worldPawns);
			foreach (var fieldName in new[] { nameof(WorldPawns.pawnsAlive), nameof(WorldPawns.pawnsMothballed), nameof(WorldPawns.pawnsDead), nameof(WorldPawns.pawnsForcefullyKeptAsWorldPawns) })
				if (traverse.Field(fieldName).GetValue<HashSet<Pawn>>() is HashSet<Pawn> pawns)
					yield return pawns;
		}

		static IEnumerable<ThingFilter> MapZombieFilters(Map map)
		{
			if (map == null)
				yield break;

			foreach (var filter in map.haulDestinationManager.AllGroups.Select(group => group.Settings?.filter).Where(filter => filter != null))
				yield return filter;
			foreach (var settings in map.listerThings.AllThings.SelectMany(ContentOfFields<StorageSettings>).Where(settings => settings?.filter != null))
				yield return settings.filter;
		}

		static IEnumerable<Bill> MapZombieBills(Map map)
		{
			if (map == null)
				yield break;

			foreach (var billStack in map.listerThings.AllThings.SelectMany(ContentOfFields<BillStack>).Where(stack => stack?.bills != null))
				foreach (var bill in billStack.bills.Where(bill => bill != null))
					yield return bill;
		}

		static IEnumerable<T> ContentOfFields<T>(object instance) where T : class
		{
			if (instance == null)
				yield break;
			foreach (var field in instance.GetType().GetFields(AccessTools.all))
				if (typeof(T).IsAssignableFrom(field.FieldType))
					if (field.GetValue(instance) is T value)
						yield return value;
		}

		static bool IsUninstallZombieThing(Thing thing)
		{
			return thing != null
				&& (thing.GetType().Namespace == Tools.zlNamespace || thing.def.IsZombieDef());
		}

		static bool IsUninstallZombieType(object obj)
		{
			return obj != null && obj.GetType().Namespace == Tools.zlNamespace;
		}

		static bool IsZombieSerumThing(Thing thing)
		{
			return string.Equals(thing?.def?.defName, "ZombieSerumSimple", StringComparison.Ordinal);
		}

		static int CountZombieHediffs(Pawn pawn)
		{
			return pawn?.health?.hediffSet?.hediffs?.Count(hediff => IsUninstallZombieType(hediff) || hediff.def.IsZombieDef() || hediff is Hediff_Injury_ZombieBite) ?? 0;
		}

		static int CountZombieMemories(Pawn pawn)
		{
			return pawn?.needs?.mood?.thoughts?.memories?.Memories?.Count(memory =>
				IsUninstallZombieType(memory)
				|| memory.def.IsZombieDef()
				|| memory.otherPawn is Zombie
				|| memory.otherPawn is ZombieSymbiant
				|| memory.otherPawn is ZombieSpitter) ?? 0;
		}

		static int CountZombieJobs(Pawn pawn)
		{
			return pawn?.jobs?.AllJobs()?.Count(job => job?.def.IsZombieDef() == true) ?? 0;
		}

		static int CountHeldZombieThings(Pawn pawn)
		{
			return CountZombieThingsInOwner(pawn?.inventory?.innerContainer)
				+ CountZombieThingsInOwner(pawn?.carryTracker?.innerContainer)
				+ (pawn?.equipment?.AllEquipmentListForReading?.Count(IsUninstallZombieThing) ?? 0)
				+ (pawn?.apparel?.WornApparel?.Count(IsUninstallZombieThing) ?? 0);
		}

		static int CountZombieThingsInOwner(ThingOwner owner)
		{
			return owner?.Count(IsUninstallZombieThing) ?? 0;
		}

		static int CountZombieFilterDefs(ThingFilter filter)
		{
			return filter?.AllowedThingDefs?.Count(def => def.IsZombieDef()) ?? 0;
		}

		static bool BattleHasZombieConcern(Battle battle)
		{
			return battle != null
				&& (battle.concerns.Any(IsUninstallZombieThing)
					|| battle.Entries.Any(EntryHasZombieConcern));
		}

		static bool EntryHasZombieConcern(LogEntry entry)
		{
			return entry?.GetConcerns()?.Any(IsUninstallZombieThing) == true;
		}

		static bool TaleHasZombieReference(Tale tale)
		{
			if (tale is Tale_SinglePawn singlePawn && IsUninstallZombieThing(singlePawn.pawnData?.pawn))
				return true;
			if (tale is Tale_SinglePawnAndDef singlePawnDef && singlePawnDef.defData?.def.IsZombieDef() == true)
				return true;
			if (tale is Tale_DoublePawn doublePawn)
				return IsUninstallZombieThing(doublePawn.firstPawnData?.pawn)
					|| IsUninstallZombieThing(doublePawn.secondPawnData?.pawn);
			if (tale is Tale_DoublePawnAndDef doublePawnDef && doublePawnDef.defData?.def.IsZombieDef() == true)
				return true;
			return false;
		}
	}
}
