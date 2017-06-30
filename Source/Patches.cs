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
using Verse.AI.Group;

namespace ZombieLand
{
	class Patches
	{
		// used to prevent zombies from being counted as hostiles
		// both in map exist and for danger music
		//
		static Dictionary<Map, HashSet<IAttackTarget>> playerHostilesWithoutZombies = new Dictionary<Map, HashSet<IAttackTarget>>();

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
				var fadeOff = Tools.PheromoneFadeoff();
				Matrix4x4 matrix = new Matrix4x4();
				Find.VisibleMap.GetGrid().IterateCells((x, z, pheromone) =>
				{
					Vector3 pos = new Vector3(x, Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1), z);
					matrix.SetTRS(pos + new Vector3(0.5f, 0f, 0.5f), Quaternion.identity, new Vector3(1f, 1f, 1f));
					var diff = now - pheromone.timestamp;
					if (diff < fadeOff)
					{
						var a = (double)(fadeOff - diff) * 0.8f / fadeOff;
						var material = SolidColorMaterials.SimpleSolidColorMaterial(new Color(1f, 0f, 0f, (float)a));
						Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
					}
				});
			}
		}

		// patch to remove the constant danger music because of the constant thread of zombies
		//
		[HarmonyPatch(typeof(AttackTargetsCache))]
		[HarmonyPatch("RegisterTarget")]
		static class AttackTargetsCache_RegisterTarget_Patch
		{
			static void Add(IAttackTarget target)
			{
				var thing = target.Thing;
				if (thing == null || thing is Zombie) return;
				if (thing.HostileTo(Faction.OfPlayer) == false) return;
				var map = thing.Map;
				if (map == null) return;
				if (playerHostilesWithoutZombies.ContainsKey(map) == false)
					playerHostilesWithoutZombies.Add(map, new HashSet<IAttackTarget>());
				playerHostilesWithoutZombies[map].Add(target);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var all = instructions.ToList();
				for (int i = 0; i < all.Count - 1; i++)
					yield return all[i];
				var ret = all.Last();

				var method = AccessTools.Method(typeof(AttackTargetsCache_RegisterTarget_Patch), "Add");
				yield return new CodeInstruction(OpCodes.Ldarg_1) { labels = ret.labels };
				yield return new CodeInstruction(OpCodes.Call, method);
				yield return new CodeInstruction(OpCodes.Ret);
			}
		}
		//
		[HarmonyPatch(typeof(AttackTargetsCache))]
		[HarmonyPatch("DeregisterTarget")]
		static class AttackTargetsCache_DeregisterTarget_Patch
		{
			static void Remove(IAttackTarget target)
			{
				var thing = target.Thing;
				if (thing == null || thing is Zombie) return;
				var map = thing.Map;
				if (map == null) return;
				if (playerHostilesWithoutZombies.ContainsKey(map))
					playerHostilesWithoutZombies[map].Remove(target);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var all = instructions.ToList();
				for (int i = 0; i < all.Count - 1; i++)
					yield return all[i];
				var ret = all.Last();

				var method = AccessTools.Method(typeof(AttackTargetsCache_DeregisterTarget_Patch), "Remove");
				yield return new CodeInstruction(OpCodes.Ldarg_1) { labels = ret.labels };
				yield return new CodeInstruction(OpCodes.Call, method);
				yield return new CodeInstruction(OpCodes.Ret);
			}
		}
		//
		[HarmonyPatch(typeof(MusicManagerPlay))]
		[HarmonyPatch("DangerMusicMode", PropertyMethod.Getter)]
		static class MusicManagerPlay_DangerMusicMode_Patch
		{
			static int lastUpdateTick;
			static StoryDanger dangerRatingInt = StoryDanger.None;

			[HarmonyPriority(Priority.First)]
			static bool Prefix(ref bool __result)
			{
				if (ZombieSettings.Values.zombiesTriggerDangerMusic) return true;

				if (Find.TickManager.TicksGame > lastUpdateTick + 101)
				{
					lastUpdateTick = Find.TickManager.TicksGame;

					var maps = Find.Maps;
					for (int i = 0; i < maps.Count; i++)
					{
						var map = maps[i];
						if (map.IsPlayerHome)
						{
							var hostiles = playerHostilesWithoutZombies.ContainsKey(map)
								? playerHostilesWithoutZombies[map]
								: new HashSet<IAttackTarget>();

							int num = hostiles.Count((IAttackTarget x) => !x.ThreatDisabled());
							if (num == 0)
								dangerRatingInt = StoryDanger.None;
							else if (num <= Mathf.CeilToInt((float)map.mapPawns.FreeColonistsSpawnedCount * 0.5f))
								dangerRatingInt = StoryDanger.Low;
							else
							{
								dangerRatingInt = StoryDanger.Low;
								var lastColonistHarmedTick = Traverse.Create(map.dangerWatcher).Field("lastColonistHarmedTick").GetValue<int>();
								if (lastColonistHarmedTick > Find.TickManager.TicksGame - 900)
									dangerRatingInt = StoryDanger.High;
								else
									foreach (Lord current in map.lordManager.lords)
										if (current.CurLordToil is LordToil_AssaultColony)
										{
											dangerRatingInt = StoryDanger.High;
											break;
										}
							}
						}
					}
				}
				__result = dangerRatingInt == StoryDanger.High;
				return false;
			}
		}

		// patch to remove zombies from hostile count so it does not
		// alter game logic (for example when a caravan leaves an enemy base)
		//
		[HarmonyPatch(typeof(GenHostility))]
		[HarmonyPatch("IsActiveThreat")]
		static class GenHostility_IsActiveThreat_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(bool __result, IAttackTarget target)
			{
				if (target is Zombie)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}

		// patch to reduce revenge by animals
		//
		[HarmonyPatch(typeof(Pawn_MindState))]
		[HarmonyPatch("Notify_DamageTaken")]
		static class Pawn_MindState_Notify_DamageTaken_Patch
		{
			static bool ShouldStartManhunting(Pawn instigator)
			{
				if (instigator is Zombie)
					return Rand.Chance(0.15f);
				return true;
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var method = AccessTools.Method(typeof(Pawn_MindState_Notify_DamageTaken_Patch), "ShouldStartManhunting");

				var instr = instructions.ToList();
				for (int i = 1; i < instr.Count; i++)
				{
					if (instr[i - 1].opcode == OpCodes.Bge_Un)
					{
						var jump = new CodeInstruction(OpCodes.Brfalse_S, instr[i - 1].operand);

						if (instr[i].opcode == OpCodes.Ldarg_0)
						{
							var ldstr = instr[i + 1];
							if (ldstr.opcode == OpCodes.Ldstr && (string)ldstr.operand == "AnimalManhunterFromDamage")
							{
								instr.Insert(i++, new CodeInstruction(OpCodes.Ldloc_0));
								instr.Insert(i++, new CodeInstruction(OpCodes.Call, method));
								instr.Insert(i++, jump);
								break;
							}
						}
					}
				}
				for (int i = 0; i < instr.Count; i++)
					yield return instr[i];
			}
		}

		// patch to add a pheromone info section to the rimworld cell inspector
		//
		[HarmonyPatch(typeof(EditWindow_DebugInspector))]
		[HarmonyPatch("CurrentDebugString")]
		public static class EditWindow_DebugInspector_CurrentDebugString_Patch
		{
			static readonly FieldInfo writeCellContentsField = AccessTools.Field(typeof(DebugViewSettings), "writeCellContents");
			static readonly MethodInfo debugGridMethod = AccessTools.Method(typeof(EditWindow_DebugInspector_CurrentDebugString_Patch), "DebugGrid");

			public static int tickedZombies;
			public static int ofTotalZombies;

			static void DebugGrid(StringBuilder builder)
			{
				if (Current.Game == null) return;
				var map = Find.VisibleMap;
				if (map == null) return;

				var tickManager = map.GetComponent<TickManager>();
				var center = Tools.CenterOfInterest(map);
				//var tickString = string.Format("{0:d6} {1:d6} {2:d6}", new object[]
				//{
				//	TickManager_DoSingleTick_Patch.min,
				//	TickManager_DoSingleTick_Patch.average,
				//	TickManager_DoSingleTick_Patch.max
				//});
				//builder.AppendLine("Average tick time: " + tickString);
				builder.AppendLine("Center of Interest: " + center.x + "/" + center.z);
				builder.AppendLine("Total zombie count: " + tickManager.ZombieCount() + " out of " + tickManager.GetMaxZombieCount());
				builder.AppendLine("Ticked zombies: " + tickedZombies + " out of " + ofTotalZombies);
				builder.AppendLine("Days left before Zombies spawn: " + Math.Max(0, ZombieSettings.Values.daysBeforeZombiesCome - GenDate.DaysPassedFloat));
				if (Constants.DEBUGGRID == false) return;

				var pos = UI.MouseCell();
				if (pos.InBounds(map) == false) return;

				pos.GetThingList(map).OfType<Zombie>().Do(zombie =>
				{
					var dest = zombie.pather.Destination.Cell;
					var wanderTo = zombie.wanderDestination;
					builder.AppendLine("Zombie " + zombie.NameStringShort + ": " + zombie.state + " at " + dest.x + "/" + dest.z + " -> " + wanderTo.x + "/" + wanderTo.z);
				});

				var fadeOff = Tools.PheromoneFadeoff();
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
		static class FactionManager_ExposeData_Patch
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
			static MentalStateDef def1 = MentalStateDefOf.Manhunter;
			static MentalStateDef def2 = MentalStateDefOf.ManhunterPermanent;

			static void Prefix(Thing __instance, IntVec3 value)
			{
				var pawn = __instance as Pawn;
				if (pawn == null || pawn.Map == null) return;

				// manhunting will always trigger senses
				//
				if (pawn.MentalState == null || (pawn.MentalState.def != def1 && pawn.MentalState.def != def2))
				{
					if (ZombieSettings.Values.attackMode == AttackMode.OnlyHumans)
						if (pawn.RaceProps.Humanlike == false) return;

					if (ZombieSettings.Values.attackMode == AttackMode.OnlyColonists)
						if (pawn.IsColonist == false) return;
				}

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
						var notOlderThan = currentTicks - Tools.PheromoneFadeoff();
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

		// patch to allow spawning Zombies with debug tools
		//
		[HarmonyPatch(typeof(PawnGenerator))]
		[HarmonyPatch("GenerateNewNakedPawn")]
		static class PawnGenerator_GenerateNewNakedPawn_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(ref PawnGenerationRequest request, ref Pawn __result)
			{
				if (request.Faction == null || request.Faction.def != ZombieDefOf.Zombies) return true;
				__result = ZombieGenerator.GeneratePawn();
				return false;
			}
		}

		// patches to disallow interacting with zombies or zombiecorpses
		//
		[HarmonyPatch(typeof(ReservationManager))]
		[HarmonyPatch("CanReserve")]
		static class ReservationManager_CanReserve_Patch
		{
			[HarmonyPriority(Priority.First)]
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
			[HarmonyPriority(Priority.First)]
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

		// patch so you cannot strip zombies
		//
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch("AnythingToStrip")]
		static class Pawn_AnythingToStrip_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn __instance, ref bool __result)
			{
				if (__instance is Zombie)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}

		// patch to not show forbidden red cross icon on zombies
		//
		[HarmonyPatch(typeof(ForbidUtility))]
		[HarmonyPatch("IsForbidden")]
		[HarmonyPatch(new Type[] { typeof(Thing), typeof(Faction) })]
		static class ForbidUtility_IsForbidden_Patch
		{
			[HarmonyPriority(Priority.First)]
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

		// patch to make zombies appear to be never "down"
		//
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch("Downed", PropertyMethod.Getter)]
		static class Pawn_Downed_Patch
		{
			[HarmonyPriority(Priority.First)]
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
				if (ZombieSettings.Values.useCustomTextures == false) return;

				var zombie = __instance.pawn as Zombie;
				if (zombie == null) return;

				__instance.nakedGraphic = zombie.customBodyGraphic;
				zombie.customBodyGraphic = null;

				__instance.headGraphic = zombie.customHeadGraphic;
				zombie.customHeadGraphic = null;
			}
		}

		// patch for rendering zombies
		//
		[HarmonyPatch(typeof(PawnRenderer))]
		[HarmonyPatch("RenderPawnAt")]
		[HarmonyPatch(new Type[] { typeof(Vector3), typeof(RotDrawMode), typeof(bool) })]
		static class PawnRenderer_RenderPawnAt_Patch
		{
			[HarmonyPriority(Priority.First)]
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
			[HarmonyPriority(Priority.First)]
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
						speed = ZombieSettings.Values.moveSpeedTracking;
					else
						speed = ZombieSettings.Values.moveSpeedIdle;

					float factor;
					switch (zombie.story.bodyType)
					{
						case BodyType.Thin:
							factor = 0.8f;
							break;
						case BodyType.Hulk:
							factor = 0.1f;
							break;
						case BodyType.Fat:
							factor = 0.2f;
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
				var settings = ZombieSettings.Values.damageFactor;
				switch (zombie.story.bodyType)
				{
					case BodyType.Thin:
						__result *= 0.5f * settings;
						break;
					case BodyType.Hulk:
						__result *= 4f * settings;
						break;
					case BodyType.Fat:
						__result *= 2f * settings;
						break;
				}
			}
		}

		// patch for zombies rotting regardless of temperature
		//
		[HarmonyPatch(typeof(Thing))]
		[HarmonyPatch("AmbientTemperature", PropertyMethod.Getter)]
		static class Thing_AmbientTemperature_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Thing __instance, ref float __result)
			{
				if (__instance is Zombie || __instance is ZombieCorpse)
				{
					__result = 21f; // fake normal conditions
					return false;
				}
				return true;
			}
		}

		// patch to set zombie bite injuries as non natural healing to avoid
		// the healing cross mote
		//
		[HarmonyPatch(typeof(HediffUtility))]
		[HarmonyPatch("CanHealNaturally")]
		static class HediffUtility_CanHealNaturally_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Hediff_Injury hd, ref bool __result)
			{
				var zombieBite = hd as Hediff_Injury_ZombieBite;
				if (zombieBite != null)
				{
					var tendDuration = zombieBite.TendDuration;
					if (tendDuration != null)
					{
						var state = tendDuration.GetInfectionState();
						if (state <= InfectionState.BittenNotVisible || state >= InfectionState.Infecting)
						{
							__result = false;
							return false;
						}
					}
				}
				return true;
			}
		}

		// patch to keep zombie bite injuries even after tending if they have to stay around
		//
		[HarmonyPatch(typeof(Hediff))]
		[HarmonyPatch("ShouldRemove", PropertyMethod.Getter)]
		static class HediffUtility_CanHealFromTending_Patch
		{
			[HarmonyPriority(Priority.Last)]
			static void Postfix(Hediff __instance, ref bool __result)
			{
				if (__result == false) return;

				var zombieBite = __instance as Hediff_Injury_ZombieBite;
				if (zombieBite != null)
				{
					var tendDuration = zombieBite.TendDuration;
					if (tendDuration != null)
					{
						var state = tendDuration.GetInfectionState();
						if (state <= InfectionState.BittenNotVisible || state >= InfectionState.Infecting)
							__result = false;
					}
				}
			}
		}

		// patch for variable zombie damage factor
		//
		[HarmonyPatch(typeof(Pawn_HealthTracker))]
		[HarmonyPatch("PainShockThreshold", PropertyMethod.Getter)]
		static class Pawn_HealthTracker_PainShockThreshold_Patch
		{
			static float Replacement(ref Pawn pawn)
			{
				var zombie = pawn as Zombie;
				switch (zombie.story.bodyType)
				{
					case BodyType.Thin:
						return 0.1f;
					case BodyType.Hulk:
						return 0.8f;
					case BodyType.Fat:
						return 0.2f;
				}
				return 0.8f;
			}

			static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
			{
				var conditions = Tools.NotZombieInstructions(generator, method);
				var replacement = AccessTools.Method(MethodBase.GetCurrentMethod().DeclaringType, "Replacement");
				var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method, replacement);
				return transpiler(generator, instructions);
			}
		}

		// patch to remove current job of zombie immediately when killed
		//
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch("Kill")]
		static class Pawn_Kill_Patch
		{
			[HarmonyPriority(Priority.First)]
			static void Prefix(Pawn __instance)
			{
				var zombie = __instance as Zombie;
				if (zombie == null || zombie.jobs == null || zombie.CurJob == null) return;
				zombie.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
			}
		}

		// patch headshot to kill zombies right away
		//
		[HarmonyPatch(typeof(DamageWorker_AddInjury))]
		[HarmonyPatch("IsHeadshot")]
		static class DamageWorker_AddInjury_IsHeadshot_Patch
		{
			static void Postfix(Pawn pawn, bool __result)
			{
				if (__result == false) return;
				var zombie = pawn as Zombie;
				if (zombie != null && zombie.Destroyed == false && zombie.Dead == false)
					zombie.state = ZombieState.ShouldDie;
			}
		}

		// patch for disallowing social interaction with zombies
		//
		[HarmonyPatch(typeof(RelationsUtility))]
		[HarmonyPatch("HasAnySocialMemoryWith")]
		static class RelationsUtility_HasAnySocialMemoryWith_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn p, Pawn otherPawn, ref bool __result)
			{
				if (p is Zombie || otherPawn is Zombie)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}
		[HarmonyPatch(typeof(Pawn_RelationsTracker))]
		[HarmonyPatch("OpinionOf")]
		static class Pawn_RelationsTracker_OpinionOf_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
			{
				var returnZeroLabel = generator.DefineLabel();

				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn_RelationsTracker), "pawn"));
				yield return new CodeInstruction(OpCodes.Isinst, typeof(Zombie));
				yield return new CodeInstruction(OpCodes.Brtrue_S, returnZeroLabel);
				yield return new CodeInstruction(OpCodes.Ldarg_1);
				yield return new CodeInstruction(OpCodes.Isinst, typeof(Zombie));
				yield return new CodeInstruction(OpCodes.Brtrue_S, returnZeroLabel);

				foreach (var instruction in instructions)
					yield return instruction;

				yield return new CodeInstruction(OpCodes.Ldc_I4_0)
				{
					labels = new List<Label> { returnZeroLabel }
				};
				yield return new CodeInstruction(OpCodes.Ret);
			}
		}
		[HarmonyPatch(typeof(RelationsUtility))]
		[HarmonyPatch("PawnsKnowEachOther")]
		static class RelationsUtility_PawnsKnowEachOther_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn p1, Pawn p2, ref bool __result)
			{
				if (p1 is Zombie || p2 is Zombie)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}
		[HarmonyPatch(typeof(ThoughtHandler))]
		[HarmonyPatch("GetSocialThoughts")]
		[HarmonyPatch(new Type[] { typeof(Pawn), typeof(List<ISocialThought>) })]
		static class ThoughtHandler_GetSocialThoughts_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(ThoughtHandler __instance, Pawn otherPawn, List<ISocialThought> outThoughts)
			{
				if (otherPawn is Zombie || __instance.pawn is Zombie)
				{
					outThoughts.Clear();
					return false;
				}
				return true;
			}
		}
		[HarmonyPatch(typeof(SituationalThoughtHandler))]
		[HarmonyPatch("AppendSocialThoughts")]
		static class SituationalThoughtHandler_AppendSocialThoughts_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(SituationalThoughtHandler __instance, Pawn otherPawn)
			{
				return !(otherPawn is Zombie || __instance.pawn is Zombie);
			}
		}
		[HarmonyPatch(typeof(Corpse))]
		[HarmonyPatch("GiveObservedThought")]
		static class Corpse_GiveObservedThought_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Corpse __instance)
			{
				return !(__instance is ZombieCorpse);
			}
		}

		// patch for disallowing thoughts on zombies
		//
		[HarmonyPatch(typeof(ThoughtUtility))]
		[HarmonyPatch("CanGetThought")]
		static class ThoughtUtility_CanGetThought_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn pawn, ref bool __result)
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
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Thing t)
			{
				return (t as ZombieCorpse == null);
			}
		}

		// patches to prevent interaction with zombies
		//
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
				if (def == null || def.IsCorpse == false) return;
				if (def.ingestible == null) return;
				if (def.ingestible.sourceDef is ThingDef_Zombie)
				{
					def.selectable = false;
					def.neverMultiSelect = true;
					def.drawGUIOverlay = false;
					def.hasTooltip = false;
					def.hideAtSnowDepth = 99f;
					def.inspectorTabs = new List<Type>();
					def.passability = Traversability.Standable;
					def.affectsRegions = false;
					def.stackLimit = 1;
					def.thingClass = typeof(ZombieCorpse);
				}
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
					radius /= ZombieSettings.Values.zombieInstinct.HalfToDoubleValue();
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
		[HarmonyPatch(typeof(PawnComponentsUtility))]
		[HarmonyPatch("RemoveComponentsOnKilled")]
		static class PawnComponentsUtility_RemoveComponentsOnKilled_Patch
		{
			static void Postfix(Pawn pawn)
			{
				if (pawn.Map == null) return;

				if (pawn is Zombie)
				{
					var grid = pawn.Map.GetGrid();
					if (pawn.pather != null)
					{
						var dest = pawn.pather.Destination;
						if (dest != null && dest != pawn.Position)
							grid.ChangeZombieCount(dest.Cell, -1);
					}
					grid.ChangeZombieCount(pawn.Position, -1);
					return;
				}

				if (Constants.KILL_CIRCLE_RADIUS_MULTIPLIER > 0)
				{
					var grid = pawn.Map.GetGrid();
					var timestamp = grid.Get(pawn.Position).timestamp;
					var radius = Tools.RadiusForPawn(pawn) * Constants.KILL_CIRCLE_RADIUS_MULTIPLIER;
					radius /= ZombieSettings.Values.zombieInstinct.HalfToDoubleValue();
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

		// patch to prevent thoughts on zombies
		//
		[HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility))]
		[HarmonyPatch("TryGiveThoughts")]
		[HarmonyPatch(new Type[] { typeof(Pawn), typeof(DamageInfo?), typeof(PawnDiedOrDownedThoughtsKind) })]
		static class PawnDiedOrDownedThoughtsUtility_TryGiveThoughts_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn victim)
			{
				return !(victim is Zombie);
			}
		}

		// patch to remove immunity ticks on zombies
		//
		[HarmonyPatch(typeof(ImmunityHandler))]
		[HarmonyPatch("ImmunityHandlerTick")]
		static class ImmunityHandler_ImmunityHandlerTick_Patch
		{
			[HarmonyPriority(Priority.First)]
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
		public static class Projectile_Launch_Patch
		{
			static void Postfix(Thing launcher, Vector3 origin, LocalTargetInfo targ)
			{
				var pawn = launcher as Pawn;
				if (pawn == null) return;

				var noiseScale = 1f;
				if (pawn.equipment.PrimaryEq != null)
					noiseScale = pawn.equipment.PrimaryEq.PrimaryVerb.verbProps.muzzleFlashScale / Constants.BASE_MUZZLE_FLASH_VALUE;

				var now = Tools.Ticks();
				var pos = origin.ToIntVec3();
				var magnitude = (targ.CenterVector3 - origin).magnitude * noiseScale * Math.Min(1f, ZombieSettings.Values.zombieInstinct.HalfToDoubleValue());
				var radius = Tools.Boxed(magnitude, Constants.MIN_WEAPON_RANGE, Constants.MAX_WEAPON_RANGE);
				var grid = launcher.Map.GetGrid();
				Tools.GetCircle(radius).Do(vec => grid.SetTimestamp(pos + vec, now - vec.LengthHorizontalSquared));
			}

			private static float GetDistanceTraveled(float velocity, float angle, float shotHeight)
			{
				if (shotHeight < 0.001f)
					return (Mathf.Pow(velocity, 2f) / 9.8f) * Mathf.Sin(2f * angle);
				return ((velocity * Mathf.Cos(angle)) / 9.8f) * (velocity * Mathf.Sin(angle) + Mathf.Sqrt(Mathf.Pow(velocity * Mathf.Sin(angle), 2f) + 2f * 9.8f * shotHeight));
			}

			static void PostfixCombatExtended(Thing launcher, Vector2 origin, float shotAngle, float shotRotation, float shotHeight = 0f, float shotSpeed = -1f, Thing equipment = null)
			{
				var pawn = launcher as Pawn;
				if (pawn == null) return;

				var noiseScale = 1f;
				if (pawn.equipment.PrimaryEq != null)
					noiseScale = pawn.equipment.PrimaryEq.PrimaryVerb.verbProps.muzzleFlashScale / Constants.BASE_MUZZLE_FLASH_VALUE;

				var now = Tools.Ticks();
				var pos = new IntVec3(origin);
				var delta = GetDistanceTraveled(shotSpeed, shotAngle, shotHeight);
				var magnitude = noiseScale * delta * Math.Min(1f, ZombieSettings.Values.zombieInstinct.HalfToDoubleValue());
				var radius = Tools.Boxed(magnitude, Constants.MIN_WEAPON_RANGE, Constants.MAX_WEAPON_RANGE);
				var grid = launcher.Map.GetGrid();
				Tools.GetCircle(radius).Do(vec => grid.SetTimestamp(pos + vec, now - vec.LengthHorizontalSquared));
			}

			public static void PatchCombatExtended(HarmonyInstance harmony)
			{
				var type = AccessTools.TypeByName("CombatExtended.ProjectileCE");
				if (type == null) return;
				var originalMethodInfo = AccessTools.Method(type, "Launch", new Type[] { typeof(Thing), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(float), typeof(Thing) });
				if (originalMethodInfo == null) return;

				var postfix = new HarmonyMethod(AccessTools.Method(typeof(Projectile_Launch_Patch), "PostfixCombatExtended"));
				harmony.Patch(originalMethodInfo, new HarmonyMethod(null), postfix);
			}
		}

		// patch to allow zombies to occupy the same spot without collision
		// 
		[HarmonyPatch(typeof(PawnCollisionTweenerUtility))]
		[HarmonyPatch("PawnCollisionPosOffsetFor")]
		static class PawnCollisionTweenerUtility_PawnCollisionPosOffsetFor_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn pawn, ref Vector3 __result)
			{
				if (!(pawn is Zombie)) return true;
				__result = Vector3.zero;
				return false;
			}
		}

		// patches so that zombies do not have needs
		// 
		[HarmonyPatch(typeof(Pawn_NeedsTracker))]
		[HarmonyPatch("AllNeeds", PropertyMethod.Getter)]
		static class Pawn_NeedsTracker_AllNeeds_Patch
		{
			static List<Need> Replacement()
			{
				return new List<Need>();
			}

			static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
			{
				var conditions = Tools.NotZombieInstructions(generator, method);
				var replacement = AccessTools.Method(MethodBase.GetCurrentMethod().DeclaringType, "Replacement");
				var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method, replacement);
				return transpiler(generator, instructions);
			}
		}
		[HarmonyPatch(typeof(Pawn_NeedsTracker))]
		[HarmonyPatch("AddOrRemoveNeedsAsAppropriate")]
		static class Pawn_NeedsTracker_AddOrRemoveNeedsAsAppropriate_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
			{
				var conditions = Tools.NotZombieInstructions(generator, method);
				var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
				return transpiler(generator, instructions);
			}
		}

		// patches so that zombies have no records
		//
		[HarmonyPatch(typeof(Pawn_RecordsTracker))]
		[HarmonyPatch("AddTo")]
		static class Pawn_RecordsTracker_AddTo_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
			{
				var conditions = Tools.NotZombieInstructions(generator, method);
				var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
				return transpiler(generator, instructions);
			}
		}
		[HarmonyPatch(typeof(Pawn_RecordsTracker))]
		[HarmonyPatch("Increment")]
		static class Pawn_RecordsTracker_Increment_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
			{
				var conditions = Tools.NotZombieInstructions(generator, method);
				var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
				return transpiler(generator, instructions);
			}
		}

		// patch to insert our settings page
		//
		[HarmonyPatch(typeof(Scenario))]
		[HarmonyPatch("GetFirstConfigPage")]
		static class Scenario_GetFirstConfigPage_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var selectLandingSiteConstructor = AccessTools.Constructor(typeof(Page_SelectLandingSite));

				var dialogConstructor = AccessTools.Constructor(typeof(SettingsDialog));
				var addMethod = AccessTools.Method(typeof(List<Page>), "Add");

				foreach (var instruction in instructions)
				{
					if (instruction.operand == selectLandingSiteConstructor)
					{
						yield return new CodeInstruction(OpCodes.Newobj, dialogConstructor);
						yield return new CodeInstruction(OpCodes.Callvirt, addMethod);
						yield return new CodeInstruction(OpCodes.Ldloc_0);
					}
					yield return instruction;
				}
			}
		}

		// patch to avoid null reference exception
		//
		[HarmonyPatch(typeof(ThoughtWorker_ColonistLeftUnburied))]
		[HarmonyPatch("CurrentStateInternal")]
		static class ThoughtWorker_ColonistLeftUnburied_CurrentStateInternal_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var get_InnerPawn = AccessTools.Method(typeof(Corpse), "get_InnerPawn");
				CodeInstruction prevInstruction = null;
				Label label = new Label();
				foreach (var instruction in instructions)
				{
					if (instruction.opcode == OpCodes.Callvirt)
						if (instruction.operand == get_InnerPawn)
						{
							yield return instruction;
							yield return new CodeInstruction(OpCodes.Brfalse_S, label);

							yield return prevInstruction;
						}

					if (instruction.opcode == OpCodes.Ble_Un_S)
						label = (Label)instruction.operand;

					yield return instruction;
					prevInstruction = instruction;
				}
			}
		}
	}
}