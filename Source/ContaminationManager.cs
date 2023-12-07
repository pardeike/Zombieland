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

		public Dictionary<int, float> contaminations = new();
		public Dictionary<int, ContaminationGrid> grounds = new();
		public bool showContaminationOverlay;
		public int nextDecontaminationQuest = 0;

		public CellBoolDrawer currentMapDrawer;
		public Map currentDrawerMap;
		public bool currentMapDirty;

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

		public override void ExposeData()
		{
			base.ExposeData();

			if (Scribe.mode == LoadSaveMode.LoadingVars)
				Reset(); // clear cache

			if (Constants.CONTAMINATION)
				Scribe_Values.Look(ref showContaminationOverlay, "showContaminationOverlay");
			else
				showContaminationOverlay = false;

			this.ExposeContamination();
			this.ExposeGrounds();
		}

		public override void WorldComponentTick()
		{
			var ticks = Find.TickManager.TicksGame;
			if (ticks > nextDecontaminationQuest)
			{
				if (nextDecontaminationQuest != 0)
					DecontaminationQuest();
				nextDecontaminationQuest = ticks + (int)ZombieSettings.Values.contamination.decontaminationQuestInterval;
			}
		}

		public float Get(Thing thing, bool includeHoldings = true)
		{
			if (thing is Mineable mineable)
			{
				var map = mineable.Map;
				if (map != null)
					return grounds[map.Index][mineable.Position];
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

		public void Set(Thing thing, float contamination)
		{
			contaminations[thing.thingIDNumber] = contamination;
			if (LOGGING)
			{
#pragma warning disable CS0162 // Unreachable code detected
				Log.ResetMessageCount();
				Log.Message($"THING {thing} at {thing.SimplePos()} => {(contamination * 100):F1}");
#pragma warning restore CS0162 // Unreachable code detected
			}
		}

		public void UpdatePawnHediff(Thing thing, float contamination)
		{
			if (thing is not Pawn pawn || pawn is Zombie || pawn is ZombieBlob || pawn is ZombieSpitter)
				return;

			if (contamination > 0)
			{
				var hediff = (Hediff_Contamination)pawn.health.hediffSet.GetFirstHediffOfDef(CustomDefs.ContaminationEffect);
				hediff ??= (Hediff_Contamination)pawn.health.AddHediff(CustomDefs.ContaminationEffect);
				hediff.Severity = contamination;
			}
			else
			{
				var hediff = (Hediff_Contamination)pawn.health.hediffSet.GetFirstHediffOfDef(CustomDefs.ContaminationEffect);
				if (hediff != null)
					pawn.health.RemoveHediff(hediff);
			}
		}

		public float ChangeDirectly(LocalTargetInfo info, Map map, float amount)
		{
			if (amount == 0)
				return 0;

			var grid = (ContaminationGrid)null;
			var id = -1;
			var thing = info.thingInt;

			IntVec3 cell;
			if (thing is Mineable)
			{
				map ??= thing.Map;
				cell = thing.Position;
				thing = null;
			}
			else
				cell = info.cellInt;

			if (thing != null)
			{
				id = thing.thingIDNumber;
				map ??= thing.Map;
			}

			if (map == null)
				return 0;

			float contamination;
			if (thing == null)
			{
				grid = grounds[map.Index];
				contamination = grid[cell];
			}
			else
				contaminations.TryGetValue(id, out contamination);

			if (-amount > contamination)
				amount = -contamination;
			contamination += amount;

			if (contamination <= 0)
			{
				if (thing == null)
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
					UpdatePawnHediff(thing, 0);
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

				if (thing == null)
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
					UpdatePawnHediff(thing, contamination);
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

		public void Add(Thing thing, float amount)
		{
			ChangeDirectly(thing, null, amount);
			if (thing is Pawn pawn)
			{
				var need = pawn.needs?.TryGetNeed<ContaminationNeed>();
				if (need != null)
					need.lastGainTick = Find.TickManager.TicksGame;
			}
		}

		public float Subtract(Thing thing, float amount)
		{
			if (thing is not IThingHolder holder)
				return -ChangeDirectly(thing, null, -amount);
			var hasMain = contaminations.ContainsKey(thing.thingIDNumber);
			var removed = 0f;
			var innerThings = ThingOwnerUtility.GetAllThingsRecursively(holder, false);
			var subAmount = amount / (innerThings.Count + (hasMain ? 1 : 0));
			foreach (var innerThing in innerThings)
				removed -= ChangeDirectly(innerThing, thing.Map, -subAmount);
			if (hasMain)
				removed -= ChangeDirectly(thing, null, -subAmount);
			return removed;
		}

		public void Remove(Thing thing)
		{
			if (thing is Mineable mineable)
			{
				var map = mineable.Map;
				if (map != null)
				{
					grounds[map.Index][mineable.Position] = 0;
					return;
				}
			}

			contaminations.Remove(thing.thingIDNumber);
			if (thing is IThingHolder holder)
			{
				var innerThings = ThingOwnerUtility.GetAllThingsRecursively(holder, false);
				foreach (var innerThing in innerThings)
					contaminations.Remove(innerThing.thingIDNumber);
			}
		}

		public float Equalize(LocalTargetInfo t1, LocalTargetInfo t2, float weight = 0.5f, bool includeHoldings1 = true, bool includeHoldings2 = true)
		{
			var map = (t1.Thing ?? t2.Thing).Map;

			var _grid = (ContaminationGrid)null;
			ContaminationGrid cachedGrid()
			{
				_grid ??= grounds[map.Index];
				return _grid;
			}

			var isT1 = t1.thingInt != null;
			var isT2 = t2.thingInt != null;
			if (isT1 == false && isT2 == false)
				throw new Exception($"cannot equalize cells only ({t1} to {t2}, weight {weight})");
			var c1 = isT1 ? Get(t1.thingInt, includeHoldings1) : cachedGrid()[t1.cellInt];
			var c2 = isT2 ? Get(t2.thingInt, includeHoldings2) : cachedGrid()[t2.cellInt];
			if (c1 < c2)
				(c1, c2, t1, t2) = (c2, c1, t2, t1);
			var transfer = c1 * (1 - weight) + c2 * weight - c1;
			if (transfer == 0)
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
			return currentDrawerMap.thingGrid.thingGrid[index]
				.Where(t => t is not Mineable)
				.Sum(t => contaminations.TryGetValue(t.thingIDNumber, 0)) > 0;
		}

		public Color GetCellExtraColor(int index)
		{
			if (currentDrawerMap == null)
				return Color.clear;
			var things = currentDrawerMap.thingGrid.ThingsListAtFast(index);
			var allContamination = things.Sum(t => contaminations.TryGetValue(t.thingIDNumber, 0));
			var a = GenMath.LerpDoubleClamped(0, 1, 0, 0.8f, Mathf.Pow(allContamination, 0.7f));
			return ContaminationGrid.color.ToTransparent(a);
		}

		public void DrawerUpdate()
		{
			var map = Find.CurrentMap;
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
		public bool GetCellBool(int index) => cells[index] > 0 && map.fogGrid.IsFogged(index) == false || DebugViewSettings.drawFog == false;
		public Color GetCellExtraColor(int index) => color.ToTransparent(Mathf.Cos(pi_half * Mathf.Pow(cells[index] - 1, 3))); // https://www.desmos.com/calculator/hnvwykal4v
		public void SetDirty() => debouncer.Run(drawer.SetDirty);

		public float this[IntVec3 cell]
		{
			get => cells[cell.z * mapSizeX + cell.x];
			set
			{
				cells[cell.z * mapSizeX + cell.x] = value >= 0 ? value : 0;
				SetDirty();
			}
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
			=> ContaminationManager.Instance.grounds[map.Index];

		public static float GetContamination(this Map map, IntVec3 cell, bool safeMode = false)
			=> safeMode == false || cell.InBounds(map) ? ContaminationManager.Instance.grounds[map.Index][cell] : 0;

		public static void SetContamination(this Thing thing, float value)
			=> ContaminationManager.Instance.Set(thing, value);

		public static void SetContamination(this Map map, IntVec3 cell, float value, bool safeMode = false)
		{
			if (safeMode == false || cell.InBounds(map))
				ContaminationManager.Instance.grounds[map.Index][cell] = value;
		}
		public static void AddContamination(this Map map, IntVec3 cell, float value, bool safeMode = false)
		{
			if (safeMode == false || cell.InBounds(map))
			{
				var grid = ContaminationManager.Instance.grounds[map.Index];
				grid[cell] = Mathf.Clamp(grid[cell] + value, 0, 1);
			}
		}

		public static CellBoolDrawer GetContaminationDrawer(this Map map)
			=> ContaminationManager.Instance.grounds[map.Index].drawer;

		public static float Equalize(this float factor, LocalTargetInfo info1, LocalTargetInfo info2, bool includeHoldings1 = true, bool includeHoldings2 = true)
			=> ContaminationManager.Instance.Equalize(info1, info2, factor, includeHoldings1, includeHoldings2);

		public static void AddContamination(this Thing thing, float val, sbyte? tempMapIndex = null, float factor = 1f)
		{
			if (val <= 0)
				return;
			var savedMapIndex = thing.mapIndexOrState;
			if (tempMapIndex.HasValue)
				thing.mapIndexOrState = tempMapIndex.Value;
			ContaminationManager.Instance.Add(thing, val * factor);
			if (tempMapIndex.HasValue)
				thing.mapIndexOrState = savedMapIndex;
		}

		public static float SubtractContamination(this Thing thing, float val)
			=> ContaminationManager.Instance.Subtract(thing, val);

		public static void ClearContamination(this Thing thing)
			=> ContaminationManager.Instance.Remove(thing);

		public static void Transfer(this ContaminationManager contamination, Thing from, float factor, Thing[] toArray)
		{
			var value = contamination.Get(from);
			if (value == 0)
				return;
			var subtracted = contamination.Subtract(from, value * factor);
			if (subtracted == 0)
				return;
			var n = toArray?.Length ?? 0;
			if (n == 0)
				return;
			var delta = subtracted / n;
			for (var j = 0; j < n; j++)
				contamination.Add(toArray[j], delta);
		}

		public static void TransferContamination(this Thing from, float factor, params Thing[] toArray)
			=> ContaminationManager.Instance.Transfer(from, factor, toArray);

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
			var drawer = map.GetContaminationDrawer();
			drawer.CellBoolDrawerUpdate();
			drawer.MarkForDraw();
			ContaminationManager.Instance.DrawerUpdate();
		}
	}
}