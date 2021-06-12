using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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

	[StaticConstructorOnStartup]
	static class Tools
	{
		public static ZombieAvoider avoider = new ZombieAvoider();
		public static Texture2D MenuIcon;
		public static Texture2D ZombieButtonBackground;
		public static string zlNamespace = typeof(Tools).Namespace;

		// public static List<Region> debugRegions = new List<Region>();
		public static List<Region> cachedPlayerReachableRegions = new List<Region>();
		public static int nextPlayerReachableRegionsUpdate = 0;

		private static DamageDef _zombieBiteDamageDef;
		public static DamageDef ZombieBiteDamageDef
		{
			get
			{
				if (_zombieBiteDamageDef == null)
					_zombieBiteDamageDef = DefDatabase<DamageDef>.GetNamed("ZombieBite");
				return _zombieBiteDamageDef;
			}
		}

		private static DamageDef _suicideBombDamageDef;
		public static DamageDef SuicideBombDamageDef
		{
			get
			{
				if (_suicideBombDamageDef == null)
					_suicideBombDamageDef = DefDatabase<DamageDef>.GetNamed("SuicideBomb");
				return _suicideBombDamageDef;
			}
		}

		private static DamageDef _toxicSplatterDamageDef;
		public static DamageDef ToxicSplatterDamageDef
		{
			get
			{
				if (_toxicSplatterDamageDef == null)
					_toxicSplatterDamageDef = DefDatabase<DamageDef>.GetNamed("ToxicSplatter");
				return _toxicSplatterDamageDef;
			}
		}

		private static DamageDef _electricalShockDamageDef;
		public static DamageDef ElectricalShockDamageDef
		{
			get
			{
				if (_electricalShockDamageDef == null)
					_electricalShockDamageDef = DefDatabase<DamageDef>.GetNamed("ElectricalShock");
				return _electricalShockDamageDef;
			}
		}

		private static ThingDef _electricalFieldThingDef;
		public static ThingDef ElectricalFieldThingDef
		{
			get
			{
				if (_electricalFieldThingDef == null)
					_electricalFieldThingDef = DefDatabase<ThingDef>.GetNamed("ElectricalField");
				return _electricalFieldThingDef;
			}
		}

		static string mealLabel;
		static string mealDescription;
		static Graphic mealGraphic;
		public static void EnableTwinkie(bool enable)
		{
			var def = ThingDefOf.MealSurvivalPack;

			if (mealLabel == null) mealLabel = def.label;
			if (mealDescription == null) mealDescription = def.description;
			if (mealGraphic == null) mealGraphic = def.graphicData.cachedGraphic;

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
			if (game != null)
			{
				game.Maps
					.SelectMany(map => map.listerThings.ThingsOfDef(def))
					.Do(meal => meal.graphicInt = null);
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

		public static Texture2D LoadTexture(string path, bool makeReadonly = true)
		{
			var fullPath = Path.Combine(Tools.GetModRootDirectory(), "Textures", $"{path}.png");
			var data = File.ReadAllBytes(fullPath);
			if (data == null || data.Length == 0) throw new Exception($"Cannot read texture {fullPath}");
			var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
			if (tex.LoadImage(data) == false) throw new Exception($"Cannot create texture {fullPath}");
			tex.Compress(true);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Trilinear;
			tex.Apply(true, makeReadonly);
			return tex;
		}

		public static string SafeTranslate(this string key)
		{
			if (key == null) return "";
			return key.Translate();
		}

		public static string SafeTranslate(this string key, params object[] args)
		{
			if (key == null) return "";
#pragma warning disable CS0618
			return key.Translate(args);
#pragma warning restore CS0618
		}

		public static long Ticks()
		{
			return 1000L * GenTicks.TicksAbs;
		}

		public static float Difficulty()
		{
#if RW11
			return Find.Storyteller.difficulty.difficulty;
#else
			return Find.Storyteller.difficultyValues.threatScale;
#endif
		}

		public static int PheromoneFadeoff()
		{
			return (int)(Constants.PHEROMONE_FADEOFF.SecondsToTicks() * ZombieSettings.Values.zombieInstinct.HalfToDoubleValue()) * 1000;
		}

		static readonly Dictionary<Map, PheromoneGrid> gridCache = new Dictionary<Map, PheromoneGrid>();
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

		public static T Boxed<T>(T val, T min, T max) where T : IComparable
		{
			if (val.CompareTo(min) < 0) return min;
			if (val.CompareTo(max) > 0) return max;
			return val;
		}

		public static void Shuffle<T>(this IList<T> list)
		{
			var n = list.Count;
			while (n > 1)
			{
				n--;
				var k = Constants.random.Next(n + 1);
				var value = list[k];
				list[k] = list[n];
				list[n] = value;
			}
		}

		public static T SafeRandomElement<T>(this IEnumerable<T> source)
		{
			if (source.Count() == 0) return default;
			return source.RandomElement();
		}

		public static float RadiusForPawn(Pawn pawn)
		{
			var radius = pawn.RaceProps.Animal ? Constants.ANIMAL_PHEROMONE_RADIUS : Constants.HUMAN_PHEROMONE_RADIUS;
			return radius * ZombieSettings.Values.zombieInstinct.HalfToDoubleValue();
		}

		public static bool ShouldAvoidZombies(Pawn pawn = null)
		{
			if (pawn == null)
				return ZombieSettings.Values.betterZombieAvoidance;

			if (pawn is Zombie || pawn.RaceProps.Humanlike == false)
				return false;

			if (pawn.IsColonist == false)
			{
				if (pawn.HostileTo(Faction.OfPlayer) == false)
				{
					var map = pawn.Map;
					if (map.areaManager?.Home[pawn.Position] ?? false) return false;
					var room = GridsUtility.GetRoom(pawn.Position, map);
					if (room.IsHuge == false) return false;
				}

				return ZombieSettings.Values.betterZombieAvoidance;
			}

			if (ZombieSettings.Values.betterZombieAvoidance == false) return false;
			return ColonistSettings.Values.ConfigFor(pawn).autoAvoidZombies;
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

		/*public static bool CanPathTo(this Zombie zombie, IntVec3 cell)
		{
			var path = zombie.Map.pathFinder.FindPath(zombie.Position, cell, zombie, PathEndMode.InteractionCell);
			var result = path != PawnPath.NotFound;
			path.ReleaseToPool();
			return result;
		}*/

		/*public static List<Zombie> NearByZombiesSorted(Pawn pawn, IntVec3 center, int radius, bool ignoreDowned, bool sortByDistance, bool usePathing)
		{
			var maxDistance = radius * radius;
			var map = pawn.Map;

			var tp = TraverseParms.For(pawn, Danger.Deadly, TraverseMode.NoPassClosedDoors, false);
			var zombies = new List<Zombie>();
			var cx = center.x;
			var cz = center.z;
			int distanceSquared(int px, int pz) => (cx - px) * (cx - px) + (cz - pz) * (cz - pz);

			bool regionValidator(Region from, Region to)
			{
				if (to.Allows(tp, false) == false)
					return false;
				return true;
			}

			bool regionProcessor(Region r)
			{
				var zombie = r.ListerThings.ThingsOfDef(CustomDefs.Zombie).OfType<Zombie>().FirstOrDefault();
				if (zombie != null)
				{
					var pos = zombie.Position;
					if (distanceSquared(pos.x, pos.z) <= maxDistance)
						if (ignoreDowned || zombie.health.Downed == false)
							zombies.Add(zombie);
				}
				return false;
			}

			RegionTraverser.BreadthFirstTraverse(center, map, regionValidator, regionProcessor, 25, RegionType.Set_Passable);
			if (sortByDistance)
				zombies.Sort(new DistanceComparer(center));

			if (usePathing)
				_ = zombies.RemoveAll(zombie => zombie.CanPathTo(center) == false);

			return zombies;
		}*/

		/*public static Zombie NearestZombie(Pawn pawn, IntVec3 cell, int radius, bool ignoreDowned = false)
		{
			var zombies = NearByZombiesSorted(pawn, cell, radius, ignoreDowned, true, false);
			return zombies.FirstOrDefault(zombie => zombie.CanPathTo(cell);
		}*/

		/*public static IntVec3 ZombiesNearby(Pawn pawn, IntVec3 destination, bool ignoreDowned = false)
		{
			var tp = TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false);
			var foundZombie = IntVec3.Invalid;
			RegionTraverser.BreadthFirstTraverse(destination, pawn.Map, (Region from, Region to) => to.Allows(tp, false), delegate (Region r)
			{
				var zombie = r.ListerThings.ThingsOfDef(CustomDefs.Zombie).OfType<Zombie>().FirstOrDefault();
				if (zombie != null && (ignoreDowned || zombie.health.Downed == false))
					foundZombie = zombie.Position;
				return foundZombie.IsValid;

			}, 25, RegionType.Set_Passable);
			return foundZombie;
		}*/

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
			var ticks = GenTicks.TicksGame;
			if (ticks > nextPlayerReachableRegionsUpdate)
			{
				nextPlayerReachableRegionsUpdate = ticks + GenTicks.TickLongInterval;
				var f = Faction.OfPlayer;
				var totalRegions = map.regionGrid.AllRegions
					.Where(region => region.listerThings.AllThings.Any(thing =>
					{
						if (thing.Faction != f) return false;
						var def = thing.def;
						return def.fillPercent >= 0.5f && def.blockWind && def.coversFloor &&
							def.castEdgeShadows && def.holdsRoof && def.blockLight;
					}))
					.ToHashSet();
				var neighbours = totalRegions.SelectMany(region => region.Neighbors).ToList();
				PlayerReachableRegions_Iterator(totalRegions, neighbours);
				cachedPlayerReachableRegions = totalRegions.ToList();
			}
			return cachedPlayerReachableRegions;
		}

		public static IntVec3 RandomSpawnCell(Map map, bool nearEdge, Predicate<IntVec3> predicate)
		{
			var allRegions = PlayerReachableRegions(map);
			if (nearEdge)
				allRegions = allRegions.Where(region => region.touchesMapEdge).ToList();
			return allRegions
				.SelectMany(region => region.Cells)
				.InRandomOrder()
				.FirstOrFallback(cell => predicate(cell), IntVec3.Invalid);
		}

		public static void QueueConvertToZombie(ThingWithComps thing, Map mapForTickmanager)
		{
			var tickManager = mapForTickmanager.GetComponent<TickManager>();
			tickManager.colonistsConverter.Enqueue(thing);
		}

		public static void PlayTink(Thing thing)
		{
			if (Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
			{
				var info = SoundInfo.InMap(thing);
				CustomDefs.TankyTink.PlayOneShot(info);
			}
		}

		public static void PlayAbsorb(Thing thing)
		{
			if (Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
			{
				var info = SoundInfo.InMap(thing);
				CustomDefs.Bzzt.PlayOneShot(info);
			}
		}

		public static void PlaySmash(Thing thing)
		{
			if (Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
			{
				var info = SoundInfo.InMap(thing);
				CustomDefs.Smash.PlayOneShot(info);
			}
		}

		static readonly NameSingle emptyName = new NameSingle("");
		public static void ConvertToZombie(ThingWithComps thing, Map map, bool force = false)
		{
			var corpse = thing as Corpse;
			var pawn = corpse != null ? corpse.InnerPawn : thing as Pawn;
			if (pawn?.RaceProps == null || pawn.RaceProps.Humanlike == false || pawn.RaceProps.IsFlesh == false || AlienTools.IsFleshPawn(pawn) == false)
				return;

			var wasPlayer = pawn.Faction?.IsPlayer ?? false;
			var pawnName = pawn.Name;
			if (force == false && (pawn.health == null || pawnName == emptyName))
				return;
			pawn.Name = emptyName;

			var pos = thing is IThingHolder thingHolder ? ThingOwnerUtility.GetRootPosition(thingHolder) : thing.Position;
			var rot = pawn.Rotation;
			var wasInGround = corpse != null && corpse.ParentHolder != null && !(corpse.ParentHolder is Map);

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
					zombie.story.crownType = pawn.story.crownType;
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
							if (compQuality != null)
								compQuality.SetQuality(QualityCategory.Awful, ArtGenerationContext.Colony);

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

				tickManager.allZombiesCached.Add(zombie);

				var label = wasPlayer ? "ColonistBecameAZombieLabel".Translate() : "OtherBecameAZombieLabel".Translate();
				var text = "BecameAZombieDesc".SafeTranslate(new object[] { pawnName.ToStringShort });
				Find.LetterStack.ReceiveLetter(label, text, wasPlayer ? CustomDefs.ColonistTurnedZombie : CustomDefs.OtherTurnedZombie, zombie);
			});
			while (it.MoveNext()) ;
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
			if (cell.Standable(map) == false || cell.Fogged(map)) return false;

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
					) return true;
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
			if (pawn.RaceProps.IsFlesh == false) return false;
			if (AlienTools.IsFleshPawn(pawn) == false) return false;

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
				if (pawn.Spawned == false || pawn.health.Downed || pawn.Dead) return false;
				if (pawn.InMentalState) return false;
				if (pawn.InContainerEnclosed) return false;
				if (pawn.equipment.Primary == null) return false;
				if (pawn.health.summaryHealth.SummaryHealthPercent <= 0.25f) return false;

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

		public static bool Attackable(AttackMode mode, Thing thing)
		{
			if (thing is ZombieCorpse)
				return false;

			if (thing is Pawn target)
			{
				if (target.Dead || target.health.Downed)
					return false;

				var distance = (target.DrawPos - thing.DrawPos).MagnitudeHorizontalSquared();
				if (distance > Constants.MIN_ATTACKDISTANCE_SQUARED)
					return false;

				if (Tools.HasInfectionState(target, InfectionState.Infecting))
					return false;

				if (mode == AttackMode.Everything)
					return true;

				if (target.MentalState != null)
				{
					var msDef = target.MentalState.def;
					if (msDef == MentalStateDefOf.Manhunter || msDef == MentalStateDefOf.ManhunterPermanent)
						return true;
				}

				if (mode == AttackMode.OnlyHumans && target.RaceProps.Humanlike && target.RaceProps.IsFlesh && AlienTools.IsFleshPawn(target))
					return true;

				if (mode == AttackMode.OnlyColonists && target.IsColonist)
					return true;
			}
			return false;
		}

		public static Predicate<IntVec3> ZombieSpawnLocator(Map map, bool isEvent = false)
		{
			if (isEvent || ZombieSettings.Values.spawnWhenType == SpawnWhenType.AllTheTime || ZombieSettings.Values.spawnWhenType == SpawnWhenType.InEventsOnly)
				return cell => IsValidSpawnLocation(cell, map);

			if (ZombieSettings.Values.spawnWhenType == SpawnWhenType.WhenDark)
				return cell => IsValidSpawnLocation(cell, map) && map.glowGrid.PsychGlowAt(cell) == PsychGlow.Dark;

			Log.Error("Unsupported spawn mode " + ZombieSettings.Values.spawnWhenType);
			return null;
		}

		public static IntVec3 CenterOfInterest(Map map)
		{
			var colonists = map.mapPawns?.SpawnedPawnsInFaction(Faction.OfPlayer) ?? new List<Pawn>();
			var buildings = map.listerBuildings?.allBuildingsColonist ?? new List<Building>();

			if (colonists.Count == 0 && buildings.Count == 0)
				return IntVec3.Invalid;

			int x = 0, z = 0, n = 0;

			const int buildingMultiplier = 3;
			buildings.Do(building =>
			{
				x += building.Position.x * buildingMultiplier;
				z += building.Position.z * buildingMultiplier;
				n += buildingMultiplier;
			});

			colonists.Do(pawn =>
			{
				x += pawn.Position.x;
				z += pawn.Position.z;
				n++;
			});

			return new IntVec3(x / n, 0, z / n);
		}

		static readonly int[] _cellsAroundIndex = new int[] { 5, 6, 7, 4, -1, 0, 3, 2, 1 };
		public static int CellsAroundIndex(IntVec3 delta)
		{
			var v = Vector3.Normalize(delta.ToVector3()) * Mathf.Sqrt(2);
			var x = (int)Math.Round(v.x);
			var z = (int)Math.Round(v.z);
			if (x == 0 && z == 0) return -1;
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
					if (action(item)) return true;
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

		public static void AutoExposeDataWithDefaults<T>(this T settings) where T : new()
		{
			var defaults = new T();
			GetFieldNames(settings).Do(name =>
			{
				var finfo = Field(settings.GetType(), name);
				var value = finfo.GetValue(settings);
				var type = value.GetType();
				var defaultValue = Traverse.Create(defaults).Field(name).GetValue();
				var m_Look = Method(typeof(Scribe_Values), "Look", null, new Type[] { type });
				var arguments = new object[] { value, name, defaultValue, false };
				_ = m_Look.Invoke(null, arguments);
				finfo.SetValue(settings, arguments[0]);
			});
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
			if (verb == null) return 0f;
			var verbProps = verb?.verbProps;
			var damage = verbProps.defaultProjectile?.projectile.GetDamageAmount(null, null) ?? 0;
			if (damage == 0) return 0f;
			var burst = Mathf.Max(1, verbProps.burstShotCount);
			var interval = Mathf.Max(1, verbProps.AdjustedFullCycleTime(verb, null));
			return damage * 60f * burst / interval;
		}

		static readonly FieldRef<Building_TurretGun, CompPowerTrader> powerComp = FieldRefAccess<Building_TurretGun, CompPowerTrader>("powerComp");
		public static int[] ColonyPoints()
		{
			static float dangerPoints(Building building)
			{
				if (building is Building_TurretGun turretGun)
					return DPS(turretGun) * ((powerComp(turretGun)?.PowerOn ?? false) ? 1 : 0.5f);
				if (building is Building_Turret turret)
					return DPS(turret);
				if (building is IAttackTargetSearcher searcher)
					return DPS(searcher);
				if (building.def == ThingDefOf.Wall && building.def.MadeFromStuff)
					return building.def.GetStatValueAbstract(StatDefOf.MaxHitPoints, building.Stuff) / 500;
				return building.HitPoints / 100;
			}

			var map = Find.CurrentMap;
			if (map == null) return new int[3];
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
			_ = GenSpawn.Spawn(newThing, pawn.Position, pawn.Map);
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

			if (!found) Log.Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);
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
