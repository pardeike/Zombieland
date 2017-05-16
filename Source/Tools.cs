using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
			//Log.Warning("#" + counter + " " + text + " = " + ms + " ms (+" + delta + ")");
			prevTime = ms;
		}

		public void End()
		{
			sw.Stop();
			Checkpoint();
		}
	}

	static class Tools
	{
		public static long Ticks()
		{
			return 1000L * GenTicks.TicksAbs;
		}

		static Dictionary<int, PheromoneGrid> gridCache = new Dictionary<int, PheromoneGrid>();
		public static PheromoneGrid GetGrid(this Map map)
		{
			if (gridCache.TryGetValue(map.uniqueID, out PheromoneGrid grid))
				return grid;

			grid = map.GetComponent<PheromoneGrid>();
			if (grid == null)
			{
				grid = new PheromoneGrid(map);
				map.components.Add(grid);
			}
			gridCache[map.uniqueID] = grid;
			return grid;
		}

		public static Texture2D WriteableCopy(this Texture2D original, Color color)
		{
			var texture = new Texture2D(original.width, original.height, TextureFormat.ARGB32, true) { wrapMode = TextureWrapMode.Clamp };
			var pixels = original.GetPixels();
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i].r *= color.r;
				pixels[i].g *= color.g;
				pixels[i].b *= color.b;
			}
			texture.SetPixels(pixels);
			return texture;
		}

		public static Rect MinimumFrame(this Texture2D texture)
		{
			Func<Color, bool> alphaCheck = c => c.a != 0f;

			var w = texture.width;
			var h = texture.height;
			var x1 = 0;
			var y1 = 0;
			var x2 = w;
			var y2 = h;

			for (int x = 0; x < w; x++)
			{
				if (texture.GetPixels(x, 0, 1, h).Any(alphaCheck))
				{
					x1 = Math.Max(x1, x - 1);
					break;
				}
			}
			for (int x = w - 1; x >= x1; x--)
			{
				if (texture.GetPixels(x, 0, 1, h).Any(alphaCheck))
				{
					x2 = Math.Min(x2, x + 1);
					break;
				}
			}
			for (int y = 0; y < h; y++)
			{
				if (texture.GetPixels(0, y, w, 1).Any(alphaCheck))
				{
					y1 = Math.Max(y1, y - 1);
					break;
				}
			}
			for (int y = h - 1; y >= y1; y--)
			{
				if (texture.GetPixels(0, y, w, 1).Any(alphaCheck))
				{
					y2 = Math.Min(y2, y + 1);
					break;
				}
			}

			return new Rect(x1, y1, x2 - x1, y2 - y1);
		}

		public static void ApplyStains(this Texture2D texture, Texture2D part, bool flipH, bool flipV, float px = -1f, float py = -1f)
		{
			var rect = texture.MinimumFrame();
			var w = part.width;
			var h = part.height;
			var x = (int)(rect.x + (rect.width - w) * (px != -1f ? px : Rand.Value));
			var y = (int)(rect.y + (rect.height - h) * (py != -1f ? py : Rand.Value));
			var oPixels = texture.GetPixels(x, y, w, h);
			var pPixels = part.GetPixels();
			for (int i = 0; i < w; i++)
				for (int j = 0; j < h; j++)
				{
					var pIdx = (flipH ? (w - i - 1) : i) + (flipV ? (h - j - 1) : j) * w;
					var oIdx = i + j * w;

					var oa = oPixels[oIdx].a;
					var a = pPixels[pIdx].a * oa;
					if (oa * (oPixels[oIdx].r + oPixels[oIdx].g + oPixels[oIdx].b) > 0.05f)
					{
						oPixels[oIdx].r = oPixels[oIdx].r * (1 - a) + pPixels[pIdx].r * a;
						oPixels[oIdx].g = oPixels[oIdx].g * (1 - a) + pPixels[pIdx].g * a;
						oPixels[oIdx].b = oPixels[oIdx].b * (1 - a) + pPixels[pIdx].b * a;
					}
				}
			texture.SetPixels(x, y, w, h, oPixels);
		}

		public static Color Color(this ColorHSV hsvColor)
		{
			return ColorHSV.ToColor(hsvColor);
		}

		public static Color RandomSkinColor()
		{
			var hueDelta = Rand.Range(-0.15f, 0.15f);
			var satDelta = Rand.Range(-0.2f, 0.2f);
			var britDelta = Rand.Range(-0.2f, 0.1f);
			var hsvColor = Constants.ZOMBIE_SKIN_COLOR + new ColorHSV(hueDelta, satDelta, britDelta, 0);
			hsvColor.Normalize();
			return hsvColor.Color();
		}

		public static T Boxed<T>(T val, T min, T max) where T : IComparable
		{
			if (val.CompareTo(min) < 0) return min;
			if (val.CompareTo(max) > 0) return max;
			return val;
		}

		public static float RadiusForPawn(Pawn pawn)
		{
			return pawn.RaceProps.Animal ? Constants.ANIMAL_PHEROMONE_RADIUS : Constants.HUMAN_PHEROMONE_RADIUS;
		}

		public static bool IsValidSpawnLocation(TargetInfo target)
		{
			return IsValidSpawnLocation(target.Cell, target.Map);
		}

		public static bool IsValidSpawnLocation(IntVec3 cell, Map map)
		{
			if (GenGrid.Walkable(cell, map) == false) return false;
			var terrain = map.terrainGrid.TerrainAt(cell);
			if (terrain != TerrainDefOf.Soil && terrain != TerrainDefOf.Sand && terrain != TerrainDefOf.Gravel) return false;
			return true;
		}

		public static bool HasValidDestination(this Pawn pawn, IntVec3 dest)
		{
			if (dest.InBounds(pawn.Map) == false) return false;
			if (dest.GetEdifice(pawn.Map) is Building_Door) return false;
			return (pawn.Map.pathGrid.WalkableFast(dest));
		}

		public static Predicate<IntVec3> ZombieSpawnLocator(Map map)
		{
			return cell => IsValidSpawnLocation(cell, map);
		}

		public static void ChainReact(PheromoneGrid grid, Map map, IntVec3 basePos, IntVec3 nextMove)
		{
			var baseTimestamp = grid.Get(nextMove, false).timestamp;
			for (int i = 0; i < 9; i++)
			{
				var pos = basePos + GenAdj.AdjacentCellsAndInside[i];
				if (pos.x != nextMove.x || pos.z != nextMove.z)
				{
					var distance = Math.Abs(nextMove.x - pos.x) + Math.Abs(nextMove.z - pos.z);
					var timestamp = baseTimestamp - distance * Constants.ZOMBIE_CLOGGING_FACTOR * 2;
					grid.SetTimestamp(pos, timestamp);
				}
			}
		}

		public static int ColonyPoints()
		{
			if (Constants.DEBUG_COLONY_POINTS > 0) return Constants.DEBUG_COLONY_POINTS;

			IEnumerable<Pawn> colonists = Find.VisibleMap.mapPawns.FreeColonists;
			ColonyEvaluation.GetColonistArmouryPoints(colonists, Find.VisibleMap, out float colonistPoints, out float armouryPoints);
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

		public static Texture2D LoadPNG(string filePath)
		{
			Texture2D textured;
			if (File.Exists(filePath) == false) return null;

			byte[] data = File.ReadAllBytes(filePath);
			textured = new Texture2D(2, 2);
			textured.LoadImage(data);
			textured.Compress(true);
			textured.name = Path.GetFileNameWithoutExtension(filePath);
			return textured;
		}

		public static void CastThoughtBubble(Pawn pawn, Material material)
		{
			var def = ThingDefOf.Mote_Speech;
			var newThing = (MoteBubble)ThingMaker.MakeThing(def, null);
			newThing.iconMat = material;
			newThing.Attach(pawn);
			GenSpawn.Spawn(newThing, pawn.Position, pawn.Map);
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

		public static List<CodeInstruction> NotZombieInstructions(ILGenerator generator, MethodBase method)
		{
			var skipReplacement = generator.DefineLabel();
			return new List<CodeInstruction>
			{
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(method.DeclaringType, "pawn")),
				new CodeInstruction(OpCodes.Isinst, typeof(Zombie)),
				new CodeInstruction(OpCodes.Brfalse, skipReplacement),
			};
		}

		public delegate IEnumerable<CodeInstruction> MyTranspiler(ILGenerator generator, IEnumerable<CodeInstruction> instructions);
		public static MyTranspiler GenerateReplacementCallTranspiler(List<CodeInstruction> condition, MethodBase method, MethodInfo replacement = null)
		{
			return (ILGenerator generator, IEnumerable<CodeInstruction> instr) =>
			{
				var labels = new List<Label>();
				foreach (var cond in condition)
				{
					if (cond.operand is Label)
						labels.Add((Label)cond.operand);
				}

				var instructions = new List<CodeInstruction>();
				instructions.AddRange(condition);

				if (replacement != null)
				{
					var parameterNames = method.GetParameters().Select(info => info.Name).ToList();
					replacement.GetParameters().Do(info =>
					{
						var name = info.Name;
						var ptype = info.ParameterType;

						if (name == "__instance")
							instructions.Add(new CodeInstruction(OpCodes.Ldarg_0)); // instance
						else
						{
							var index = parameterNames.IndexOf(name);
							if (index >= 0)
								instructions.Add(new CodeInstruction(OpCodes.Ldarg, index + 1)); // parameters
							else
							{
								var field = name.Substring(2, name.Length - 4);
								var fInfo = AccessTools.Field(method.DeclaringType, name);
								instructions.Add(new CodeInstruction(OpCodes.Ldarg_0));
								instructions.Add(new CodeInstruction(OpCodes.Ldflda, fInfo)); // extra fields
							}
						}
					});
				}

				if (replacement != null)
					instructions.Add(new CodeInstruction(OpCodes.Call, replacement));
				instructions.Add(new CodeInstruction(OpCodes.Ret));

				instructions.Add(new CodeInstruction(OpCodes.Nop) { labels = labels });
				instructions.AddRange(instr);

				return instructions.AsEnumerable();
			};

			/*
			 (A)
			 L_0000: ldarg.0 
			 L_0001: ldfld class ZombieLand.FOO ZombieLand.AAA::pawn
			 L_0006: isinst ZombieLand.ZZZ
			 L_000b: brfalse.s L_001a
			 (B)
			 L_000d: ldarg.0 
			 L_000e: ldarg.0 
			 L_000f: ldflda class ZombieLand.FOO ZombieLand.AAA::pawn
			 L_0014: call void ZombieLand.AAAPatch::TestPatched(class ZombieLand.AAA, class ZombieLand.FOO&)
			 L_0019: ret
			 (C)
			 L_001f: nop
			 (D)
			 .......
			*/
		}
	}
}
