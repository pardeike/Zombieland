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
	class ZombielandMod : Mod
	{
		public ZombielandMod(ModContentPack content) : base(content)
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
				Find.VisibleMap.GetGrid().IterateCells((x, z, pheromone) =>
				{
					Vector3 pos = new Vector3(x, Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1), z);
					Matrix4x4 matrix = new Matrix4x4();
					matrix.SetTRS(pos + new Vector3(0.5f, 0f, 0.5f), Quaternion.identity, new Vector3(1f, 1f, 1f));
					var diff = now - pheromone.timestamp;
					var fadeOff = 1000L * GenTicks.SecondsToTicks(Constants.PHEROMONE_FADEOFF);
					if (diff < fadeOff)
					{
						var a = (fadeOff - diff) / (float)fadeOff * 0.8f;
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
		public static class EditWindow_DebugInspector_CurrentDebugString_Patch
		{
			static FieldInfo writeCellContentsField = AccessTools.Field(typeof(DebugViewSettings), "writeCellContents");
			static MethodInfo debugGridMethod = AccessTools.Method(typeof(EditWindow_DebugInspector_CurrentDebugString_Patch), "DebugGrid");
			static Traverse allGraphicsField = Traverse.Create(typeof(GraphicDatabase)).Field("allGraphics");

			public static int tickedZombies = 0;
			public static int ofTotalZombies = 0;

			static void DebugGrid(StringBuilder builder)
			{
				if (Current.Game == null) return;
				var map = Find.VisibleMap;
				if (map == null) return;

				var tickManager = map.TickManager();
				builder.AppendLine("Center of Interest: " + tickManager.centerOfInterest.x + "/" + tickManager.centerOfInterest.z);
				builder.AppendLine("Total zombie count: " + tickManager.ZombieCount() + " out of " + tickManager.GetMaxZombieCount(false));
				builder.AppendLine("Ticked zombies: " + tickedZombies + " out of " + ofTotalZombies);
				builder.AppendLine("Days left before Zombies spawn: " + Math.Max(0, Constants.DAYS_BEFORE_ZOMBIES_SPAWN - GenDate.DaysPassedFloat));
				if (Constants.DEBUGGRID == false) return;

				var allGraphics = allGraphicsField.GetValue<Dictionary<GraphicRequest, Graphic>>();
				var zombieGraphics = allGraphics.Where(
				  pair => pair.Key.path.StartsWith("Zombie")).ToList();
				builder.AppendLine("Total graphics loaded: " + allGraphics.Count + " (" + zombieGraphics.Count + ")");

				var pos = UI.MouseCell();
				if (pos.InBounds(map) == false) return;

				pos.GetThingList(map).OfType<Zombie>().Do(zombie =>
				{
					var dest = zombie.pather.Destination.Cell;
					builder.AppendLine("Zombie " + zombie.NameStringShort + ": " + zombie.state + " at " + dest.x + "/" + dest.z);
				});

				var fadeOff = 1000L * GenTicks.SecondsToTicks(Constants.PHEROMONE_FADEOFF);
				GenAdj.AdjacentCellsAndInside
					.Select(cell => pos + cell)
					.Where(cell => cell.InBounds(map))
					.Do(loc =>
					{
						var cell = map.GetGrid().Get(loc, false);
						if (cell.timestamp > 0)
						{
							var now = Tools.Ticks();
							var diff = now - cell.timestamp;
							var realZombieCount = loc.GetThingList(map).OfType<Zombie>().Count();
							builder.AppendLine(loc.x + " " + loc.z + ": " + cell.zombieCount + "z (" + realZombieCount + "z), "
								+ cell.timestamp + (diff < fadeOff ? (", +" + diff) : ""));
						}
						else
							builder.AppendLine(loc.x + " " + loc.z + ": empty");
					});
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

		// patch for adding zombie faction to existing games
		//
		[HarmonyPatch(typeof(FactionManager))]
		[HarmonyPatch("ExposeData")]
		static class FactionManager_AllFactions_Patch
		{
			static void Postfix(FactionManager __instance)
			{
				var factions = Traverse.Create(__instance).Field("allFactions").GetValue<List<Faction>>();
				var factionDefs = factions.Select(f => f.def).ToList();
				if (factionDefs.Contains(ZombieDefOf.Zombies) == false)
				{
					var zombies = FactionGenerator.NewGeneratedFaction(ZombieDefOf.Zombies);
					foreach (var faction in factions)
					{
						FactionRelation rel1 = new FactionRelation()
						{
							other = faction,
							goodwill = 0f,
							hostile = true
						};
						Traverse.Create(zombies).Field("relations").GetValue<List<FactionRelation>>().Add(rel1);

						FactionRelation rel2 = new FactionRelation()
						{
							other = zombies,
							goodwill = 0f,
							hostile = true
						};
						Traverse.Create(faction).Field("relations").GetValue<List<FactionRelation>>().Add(rel2);

					}
					factions.Add(zombies);
				}
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

				var grid = pawn.Map.GetGrid();
				if (pawn is Zombie)
				{
					grid.ChangeZombieCount(pos, -1);
					var newPos = grid.Get(value);
					if (newPos.zombieCount > 0)
					{
						newPos.timestamp -= newPos.zombieCount * Constants.ZOMBIE_CLOGGING_FACTOR;
						var currentTicks = Tools.Ticks();
						var notOlderThan = currentTicks - 1000L * GenTicks.SecondsToTicks(Constants.PHEROMONE_FADEOFF);
						if (newPos.timestamp < notOlderThan)
							newPos.timestamp = notOlderThan;
					}
				}
				else
				{
					var now = Tools.Ticks();
					var radius = Tools.RadiusForPawn(pawn);
					Tools.GetCircle(radius).Do(vec => grid.SetTimestamp(value + vec, now - (long)(2f * vec.LengthHorizontal)));
				}
			}
		}

		// patch to add TickManager to new maps
		//
		[HarmonyPatch(typeof(Scenario))]
		[HarmonyPatch("PostMapGenerate")]
		static class Scenario_PostMapGenerate_Patch
		{
			static void Postfix(Map map)
			{
				map.TickManager().Initialize();
			}
		}

		// patch to add TickManager to loading maps
		//
		[HarmonyPatch(typeof(Map))]
		[HarmonyPatch("FinalizeInit")]
		static class Map_FinalizeLoading_Patch
		{
			static void Postfix(Map __instance)
			{
				__instance.TickManager().Initialize();
			}
		}

		// patch to allow spawning Zombies with debug tools
		//
		[HarmonyPatch(typeof(PawnGenerator))]
		[HarmonyPatch("TryGenerateNewNakedPawn")]
		static class PawnGenerator_TryGenerateNewNakedPawn_Patch
		{
			static bool Prefix(ref PawnGenerationRequest request, ref Pawn __result)
			{
				if (request.Faction == null || request.Faction.def != ZombieDefOf.Zombies) return true;
				__result = ZombieGenerator.GeneratePawn();
				return false;
			}
		}

		// patch to disallow interacting with zombies or zombiecorpses
		//
		[HarmonyPatch(typeof(ReservationManager))]
		[HarmonyPatch("CanReserve")]
		static class ReservationManager_CanReserve_Patch
		{
			static bool Prefix(LocalTargetInfo target, ref bool __result)
			{
				if (target.HasThing)
				{
					if (target.Thing is Zombie || target.Thing is ZombieCorpse)
					{
						__result = false;
						return false;
					}
				}
				return true;
			}
		}
		[HarmonyPatch(typeof(ReservationManager))]
		[HarmonyPatch("Reserve")]
		static class ReservationManager_Reserve_Patch
		{
			static bool Prefix(LocalTargetInfo target, ref bool __result)
			{
				if (target.HasThing)
				{
					if (target.Thing is Zombie || target.Thing is ZombieCorpse)
					{
						__result = false;
						return false;
					}
				}
				return true;
			}
		}

		[HarmonyPatch(typeof(ForbidUtility))]
		[HarmonyPatch("IsForbidden")]
		[HarmonyPatch(new Type[] { typeof(Thing), typeof(Faction) })]
		static class ForbidUtility_IsForbidden_Patch
		{
			static bool Prefix(Thing t, ref bool __result)
			{
				if (t is Zombie || t is ZombieCorpse)
				{
					__result = true;
					return false;
				}
				return true;
			}
		}


		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch("Downed", PropertyMethod.Getter)]
		static class Pawn_Downed_Patch
		{
			static bool Prefix(Pawn __instance, ref bool __result)
			{
				var zombie = __instance as Zombie;
				if (zombie == null) return true;
				__result = false;
				return false;
			}
		}

		// patch for custom zombie graphic parts
		//
		[HarmonyPatch(typeof(PawnGraphicSet))]
		[HarmonyPatch("ResolveAllGraphics")]
		static class PawnGraphicSet_ResolveAllGraphics_Patch
		{
			static void Postfix(PawnGraphicSet __instance)
			{
				var zombie = __instance.pawn as Zombie;
				if (zombie == null) return;
				if (Constants.USE_CUSTOM_TEXTURES == false) return;

				// TODO: find correct value, for now we use 0
				var renderPrecedence = 0;

				var bodyPath = "Zombie/Naked_" + zombie.story.bodyType.ToString();
				var bodyRequest = new GraphicRequest(typeof(VariableGraphic), bodyPath, ShaderDatabase.CutoutSkin, Vector2.one, Color.white, Color.white, null, renderPrecedence);
				var bodyGraphic = Activator.CreateInstance<VariableGraphic>();
				bodyGraphic.Init(bodyRequest);
				__instance.nakedGraphic = bodyGraphic;

				var headPath = zombie.story.HeadGraphicPath;
				var sep = headPath.LastIndexOf('/');
				headPath = "Zombie" + headPath.Substring(sep);
				var headRequest = new GraphicRequest(typeof(VariableGraphic), headPath, ShaderDatabase.CutoutSkin, Vector2.one, Color.white, Color.white, null, renderPrecedence);
				var headGraphic = Activator.CreateInstance<VariableGraphic>();
				headGraphic.Init(headRequest);
				__instance.headGraphic = headGraphic;
			}
		}

		// patch for rendering zombies
		//
		[HarmonyPatch(typeof(PawnRenderer))]
		[HarmonyPatch("RenderPawnAt")]
		[HarmonyPatch(new Type[] { typeof(Vector3), typeof(RotDrawMode), typeof(bool) })]
		static class PawnRenderer_RenderPawnAt_Patch
		{
			static bool Prefix(PawnRenderer __instance, Vector3 drawLoc, RotDrawMode bodyDrawType)
			{
				var zombie = __instance.graphics.pawn as Zombie;
				if (zombie == null) return true;

				if (zombie.state == ZombieState.Emerging)
				{
					zombie.Render(__instance, drawLoc, bodyDrawType);
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
				if (zombie == null) return true;

				if (stat == StatDefOf.MeleeHitChance)
				{
					if (zombie.state == ZombieState.Tracking)
						__result = Constants.ZOMBIE_HIT_CHANCE_TRACKING;
					else
						__result = Constants.ZOMBIE_HIT_CHANCE_IDLE;
					return false;
				}

				if (stat == StatDefOf.MoveSpeed)
				{
					float speed;
					if (zombie.state == ZombieState.Tracking)
						speed = Constants.ZOMBIE_MOVE_SPEED_TRACKING;
					else
						speed = Constants.ZOMBIE_MOVE_SPEED_IDLE;

					float factor;
					switch (zombie.story.bodyType)
					{
						case BodyType.Thin:
							factor = 2f;
							break;
						case BodyType.Hulk:
							factor = 0.25f;
							break;
						case BodyType.Fat:
							factor = 0.5f;
							break;
						default:
							factor = 1f;
							break;
					}

					__result = speed * factor;
					return false;
				}

				return true;
			}
		}

		// patch for variable zombie damage factor
		//
		[HarmonyPatch(typeof(Verb))]
		[HarmonyPatch("GetDamageFactorFor")]
		static class Verb_GetDamageFactorFor_Patch
		{
			static void Postfix(Pawn pawn, ref float __result)
			{
				var zombie = pawn as Zombie;
				if (zombie == null) return;
				switch (zombie.story.bodyType)
				{
					case BodyType.Thin:
						__result *= 0.5f;
						break;
					case BodyType.Hulk:
						__result *= 4f;
						break;
					case BodyType.Fat:
						__result *= 2f;
						break;
				}
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
		[HarmonyPatch(typeof(ThoughtUtility))]
		[HarmonyPatch("CanGetThought")]
		static class ThoughtUtility_CanGetThought_Patch
		{
			static bool Prefix(Pawn pawn, ThoughtDef def, ref bool __result)
			{
				if (pawn is Zombie)
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
				return (t as ZombieCorpse == null);
			}
		}

		[HarmonyPatch(typeof(Pawn_InteractionsTracker))]
		[HarmonyPatch("TryInteractWith")]
		static class Pawn_InteractionsTracker_TryInteractWith_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
			{
				var conditions = new List<CodeInstruction>();
				conditions.AddRange(Tools.NotZombieInstructions(generator, method));
				conditions.AddRange(Tools.NotZombieInstructions(generator, method, "recipient"));
				var transpiler = Tools.GenerateReplacementCallTranspiler(conditions);
				return transpiler(generator, instructions);
			}
		}

		[HarmonyPatch(typeof(Pawn_InteractionsTracker))]
		[HarmonyPatch("InteractionsTrackerTick")]
		static class Pawn_InteractionsTracker_InteractionsTrackerTick_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
			{
				var conditions = Tools.NotZombieInstructions(generator, method);
				var transpiler = Tools.GenerateReplacementCallTranspiler(conditions);
				return transpiler(generator, instructions);
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
					def.affectsRegions = false;
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
				if (pawn == null || pawn.Map == null) return;

				var grid = pawn.Map.GetGrid();
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

		// patch to handle targets deaths so that we update our grid
		//
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch("Kill")]
		static class Pawn_Kill_Patch
		{
			static void Postfix(Pawn __instance)
			{
				if (__instance.Map == null) return;

				var grid = __instance.Map.GetGrid();
				if (__instance is Zombie)
				{
					if (__instance.pather != null)
					{
						var dest = __instance.pather.Destination;
						if (dest != null && dest != __instance.Position)
							grid.ChangeZombieCount(dest.Cell, -1);
					}
					grid.ChangeZombieCount(__instance.Position, -1);
					return;
				}

				var id = __instance.ThingID;
				if (id == null) return;

				if (Constants.KILL_CIRCLE_RADIUS_MULTIPLIER > 0)
				{
					var timestamp = grid.Get(__instance.Position).timestamp;
					var radius = Tools.RadiusForPawn(__instance) * Constants.KILL_CIRCLE_RADIUS_MULTIPLIER;
					Tools.GetCircle(radius).Do(vec =>
					{
						var pos = __instance.Position + vec;
						var cell = grid.Get(pos, false);
						if (cell.timestamp > 0 && cell.timestamp <= timestamp)
							grid.SetTimestamp(pos, 0);
					});
				}
			}
		}

		[HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility))]
		[HarmonyPatch("TryGiveThoughts")]
		[HarmonyPatch(new Type[] { typeof(Pawn), typeof(DamageInfo?), typeof(PawnDiedOrDownedThoughtsKind) })]
		static class PawnDiedOrDownedThoughtsUtility_TryGiveThoughts_Patch
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
				var grid = launcher.Map.GetGrid();
				Tools.GetCircle(radius).Do(vec => grid.SetTimestamp(pos + vec, now - (int)vec.LengthHorizontalSquared));
			}
		}
	}
}