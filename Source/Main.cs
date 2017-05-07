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
using Verse.AI;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	static class Main
	{
		public static Queue<TargetInfo> spawnQueue = new Queue<TargetInfo>();

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
			static void Postfix()
			{
				if (Constants.DEBUGGRID == false) return;
				var now = Tools.Ticks();
				Tools.GetGrid(Find.VisibleMap).IterateCells((x, z, pheromone) =>
				{
					Vector3 pos = new Vector3(x, Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1), z);
					Matrix4x4 matrix = new Matrix4x4();
					matrix.SetTRS(pos + new Vector3(0.5f, 0f, 0.5f), Quaternion.identity, new Vector3(1f, 1f, 1f));
					var diff = now - pheromone.timestamp;
					if (diff < Constants.PHEROMONE_FADEOFF)
					{
						var a = (Constants.PHEROMONE_FADEOFF - diff) / (float)Constants.PHEROMONE_FADEOFF * 0.8f;
						var material = SolidColorMaterials.SimpleSolidColorMaterial(new Color(1f, 0f, 0f, a));
						Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
					}
				});
			}
		}

		// patch to remove the constant danger music because of the constant thread of zombies
		//
		[HarmonyPatch(typeof(DangerWatcher))]
		[HarmonyPatch("DangerRating", PropertyMethod.Getter)]
		static class AttackTargetsCache_get_DangerRating_Patch
		{
			class ZombieDangerWatcher : AttackTargetsCache
			{
				public ZombieDangerWatcher(Map map) : base(map) { }

				HashSet<IAttackTarget> TargetsHostileToColonyWithoutZombies()
				{
					return new HashSet<IAttackTarget>(TargetsHostileToColony.Where(t => !(t is Zombie)));
				}
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				return Transpilers.MethodReplacer(instructions,
					AccessTools.Method(typeof(AttackTargetsCache), "get_TargetsHostileToColony"),
					AccessTools.Method(typeof(ZombieDangerWatcher), "TargetsHostileToColonyWithoutZombies")
				);
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
				var cell = Tools.GetGrid(Find.VisibleMap).Get(UI.MouseCell(), false);
				if (cell.timestamp > 0)
				{
					var now = Tools.Ticks();
					builder.AppendLine("Pheromones ts=" + cell.timestamp + "(" + (now - cell.timestamp) + ") zc=" + cell.zombieCount);
				}
				else
					builder.AppendLine("Pheromones empty");
				builder.AppendLine("Total Zombie Count should be " + TickManager.GetMaxZombieCount());
				builder.AppendLine("Total Zombie Count is " + TickManager.allZombies.Count());
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

		// patch for repeating calculations
		//
		[HarmonyPatch(typeof(Verse.TickManager))]
		[HarmonyPatch("DoSingleTick")]
		static class TickManager_DoSingleTick_Patch
		{
			static void Postfix(Verse.TickManager __instance)
			{
				TickManager.ZombieTicking(__instance.TickRateMultiplier);
				TickManager.Tick();
			}
		}

		// patch for detecting if a pawn enters a new cell
		//
		[HarmonyPatch(typeof(Thing))]
		[HarmonyPatch("Position", PropertyMethod.Setter)]
		static class Thing_Position_Patch
		{
			static void Prefix(Thing __instance, IntVec3 value)
			{
				var pawn = __instance as Pawn;
				if (pawn == null || pawn.Map == null) return;

				var pos = pawn.Position;
				if (pos.x == value.x && pos.z == value.z) return;

				var grid = Tools.GetGrid(pawn.Map);
				if (pawn is Zombie)
				{
					grid.ChangeZombieCount(pos, -1);
					var newPos = grid.Get(value);
					if (newPos.zombieCount > 0)
						newPos.timestamp -= newPos.zombieCount * Constants.ZOMBIE_CLOGGING_FACTOR;
				}
				else
				{
					var now = Tools.Ticks();
					var radius = Tools.RadiusForPawn(pawn);
					Tools.GetCircle(radius).Do(vec => grid.SetTimestamp(value + vec, now - (long)(2f * vec.LengthHorizontal)));
				}
			}
		}

		// patch to exclude zombies from ordinary ticking
		//
		[HarmonyPatch(typeof(Verse.TickManager))]
		[HarmonyPatch("RegisterAllTickabilityFor")]
		static class TickManager_RegisterAllTickabilityFor_Patch
		{
			static bool Prefix(Thing t)
			{
				var zombie = t as Zombie;
				if (zombie == null) return true;
				TickManager.allZombies.Add(zombie);
				return false;
			}
		}
		[HarmonyPatch(typeof(Verse.TickManager))]
		[HarmonyPatch("DeRegisterAllTickabilityFor")]
		static class TickManager_DeRegisterAllTickabilityFor_Patch
		{
			static bool Prefix(Thing t)
			{
				var zombie = t as Zombie;
				if (zombie == null) return true;
				TickManager.allZombies.Remove(zombie);
				return false;
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
				if (pawn is Zombie)
				{
					__result = Constants.ZOMBIE_SKIN_COLOR;
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
					if (zombie.state == ZombieState.Tracking)
						__result = Constants.ZOMBIE_MOVE_SPEED_TRACKING;
					else
						__result = Constants.ZOMBIE_MOVE_SPEED_IDLE;
					return false;
				}
				return true;
			}
		}

		// patch headshot to kill zombies right away
		//
		[HarmonyPatch(typeof(DamageWorker_AddInjury))]
		[HarmonyPatch("IsHeadshot")]
		static class DamageWorker_IsHeadshot_Patch
		{
			static void Postfix(Pawn pawn, bool __result)
			{
				if (__result == false) return;
				if (pawn is Zombie zombie && zombie.Destroyed == false && zombie.Dead == false)
					zombie.state = ZombieState.ShouldDie;
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
				if (__instance.pawn is Zombie)
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
				if (pawn is Zombie) return false;
				if (recipient is Zombie) return false;
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
				return !(pawn is Zombie);
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
				if (def.ingestible.sourceDef is ThingDef_Zombie)
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
				var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
				if (pawn is Zombie) return;
				if (pawn.Map == null) return;

				var grid = Tools.GetGrid(pawn.Map);
				if (Constants.KILL_CIRCLE_RADIUS_MULTIPLIER > 0)
				{
					var timestamp = grid.Get(pawn.Position).timestamp;
					var radius = Tools.RadiusForPawn(pawn) * Constants.KILL_CIRCLE_RADIUS_MULTIPLIER;
					Tools.GetCircle(radius).Do(vec =>
					{
						var pos = pawn.Position + vec;
						var cell = grid.Get(pos, false);
						if (cell.timestamp > 0 && cell.timestamp <= timestamp)
							grid.SetTimestamp(pos, 0);
					});
				}
				grid.SetTimestamp(pawn.Position, 0);
			}
		}

		[HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility))]
		[HarmonyPatch("AppendThoughts_Relations")]
		static class PawnDiedOrDownedThoughtsUtility_AppendThoughts_Relations_Patch
		{
			static bool Prefix(Pawn victim)
			{
				return !(victim is Zombie);
			}
		}

		[HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility))]
		[HarmonyPatch("AppendThoughts_Humanlike")]
		static class PawnDiedOrDownedThoughtsUtility_AppendThoughts_Humanlike_Patch
		{
			static bool Prefix(Pawn victim)
			{
				return !(victim is Zombie);
			}
		}

		[HarmonyPatch(typeof(ImmunityHandler))]
		[HarmonyPatch("ImmunityHandlerTick")]
		static class ImmunityHandler_ImmunityHandlerTick_Patch
		{
			static bool Prefix(ImmunityHandler __instance)
			{
				return !(__instance.pawn is Zombie);
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
				var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
				if (pawn == null || pawn.Map == null) return;

				var grid = Tools.GetGrid(pawn.Map);
				if (pawn is Zombie)
				{
					if (pawn.pather != null)
					{
						var dest = pawn.pather.Destination;
						if (dest != null)
							grid.ChangeZombieCount(dest.Cell, -1);
					}
					grid.ChangeZombieCount(pawn.Position, -1);
					return;
				}

				var id = pawn.ThingID;
				if (id == null) return;

				if (Constants.KILL_CIRCLE_RADIUS_MULTIPLIER > 0)
				{
					var timestamp = grid.Get(pawn.Position).timestamp;
					var radius = Tools.RadiusForPawn(pawn) * Constants.KILL_CIRCLE_RADIUS_MULTIPLIER;
					Tools.GetCircle(radius).Do(vec =>
					{
						var pos = pawn.Position + vec;
						var cell = grid.Get(pos, false);
						if (cell.timestamp > 0 && cell.timestamp <= timestamp)
							grid.SetTimestamp(pos, 0);
					});
				}
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
				var radius = Tools.Boxed((targ.CenterVector3 - origin).magnitude, Constants.MIN_WEAPON_RANGE, Constants.MAX_WEAPON_RANGE);
				var grid = Tools.GetGrid(launcher.Map);
				Tools.GetCircle(radius).Do(vec => grid.SetTimestamp(pos + vec, now - (int)vec.LengthHorizontalSquared));
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
