using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	/*class Measure
	{
		Stopwatch sw;
		String text;
		long prevTime = 0;
		int counter = 0;

		public Measure(string text)
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
	}*/

	[StaticConstructorOnStartup]
	static class Tools
	{
		public static ZombieGenerator generator = new ZombieGenerator();

		public static string GetModRootDirectory()
		{
			var me = LoadedModManager.GetMod<ZombielandMod>();
			return me.Content.RootDir;
		}

		public static long Ticks()
		{
			return 1000L * GenTicks.TicksAbs;
		}

		public static int PheromoneFadeoff()
		{
			return (int)(Constants.PHEROMONE_FADEOFF.SecondsToTicks() * ZombieSettings.Values.zombieInstinct.HalfToDoubleValue()) * 1000;
		}

		static Dictionary<int, PheromoneGrid> gridCache = new Dictionary<int, PheromoneGrid>();
		public static PheromoneGrid GetGrid(this Map map)
		{
			PheromoneGrid grid;
			if (gridCache.TryGetValue(map.uniqueID, out grid))
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

		public static void ColorBlend(ref float original, float color)
		{
			original = original + color - 1f;
			if (original < 0f) original = 0f;
			if (original > 1f) original = 1f;
		}

		public static T Boxed<T>(T val, T min, T max) where T : IComparable
		{
			if (val.CompareTo(min) < 0) return min;
			if (val.CompareTo(max) > 0) return max;
			return val;
		}

		public static float RadiusForPawn(Pawn pawn)
		{
			var radius = pawn.RaceProps.Animal ? Constants.ANIMAL_PHEROMONE_RADIUS : Constants.HUMAN_PHEROMONE_RADIUS;
			return radius * ZombieSettings.Values.zombieInstinct.HalfToDoubleValue();
		}

		public static bool DoesRepellZombies(this Def def)
		{
			return def.defName.StartsWith("ZL_REPELL", StringComparison.Ordinal);
		}

		// TODO: implement
		public static bool DoesAttractZombies(this Def def)
		{
			return def.defName.StartsWith("ZL_ATTRACT", StringComparison.Ordinal);
		}

		// TODO: implement
		public static bool DoesKillZombies(this Def def)
		{
			return def.defName.StartsWith("ZL_KILL", StringComparison.Ordinal);
		}

		public static bool IsValidSpawnLocation(TargetInfo target)
		{
			return IsValidSpawnLocation(target.Cell, target.Map);
		}

		public static bool IsValidSpawnLocation(IntVec3 cell, Map map)
		{
			if (cell.Walkable(map) == false) return false;
			var terrain = map.terrainGrid.TerrainAt(cell);
			if (terrain != TerrainDefOf.Soil && terrain != TerrainDefOf.Sand && terrain != TerrainDefOf.Gravel) return false;
			if (terrain.DoesRepellZombies()) return false;
			return true;
		}

		public static bool HasValidDestination(this Pawn pawn, IntVec3 dest)
		{
			if (dest.InBounds(pawn.Map) == false) return false;
			var door = dest.GetEdifice(pawn.Map) as Building_Door;
			if (door != null)
			{
				if (door.Open == false)
					return false;
			}
			if (pawn.Map.pathGrid.WalkableFast(dest) == false) return false;
			return pawn.Map.terrainGrid.TerrainAt(dest).DoesRepellZombies() == false;
		}

		public static bool HasInfectionState(Pawn pawn, InfectionState state)
		{
			if (pawn.IsColonist == false) return false;

			return pawn.health.hediffSet
						.GetHediffs<Hediff_Injury_ZombieBite>()
						.SelectMany(hediff => hediff.comps)
						.OfType<HediffComp_Zombie_TendDuration>()
						.Cast<HediffComp_Zombie_TendDuration>()
						.Any(tendDuration => tendDuration.GetInfectionState() == state);
		}

		public static bool HasInfectionState(Pawn pawn, InfectionState minState, InfectionState maxState)
		{
			return pawn.health.hediffSet
						.GetHediffs<Hediff_Injury_ZombieBite>()
						.SelectMany(hediff => hediff.comps)
						.OfType<HediffComp_Zombie_TendDuration>()
						.Cast<HediffComp_Zombie_TendDuration>()
						.Any(tendDuration => tendDuration.InfectionStateBetween(minState, maxState));
		}

		public static Predicate<IntVec3> ZombieSpawnLocator(Map map, bool isEvent = false)
		{
			if (isEvent || ZombieSettings.Values.spawnWhenType == SpawnWhenType.AllTheTime
				|| ZombieSettings.Values.spawnWhenType == SpawnWhenType.InEventsOnly)
			{
				return cell => IsValidSpawnLocation(cell, map)
					&& map.reachability.CanReachColony(cell);
			}

			if (ZombieSettings.Values.spawnWhenType == SpawnWhenType.WhenDark)
			{
				return cell => IsValidSpawnLocation(cell, map)
					&& map.glowGrid.PsychGlowAt(cell) == PsychGlow.Dark
					&& map.reachability.CanReachColony(cell);
			}

			Log.Error("Unsupported spawn mode " + ZombieSettings.Values.spawnWhenType);
			return null;
		}

		public static IntVec3 CenterOfInterest(Map map)
		{
			int x = 0, z = 0, n = 0;
			var buildingMultiplier = 3;
			if (map.listerBuildings != null && map.listerBuildings.allBuildingsColonist != null)
			{
				map.listerBuildings.allBuildingsColonist.Do(building =>
				{
					x += building.Position.x * buildingMultiplier;
					z += building.Position.z * buildingMultiplier;
					n += buildingMultiplier;
				});
			}
			if (map.mapPawns != null)
			{
				map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Do(pawn =>
				{
					x += pawn.Position.x;
					z += pawn.Position.z;
					n++;
				});
			}
			return n == 0 ? map.Center : new IntVec3(x / n, 0, z / n);
		}

		public static void ChainReact(Map map, IntVec3 basePos, IntVec3 nextMove)
		{
			var grid = map.GetGrid();
			var baseTimestamp = grid.Get(nextMove, false).timestamp;
			for (var i = 0; i < 9; i++)
			{
				var pos = basePos + GenAdj.AdjacentCellsAndInside[i];
				if (pos.x != nextMove.x || pos.z != nextMove.z && pos.InBounds(map))
				{
					var distance = Math.Abs(nextMove.x - pos.x) + Math.Abs(nextMove.z - pos.z);
					var timestamp = baseTimestamp - distance * Constants.ZOMBIE_CLOGGING_FACTOR * 2;
					grid.SetTimestamp(pos, timestamp);
				}
			}
		}

		public static void AutoExposeDataWithDefaults<T>(this T settings) where T : new()
		{
			var defaults = new T();
			AccessTools.GetFieldNames(settings).Do(name =>
			{
				var finfo = AccessTools.Field(settings.GetType(), name);
				var value = finfo.GetValue(settings);
				var type = value.GetType();
				var defaultValue = Traverse.Create(defaults).Field(name).GetValue();
				var m_Look = AccessTools.Method(typeof(Scribe_Values), "Look", null, new Type[] { type });
				var arguments = new object[] { value, name, defaultValue, false };
				m_Look.Invoke(null, arguments);
				finfo.SetValue(settings, arguments[0]);
			});
		}

		public static string ToHourString(this int ticks, bool relativeToAbsoluteGameTime = true)
		{
			var t = relativeToAbsoluteGameTime ? ticks - GenTicks.TicksAbs : ticks;
			return string.Format("{0:0.0}h", Math.Floor(10f * t / GenDate.TicksPerHour) / 10f);
		}

		static int combatExtendedIsInstalled = 0;
		public static bool IsCombatExtendedInstalled()
		{
			if (combatExtendedIsInstalled == 0)
				combatExtendedIsInstalled = (AccessTools.TypeByName("CombatExtended.Controller") != null) ? 1 : 2;
			return combatExtendedIsInstalled == 1;
		}

		public static int ColonyPoints()
		{
			if (Constants.DEBUG_COLONY_POINTS > 0) return Constants.DEBUG_COLONY_POINTS;

			var colonists = Find.VisibleMap.mapPawns.FreeColonists;
			float colonistPoints;
			float armouryPoints;
			ColonyEvaluation.GetColonistArmouryPoints(colonists, Find.VisibleMap, out colonistPoints, out armouryPoints);
			return (int)(colonistPoints + armouryPoints);
		}

		public static void ReApplyThingToListerThings(IntVec3 cell, Thing thing)
		{
			if ((((cell != IntVec3.Invalid) && (thing != null)) && (thing.Map != null)) && thing.Spawned)
			{
				var map = thing.Map;
				var regionGrid = map.regionGrid;
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

		public static void DoWithAllZombies(Map map, Action<Zombie> action)
		{
			map.GetComponent<TickManager>().AllZombies().Do(action);
		}

		public static void CastThoughtBubble(Pawn pawn, Material material)
		{
			var def = ThingDefOf.Mote_Speech;
			var newThing = (MoteBubble)ThingMaker.MakeThing(def, null);
			newThing.iconMat = material;
			newThing.Attach(pawn);
			GenSpawn.Spawn(newThing, pawn.Position, pawn.Map);
		}

		static readonly float[] halfToDouble = { 0.5f, 1.0f, 2.0f };
		public static float HalfToDoubleValue(this ZombieInstinct e)
		{
			return halfToDouble[(int)e];
		}

		public static Dictionary<float, HashSet<IntVec3>> circles;
		public static IEnumerable<IntVec3> GetCircle(float radius)
		{
			if (circles == null) circles = new Dictionary<float, HashSet<IntVec3>>();
			var cells = circles.ContainsKey(radius) ? circles[radius] : null;
			if (cells == null)
			{
				cells = new HashSet<IntVec3>();
				var enumerator = GenRadial.RadialPatternInRadius(radius).GetEnumerator();
				while (enumerator.MoveNext())
				{
					var v = enumerator.Current;
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

		public static string TranslateHoursToText(float hours)
		{
			var ticks = (int)(GenDate.TicksPerHour * hours);
			return ticks.ToStringTicksToPeriod(true, true, false);
		}

		public static string TranslateHoursToText(int hours)
		{
			var ticks = GenDate.TicksPerHour * hours;
			return ticks.ToStringTicksToPeriod(true, true, false);
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

		public static List<CodeInstruction> NotZombieInstructions(ILGenerator generator, MethodBase method, string parameterName)
		{
			var parameterIndex = -1;
			var pinfo = method.GetParameters();
			for (var i = 0; i < pinfo.Length; i++)
				if (pinfo[i].Name == parameterName)
				{
					parameterIndex = i;
					break;
				}
			if (parameterIndex == -1)
				throw new ArgumentException("Cannot find parameter named " + parameterName, "parameterName");

			var skipReplacement = generator.DefineLabel();
			return new List<CodeInstruction>
			{
				new CodeInstruction(OpCodes.Ldarg, parameterIndex),
				new CodeInstruction(OpCodes.Isinst, typeof(Zombie)),
				new CodeInstruction(OpCodes.Brfalse, skipReplacement),
			};
		}

		public delegate IEnumerable<CodeInstruction> MyTranspiler(ILGenerator generator, IEnumerable<CodeInstruction> instructions);
		public static MyTranspiler GenerateReplacementCallTranspiler(List<CodeInstruction> condition, MethodBase method = null, MethodInfo replacement = null)
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

				if (method != null && replacement != null)
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

				var idx = instructions.Count;
				instructions.AddRange(instr);
				instructions[idx].labels = instructions[idx].labels ?? new List<Label>();
				instructions[idx].labels.AddRange(labels);

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

		static bool DownedReplacement(Pawn pawn)
		{
			if (pawn is Zombie) return false;
			return pawn.Downed;
		}

		public static IEnumerable<CodeInstruction> DownedReplacer(IEnumerable<CodeInstruction> instructions, int skip = 0)
		{
			var m_get_Downed = AccessTools.Method(typeof(Pawn), "get_Downed");
			var m_replacement = AccessTools.Method(typeof(Tools), "DownedReplacement");

			foreach (var instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Callvirt && instruction.operand == m_get_Downed)
				{
					skip--;
					if (skip < 0)
					{
						instruction.opcode = OpCodes.Call;
						instruction.operand = m_replacement;
					}
				}
				yield return instruction;
			}
		}
	}
}