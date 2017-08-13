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

				// debug zombie counts
				Find.VisibleMap.GetGrid().IterateCells((x, z, cell) =>
				{
					var pos = new Vector3(x, Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1), z);
					if (cell.zombieCount > 1)
					{
						var a = Math.Min(0.9f, 0.2f * (cell.zombieCount - 1));
						Tools.DebugPosition(pos, new Color(0f, 0f, 1f, a));
					}
				});

				// debug timestamps
				var fadeOff = Tools.PheromoneFadeoff();
				var now = Tools.Ticks();
				Find.VisibleMap.GetGrid().IterateCells((x, z, cell) =>
				{
					var pos = new Vector3(x, Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1), z);
					var diff = now - cell.timestamp;
					if (diff < fadeOff)
					{
						var a = (fadeOff - diff) * 0.5f / fadeOff;
						Tools.DebugPosition(pos, new Color(1f, 0f, 0f, a));
					}
				});
			}
		}

		// patch for debugging: show zombie avoidance grid
		//
		[HarmonyPatch(typeof(MapInterface))]
		[HarmonyPatch("MapInterfaceUpdate")]
		class MapInterface_MapInterfaceUpdate_Patch
		{
			static void Postfix()
			{
				if (DebugViewSettings.writePathCosts == false) return;

				var map = Find.VisibleMap;
				var avoidGrid = map.GetComponent<TickManager>().avoidGrid;

				var currentViewRect = Find.CameraDriver.CurrentViewRect;
				currentViewRect.ClipInsideMap(map);
				foreach (var c in currentViewRect)
				{
					var cost = avoidGrid.GetCosts()[c.x + c.z * map.Size.x];
					if (cost > 0)
						Tools.DebugPosition(c.ToVector3(), new Color(1f, 0f, 0f, GenMath.LerpDouble(0, 10000, 0.4f, 1f, cost)));
				}
			}
		}

		// patch for debugging: show zombie count around mouse
		//
		[HarmonyPatch(typeof(MapInterface))]
		[HarmonyPatch("MapInterfaceOnGUI_AfterMainTabs")]
		class MapInterface_MapInterfaceOnGUI_AfterMainTabs_Patch
		{
			static void Postfix()
			{
				if (DebugViewSettings.writePathCosts == false) return;
				if (Event.current.type != EventType.Repaint) return;

				var map = Find.VisibleMap;
				if (map == null) return;
				var grid = map.GetGrid();
				var basePos = UI.MouseCell();
				Tools.GetCircle(4).Select(vec => vec + basePos).Do(cell =>
				{
					var n = grid.GetZombieCount(cell);
					var v = GenMapUI.LabelDrawPosFor(cell);
					GenMapUI.DrawThingLabel(v, n.ToStringCached(), Color.white);
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
				for (var i = 0; i < all.Count - 1; i++)
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
				for (var i = 0; i < all.Count - 1; i++)
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
					for (var i = 0; i < maps.Count; i++)
					{
						var map = maps[i];
						if (map.IsPlayerHome)
						{
							var hostiles = playerHostilesWithoutZombies.ContainsKey(map)
								? playerHostilesWithoutZombies[map]
								: new HashSet<IAttackTarget>();

							var num = hostiles.Count((IAttackTarget x) => !x.ThreatDisabled());
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
									foreach (var current in map.lordManager.lords)
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

		// patch to increase hit chance for shooting at zombies
		//
		[HarmonyPatch(typeof(Verb_LaunchProjectile))]
		[HarmonyPatch("TryCastShot")]
		static class Verb_LaunchProjectile_TryCastShot_Patch
		{
			static int hardDef = DifficultyDefOf.Hard.difficulty;

			static bool SkipMissingShotsAtZombies(Verb verb, LocalTargetInfo currentTarget)
			{
				// difficulty Intense or worse will trigger default behavior
				if (Find.Storyteller.difficulty.difficulty >= hardDef) return false;

				// only for colonists
				var colonist = verb.caster as Pawn;
				if (colonist == null || colonist.Faction != Faction.OfPlayer) return false;

				// shooting zombies
				var zombie = currentTarget.HasThing ? currentTarget.Thing as Zombie : null;
				if (zombie == null) return false;

				// max 25 cells awaw
				if ((zombie.Position - colonist.Position).LengthHorizontalSquared > 625) return false;

				// with line of sight
				var shot = verb as Verb_LaunchProjectile;
				if (shot == null || shot.verbProps.requireLineOfSight == false) return false;

				// skip miss calculations
				return Rand.Chance(Constants.COLONISTS_HIT_ZOMBIES_CHANCE);
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
			{
				var m_SkipMissingShotsAtZombies = AccessTools.Method(typeof(Verb_LaunchProjectile_TryCastShot_Patch), "SkipMissingShotsAtZombies");
				var f_forcedMissRadius = AccessTools.Field(typeof(VerbProperties), "forcedMissRadius");
				var m_HitReportFor = AccessTools.Method(typeof(ShotReport), "HitReportFor");
				var f_currentTarget = AccessTools.Field(typeof(Verb), "currentTarget");

				var skipLabel = generator.DefineLabel();

				var inList = instructions.ToList();

				var idx1 = inList.FirstIndexOf(instr => instr.opcode == OpCodes.Ldfld && instr.operand == f_forcedMissRadius);
				var idx2 = inList.FindLastIndex(instr => instr.opcode == OpCodes.Call
					&& (instr.operand as MethodInfo)?.DeclaringType == typeof(ShotReport)
					&& (instr.operand as MethodInfo)?.ReturnType == typeof(float)
				);
				if (idx1 > 0)
				{
					var jump = inList[idx2 + 1];
					if (jump.opcode == OpCodes.Ble_Un)
					{
						idx1 -= 2;
						inList.Insert(idx1++, new CodeInstruction(OpCodes.Ldarg_0));
						inList.Insert(idx1++, new CodeInstruction(OpCodes.Ldarg_0));
						inList.Insert(idx1++, new CodeInstruction(OpCodes.Ldfld, f_currentTarget));
						inList.Insert(idx1++, new CodeInstruction(OpCodes.Call, m_SkipMissingShotsAtZombies));
						inList.Insert(idx1++, new CodeInstruction(OpCodes.Brtrue, jump.operand));
					}
					else
						Log.Error("No call on ShotReport method returning float in Verb_LaunchProjectile.TryCastShot");
				}
				else
					Log.Error("No ldfld forcedMissRadius in Verb_LaunchProjectile.TryCastShot");

				foreach (var instruction in inList)
					yield return instruction;
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
				for (var i = 1; i < instr.Count; i++)
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
				for (var i = 0; i < instr.Count; i++)
					yield return instr[i];
			}
		}

		// patch to let predators prefer humans for zombies
		//
		[HarmonyPatch(typeof(FoodUtility))]
		[HarmonyPatch("GetPreyScoreFor")]
		public static class FoodUtility_GetPreyScoreFor_Patch
		{
			static void Postfix(Pawn predator, Pawn prey, ref float __result)
			{
				if (prey is Zombie)
					__result -= 35f;
			}
		}

		// patch for pather to avoid zombies
		/* 
			# if (allowedArea != null && !allowedArea[num14])
			# {
			# 	num16 += 600;
			# }
			# // start added code
			# num16 += GetZombieCosts(this.map, num14);
			# // end added code

			IL_098a: ldloc.s 14
			IL_098c: brfalse IL_09a9

			IL_0991: ldloc.s 14
		-4	IL_0993: ldloc.s 36														<-- local var that holds grid index
			IL_0995: callvirt instance bool Verse.Area::get_Item(int32)
			IL_099a: brtrue IL_09a9

		-1	IL_099f: ldloc.s 40														<-- local var that holds sum
		0	IL_09a1: ldc.i4 600														<-- search for "600" to get this as main reference point
			IL_09a6: add
			IL_09a7: stloc.s 40 

		3	IL_09a9:	ldloc.s 40														<-- added code (keep labels from before)
						ldarg.0
						ldfld PathFinder::map
						ldloc.s 36
						call PathFinder_FindPath_Patch::GetZombieCosts(Map map, int idx)
						add
						stloc.s 40
			*/
		//
		[HarmonyPatch(typeof(PathFinder))]
		[HarmonyPatch("FindPath")]
		[HarmonyPatch(new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathEndMode) })]
		public static class PathFinder_FindPath_Patch
		{
			public static MethodInfo zombieCostMethod = AccessTools.Method(typeof(PathFinder_FindPath_Patch), "GetZombieCosts");
			static Dictionary<Map, TickManager> tickManagerCache = new Dictionary<Map, TickManager>();

			static int GetZombieCosts(Map map, int idx)
			{
				if (ZombieSettings.Values.betterZombieAvoidance == false) return 0;

				TickManager tickManager;
				if (tickManagerCache.TryGetValue(map, out tickManager) == false)
				{
					tickManager = map.GetComponent<TickManager>();
					tickManagerCache[map] = tickManager;
				}
				return tickManager.avoidGrid.GetCosts()[idx];
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var list = instructions.ToList();
				var refIdx = list.FirstIndexOf(ins => ins.operand is int && (int)ins.operand == 600);
				if (refIdx > 0)
				{
					var gridIdx = list[refIdx - 4].operand;
					var sumIdx = list[refIdx - 1].operand;
					var insertIdx = refIdx + 3;
					var movedLabels = list[insertIdx].labels;
					list[insertIdx].labels = new List<Label>();

					list.Insert(insertIdx++, new CodeInstruction(OpCodes.Ldloc_S, sumIdx) { labels = movedLabels });
					list.Insert(insertIdx++, new CodeInstruction(OpCodes.Ldarg_0));
					list.Insert(insertIdx++, new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(PathFinder), "map")));
					list.Insert(insertIdx++, new CodeInstruction(OpCodes.Ldloc_S, gridIdx));
					list.Insert(insertIdx++, new CodeInstruction(OpCodes.Call, zombieCostMethod));
					list.Insert(insertIdx++, new CodeInstruction(OpCodes.Add));
					list.Insert(insertIdx++, new CodeInstruction(OpCodes.Stloc_S, sumIdx));
				}
				else
					Log.Error("Cannot find path cost 600 in PathFinder.FindPath");

				foreach (var instr in list)
					yield return instr;
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
				var pos = UI.MouseCell();

				var tickManager = map.GetComponent<TickManager>();
				var center = Tools.CenterOfInterest(map);
				builder.AppendLine("Center of Interest: " + tickManager.centerOfInterest.x + "/" + tickManager.centerOfInterest.z);
				builder.AppendLine("Total zombie count: " + tickManager.ZombieCount() + " out of " + tickManager.GetMaxZombieCount());
				builder.AppendLine("Ticked zombies: " + tickedZombies + " out of " + ofTotalZombies);
				builder.AppendLine("Days left before Zombies spawn: " + Math.Max(0, ZombieSettings.Values.daysBeforeZombiesCome - GenDate.DaysPassedFloat));
				if (pos.InBounds(map))
				{
					var avoidGrid = map.GetComponent<TickManager>().avoidGrid;
					builder.AppendLine("Avoid cost: " + avoidGrid.GetCosts()[pos.x + pos.z * map.Size.x]);
				}

				if (pos.InBounds(map) == false) return;

				var cell = map.GetGrid().GetPheromone(pos, false);
				if (cell != null)
				{
					var realZombieCount = pos.GetThingList(map).OfType<Zombie>().Count();
					var timestampDiff = Tools.Ticks() - cell.timestamp;

					var sb = new StringBuilder();
					sb.Append("Zombie grid: " + cell.zombieCount + " zombies");
					if (cell.zombieCount != realZombieCount) sb.Append(" (real " + realZombieCount + ")");
					sb.Append(", timestamp " + (timestampDiff > 0 ? "+" + timestampDiff : "" + timestampDiff));
					builder.AppendLine(sb.ToString());
				}
				else
					builder.AppendLine(pos.x + " " + pos.z + ": empty");

				var gridSum = GenAdj.AdjacentCellsAndInside.Select(vec => pos + vec)
					.Where(c => c.InBounds(map))
					.Select(c => map.GetGrid().GetZombieCount(c))
					.Sum();
				var realSum = GenAdj.AdjacentCellsAndInside.Select(vec => pos + vec)
					.Where(c => c.InBounds(map))
					.Select(c => map.thingGrid.ThingsListAtFast(c).OfType<Zombie>().Count())
					.Sum();
				builder.AppendLine("Rage factor: grid=" + gridSum + ", real=" + realSum);

				map.thingGrid.ThingsListAtFast(pos).OfType<Zombie>().Do(zombie =>
				{
					var currPos = zombie.Position;
					var gotoPos = zombie.pather.Moving ? zombie.pather.Destination.Cell : IntVec3.Invalid;
					var wanderTo = zombie.wanderDestination;
					var sb = new StringBuilder();
					sb.Append("Zombie " + zombie.NameStringShort + " at " + currPos.x + "," + currPos.z);
					sb.Append(", " + zombie.state.ToString().ToLower());
					if (zombie.raging > 0) sb.Append(", raging ");
					sb.Append(", going to " + gotoPos.x + "," + gotoPos.z);
					sb.Append(" (wander dest " + wanderTo.x + "," + wanderTo.z + ")");
					builder.AppendLine(sb.ToString());
				});
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var previousPopInstruction = false;
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
						var rel1 = new FactionRelation()
						{
							other = faction,
							goodwill = 0f,
							hostile = true
						};
						Traverse.Create(zombies).Field("relations").GetValue<List<FactionRelation>>().Add(rel1);

						var rel2 = new FactionRelation()
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
				if (pawn.Position == value) return;

				var zombie = pawn as Zombie;
				if (zombie != null)
				{
					var grid = pawn.Map.GetGrid();
					var newCell = grid.GetPheromone(value, false);
					if (newCell != null && newCell.zombieCount > 0)
					{
						newCell.timestamp -= newCell.zombieCount * Constants.ZOMBIE_CLOGGING_FACTOR;
						var notOlderThan = Tools.Ticks() - Tools.PheromoneFadeoff();
						if (newCell.timestamp < notOlderThan)
							newCell.timestamp = notOlderThan;
					}
				}
				else
				{
					// manhunting will always trigger senses
					//
					if (pawn.MentalState == null || (pawn.MentalState.def != def1 && pawn.MentalState.def != def2))
					{
						if (ZombieSettings.Values.attackMode == AttackMode.OnlyHumans)
							if (pawn.RaceProps.Humanlike == false) return;

						if (ZombieSettings.Values.attackMode == AttackMode.OnlyColonists)
							if (pawn.IsColonist == false) return;
					}

					if (Tools.HasInfectionState(pawn, InfectionState.Infecting) == false)
					{
						var now = Tools.Ticks();
						var radius = Tools.RadiusForPawn(pawn);
						var grid = pawn.Map.GetGrid();
						Tools.GetCircle(radius).Do(vec => grid.SetTimestamp(value + vec, now - (long)(2f * vec.LengthHorizontal)));
					}
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
					var zombie = target.Thing as Zombie;
					if ((zombie != null && zombie.wasColonist == false) || target.Thing is ZombieCorpse)
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

		// patch to keep shooting even if a zombie is down (only
		// if "double tap" is on)
		//
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch("ThreatDisabled")]
		static class Pawn_ThreatDisabled_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn __instance, ref bool __result)
			{
				var zombie = __instance as Zombie;
				if (zombie == null) return true;
				__result = !zombie.Spawned;
				return false;
			}
		}
		[HarmonyPatch]
		static class Toils_Combat_FollowAndMeleeAttack_KillIncappedTarget_Patch
		{
			static bool IncappedTargetCheck(Job curJob, Pawn target)
			{
				if (target is Zombie) return true;
				return curJob.killIncappedTarget;
			}

			static MethodBase TargetMethod()
			{
				var inner = AccessTools.FirstInner(typeof(Toils_Combat), type => type.Name.StartsWith("<FollowAndMeleeAttack"));
				return inner.GetMethods(AccessTools.all).FirstOrDefault(m => m.Name.StartsWith("<>m__"));
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var f_killIncappedTarget = AccessTools.Field(typeof(Job), "killIncappedTarget");
				var m_IncappedTargetCheck = AccessTools.Method(typeof(Toils_Combat_FollowAndMeleeAttack_KillIncappedTarget_Patch), "IncappedTargetCheck");
				var m_get_Downed = AccessTools.Method(typeof(Pawn), "get_Downed");

				CodeInstruction last = null;
				CodeInstruction localPawnInstruction = null;
				foreach (var instruction in instructions)
				{
					if (instruction.opcode == OpCodes.Callvirt && instruction.operand == m_get_Downed)
						localPawnInstruction = new CodeInstruction(last);

					if (instruction.opcode == OpCodes.Ldfld
						&& instruction.operand == f_killIncappedTarget
						&& localPawnInstruction != null)
					{
						yield return localPawnInstruction;

						instruction.opcode = OpCodes.Call;
						instruction.operand = m_IncappedTargetCheck;
					}
					yield return instruction;
					last = instruction;
				}
			}
		}
		[HarmonyPatch(typeof(Stance_Warmup))]
		[HarmonyPatch("StanceTick")]
		static class Stance_Warmup_StanceTick_Patch
		{
			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				return Tools.DownedReplacer(instructions);
			}
		}
		[HarmonyPatch]
		static class Toils_Jump_JumpIfTargetDownedDistant_Patch
		{
			static MethodBase TargetMethod()
			{
				var inner = AccessTools.FirstInner(typeof(Toils_Jump), type => type.Name.StartsWith("<JumpIfTargetDownedDistant>c__"));
				return inner.GetMethods(AccessTools.all).FirstOrDefault(m => m.Name.StartsWith("<>m__"));
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				return Tools.DownedReplacer(instructions);
			}
		}
		[HarmonyPatch(typeof(Pawn_MindState))]
		[HarmonyPatch("MeleeThreatStillThreat", PropertyMethod.Getter)]
		static class Pawn_MindState_MeleeThreatStillThreat_Patch
		{
			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				return Tools.DownedReplacer(instructions);
			}
		}
		[HarmonyPatch(typeof(JobDriver_Wait))]
		[HarmonyPatch("CheckForAutoAttack")]
		static class JobDriver_Wait_CheckForAutoAttack_Patch
		{
			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				return Tools.DownedReplacer(instructions, 1);
			}
		}
		[HarmonyPatch]
		static class JobDriver_AttackStatic_TickAction_Patch
		{
			static MethodBase TargetMethod()
			{
				var inner = AccessTools.FirstInner(typeof(JobDriver_AttackStatic), type => type.Name.StartsWith("<MakeNewToils>c__Iterator"));
				return inner.GetMethods(AccessTools.all)
					.Where(m => m.Name.StartsWith("<>m__"))
					.OrderBy(m => m.Name)
					.LastOrDefault(); // the second one is the tickAction
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				return Tools.DownedReplacer(instructions);
			}
		}
		[HarmonyPatch(typeof(TargetingParameters))]
		[HarmonyPatch("CanTarget")]
		static class TargetingParameters_CanTarget_Patch
		{
			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				return Tools.DownedReplacer(instructions);
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

				if (zombie.customBodyGraphic != null)
					__instance.nakedGraphic = zombie.customBodyGraphic;
				zombie.customBodyGraphic = null;

				if (zombie.customHeadGraphic != null)
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
			static float moteAltitute = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead);
			static Vector3 leftEyeOffset = new Vector3(-0.092f, 0f, -0.08f);
			static Vector3 rightEyeOffset = new Vector3(0.092f, 0f, -0.08f);

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

			static void Postfix(PawnRenderer __instance, Vector3 drawLoc)
			{
				var zombie = __instance.graphics.pawn as Zombie;
				if (zombie == null || zombie.raging == 0) return;
				if (zombie.GetPosture() != PawnPosture.Standing) return;

				drawLoc.y = moteAltitute;
				var quickHeadCenter = drawLoc + new Vector3(0, 0, 0.35f);

				if (Find.CameraDriver.CurrentZoom <= CameraZoomRange.Middle)
				{
					var tm = Find.TickManager;
					var blinkPeriod = 60 + zombie.thingIDNumber % 180; // between 2-5s
					var eyesOpen = (tm.TicksGame % blinkPeriod) > 3;
					if (eyesOpen || tm.CurTimeSpeed == TimeSpeed.Paused)
					{
						// the following constant comes from PawnRenderer.RenderPawnInternal
						var loc = drawLoc + __instance.BaseHeadOffsetAt(zombie.Rotation) + new Vector3(0, 0.0281250011f, 0);

						// not clear why 75 but it seems to fit
						var eyeX = zombie.sideEyeOffset.x / 75f;
						var eyeZ = zombie.sideEyeOffset.z / 75f;

						if (zombie.Rotation == Rot4.West)
							GraphicToolbox.DrawScaledMesh(MeshPool.plane05, Constants.RAGE_EYE, loc + new Vector3(-eyeX, 0, eyeZ), Quaternion.identity, 0.5f, 0.5f);

						else if (zombie.Rotation == Rot4.East)
							GraphicToolbox.DrawScaledMesh(MeshPool.plane05, Constants.RAGE_EYE, loc + new Vector3(eyeX, 0, eyeZ), Quaternion.identity, 0.5f, 0.5f);

						if (zombie.Rotation == Rot4.South)
						{
							GraphicToolbox.DrawScaledMesh(MeshPool.plane05, Constants.RAGE_EYE, quickHeadCenter + leftEyeOffset, Quaternion.identity, 0.5f, 0.5f);
							GraphicToolbox.DrawScaledMesh(MeshPool.plane05, Constants.RAGE_EYE, quickHeadCenter + rightEyeOffset, Quaternion.identity, 0.5f, 0.5f);
						}
					}
				}

				if (zombie.Rotation == Rot4.West) quickHeadCenter.x -= 0.09f;
				if (zombie.Rotation == Rot4.East) quickHeadCenter.x += 0.09f;
				GraphicToolbox.DrawScaledMesh(MeshPool.plane20, Constants.RAGE_AURAS[Find.CameraDriver.CurrentZoom], quickHeadCenter, Quaternion.identity, 1f, 1f);
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
					if (zombie.state == ZombieState.Tracking || zombie.raging > 0)
						__result = Constants.ZOMBIE_HIT_CHANCE_TRACKING;
					else
						__result = Constants.ZOMBIE_HIT_CHANCE_IDLE;
					return false;
				}

				if (stat == StatDefOf.MoveSpeed)
				{
					float speed;
					if (zombie.state == ZombieState.Tracking || zombie.raging > 0)
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
					if (zombie.wasColonist)
						__result *= 2f;
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
				if (zombie.wasColonist)
					__result *= 10f;
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
						if (state == InfectionState.BittenNotVisible || state >= InfectionState.Infecting)
						{
							__result = false;
							return false;
						}
					}
				}
				return true;
			}
		}

		// patch to allow amputation of biten body parts
		//
		[HarmonyPatch]
		static class Recipe_RemoveBodyPart_GetPartsToApplyOn_Patch
		{
			static MethodBase TargetMethod()
			{
				var type = AccessTools.TypeByName("RimWorld.Recipe_RemoveBodyPart");
				return AccessTools.Method(type, "GetPartsToApplyOn");
			}

			static void Postfix(Pawn pawn, RecipeDef recipe, ref IEnumerable<BodyPartRecord> __result)
			{
				if (recipe != RecipeDefOf.RemoveBodyPart)
					return;

				__result = pawn.health.hediffSet
					.GetHediffs<Hediff_Injury_ZombieBite>()
					.Select(bite => bite.Part)
					.Union(__result);
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
						if (state == InfectionState.BittenNotVisible || state >= InfectionState.Infecting)
							__result = false;
					}
				}
			}
		}

		// patch for variable zombie shock resistance
		//
		[HarmonyPatch(typeof(Pawn_HealthTracker))]
		[HarmonyPatch("PainShockThreshold", PropertyMethod.Getter)]
		static class Pawn_HealthTracker_PainShockThreshold_Patch
		{
			static float Replacement(ref Pawn pawn)
			{
				var zombie = pawn as Zombie;
				if (zombie.wasColonist || zombie.raging > 0)
					return 1000f;
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
				if (zombie != null && zombie.Spawned && zombie.Dead == false && zombie.raging == 0 && zombie.wasColonist == false)
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

		// patch to colorize the label of zombies that were colonists
		//
		[HarmonyPatch(typeof(PawnNameColorUtility))]
		[HarmonyPatch("PawnNameColorOf")]
		static class PawnNameColorUtility_PawnNameColorOf_Patch
		{
			static Color zombieLabelColor = new Color(0.7f, 1f, 0.7f);

			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn pawn, ref Color __result)
			{
				var zombie = pawn as Zombie;
				if (zombie != null && zombie.wasColonist)
				{
					__result = zombieLabelColor;
					return false;
				}
				return true;
			}
		}

		// allow clicks on zombies that were colonists
		//
		[HarmonyPatch(typeof(ThingSelectionUtility))]
		[HarmonyPatch("SelectableByMapClick")]
		static class ThingSelectionUtility_SelectableByMapClick_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Thing t, ref bool __result)
			{
				var zombie = t as Zombie;
				if (zombie != null && zombie.wasColonist)
				{
					__result = true;
					return false;
				}
				return true;
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
					var timestamp = grid.GetTimestamp(pawn.Position);
					if (timestamp > 0)
					{
						var radius = Tools.RadiusForPawn(pawn) * Constants.KILL_CIRCLE_RADIUS_MULTIPLIER;
						radius /= ZombieSettings.Values.zombieInstinct.HalfToDoubleValue();
						Tools.GetCircle(radius).Do(vec =>
						{
							var pos = pawn.Position + vec;
							var cell = grid.GetPheromone(pos, false);
							if (cell != null && cell.timestamp > 0 && cell.timestamp <= timestamp)
								cell.timestamp = 0;
						});
					}
				}
				grid.SetTimestamp(pawn.Position, 0);
			}
		}

		// patch to update twinkie graphics
		//
		[HarmonyPatch(typeof(Game))]
		[HarmonyPatch("FinalizeInit")]
		static class Game_FinalizeInit_Patch
		{
			static void Postfix(Game __instance)
			{
				Tools.EnableTwinkie(ZombieSettings.Values.replaceTwinkie);
			}
		}

		// patches to update our zombie count grid
		//
		[HarmonyPatch(typeof(Map))]
		[HarmonyPatch("FinalizeLoading")]
		static class Map_FinalizeLoading_Patch
		{
			static void Prefix(Map __instance)
			{
				var grid = __instance.GetGrid();
				grid.IterateCellsQuick(cell => cell.zombieCount = 0);
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
				if (pawn is Zombie || pawn.Map == null) return;

				if (Constants.KILL_CIRCLE_RADIUS_MULTIPLIER > 0)
				{
					var grid = pawn.Map.GetGrid();
					var timestamp = grid.GetTimestamp(pawn.Position);
					var radius = Tools.RadiusForPawn(pawn) * Constants.KILL_CIRCLE_RADIUS_MULTIPLIER;
					radius /= ZombieSettings.Values.zombieInstinct.HalfToDoubleValue();
					Tools.GetCircle(radius).Do(vec =>
					{
						var pos = pawn.Position + vec;
						var cell = grid.GetPheromone(pos, false);
						if (cell != null && cell.timestamp > 0 && cell.timestamp <= timestamp)
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
				var label = new Label();
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

		// patch to add our settings to the main bottom-right menu
		//
		[HarmonyPatch(typeof(MainTabWindow_Menu))]
		[HarmonyPatch("RequestedTabSize", PropertyMethod.Getter)]
		static class MainTabWindow_Menu_RequestedTabSize_Path
		{
			static void Postfix(ref Vector2 __result)
			{
				__result.y += MainMenuDrawer_DoMainMenuControls_Path.addedHeight;
			}
		}
		[HarmonyPatch(typeof(MainTabWindow_Menu))]
		[HarmonyPatch("DoWindowContents")]
		static class MainTabWindow_Menu_DoWindowContents_Path
		{
			static void Prefix(ref Rect rect)
			{
				rect.height += MainMenuDrawer_DoMainMenuControls_Path.addedHeight;
			}
		}
		[HarmonyPatch(typeof(Widgets))]
		[HarmonyPatch("ButtonText")]
		[HarmonyPatch(new Type[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(Color), typeof(bool) })]
		static class Widgets_DoWindowContents_Path
		{
			static void NewDrawAtlas(Rect rect, Texture2D atlas, string label)
			{
				Widgets.DrawAtlas(rect, atlas);
				if (label == "Zombieland")
				{
					var texture = Tools.GetZombieButtonBackground();
					GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true, 0f);
				}
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var from = AccessTools.Method(typeof(Widgets), "DrawAtlas", new Type[] { typeof(Rect), typeof(Texture2D) });
				var to = AccessTools.Method(typeof(Widgets_DoWindowContents_Path), "NewDrawAtlas");
				foreach (var instruction in instructions)
				{
					if (instruction.operand == from)
					{
						instruction.operand = to;
						yield return new CodeInstruction(OpCodes.Ldarg_1);
					}
					yield return instruction;
				}
			}
		}
		[HarmonyPatch(typeof(MainMenuDrawer))]
		[HarmonyPatch("DoMainMenuControls")]
		static class MainMenuDrawer_DoMainMenuControls_Path
		{
			public static float addedHeight = 45f + 7f; // default height ListableOption + OptionListingUtility.DrawOptionListing spacing
			static Dialog_ModSettings modDialog = new Dialog_ModSettings();
			static Vector2 closeButSize = Traverse.Create(modDialog).Field("CloseButSize").GetValue<Vector2>();

			static float DrawOptionListingPatch1(Rect rect, List<ListableOption> optList)
			{
				if (Current.ProgramState == ProgramState.Playing)
				{
					var label = "Options".Translate();
					var idx = optList.FirstIndexOf(opt => opt.label == label);
					if (idx > 0) optList.Insert(idx, new ListableOption("Zombieland", delegate
					{
						var dialog = new Dialog_ModSettings();
						var me = LoadedModManager.GetMod<ZombielandMod>();
						Traverse.Create(dialog).Field("selMod").SetValue(me);
						Find.WindowStack.Add(dialog);
					}, null));
				}
				return OptionListingUtility.DrawOptionListing(rect, optList);
			}

			static float DrawOptionListingPatch2(Rect rect, List<ListableOption> optList)
			{
				if (Current.ProgramState == ProgramState.Playing)
				{
					var item = new ListableOption_WebLink("Brrainz", "http://patreon.com/pardeike", Tools.GetMenuIcon());
					optList.Add(item);
				}
				return OptionListingUtility.DrawOptionListing(rect, optList);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var m_DrawOptionListing = AccessTools.Method(typeof(OptionListingUtility), "DrawOptionListing");
				var counter = 0;
				foreach (var instruction in instructions)
				{
					if (instruction.operand == m_DrawOptionListing)
					{
						counter++;
						instruction.operand = AccessTools.Method(typeof(MainMenuDrawer_DoMainMenuControls_Path), "DrawOptionListingPatch" + counter);
					}
					yield return instruction;
				}
			}
		}
	}
}