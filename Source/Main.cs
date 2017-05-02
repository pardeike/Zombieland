using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	static class Main
	{
		public static bool DEBUGGRID = false;
		public static bool USE_SOUND = false;
		public static int DEBUG_COLONY_POINTS = 1000;

		public static PheromoneGrid phGrid;

		public static long pheromoneFadeoff = 1000L * GenTicks.SecondsToTicks(90f);
		public static Dictionary<string, IntVec3> lastPositions = new Dictionary<string, IntVec3>();
		public static Queue<TargetInfo> spawnQueue = new Queue<TargetInfo>();

		public static Material rubble = MaterialPool.MatFrom("Rubble", ShaderDatabase.Cutout);

		static Main()
		{
			// HarmonyInstance.DEBUG = true;
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
				if (DEBUGGRID == false) return;
				var now = Tools.Ticks();
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

		// patch to add a pheromone info section to the rimworld cell inspector
		//
		[HarmonyPatch(typeof(EditWindow_DebugInspector))]
		[HarmonyPatch("CurrentDebugString")]
		static class EditWindow_DebugInspector_CurrentDebugString_Patch
		{
			static FieldInfo writeCellContentsField = AccessTools.Field(typeof(DebugViewSettings), "writeCellContents");
			static MethodInfo debugGridMethod = AccessTools.Method(typeof(EditWindow_DebugInspector_CurrentDebugString_Patch), "DebugGrid");

			static void DebugGrid(StringBuilder builder)
			{
				var cell = phGrid.Get(UI.MouseCell(), false);
				if (cell.timestamp > 0)
				{
					var now = Tools.Ticks();
					builder.AppendLine("Pheromones ts=" + cell.timestamp + "(" + (now - cell.timestamp) + ") zc=" + cell.zombieCount);
				}
				else
					builder.AppendLine("Pheromones empty");
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				bool previousPopInstruction = false;
				foreach (var instruction in instructions)
				{
					if (previousPopInstruction == false && instruction.opcode == OpCodes.Pop)
					{
						previousPopInstruction = true;
						yield return instruction;
					}
					else if (previousPopInstruction && instruction.opcode == OpCodes.Ldsfld && instruction.operand == writeCellContentsField)
					{
						yield return new CodeInstruction(OpCodes.Ldloc_0);
						yield return new CodeInstruction(OpCodes.Call, debugGridMethod);
						yield return instruction;
					}
					else
					{
						yield return instruction;
						previousPopInstruction = false;
					}
				}
			}
		}

		// initialize the pheromone matrix after the map is loaded
		//
		[HarmonyPatch(typeof(Map))]
		[HarmonyPatch("FinalizeInit")]
		static class Scribe_FinalizeInit_Patch
		{
			static void Prefix(Map __instance)
			{
				phGrid = __instance.components.OfType<PheromoneGrid>().FirstOrDefault();
				if (phGrid == null)
				{
					phGrid = new PheromoneGrid(__instance);
					__instance.components.Add(phGrid);
				}
			}
		}

		// patch for repeating calculations
		//
		[HarmonyPatch(typeof(Verse.TickManager))]
		[HarmonyPatch("DoSingleTick")]
		static class TickManager_DoSingleTick_Patch
		{
			static void Postfix()
			{
				TickManager.Tick();
			}
		}

		// patch for rendering zombies
		//
		[HarmonyPatch(typeof(PawnRenderer))]
		[HarmonyPatch("RenderPawnAt")]
		static class PawnRenderer_RenderPawnAt_Patch
		{
			static bool Prefix(PawnRenderer __instance, Vector3 drawLoc, RotDrawMode bodyDrawType)
			{
				var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
				var zombie = pawn as Zombie;
				if (zombie == null || zombie.state != ZombieState.Emerging) return true;
				zombie.Render(__instance, drawLoc, bodyDrawType);
				return false;
			}

			/*
			static void Postfix(Vector3 drawLoc)
			{
				var m1 = MaterialPool.MatFrom("UI/Overlays/TargetHighlight_Square", ShaderDatabase.Transparent);
				m1.renderQueue = 3010;
				Tools.DrawScaledMesh(MeshPool.plane10, m1, drawLoc + new Vector3(0.3f, 1f, 0.5f), Quaternion.Euler(0f, 45f, 0f), 1f, 1f);

				var m2 = SolidColorMaterials.SimpleSolidColorMaterial(new Color(1f, 0f, 1f));
				m2.renderQueue = 3020;
				Tools.DrawScaledMesh(MeshPool.plane10, m2, drawLoc + new Vector3(-0.3f, 1f, 0.5f), Quaternion.Euler(0f, 45f, 0f), 1f, 1f);
			}
			*/
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
				if (pawn.GetType() == Zombie.type)
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
					__result = zombie.state == ZombieState.Tracking ? 1f : 0.2f;
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

		[HarmonyPatch(typeof(Pawn_InteractionsTracker))]
		[HarmonyPatch("TryInteractWith")]
		static class Pawn_InteractionsTracker_TryInteractWith_Patch
		{
			static bool Prefix(Pawn_InteractionsTracker __instance, Pawn recipient)
			{
				var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
				if (pawn.GetType() == Zombie.type) return false;
				if (recipient.GetType() == Zombie.type) return false;
				return true;
			}
		}

		[HarmonyPatch(typeof(Pawn_InteractionsTracker))]
		[HarmonyPatch("InteractionsTrackerTick")]
		static class Pawn_InteractionsTracker_InteractionsTrackerTick_Patch
		{
			static bool Prefix(Pawn_InteractionsTracker __instance)
			{
				var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
				return (pawn.GetType() != Zombie.type);
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
					def.stackLimit = 1;
					def.thingClass = typeof(ZombieCorpse);
				}
			}
		}

		// patch so that zombie corpses vanish quicker
		//
		[HarmonyPatch(typeof(Corpse))]
		[HarmonyPatch("ShouldVanish", PropertyMethod.Getter)]
		static class Corpse_ShouldVanish_Patch
		{
			static bool Prefix(Corpse __instance, ref bool __result)
			{
				var zombieCorpse = __instance as ZombieCorpse;
				if (zombieCorpse == null) return true;

				__result = __instance.Age >= zombieCorpse.vanishAfter;
				return false;
			}
		}

		// patch to handle targets downed so that we update our grid
		//
		[HarmonyPatch(typeof(Pawn_HealthTracker))]
		[HarmonyPatch("MakeDowned")]
		static class Pawn_HealthTracker_MakeDowned_Patch
		{
			static void Postfix(Pawn_HealthTracker __instance)
			{
				if (phGrid == null) return;

				var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
				if (pawn.GetType() == Zombie.type) return;
				var timestamp = phGrid.Get(pawn.Position).timestamp;
				var radius = pawn.RaceProps.Animal ? 1f : 5f;
				Tools.GetCircle(radius).Do(vec =>
				{
					var pos = pawn.Position + vec;
					var cell = phGrid.Get(pos, false);
					if (cell.timestamp > 0 && cell.timestamp <= timestamp && Rand.Bool)
						phGrid.SetTimestamp(pos, 0);
				});
				phGrid.SetTimestamp(pawn.Position, 0);
			}
		}

		// patch to clean up our lastLocation directory
		//
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch("DeSpawn")]
		static class Pawn_DeSpawn_Patch
		{
			static void Postfix(Pawn __instance)
			{
				if (__instance.Map != Find.VisibleMap) return;
				var id = __instance.ThingID;
				if (lastPositions.ContainsKey(id)) lastPositions.Remove(id);
			}
		}

		[HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility))]
		[HarmonyPatch("GetThoughts")]
		static class PawnDiedOrDownedThoughtsUtility_GetThoughts_Patch
		{
			static void Prefix(ref Hediff hediff)
			{
				if (hediff == null) return;
				try
				{
					// somehow, 'def' can cause a NPE sometimes
					// so we catch this and use a null hediff instead
					//
					if (hediff.def == null)
						hediff = null;
				}
				catch (Exception)
				{
					hediff = null;
				}
			}
		}

		// patch to handle targets deaths so that we update our grid
		//
		[HarmonyPatch(typeof(Pawn_HealthTracker))]
		[HarmonyPatch("Kill")]
		static class Pawn_HealthTracker_Kill_Patch
		{
			static void Postfix(Pawn_HealthTracker __instance)
			{
				if (phGrid == null) return;

				var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
				if (pawn == null) return;

				if (pawn.GetType() == Zombie.type)
				{
					var dest = pawn.pather == null ? null : pawn.pather.Destination;
					if (dest != null)
						phGrid.ChangeZombieCount(dest.Cell, -1);
					phGrid.ChangeZombieCount(pawn.Position, -1);
					return;
				}

				var id = pawn.ThingID;
				if (id == null) return;

				if (pawn.Map != Find.VisibleMap && lastPositions.ContainsKey(id)) lastPositions.Remove(id);

				var timestamp = phGrid.Get(pawn.Position).timestamp;
				var radius = pawn.RaceProps.Animal ? 3f : 5f;
				Tools.GetCircle(radius).Do(vec =>
				{
					var pos = pawn.Position + vec;
					var cell = phGrid.Get(pos, false);
					if (cell.timestamp > 0 && cell.timestamp <= timestamp && Rand.Bool)
						phGrid.SetTimestamp(pos, 0);
				});
			}
		}

		// patch to trigger on gun shots
		//
		[HarmonyPatch(typeof(Projectile))]
		[HarmonyPatch("Launch")]
		[HarmonyPatch(new Type[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(Thing) })]
		static class Projectile_Launch_Patch
		{
			static void Prefix(Projectile __instance, Thing launcher, Vector3 origin, LocalTargetInfo targ, Thing equipment)
			{
				if ((launcher is Pawn) == false) return;

				var now = Tools.Ticks();
				var pos = origin.ToIntVec3();
				var radius = (targ.CenterVector3 - origin).magnitude;
				Tools.GetCircle(radius).Do(vec => phGrid.SetTimestamp(pos + vec, now - (int)vec.LengthHorizontalSquared));
			}
		}

		/* temporary patch to create zombies when alt-clicking on map
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
		}*/
	}
}
