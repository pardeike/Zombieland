using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using static HarmonyLib.AccessTools;

namespace ZombieLand
{
	public enum FacingIndex
	{
		South,
		East,
		North,
		West
	}

	public class IsCombatExtendedInstalled : PatchOperation
	{
		public override bool ApplyWorker(XmlDocument xml)
		{
			return TypeByName("CombatExtended.ToolCE") != null;
		}
	}

	public class FloatRef
	{
		public Func<float> getter;
		public Action<float> setter;

		public float Value
		{
			get => getter();
			set => setter(value);
		}

		public FloatRef(Func<float> getter, Action<float> setter)
		{
			this.getter = getter;
			this.setter = setter;
		}
	}

	[StaticConstructorOnStartup]
	static class Tools
	{
		public static ZombieAvoider avoider = new();
		public static Texture2D MenuIcon;
		public static Texture2D ZombieButtonBackground;
		public static string zlNamespace = typeof(Tools).Namespace;

		public static List<Region> cachedPlayerReachableRegions = new();
		public static int nextPlayerReachableRegionsUpdate = 0;

		public static HashSet<BiomeDef> biomeBlacklist = new();

		static string mealLabel;
		static string mealDescription;
		static Graphic mealGraphic;
		public static void EnableTwinkie(bool enable)
		{
			var def = ThingDefOf.MealSurvivalPack;

			mealLabel ??= def.label;
			mealDescription ??= def.description;
			mealGraphic ??= def.graphicData.cachedGraphic;

			if (enable)
			{
				def.label = "Twinkie";
				def.description = "A Twinkie is an American snack cake, marketed as a \"Golden Sponge Cake with Creamy Filling\".";
				def.graphicData.cachedGraphic = GraphicsDatabase.twinkieGraphic;
			}
			else
			{
				def.label = mealLabel;
				def.description = mealDescription;
				def.graphicData.cachedGraphic = mealGraphic;
			}

			def.graphic = def.graphicData.Graphic;
			GenLabel.ClearCache();

			var game = Current.Game;
			game?.Maps
					.SelectMany(map => map.listerThings.ThingsOfDef(def))
					.Do(meal => meal.graphicInt = null);
		}

		public static bool IsBroken(this Thing t)
		{
			var compBreakable = t.TryGetComp<CompBreakable>();
			return compBreakable != null && compBreakable.broken;
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

		static readonly Dictionary<string, float> nextExecutions = new();
		public static bool RunThrottled(this string key, float cooldown)
		{
			var timeNow = Time.realtimeSinceStartup;
			if (nextExecutions.TryGetValue(key, out var nextExecution) && timeNow < nextExecution)
				return false;
			nextExecutions[key] = timeNow + cooldown;
			return true;
		}

		public static Texture2D LoadTexture(string path, bool makeReadonly = true)
		{
			var fullPath = Path.Combine(GetModRootDirectory(), "Textures", $"{path}.png");
			var data = File.ReadAllBytes(fullPath);
			if (data == null || data.Length == 0)
				throw new Exception($"Cannot read texture {fullPath}");
			var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
			if (tex.LoadImage(data) == false)
				throw new Exception($"Cannot create texture {fullPath}");
			tex.Compress(true);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Trilinear;
			tex.Apply(true, makeReadonly);
			return tex;
		}

		public static string SafeTranslate(this string key)
		{
			if (key == null)
				return "";
			return key.Translate();
		}

		public static string SafeTranslate(this string key, params object[] args)
		{
			if (key == null)
				return "";
			var namedArgs = args.Select(arg => new NamedArgument(arg, "")).ToArray();
			return key.Translate(namedArgs);
		}

		public static long Ticks()
		{
			return 1000L * GenTicks.TicksAbs;
		}

		public static float Difficulty() => ZombieSettings.Values.threatScale; // Find.Storyteller.difficulty.threatScale;

		public static int PheromoneFadeoff()
		{
			return (int)(Constants.PHEROMONE_FADEOFF.SecondsToTicks() * ZombieSettings.Values.zombieInstinct.HalfToDoubleValue()) * 1000;
		}

		static readonly Dictionary<Map, PheromoneGrid> gridCache = new();
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
			if (original < 0f)
				original = 0f;
			if (original > 1f)
				original = 1f;
		}

		public static void DebugPosition(Vector3 pos, Color color)
		{
			pos.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1);
			var material = SolidColorMaterials.SimpleSolidColorMaterial(color);
			GraphicToolbox.DrawScaledMesh(MeshPool.plane10, material, pos + new Vector3(0.5f, 0f, 0.5f), Quaternion.identity, 1.0f, 1.0f);
		}

		public static T Boxed<T>(T val, T min, T max) where T : IComparable
		{
			if (val.CompareTo(min) < 0)
				return min;
			if (val.CompareTo(max) > 0)
				return max;
			return val;
		}

		public static void Shuffle<T>(this IList<T> list)
		{
			var n = list.Count;
			while (n > 1)
			{
				n--;
				var k = Constants.random.Next(n + 1);
				(list[n], list[k]) = (list[k], list[n]);
			}
		}

		public static T SafeRandomElement<T>(this IEnumerable<T> source)
		{
			if (source.Count() == 0)
				return default;
			return source.RandomElement();
		}

		public static float RadiusForPawn(Pawn pawn)
		{
			var radius = pawn.RaceProps.Animal ? Constants.ANIMAL_PHEROMONE_RADIUS : Constants.HUMAN_PHEROMONE_RADIUS;
			return radius * ZombieSettings.Values.zombieInstinct.HalfToDoubleValue();
		}

		public static bool ActivePartOfColony(this Pawn pawn)
		{
			if (pawn == null)
				return false;
			return (pawn.Faction?.IsPlayer ?? false) && pawn.jobs != null;
		}

		static readonly HashSet<Type> valuableThings = new()
		{
			typeof(Building_Art),
			typeof(Building_Bed),
			typeof(Building_Battery),
			typeof(Building_CommsConsole),
			typeof(Building_Cooler),
			typeof(Building_Heater),
			typeof(Building_MechCharger),
			typeof(Building_MechGestator),
			typeof(Building_NutrientPasteDispenser),
			typeof(Building_OrbitalTradeBeacon),
			typeof(Building_PodLauncher),
			typeof(Building_PowerSwitch),
			typeof(Building_ResearchBench),
			typeof(Building_Storage),
			typeof(Building_StylingStation),
			typeof(Building_SunLamp),
			typeof(Building_TempControl),
			typeof(Building_Vent),
			typeof(Building_WorkTable)
		};
		public static IEnumerable<Room> ValuableRooms(Map map)
		{
			var rooms = map.regionGrid.allRooms.Where(r => r.IsDoorway == false && r.Fogged == false && r.IsHuge == false && r.UsesOutdoorTemperature == false && r.ProperRoom);
			var home = map.areaManager.Home;
			foreach (var room in rooms)
			{
				foreach (var thing in room.ContainedAndAdjacentThings)
					if (valuableThings.Contains(thing.GetType()))
						if (home.TrueCount == 0 || home[thing.Position])
						{
							yield return room;
							break;
						}
			}
		}

		public static bool ShouldAvoidZombies(Pawn pawn = null)
		{
			if (pawn == null)
				return ZombieSettings.Values.betterZombieAvoidance;

			if (pawn is Zombie || pawn.RaceProps.Humanlike == false)
				return false;

			if (pawn.InfectionState() == InfectionState.Infecting)
				return false;

			if (pawn.IsColonist == false)
			{
				if (pawn.HostileTo(Faction.OfPlayer) == false)
				{
					var map = pawn.Map;
					if (map.areaManager?.Home[pawn.Position] ?? false)
						return false;
					var room = GridsUtility.GetRoom(pawn.Position, map);
					if (room.IsHuge == false)
						return false;
				}

				return ZombieSettings.Values.betterZombieAvoidance;
			}

			if (ZombieSettings.Values.betterZombieAvoidance == false)
				return false;
			return ColonistSettings.Values.ConfigFor(pawn)?.autoAvoidZombies ?? false;
		}

		public static bool CanHarmElectricZombies(this Verb verb)
		{
			if (verb == null)
				return false;
			if (verb.IsMeleeAttack)
				return true;
			if (verb.IsEMP())
				return true;
			var def = verb.GetDamageDef();
			if (def == null)
				return false;
			return def.isRanged == false;
		}

		public static float ZombieAvoidRadius(Zombie zombie, bool squared = false)
		{
			if (zombie.IsActiveElectric || zombie.isAlbino)
				return 0f;
			if (zombie.wasMapPawnBefore)
				return squared ? 64f : 8f;
			if (zombie.raging > 0)
				return squared ? 36f : 6f;
			return zombie.state switch
			{
				ZombieState.Wandering => squared ? 16f : 4f,
				ZombieState.Tracking => squared ? 36f : 6f,
				_ => squared ? 4f : 2f,
			};
		}

		public static void SpawnZombiesInRoom(Map map, IntVec3 c)
		{
			if (map.Parent.def == SoSTools.sosShipOrbitingWorldObjectDef)
				return;

			var room = GridsUtility.GetRoom(c, map);
			if (room == null || room.IsHuge || room.TouchesMapEdge || room.Fogged == false)
				return;

			var cellCount = room.CellCount;
			var maxCount = (int)GenMath.LerpDoubleClamped(0, 5, 200, 800, Difficulty());
			if (cellCount < 10 || cellCount > maxCount)
				return;

			var pawns = room.Regions.SelectMany(region => region.ListerThings.ThingsInGroup(ThingRequestGroup.Pawn));
			if (pawns.Any())
				return;

			if (Rand.Chance(ZombieSettings.Values.infectedRaidsChance) == false)
				return;

			var cells = room.Cells.InRandomOrder().Take(cellCount / 10);
			foreach (var cell in cells)
			{
				var iterator = ZombieGenerator.SpawnZombieIterativ(cell, map, ZombieType.Random, zombie =>
				{
					zombie.rubbleCounter = Constants.RUBBLE_AMOUNT;
					zombie.state = ZombieState.Wandering;
					zombie.Rotation = Rot4.Random;

					var tickManager = Find.CurrentMap.GetComponent<TickManager>();
					_ = tickManager.allZombiesCached.Add(zombie);
				});
				while (iterator.MoveNext())
					;
			}
		}

		public static bool CanDoctor(this Pawn pawn, bool rightNow = false)
		{
			if (pawn.RaceProps.Humanlike == false || pawn.IsPrisoner)
				return false;
			if (rightNow && (pawn.health.Downed || pawn.Awake() == false || pawn.InBed() || pawn.InMentalState))
				return false;
			if (pawn.workSettings == null)
				return false;
			return pawn.workSettings.WorkIsActive(WorkTypeDefOf.Doctor) && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
		}

		public static bool CanHunt(this Pawn pawn, bool rightNow = false)
		{
			if (pawn.RaceProps.Humanlike == false || pawn.IsPrisoner)
				return false;
			if (rightNow && (pawn.health.Downed || pawn.Awake() == false || pawn.InBed() || pawn.InMentalState))
				return false;
			if (pawn.workSettings == null)
				return false;
			return pawn.workSettings.WorkIsActive(WorkTypeDefOf.Hunting) && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
		}

		public static void PlayerReachableRegions_Iterator(HashSet<Region> knownRegions, List<Region> regions)
		{
			var newRegions = regions.Except(knownRegions).ToList();
			if (newRegions.Any())
			{
				knownRegions.AddRange(newRegions);
				var neighbours = newRegions
					.SelectMany(region => region.Neighbors)
					.Where(region => region.valid && region.Room.Fogged == false && region.Room.UsesOutdoorTemperature)
					.ToList();
				PlayerReachableRegions_Iterator(knownRegions, neighbours);
			}
		}

		public static List<Region> PlayerReachableRegions(Map map)
		{
			var ticks = Find.TickManager.TicksGame;
			if (ticks > nextPlayerReachableRegionsUpdate)
			{
				nextPlayerReachableRegionsUpdate = ticks + GenTicks.TickLongInterval;
				var f = Faction.OfPlayer;
				var totalRegions = map.regionGrid.allRooms
					.Where(room => room.IsHuge == false && room.Fogged == false)
					.SelectMany(room => room.Regions)
					.Where(region => region.listerThings.AllThings.Any(thing =>
					{
						if (thing.Faction != f)
							return false;
						var def = thing.def;
						return def.fillPercent >= 0.2f || def.blockWind || def.coversFloor ||
							def.castEdgeShadows || def.holdsRoof || def.blockLight;
					}))
					.ToHashSet();
				if (totalRegions.Count == 0)
				{
					var spot = IntVec3.Zero;
					map.mapPawns.FreeColonists.Do(colonist => spot += colonist.Position);
					var n = map.mapPawns.FreeColonists.Count;
					spot.x /= n;
					spot.z /= n;
					if (spot.InBounds(map))
					{
						var region = map.regionGrid.GetValidRegionAt(spot);
						if (region != null)
							_ = totalRegions.Add(region);
					}
				}
				var neighbours = totalRegions.SelectMany(region => region.Neighbors).ToList();
				PlayerReachableRegions_Iterator(totalRegions, neighbours);
				cachedPlayerReachableRegions = totalRegions.ToList();
			}
			return cachedPlayerReachableRegions;
		}

		public static T RandomElement<T>(this T[] array) => array[Constants.random.Next() % array.Length];

		public static IntVec3 RandomSpawnCell(Map map, bool nearEdge, Predicate<IntVec3> predicate)
		{
			var allRegions = PlayerReachableRegions(map);
			if (nearEdge)
				allRegions = allRegions.Where(region => region.touchesMapEdge).ToList();
			var cell = allRegions
				.SelectMany(region => region.Cells)
				.InRandomOrder()
				.FirstOrFallback(cell => predicate(cell), IntVec3.Invalid);
			if (cell.IsValid == false)
			{
				if (RCellFinder.TryFindRandomPawnEntryCell(out cell, map, 0.1f, true, predicate) == false)
					cell = IntVec3.Invalid;
			}
			return cell;
		}

		public static void QueueConvertToZombie(ThingWithComps thing, Map mapForTickmanager)
		{
			var tickManager = mapForTickmanager.GetComponent<TickManager>();
			tickManager.colonistsToConvert.Enqueue(thing);
		}

		public static void PlayTink(Thing thing)
		{
			if (Constants.USE_SOUND)
			{
				var info = SoundInfo.InMap(thing);
				CustomDefs.TankyTink.PlayOneShot(info);
			}
		}

		public static void PlayAbsorb(Thing thing)
		{
			if (Constants.USE_SOUND)
			{
				var info = SoundInfo.InMap(thing);
				CustomDefs.Bzzt.PlayOneShot(info);
			}
		}

		public static void PlaySmash(Thing thing)
		{
			if (Constants.USE_SOUND)
			{
				var info = SoundInfo.InMap(thing);
				CustomDefs.Smash.PlayOneShot(info);
			}
		}

		public static bool HasHediff<T>(this Pawn pawn) where T : Hediff
		{
			return pawn.health.hediffSet.GetFirstHediff<T>() != null;
		}

		public static List<T> GetHediffsList<T>(this Pawn pawn) where T : Hediff
		{
			var list = new List<T>();
			pawn.health.hediffSet.GetHediffs<T>(ref list);
			return list;
		}

		static readonly NameSingle emptyName = new("");
		public static void ConvertToZombie(ThingWithComps thing, Map map, bool force = false)
		{
			var corpse = thing as Corpse;
			var pawn = corpse != null ? corpse.InnerPawn : thing as Pawn;
			if (pawn?.RaceProps == null
				|| pawn.RaceProps.Humanlike == false
				|| pawn.RaceProps.IsFlesh == false
				|| AlienTools.IsFleshPawn(pawn) == false
				|| SoSTools.IsHologram(pawn))
				return;

			var wasPlayer = pawn.Faction?.IsPlayer ?? false;
			var pawnName = pawn.Name;
			if (force == false && (pawn.health == null || pawnName == emptyName))
				return;
			pawn.Name = emptyName;
			pawn.ideo = null;

			var pos = thing is IThingHolder thingHolder ? ThingOwnerUtility.GetRootPosition(thingHolder) : thing.Position;
			var rot = pawn.Rotation;
			var wasInGround = corpse != null && corpse.ParentHolder != null && corpse.ParentHolder is not Map;

			if (map == null && thing != null && thing.Destroyed == false)
			{
				thing.Destroy();
				return;
			}

			_ = pawn.health.hediffSet.hediffs.RemoveAll(hediff => hediff is Hediff_ZombieInfection);

			var tickManager = map.GetComponent<TickManager>();
			var it = ZombieGenerator.SpawnZombieIterativ(pos, map, ZombieType.Normal, (Zombie zombie) =>
			{
				zombie.Name = pawnName;
				zombie.gender = pawn.gender;
				zombie.ideo = pawn.ideo;

				if (zombie.ageTracker != null && pawn.ageTracker != null)
				{
					zombie.ageTracker.AgeBiologicalTicks = pawn.ageTracker.AgeBiologicalTicks;
					zombie.ageTracker.AgeChronologicalTicks = pawn.ageTracker.AgeChronologicalTicks;
					zombie.ageTracker.BirthAbsTicks = pawn.ageTracker.BirthAbsTicks;
				}

				if (zombie.story != null && pawn.story != null)
				{
					zombie.story.childhood = pawn.story.childhood;
					zombie.story.adulthood = pawn.story.adulthood;
					zombie.story.melanin = pawn.story.melanin;
					//zombie.story.crownType = pawn.story.crownType;
					zombie.story.hairDef = pawn.story.hairDef;
					zombie.story.bodyType = pawn.story.bodyType;
				}

				// redo because we changed stuff
				if (ZombieSettings.Values.useCustomTextures)
					ZombieGenerator.AssignNewGraphics(zombie);

				var zTweener = zombie.Drawer.tweener;
				var pTweener = pawn.Drawer.tweener;
				zTweener.tweenedPos = pTweener.tweenedPos;
				zTweener.lastDrawFrame = pTweener.lastDrawFrame;
				zTweener.lastTickSpringPos = pTweener.lastTickSpringPos;

				zombie.Rotation = rot;
				if (wasInGround == false)
				{
					zombie.rubbleCounter = Constants.RUBBLE_AMOUNT;
					zombie.state = ZombieState.Wandering;
				}
				zombie.wasMapPawnBefore = true;

				if (zombie.apparel != null && pawn.apparel != null)
				{
					zombie.apparel.DestroyAll();
					var wornApparel = pawn.apparel.WornApparel.ToArray();
					foreach (var apparel in wornApparel)
					{
						if (pawn.apparel.TryDrop(apparel, out var newApparel))
						{
							zombie.apparel.Wear(newApparel);
							newApparel.SetForbidden(false, false);
							newApparel.HitPoints = 1;
							var compQuality = newApparel.TryGetComp<CompQuality>();
							compQuality?.SetQuality(QualityCategory.Awful, ArtGenerationContext.Colony);

							zombie.apparel.Notify_ApparelAdded(newApparel);
						}
					}
				}

				if (thing is Corpse)
				{
					if (thing.Destroyed == false)
						thing.Destroy();
				}
				else
				{
					if (pawn.Dead == false)
					{
						var previousProgramState = Current.ProgramState;
						Current.ProgramState = ProgramState.Entry;
						pawn.Kill(null);
						Current.ProgramState = previousProgramState;
						Find.ColonistBar.MarkColonistsDirty();
					}

					if (pawn.Corpse != null && pawn.Corpse.Destroyed == false)
						pawn.Corpse.Destroy();
				}

				_ = tickManager.allZombiesCached.Add(zombie);

				if (map.Biome != SoSTools.sosOuterSpaceBiomeDef)
				{
					if (ZombieSettings.Values.deadBecomesZombieMessage || wasPlayer)
					{
						var label = wasPlayer ? "ColonistBecameAZombieLabel".Translate() : "OtherBecameAZombieLabel".Translate();
						var text = "BecameAZombieDesc".SafeTranslate(new object[] { pawnName.ToStringShort });
						Find.LetterStack.ReceiveLetter(label, text, wasPlayer ? CustomDefs.ColonistTurnedZombie : CustomDefs.OtherTurnedZombie, zombie);
					}
				}
			});
			while (it.MoveNext())
				;
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

		public static bool IsValidSpawnLocation(IntVec3 cell, Map map)
		{
			if (cell.Standable(map) == false || cell.Fogged(map))
				return false;

			//if (map.IsSpace())
			//{
			//	var room = cell.GetRoom(map);
			//	if (room == null || room.OpenRoofCount > 0 || room.TouchesMapEdge)
			//		return false;
			//}

			var edifice = cell.GetEdifice(map);
			if (edifice != null && edifice is Building_Door door)
				if (door.Open == false)
					return false;

			if (ZombieSettings.Values.spawnHowType == SpawnHowType.FromTheEdges)
				return true;

			var terrainGrid = map.terrainGrid;
			if (terrainGrid.CanRemoveTopLayerAt(cell))
				return false;

			var terrain = terrainGrid.TerrainAt(cell);
			var aff = terrain.affordances;

			if (terrain.modContentPack.IsCoreMod == false)
			{
				if (false
					|| aff.Contains(TerrainAffordanceDefOf.Diggable)
					|| aff.Contains(TerrainAffordanceDefOf.GrowSoil)
					)
					return true;
				return false;
			}
			return (false
				|| terrain == TerrainDefOf.Soil
				|| terrain == TerrainDefOf.Sand
				|| terrain == TerrainDefOf.Gravel
			);

			// For now, we disable this to gain execution speed
			//if (terrain.DoesRepellZombies()) return false;
		}

		// this is called very often so we optimize it a bit
		public static bool HasValidDestination(this Pawn pawn, IntVec3 dest)
		{
			var map = pawn.Map;
			var size = map.info.Size;
			if (dest.x < 0 || dest.x >= size.x || dest.z < 0 || dest.z >= size.z)
				return false;
			if (map.edificeGrid[dest] is Building_Door door && door.Open == false)
				return false;
			var idx = map.cellIndices.CellToIndex(dest);
			var pathGrid = map.pathing.For(pawn).pathGrid;
			if (pathGrid.pathGrid[idx] >= 10000)
				return false;
			return true;
			// For now, we disable this to gain execution speed
			//return map.terrainGrid.topGrid[idx].DoesRepellZombies() == false;
		}

		public static bool IsZombieHediff(this HediffDef hediff)
		{
			if (hediff == null)
				return false;
			if (hediff.GetType().Namespace == zlNamespace)
				return true;
			if (hediff.hediffClass.Namespace == zlNamespace)
				return true;
			return false;
		}

		public static void AddZombieInfection(Pawn pawn)
		{
			if (pawn == null || pawn is Zombie || pawn.InfectionState() == InfectionState.Infected)
				return;

			if (pawn.health?.hediffSet == null)
				return;

			var torso = pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined, null, null).FirstOrDefault((BodyPartRecord x) => x.def == BodyPartDefOf.Torso);
			if (torso == null)
				return;
			
			var bite = (Hediff_Injury_ZombieBite)HediffMaker.MakeHediff(HediffDef.Named("ZombieBite"), pawn, torso);
			if (bite == null)
				return;

			bite.mayBecomeZombieWhenDead = true;
			var damageInfo = new DamageInfo(CustomDefs.ZombieBite, 0);
			pawn.health.AddHediff(bite, torso, damageInfo);
			bite.Tended(1, 1);
			bite.TendDuration.ZombieInfector.ForceFinalStage();
		}

		public static (int, int) ColonistsInfo(Map map)
		{
			var colonists = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer);
			var capable = 0;
			var incapable = 0;
			for (var i = 0; i < colonists.Count; i++)
			{
				var pawn = colonists[i];

				if (pawn.Spawned == false || pawn.health.Downed || pawn.Dead)
					continue;
				if (pawn.InMentalState)
					continue;
				if (pawn.InContainerEnclosed)
					continue;

				incapable++;

				if (pawn.equipment.Primary == null)
					continue;
				if (pawn.health.summaryHealth.SummaryHealthPercent <= 0.25f)
					continue;

				var walkCapacity = PawnCapacityUtility.CalculateCapacityLevel(pawn.health.hediffSet, PawnCapacityDefOf.Moving);
				if (walkCapacity < 0.25f)
					continue;

				capable++;
			}
			return (capable, incapable);
		}

		public static bool IsHostileToZombies(Pawn pawn)
		{
			if (pawn.RaceProps.Animal)
				return ZombieSettings.Values.animalsAttackZombies;

			if (pawn.Faction.HostileTo(Faction.OfPlayer))
				return ZombieSettings.Values.enemiesAttackZombies;

			return false;
		}

		public static bool Attackable(Zombie zombie, AttackMode mode, Thing thing)
		{
			if (thing is ZombieCorpse)
				return false;

			if (thing is Pawn target)
			{
				if (target.Dead || target.health.Downed)
					return false;
				if (target.equipment?.Primary is Chainsaw chainsaw && chainsaw.running && zombie.IsActiveElectric == false)
					return Rand.Chance(chainsaw.CounterHitChance());

				var distance = (target.DrawPos - thing.DrawPos).MagnitudeHorizontalSquared();
				if (distance > Constants.MIN_ATTACKDISTANCE_SQUARED)
					return false;

				if (target.InfectionState() == InfectionState.Infecting)
					return false;

				if (mode == AttackMode.Everything)
					return true;

				if (target.MentalState != null)
				{
					var msDef = target.MentalState.def;
					if (msDef == MentalStateDefOf.Manhunter || msDef == MentalStateDefOf.ManhunterPermanent)
						return true;
				}

				if (mode == AttackMode.OnlyHumans
					&& target.RaceProps.Humanlike
					&& target.RaceProps.IsFlesh
					&& AlienTools.IsFleshPawn(target)
					&& SoSTools.IsHologram(target) == false)
					return true;

				if (mode == AttackMode.OnlyColonists && target.IsColonist)
					return true;
			}
			return false;
		}

		public static bool IsDark(this Map map, IntVec3 cell)
		{
			return map.glowGrid.PsychGlowAt(cell) == PsychGlow.Dark;
		}

		public static Predicate<IntVec3> ZombieSpawnLocator(Map map, bool isEvent = false)
		{
			if (isEvent || ZombieSettings.Values.spawnWhenType == SpawnWhenType.AllTheTime || ZombieSettings.Values.spawnWhenType == SpawnWhenType.InEventsOnly)
				return cell => IsValidSpawnLocation(cell, map);

			if (ZombieSettings.Values.spawnWhenType == SpawnWhenType.WhenDark)
				return cell => IsValidSpawnLocation(cell, map) && map.IsDark(cell);

			Log.Error("Unsupported spawn mode " + ZombieSettings.Values.spawnWhenType);
			return null;
		}

		static readonly int[] _cellsAroundIndex = new int[] { 5, 6, 7, 4, -1, 0, 3, 2, 1 };
		public static int CellsAroundIndex(IntVec3 delta)
		{
			var v = Vector3.Normalize(delta.ToVector3()) * Mathf.Sqrt(2);
			var x = (int)Math.Round(v.x);
			var z = (int)Math.Round(v.z);
			if (x == 0 && z == 0)
				return -1;
			var i = 3 * (x + 1) + z + 1;
			return _cellsAroundIndex[i];
		}

		public static void PerformOnAdjacted(this Zombie zombie, Func<Thing, bool> action)
		{
			zombie.Randomize8();

			var map = zombie.Map;
			var size = map.Size;
			var grid = map.thingGrid.thingGrid;
			var basePos = zombie.Position;
			var (left, top, right, bottom) = (basePos.x > 0, basePos.z < size.z - 1, basePos.x < size.x - 1, basePos.z > 0);
			var baseIndex = map.cellIndices.CellToIndex(basePos);
			var rowOffset = size.z;

			bool Evaluate(List<Thing> items)
			{
				for (var i = 0; i < items.Count; i++)
				{
					var item = items[i];
					if (action(item))
						return true;
				}
				return false;
			}

			var actions = new Func<bool>[]
			{
		() => left && Evaluate(grid[baseIndex - 1]),
		() => left && top && Evaluate(grid[baseIndex - 1 + rowOffset]),
		() => left && bottom && Evaluate(grid[baseIndex - 1 - rowOffset]),
		() => top && Evaluate(grid[baseIndex + rowOffset]),
		() => right && Evaluate(grid[baseIndex + 1]),
		() => right && bottom && Evaluate(grid[baseIndex + 1 - rowOffset]),
		() => right && top && Evaluate(grid[baseIndex + 1 + rowOffset]),
		() => bottom && Evaluate(grid[baseIndex - rowOffset])
			};

			for (var i = 0; i < 8; i++)
				if (actions[zombie.adjIndex8[i]]())
					return;
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

		public static string GetHex(byte[] ba)
		{
			var hex = new StringBuilder(ba.Length * 2);
			foreach (byte b in ba)
				_ = hex.AppendFormat("{0:x2}", b);
			return hex.ToString();
		}

		public static byte[] GetBytesFromHex(string hex)
		{
			hex = hex.ToLower();
			var num = hex.Length;
			var bytes = new byte[num / 2];
			for (var i = 0; i < num; i += 2)
				bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
			return bytes;
		}

		public static void AutoExposeDataWithDefaults<T>(this T settings, Func<T, string, object, object, bool> callback = null) where T : new()
		{
			var defaults = new T();
			GetFieldNames(settings).Do(name =>
			{
				var finfo = Field(settings.GetType(), name);
				var value = finfo.GetValue(settings);
				var defaultValue = Traverse.Create(defaults).Field(name).GetValue();
				value ??= defaultValue;
				var type = value.GetType();
				try
				{
					if (callback != null && callback(settings, name, value, defaultValue))
						return;

					MethodInfo m_Look;
					object[] arguments;
					if (type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>)))
					{
						m_Look = FirstMethod(typeof(Scribe_Collections), method =>
								method.Name == "Look" && method.GetParameters().Length >= 2 &&
								method.GetParameters()[0].ParameterType.GetElementType().GetGenericTypeDefinition() == type.GetGenericTypeDefinition() &&
								method.GetParameters()[1].ParameterType != typeof(bool)
							).MakeGenericMethod(type.GenericTypeArguments[0]);
						arguments = new object[] { value, name, LookMode.Value };
						if (type.GetGenericTypeDefinition() == typeof(List<>))
							arguments = arguments.Append(Array.Empty<object>()).ToArray();
					}
					else
					{
						m_Look = Method(typeof(Scribe_Values), "Look", null, new Type[] { type });
						arguments = new object[] { value, name, defaultValue, false };
					}
					_ = m_Look.Invoke(null, arguments);
					finfo.SetValue(settings, arguments[0]);
				}
				catch (Exception ex)
				{
					Log.Error($"Exception while auto exposing Zombieland setting '{name}', mode {Scribe.mode} = {ex}");
					finfo.SetValue(settings, defaultValue);
				}
			});
		}

		public static string SerializeToHex<T>(T obj)
		{
			var ms = new MemoryStream();
			using (var writer = new BsonWriter(ms))
			{
				var serializer = new JsonSerializer();
				serializer.Serialize(writer, obj);
			}
			return GetHex(ms.ToArray());
		}

		public static T DeserializeFromHex<T>(string hex)
		{
			var data = GetBytesFromHex(hex);
			var ms = new MemoryStream(data);
			using var reader = new BsonReader(ms);
			var serializer = new JsonSerializer();
			return serializer.Deserialize<T>(reader);
		}

		public static object Check(this Stopwatch sw, string name)
		{
			sw.Stop();
			var tick = sw.ElapsedTicks * (double)60 / 10000000;
			if (tick > 0.2)
				Log.Warning(name + " " + string.Format("{0:0.00}", tick));
			sw.Reset();
			sw.Start();
			return null;
		}

		public static void Continue(this Stopwatch sw)
		{
			sw.Reset();
			sw.Start();
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
				combatExtendedIsInstalled = (TypeByName("CombatExtended.Controller") != null) ? 1 : 2;
			return combatExtendedIsInstalled == 1;
		}

		static float DPS(IAttackTargetSearcher s)
		{
			var verb = s.CurrentEffectiveVerb;
			if (verb == null)
				return 0f;
			var verbProps = verb?.verbProps;
			var damage = verbProps.defaultProjectile?.projectile.GetDamageAmount(null, null) ?? 0;
			if (damage == 0)
				return 0f;
			var burst = Mathf.Max(1, verbProps.burstShotCount);
			var interval = Mathf.Max(1, verbProps.AdjustedFullCycleTime(verb, null));
			return damage * 60f * burst / interval;
		}

		public static bool InWater(this Pawn pawn)
		{
			var map = pawn?.Map;
			if (map == null)
				return false;
			var index = CellIndicesUtility.CellToIndex(pawn.Position, map.Size.x);
			var terrainDef = map.terrainGrid.TerrainAt(index);
			if (terrainDef == null)
				return false;
			return terrainDef.IsWater;
		}

		public static bool IsWallOrDoor(this IntVec3 cell, Map map)
		{
			var edifice = map.edificeGrid[cell];
			return edifice is Building building && building is not Mineable;
		}

		public static int[] ColonyPoints()
		{
			static float dangerPoints(Building building)
			{
				if (building is Building_TurretGun turretGun)
					return DPS(turretGun) * ((turretGun.powerComp?.PowerOn ?? false) ? 1 : 0.5f);
				if (building is Building_Turret turret)
					return DPS(turret);
				if (building is IAttackTargetSearcher searcher)
					return DPS(searcher);
				if (building.def == ThingDefOf.Wall && building.def.MadeFromStuff)
					return building.def.GetStatValueAbstract(StatDefOf.MaxHitPoints, building.Stuff) / 500;
				return building.HitPoints / 100;
			}

			var map = Find.CurrentMap;
			if (map == null)
				return new int[3];
			var colonists = map.mapPawns.FreeColonists;
			ColonyEvaluation.GetColonistArmouryPoints(colonists, map, out var colonistPoints, out var armouryPoints);
			var turretPoints = map.listerBuildings.allBuildingsColonist.Sum(dangerPoints);
			return new int[] { (int)colonistPoints, (int)armouryPoints, (int)turretPoints };
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

		public static float HorizontalSlider(Rect rect, float value, float leftValue, float rightValue, bool middleAlignment = false, string label = null, string leftAlignedLabel = null, string rightAlignedLabel = null, float roundTo = -1f)
		{
			if (middleAlignment || !label.NullOrEmpty())
				rect.y += Mathf.Round((rect.height - 16f) / 2f);
			if (!label.NullOrEmpty())
				rect.y += 5f;
			float num = GUI.HorizontalSlider(rect, value, leftValue, rightValue);
			if (!label.NullOrEmpty() || !leftAlignedLabel.NullOrEmpty() || !rightAlignedLabel.NullOrEmpty())
			{
				TextAnchor anchor = Text.Anchor;
				GameFont font = Text.Font;
				Text.Font = GameFont.Tiny;
				float num2 = (label.NullOrEmpty() ? 18f : Text.CalcSize(label).y);
				rect.y = rect.y - num2 + 3f;
				if (!leftAlignedLabel.NullOrEmpty())
				{
					Text.Anchor = TextAnchor.UpperLeft;
					Widgets.Label(rect, leftAlignedLabel);
				}
				if (!rightAlignedLabel.NullOrEmpty())
				{
					Text.Anchor = TextAnchor.UpperRight;
					Widgets.Label(rect, rightAlignedLabel);
				}
				if (!label.NullOrEmpty())
				{
					Text.Anchor = TextAnchor.UpperCenter;
					Widgets.Label(rect, label);
				}
				Text.Anchor = anchor;
				Text.Font = font;
			}
			if (roundTo > 0f)
				num = (float)Mathf.RoundToInt(num / roundTo) * roundTo;
			if (value != num)
				SoundDefOf.DragSlider.PlayOneShotOnCamera();
			return num;
		}

		public static bool ButtonText(Rect rect, string label, bool active, Color activeColor, Color inactiveColor)
		{
			var anchor = Text.Anchor;
			var color = GUI.color;
			var atlas = Widgets.ButtonBGAtlas;
			if (active && Mouse.IsOver(rect))
			{
				atlas = Widgets.ButtonBGAtlasMouseover;
				if (Input.GetMouseButton(0))
					atlas = Widgets.ButtonBGAtlasClick;
			}
			Widgets.DrawAtlas(rect, atlas);
			if (active)
				MouseoverSounds.DoRegion(rect);
			GUI.color = active ? activeColor : inactiveColor;
			Text.Anchor = TextAnchor.MiddleCenter;
			var wordWrap = Text.WordWrap;
			Text.WordWrap = false;
			Widgets.Label(rect, label);
			Text.Anchor = anchor;
			GUI.color = color;
			Text.WordWrap = wordWrap;
			return active && Widgets.ButtonInvisible(rect, false);
		}

		public static void OnGUISimple(this QuickSearchWidget self, Rect rect, Action onFilterChange = null)
		{
			if (OriginalEventUtility.EventType == EventType.MouseDown && !rect.Contains(Event.current.mousePosition))
				self.Unfocus();

			var color = GUI.color;
			GUI.color = Color.white;

			var num = Mathf.Min(18f, rect.height);
			var num2 = num + 8f;
			var y = rect.y + (rect.height - num2) / 2f + 4f;
			var position = new Rect(rect.x + 4f, y, num, num);
			GUI.DrawTexture(position, TexButton.Search);

			var rect3 = new Rect(rect.xMax - 4f - num, y, num, num);
			if (self.filter.Text != "" && Widgets.ButtonInvisible(rect3))
			{
				self.filter.Text = "";
				SoundDefOf.CancelMode.PlayOneShotOnCamera(null);
				onFilterChange?.Invoke();
			}

			GUI.SetNextControlName(self.controlName);
			var rect2 = rect;
			rect2.xMin = position.xMax + 4f;
			var text = Widgets.TextField(rect2, self.filter.Text, 15, null);

			if (text != self.filter.Text)
			{
				self.filter.Text = text;
				onFilterChange?.Invoke();
			}

			if (self.filter.Text != "")
				GUI.DrawTexture(rect3, TexButton.CloseXSmall);

			GUI.color = color;
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
			var newThing = (MoteBubble)ThingMaker.MakeThing(CustomDefs.ZombieThought, null);
			newThing.iconMat = material;
			newThing.Attach(pawn);
			_ = GenSpawn.Spawn(newThing, pawn.Position, pawn.Map);
		}

		public static void CastBlockBubble(Pawn attacker, Pawn defender)
		{
			var block = (MoteAttached)ThingMaker.MakeThing(CustomDefs.Mote_Block, null);
			block.Scale = 0.5f;
			block.Attach(defender, (attacker.DrawPos - defender.DrawPos) * 0.25f);
			_ = GenSpawn.Spawn(block, defender.Position, defender.Map, WipeMode.Vanish);
		}

		static readonly ThingDef[] bumps = new ThingDef[] { CustomDefs.BumpSmall, CustomDefs.BumpMedium, CustomDefs.BumpLarge };
		static readonly float[] nextBumps = new float[] { 0f, 0f, 0f };
		public static void CastBumpMote(Map map, Vector3 pos, int idx)
		{
			var now = Time.time;
			if (now < nextBumps[idx])
				return;
			nextBumps[idx] = now + Rand.Range(0.25f, 0.5f);
			var mote = (Mote)ThingMaker.MakeThing(bumps[idx], null);
			mote.exactPosition = pos + Rand.UnitVector3 * 0.25f;
			mote.exactRotation = UnityEngine.Random.Range(-30f, 30f);
			mote.exactScale = Vector3.one + Vector3.one * (idx) / 2f;
			mote.rotationRate = 25f * UnityEngine.Random.Range(1, 3) * Rand.Sign;
			mote.instanceColor = new(1, 1, 1, 0.5f + idx * 0.1f);
			_ = GenSpawn.Spawn(mote, pos.ToIntVec3(), map, WipeMode.Vanish);
		}

		static readonly float[] halfToDouble = { 0.5f, 1.0f, 2.0f };
		public static float HalfToDoubleValue(this ZombieInstinct e)
		{
			return halfToDouble[(int)e];
		}

		public static Dictionary<float, HashSet<IntVec3>> circles;
		public static IEnumerable<IntVec3> GetCircle(float radius)
		{
			circles ??= new Dictionary<float, HashSet<IntVec3>>();
			var cells = circles.ContainsKey(radius) ? circles[radius] : null;
			if (cells == null)
			{
				cells = new HashSet<IntVec3>();
				var enumerator = GenRadial.RadialPatternInRadius(radius).GetEnumerator();
				while (enumerator.MoveNext())
				{
					var v = enumerator.Current;
					_ = cells.Add(v);
					_ = cells.Add(new IntVec3(-v.x, 0, v.z));
					_ = cells.Add(new IntVec3(-v.x, 0, -v.z));
					_ = cells.Add(new IntVec3(v.x, 0, -v.z));
				}
				enumerator.Dispose();
				circles[radius] = cells;
			}
			return cells;
		}

		public static string TranslateHoursToText(float hours)
		{
			var ticks = (int)(GenDate.TicksPerHour * hours);
			return ticks.ToStringTicksToPeriodVerbose(true, false);
		}

		public static string TranslateHoursToText(int hours)
		{
			var ticks = GenDate.TicksPerHour * hours;
			return ticks.ToStringTicksToPeriodVerbose(true, false);
		}

		public static void Look<T>(ref T[] list, string label, params object[] ctorArgs) where T : IExposable
		{
			if (Scribe.EnterNode(label) == false)
				return;

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
					if (cond.operand is Label label)
						labels.Add(label);
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

				return instructions;
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
			if (pawn is Zombie)
				return false;
			return pawn.health.Downed;
		}

		public static IEnumerable<CodeInstruction> DownedReplacer(IEnumerable<CodeInstruction> instructions, int skip = 0)
		{
			var m_get_Downed = typeof(Pawn).PropertyGetter(nameof(Pawn.Downed));
			var m_replacement = SymbolExtensions.GetMethodInfo(() => DownedReplacement(null));

			var found = false;
			foreach (var instruction in instructions)
			{
				if (instruction.Calls(m_get_Downed))
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

			if (!found)
				Log.Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);
		}

		public static int ExtractPerZombie()
		{
			var f = ZombieSettings.Values.corpsesExtractAmount;
			var n = Mathf.FloorToInt(f);
			f -= n;
			n += Rand.Chance(f) ? 1 : 0;
			return n;
		}

		public static void DropLoot(Zombie zombie)
		{
			var f = ZombieSettings.Values.lootExtractAmount;
			var amount = Mathf.FloorToInt(f);
			f -= amount;
			amount += Rand.Chance(f) ? 1 : 0;

			for (var i = 1; i <= amount; i++)
			{
				var apparels = zombie.apparel.UnlockedApparel;
				if (apparels.Any() == false)
					break;
				var apparel = apparels.RandomElementByWeight(apparel => apparel.MarketValue);
				_ = zombie.apparel.TryDrop(apparel);
			}
		}

		public static void UpdateBiomeBlacklist(HashSet<string> defNames)
		{
			biomeBlacklist = defNames
				.Select(name => DefDatabase<BiomeDef>.GetNamed(name, false))
				.OfType<BiomeDef>()
				.ToHashSet();
			if (SoSTools.sosOuterSpaceBiomeDef != null)
				_ = biomeBlacklist.Add(SoSTools.sosOuterSpaceBiomeDef);
		}

		public static bool IsBlacklisted(this Map map)
		{
			if (map == null)
				return false;
			return biomeBlacklist.Contains(map.Biome);
		}

		static readonly RenderTexture renderTextureBack = new(64, 64, 16);
		static readonly RenderTexture renderTextureFore = new(256, 256, 16);
		public static void CreateFakeZombie(Map map, Action<Material> callback, bool foreground)
		{
			var zombie = ZombieGenerator.SpawnZombie(IntVec3.Zero, map, ZombieType.Normal);

			zombie.rubbleCounter = Constants.RUBBLE_AMOUNT;
			zombie.state = ZombieState.Floating;
			zombie.Rotation = Rot4.South;

			var renderTexture = foreground ? renderTextureFore : renderTextureBack;
			Find.PawnCacheRenderer.RenderPawn(zombie, renderTexture, Vector3.zero, 1f, 0f, Rot4.South, true, true, true, true, true, Vector3.zero, null, null, false);
			var texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
			RenderTexture.active = renderTexture;
			texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
			texture.Apply();
			RenderTexture.active = null;
			RenderTexture.ReleaseTemporary(renderTexture);

			if (foreground)
			{
				var materialFront = MaterialPool.MatFrom(new MaterialRequest(texture, ShaderDatabase.MetaOverlay));
				callback(materialFront);
			}
			else
			{
				var materialBack = MaterialPool.MatFrom(texture);
				materialBack.renderQueue = 2000 + 60;
				callback(materialBack);
			}

			zombie.Destroy();
		}
	}

	/*public class Random
	{
		private readonly uint seed;

		public Random(uint seed = 0)
		{
			this.seed = seed;
		}

		static uint BitRotate(uint x)
		{
			const int bits = 16;
			return (x << bits) | (x >> (32 - bits));
		}

		public uint GetValue(int x, int y)
		{
			var num = seed;
			for (uint i = 0; i < 16; i++)
			{
				num = num * 541 + (uint)x;
				num = BitRotate(num);
				num = num * 809 + (uint)y;
				num = BitRotate(num);
				num = num * 673 + (uint)i;
				num = BitRotate(num);
			}
			return num % 4;
		}
	}
	*/
}
