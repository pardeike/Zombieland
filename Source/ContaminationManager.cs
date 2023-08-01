using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class ContaminationManager : WorldComponent
	{
		public Dictionary<int, float> contaminations = new();
		public Dictionary<int, ContaminationGrid> grounds = new();
		public bool showContaminationOverlay = false;

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

		public override void ExposeData()
		{
			base.ExposeData();

			if (Scribe.mode == LoadSaveMode.LoadingVars)
				_instance = null; // clear cache

			Scribe_Values.Look(ref showContaminationOverlay, "showContaminationOverlay");

			this.ExposeContamination();
			this.ExposeGrounds();
			this.ExposeMineables();
		}

		public float Get(Thing thing)
		{
			var sum = 0f;
			if (contaminations.TryGetValue(thing.thingIDNumber, out var contamination))
				sum += contamination;
			if (thing is IThingHolder holder)
			{
				var innerThings = ThingOwnerUtility.GetAllThingsRecursively(holder, false);
				foreach(var innerThing in innerThings)
					if (contaminations.TryGetValue(innerThing.thingIDNumber, out contamination))
						sum += contamination;
			}
			return sum;
		}

		public float ChangeDirectly(Thing thing, float amount)
		{
			if (amount == 0)
				return 0;
			var id = thing.thingIDNumber;
			if (contaminations.TryGetValue(id, out var contamination) == false)
				contamination = 0;
			if (-amount > contamination)
				amount = -contamination;
			contamination += amount;
			if (contamination <= 0)
				contaminations.Remove(id);
			else if (contamination > 0)
				contaminations[id] = contamination;
			return amount;
		}

		public void Add(Thing thing, float amount)
		{
			ChangeDirectly(thing, amount);
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
				return -ChangeDirectly(thing, -amount);
			var hasMain = contaminations.ContainsKey(thing.thingIDNumber);
			var removed = 0f;
			var innerThings = ThingOwnerUtility.GetAllThingsRecursively(holder, false);
			var subAmount = amount / (innerThings.Count + (hasMain ? 1 : 0));
			foreach (var innerThing in innerThings)
				removed -= ChangeDirectly(innerThing, -subAmount);
			if (hasMain)
				removed -= ChangeDirectly(thing, -subAmount);
			return removed;
		}

		public void Remove(Thing thing)
		{
			contaminations.Remove(thing.thingIDNumber);
			if (thing is IThingHolder holder)
			{
				var innerThings = ThingOwnerUtility.GetAllThingsRecursively(holder, false);
				foreach (var innerThing in innerThings)
					contaminations.Remove(innerThing.thingIDNumber);
			}
		}

		public float GroundTransfer(Thing thing, float factor)
		{
			var map = thing.Map;
			if (map == null)
				return 0;
			
			var grid = map.GetContamination();
			var cell = thing.Position;
			var pawnContamination = Get(thing);
			var groundContamination = grid[cell];
			var transfer = 0f;
			if (pawnContamination != groundContamination)
			{
				var midPoint = (pawnContamination + groundContamination) / 2;
				transfer = ChangeDirectly(thing, (midPoint - pawnContamination) * factor);
				grid[cell] -= transfer;
				if (transfer > 0)
					Log.Message($"ground transfer {cell} -{transfer}-> {thing}");
				if (transfer < 0)
					Log.Message($"ground transfer {thing} -{-transfer}-> {cell}");
			}
			return transfer;
		}
	}

	public class ContaminationGrid : ICellBoolGiver
	{
		static readonly Color color = new(0, 0.8f, 0);

		public float[] cells;
		public CellBoolDrawer drawer;
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
			mapSizeX = map.Size.x;
		}

		public ContaminationGrid(Map map)
		{
			cells = new float[map.Size.x * map.Size.z];
			AddMap(map);
		}

		public Color Color => Color.white;
		public bool GetCellBool(int index) => cells[index] > 0;
		public Color GetCellExtraColor(int index) => color.ToTransparent(cells[index]);

		public float this[IntVec3 cell]
		{
			get => cells[cell.z * mapSizeX + cell.x];
			set
			{
				cells[cell.z * mapSizeX + cell.x] = value >= 0 ? value : 0;
				drawer.SetDirty();
			}
		}
	}

	public static class ContaminationExtension
	{
		public static float GetContamination(this Thing thing) => ContaminationManager.Instance.Get(thing);
		public static ContaminationGrid GetContamination(this Map map) => ContaminationManager.Instance.grounds[map.Index];
		public static float[] GetContaminationCells(this Map map) => ContaminationManager.Instance.grounds[map.Index].cells;
		public static CellBoolDrawer GetContaminationDrawer(this Map map) => ContaminationManager.Instance.grounds[map.Index].drawer;
		public static float GroundTransfer(this Thing thing, float factor) => ContaminationManager.Instance.GroundTransfer(thing, factor);
		public static void AddContamination(this Thing thing, float val, float factor = 1f)
		{
			Log.Message($"add {thing} {val} [{factor}x]");
			ContaminationManager.Instance.Add(thing, val * factor);
		}
		public static float SubtractContamination(this Thing thing, float val)
		{
			Log.Message($"subtract {thing} {val}");
			return ContaminationManager.Instance.Subtract(thing, val);
		}
		public static void ClearContamination(this Thing thing)
		{
			Log.Message($"clear {thing}");
			ContaminationManager.Instance.Remove(thing);
		}

		private static void Transfer(this ContaminationManager contamination, Thing from, float factor, Thing[] toArray)
		{
			var value = contamination.Get(from);
			var subtracted = contamination.Subtract(from, value * factor);
			if (subtracted == 0)
				return;
			else
				Log.Message($"thing transfer: {from} looses {subtracted}");
			var n = toArray.Length;
			if (n == 0)
				return;
			var delta = subtracted / n;
			for (var j = 0; j < n; j++)
			{
				contamination.Add(toArray[j], delta);
				Log.Message($"thing transfer: {toArray[j]} gains {delta}");
			}
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
		}
	}
}