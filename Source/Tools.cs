using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	class Tools
	{
		public static long Ticks()
		{
			return 1000L * GenTicks.TicksAbs;
		}

		public static Predicate<IntVec3> ZombieSpawnLocator(Map map)
		{
			return cell =>
			{
				if (GenGrid.Walkable(cell, map) == false) return false;
				if (map.thingGrid.ThingsListAt(cell).Exists(thing => thing.def.BlockPlanting)) return false;
				return true;
			};
		}

		public static float ColonyPoints()
		{
			IEnumerable<Pawn> colonists = Find.VisibleMap.mapPawns.FreeColonists;
			ReadinessUtil.GetColonistArmouryPoints(colonists, null, out float colonistPoints, out float armouryPoints);
			return colonistPoints + armouryPoints;
		}

		public static void ReApplyThingToListerThings(IntVec3 cell, Thing thing)
		{
			if ((((cell != IntVec3.Invalid) && (thing != null)) && (thing.Map != null)) && thing.Spawned)
			{
				Map map = thing.Map;
				RegionGrid regionGrid = map.regionGrid;
				Region validRegionAt = null;
				if (cell.InBounds(map))
				{
					validRegionAt = regionGrid.GetValidRegionAt(cell);
				}
				if ((validRegionAt != null) && !validRegionAt.ListerThings.Contains(thing))
				{
					validRegionAt.ListerThings.Add(thing);
				}
			}
		}

		public static void DrawScaledMesh(Mesh mesh, Material mat, Vector3 pos, Quaternion q, float mx, float my, float mz = 1f)
		{
			Vector3 s = new Vector3(mx, mz, my);
			Matrix4x4 matrix = new Matrix4x4();
			matrix.SetTRS(pos, q, s);
			Graphics.DrawMesh(mesh, matrix, mat, 0);
		}

		public static Dictionary<float, HashSet<IntVec3>> circles = null;
		public static IEnumerable<IntVec3> GetCircle(float radius)
		{
			if (circles == null) circles = new Dictionary<float, HashSet<IntVec3>>();
			HashSet<IntVec3> cells = circles.ContainsKey(radius) ? circles[radius] : null;
			if (cells == null)
			{
				cells = new HashSet<IntVec3>();
				IEnumerator<IntVec3> enumerator = GenRadial.RadialPatternInRadius(radius).GetEnumerator();
				while (enumerator.MoveNext())
				{
					IntVec3 v = enumerator.Current;
					cells.Add(v);
					cells.Add(new IntVec3(-v.x, 0, v.z));
					cells.Add(new IntVec3(-v.x, 0, -v.z));
					cells.Add(new IntVec3(v.x, 0, -v.z));
				}
				enumerator.Dispose();
				circles[radius] = cells;
			}
			return cells;
		}
	}
}