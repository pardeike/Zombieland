using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public class BackpointingRegion
	{
		public readonly Region region;
		public readonly int parentIdx;
		public readonly IntVec3 cell;

		public BackpointingRegion(Region region, int parentIdx)
		{
			this.region = region;
			this.parentIdx = parentIdx;
			cell = RandomStandingCell();
		}

		public static readonly TraverseParms traverseParams = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly, true, false, true);
		IntVec3 RandomStandingCell()
		{
			if (region.Cells.Any() == false) return IntVec3.Invalid;
			var center = region.extentsClose.CenterCell;
			return region.Cells.OrderBy(c => center.DistanceToSquared(c)).First();
		}
	}

	public class ZombiePathing
	{
		public bool running = true;
		readonly Map map;
		public Dictionary<Region, int> backpointingRegionsIndices = new Dictionary<Region, int>();
		public List<BackpointingRegion> backpointingRegions = new List<BackpointingRegion>();

		public ZombiePathing(Map map)
		{
			this.map = map;
		}

		public IntVec3 GetWanderDestination(IntVec3 cell)
		{
			var region = map.regionGrid.GetRegionAt_NoRebuild_InvalidAllowed(cell);
			if (region == null)
				return IntVec3.Invalid;
			if (backpointingRegionsIndices.TryGetValue(region, out var idx) == false)
				return IntVec3.Invalid;
			idx = backpointingRegions[idx].parentIdx;
			if (idx == -1)
				return IntVec3.Invalid;
			return backpointingRegions[idx].cell;
		}

		public void UpdateRegions()
		{
			var finalRegionIndices = new Dictionary<Region, int>();
			var finalRegions = new List<BackpointingRegion>();

			void Add(Region region, int parentIdx)
			{
				var aRegion = new BackpointingRegion(region, parentIdx);
				finalRegions.Add(aRegion);
				finalRegionIndices[aRegion.region] = finalRegions.Count - 1;
			}

			Region IncrementAndNext(ref int n) => finalRegions[n++].region;

			map.regionGrid.allRooms
				.Where(r => r.IsDoorway == false && r.Fogged == false && r.IsHuge == false && r.ProperRoom)
				.SelectMany(r => r.Regions)
				.Where(r => r != null && r.valid)
				.Distinct()
				.Do(region => Add(region, -1));

			if (finalRegions.Any() == false)
				return;

			var n = 0;
			while (n < finalRegions.Count)
			{
				var region = IncrementAndNext(ref n);
				var idx = n - 1;
				if (region == null || region.valid == false)
					continue;

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
						if (finalRegionIndices.ContainsKey(subRegion))
							continue;
						Add(subRegion, idx);
					}
				}
			}

			backpointingRegionsIndices = finalRegionIndices;
			backpointingRegions = finalRegions;
		}
	}
}
