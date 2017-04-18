using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	static class Main
	{
		public static PheromoneGrid phGrid;
		public static long pheromoneFadeoff = 600L * 10000000;
		public static IntVec3 centerOfInterest = IntVec3.Invalid;

		static Main()
		{
			var harmony = HarmonyInstance.Create("net.pardeike.zombieland");
			harmony.PatchAll(Assembly.GetExecutingAssembly());
		}

		// patch for debugging: start a specific game when loading rimworld
		//
		[HarmonyPatch(typeof(UIRoot_Entry), "Init", new Type[0])]
		static class UIRoot_Entry_Init_Patch
		{
			static bool firstTime = true;
			static void Postfix()
			{
				Environment.GetCommandLineArgs().Aggregate((prev, saveGameName) =>
				{
					if (firstTime && GenScene.InEntryScene && prev.ToLower() == "-rungame")
					{
						PreLoadUtility.CheckVersionAndLoad(GenFilePaths.FilePathForSavedGame(saveGameName), ScribeMetaHeaderUtility.ScribeHeaderMode.Map, () =>
						{
							firstTime = false;
							LongEventHandler.QueueLongEvent(delegate
							{
								Current.Game = new Game() { InitData = new GameInitData() { gameToLoad = saveGameName } };
							}, "Play", "LoadingLongEvent", true, null);
						});
					}
					return saveGameName;
				});
			}
		}

		// patch for debugging: show pheromone grid as overlay
		// 
		[HarmonyPatch(typeof(SelectionDrawer))]
		[HarmonyPatch("DrawSelectionOverlays")]
		static class SelectionDrawer_DrawSelectionOverlays_Patch
		{
			static void Prefix()
			{
				var now = Stopwatch.GetTimestamp();
				phGrid.IterateCells((x, z, pheromone) =>
				{
					Vector3 pos = new Vector3(x, Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1), z);
					Matrix4x4 matrix = new Matrix4x4();
					matrix.SetTRS(pos + new Vector3(0.5f, 0f, 0.5f), Quaternion.identity, new Vector3(1f, 1f, 1f));
					var diff = now - pheromone.timestamp;
					if (diff < pheromoneFadeoff)
					{
						var a = (pheromoneFadeoff - diff) / (float)pheromoneFadeoff;
						var material = SolidColorMaterials.SimpleSolidColorMaterial(new Color(1f, 0f, 0f, a));
						Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
					}
				});
			}
		}

		// initialize the pheromone matrix after the map is loaded
		//
		[HarmonyPatch(typeof(Map))]
		[HarmonyPatch("FinalizeInit")]
		static class Map_FinalizeInit_Patch
		{
			static void Postfix(Map __instance)
			{
				phGrid = new PheromoneGrid(__instance);
			}
		}

		// patch for repeating calculations
		//
		[HarmonyPatch(typeof(TickManager))]
		[HarmonyPatch("DoSingleTick")]
		static class TickManager_DoSingleTick_Patch
		{
			static int updateCounter = 0;
			static int updateDelay = GenTicks.SecondsToTicks(2f);

			static Dictionary<string, IntVec3> lastPositions = new Dictionary<string, IntVec3>();

			static void Postfix()
			{
				var allPawns = Find.VisibleMap.mapPawns.AllPawnsSpawned
					.Where(pawn => pawn.GetType() != Zombie.type);

				allPawns.Do(pawn =>
				{
					var pos = pawn.Position;
					var id = pawn.ThingID;
					if (lastPositions.ContainsKey(id) == false)
						lastPositions[id] = pos;
					else
					{
						if (pos != lastPositions[id])
						{
							var destCell = IntVec3.Invalid;
							if (pawn.pather != null && pawn.pather.Destination != null)
								destCell = pawn.pather.Destination.Cell;
							phGrid.Set(pos, destCell);
						}

						lastPositions[id] = pos;
					}
				});

				if (updateCounter-- > 0) return;
				updateCounter = updateDelay;

				int x = 0, z = 0, n = 0;
				int buildingMultiplier = 3;
				Find.VisibleMap.listerBuildings.allBuildingsColonist.Do(building =>
				{
					x += building.Position.x * buildingMultiplier;
					z += building.Position.z * buildingMultiplier;
					n += buildingMultiplier;
				});
				allPawns.Do(pawn =>
				{
					x += pawn.Position.x;
					z += pawn.Position.z;
					n++;
				});
				centerOfInterest = new IntVec3(x / n, 0, z / n);
			}
		}

		// patch for setting a custom skin color for our zombies
		//
		[HarmonyPatch(typeof(Pawn_StoryTracker))]
		[HarmonyPatch("SkinColor", PropertyMethod.Getter)]
		static class Pawn_StoryTracker_SkinColor_Setter_Patch
		{
			static bool Prefix(Pawn_StoryTracker __instance, ref Color __result)
			{
				var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
				if (pawn.Faction.def == FactionDef.Named("Zombies"))
				{
					__result = new Color(
						79f / 255f,
						130f / 255f,
						68f / 255f
					);
					return false;
				}
				return true;
			}
		}

		// patch for variable zombie movement speed
		//
		[HarmonyPatch(typeof(StatExtension))]
		[HarmonyPatch("GetStatValue")]
		static class StatExtension_GetStatValue_Patch
		{
			static bool Prefix(Thing thing, StatDef stat, ref float __result)
			{
				var zombie = thing as Zombie;
				if (zombie != null && stat == StatDefOf.MoveSpeed)
				{
					__result = zombie.isSniffing ? 0.8f : 0.2f;
					return false;
				}
				return true;
			}
		}

		// patch for disallowing thoughts on zombies
		//
		[HarmonyPatch(typeof(ThoughtHandler))]
		[HarmonyPatch("CanGetThought")]
		static class ThoughtHandler_CanGetThought_Patch
		{
			static bool Prefix(ThoughtHandler __instance, ThoughtDef def, ref bool __result)
			{
				if (__instance.pawn.GetType() == Zombie.type)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}

		// patch for not forbidding zombie corpses
		//
		[HarmonyPatch(typeof(ForbidUtility))]
		[HarmonyPatch("SetForbiddenIfOutsideHomeArea")]
		static class ForbidUtility_SetForbiddenIfOutsideHomeArea_Patch
		{
			static bool Prefix(Thing t)
			{
				return (t.GetType() != ZombieCorpse.type);
			}
		}

		// patch for a custom zombie corpse class
		//
		[HarmonyPatch(typeof(ThingMaker))]
		[HarmonyPatch("MakeThing")]
		static class ThingMaker_MakeThing_Patch
		{
			static void Prefix(ThingDef def)
			{
				if (def.IsCorpse == false) return;
				if (def.ingestible == null) return;
				if (def.ingestible.sourceDef.GetType() == ThingDef_Zombie.type)
				{
					def.selectable = false;
					def.drawGUIOverlay = false;
					def.hasTooltip = false;
					def.hideAtSnowDepth = 0.1f;
					def.inspectorTabs = new List<Type>();
					def.passability = Traversability.Standable;
					def.regionBarrier = false;
					def.stackLimit = 999;
					def.thingClass = typeof(ZombieCorpse);
				}
			}
		}

		// patch to make zombie corpses vanish faster
		//
		[HarmonyPatch(typeof(Corpse))]
		[HarmonyPatch("ShouldVanish", PropertyMethod.Getter)]
		static class Corpse_get_ShouldVanish_Patch
		{
			static Type zombieCorpseClass = typeof(ZombieCorpse);
			static bool Prefix(object __instance, ref bool __result)
			{
				if (__instance.GetType() != zombieCorpseClass) return true;
				var zombieCorpse = (ZombieCorpse)__instance;
				__result = zombieCorpse.ShouldVanish();
				return false;
			}
		}

		// temporary patch to create zombies when alt-clicking on map
		//
		[HarmonyPatch(typeof(Selector))]
		[HarmonyPatch("HandleMapClicks")]
		static class HandleMapClicks_Patch
		{
			static bool Prefix(Selector __instance)
			{
				if (Event.current.alt == false) return true;
				if (Event.current.type != EventType.MouseDown) return true;
				if (Event.current.button != 0) return true;

				var cell = UI.MouseCell();
				var map = Find.VisibleMap;

				var zombie = ZombieGenerator.GeneratePawn(map);
				GenPlace.TryPlaceThing(zombie, cell, map, ThingPlaceMode.Near, null);

				Event.current.Use();
				return false;
			}
		}
	}
}