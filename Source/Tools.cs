using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	class Measure
	{
		Stopwatch sw;
		String text;
		long prevTime = 0;
		int counter = 0;

		public Measure(String text)
		{
			this.text = text;
			sw = new Stopwatch();
			sw.Start();
		}

		public void Checkpoint()
		{
			counter++;
			var ms = sw.ElapsedMilliseconds;
			var delta = prevTime == 0 ? 0 : (ms - prevTime);
			Log.Warning("#" + counter + " " + text + " = " + ms + " ms (+" + delta + ")");
			prevTime = ms;
		}

		public void End()
		{
			sw.Stop();
			Checkpoint();
		}
	}

	class Tools
	{
		public static long Ticks()
		{
			return 1000L * GenTicks.TicksAbs;
		}

		public static bool IsValidSpawnLocation(TargetInfo target)
		{
			return IsValidSpawnLocation(target.Cell, target.Map);
		}

		public static bool IsValidSpawnLocation(IntVec3 cell, Map map)
		{
			if (GenGrid.Walkable(cell, map) == false) return false;
			var terrain = map.terrainGrid.TerrainAt(cell);
			if (terrain != TerrainDefOf.Soil && terrain != TerrainDefOf.Sand) return false;
			// if (cell.SupportsStructureType(map, TerrainAffordance.Diggable) == false) return false;
			return true;
		}

		public static Predicate<IntVec3> ZombieSpawnLocator(Map map)
		{
			return cell => IsValidSpawnLocation(cell, map);
		}

		public static int ColonyPoints()
		{
			if (Main.DEBUG_COLONY_POINTS > 0) return Main.DEBUG_COLONY_POINTS;

			IEnumerable<Pawn> colonists = Find.VisibleMap.mapPawns.FreeColonists;
			ColonyEvaluation.GetColonistArmouryPoints(colonists, null, out float colonistPoints, out float armouryPoints);
			return (int)(colonistPoints + armouryPoints);
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