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

			void Iterate()
			{
				var n = 0;
				while (n < finalRegions.Count)
				{
					var region = IncrementAndNext(ref n);
					var idx = n - 1;
					if (region == null || region.valid == false)
						continue;

					var subRegions = region.Neighbors.ToArray();
					var subRegionCount = subRegions.Length;
					if (subRegionCount > 0)
					{
						if (region.IsDoorway && subRegionCount == 1)
						{
							var door = region.door;
							var doorCell = door.Position;
							var cells = door.InteractionCells;
							if (cells.Count == 1)
							{
								var interactionCell = cells[0];
								var blockedCell = doorCell + doorCell - interactionCell;
								if (blockedCell.InBounds(map) && blockedCell.GetEdifice(map) != null)
								{
									GenAdj.CardinalDirections.Select(c => c + blockedCell)
										.Where(c => c != doorCell)
										.Select(c => map.regionGrid.GetRegionAt_NoRebuild_InvalidAllowed(c))
										.OfType<Region>().Where(r => r.valid).Distinct()
										.DoIf(r => finalRegionIndices.ContainsKey(r) == false, r => Add(r, idx));
								}
							}
						}
						else
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
				}
			}

			Iterate();

			var knownRegions = finalRegions.Select(r => r.region).ToHashSet();
			map.regionGrid.allRooms
				.Where(r => r.IsDoorway && r.Fogged == false)
				.SelectMany(r => r.Regions)
				.Where(r => r != null && r.valid)
				.Except(finalRegions.Select(r => r.region))
				.Select(r => r.door)
				.Do(door =>
				{
					var doorCell = door.Position;
					var cells = door.InteractionCells;
					if (cells.Count == 1)
					{
						var interactionCell = cells[0];
						var blockedCell = doorCell + doorCell - interactionCell;
						if (blockedCell.InBounds(map) && blockedCell.GetEdifice(map) != null)
						{
							var knownRegion = GenAdj.CardinalDirections.Select(c => c + blockedCell)
								.Where(c => c != doorCell)
								.Select(c => map.regionGrid.GetRegionAt_NoRebuild_InvalidAllowed(c))
								.OfType<Region>().Where(r => r.valid).Distinct()
								.FirstOrDefault(r => knownRegions.Contains(r));
							if (knownRegion != null)
							{
								var idx = finalRegions.FirstIndexOf(r => r.region == knownRegion);
								if (idx != -1)
								{
									var subRegion = map.regionGrid.GetRegionAt_NoRebuild_InvalidAllowed(interactionCell);
									if (subRegion != null && subRegion.valid && knownRegions.Contains(subRegion) == false)
										Add(subRegion, idx);
								}
							}
						}
					}
				});

			Iterate();

			backpointingRegionsIndices = finalRegionIndices;
			backpointingRegions = finalRegions;
		}
	}
}
