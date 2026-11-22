using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Verse;

namespace ZombieLand
{
	// Multiplayer session evidence (S-Multiplayer-Lockstep). The fixture runs in single player only.
	// UI actions run from the bridge, which is interface context, so their commands sync like real input.
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/mp_fixture_features", Description = "Single player only: add Multiplayer feature-cluster content around a base center: fueled active Thumper, fueled chainsaw on a colonist, granite block next to miners, zombie corpses, a stockpile, ground and colonist contamination, and Death Pall when Anomaly is active.")]
		public static object MultiplayerFixtureFeatures(
			[ToolParameter(Description = "Base center x.")] int cx,
			[ToolParameter(Description = "Base center z.")] int cz)
		{
			var map = CurrentMap;
			if (map == null || MultiplayerSupport.IsActive)
				return new { success = false, error = "Needs a single-player map." };
			var notes = new List<string>();
			var center = new IntVec3(cx, 0, cz);

			if (TryFindClearBuildingFootprint(map, CustomDefs.Thumper, center + new IntVec3(4, 0, 4), 6f, out var thumperCell, out var thumperError) == false)
				return thumperError;
			var thumper = (ZombieThumper)ThingMaker.MakeThing(CustomDefs.Thumper);
			thumper.SetFactionDirect(Faction.OfPlayer);
			_ = GenSpawn.Spawn(thumper, thumperCell, map, Rot4.North, WipeMode.Vanish, false);
			Refuel(thumper.TryGetComp<CompRefuelable>(), map, thumperCell);
			thumper.TryGetComp<CompSwitchable>().isActive = true;
			thumper.intervalTicks = GenDate.TicksPerHour / 4;
			notes.Add($"thumper {thumper.ThingID} at {thumperCell}");

			var sawyer = map.mapPawns.FreeColonistsSpawned.Where(p => p.Drafted == false).OrderBy(p => p.thingIDNumber).FirstOrDefault();
			if (sawyer != null)
			{
				var chainsaw = (Chainsaw)ThingMaker.MakeThing(CustomDefs.Chainsaw);
				_ = GenSpawn.Spawn(chainsaw, sawyer.Position, map, WipeMode.Vanish);
				Refuel(chainsaw.TryGetComp<CompRefuelable>(), map, sawyer.Position);
				chainsaw.DeSpawn();
				sawyer.equipment.DestroyAllEquipment(DestroyMode.Vanish);
				sawyer.equipment.AddEquipment(chainsaw);
				sawyer.drafter.Drafted = true;
				notes.Add($"chainsaw on drafted {sawyer.ThingID}");
			}

			var granite = DefDatabase<ThingDef>.GetNamed("Granite");
			var rock = 0;
			foreach (var cell in CellRect.FromLimits(cx - 6, cz + 21, cx + 2, cz + 26))
				if (cell.InBounds(map) && cell.GetEdifice(map) == null && cell.GetFirstPawn(map) == null)
				{
					_ = GenSpawn.Spawn(granite, cell, map, WipeMode.Vanish);
					rock++;
				}
			// Miners dig toward the map center, so they start on the far side of the block.
			var minerZ = map.Center.z < cz + 21 ? cz + 27 : cz + 20;
			var miners = 0;
			for (var x = cx - 5; x <= cx + 1; x += 3)
			{
				var cell = new IntVec3(x, 0, minerZ);
				if (cell.InBounds(map) && cell.Standable(map) && ZombieGenerator.SpawnZombie(cell, map, ZombieType.Miner) != null)
					miners++;
			}
			notes.Add($"{rock} granite cells, {miners} miners at z {minerZ}");

			// Extract harvesting needs a colonist who can doctor; debug colonies are random.
			for (var i = 0; i < 30; i++)
			{
				var doctor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				if (doctor.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
				{
					doctor.Destroy();
					continue;
				}
				_ = GenSpawn.Spawn(doctor, CellFinder.StandableCellNear(center + new IntVec3(0, 0, -2), map, 3f), map);
				notes.Add($"doctor {doctor.ThingID}");
				break;
			}

			var corpses = 0;
			for (var i = 0; i < 6; i++)
			{
				// Inside the walls, so that harvesting colonists are not driven off by zombies. The roof makes the
				// corpses indoor, so Death Pall does not raise them first.
				var cell = center + new IntVec3(-3 + i, 0, -3);
				if (cell.InBounds(map) == false || cell.Standable(map) == false)
					continue;
				map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
				var zombie = ZombieGenerator.SpawnZombie(cell, map, ZombieType.Normal);
				zombie?.Kill(null);
				corpses++;
			}
			notes.Add($"{corpses} zombie corpses");

			var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
			map.zoneManager.RegisterZone(stockpile);
			foreach (var cell in CellRect.FromLimits(cx - 7, cz - 7, cx - 5, cz - 5))
				if (cell.InBounds(map) && cell.Standable(map))
					stockpile.AddCell(cell);
			notes.Add($"stockpile {stockpile.label} with {stockpile.Cells.Count} cells");

			foreach (var cell in CellRect.CenteredOn(center + new IntVec3(0, 0, -12), 5))
				if (cell.InBounds(map))
					map.SetContamination(cell, 0.6f);
			foreach (var pawn in map.mapPawns.FreeColonistsSpawned.Take(2))
				pawn.AddContamination(0.3f);
			notes.Add("ground and two colonists contaminated");

			var deathPall = DefDatabase<GameConditionDef>.GetNamedSilentFail("DeathPall");
			if (ModsConfig.AnomalyActive && deathPall != null)
			{
				map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeConditionPermanent(deathPall));
				notes.Add("Death Pall permanent");
			}
			return new { success = true, notes };
		}

		static void Refuel(CompRefuelable refuelable, Map map, IntVec3 cell)
		{
			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			_ = GenSpawn.Spawn(fuel, cell, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
		}

		[Tool("zombieland/mp_ui_action", Description = "Run one Zombieland UI commit path from interface context: thumper (settings dialog close), storage (stockpile range slider release), area (area risk menu), settings (settings window close with one changed field). Gizmos use rimworld/execute_gizmo.")]
		public static object MultiplayerUiAction(
			[ToolParameter(Description = "thumper, storage, area or settings.")] string action,
			[ToolParameter(Description = "thumper: intensity. storage: min. settings: new value.", Required = false, DefaultValue = "")] string value = "",
			[ToolParameter(Description = "thumper: interval ticks. storage: max.", Required = false, DefaultValue = "")] string value2 = "",
			[ToolParameter(Description = "area: area label and mode as Label=Mode. settings: SettingsGroup field name.", Required = false, DefaultValue = "")] string name = "")
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map." };
			var context = new { sessionActive = MultiplayerSupport.IsActive, inInterface = MultiplayerSupport.InInterface };
			float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
			switch (action)
			{
				case "select":
				{
					// Selector.Select runs Notify_ThingSelected, as a click does (Symbiant discovery).
					Thing target = name switch
					{
						"thumper" => map.listerThings.AllThings.OfType<ZombieThumper>().FirstOrDefault(),
						"symbiant" => map.mapPawns.AllPawnsSpawned.OfType<ZombieSymbiant>().FirstOrDefault(),
						"chainsaw" => map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p.equipment?.Primary is Chainsaw),
						_ => map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.thingIDNumber.ToString(CultureInfo.InvariantCulture) == name),
					};
					if (target == null)
						return new { success = false, error = $"Nothing to select for {name}." };
					Find.Selector.ClearSelection();
					Find.Selector.Select(target, false, false);
					return new { success = true, context, selected = target.ThingID, target.thingIDNumber };
				}
				case "thumper":
				{
					var thumper = map.listerThings.AllThings.OfType<ZombieThumper>().FirstOrDefault();
					if (thumper == null)
						return new { success = false, error = "No thumper." };
					var dialog = new Dialog_ThumperSettings(thumper);
					Find.WindowStack.Add(dialog);
					Traverse.Create(dialog).Field("intensity").SetValue(F(value));
					Traverse.Create(dialog).Field("intervalTicks").SetValue(int.Parse(value2, CultureInfo.InvariantCulture));
					_ = Find.WindowStack.TryRemove(dialog, false);
					return new { success = true, context, thumper = thumper.ThingID };
				}
				case "storage":
				{
					var zone = map.zoneManager.AllZones.OfType<Zone_Stockpile>().OrderBy(z => z.ID).FirstOrDefault();
					if (zone == null)
						return new { success = false, error = "No stockpile." };
					var type = typeof(ContaminationStorageRange);
					AccessTools.Field(type, "pendingSettings").SetValue(null, zone.settings);
					AccessTools.Field(type, "pendingRange").SetValue(null, new FloatRange(F(value), F(value2)));
					_ = AccessTools.Method(type, "SubmitPendingRange").Invoke(null, null);
					return new { success = true, context, zone = zone.label };
				}
				case "area":
				{
					var parts = name.Split('=');
					var area = map.areaManager.AllAreas.FirstOrDefault(a => a.Label == parts[0]);
					if (area == null || parts.Length != 2)
						return new { success = false, error = $"No area {parts[0]}." };
					MultiplayerCommands.SetAreaRiskMode(area, (AreaRiskMode)Enum.Parse(typeof(AreaRiskMode), parts[1]));
					return new { success = true, context, area = area.Label };
				}
				case "settings":
				{
					var settings = ZombieSettings.GetGameSettings();
					var field = AccessTools.Field(typeof(SettingsGroup), name);
					if (settings == null || field == null)
						return new { success = false, error = $"No settings or field {name}." };
					// Same state as an open window: DoWindowContents creates the working copy during a session.
					var copy = ZombieSettings.ValuesOverTime.Select(frame => frame.DeepCopy()).ToList();
					Traverse.Create(settings).Field("multiplayerWorkingCopy").SetValue(copy);
					Traverse.Create(settings).Field("multiplayerWorkingCopyFingerprint").SetValue(MultiplayerCommands.SettingsFingerprint(copy));
					foreach (var frame in copy)
						field.SetValue(frame.values, Convert.ChangeType(value, field.FieldType, CultureInfo.InvariantCulture));
					settings.WriteSettings();
					return new { success = true, context, field = name };
				}
			}
			return new { success = false, error = $"Unknown action {action}." };
		}

		[Tool("zombieland/mp_sync_state", Description = "Per-peer digest of synchronized Zombieland and map state, one sorted row list per section. Compare host and client at a paused tick.")]
		public static object MultiplayerSyncState()
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map." };
			static string R(float f) => f.ToString("R", CultureInfo.InvariantCulture);
			var things = map.listerThings.AllThings.Where(t => t.Spawned && t is not Mote).ToList();
			var sections = new Dictionary<string, List<string>>
			{
				["things"] = things.Select(t => $"{t.thingIDNumber}|{t.def.defName}|{t.Position.x},{t.Position.z}|{t.HitPoints}|{t.stackCount}").ToList(),
				["pawnHealth"] = map.mapPawns.AllPawnsSpawned.Where(p => p is not Zombie)
					.Select(p => $"{p.thingIDNumber}|{string.Join(",", p.health.hediffSet.hediffs.Select(h => $"{h.def.defName}:{R(h.Severity)}"))}|{p.CurJobDef?.defName}").ToList(),
				["colonistFlags"] = map.mapPawns.FreeColonistsSpawned.Select(p =>
				{
					var config = ColonistSettings.colonists?.TryGetValue(p) ?? new ColonistConfig();
					return $"{p.thingIDNumber}|{config.autoAvoidZombies}|{config.autoExtractZombieSerum}|{config.autoDoubleTap}";
				}).ToList(),
				["switches"] = things.Select(t => (t, comp: t.TryGetComp<CompSwitchable>())).Where(x => x.comp != null).Select(x => $"{x.t.thingIDNumber}|{x.comp.isActive}").ToList(),
				["thumpers"] = things.OfType<ZombieThumper>().Select(t => $"{t.thingIDNumber}|{R(t.intensity)}|{t.intervalTicks}").ToList(),
				["chainsaws"] = map.mapPawns.AllPawnsSpawned.Select(p => p.equipment?.Primary as Chainsaw).Where(c => c != null)
					.Select(c => $"{c.thingIDNumber}|{c.pawn?.thingIDNumber}|{c.running}|{R(c.TryGetComp<CompRefuelable>().Fuel)}").ToList(),
				["areas"] = map.areaManager.AllAreas.Select(a => $"{a.ID}|{a.Label}|{Dialog_ManageAreas_Patches.GetMode(a)}").ToList(),
				["storage"] = map.zoneManager.AllZones.OfType<Zone_Stockpile>().Select(z => $"{z.ID}|{ContaminationStorageRange.GetRange(z.settings)}").ToList(),
				["settings"] = new List<string> { Fnv(MultiplayerCommands.SettingsFingerprint(ZombieSettings.ValuesOverTime)), $"colonyMultiplier={R(ZombieSettings.Values.colonyMultiplier)}" },
				["constants"] = ((Dictionary<string, string>)AccessTools.Method(typeof(MultiplayerConstants), "Capture").Invoke(null, null)).Select(kv => $"{kv.Key}={kv.Value}").ToList(),
				["contamination"] = ContaminationManager.Instance.contaminations.Select(kv => $"{kv.Key}|{R(kv.Value)}")
					.Append($"ground|{R(map.GetContamination()?.cells?.Sum() ?? 0f)}").ToList(),
				["conditions"] = map.gameConditionManager.ActiveConditions.Select(c => $"{c.def.defName}|{c.TicksPassed}").ToList(),
			};
			foreach (var rows in sections.Values)
				rows.Sort(StringComparer.Ordinal);
			return new
			{
				success = true,
				ticksGame = GenTicks.TicksGame,
				hashes = sections.ToDictionary(kv => kv.Key, kv => Fnv(string.Join("\n", kv.Value))),
				counts = sections.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
				sections
			};
		}

		// string.GetHashCode is randomized per process, so peers need a stable hash.
		static string Fnv(string text)
		{
			var hash = 14695981039346656037UL;
			foreach (var c in text)
			{
				hash ^= c;
				hash *= 1099511628211UL;
			}
			return hash.ToString("x16");
		}
	}
}
