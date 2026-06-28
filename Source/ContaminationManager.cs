using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class ContaminationManager : WorldComponent, ICellBoolGiver
	{
		public const bool LOGGING = false;
		public const float DecontaminationImmunityDaysAtDifficulty100 = 15f;
		public const float DecontaminationImmunityDaysAtDifficulty500 = 12f;

		public Dictionary<int, float> contaminations = new();
		public Dictionary<int, ContaminationGrid> grounds = new();
		public Dictionary<int, int> decontaminationImmunityUntilTicks = new();
		public bool showContaminationOverlay;
		public int nextDecontaminationQuest = 0;

		public CellBoolDrawer currentMapDrawer;
		public Map currentDrawerMap;
		public bool currentMapDirty;
		readonly HashSet<int> lazyGroundWarnings = new();

		public ContaminationManager(World world) : base(world)
		{
		}

		private static ContaminationManager _instance = null;
		public static ContaminationManager Instance
		{
			get
			{
				_instance ??= Current.Game.World.GetComponent<ContaminationManager>();
				return _instance;
			}
		}

		public static void Reset() => _instance = null;

		public static bool CanDrawOverlayFor(Map map) => Tools.MapViewActiveFor(map);

		public static int DecontaminationImmunityTicks()
		{
			var days = GenMath.LerpDoubleClamped(1f, 5f, DecontaminationImmunityDaysAtDifficulty100, DecontaminationImmunityDaysAtDifficulty500, Tools.Difficulty());
			return Mathf.Max(GenDate.TicksPerDay, Mathf.RoundToInt(days * GenDate.TicksPerDay));
		}

		public void GrantDecontaminationImmunity(Pawn pawn, int ticks)
		{
			if (pawn == null || ticks <= 0)
				return;
			decontaminationImmunityUntilTicks ??= new Dictionary<int, int>();
			var untilTick = Find.TickManager.TicksGame + ticks;
			if (decontaminationImmunityUntilTicks.TryGetValue(pawn.thingIDNumber, out var existingUntilTick))
				untilTick = Mathf.Max(untilTick, existingUntilTick);
			decontaminationImmunityUntilTicks[pawn.thingIDNumber] = untilTick;
			Remove(pawn);
		}

		public bool HasDecontaminationImmunity(Pawn pawn)
			=> DecontaminationImmunityTicksLeft(pawn) > 0;

		public int DecontaminationImmunityTicksLeft(Pawn pawn)
		{
			if (pawn == null || decontaminationImmunityUntilTicks == null)
				return 0;
			if (decontaminationImmunityUntilTicks.TryGetValue(pawn.thingIDNumber, out var untilTick) == false)
				return 0;
			var ticksLeft = untilTick - Find.TickManager.TicksGame;
			if (ticksLeft > 0)
				return ticksLeft;
			decontaminationImmunityUntilTicks.Remove(pawn.thingIDNumber);
			return 0;
		}

		void PruneExpiredDecontaminationImmunity(int currentTick)
		{
			if (decontaminationImmunityUntilTicks == null || decontaminationImmunityUntilTicks.Count == 0)
				return;
			var expired = decontaminationImmunityUntilTicks
				.Where(entry => entry.Value <= currentTick)
				.Select(entry => entry.Key)
				.ToArray();
			for (var i = 0; i < expired.Length; i++)
				decontaminationImmunityUntilTicks.Remove(expired[i]);
		}

		internal bool BlocksContaminationGain(Thing thing)
			=> thing is Pawn pawn && HasDecontaminationImmunity(pawn);

		public void ClearCurrentDrawer()
		{
			currentMapDrawer = null;
			currentDrawerMap = null;
			currentMapDirty = true;
		}

		public override void ExposeData()
		{
			base.ExposeData();

			if (Scribe.mode == LoadSaveMode.LoadingVars)
				Reset(); // clear cache

			if (Constants.CONTAMINATION)
				Scribe_Values.Look(ref showContaminationOverlay, "showContaminationOverlay");
			else
				showContaminationOverlay = false;

			Scribe_Collections.Look(ref decontaminationImmunityUntilTicks, "decontaminationImmunityUntilTicks", LookMode.Value, LookMode.Value);
			decontaminationImmunityUntilTicks ??= new Dictionary<int, int>();

			this.ExposeContamination();
			this.ExposeGrounds();
		}

		public override void WorldComponentTick()
		{
			var ticks = Find.TickManager.TicksGame;
			if (ticks % GenDate.TicksPerHour == 0)
				PruneExpiredDecontaminationImmunity(ticks);
			if (ticks > nextDecontaminationQuest)
			{
				if (nextDecontaminationQuest != 0)
					DecontaminationQuest();
				nextDecontaminationQuest = ticks + (int)ZombieSettings.Values.contamination.decontaminationQuestInterval;
			}
		}

		public ContaminationGrid GetOrCreateGrounds(Map map)
		{
			if (map == null)
				return null;

			var idx = map.Index;
			if (grounds.TryGetValue(idx, out var grid) && grid != null)
			{
				if (grid.map != map || grid.drawer == null || grid.mapSizeX != map.Size.x)
				{
					var expectedCells = map.Size.x * map.Size.z;
					if (grid.cells?.Length == expectedCells)
						grid.AddMap(map);
					else
					{
						WarnLazyGroundRepair(map, $"replaced invalid contamination grid ({grid.cells?.Length ?? 0} cells, expected {expectedCells})");
						grid = new ContaminationGrid(map);
						grounds[idx] = grid;
					}
				}
				return grid;
			}

			grid = new ContaminationGrid(map);
			grounds[idx] = grid;
			WarnLazyGroundRepair(map, "created missing contamination grid");
			return grid;
		}

		void WarnLazyGroundRepair(Map map, string reason)
		{
			if (lazyGroundWarnings.Add(map.Index))
				Log.Warning($"[Zombieland] {reason} for map {map.Index}. This should normally be prepared during map generation or load repair.");
		}

		public float Get(Thing thing, bool includeHoldings = true, Map contextMap = null)
		{
			if (thing == null)
				return 0f;

			if (TryGetCellBackedContaminationTarget(thing, contextMap, out var map, out var cell))
			{
				var grid = GetOrCreateGrounds(map);
				return grid[cell];
			}

			var sum = 0f;
			if (contaminations.TryGetValue(thing.thingIDNumber, out var contamination))
				sum += contamination;
			if (includeHoldings && thing is IThingHolder holder)
			{
				var innerThings = ThingOwnerUtility.GetAllThingsRecursively(holder, false);
				foreach (var innerThing in innerThings)
					if (contaminations.TryGetValue(innerThing.thingIDNumber, out contamination))
						sum += contamination;
			}
			return sum;
		}

		public void Set(Thing thing, float contamination, Map contextMap = null)
		{
			if (thing == null)
				return;

			if (TryGetCellBackedContaminationTarget(thing, contextMap, out var map, out var cell))
			{
				var grid = GetOrCreateGrounds(map);
				grid[cell] = Mathf.Clamp01(contamination);
				return;
			}

			if (contamination <= 0)
			{
				Remove(thing, contextMap);
				return;
			}
			if (BlocksContaminationGain(thing))
			{
				RemoveDirectContamination(thing, contextMap);
				return;
			}

			contamination = Mathf.Clamp01(contamination);
			contaminations[thing.thingIDNumber] = contamination;
			UpdatePawnHediff(thing, contamination, contextMap);
			currentMapDirty = true;
			if (LOGGING)
			{
#pragma warning disable CS0162 // Unreachable code detected
				Log.ResetMessageCount();
				Log.Message($"THING {thing} at {thing.SimplePos()} => {(contamination * 100):F1}");
#pragma warning restore CS0162 // Unreachable code detected
			}
		}

		public void UpdatePawnHediff(Thing thing, float contamination, Map contextMap = null)
		{
			if (thing is not Pawn pawn)
				return;

			var effects = TryGetThingMap(pawn, contextMap, out var map) ? map.GetComponent<TickManager>()?.contaminationEffects : null;
			if (pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter)
			{
				effects?.Remove(pawn);
				return;
			}

			if (contamination > 0)
			{
				var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(CustomDefs.ContaminationEffect) as Hediff_Contamination;
				hediff ??= pawn.health.AddHediff(CustomDefs.ContaminationEffect) as Hediff_Contamination;
				if (hediff == null)
				{
					effects?.Remove(pawn);
					return;
				}
				hediff.Severity = contamination;
				effects?.Add(pawn);
			}
			else
			{
				var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(CustomDefs.ContaminationEffect) as Hediff_Contamination;
				if (hediff != null)
					pawn.health.RemoveHediff(hediff);
				effects?.Remove(pawn);
			}
		}

		public float ChangeDirectly(LocalTargetInfo info, Map map, float amount)
		{
			if (amount == 0)
				return 0;

			var grid = (ContaminationGrid)null;
			var id = -1;
			var thing = info.thingInt;
			var useThingBacking = thing != null;

			IntVec3 cell;
			if (TryGetCellBackedContaminationTarget(thing, map, out map, out cell))
			{
				useThingBacking = false;
				thing = null;
			}
			else
				cell = info.cellInt;

			if (useThingBacking)
				id = thing.thingIDNumber;

			if (amount > 0f && useThingBacking && BlocksContaminationGain(thing))
			{
				RemoveDirectContamination(thing, map);
				return 0f;
			}

			float contamination;
			if (useThingBacking == false)
			{
				if (map == null || cell.IsValid == false || cell.InBounds(map) == false)
					return 0;
				grid = GetOrCreateGrounds(map);
				contamination = grid[cell];
			}
			else
				contaminations.TryGetValue(id, out contamination);

			if (-amount > contamination)
				amount = -contamination;
			contamination += amount;

			if (contamination <= 0)
			{
				if (useThingBacking == false)
				{
					grid[cell] = 0;
					if (LOGGING)
					{
#pragma warning disable CS0162 // Unreachable code detected
						Log.ResetMessageCount();
						Log.Message($"CELL {cell.SimplePos()} cleared");
#pragma warning restore CS0162 // Unreachable code detected
					}
				}
				else
				{
					contaminations.Remove(id);
					UpdatePawnHediff(thing, 0, map);
					currentMapDirty = true;
					if (LOGGING)
					{
#pragma warning disable CS0162 // Unreachable code detected
						Log.ResetMessageCount();
						Log.Message($"THING {thing} at {thing.SimplePos()} cleared");
#pragma warning restore CS0162 // Unreachable code detected
					}
				}
			}
			else if (contamination > 0)
			{
				if (contamination > 1)
					contamination = 1;

				if (useThingBacking == false)
				{
					grid[cell] = contamination;
					if (LOGGING)
					{
#pragma warning disable CS0162 // Unreachable code detected
						Log.ResetMessageCount();
						Log.Message($"CELL {cell.SimplePos()} => {(contamination * 100):F1}");
#pragma warning restore CS0162 // Unreachable code detected
					}
				}
				else
				{
					contaminations[id] = contamination;
					UpdatePawnHediff(thing, contamination, map);
					currentMapDirty = true;
					if (LOGGING)
					{
#pragma warning disable CS0162 // Unreachable code detected
						Log.ResetMessageCount();
						Log.Message($"THING {thing} at {thing.SimplePos()} => {(contamination * 100):F1}");
#pragma warning restore CS0162 // Unreachable code detected
					}
				}
			}

			return amount;
		}

		void RemoveDirectContamination(Thing thing, Map contextMap = null)
		{
			if (thing == null)
				return;
			if (contaminations.Remove(thing.thingIDNumber))
			{
				UpdatePawnHediff(thing, 0, contextMap);
				currentMapDirty = true;
			}
		}

		public void Add(Thing thing, float amount, Map contextMap = null)
		{
			if (thing == null)
				return;

			if (amount > 0f && BlocksContaminationGain(thing))
			{
				RemoveDirectContamination(thing, contextMap);
				return;
			}

			ChangeDirectly(thing, contextMap, amount);
			if (thing is Pawn pawn)
			{
				var need = pawn.needs?.TryGetNeed<ContaminationNeed>();
				if (need != null)
					need.lastGainTick = Find.TickManager.TicksGame;
			}
		}

		public float Subtract(Thing thing, float amount, Map contextMap = null)
		{
			if (thing == null)
				return 0f;

			if (thing is not IThingHolder holder)
				return -ChangeDirectly(thing, contextMap, -amount);
			var hasMain = contaminations.ContainsKey(thing.thingIDNumber);
			var removed = 0f;
			var innerThings = ThingOwnerUtility.GetAllThingsRecursively(holder, false);
			var targetCount = innerThings.Count + (hasMain ? 1 : 0);
			if (targetCount == 0)
				return 0f;
			var subAmount = amount / targetCount;
			if (TryGetThingMap(thing, contextMap, out var holderMap) == false)
				holderMap = contextMap;
			foreach (var innerThing in innerThings)
				removed -= ChangeDirectly(innerThing, holderMap, -subAmount);
			if (hasMain)
				removed -= ChangeDirectly(thing, contextMap, -subAmount);
			return removed;
		}

		public void Remove(Thing thing, Map contextMap = null)
		{
			if (thing == null)
				return;

			if (TryGetCellBackedContaminationTarget(thing, contextMap, out var map, out var cell))
			{
				var grid = GetOrCreateGrounds(map);
				grid[cell] = 0;
				return;
			}

			if (contaminations.Remove(thing.thingIDNumber))
			{
				UpdatePawnHediff(thing, 0, contextMap);
				currentMapDirty = true;
			}
			if (thing is IThingHolder holder)
			{
				var innerThings = ThingOwnerUtility.GetAllThingsRecursively(holder, false);
				foreach (var innerThing in innerThings)
					if (contaminations.Remove(innerThing.thingIDNumber))
					{
						UpdatePawnHediff(innerThing, 0, contextMap);
						currentMapDirty = true;
					}
			}
		}

		public float Equalize(LocalTargetInfo t1, LocalTargetInfo t2, float weight = 0.5f, bool includeHoldings1 = true, bool includeHoldings2 = true)
		{
			_ = TryGetTargetMap(t1, t2, null, out var map);
			return Equalize(t1, t2, weight, includeHoldings1, includeHoldings2, map);
		}

		public float Equalize(LocalTargetInfo t1, LocalTargetInfo t2, Map contextMap, float weight = 0.5f, bool includeHoldings1 = true, bool includeHoldings2 = true)
		{
			_ = TryGetTargetMap(t1, t2, contextMap, out var map);
			return Equalize(t1, t2, weight, includeHoldings1, includeHoldings2, map);
		}

		float Equalize(LocalTargetInfo t1, LocalTargetInfo t2, float weight, bool includeHoldings1, bool includeHoldings2, Map map)
		{
			var _grid = (ContaminationGrid)null;
			ContaminationGrid cachedGrid()
			{
				if (map == null)
					return null;
				_grid ??= GetOrCreateGrounds(map);
				return _grid;
			}

			var isT1 = t1.thingInt != null;
			var isT2 = t2.thingInt != null;
			if (isT1 == false && isT2 == false)
				throw new Exception($"cannot equalize cells only ({t1} to {t2}, weight {weight})");
			var c1 = isT1 ? Get(t1.thingInt, includeHoldings1, map) : cachedGrid()?[t1.cellInt] ?? 0;
			var c2 = isT2 ? Get(t2.thingInt, includeHoldings2, map) : cachedGrid()?[t2.cellInt] ?? 0;
			if (c1 < c2)
				(c1, c2, t1, t2) = (c2, c1, t2, t1);
			var transfer = c1 * (1 - weight) + c2 * weight - c1;
			if (transfer == 0)
				return 0;
			if (transfer < 0f && BlocksContaminationGain(t2.thingInt))
				return 0;
			if (transfer > 0f && BlocksContaminationGain(t1.thingInt))
				return 0;
			ChangeDirectly(t1, map, transfer);
			ChangeDirectly(t2, map, -transfer);
			return transfer;
		}

		public Color Color => Color.white;

		public bool GetCellBool(int index)
		{
			if (currentDrawerMap == null || (DebugViewSettings.drawFog && currentDrawerMap.fogGrid.IsFogged(index)))
				return false;
			var contamination = currentDrawerMap.thingGrid.thingGrid[index]
				.Where(t => UsesCellBackedContamination(t) == false)
				.Sum(t => contaminations.TryGetValue(t.thingIDNumber, 0));
			return ContaminationThresholds.IsVisible(contamination);
		}

		public Color GetCellExtraColor(int index)
		{
			if (currentDrawerMap == null)
				return Color.clear;
			var things = currentDrawerMap.thingGrid.ThingsListAtFast(index);
			var allContamination = things
				.Where(t => UsesCellBackedContamination(t) == false)
				.Sum(t => contaminations.TryGetValue(t.thingIDNumber, 0));
			if (ContaminationThresholds.IsVisible(allContamination) == false)
				return Color.clear;
			var a = GenMath.LerpDoubleClamped(0, 1, 0, 0.8f, Mathf.Pow(allContamination, 0.7f));
			return ContaminationGrid.color.ToTransparent(a);
		}

		public void DrawerUpdate(Map map)
		{
			if (CanDrawOverlayFor(map) == false)
			{
				ClearCurrentDrawer();
				return;
			}

			if (currentDrawerMap != map)
			{
				currentMapDrawer = new CellBoolDrawer(this, map.Size.x, map.Size.z, 3640, 0.8f);
				currentDrawerMap = map;
				currentMapDirty = true;
			}

			var tickManager = Find.TickManager;
			if (currentMapDirty && (tickManager.TicksGame % 60 == 30 || tickManager.Paused))
			{
				currentMapDirty = false;
				currentMapDrawer.SetDirty();
			}

			currentMapDrawer.CellBoolDrawerUpdate();
			currentMapDrawer.MarkForDraw();
		}

		public void DecontaminationQuest()
		{
			if (QuestNode_GetRandomAlliedFactionLeader.GetAlliedFactionLeader() == null)
				return;
			Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(CustomDefs.Decontamination, new Slate());
			QuestUtility.SendLetterQuestAvailable(quest);
		}

		static bool UsesCellBackedContamination(Thing thing)
			=> TryGetCellBackedContaminationTarget(thing, null, out _, out _);

		static bool TryGetCellBackedContaminationTarget(Thing thing, Map fallbackMap, out Map map, out IntVec3 cell)
		{
			map = null;
			cell = IntVec3.Invalid;
			if (thing == null)
				return false;

			if (thing is not Mineable && thing.GetType() != typeof(Thing))
				return false;

			if (TryGetThingMap(thing, fallbackMap, out map) == false)
				return false;

			cell = thing.Position;
			return cell.IsValid && cell.InBounds(map) && map.thingGrid != null && map.thingGrid.ThingsListAtFast(cell).Contains(thing);
		}

		static bool TryGetTargetMap(LocalTargetInfo t1, LocalTargetInfo t2, Map fallbackMap, out Map map)
		{
			if (TryGetTargetMap(t1, fallbackMap, out map))
				return true;
			return TryGetTargetMap(t2, fallbackMap, out map);
		}

		static bool TryGetTargetMap(LocalTargetInfo target, Map fallbackMap, out Map map)
		{
			if (fallbackMap != null)
			{
				map = fallbackMap;
				return true;
			}
			if (target.thingInt != null)
				return TryGetThingMap(target.thingInt, null, out map);
			map = null;
			return false;
		}

		internal static bool TryGetThingMap(Thing thing, Map fallbackMap, out Map map)
		{
			map = fallbackMap;
			if (map != null)
				return true;

			if (thing == null)
				return false;
			return TryGetMap(thing.mapIndexOrState, out map);
		}

		internal static bool TryGetMap(sbyte? mapIndex, out Map map)
		{
			map = null;
			if (mapIndex.HasValue == false || mapIndex.Value < 0)
				return false;

			var maps = Current.Game?.Maps;
			var idx = mapIndex.Value;
			if (maps == null || idx >= maps.Count)
				return false;

			map = maps[idx];
			return map != null;
		}
	}

	public class ContaminationGrid : ICellBoolGiver
	{
		public static readonly Color color = new(0, 0.8f, 0);
		public const float pi_half = Mathf.PI / 2;
		private readonly Debouncer debouncer = new(60, false);

		public float[] cells;
		public CellBoolDrawer drawer;
		public Map map;
		public int mapSizeX;

		public ContaminationGrid(float[] cells)
		{
			this.cells = cells;
		}

		public void AddMap(Map map)
		{
			if (cells.Length != map.Size.x * map.Size.z)
				throw new Exception($"Map size ({map.Size}) does not match cell array size ({cells.Length})");
			drawer = new CellBoolDrawer(this, map.Size.x, map.Size.z, 3640, 1f);
			this.map = map;
			mapSizeX = map.Size.x;
		}

		public ContaminationGrid(Map map)
		{
			cells = new float[map.Size.x * map.Size.z];
			AddMap(map);
		}

		public Color Color => Color.white;
		public bool GetCellBool(int index) => ContaminationThresholds.IsVisible(cells[index]) && (map.fogGrid.IsFogged(index) == false || DebugViewSettings.drawFog == false);
		public Color GetCellExtraColor(int index)
		{
			var contamination = cells[index];
			if (ContaminationThresholds.IsVisible(contamination) == false)
				return Color.clear;
			return color.ToTransparent(Mathf.Cos(pi_half * Mathf.Pow(contamination - 1, 3))); // https://www.desmos.com/calculator/hnvwykal4v
		}
		public void SetDirty() => debouncer.Run(drawer.SetDirty);

		public float this[IntVec3 cell]
		{
			get => TryGetIndex(cell, out var index) ? cells[index] : 0f;
			set
			{
				if (TryGetIndex(cell, out var index) == false)
					return;
				cells[index] = value >= 0 ? value : 0;
				SetDirty();
			}
		}

		bool TryGetIndex(IntVec3 cell, out int index)
		{
			index = -1;
			if (cell.IsValid == false || mapSizeX <= 0 || cell.x < 0 || cell.z < 0 || cell.x >= mapSizeX)
				return false;
			index = cell.z * mapSizeX + cell.x;
			if (index < 0 || index >= cells.Length)
			{
				index = -1;
				return false;
			}
			return true;
		}
	}

	[StaticConstructorOnStartup]
	public static class ContaminationExtension
	{
		static readonly Material[] contaminationMaterials;

		static ContaminationExtension()
		{
			contaminationMaterials = new Material[100];
			for (var i = 0; i < 100; i++)
				contaminationMaterials[i] = SolidColorMaterials.NewSolidColorMaterial(Color.green.ToTransparent(i / 99f), ShaderDatabase.MoteGlow);
		}

		public static float GetContamination(this Thing thing, bool includeHoldings = false)
			=> ContaminationManager.Instance.Get(thing, includeHoldings);

		public static float GetEffectiveness(this Thing thing)
			=> Mathf.Max(0.05f, 1 - ContaminationManager.Instance.Get(thing, false) * ZombieSettings.Values.contamination.contaminationEffectivenessPercentage);

		public static ContaminationGrid GetContamination(this Map map)
			=> ContaminationManager.Instance.GetOrCreateGrounds(map);

		public static float GetContamination(this Map map, IntVec3 cell, bool safeMode = false)
		{
			if (map == null || (safeMode && cell.InBounds(map) == false))
				return 0f;
			var grid = ContaminationManager.Instance.GetOrCreateGrounds(map);
			return grid[cell];
		}

		public static void SetContamination(this Thing thing, float value)
			=> ContaminationManager.Instance.Set(thing, value);

		public static void SetContamination(this Map map, IntVec3 cell, float value, bool safeMode = false)
		{
			if (map == null || (safeMode && cell.InBounds(map) == false))
				return;
			var grid = ContaminationManager.Instance.GetOrCreateGrounds(map);
			grid[cell] = value;
		}
		public static void AddContamination(this Map map, IntVec3 cell, float value, bool safeMode = false)
		{
			if (map == null || (safeMode && cell.InBounds(map) == false))
				return;
			var grid = ContaminationManager.Instance.GetOrCreateGrounds(map);
			grid[cell] = Mathf.Clamp(grid[cell] + value, 0, 1);
		}

		public static CellBoolDrawer GetContaminationDrawer(this Map map)
			=> map.GetContamination()?.drawer;

		public static float Equalize(this float factor, LocalTargetInfo info1, LocalTargetInfo info2, bool includeHoldings1 = true, bool includeHoldings2 = true)
			=> ContaminationManager.Instance.Equalize(info1, info2, factor, includeHoldings1, includeHoldings2);

		public static float Equalize(this float factor, LocalTargetInfo info1, LocalTargetInfo info2, Map contextMap, bool includeHoldings1 = true, bool includeHoldings2 = true)
			=> ContaminationManager.Instance.Equalize(info1, info2, contextMap, factor, includeHoldings1, includeHoldings2);

		public static void AddContamination(this Thing thing, float val, sbyte? tempMapIndex = null, float factor = 1f)
		{
			if (val <= 0)
				return;
			_ = ContaminationManager.TryGetMap(tempMapIndex, out var contextMap);
			ContaminationManager.Instance.Add(thing, val * factor, contextMap);
		}

		public static float SubtractContamination(this Thing thing, float val)
			=> ContaminationManager.Instance.Subtract(thing, val);

		public static float SubtractContamination(this Thing thing, float val, Map contextMap)
			=> ContaminationManager.Instance.Subtract(thing, val, contextMap);

		public static void ClearContamination(this Thing thing)
			=> ContaminationManager.Instance.Remove(thing);

		public static void ClearContamination(this Thing thing, Map contextMap)
			=> ContaminationManager.Instance.Remove(thing, contextMap);

		public static void Transfer(this ContaminationManager contamination, Thing from, float factor, Thing[] toArray)
			=> contamination.Transfer(from, factor, null, toArray);

		public static void Transfer(this ContaminationManager contamination, Thing from, float factor, Map contextMap, Thing[] toArray)
		{
			var value = contamination.Get(from, true, contextMap);
			if (value == 0)
				return;
			var targets = toArray?
				.Where(thing => thing != null && contamination.BlocksContaminationGain(thing) == false)
				.ToArray();
			var n = targets?.Length ?? 0;
			if (n == 0)
				return;
			var subtracted = contamination.Subtract(from, value * factor, contextMap);
			if (subtracted == 0)
				return;
			var delta = subtracted / n;
			for (var j = 0; j < n; j++)
				contamination.Add(targets[j], delta, contextMap);
		}

		public static void TransferContamination(this Thing from, float factor, params Thing[] toArray)
			=> ContaminationManager.Instance.Transfer(from, factor, toArray);

		public static void TransferContamination(this Thing from, float factor, Map contextMap, params Thing[] toArray)
			=> ContaminationManager.Instance.Transfer(from, factor, contextMap, toArray);

		public static void TransferContamination(this Thing from, Thing to)
			=> from.TransferContamination(1f, to);

		public static void TransferContamination(this IReadOnlyList<Thing> fromArray, float factor, params Thing[] toArray)
		{
			var fromCount = fromArray.Count;
			var contamination = ContaminationManager.Instance;
			for (var i = 0; i < fromCount; i++)
				contamination.Transfer(fromArray[i], factor, toArray);
		}

		public static void ContaminationGridUpdate(this Map map)
		{
			var manager = ContaminationManager.Instance;
			if (ContaminationManager.CanDrawOverlayFor(map) == false)
			{
				manager.ClearCurrentDrawer();
				return;
			}

			var drawer = map.GetContaminationDrawer();
			if (drawer == null)
				return;
			drawer.CellBoolDrawerUpdate();
			drawer.MarkForDraw();
			manager.DrawerUpdate(map);
		}
	}
}
