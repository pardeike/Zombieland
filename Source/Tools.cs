using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	class Measure
	{
		readonly Stopwatch sw;
		string text;
		long prevTime;
		int counter;

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

		public void End(bool cancel = false)
		{
			sw.Stop();
			if (cancel == false) Checkpoint();
		}
	}

	public enum FacingIndex
	{
		Front,
		Side,
		Back
	}

	[StaticConstructorOnStartup]
	static class Tools
	{
		public static ZombieGenerator generator = new ZombieGenerator();
		public static ZombieAvoider avoider = new ZombieAvoider();
		public static ZombieWanderer wanderer = new ZombieWanderer();
		public static Texture2D MenuIcon;
		public static Texture2D ZombieButtonBackground;
		public static string zlNamespace = typeof(Tools).Namespace;

		static string mealLabel;
		static string mealDescription;
		static Graphic mealGraphic;
		public static void EnableTwinkie(bool enable)
		{
			var def = ThingDefOf.MealSurvivalPack;
			var cachedGraphic = Traverse.Create(def.graphicData).Field("cachedGraphic");

			if (mealLabel == null) mealLabel = def.label;
			if (mealDescription == null) mealDescription = def.description;
			if (mealGraphic == null) mealGraphic = cachedGraphic.GetValue<Graphic>();

			if (enable)
			{
				def.label = "Twinkie";
				def.description = "A Twinkie is an American snack cake, marketed as a \"Golden Sponge Cake with Creamy Filling\".";
				cachedGraphic.SetValue(GraphicsDatabase.twinkieGraphic);
			}
			else
			{
				def.label = mealLabel;
				def.description = mealDescription;
				cachedGraphic.SetValue(mealGraphic);
			}

			def.graphic = def.graphicData.Graphic;
			GenLabel.ClearCache();

			var game = Current.Game;
			if (game != null)
			{
				game.Maps.SelectMany(map => map.listerThings.ThingsOfDef(def))
					.Do(meal => { Traverse.Create(meal).Field("graphicInt").SetValue(null); });
			}
		}

		public static string GetModRootDirectory()
		{
			var me = LoadedModManager.GetMod<ZombielandMod>();
			if (me == null)
			{
				Log.Error("LoadedModManager.GetMod<ZombielandMod>() failed");
				return "";
			}
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

		static Dictionary<Map, PheromoneGrid> gridCache = new Dictionary<Map, PheromoneGrid>();
		public static PheromoneGrid GetGrid(this Map map)
		{
			if (gridCache.TryGetValue(map, out var grid))
				return grid;

			grid = map.GetComponent<PheromoneGrid>();
			if (grid == null)
			{
				grid = new PheromoneGrid(map);
				map.components.Add(grid);
			}
			gridCache[map] = grid;
			return grid;
		}

		public static void ColorBlend(ref float original, float color)
		{
			original = original + color - 1f;
			if (original < 0f) original = 0f;
			if (original > 1f) original = 1f;
		}

		public static void DebugPosition(Vector3 pos, Color color)
		{
			pos.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1);
			var material = SolidColorMaterials.SimpleSolidColorMaterial(color);
			DrawScaledMesh(MeshPool.plane10, material, pos + new Vector3(0.5f, 0f, 0.5f), Quaternion.identity, 1.0f, 1.0f);
		}

		public static void DrawScaledMesh(Mesh mesh, Material mat, Vector3 pos, Quaternion q, float mx, float my, float mz = 1f)
		{
			var s = new Vector3(mx, mz, my);
			var matrix = new Matrix4x4();
			matrix.SetTRS(pos, q, s);
			Graphics.DrawMesh(mesh, matrix, mat, 0);
		}

		public static Func<T, R> GetFieldAccessor<T, R>(string fieldName)
		{
			var param = Expression.Parameter(typeof(T), "arg");
			var member = Expression.Field(param, fieldName);
			var lambda = Expression.Lambda(typeof(Func<T, R>), member, param);
			return (Func<T, R>)lambda.Compile();
		}

		public static T Boxed<T>(T val, T min, T max) where T : IComparable
		{
			if (val.CompareTo(min) < 0) return min;
			if (val.CompareTo(max) > 0) return max;
			return val;
		}

		static System.Random rng = new System.Random();
		public static void Shuffle<T>(this IList<T> list)
		{
			var n = list.Count;
			while (n > 1)
			{
				n--;
				var k = rng.Next(n + 1);
				var value = list[k];
				list[k] = list[n];
				list[n] = value;
			}
		}

		public static float RadiusForPawn(Pawn pawn)
		{
			var radius = pawn.RaceProps.Animal ? Constants.ANIMAL_PHEROMONE_RADIUS : Constants.HUMAN_PHEROMONE_RADIUS;
			return radius * ZombieSettings.Values.zombieInstinct.HalfToDoubleValue();
		}

		public static float ZombieAvoidRadius(Zombie zombie, bool squared = false)
		{
			if (zombie.wasMapPawnBefore)
				return squared ? 100f : 10f;
			if (zombie.raging > 0)
				return squared ? 36f : 6f;
			switch (zombie.state)
			{
				case ZombieState.Wandering:
					return squared ? 16f : 4f;
				case ZombieState.Tracking:
					return squared ? 36f : 6f;
				default:
					return squared ? 4f : 2f;
			}
		}

		public static void ConvertToZombie(ThingWithComps thing, bool force = false)
		{
			var pawn = thing is Corpse corpse ? corpse.InnerPawn : thing as Pawn;
			if (pawn == null || pawn.RaceProps.Humanlike == false)
				return;

			// clear zombie hediffs to avoid triggering this convert method again
			//
			if (force == false && (pawn.health == null || pawn.health.hediffSet.hediffs.Any(hediff => hediff.def.IsZombieHediff()) == false))
				return;
			pawn.health?.hediffSet.hediffs.RemoveAll(hediff => hediff.def.IsZombieHediff());

			var pos = thing is IThingHolder ? ThingOwnerUtility.GetRootPosition(thing as IThingHolder) : thing.Position;
			var map = thing is IThingHolder ? ThingOwnerUtility.GetRootMap(thing as IThingHolder) : thing.Map;
			var rot = pawn.Rotation;
			var wasInGround = thing.Map == null;

			if (map == null && thing.Destroyed == false)
			{
				thing.Destroy();
				return;
			}

			var zombie = ZombieGenerator.GeneratePawn(ZombieGenerator.ZombieType.Normal);

			zombie.Name = pawn.Name;
			zombie.gender = pawn.gender;

			zombie.ageTracker.AgeBiologicalTicks = pawn.ageTracker.AgeBiologicalTicks;
			zombie.ageTracker.AgeChronologicalTicks = pawn.ageTracker.AgeChronologicalTicks;
			zombie.ageTracker.BirthAbsTicks = pawn.ageTracker.BirthAbsTicks;

			zombie.story.childhood = pawn.story.childhood;
			zombie.story.adulthood = pawn.story.adulthood;
			zombie.story.melanin = pawn.story.melanin;
			zombie.story.crownType = pawn.story.crownType;
			zombie.story.hairDef = pawn.story.hairDef;
			zombie.story.bodyType = pawn.story.bodyType;

			var zTweener = Traverse.Create(zombie.Drawer.tweener);
			var pTweener = Traverse.Create(pawn.Drawer.tweener);
			zTweener.Field("tweenedPos").SetValue(pTweener.Field("tweenedPos").GetValue());
			zTweener.Field("lastDrawFrame").SetValue(pTweener.Field("lastDrawFrame").GetValue());
			zTweener.Field("lastTickSpringPos").SetValue(pTweener.Field("lastTickSpringPos").GetValue());

			ZombieGenerator.AssignNewCustomGraphics(zombie);
			ZombieGenerator.FinalizeZombieGeneration(zombie);
			GenPlace.TryPlaceThing(zombie, pos, map, ThingPlaceMode.Direct, null);

			zombie.Rotation = rot;
			if (wasInGround == false)
			{
				zombie.rubbleCounter = Constants.RUBBLE_AMOUNT;
				zombie.state = ZombieState.Wandering;
			}
			zombie.wasMapPawnBefore = true;

			zombie.apparel.DestroyAll();
			pawn.apparel.WornApparelInDrawOrder.ToList().ForEach(apparel =>
			{
				if (pawn.apparel.TryDrop(apparel, out var newApparel))
				{
					zombie.apparel.Wear(newApparel);
					newApparel.SetForbidden(false, false);
					newApparel.HitPoints = 1;
					var compQuality = newApparel.TryGetComp<CompQuality>();
					if (compQuality != null)
						compQuality.SetQuality(QualityCategory.Shoddy, ArtGenerationContext.Colony);

					zombie.apparel.Notify_ApparelAdded(newApparel);
				}
			});

			if (thing is Corpse)
			{
				if (thing.Destroyed == false)
					thing.Destroy();
			}
			else
			{
				pawn.Kill(null);
				if (pawn.Corpse != null && pawn.Corpse.Destroyed == false)
					pawn.Corpse.Destroy();
			}

			var label = "BecameAZombieLabel".Translate();
			var text = "BecameAZombieDesc".Translate(new object[] { pawn.NameStringShort });
			Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.ThreatBig, zombie);
		}

		// implement
		public static bool DoesRepellZombies(this Def def)
		{
			return def.defName.StartsWith("ZL_REPELL", StringComparison.Ordinal);
		}

		// implement
		public static bool DoesAttractZombies(this Def def)
		{
			return def.defName.StartsWith("ZL_ATTRACT", StringComparison.Ordinal);
		}

		// implement
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
			// For now, we disable this to gain execution speed
			//if (terrain.DoesRepellZombies()) return false;
			return true;
		}

		// this is called very often so we optimize it a bit
		public static bool HasValidDestination(this Pawn pawn, IntVec3 dest)
		{
			var map = pawn.Map;
			var size = map.info.Size;
			if (dest.x < 0 || dest.x >= size.x || dest.z < 0 || dest.z >= size.z) return false;
			if (map.edificeGrid[dest] is Building_Door door && door.Open == false) return false;
			var idx = map.cellIndices.CellToIndex(dest);
			if (map.pathGrid.pathGrid[idx] >= 10000) return false;
			return true;
			// For now, we disable this to gain execution speed
			//return map.terrainGrid.topGrid[idx].DoesRepellZombies() == false;
		}

		public static bool IsZombieHediff(this HediffDef hediff)
		{
			if (hediff == null) return false;
			if (hediff.GetType().Namespace == zlNamespace) return true;
			if (hediff.hediffClass.Namespace == zlNamespace) return true;
			return false;
		}

		public static bool HasInfectionState(Pawn pawn, InfectionState state)
		{
			if (pawn.RaceProps.Humanlike == false) return false;

			return pawn.health.hediffSet
						.GetHediffs<Hediff_Injury_ZombieBite>()
						.SelectMany(hediff => hediff.comps)
						.OfType<HediffComp_Zombie_TendDuration>()
						.Any(tendDuration => tendDuration.GetInfectionState() == state);
		}

		public static bool HasInfectionState(Pawn pawn, InfectionState minState, InfectionState maxState)
		{
			return pawn.health.hediffSet
						.GetHediffs<Hediff_Injury_ZombieBite>()
						.SelectMany(hediff => hediff.comps)
						.OfType<HediffComp_Zombie_TendDuration>()
						.Any(tendDuration => tendDuration.InfectionStateBetween(minState, maxState));
		}

		public static int CapableColonists(Map map)
		{
			var colonists = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer);
			return colonists.Count(pawn =>
			{
				if (pawn.Spawned == false || pawn.Downed || pawn.Dead) return false;
				if (pawn.health.HasHediffsNeedingTend(true)) return false;
				if (pawn.equipment.Primary == null) return false;
				if (pawn.InMentalState) return false;
				if (pawn.InContainerEnclosed) return false;

				var walkCapacity = PawnCapacityUtility.CalculateCapacityLevel(pawn.health.hediffSet, PawnCapacityDefOf.Moving);
				if (walkCapacity < 0.25f) return false;

				return true;
			});
		}

		public static bool IsHostileToZombies(Pawn pawn)
		{
			if (pawn.RaceProps.Animal)
				return ZombieSettings.Values.animalsAttackZombies;

			if (pawn.Faction.HostileTo(Faction.OfPlayer))
				return ZombieSettings.Values.enemiesAttackZombies;

			return false;
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
			var baseTimestamp = grid.GetTimestamp(nextMove);
			if (baseTimestamp > 0)
				for (var i = 0; i < 9; i++)
				{
					var pos = basePos + GenAdj.AdjacentCellsAndInside[i];
					if (pos.x != nextMove.x || pos.z != nextMove.z && pos.InBounds(map))
					{
						var distance = Mathf.Abs(nextMove.x - pos.x) + Mathf.Abs(nextMove.z - pos.z);
						var timestamp = baseTimestamp - distance * Constants.ZOMBIE_CLOGGING_FACTOR * 2;
						grid.BumpTimestamp(pos, timestamp);
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
			return string.Format("{0:0.0}h", Mathf.Floor(10f * t / GenDate.TicksPerHour) / 10f);
		}

		static int combatExtendedIsInstalled;
		public static bool IsCombatExtendedInstalled()
		{
			if (combatExtendedIsInstalled == 0)
				combatExtendedIsInstalled = (AccessTools.TypeByName("CombatExtended.Controller") != null) ? 1 : 2;
			return combatExtendedIsInstalled == 1;
		}

		public static int ColonyPoints()
		{
			var colonists = Find.VisibleMap.mapPawns.FreeColonists;
			ColonyEvaluation.GetColonistArmouryPoints(colonists, Find.VisibleMap, out var colonistPoints, out var armouryPoints);
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
			map.GetComponent<TickManager>()?.AllZombies().Do(action);
		}

		public static Texture2D GetMenuIcon()
		{
			if (MenuIcon == null)
				MenuIcon = GraphicsDatabase.GetTexture("PatreonIcon");
			return MenuIcon;
		}

		public static Texture2D GetZombieButtonBackground()
		{
			if (ZombieButtonBackground == null)
				ZombieButtonBackground = GraphicsDatabase.GetTexture("ZombieButtonBackground");
			return ZombieButtonBackground;
		}

		public static Material[][] GetDamageableGraphics(string name, int variantCount, int maxCount)
		{
			var mats = new Material[variantCount][];
			for (var v = 0; v < variantCount; v++)
			{
				mats[v] = new Material[maxCount + 1];
				var variant = Enum.GetName(typeof(FacingIndex), v).ToLower();
				for (var i = 0; i <= maxCount; i++)
					mats[v][i] = MaterialPool.MatFrom(name + "/" + name + i + "_" + variant, ShaderDatabase.Cutout);
			}
			return mats;
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

		public static void Look<T>(ref T[] list, string label, params object[] ctorArgs) where T : IExposable
		{
			if (Scribe.EnterNode(label) == false) return;

			try
			{
				if (Scribe.mode == LoadSaveMode.Saving)
				{
					if (list == null)
						Scribe.saver.WriteAttribute("IsNull", "True");
					else
					{
						foreach (var current in list)
						{
							var t2 = current;
							Scribe_Deep.Look<T>(ref t2, false, "li", ctorArgs);
						}
					}
				}
				else if (Scribe.mode == LoadSaveMode.LoadingVars)
				{
					var curXmlParent = Scribe.loader.curXmlParent;
					var xmlAttribute = curXmlParent.Attributes["IsNull"];
					if (xmlAttribute != null && xmlAttribute.Value.ToLower() == "true")
						list = null;
					else
					{
						list = new T[curXmlParent.ChildNodes.Count];
						var i = 0;
						foreach (var subNode2 in curXmlParent.ChildNodes)
							list[i++] = ScribeExtractor.SaveableFromNode<T>((XmlNode)subNode2, ctorArgs);
					}
				}
			}
			finally
			{
				Scribe.ExitNode();
			}
		}

		public static List<CodeInstruction> NotZombieInstructions(ILGenerator generator, MethodBase method)
		{
			var skipReplacement = generator.DefineLabel();
			return new List<CodeInstruction>
			{
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Ldfld, method.DeclaringType.Field("pawn")),
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
				throw new ArgumentException("Cannot find parameter named " + parameterName, nameof(parameterName));

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
								var fInfo = method.DeclaringType.Field(name);
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
			var m_get_Downed = typeof(Pawn).PropertyGetter(nameof(Pawn.Downed));
			var m_replacement = SymbolExtensions.GetMethodInfo(() => DownedReplacement(null));

			var found = false;
			foreach (var instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Callvirt && instruction.operand == m_get_Downed)
				{
					skip--;
					if (skip < 0)
					{
						instruction.opcode = OpCodes.Call;
						instruction.operand = m_replacement;
						found = true;
					}
				}
				yield return instruction;
			}

			if (!found) Log.Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);
		}
	}
}