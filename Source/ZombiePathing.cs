using HarmonyLib;
using RimWorld;
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
		public Dictionary<Region, int> backpointingRegionsIndices = new();
		public List<BackpointingRegion> backpointingRegions = new();

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

			bool InvalidRegion(Region region)
			{
				if (region == null || region.valid == false || (region.type & RegionType.Set_Passable) == 0)
					return true;
				return finalRegionIndices.ContainsKey(region);
			}

			void Add(Region region, int parentIdx)
			{
				var aRegion = new BackpointingRegion(region, parentIdx);
				finalRegions.Add(aRegion);
				finalRegionIndices[aRegion.region] = finalRegions.Count - 1;
			}

			map.regionGrid.allRooms
				.Where(r => r.IsDoorway == false && r.Fogged == false && r.IsHuge == false && r.ProperRoom)
				.SelectMany(r => r.Regions)
				.Where(r => r != null && r.valid)
				.Distinct()
				.Do(region => Add(region, -1));

			if (finalRegions.Any() == false)
				return;

			var allDoors = map.listerThings
				.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
				.OfType<Building_Door>()
				.Where(door => door.Fogged() == false)
				.ToList();

			var n = 0;
			while (n < finalRegions.Count)
			{
				var region = finalRegions[n++].region;
				var idx = n - 1;
				if (region == null || region.valid == false)
					continue;

				foreach (var subRegion in region.Neighbors.InRandomOrder())
				{
					if (InvalidRegion(subRegion))
						continue;
					Add(subRegion, idx);
				}

				// blocked on the inside, we find any doors that are diagnoally reachable from any of our region cells
				foreach (var cell in region.Cells)
				{
					var adjacentDoor = allDoors.FirstOrDefault(door =>
					{
						var doorPos = door.Position;
						return (doorPos.x == cell.x + 1 || doorPos.x == cell.x - 1) &&
							(doorPos.z == cell.z + 1 || doorPos.z == cell.z - 1);
					});
					if (adjacentDoor == null)
						continue;
					var doorRegion = map.regionGrid.GetRegionAt_NoRebuild_InvalidAllowed(adjacentDoor.Position);
					if (InvalidRegion(doorRegion))
						continue;
					Add(doorRegion, idx);
				}

				// blocked outside, lets skip the block
				var door = region.door;
				if (door != null)
				{
					var doorCell = door.Position;
					GenAdj.CardinalDirections.Do(v =>
					{
						var block = doorCell + v;
						if (block.InBounds(map) == false || block.GetEdifice(map) == null)
							return;
						var beyond = block + v;
						if (block.InBounds(map) == false)
							return;
						var doorRegion = map.regionGrid.GetRegionAt_NoRebuild_InvalidAllowed(beyond);
						if (InvalidRegion(doorRegion))
							return;
						Add(doorRegion, idx);
					});
				}
			}

			backpointingRegionsIndices = finalRegionIndices;
			backpointingRegions = finalRegions;
		}
	}
}