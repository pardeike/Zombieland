using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public class BackpointingRegion
	{
		public readonly Region region;
		public readonly int parentIdx;
		public bool isEnd;
		public readonly IntVec3 cell;

		public BackpointingRegion(Region region, int parentIdx, bool isEnd)
		{
			this.region = region;
			this.parentIdx = parentIdx;
			this.isEnd = isEnd;
			cell = RandomStandingCell();
		}

		IntVec3 RandomStandingCell()
		{
			if (region.TryFindRandomCellInRegion(c => c.Standable(region.Map), out var cell))
				return cell;
			return IntVec3.Invalid;
		}
	}

	public class ZombiePathing
	{
		public bool running = true;
		readonly Map map;
		private Dictionary<Region, int> backpointingRegionsIndices = new Dictionary<Region, int>();
		private List<BackpointingRegion> backpointingRegions = new List<BackpointingRegion>();

		public ZombiePathing(Map map) { this.map = map; }

		public IntVec3 GetWanderDestination(IntVec3 cell)
		{
			var region = map.regionGrid.GetRegionAt_NoRebuild_InvalidAllowed(cell);
			if (backpointingRegionsIndices.TryGetValue(region, out var idx) == false)
				return IntVec3.Invalid;
			if (idx < 0 || idx >= backpointingRegions.Count)
				return IntVec3.Invalid;
			idx = backpointingRegions[idx].parentIdx;
			return backpointingRegions[idx].cell;
		}

		public void UpdateRegions()
		{
			var finalRegionIndices = new Dictionary<Region, int>();
			var finalRegions = new List<BackpointingRegion>();
			var visited = new HashSet<Region>();

			void Add(Region region, int parentIdx, bool isEnd)
			{
				var aRegion = new BackpointingRegion(region, parentIdx, isEnd);
				if (aRegion.cell.IsValid)
				{
					finalRegions.Add(aRegion);
					finalRegionIndices[aRegion.region] = finalRegions.Count - 1;
				}
				_ = visited.Add(region);
			}

			Region IncrementAndNext(ref int n) => finalRegions[n++].region;

			map.regionGrid.allRooms
				.Where(r => r.IsDoorway == false && r.Fogged == false && r.IsHuge == false && r.UsesOutdoorTemperature == false)
				.SelectMany(r => r.Regions)
				.Where(r => r != null && r.valid)
				.Distinct()
				.Do(region => Add(region, -1, false));

			if (finalRegions.Any() == false)
				return;

			var n = 0;
			while (n < finalRegions.Count)
			{
				var region = IncrementAndNext(ref n);
				var idx = n - 1;
				if (region == null || region.valid == false)
					continue;

				var found = false;
				var subRegions = region.Neighbors;
				if (subRegions.Any())
				{
					foreach (var subRegion in subRegions.InRandomOrder())
					{
						if (subRegion == null)
							continue;
						if (subRegion.valid == false)
							continue;
						if ((subRegion.type & RegionType.Set_Passable) == 0)
							continue;
						if (visited.Contains(subRegion))
							continue;
						Add(subRegion, idx, true);
						found = true;
					}
					if (found)
						finalRegions[idx].isEnd = false;
				}
			}

			backpointingRegionsIndices = finalRegionIndices;
			backpointingRegions = finalRegions;
		}

		public IEnumerator Process()
		{
			var nextRefresh = 0;
			while (running)
			{
				yield return null;

				var nowTicks = GenTicks.TicksAbs;
				if (nowTicks > nextRefresh)
				{
					nextRefresh = nowTicks + GenTicks.TicksPerRealSecond * 30 / Find.Maps.Count;
					UpdateRegions();
				}

				/*
				var endRegions = backpointingRegions.Where(r => r.parentIdx != -1 && r.isEnd);
				if (endRegions.Any() == false)
					continue;

				var tuple = endRegions.RandomElement();
				var endCell = tuple.cell;
				while (tuple.parentIdx >= 0)
				{
					var p1 = tuple.cell;
					tuple = backpointingRegions[tuple.parentIdx];
					var p2 = tuple.cell;
					var d = (p2 - p1).LengthHorizontal;
					var x = new IntRange(p1.x, p2.x);
					var z = new IntRange(p1.z, p2.z);
					for (var f = 0f; f < d; f += 2f)
					{
						var pos = new IntVec3(x.Lerped(f / d), 0, z.Lerped(f / d));
						var grid = map.GetGrid();
						var now = Tools.Ticks();
						Tools.GetCircle(8)
							.Do(vec => grid.BumpTimestamp(pos + vec, now - (long)(2f * vec.LengthHorizontal)));
						yield return null;
					}
				}
				*/
			}
		}
	}
}
