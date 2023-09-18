﻿using Brrainz;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	public class BombVest : Apparel { }
	public class TankySuit : Apparel { }
	public class StickyGoo : Filth { }

	[StaticConstructorOnStartup]
	static class Patches
	{
		static readonly List<string> errors = new();

		static Patches()
		{
			var harmony = new Harmony("net.pardeike.zombieland");
			errors = new List<string>();
			try
			{
				harmony.PatchAll();
			}
			catch (Exception ex)
			{
				var error = ex.ToString();
				Log.Error(error);
				var idx = error.IndexOf("\n  at");
				if (idx > 0)
					errors.Insert(0, error.Substring(0, idx));
			}

			// prepare Twinkie
			LongEventHandler.QueueLongEvent(() => { Tools.EnableTwinkie(false); }, "", true, null);

			// patches for other mods (need to run late or else statics in those classes are not set yet)
			LongEventHandler.ExecuteWhenFinished(() =>
			{
				CETools.Init(harmony);
				AlienTools.Init();
				Customization.Init();
			});

			// for debugging
			//
			//DebugRimworldMethodCalls((Type type) =>
			//{
			//	if (type.Name.Contains("AttackTarget")) return true;
			//	if (type.Name.Contains("_AI")) return true;
			//	if (type.Name.Contains("Reachability")) return true;
			//	return false;
			//});

			CrossPromotion.Install(76561197973010050);
		}

		public static void Error(string error)
		{
			errors.Add(error);
			Log.Error(error);
		}

		// settings backwards compatibility
		//
		[HarmonyPatch(typeof(ParseHelper))]
		[HarmonyPatch(nameof(ParseHelper.FromString))]
		[HarmonyPatch(new[] { typeof(string), typeof(Type) })]
		static class ParseHelper_FromString_Patch
		{
			[HarmonyPriority(Priority.First)]
			static void Prefix(ref string str, Type itemType)
			{
				if (itemType == typeof(AreaRiskMode))
				{
					if (str == "IfInside")
						str = nameof(AreaRiskMode.ColonistInside);
					if (str == "IfOutside")
						str = nameof(AreaRiskMode.ColonistOutside);
				}
			}
		}

		[HarmonyPatch(typeof(MainMenuDrawer))]
		[HarmonyPatch(nameof(MainMenuDrawer.Init))]
		static class MainMenuDrawer_Init_Patch
		{
			static void Postfix()
			{
				if (errors.Any())
				{
					LongEventHandler.ExecuteWhenFinished(() =>
					{
						var message = errors.Join(error => error, "\n\n");
						errors.Clear();
						Find.WindowStack?.Add(new Dialog_ErrorMessage($"Zombieland encountered an unexpected error and might not work as expected. Either RimWorld has been updated or you have a mod conflict:\n\n{message}"));
					});
				}
			}
		}

		// patch for debugging: show pheromone grid as overlay
		//
		[HarmonyPatch(typeof(SelectionDrawer))]
		[HarmonyPatch(nameof(SelectionDrawer.DrawSelectionOverlays))]
		static class SelectionDrawer_DrawSelectionOverlays_Patch
		{
			static readonly float pawnAltitude = Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1);

			static void Postfix()
			{
				if (Constants.SHOW_PHEROMONE_GRID == false)
					return;

				// debug zombie counts
				Find.CurrentMap.GetGrid().IterateCells((x, z, cell) =>
				{
					var pos = new Vector3(x, pawnAltitude, z);
					if (cell.zombieCount > 1)
					{
						var a = Math.Min(0.9f, 0.2f * (cell.zombieCount - 1));
						Tools.DebugPosition(pos, new Color(0f, 0f, 1f, a));
					}
				});

				// debug timestamps
				var fadeOff = Tools.PheromoneFadeoff();
				var now = Tools.Ticks();
				Find.CurrentMap.GetGrid().IterateCells((x, z, cell) =>
				{
					var pos = new Vector3(x, pawnAltitude, z);
					var diff = now - cell.timestamp;
					if (diff >= -fadeOff && diff < 0)
					{
						var a = GenMath.LerpDouble(-fadeOff, 0, 0.8f, 0.5f, diff);
						Tools.DebugPosition(pos, new Color(1f, 1f, 0f, a));
					}
					else if (diff < fadeOff)
					{
						var a = GenMath.LerpDouble(0, fadeOff, 0.5f, 0.0f, diff);
						Tools.DebugPosition(pos, new Color(1f, 0f, 0f, a));
					}
				});
			}
		}

		// patch for debugging: show zombie avoidance grid
		//
		[HarmonyPatch(typeof(MapInterface))]
		[HarmonyPatch(nameof(MapInterface.MapInterfaceUpdate))]
		[StaticConstructorOnStartup]
		class MapInterface_MapInterfaceUpdate_Patch
		{
			static void Postfix()
			{
				var map = Find.CurrentMap;
				var currentViewRect = Find.CameraDriver.CurrentViewRect;
				currentViewRect.ClipInsideMap(map);

				if (ContaminationManager.Instance.showContaminationOverlay)
				{
					if (Find.CameraDriver.CurrentViewRect.Area >= Constants.MAX_CELLS_FOR_DETAILED_CONTAMINATION)
						map.ContaminationGridUpdate();
					else
					{
						map.listerThings.AllThings
							.DoIf(thing =>
							{
								if (thing is Mineable)
									return false;
								var cell = thing.Position;
								return currentViewRect.Contains(cell) && cell.Fogged(map) == false;
							},
							thing => GraphicToolbox.DrawContamination(thing.DrawPos, thing.GetContamination(), true));
						var grid = map.GetContamination();
						currentViewRect.DoIf(cell => cell.Fogged(map) == false, cell => GraphicToolbox.DrawContamination(cell.ToVector3Shifted(), grid[cell], false));
					}
				}

				if (Constants.SHOW_PLAYER_REACHABLE_REGIONS)
				{
					var m = DebugSolidColorMats.MaterialOf(Color.magenta);
					Tools.PlayerReachableRegions(Find.CurrentMap).SelectMany(r => r.Cells).Do(c => CellRenderer.RenderSpot(c.ToVector3Shifted(), m, 0.25f));
				}

				if (Constants.SHOW_AVOIDANCE_GRID && Tools.ShouldAvoidZombies())
				{
					var tickManager = map.GetComponent<TickManager>();
					if (tickManager == null)
						return;
					var avoidGrid = tickManager.avoidGrid;
					foreach (var c in currentViewRect)
					{
						var cost = avoidGrid.GetCosts()[c.x + c.z * map.Size.x];
						if (cost > 0)
							Tools.DebugPosition(c.ToVector3(), new Color(0f, 1f, 0f, GenMath.LerpDouble(0, 10000, 0.4f, 1f, cost)));
					}
				}

				if (Constants.SHOW_WANDER_REGIONS)
				{
					var pathing = map?.GetComponent<TickManager>()?.zombiePathing;
					if (pathing == null)
						return;
					var cell = UI.MouseCell();
					if (cell.InBounds(map))
					{
						var region = map.regionGrid.GetRegionAt_NoRebuild_InvalidAllowed(cell);
						if (region != null)
						{
							if (pathing.backpointingRegionsIndices.TryGetValue(region, out var idx))
							{
								if (idx != -1)
								{
									var r1 = pathing.backpointingRegions[idx].region;
									GenDraw.DrawFieldEdges(r1.Cells.ToList(), new Color(1f, 1f, 0f, 0.25f), null);
									idx = pathing.backpointingRegions[idx].parentIdx;
									if (idx != -1)
									{
										var r2 = pathing.backpointingRegions[idx].region;
										GenDraw.DrawFieldEdges(r2.Cells.ToList(), new Color(1f, 1f, 0f, 0.75f), null);
										cell = pathing.backpointingRegions[idx].cell;
										var m = DebugSolidColorMats.MaterialOf(Color.yellow);
										CellRenderer.RenderSpot(cell.ToVector3Shifted(), m, 0.5f);
									}
								}
							}
						}
					}
				}
			}
		}

		// patch for debugging: show zombie pathing grid around the mouse
		//
		[HarmonyPatch(typeof(MapInterface))]
		[HarmonyPatch(nameof(MapInterface.MapInterfaceOnGUI_AfterMainTabs))]
		class MapInterface_MapInterfaceOnGUI_AfterMainTabs_Patch
		{
			static void Postfix()
			{
				if (Constants.SHOW_NORMAL_PATHING_GRID == false && Constants.SHOW_DIRECT_PATHING_GRID == false)
					return;
				if (Event.current.type != EventType.Repaint)
					return;

				var map = Find.CurrentMap;
				if (map == null)
					return;
				var basePos = UI.MouseCell();
				var info = ZombieWanderer.GetMapInfo(map);

				void DrawGrid(bool ignoreBuildings, Color color, Vector2 offset)
				{
					var noneColor = new Color(1f, 0, 0, 0.5f);
					Tools.GetCircle(4).Select(vec => vec + basePos).Do(cell =>
					{
						var labelVec = GenMapUI.LabelDrawPosFor(cell) + offset;
						var newPos = info.GetParent(cell, ignoreBuildings);
						if (newPos.IsValid == false)
						{
							GenMapUI.DrawThingLabel(labelVec, "⁜", noneColor);
							return;
						}

						var d = newPos - cell;
						var n = (d.x + 1) + (d.z + 1) * 3;
						var arrow = "↙↓↘←◌→↖↑↗".Substring(n, 1);
						GenMapUI.DrawThingLabel(labelVec, arrow, color);
					});
				}

				if (Constants.SHOW_NORMAL_PATHING_GRID)
					DrawGrid(false, Color.white, new Vector2(0, -5));
				if (Constants.SHOW_DIRECT_PATHING_GRID)
					DrawGrid(true, Color.yellow, new Vector2(0, 5));
			}
		}

		// patch to show zombieland version and total number of zombies
		//
		[HarmonyPatch(typeof(GlobalControlsUtility))]
		[HarmonyPatch(nameof(GlobalControlsUtility.DoDate))]
		class GlobalControlsUtility_DoDate_Patch
		{
			static Color percentageBackground = new(1, 1, 1, 0.1f);

			static void Postfix(float leftX, float width, ref float curBaseY)
			{
				var map = Find.CurrentMap;
				if (map == null)
					return;
				if (Find.CurrentMap.IsBlacklisted())
					return;

				const float rightMargin = 7f;
				if (ZombieSettings.Values.showZombieStats)
				{
					var tickManager = map.GetComponent<TickManager>();
					if (tickManager == null)
						return;
					var count = tickManager.ZombieCount();
					if (count > 0)
					{
						var zombieCountString = count + " Zombies";

						var zlRect = new Rect(leftX, curBaseY - 24f, width, 24f);
						Text.Font = GameFont.Small;
						var len = Text.CalcSize(zombieCountString);
						zlRect.xMin = zlRect.xMax - Math.Min(leftX, len.x + rightMargin);

						GUI.BeginGroup(zlRect);
						Text.Anchor = TextAnchor.UpperRight;
						var rect = zlRect.AtZero();
						rect.xMax -= rightMargin;
						var percentRect = rect;
						percentRect.width *= ZombieTicker.PercentTicking;
						percentRect.xMin -= 2;
						percentRect.xMax += 2;
						percentRect.yMax -= 3;
						Widgets.DrawRectFast(percentRect, percentageBackground);
						Widgets.Label(rect, zombieCountString);
						Text.Anchor = TextAnchor.UpperLeft;
						GUI.EndGroup();

						TooltipHandler.TipRegion(zlRect, new TipSignal(delegate
						{
							var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
							return $"Zombieland v{currentVersion.ToString(4)}";
						}, 99799));
						if (Mouse.IsOver(zlRect) && tickManager.allZombiesCached.Count <= 100)
							tickManager.allZombiesCached.Do(zombie => TargetHighlighter.Highlight(new GlobalTargetInfo(zombie), true, false, false));

						curBaseY -= zlRect.height;
					}

					if (ZombieSettings.Values.useDynamicThreatLevel)
					{
						static string Format(float f1, float f2)
						{
							var n1 = Mathf.FloorToInt(f1 * 100);
							var n2 = Mathf.FloorToInt(f2 * 100);
							if (n1 == n2)
								return string.Format("{0:D0}%", n1) + " " + "ThreatLevel".Translate();
							return string.Format("{0:D0}-{1:D0}%", n1, n2) + " " + "ThreatLevel".Translate();
						}

						var zombieWeather = map.GetComponent<ZombieWeather>();
						var (f1, f2) = zombieWeather.GetFactorRangeFor();
						var zombieWeatherString = Format(f1, f2);
						var zlRect = new Rect(leftX, curBaseY - 24f, width, 24f);
						Text.Font = GameFont.Small;
						var len = Text.CalcSize(zombieWeatherString);
						zlRect.xMin = zlRect.xMax - Math.Min(leftX, len.x + rightMargin);

						var over = Mouse.IsOver(zlRect);
						if (over)
						{
							var r = zlRect;
							r.xMin -= 10;
							Widgets.DrawHighlight(r);
						}

						GUI.BeginGroup(zlRect);
						Text.Anchor = TextAnchor.UpperRight;
						var rect = zlRect.AtZero();
						rect.xMax -= rightMargin;
						Widgets.Label(rect, zombieWeatherString);
						Text.Anchor = TextAnchor.UpperLeft;
						GUI.EndGroup();

						if (over)
						{
							var winWidth = 720;
							var winHeight = 320;
							var bgRect = new Rect(zlRect.xMin - 10 - winWidth, zlRect.yMax - winHeight, winWidth, winHeight);
							Find.WindowStack.ImmediateWindow(564534346, bgRect, WindowLayer.Super, ZombieWeather.GenerateTooltipDrawer(bgRect.AtZero()), false, false, 1f);
						}

						curBaseY -= zlRect.height;
					}
				}
			}
		}

		// custom ticking
		//
		[HarmonyPatch(typeof(Verse.TickManager))]
		[HarmonyPatch(nameof(Verse.TickManager.TickManagerUpdate))]
		static class Verse_TickManager_TickManagerUpdate_Patch
		{
			static void Prefix(Verse.TickManager __instance)
			{
				_ = ZombieWanderer.processor.MoveNext();
				if (Find.TickManager.Paused)
					return;

				ZombieTicker.zombiesTicked = 0;
				ZombieTicker.managers = Find.Maps.Select(map => map.GetComponent<TickManager>()).OfType<TickManager>();

				var curTimePerTick = __instance.CurTimePerTick;
				var realTimeToTickThrough = __instance.realTimeToTickThrough;
				if (Mathf.Abs(Time.deltaTime - curTimePerTick) < curTimePerTick * 0.1f)
					realTimeToTickThrough += curTimePerTick;
				else
					realTimeToTickThrough += Time.deltaTime;

				var n1 = realTimeToTickThrough / curTimePerTick;
				var n2 = __instance.TickRateMultiplier * 2f;
				var loopEstimate = Mathf.FloorToInt(Mathf.Min(n1, n2));

				ZombieTicker.maxTicking = Mathf.FloorToInt(loopEstimate * ZombieTicker.managers.Sum(tm => tm.allZombiesCached.Count(zombie => zombie.Spawned && zombie.Dead == false)));
				ZombieTicker.currentTicking = Mathf.FloorToInt(ZombieTicker.maxTicking * ZombieTicker.PercentTicking);
			}

			static void Postfix(Verse.TickManager __instance)
			{
				if (__instance.Paused)
					return;

				var ticked = ZombieTicker.zombiesTicked;
				var current = ZombieTicker.currentTicking;
				var newPercentZombiesTicked = ticked == 0 || current == 0 ? 1f : ticked / (float)current;

				if (ticked > current - 100)
					newPercentZombiesTicked = Math.Min(1f, newPercentZombiesTicked + 0.5f);
				ZombieTicker.PercentTicking = newPercentZombiesTicked;
			}
		}
		[HarmonyPatch(typeof(Verse.TickManager))]
		[HarmonyPatch(nameof(Verse.TickManager.DoSingleTick))]
		static class TickManager_DoSingleTick_Patch
		{
			static void Postfix()
			{
				ZombieTicker.DoSingleTick();
			}
		}
		[HarmonyPatch(typeof(Verse.TickManager))]
		[HarmonyPatch(nameof(Verse.TickManager.NothingHappeningInGame))]
		static class Verse_TickManager_NothingHappeningInGame_Patch
		{
			static void Postfix(ref bool __result)
			{
				if (__result == false)
					return;
				__result = ZombieGenerator.ZombiesSpawning == 0;
			}
		}

		// patch to have zombies not being mothballed
		//
		[HarmonyPatch(typeof(RimWorld.Planet.WorldPawns))]
		[HarmonyPatch(nameof(RimWorld.Planet.WorldPawns.ShouldMothball))]
		static class WorldPawns_ShouldMothball_Patch
		{
			static bool Prefix(Pawn p, ref bool __result)
			{
				if (p is Zombie)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}

		// patch to make ZombieThumper repell infestations
		//
		[HarmonyPatch]
		static class InfestationCellFinder_CalculateLocationCandidates_Patch
		{
			public static List<ZombieThumper> thumpers = new();

			[HarmonyPatch(typeof(InfestationCellFinder))]
			[HarmonyPatch(nameof(InfestationCellFinder.CalculateLocationCandidates))]
			[HarmonyPrefix]
			static void CalculateLocationCandidates_Prefix(Map map)
			{
				thumpers = map.listerThings.ThingsOfDef(CustomDefs.Thumper).OfType<ZombieThumper>().ToList();
			}

			[HarmonyPatch(typeof(InfestationCellFinder))]
			[HarmonyPatch(nameof(InfestationCellFinder.GetScoreAt))]
			[HarmonyPrefix]
			static bool GetScoreAt_Prefix(IntVec3 cell, Map map, ref float __result)
			{
				var skipSpawn = thumpers.Any(thumper => thumper.Map == map && thumper.IsActive && thumper.Position.DistanceTo(cell) <= thumper.Radius + 0.5f);
				if (skipSpawn)
				{
					__result = 0f;
					return false;
				}
				return true;
			}
		}

		// patch to update infection state
		//
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch(nameof(Pawn.Tick))]
		static class Pawn_Tick_Patch
		{
			static void Postfix(Pawn __instance)
			{
				if (__instance is Zombie || __instance is ZombieSpitter || __instance.RaceProps.Humanlike == false)
					return;
				var hediffs = __instance.health.hediffSet.hediffs;
				var maxState = InfectionState.None;
				for (var i = 0; i < hediffs.Count; i++)
				{
					if (hediffs[i] is not Hediff_Injury_ZombieBite bite)
						continue;
					var state = bite.TendDuration.GetInfectionState();
					if (state > maxState)
						maxState = state;
				}
				__instance.SetInfectionState(maxState);
			}
		}

		// tick chainsaw when equipped
		//
		[HarmonyPatch(typeof(Pawn_EquipmentTracker))]
		[HarmonyPatch(nameof(Pawn_EquipmentTracker.EquipmentTrackerTick))]
		static class Pawn_EquipmentTracker_EquipmentTrackerTick_Patch
		{
			static void Postfix(Pawn ___pawn)
			{
				if (___pawn.equipment?.Primary is Chainsaw chainsaw)
					chainsaw.Tick();
			}
		}

		// rotate chainsaw when moving
		//
		[HarmonyPatch(typeof(Pawn_PathFollower))]
		[HarmonyPatch(nameof(Pawn_PathFollower.SetupMoveIntoNextCell))]
		static class Pawn_PathFollower_SetupMoveIntoNextCell_Patch
		{
			static void Postfix(Pawn ___pawn, IntVec3 ___nextCell)
			{
				if (___pawn.equipment?.Primary is not Chainsaw chainsaw || chainsaw.swinging)
					return;
				var delta = ___nextCell - ___pawn.Position;
				chainsaw.angle = delta.AngleFlat;
			}
		}

		// stop chainsaw when undrafted
		//
		[HarmonyPatch(typeof(Pawn_DraftController))]
		[HarmonyPatch(nameof(Pawn_DraftController.Drafted))]
		[HarmonyPatch(MethodType.Setter)]
		static class Pawn_DraftController_setDrafted_Patch
		{
			static void Postfix(Pawn ___pawn, bool value)
			{
				if (value == false && ___pawn.equipment?.Primary is Chainsaw chainsaw)
					chainsaw.StopMotor();
			}
		}

		// remove melee from chainsaw
		//
		[HarmonyPatch(typeof(Pawn_MeleeVerbs))]
		[HarmonyPatch(nameof(Pawn_MeleeVerbs.TryMeleeAttack))]
		static class Pawn_MeleeVerbs_TryMeleeAttack_Patch
		{
			static bool Prefix(Pawn ___pawn, ref bool __result)
			{
				if (___pawn is ZombieSpitter)
				{
					__result = false;
					return false;
				}

				if (___pawn.equipment?.Primary is Chainsaw)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}
		//
		[HarmonyPatch(typeof(FloatMenuUtility))]
		[HarmonyPatch(nameof(FloatMenuUtility.GetMeleeAttackAction))]
		static class FloatMenuUtility_GetMeleeAttackAction_Patch
		{
			static bool Prefix(Pawn pawn, ref Action __result)
			{
				if (pawn.equipment?.Primary is Chainsaw chainsaw && chainsaw.running)
				{
					__result = null;
					return false;
				}
				return true;
			}
		}

		// remove gizmos from equipped chainsaws
		//
		[HarmonyPatch]
		static class Pawn_EquipmentTracker_YieldGizmos_Patch
		{
			static MethodBase TargetMethod()
			{
				return AccessTools.FirstMethod(typeof(Pawn_EquipmentTracker), mi =>
				{
					if (mi.GetParameters().Length < 1)
						return false;
					if (mi.GetParameters()[0].ParameterType != typeof(ThingWithComps))
						return false;
					return mi.Name.Contains("__YieldGizmos");
				});
			}

			static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, ThingWithComps eq)
			{
				if (eq is not Chainsaw chainsaw || chainsaw.pawn == null)
				{
					foreach (var gizmo in gizmos)
						yield return gizmo;
					yield break;
				}

				if (chainsaw.pawn?.Drafted == false)
					foreach (var gizmo in gizmos)
						yield return gizmo;

				foreach (var gizmo in chainsaw.GetGizmos())
					yield return gizmo;
			}
		}

		// aim chainsaw
		//
		[HarmonyPatch(typeof(PawnRenderer))]
		[HarmonyPatch(nameof(PawnRenderer.DrawEquipment))]
		static class PawnRenderer_DrawEquipment_Patch
		{
			static bool Prefix(PawnRenderer __instance, Pawn ___pawn, Vector3 rootLoc, Rot4 pawnRotation, PawnRenderFlags flags)
			{
				if (___pawn.equipment?.Primary is not Chainsaw chainsaw)
					return true;

				if (___pawn.Dead || ___pawn.Spawned == false)
					return true;
				if ((flags & PawnRenderFlags.NeverAimWeapon) != PawnRenderFlags.None)
					return true;
				if (chainsaw.running == false)
					return true;

				if (chainsaw.swinging == false/* && ___pawn.Drafted && Find.Selector.IsSelected(___pawn) == false*/)
					return true;

				var angle = chainsaw.angle;

				var vector = new Vector3(0f, (pawnRotation == Rot4.North) ? (-0.0028957527f) : 0.03474903f, 0f);
				var equipmentDrawDistanceFactor = ___pawn.ageTracker.CurLifeStage.equipmentDrawDistanceFactor;
				vector += rootLoc + new Vector3(0f, 0f, 0.4f + CustomDefs.Chainsaw.equippedDistanceOffset).RotatedBy(angle) * equipmentDrawDistanceFactor;

				__instance.DrawEquipmentAiming(chainsaw, vector, angle);
				if (Find.TickManager.Paused)
					___pawn.rotationTracker.Face(vector);

				return false;
			}
		}

		// prevent default facing calculations when equipped with chainsaw
		//
		[HarmonyPatch(typeof(Pawn_RotationTracker))]
		[HarmonyPatch(nameof(Pawn_RotationTracker.UpdateRotation))]
		static class Pawn_RotationTracker_UpdateRotation_Patch
		{
			static bool Prefix(Pawn ___pawn)
			{
				if (___pawn.equipment?.Primary is not Chainsaw chainsaw)
					return true;
				return chainsaw.swinging == false;
			}
		}

		// fix stats panel of chainsaw fuel component
		//
		[HarmonyPatch(typeof(CompProperties_Refuelable))]
		[HarmonyPatch(nameof(CompProperties_Refuelable.SpecialDisplayStats))]
		static class CompProperties_Refuelable_SpecialDisplayStats_Patch
		{
			static bool Prefix(CompProperties_Refuelable __instance, StatRequest req, ref IEnumerable<StatDrawEntry> __result)
			{
				if (req.Def != CustomDefs.Chainsaw)
					return true;

				__result = new List<StatDrawEntry>()
				{
					new StatDrawEntry(
						StatCategoryDefOf.Weapon_Melee,
						__instance.FuelLabel,
						((int)__instance.fuelCapacity).ToString(),
						null,
						3171
					)
				}.AsEnumerable();
				return false;
			}
		}

		[HarmonyPatch(typeof(Gizmo_RefuelableFuelStatus))]
		[HarmonyPatch(nameof(Gizmo_RefuelableFuelStatus.GizmoOnGUI))]
		static class Gizmo_RefuelableFuelStatus_GizmoOnGUI_Patch
		{
			static bool Prefix(CompRefuelable ___refuelable)
			{
				return ___refuelable != null;
			}
		}

		// patch so other zombies do not affect goodwill of other factions
		//
		[HarmonyPatch(typeof(Faction))]
		[HarmonyPatch(nameof(Faction.TryAffectGoodwillWith))]
		static class Faction_TryAffectGoodwillWith_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(ref bool __result, Faction __instance, Faction other)
			{
				if (__instance.def == ZombieDefOf.Zombies || other.def == ZombieDefOf.Zombies)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}

		// patch to make downed zombies as easy to kill as standing
		//
		[HarmonyPatch(typeof(Projectile))]
		[HarmonyPatch(nameof(Projectile.ImpactSomething))]
		static class Projectile_ImpactSomething_Patch
		{
			static PawnPosture GetPostureFix(Pawn p)
			{
				if (p is Zombie)
					return PawnPosture.Standing; // fake standing
				return p.GetPosture();
			}

			static bool RandChance(float chance, Pawn pawn)
			{
				return Rand.Chance(pawn is Zombie ? Math.Min(1f, chance * 2f) : chance);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var m_GetPosture = SymbolExtensions.GetMethodInfo(() => PawnUtility.GetPosture(null));
				var m_Chance = SymbolExtensions.GetMethodInfo(() => Rand.Chance(0f));

				var list = instructions.ToList();
				CodeInstruction lastPawnInstruction = null;
				var len = list.Count;
				for (var i = 0; i < len; i++)
				{
					if (list[i].Calls(m_GetPosture))
					{
						list[i].opcode = OpCodes.Call;
						list[i].operand = SymbolExtensions.GetMethodInfo(() => GetPostureFix(null));
						lastPawnInstruction = list[i - 1];
					}
					if (list[i].Calls(m_Chance))
					{
						list.Insert(i, lastPawnInstruction);
						i++;
						len++;
						list[i].opcode = OpCodes.Call;
						list[i].operand = SymbolExtensions.GetMethodInfo(() => RandChance(0f, null));
					}
				}
				return list;
			}
		}

		// make zombies not affect overall danger rating
		//
		[HarmonyPatch(typeof(DangerWatcher), nameof(DangerWatcher.AffectsStoryDanger))]
		static class DangerWatcher_AffectsStoryDanger_Patch
		{
			static bool Prefix(IAttackTarget t, Map ___map, ref bool __result)
			{
				if (t.Thing is not Zombie zombie)
					return true;
				if (zombie.Spawned == false || zombie.Downed || zombie.IsRopedOrConfused)
				{
					__result = false;
					return false;
				}
				var pos = zombie.Position;
				__result = (pos.InBounds(___map) && ___map.areaManager.Home[pos]);
				return false;
			}
		}

		// do not flee from certain zombies
		//
		[HarmonyPatch(typeof(SelfDefenseUtility))]
		[HarmonyPatch(nameof(SelfDefenseUtility.ShouldFleeFrom))]
		static class SelfDefenseUtility_ShouldFleeFrom_Patch
		{
			static void Postfix(Thing t, Pawn pawn, ref bool __result)
			{
				if (__result == false)
					return;
				if (t is not Zombie zombie)
					return;
				if (pawn.SeesZombieAsThreat(zombie) == false)
					__result = false;
			}
		}

		// smart melee skips bites 
		//
		[HarmonyPatch(typeof(Verb_MeleeAttack))]
		[HarmonyPatch(nameof(Verb_MeleeAttack.TryCastShot))]
		static class Verb_MeleeAttack_TryCastShot_Patch
		{
			static bool Prefix(Verb_MeleeAttack __instance, ref bool __result)
			{
				var limit = ZombieSettings.Values.safeMeleeLimit;
				if (limit == 0)
					return true;

				var caster = __instance.CasterPawn;
				if (caster.equipment?.Primary is Chainsaw)
					return false;

				if (__instance.currentTarget.Thing is not Pawn target)
					return true;
				if (caster is not Zombie zombie)
				{
					if (target is Zombie targetZombie && targetZombie.IsRopedOrConfused)
					{
						target.Kill(null);
						return false;
					}
					return true;
				}

				if ((target.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) ?? false) == false)
					return true;
				if (target.WorkTagIsDisabled(WorkTags.Violent))
					return true;
				if ((target.meleeVerbs?.curMeleeVerb?.Available() ?? false) == false)
					return true;
				if (target.Downed || target.GetPosture() > PawnPosture.Standing)
					return true;
				// allow mentally broken colonists to use smart melee
				// if (target.mindState.mentalStateHandler.InMentalState) return true;


				var pos = target.Position;
				var posX = pos.x;
				var posZ = pos.z;
				var vecs = GenAdj.AdjacentCellsAround;
				var thingGrid = target.Map.thingGrid;
				var concurrentAttacks = GenAdj.AdjacentCellsAround
					.SelectMany(vec => thingGrid.ThingsAt(pos + vec))
					.OfType<Zombie>()
					.Where(zombie => zombie.IsRopedOrConfused == false)
					.Where(zombie =>
					{
						var zombiePos = zombie.Position;
						var dist = posX == zombiePos.x || posZ == zombiePos.z ? 1.1f : 2.2f;
						var res = (target.DrawPos - zombie.DrawPos).MagnitudeHorizontalSquared() <= dist;
						return res;
					})
					.Sum(zombie => zombie.IsTanky ? 2 : 1);
				if (concurrentAttacks <= limit)
					if (__instance.GetDamageDef() == CustomDefs.ZombieBite)
					{
						var level = (target.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0) * (limit - concurrentAttacks + 1);
						if (Rand.Chance(level / 20f))
						{
							target.rotationTracker?.Face(zombie.DrawPos);
							CustomDefs.Smash.PlayOneShot(new TargetInfo(target.Position, target.Map, false));
							Tools.CastBlockBubble(zombie, target);
							__result = false;
							return false;
						}
					}
				return true;
			}
		}

		// patch to increase hit chance for shooting at zombies
		//
		[HarmonyPatch(typeof(Verb_LaunchProjectile))]
		[HarmonyPatch(nameof(Verb_LaunchProjectile.TryCastShot))]
		static class Verb_LaunchProjectile_TryCastShot_Patch
		{
			static bool SkipMissingShotsAtZombies(Verb verb, LocalTargetInfo currentTarget)
			{
				// difficulty Intense or worse will trigger default behavior
				if (Tools.Difficulty() >= 1.5f)
					return false;

				// only for colonists
				if (verb.caster is not Pawn colonist || colonist.Faction != Faction.OfPlayer)
					return false;

				// shooting zombies
				var zombie = currentTarget.HasThing ? currentTarget.Thing as Zombie : null;
				if (zombie == null)
					return false;

				// max 15 cells awaw
				if ((zombie.Position - colonist.Position).LengthHorizontalSquared > 225)
					return false;

				// with line of sight
				if (verb is not Verb_LaunchProjectile shot || shot.verbProps.requireLineOfSight == false)
					return false;

				// skip miss calculations
				return Rand.Chance(Constants.COLONISTS_HIT_ZOMBIES_CHANCE);
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
			{
				var m_SkipMissingShotsAtZombies = SymbolExtensions.GetMethodInfo(() => SkipMissingShotsAtZombies(null, null));
				var p_forcedMissRadius = AccessTools.DeclaredPropertyGetter(typeof(VerbProperties), nameof(VerbProperties.ForcedMissRadius));
				var f_canHitNonTargetPawnsNow = AccessTools.DeclaredField(typeof(Verb), "canHitNonTargetPawnsNow");
				var f_currentTarget = typeof(Verb).Field("currentTarget");

				var skipLabel = generator.DefineLabel();
				var inList = instructions.ToList();

				var idx1 = inList.FirstIndexOf(instr => instr.Calls(p_forcedMissRadius));
				if (idx1 > 0 && idx1 < inList.Count())
				{
					var jumpToIndex = -1;
					for (var i = inList.Count - 1; i >= 3; i--)
					{
						if (inList[i].LoadsField(f_canHitNonTargetPawnsNow) == false)
							continue;
						i -= 3;
						if (inList[i].LoadsConstant(1))
						{
							jumpToIndex = i;
							break;
						}
					}
					if (jumpToIndex >= 0)
					{
						inList[jumpToIndex].labels.Add(skipLabel);

						idx1 -= 2;
						inList.Insert(idx1++, new CodeInstruction(OpCodes.Ldarg_0));
						inList.Insert(idx1++, new CodeInstruction(OpCodes.Ldarg_0));
						inList.Insert(idx1++, new CodeInstruction(OpCodes.Ldfld, f_currentTarget));
						inList.Insert(idx1++, new CodeInstruction(OpCodes.Call, m_SkipMissingShotsAtZombies));
						inList.Insert(idx1++, new CodeInstruction(OpCodes.Brtrue, skipLabel));
					}
					else
						Error("No ldfld canHitNonTargetPawnsNow prefixed by ldc.i4.1;stloc.s;ldarg.0 in Verb_LaunchProjectile.TryCastShot");
				}
				else
					Error("No ldfld forcedMissRadius in Verb_LaunchProjectile.TryCastShot");

				foreach (var instruction in inList)
					yield return instruction;
			}
		}

		// patch to not allow some jobs on zombies
		//
		[HarmonyPatch(typeof(Pawn_JobTracker))]
		[HarmonyPatch(nameof(Pawn_JobTracker.StartJob))]
		static class Pawn_JobTracker_StartJob_Patch
		{
			static readonly HashSet<JobDef> allowedJobs = new()
			{
				CustomDefs.Stumble,
				CustomDefs.Sabotage,
				CustomDefs.Spitter,
				DefDatabase<JobDef>.GetNamed("ExtractZombieSerum"),
				DefDatabase<JobDef>.GetNamed("DoubleTap"),
				JobDefOf.Goto,
				JobDefOf.Wait,
				JobDefOf.Wait_MaintainPosture,
				JobDefOf.AttackMelee,
				JobDefOf.AttackStatic,
			};

			static bool Prefix(Job newJob, Pawn ___pawn, ref int ___jobsGivenThisTick, ref string ___jobsGivenThisTickTextual, ref bool ___startingNewJob)
			{
				if (___pawn is not Zombie && ___pawn is not ZombieSpitter)
					return true;
				if (allowedJobs.Contains(newJob.def))
					return true;

				___jobsGivenThisTick = 0;
				___jobsGivenThisTickTextual = "";
				___startingNewJob = false;
				___pawn.ClearReservationsForJob(newJob);
				Log.Warning($"Zombies cannot do job {newJob.def.defName}");
				return false;
			}
		}

		// make static attacks on doors stop when door is open
		//
		[HarmonyPatch]
		static class JobDriver_AttackStatic_MakeNewToils_b__1_Patch
		{
			static AccessTools.FieldRef<object, JobDriver_AttackStatic> _this;

			static MethodBase TargetMethod()
			{
				var method = typeof(JobDriver_AttackStatic).InnerMethodsStartingWith("<MakeNewToils>b__1").First();
				if (method != null)
				{
					var f_this = AccessTools.GetDeclaredFields(method.DeclaringType).First();
					_this = AccessTools.FieldRefAccess<object, JobDriver_AttackStatic>(f_this);
				}
				else
					Error($"Cannot find field Verse.AI.JobDriver_AttackStatic.*.<MakeNewToils>b__1");

				return method;
			}

			static bool Prefix(object __instance)
			{
				var me = _this(__instance);
				if (me.TargetA.HasThing && me.TargetThingA is Building_Door door && door.Open)
				{
					me.EndJobWith(JobCondition.Incompletable);
					return false;
				}
				return true;
			}
		}

		// hide zombie bite when electrifier/albino zombie wants to melee
		//
		[HarmonyPatch(typeof(Pawn_MeleeVerbs))]
		[HarmonyPatch(nameof(Pawn_MeleeVerbs.GetUpdatedAvailableVerbsList))]
		static class Pawn_MeleeVerbs_GetUpdatedAvailableVerbsList_Patch
		{
			static void Postfix(List<VerbEntry> __result, Pawn ___pawn)
			{
				if (___pawn is Zombie zombie && (zombie.isElectrifier || zombie.isAlbino))
					_ = __result.RemoveAll(entry => entry.verb.GetDamageDef() == CustomDefs.ZombieBite);
			}
		}

		// apply electrical damage when electrifier zombies melee
		//
		[HarmonyPatch(typeof(Verb_MeleeAttackDamage))]
		[HarmonyPatch(nameof(Verb_MeleeAttackDamage.DamageInfosToApply))]
		static class Pawn_MeleeVerbs_ChooseMeleeVerb_Patch
		{
			static void ElectricalDamage(Zombie zombie, Pawn pawn, ref DamageInfo damageInfo)
			{
				if (pawn.equipment?.Primary is Chainsaw chainsaw)
				{
					chainsaw.Shock(120);

					FleckMaker.Static(pawn.TrueCenter(), pawn.Map, FleckDefOf.ExplosionFlash, 12f);
					FleckMaker.ThrowDustPuff(pawn.TrueCenter(), pawn.Map, Rand.Range(0.8f, 1.2f));
					zombie.ElectrifyAnimation();
				}

				if (pawn.apparel != null)
				{
					var apparel = pawn.apparel.WornApparel;

					var smokepopBelt = apparel.OfType<SmokepopBelt>().FirstOrDefault();
					if (smokepopBelt != null)
					{
						damageInfo = new DamageInfo(CustomDefs.ElectricalShock, 1f, 0f, -1f, zombie, null, CustomDefs.ElectricalField);
						zombie.ElectrifyAnimation();
						return;
					}

					var dinfo = new DamageInfo(DamageDefOf.EMP, 1000, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
					_ = pawn.TakeDamage(dinfo);

					/*
					var shieldBelt = apparel.OfType<ShieldBelt>().FirstOrDefault();
					if (shieldBelt != null)
					{
						if (shieldBelt.Energy > 0)
							damageInfo = new DamageInfo(DamageDefOf.EMP, 1f, 0f, -1f, zombie, null, CustomDefs.ElectricalField);
						else
							shieldBelt.Destroy();

						FleckMaker.Static(pawn.TrueCenter(), pawn.Map, FleckDefOf.ExplosionFlash, 12f);
						FleckMaker.ThrowDustPuff(pawn.TrueCenter(), pawn.Map, Rand.Range(0.8f, 1.2f));
						zombie.ElectrifyAnimation();
						return;
					}
					*/

					var sensitiveStuff = apparel.Cast<Thing>();
					if (pawn.equipment != null)
						sensitiveStuff = sensitiveStuff
							.Union(pawn.equipment.AllEquipmentListForReading.Cast<Thing>());
					if (pawn.inventory != null)
						sensitiveStuff = sensitiveStuff
							.Union(pawn.inventory.GetDirectlyHeldThings());

					var success = sensitiveStuff
						.Where(thing =>
						{
							if (thing?.def?.costList == null)
								return false;
							return thing.def.costList.Any(cost => cost.thingDef == ThingDefOf.ComponentIndustrial || cost.thingDef == ThingDefOf.ComponentSpacer);
						})
						.TryRandomElement(out var stuff);

					if (success && stuff != null)
					{
						var amount = 2f * Tools.Difficulty();
						var damage = new DamageInfo(DamageDefOf.Deterioration, amount);
						_ = stuff.TakeDamage(damage);

						FleckMaker.Static(pawn.TrueCenter(), pawn.Map, FleckDefOf.ExplosionFlash, 12f);
						FleckMaker.ThrowDustPuff(pawn.TrueCenter(), pawn.Map, Rand.Range(0.8f, 1.2f));
						zombie.ElectrifyAnimation();
					}
				}
			}

			static IEnumerable<DamageInfo> Postfix(IEnumerable<DamageInfo> results, LocalTargetInfo target, Thing ___caster)
			{
				if (target.Thing is Pawn pawn && pawn.Map != null)
					if (___caster is Zombie zombie && zombie.IsActiveElectric)
					{
						foreach (var result in results)
						{
							var def = result.Def;
							var damage = result;
							if (def.isRanged == false && def.isExplosive == false && target.HasThing)
								ElectricalDamage(zombie, pawn, ref damage);
							yield return damage;
						}
						yield break;
					}

				foreach (var result in results)
					yield return result;
			}
		}

		// patch to reduce revenge by animals
		//
		[HarmonyPatch(typeof(PawnUtility))]
		[HarmonyPatch(nameof(PawnUtility.GetManhunterOnDamageChance))]
		[HarmonyPatch(new Type[] { typeof(Pawn), typeof(float), typeof(Thing) })]
		static class PawnUtility_GetManhunterOnDamageChance_Patch
		{
			static void Postfix(ref float __result, Thing instigator)
			{
				if (ZombieSettings.Values.zombiesCauseManhuntingResponse == false)
					__result = 0;
				else if (instigator is Zombie)
					__result /= 20;
				else if (instigator is ZombieSpitter)
					__result = 0;
			}
		}

		// patch to let predators prefer humans for zombies
		//
		[HarmonyPatch(typeof(FoodUtility))]
		[HarmonyPatch(nameof(FoodUtility.GetPreyScoreFor))]
		static class FoodUtility_GetPreyScoreFor_Patch
		{
			static void Postfix(Pawn prey, ref float __result)
			{
				if (prey is Zombie)
				{
					if (ZombieSettings.Values.animalsAttackZombies)
						__result -= 70f;
					else
						__result -= 10000f;
				}
				else if (prey is ZombieSpitter)
					__result = 0f;
			}
		}

		[HarmonyPatch(typeof(PathFinder))]
		[HarmonyPatch(nameof(PathFinder.FindPath))]
		[HarmonyPatch(new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathEndMode), typeof(PathFinderCostTuning) })]
		public static class PathFinder_FindPath_Patch
		{
			public static Dictionary<Map, TickManager> tickManagerCache = new();

			// infected colonists will still path so exclude them from this check
			// by returning 0 - currently disabled because it does cost too much
			static int GetZombieCosts(Pawn pawn, int idx)
			{
				if (pawn == null)
					return 0;
				if (Tools.ShouldAvoidZombies(pawn) == false)
					return 0;

				var map = pawn.Map;
				if (map == null)
					return 0;
				if (tickManagerCache.TryGetValue(map, out var tickManager) == false)
				{
					tickManager = map.GetComponent<TickManager>();
					if (tickManager == null)
						return 0;
					tickManagerCache[map] = tickManager;
				}
				if (tickManager.avoidGrid == null)
					return 0;
				return tickManager.avoidGrid.GetCosts()[idx];
			}

			static readonly MethodInfo m_CellToIndex_int_int = AccessTools.Method(typeof(CellIndices), nameof(CellIndices.CellToIndex), new Type[] { typeof(int), typeof(int) });
			static readonly FieldInfo f_TraverseParms_pawn = AccessTools.Field(typeof(TraverseParms), nameof(TraverseParms.pawn));
			static readonly MethodInfo m_GetExtraCosts = SymbolExtensions.GetMethodInfo(() => GetZombieCosts(null, 0));
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
			{
				var list = instructions.ToList();
				while (true)
				{
					var t_PathFinderNodeFast = AccessTools.Inner(typeof(PathFinder), "PathFinderNodeFast");
					var f_knownCost = AccessTools.Field(t_PathFinderNodeFast, "knownCost");
					if (f_knownCost == null)
					{
						Error($"Cannot find field Verse.AI.PathFinder.PathFinderNodeFast.knownCost");
						break;
					}

					var idx = list.FirstIndexOf(ins => ins.Calls(m_CellToIndex_int_int));
					if (idx < 0 || idx >= list.Count() || list[idx + 1].opcode != OpCodes.Stloc_S)
					{
						Error($"Cannot find CellToIndex(n,n)/Stloc_S in {original.FullDescription()}");
						break;
					}
					var gridIdx = list[idx + 1].operand;

					var insertLoc = list.FirstIndexOf(ins => ins.opcode == OpCodes.Ldfld && (FieldInfo)ins.operand == f_knownCost);
					while (insertLoc >= 0 && insertLoc < list.Count)
					{
						if (list[insertLoc].opcode == OpCodes.Add)
							break;
						insertLoc++;
					}
					if (insertLoc < 0 || insertLoc >= list.Count())
					{
						Error($"Cannot find Ldfld knownCost ... Add in {original.FullDescription()}");
						break;
					}

					var traverseParmsIdx = original.GetParameters().FirstIndexOf(info => info.ParameterType == typeof(TraverseParms)) + 1;

					list.Insert(insertLoc++, new CodeInstruction(OpCodes.Add));
					list.Insert(insertLoc++, new CodeInstruction(OpCodes.Ldarga_S, traverseParmsIdx));
					list.Insert(insertLoc++, new CodeInstruction(OpCodes.Ldfld, f_TraverseParms_pawn));
					list.Insert(insertLoc++, new CodeInstruction(OpCodes.Ldloc_S, gridIdx));
					list.Insert(insertLoc++, new CodeInstruction(OpCodes.Call, m_GetExtraCosts));
					break;
				}

				foreach (var instr in list)
					yield return instr;
			}
		}
		[HarmonyPatch(typeof(Pawn_PathFollower))]
		[HarmonyPatch(nameof(Pawn_PathFollower.NeedNewPath))]
		static class Pawn_PathFollower_NeedNewPath_Patch
		{
			static readonly MethodInfo m_ShouldCollideWithPawns = SymbolExtensions.GetMethodInfo(() => PawnUtility.ShouldCollideWithPawns(null));

			static bool ZombieInPath(Pawn_PathFollower __instance, Pawn pawn)
			{
				if (Tools.ShouldAvoidZombies(pawn) == false)
					return false;
				if (pawn.RaceProps.Humanlike == false)
					return false;
				if (pawn.RaceProps.IsFlesh == false)
					return false;
				if (AlienTools.IsFleshPawn(pawn) == false)
					return false;
				if (SoSTools.IsHologram(pawn))
					return false;

				var path = __instance.curPath;
				if (path.NodesLeftCount < 5)
					return false;
				var lookAhead = path.Peek(4);
				var destination = path.LastNode;
				if ((lookAhead - destination).LengthHorizontalSquared < 25)
					return false;

				var map = pawn.Map;
				var tickManager = map.GetComponent<TickManager>();
				if (tickManager == null)
					return false;
				var costs = tickManager.avoidGrid.GetCosts();
				var zombieDanger = costs[lookAhead.x + lookAhead.z * map.Size.x];
				return (zombieDanger > 0);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
			{
				var list = instructions.ToList();
				var idx = list.FirstIndexOf(code => code.Calls(m_ShouldCollideWithPawns)) - 1;
				if (idx > 0 && idx < list.Count())
				{
					if (list[idx].opcode == OpCodes.Ldfld)
					{
						var jump = generator.DefineLabel();

						// here we should have a Ldarg_0 but original code has one with a label on it so we reuse it
						list.Insert(idx++, new CodeInstruction(OpCodes.Ldarg_0));
						list.Insert(idx++, new CodeInstruction(OpCodes.Ldfld, typeof(Pawn_PathFollower).Field("pawn")));
						list.Insert(idx++, new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => ZombieInPath(null, null))));
						list.Insert(idx++, new CodeInstruction(OpCodes.Brfalse, jump));
						list.Insert(idx++, new CodeInstruction(OpCodes.Ldc_I4_1));
						list.Insert(idx++, new CodeInstruction(OpCodes.Ret));
						list.Insert(idx++, new CodeInstruction(OpCodes.Ldarg_0) { labels = new List<Label>() { jump } }); // add the missing Ldarg_0 from original code here
					}
					else
						Error("Cannot find Ldfld one instruction before " + m_ShouldCollideWithPawns + " in Pawn_PathFollower.NeedNewPath");
				}
				else
					Error("Cannot find " + m_ShouldCollideWithPawns + " in Pawn_PathFollower.NeedNewPath");

				foreach (var instr in list)
					yield return instr;
			}
		}

		// patch to allow the zombieshocker to be placed over walls without them being replaced
		//
		[HarmonyPatch(typeof(GenSpawn))]
		[HarmonyPatch(nameof(GenSpawn.SpawningWipes))]
		static class GenSpawn_SpawningWipes_Patch
		{
			static bool Prefix(BuildableDef newEntDef, BuildableDef oldEntDef)
			{
				if (newEntDef != CustomDefs.ZombieShocker)
					return true;
				var thingDef = oldEntDef as ThingDef;
				if (thingDef.category != ThingCategory.Building)
					return true;
				return false;
			}
		}

		// do not open doors when not drafted and they are marked by the avoid grid
		//
		[HarmonyPatch]
		static class Building_Door_PawnCanOpen_Patch
		{
			static void Postfix(Building_Door __instance, Pawn p, ref bool __result)
			{
				if (__result == false)
					return;

				if (p == null || p.Map == null || p.Drafted || __instance == null)
					return;

				if (__instance.FreePassage)
					return;

				if (p.CurJob?.playerForced ?? false)
					return;

				if (Tools.ShouldAvoidZombies(p) == false)
					return;

				var map = p.Map;

				var tickManager = map.GetComponent<TickManager>();
				if (tickManager == null)
					return;

				var avoidGrid = tickManager.avoidGrid;
				if (avoidGrid == null)
					return;

				var size = __instance.def.size;
				if (size.x == 1 && size.z == 1)
				{
					if (avoidGrid.ShouldAvoid(map, __instance.Position))
						__result = false;
				}
				else
				{
					var cells = __instance.OccupiedRect().Cells;
					if (cells.Any(cell => avoidGrid.ShouldAvoid(map, cell)))
						__result = false;
				}
			}

			static IEnumerable<MethodBase> TargetMethods()
			{
				return GenTypes.AllSubclasses(typeof(Building_Door))
				.Union(new List<Type>() { typeof(Building_Door) })
				.Select(type => type.GetMethod("PawnCanOpen", AccessTools.all | BindingFlags.DeclaredOnly))
				.Where(method => method != null)
				.Cast<MethodBase>();
			}
		}

		// patch to make zombie not auto-close doors
		//
		[HarmonyPatch(typeof(Building_Door))]
		[HarmonyPatch(nameof(Building_Door.Tick))]
		static class Building_Door_Tick_Patch
		{
			static bool CellContains(ThingGrid instance, IntVec3 c, ThingCategory cat)
			{
				var zombie = instance.ThingAt<Zombie>(c);
				if (zombie != null && zombie.isAlbino)
					return false;
				return instance.CellContains(c, cat);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var from = SymbolExtensions.GetMethodInfo(() => new ThingGrid(null).CellContains(default, default(ThingCategory)));
				var to = SymbolExtensions.GetMethodInfo(() => CellContains(null, default, default));
				return Transpilers.MethodReplacer(instructions, from, to);
			}
		}
		//
		[HarmonyPatch(typeof(Building_Door))]
		[HarmonyPatch(nameof(Building_Door.StartManualCloseBy))]
		static class Building_Door_StartManualCloseBy_Patch
		{
			static bool Prefix(Pawn closer)
			{
				return closer is not Zombie;
			}
		}

		// patch to stop jobs when zombies have to be avoided
		//
		[HarmonyPatch(typeof(JobDriver))]
		[HarmonyPatch(nameof(JobDriver.DriverTick))]
		static class JobDriver_DriverTick_Patch
		{
			static void Postfix(JobDriver __instance, Pawn ___pawn)
			{
				if (___pawn is Zombie || ___pawn.Map == null || ___pawn.IsColonist == false)
					return;

				// could also check ___pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving) but it's expensive
				// and Pawn_HealthTracker.ShouldBeDowned checks it too
				if (___pawn.health.Downed || ___pawn.InMentalState || ___pawn.Drafted)
					return;

				if (__instance.job == null || __instance.job.playerForced || Tools.ShouldAvoidZombies(___pawn) == false)
					return;

				var tickManager = ___pawn.Map.GetComponent<TickManager>();
				if (tickManager == null)
					return;

				var avoidGrid = tickManager.avoidGrid;
				if (avoidGrid == null)
					return;
				if (avoidGrid.InAvoidDanger(___pawn) == false)
					return;

				var jobDef = __instance.job.def;
				if (false
					|| jobDef == JobDefOf.ExtinguishSelf
					|| jobDef == JobDefOf.Flee
					|| jobDef == JobDefOf.FleeAndCower
					|| jobDef == JobDefOf.Vomit
				)
					return;

				var pos = ___pawn.Position;
				var map = ___pawn.Map;

				var safeDestinations = new List<IntVec3>();
				map.floodFiller.FloodFill(pos, (IntVec3 cell) =>
				{
					if (cell.x == pos.x && cell.z == pos.z)
						return true;
					if (cell.Walkable(map) == false)
						return false;
					if (cell.GetEdifice(map) is Building_Door building_Door && building_Door.CanPhysicallyPass(___pawn) == false)
						return false;
					return PawnUtility.AnyPawnBlockingPathAt(cell, ___pawn, true, false, false) == false;
				}, (IntVec3 cell) =>
				{
					if (cell.Standable(map) && avoidGrid.ShouldAvoid(map, cell) == false)
						safeDestinations.Add(cell);
					return false;
				}, 64, false, null);

				if (safeDestinations.Count > 0)
				{
					safeDestinations.SortByDescending(dest => (pos - dest).LengthHorizontalSquared);
					var destination = safeDestinations.First();
					if (destination.IsValid)
					{
						var flee = JobMaker.MakeJob(JobDefOf.Flee, destination);
						flee.playerForced = true;
						___pawn.jobs.ClearQueuedJobs();
						___pawn.jobs.StartJob(flee, JobCondition.Incompletable, null);
					}
				}
			}
		}
		[HarmonyPatch(typeof(JobGiver_ConfigurableHostilityResponse))]
		[HarmonyPatch(nameof(JobGiver_ConfigurableHostilityResponse.TryGetAttackNearbyEnemyJob))]
		static class JobGiver_ConfigurableHostilityResponse_TryGetAttackNearbyEnemyJob_Patch
		{
			static bool Prefix(Pawn pawn, ref Job __result)
			{
				if (pawn.CurJobDef == JobDefOf.Flee && pawn.CurJob.playerForced)
				{
					__result = null;
					return false;
				}
				return true;
			}

			public static bool MyCanReachImmediate(Pawn pawn, LocalTargetInfo target, PathEndMode peMode)
			{
				if (target.Thing is Zombie zombie)
					if (zombie.IsActiveElectric)
						return true;
				return pawn.CanReachImmediate(target, peMode);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var m_CanReachImmediate = SymbolExtensions.GetMethodInfo(() => ReachabilityImmediate.CanReachImmediate(null, default, default));
				var m_MyCanReachImmediate = SymbolExtensions.GetMethodInfo(() => MyCanReachImmediate(null, default, default));
				return Transpilers.MethodReplacer(instructions, m_CanReachImmediate, m_MyCanReachImmediate);
			}
		}
		[HarmonyPatch(typeof(DangerUtility))]
		[HarmonyPatch(nameof(DangerUtility.GetDangerFor))]
		static class DangerUtility_GetDangerFor_Patch
		{
			static void Postfix(IntVec3 c, Pawn p, Map map, ref Danger __result)
			{
				if (p is Zombie || p.ActivePartOfColony() == false || Tools.ShouldAvoidZombies(p) == false)
					return;

				if (p.CurJob?.playerForced ?? false)
					return;

				var avoidGrid = map.GetComponent<TickManager>()?.avoidGrid;
				if (avoidGrid == null)
					return;

				if (avoidGrid.ShouldAvoid(map, c))
					__result = Danger.Deadly;
			}
		}
		[HarmonyPatch]
		static class WorkGiver_Scanner_HasJobOnCell_Patches
		{
			static bool ShouldAvoid(Pawn pawn, IntVec3 cell, bool forced)
			{
				if (forced || pawn.ActivePartOfColony() == false)
					return false;

				if (Tools.ShouldAvoidZombies(pawn) == false)
					return false;

				var tickManager = pawn.Map?.GetComponent<TickManager>();
				if (tickManager == null)
					return false;

				return tickManager.avoidGrid.ShouldAvoid(pawn.Map, cell);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
			{
				var label = generator.DefineLabel();

				yield return new CodeInstruction(OpCodes.Ldarg_1);
				yield return new CodeInstruction(OpCodes.Ldarg_2);
				yield return new CodeInstruction(OpCodes.Ldarg_3);
				yield return new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => ShouldAvoid(null, default, false)));
				yield return new CodeInstruction(OpCodes.Brfalse, label);
				yield return new CodeInstruction(OpCodes.Ldc_I4_0);
				yield return new CodeInstruction(OpCodes.Ret);

				var list = instructions.ToList();
				list[0].labels.Add(label);
				foreach (var instruction in list)
					yield return instruction;
			}

			static IEnumerable<MethodBase> TargetMethods()
			{
				return GenTypes.AllSubclasses(typeof(WorkGiver_Scanner))
				.Select(type => type.GetMethod("HasJobOnCell", AccessTools.all | BindingFlags.DeclaredOnly))
				.Where(method => method != null)
				.Cast<MethodBase>();
			}
		}
		[HarmonyPatch]
		static class WorkGiver_Scanner_JobOnCell_Patches
		{
			static bool ShouldAvoid(Pawn pawn, IntVec3 cell, bool forced)
			{
				if (forced || pawn.ActivePartOfColony() == false)
					return false;

				if (Tools.ShouldAvoidZombies(pawn) == false)
					return false;

				var tickManager = pawn.Map?.GetComponent<TickManager>();
				if (tickManager == null)
					return false;

				return tickManager.avoidGrid.ShouldAvoid(pawn.Map, cell);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
			{
				var label = generator.DefineLabel();

				yield return new CodeInstruction(OpCodes.Ldarg_1);
				yield return new CodeInstruction(OpCodes.Ldarg_2);
				yield return new CodeInstruction(OpCodes.Ldarg_3);
				yield return new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => ShouldAvoid(null, default, false)));
				yield return new CodeInstruction(OpCodes.Brfalse, label);
				yield return new CodeInstruction(OpCodes.Ldnull);
				yield return new CodeInstruction(OpCodes.Ret);

				var list = instructions.ToList();
				list[0].labels.Add(label);
				foreach (var instruction in list)
					yield return instruction;
			}

			static IEnumerable<MethodBase> TargetMethods()
			{
				return GenTypes.AllSubclasses(typeof(WorkGiver_Scanner))
				.Select(type => type.GetMethod(nameof(WorkGiver_Scanner.JobOnCell), AccessTools.all | BindingFlags.DeclaredOnly))
				.Where(method => method != null)
				.Cast<MethodBase>();
			}
		}
		[HarmonyPatch]
		static class WorkGiver_Scanner_HasJobOnThing_Patches
		{
			static bool ShouldAvoid(Pawn pawn, Thing thing, bool forced)
			{
				if (forced || pawn.ActivePartOfColony() == false)
					return false;

				if (Tools.ShouldAvoidZombies(pawn) == false)
					return false;

				var tickManager = pawn.Map?.GetComponent<TickManager>();
				if (tickManager == null)
					return false;

				return tickManager.avoidGrid.ShouldAvoid(thing.Map, thing.Position);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
			{
				var label = generator.DefineLabel();

				yield return new CodeInstruction(OpCodes.Ldarg_1);
				yield return new CodeInstruction(OpCodes.Ldarg_2);
				yield return new CodeInstruction(OpCodes.Ldarg_3);
				yield return new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => ShouldAvoid(null, null, false)));
				yield return new CodeInstruction(OpCodes.Brfalse, label);
				yield return new CodeInstruction(OpCodes.Ldc_I4_0);
				yield return new CodeInstruction(OpCodes.Ret);

				var list = instructions.ToList();
				list[0].labels.Add(label);
				foreach (var instruction in list)
					yield return instruction;
			}

			static IEnumerable<MethodBase> TargetMethods()
			{
				return GenTypes.AllSubclasses(typeof(WorkGiver_Scanner))
				.Select(type => type.GetMethod(nameof(WorkGiver_Scanner.HasJobOnThing), AccessTools.all | BindingFlags.DeclaredOnly))
				.Where(method => method != null)
				.Cast<MethodBase>();
			}
		}
		[HarmonyPatch]
		static class WorkGiver_Scanner_JobOnThing_Patches
		{
			static bool ShouldAvoid(Pawn pawn, Thing thing, bool forced)
			{
				if (forced || pawn.ActivePartOfColony() == false)
					return false;

				if (Tools.ShouldAvoidZombies(pawn) == false)
					return false;

				var tickManager = pawn.Map?.GetComponent<TickManager>();
				if (tickManager == null)
					return false;

				return tickManager.avoidGrid.ShouldAvoid(thing.Map, thing.Position);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
			{
				var label = generator.DefineLabel();

				yield return new CodeInstruction(OpCodes.Ldarg_1);
				yield return new CodeInstruction(OpCodes.Ldarg_2);
				yield return new CodeInstruction(OpCodes.Ldarg_3);
				yield return new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => ShouldAvoid(null, null, false)));
				yield return new CodeInstruction(OpCodes.Brfalse, label);
				yield return new CodeInstruction(OpCodes.Ldnull);
				yield return new CodeInstruction(OpCodes.Ret);

				var list = instructions.ToList();
				list[0].labels.Add(label);
				foreach (var instruction in list)
					yield return instruction;
			}

			static IEnumerable<MethodBase> TargetMethods()
			{
				return GenTypes.AllSubclasses(typeof(WorkGiver_Scanner))
				.Select(type => type.GetMethod(nameof(WorkGiver_Scanner.JobOnThing), AccessTools.all | BindingFlags.DeclaredOnly))
				.Where(method => method != null)
				.Cast<MethodBase>();
			}
		}

		// patch to remove log error "xxx pathing to destroyed thing (zombie)"
		//
		[HarmonyPatch(typeof(Pawn_PathFollower))]
		[HarmonyPatch(nameof(Pawn_PathFollower.StartPath))]
		static class Pawn_PathFollower_StartPath_Patch
		{
			static bool ThingDestroyedAndNotZombie(LocalTargetInfo info)
			{
				return info.ThingDestroyed && (info.Thing is Zombie) == false;
			}

			static PawnPosture GetPawnPosture(Pawn pawn)
			{
				if (pawn is Zombie zombie && zombie.health.Downed)
					return PawnPosture.LayingOnGroundNormal;
				return PawnPosture.Standing;
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var from = typeof(LocalTargetInfo).PropertyGetter(nameof(LocalTargetInfo.ThingDestroyed));
				var to = SymbolExtensions.GetMethodInfo(() => ThingDestroyedAndNotZombie(null));

				var list = Tools.DownedReplacer(instructions).ToList();
				var i = list.FirstIndexOf(instr => instr.Calls(from));
				if (i < 0 || i >= list.Count())
				{
					Error("Cannot find " + from.FullDescription() + " in Pawn_PathFollower.StartPath");
					return list;
				}

				list[i - 1].opcode = OpCodes.Ldarg_1;
				list[i].operand = to;

				i = list.FindLastIndex(instr => instr.LoadsConstant(0));
				list.RemoveAt(i);
				list.InsertRange(i, new CodeInstruction[]
				{
					new CodeInstruction(OpCodes.Ldarg_0),
					new CodeInstruction(OpCodes.Ldfld, typeof(Pawn_PathFollower).Field("pawn")),
					new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => GetPawnPosture(null)))
				});

				return list;
			}
		}

		// patch to add a pheromone info section to the rimworld cell inspector
		//
		[HarmonyPatch(typeof(EditWindow_DebugInspector))]
		[HarmonyPatch(nameof(EditWindow_DebugInspector.CurrentDebugString))]
		static class EditWindow_DebugInspector_CurrentDebugString_Patch
		{
			static int[] colonyPoints = new int[3];
			static int capableColonists = 0;
			static int incapableColonists = 0;
			static int colonyPointsCounter = 0;

			static void DebugGrid(StringBuilder builder)
			{
				if (Current.Game == null)
					return;
				var map = Current.Game.CurrentMap;
				if (map == null)
					return;
				var pos = UI.MouseCell();

				var tickManager = map.GetComponent<TickManager>();
				if (tickManager == null)
					return;

				if (colonyPointsCounter-- < 0)
				{
					colonyPointsCounter = 60;
					colonyPoints = Tools.ColonyPoints();
					(capableColonists, incapableColonists) = Tools.ColonistsInfo(map);
				}

				var maxCount = tickManager.GetMaxZombieCount();
				var threatLevel = ZombieWeather.GetThreatLevel(map);
				var realCount = Mathf.FloorToInt(maxCount * threatLevel);
				_ = builder.AppendLine("---");
				_ = builder.AppendLine($"Colonists: {capableColonists} + {incapableColonists}");
				_ = builder.AppendLine($"Colony points: {tickManager.currentColonyPoints}");
				_ = builder.AppendLine($"Center of Interest: {tickManager.centerOfInterest.x}/{tickManager.centerOfInterest.z}");
				_ = builder.AppendLine($"Colony points: {tickManager.currentColonyPoints}");
				_ = builder.AppendLine($"Colonist points: {colonyPoints[0]}");
				_ = builder.AppendLine($"Weapon points: {colonyPoints[1]}");
				_ = builder.AppendLine($"Defense points: {colonyPoints[2]}");
				_ = builder.AppendLine($"Max zombie count: {maxCount}");
				if (ZombieSettings.Values.useDynamicThreatLevel)
					_ = builder.AppendLine($"Zombie threat level: {Mathf.FloorToInt(10000 * threatLevel) / 100f}%");
				else
					_ = builder.AppendLine("Zombie threat level off");
				_ = builder.AppendLine($"Total zombie count: {tickManager.ZombieCount()} out of {realCount}");

				_ = builder.AppendLine("");
				AccessTools.GetFieldNames(typeof(IncidentParameters)).Do(name =>
				{
					var value = Traverse.Create(tickManager.incidentInfo.parameters).Field(name).GetValue();
					_ = builder.AppendLine($"{name}: {value}");
				});
				_ = builder.AppendLine("");

				var ticks = GenTicks.TicksGame;
				var (minTicksForSpitter, deltaContact, deltaSpitter) = Tools.ZombieSpitterParameter();
				_ = builder.AppendLine($"Zombie Spitter ({ZombieSettings.Values.spitterThreat:0%}x):");
				_ = builder.AppendLine($"- min ticks: {minTicksForSpitter} {(tickManager.zombieSpitterInited ? "(inited)" : "")}");
				_ = builder.AppendLine($"- contact last={tickManager.lastZombieContact}, diff={ticks - tickManager.lastZombieContact}, min={deltaContact}");
				_ = builder.AppendLine($"- spitter last={tickManager.lastZombieSpitter}, diff={ticks - tickManager.lastZombieSpitter}, min={deltaSpitter}");
				_ = builder.AppendLine("");

				if (pos.InBounds(map) == false)
					return;

				var contaminationList = map.thingGrid.ThingsListAt(pos)
					.Select(t => (thing: t, contamination: t.GetContamination()))
					.Where(pair => pair.contamination != 0)
					.Join(pair => $"{pair.thing}/{pair.contamination}", " ");
				if (contaminationList.Any())
				{
					_ = builder.AppendLine($"Contaminations: {contaminationList}");
					_ = builder.AppendLine("");
				}

				if (Tools.ShouldAvoidZombies())
				{
					var avoidGrid = map.GetComponent<TickManager>().avoidGrid;
					_ = builder.AppendLine($"Avoid cost: {avoidGrid.GetCosts()[pos.x + pos.z * map.Size.x]}");
				}

				var info = ZombieWanderer.GetMapInfo(map);
				_ = builder.AppendLine($"Parent normal: {info.GetParent(pos, false)}");
				_ = builder.AppendLine($"Parent via doors: {info.GetParent(pos, true)}");
				_ = builder.AppendLine($"Parent raw: {info.GetDirectDebug(pos)}");

				var cell = map.GetGrid().GetPheromone(pos, false);
				if (cell != null)
				{
					var realZombieCount = pos.GetThingList(map).OfType<Zombie>().Count();
					var sb = new StringBuilder();
					_ = sb.Append($"Zombie grid: {cell.zombieCount} zombies");
					if (cell.zombieCount != realZombieCount)
						_ = sb.Append($" (real {realZombieCount})");
					_ = builder.AppendLine(sb.ToString());

					var now = Tools.Ticks();
					var tdiff = (cell.timestamp - now).ToString();
					if (tdiff.StartsWith("-"))
						tdiff = tdiff.ReplaceFirst("-", "- ");
					else
						tdiff = "+ " + tdiff;
					_ = builder.AppendLine($"Pheromone timestamp {cell.timestamp} = {now} {tdiff}");
				}
				else
					_ = builder.AppendLine($"{pos.x} {pos.z}: empty");
				_ = builder.AppendLine("");

				var pathing = map.GetComponent<TickManager>()?.zombiePathing;
				if (pathing != null && pos.InBounds(map))
				{
					var wrong = pathing.backpointingRegions.Count != pathing.backpointingRegionsIndices.Count;
					_ = builder.AppendLine($"Smart wandering seeds: {pathing.backpointingRegions.Count(br => br.parentIdx == -1)}");
					_ = builder.AppendLine($"Smart wandering regions: {pathing.backpointingRegions.Count} {(wrong ? " [count wrong]" : "")}");
					var from = IntVec3.Invalid;
					var region = map.regionGrid.GetRegionAt_NoRebuild_InvalidAllowed(pos);
					_ = builder.AppendLine($"Smart wandering region id: {region?.id.ToString() ?? "null"}");
					if (region != null)
					{
						if (pathing.backpointingRegionsIndices.TryGetValue(region, out var idx))
							from = pathing.backpointingRegions[idx].cell;
						else
							idx = -1;
						_ = builder.AppendLine($"Smart wandering index: {idx}");
					}
					var destination = pathing.GetWanderDestination(pos);
					var fromStr = from.IsValid ? from.ToString() : "null";
					var destStr = destination.IsValid ? destination.ToString() : "null";
					_ = builder.AppendLine($"Smart wandering {fromStr} -> {destStr}");
					_ = builder.AppendLine("");
				}

				var gridSum = GenAdj.AdjacentCellsAndInside.Select(vec => pos + vec)
				.Where(c => c.InBounds(map))
				.Select(c => map.GetGrid().GetZombieCount(c))
				.Sum();
				var realSum = GenAdj.AdjacentCellsAndInside.Select(vec => pos + vec)
					.Where(c => c.InBounds(map))
					.Select(c => map.thingGrid.ThingsListAtFast(c).OfType<Zombie>().Count())
					.Sum();
				_ = builder.AppendLine($"Rage factor: grid={gridSum}, real={realSum}");

				map.thingGrid.ThingsListAtFast(pos).OfType<Zombie>().Do(zombie =>
				{
					var currPos = zombie.Position;
					var gotoPos = zombie.pather.Moving ? zombie.pather.Destination.Cell : IntVec3.Invalid;
					var wanderTo = zombie.wanderDestination;
					var sb = new StringBuilder();
					_ = sb.Append($"Zombie {zombie.Name.ToStringShort} at {currPos.x},{currPos.z}");
					_ = sb.Append($", {zombie.state.ToString().ToLower()}");
					if (zombie.raging > 0)
						_ = sb.Append($", raging[{zombie.raging - GenTicks.TicksAbs}] ");
					_ = sb.Append($", going to {gotoPos.x},{gotoPos.z}");
					_ = sb.Append($" (wander dest {wanderTo.x},{wanderTo.z})");
					_ = builder.AppendLine(sb.ToString());
				});
			}

			static bool Prefix(ref string __result)
			{
				if (Current.Game == null)
				{
					__result = "";
					return false;
				}
				return true;
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var list = instructions.ToList();
				var m_ToString = AccessTools.Method(typeof(object), "ToString");
				var idx = list.FindLastIndex(instr => instr.Calls(m_ToString));
				if (idx > 0)
				{
					list.Insert(idx++, new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => DebugGrid(null))));
					list.Insert(idx++, list[idx - 3].Clone());
				}
				else
					Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);

				return list;
			}
		}

		// patch for adding zombie faction to new games
		//
		[HarmonyPatch(typeof(FactionGenerator))]
		[HarmonyPatch(nameof(FactionGenerator.GenerateFactionsIntoWorld))]
		static class FactionGenerator_GenerateFactionsIntoWorld_Patch
		{
			static void Prefix(List<FactionDef> factions)
			{
				if (factions != null && factions.Contains(ZombieDefOf.Zombies) == false)
					factions.Add(ZombieDefOf.Zombies);

			}
		}

		// patch for adding zombie faction to existing games
		//
		[HarmonyPatch(typeof(FactionManager))]
		[HarmonyPatch(nameof(FactionManager.ExposeData))]
		static class FactionManager_ExposeData_Patch
		{
			static void Postfix(List<Faction> ___allFactions)
			{
				if (Scribe.mode == LoadSaveMode.Saving)
					return;
				if (___allFactions == null)
					return;

				var factionDefs = ___allFactions.Select(f => f.def).ToList();
				if (factionDefs.Contains(ZombieDefOf.Zombies) == false)
				{
					var parms = new FactionGeneratorParms(ZombieDefOf.Zombies);
					var zombies = FactionGenerator.NewGeneratedFaction(parms);
					foreach (var faction in ___allFactions)
					{
						var rel1 = new FactionRelation()
						{
							other = faction,
							baseGoodwill = 0,
							kind = FactionRelationKind.Hostile
						};
						zombies.relations.Add(rel1);

						var rel2 = new FactionRelation()
						{
							other = zombies,
							baseGoodwill = 0,
							kind = FactionRelationKind.Hostile
						};
						faction.relations.Add(rel2);

					}
					___allFactions.Add(zombies);
				}
			}
		}

		// patch for detecting if a pawn enters a new cell
		//
		[HarmonyPatch(typeof(Thing))]
		[HarmonyPatch(nameof(Thing.Position), MethodType.Setter)]
		static class Thing_Position_Patch
		{
			static readonly MentalStateDef def1 = MentalStateDefOf.Manhunter;
			static readonly MentalStateDef def2 = MentalStateDefOf.ManhunterPermanent;

			// top level idx = sign(new.x-old.x) + 1 + 3 * (sign(new.z-old.z) + 1)
			static readonly IntVec3[][] orthogonalIndices = new[]
			{                                                             // (T)op (B)ottom (L)eft (R)right (0)zero
				new [] { new IntVec3(00, 0, -1), new IntVec3(-1, 0, 00) }, // LB -> 0B + L0
				new [] { new IntVec3(01, 0, 00), new IntVec3(-1, 0, 00) }, // 0B -> R0 + L0
				new [] { new IntVec3(01, 0, 00), new IntVec3(00, 0, -1) }, // RB -> R0 + 0B
				new [] { new IntVec3(00, 0, -1), new IntVec3(00, 0, 01) }, // L0 -> 0B + 0T
				new [] { new IntVec3(00, 0, 00), new IntVec3(00, 0, 00) }, // center unused
				new [] { new IntVec3(00, 0, 01), new IntVec3(00, 0, -1) }, // R0 -> 0T + 0B
				new [] { new IntVec3(-1, 0, 00), new IntVec3(00, 0, 01) }, // LT -> L0 + 0T
				new [] { new IntVec3(-1, 0, 00), new IntVec3(01, 0, 00) }, // 0T -> L0 + R0
				new [] { new IntVec3(00, 0, 01), new IntVec3(01, 0, 00) }, // RT -> 0T + R0
			};

			static readonly HashSet<IntVec3> exclude = new(Tools.GetCircle(2));
			static void Prefix(Thing __instance, IntVec3 value)
			{
				if (__instance is not Pawn pawn)
					return;
				var map = pawn.Map;
				if (map == null)
					return;
				var pos = pawn.Position;
				if (pos == value)
					return;

				if (pawn is ZombieSpitter)
				{
					var now = Tools.Ticks();
					var grid = pawn.Map.GetGrid();
					var f = ZombieSettings.Values.spitterThreat;
					var radius = f * GenMath.LerpDouble(0, 5, 4, 32, Tools.Difficulty());
					Tools.GetCircle(radius).DoIf(vec => exclude.Contains(vec) == false, vec =>
						grid.BumpTimestamp(value + vec, now - (long)(2f * vec.LengthHorizontal)));
					return;
				}

				if (pawn is Zombie zombie)
				{
					var grid = map.GetGrid();

					// tanky zombies leave pherome trace too so other zombies follow
					//
					if (zombie.IsTanky)
					{
						var fadeOff = Tools.PheromoneFadeoff();
						var now = Tools.Ticks();
						var radius = Constants.TANKY_PHEROMONE_RADIUS;
						var dx = pos.x - value.x;
						var dz = pos.z - value.z;
						var r2 = radius * radius;
						Tools.GetCircle(radius).Do(vec =>
						{
							var vx = Math.Sign(vec.x);
							var vz = Math.Sign(vec.z);
							var vlen = vec.LengthHorizontalSquared;
							if ((vx == 0 || vx == dx) && (vz == 0 || vz == dz) && vlen > 1f)
							{
								var offset = GenMath.LerpDouble(0f, r2, fadeOff / 8f, fadeOff / 4f, vlen);
								grid.BumpTimestamp(value + vec, now - (long)offset);
							}
						});
					}
					else
					{
						var newCell = grid.GetPheromone(value, false);
						if (newCell != null && newCell.zombieCount > 0)
						{
							newCell.timestamp -= newCell.zombieCount * Constants.ZOMBIE_CLOGGING_FACTOR;
							var notOlderThan = Tools.Ticks() - Tools.PheromoneFadeoff();
							newCell.timestamp = Math.Max(newCell.timestamp, notOlderThan);
						}
					}

					// dark slimers leave dark slime behind them
					//
					if (zombie.isDarkSlimer)
					{
						_ = FilthMaker.TryMakeFilth(value, map, CustomDefs.TarSlime, null, true);
						if (Tools.Difficulty() > 1)
						{
							var x = Math.Sign(value.x - pos.x) + 1;
							var z = Math.Sign(value.z - pos.z) + 1;
							var orthIdx = x + 3 * z;
							var pair = orthogonalIndices[orthIdx];
							_ = FilthMaker.TryMakeFilth(pos + pair[0], map, CustomDefs.TarSlime, null, true);
							_ = FilthMaker.TryMakeFilth(pos + pair[1], map, CustomDefs.TarSlime, null, true);
						}
					}

					return;
				}

				// set zombie contact timestamp
				var isNotInfected = pawn.InfectionState() < InfectionState.Infecting;
				if (isNotInfected && pawn.IsColonist)
				{
					var tickManager = map.GetComponent<TickManager>();
					if (tickManager?.avoidGrid?.InAvoidDanger(pawn) ?? false)
						tickManager.MarkZombieContact();
				}

				// manhunting will always trigger senses
				//
				if (pawn.MentalState == null || (pawn.MentalState.def != def1 && pawn.MentalState.def != def2))
				{
					if (ZombieSettings.Values.attackMode == AttackMode.OnlyHumans)
						if (pawn.RaceProps.Humanlike == false
								|| pawn.RaceProps.IsFlesh == false
								|| AlienTools.IsFleshPawn(pawn) == false
								|| SoSTools.IsHologram(pawn)
						)
							return;

					if (ZombieSettings.Values.attackMode == AttackMode.OnlyColonists)
						if (pawn.IsColonist == false)
							return;
				}

				// apply toxic splatter damage
				var toxity = 0.023006668f * Mathf.Max(1f - pawn.GetStatValue(StatDefOf.ToxicResistance, true, -1), 0f);
				if (ModsConfig.BiotechActive)
					toxity *= Mathf.Max(1f - pawn.GetStatValue(StatDefOf.ToxicEnvironmentResistance, true, -1), 0f);
				if (toxity > 0f)
				{
					pawn.Position.GetThingList(pawn.Map).Where(thing => thing.def == CustomDefs.StickyGoo).Do(thing =>
					{
						HealthUtility.AdjustSeverity(pawn, HediffDefOf.ToxicBuildup, toxity);
					});
				}

				// leave pheromone trail
				if (isNotInfected && Customization.DoesAttractsZombies(pawn))
				{
					var now = Tools.Ticks();
					var grid = pawn.Map.GetGrid();
					Tools.GetCircle(Tools.RadiusForPawn(pawn)).Do(vec => grid.BumpTimestamp(value + vec, now - (long)(2f * vec.LengthHorizontal)));
				}
			}
		}

		// turrets consume less steam
		//
		[HarmonyPatch(typeof(CompRefuelable))]
		[HarmonyPatch(nameof(CompRefuelable.ConsumeFuel))]
		public static class CompRefuelable_ConsumeFuel_Patch
		{
			static void Prefix(CompRefuelable __instance, ref float amount)
			{
				if (__instance.parent is not Building_Turret)
					return;
				amount -= amount * ZombieSettings.Values.reducedTurretConsumption;
			}
		}

		// downed zombies only scratch feet parts
		//
		[HarmonyPatch(typeof(DamageWorker_Scratch))]
		[HarmonyPatch(nameof(DamageWorker_Scratch.ChooseHitPart))]
		public static class DamageWorker_Scratch_ChooseHitPart_Patch
		{
			static void Prefix(ref DamageInfo dinfo)
			{
				if (dinfo.Instigator is not Zombie zombie || zombie.health.Downed == false)
					return;
				dinfo.SetBodyRegion(BodyPartHeight.Bottom, BodyPartDepth.Outside);
			}
		}
		[HarmonyPatch(typeof(DamageWorker_Bite))]
		[HarmonyPatch(nameof(DamageWorker_Bite.ChooseHitPart))]
		public static class DamageWorker_Bite_ChooseHitPart_Patch
		{
			static void Prefix(ref DamageInfo dinfo)
			{
				if (dinfo.Instigator is not Zombie zombie || zombie.health.Downed == false)
					return;
				dinfo.SetBodyRegion(BodyPartHeight.Bottom, BodyPartDepth.Outside);
			}
		}

		// patch to make infected colonists have no needs
		//
		[HarmonyPatch(typeof(Need))]
		[HarmonyPatch(nameof(Need.CurLevel), MethodType.Setter)]
		public static class Need_CurLevel_Patch
		{
			// this is set periodically from Alerts.Alert_ZombieInfection
			public static HashSet<Pawn> infectedColonists = new();

			static bool ShouldBeAverageNeed(Pawn pawn)
			{
				return infectedColonists.Contains(pawn);
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(ILGenerator il, IEnumerable<CodeInstruction> instructions)
			{
				var average = il.DeclareLocal(typeof(float));
				var originalStart = il.DefineLabel();

				yield return new CodeInstruction(OpCodes.Ldarg_1);
				yield return new CodeInstruction(OpCodes.Stloc, average);
				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldfld, typeof(Need).Field("pawn"));
				yield return new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => ShouldBeAverageNeed(null)));
				yield return new CodeInstruction(OpCodes.Brfalse, originalStart);
				yield return new CodeInstruction(OpCodes.Ldc_R4, 0.5f);
				yield return new CodeInstruction(OpCodes.Stloc, average);

				var found = false;
				var firstTime = true;
				foreach (var instruction in instructions)
				{
					if (firstTime)
						instruction.labels.Add(originalStart);
					if (instruction.IsLdarg(1))
					{
						instruction.opcode = OpCodes.Ldloc;
						instruction.operand = average;
						found = true;
					}
					yield return instruction;
					firstTime = false;
				}

				if (!found)
					Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);
			}
		}

		// patch to make infected colonists have no mental breaks
		//
		[HarmonyPatch(typeof(MentalStateHandler))]
		[HarmonyPatch(nameof(MentalStateHandler.TryStartMentalState))]
		static class MentalStateHandler_TryStartMentalState_Patch
		{
			static bool NoMentalState(Pawn pawn)
			{
				return Need_CurLevel_Patch.infectedColonists.Contains(pawn);
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(ILGenerator il, IEnumerable<CodeInstruction> instructions)
			{
				var originalStart = il.DefineLabel();

				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldfld, typeof(MentalStateHandler).Field("pawn"));
				yield return new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => NoMentalState(null)));
				yield return new CodeInstruction(OpCodes.Brfalse, originalStart);
				yield return new CodeInstruction(OpCodes.Ldc_I4_0);
				yield return new CodeInstruction(OpCodes.Ret);

				var firstTime = true;
				foreach (var instruction in instructions)
				{
					if (firstTime)
						instruction.labels.Add(originalStart);
					yield return instruction;
					firstTime = false;
				}
			}

			static void Postfix(bool __result, Pawn ___pawn)
			{
				if (__result && ___pawn.equipment?.Primary is Chainsaw chainsaw)
					_ = ___pawn.equipment.TryDropEquipment(chainsaw, out var _, ___pawn.Position);
			}
		}

		// patch to make infected colonists feel no pain
		//
		[HarmonyPatch(typeof(HediffSet))]
		[HarmonyPatch(nameof(HediffSet.PainTotal), MethodType.Getter)]
		static class HediffSet_CalculatePain_Patch
		{
			static bool Prefix(HediffSet __instance, ref float __result)
			{
				if (Need_CurLevel_Patch.infectedColonists.Contains(__instance.pawn))
				{
					__result = 0f;
					return false;
				}
				return true;
			}
		}

		// patch to make infected colonists have full capacity
		//
		[HarmonyPatch(typeof(PawnCapacitiesHandler))]
		[HarmonyPatch(nameof(PawnCapacitiesHandler.GetLevel))]
		static class PawnCapacitiesHandler_GetLevel_Patch
		{
			static bool FullLevel(Pawn pawn)
			{
				if (pawn.health.Dead)
					return false;
				return Need_CurLevel_Patch.infectedColonists.Contains(pawn);
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(ILGenerator il, IEnumerable<CodeInstruction> instructions)
			{
				var originalStart = il.DefineLabel();

				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldfld, typeof(PawnCapacitiesHandler).Field("pawn"));
				yield return new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => FullLevel(null)));
				yield return new CodeInstruction(OpCodes.Brfalse, originalStart);
				yield return new CodeInstruction(OpCodes.Ldc_R4, 1f);
				yield return new CodeInstruction(OpCodes.Ret);

				var firstTime = true;
				foreach (var instruction in instructions)
				{
					if (firstTime)
						instruction.labels.Add(originalStart);
					yield return instruction;
					firstTime = false;
				}
			}
		}

		// patch to reduce instant zombie infections for pawns in incidents
		//
		[HarmonyPatch(typeof(IncidentWorker))]
		[HarmonyPatch(nameof(IncidentWorker.TryExecute))]
		static class IncidentWorker_TryExecute_Patch
		{
			static void Postfix(IncidentParms parms)
			{
				if (parms.pawnGroups == null)
					return;
				var f = GenMath.LerpDoubleClamped(0, 5, 100, 0, Tools.Difficulty());
				parms.pawnGroups.Keys.DoIf(_ => Rand.Chance(f), pawn => pawn.GetHediffsList<Hediff_Injury_ZombieBite>()
					.Do(bite =>
					{
						bite.mayBecomeZombieWhenDead = false;
						var tendDuration = bite.TryGetComp<HediffComp_Zombie_TendDuration>();
						tendDuration?.ZombieInfector.MakeHarmless();
					})
				);
			}
		}

		// patch to allow spawning zombie raids with debug tools
		//
		[HarmonyPatch(typeof(IncidentWorker_Raid))]
		[HarmonyPatch(nameof(IncidentWorker_Raid.TryExecuteWorker))]
		static class IncidentWorker_Raid_TryExecuteWorker_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(ref bool __result, IncidentParms parms)
			{
				if (parms?.faction?.def != ZombieDefOf.Zombies)
					return true;

				var oldMode = ZombieSettings.Values.spawnHowType;
				ZombieSettings.Values.spawnHowType = parms.raidArrivalMode.walkIn ? SpawnHowType.FromTheEdges : SpawnHowType.AllOverTheMap;
				_ = ZombiesRising.TryExecute(Find.CurrentMap, Mathf.FloorToInt(parms.points), parms.spawnCenter, false, false);
				ZombieSettings.Values.spawnHowType = oldMode;
				__result = false;
				return false;
			}
		}

		// patch to let incidents spawn infected
		//
		[HarmonyPatch(typeof(PawnGroupKindWorker))]
		[HarmonyPatch(nameof(PawnGroupKindWorker.GeneratePawns))]
		[HarmonyPatch(new[] { typeof(PawnGroupMakerParms), typeof(PawnGroupMaker), typeof(bool) })]
		static class IncidentWorker_Patches
		{
			static void Postfix(List<Pawn> __result)
			{
				if (__result == null)
					return;
				var notLaunchingShip = ShipCountdown.CountingDown == false;
				if (notLaunchingShip && Rand.Chance(ZombieSettings.Values.infectedRaidsChance) == false)
					return;
				if (notLaunchingShip && ZombieWeather.GetThreatLevel(__result.FirstOrDefault()?.Map) == 0f)
					return;
				__result.DoIf(pawn => pawn.RaceProps.Humanlike, Tools.AddZombieInfection);
			}
		}

		// patch to allow spawning zombies with debug tools
		//
		[HarmonyPatch(typeof(PawnGenerator))]
		[HarmonyPatch(nameof(PawnGenerator.GenerateNewPawnInternal))]
		static class PawnGenerator_GenerateNewPawnInternal_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(ref PawnGenerationRequest request, ref Pawn __result)
			{
				if (request.Faction?.def != ZombieDefOf.Zombies)
					return true;
				if (request.KindDef == ZombieDefOf.ZombieSpitter)
					return true;

				Zombie zombie = null;
				var map = Find.CurrentMap;
				var it = ZombieGenerator.SpawnZombieIterativ(map.Center, map, ZombieType.Random, z => zombie = z);
				while (it.MoveNext())
					;
				__result = zombie;
				return false;
			}
		}

		// patches to disallow interacting with zombies or zombiecorpses
		//
		[HarmonyPatch(typeof(WorkGiver_Haul))]
		[HarmonyPatch(nameof(WorkGiver_Haul.JobOnThing))]
		static class WorkGiver_Haul_JobOnThing_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Thing t, bool forced, ref Job __result)
			{
				if (forced)
					return true;

				if (t is ZombieCorpse)
				{
					__result = null;
					return false;
				}

				return true;
			}
		}
		[HarmonyPatch(typeof(ReservationManager))]
		[HarmonyPatch(nameof(ReservationManager.CanReserve))]
		static class ReservationManager_CanReserve_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(LocalTargetInfo target, ref bool __result)
			{
				if (target.HasThing && target.Thing is Zombie zombie && zombie.wasMapPawnBefore == false)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}
		[HarmonyPatch(typeof(ReservationManager))]
		[HarmonyPatch(nameof(ReservationManager.Reserve))]
		static class ReservationManager_Reserve_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(LocalTargetInfo target, ref bool __result)
			{
				if (target.HasThing)
				{
					//if (target.Thing is Zombie || target.Thing is ZombieCorpse)
					if (target.Thing is Zombie)
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
		[HarmonyPatch(nameof(Pawn.AnythingToStrip))]
		static class Pawn_AnythingToStrip_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn __instance, ref bool __result)
			{
				if (__instance is Zombie || __instance is ZombieSpitter)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}

		// patch to not show forbidden red cross icon on zombies
		//
		// [HarmonyPatch(typeof(ForbidUtility))]
		// [HarmonyPatch(nameof(ForbidUtility.IsForbidden))]
		// [HarmonyPatch(new Type[] { typeof(Thing), typeof(Faction) })]
		// static class ForbidUtility_IsForbidden_Patch
		// {
		// 	[HarmonyPriority(Priority.First)]
		// 	static bool Prefix(Thing t, ref bool __result)
		// 	{
		// 		//if (t is Zombie || t is ZombieCorpse)
		// 		if (t is Zombie)
		// 		{
		// 			__result = true;
		// 			return false;
		// 		}
		// 		return true;
		// 	}
		// }

		// patch to hide zombie names
		//
		[HarmonyPatch(typeof(GenMapUI))]
		[HarmonyPatch(nameof(GenMapUI.DrawPawnLabel))]
		[HarmonyPatch(new Type[] { typeof(Pawn), typeof(Vector2), typeof(float), typeof(float), typeof(Dictionary<string, string>), typeof(GameFont), typeof(bool), typeof(bool) })]
		[StaticConstructorOnStartup]
		static class GenMapUI_DrawPawnLabel_Patch
		{
			static bool Prefix(Pawn pawn)
			{
				if (pawn is not Zombie zombie)
					return true;
				return zombie.wasMapPawnBefore;
			}
		}

		// patch to fix null exceptions for zombie panels
		//
		[HarmonyPatch(typeof(MainTabWindow_Inspect))]
		[HarmonyPatch(nameof(MainTabWindow_Inspect.CurTabs), MethodType.Getter)]
		static class MainTabWindow_Inspect_CurTabs_Patch
		{
			static void Postfix(ref IEnumerable<InspectTabBase> __result)
			{
				__result ??= new List<InspectTabBase>();
			}
		}

		// patch to make zombies appear to be never "down" if self-healing is on
		// to get original state, use pawn.health.Downed instead
		//
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch(nameof(Pawn.Downed), MethodType.Getter)]
		static class Pawn_Downed_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn __instance, ref bool __result)
			{
				if (ZombieSettings.Values.doubleTapRequired == false)
					return true;
				if (__instance is not Zombie)
					return true;
				__result = false;
				return false;
			}
		}

		// patch to keep shooting even if a zombie is down (only if self-healing is on)
		/*
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch(nameof(Pawn.ThreatDisabled))]
		static class Pawn_ThreatDisabled_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn __instance, ref bool __result)
			{
				if (__instance is not Zombie zombie)
					return true;
				__result = !zombie.Spawned;
				return false;
			}
		}
		[HarmonyPatch]
		static class Toils_Combat_FollowAndMeleeAttack_KillIncappedTarget_Patch
		{
			static bool IncappedTargetCheck(Job curJob, Pawn target)
			{
				if (target is Zombie)
					return true;
				return curJob.killIncappedTarget;
			}

			static MethodBase TargetMethod()
			{
				return typeof(Toils_Combat).InnerMethodsStartingWith("<FollowAndMeleeAttack>b__0").First();
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var m_get_Downed = typeof(Pawn).PropertyGetter(nameof(Pawn.Downed));
				var f_killIncappedTarget = typeof(Job).Field(nameof(Job.killIncappedTarget));

				var found1 = false;
				var found2 = false;
				CodeInstruction last = null;
				CodeInstruction localPawnInstruction = null;
				foreach (var instruction in instructions)
				{
					if (instruction.Calls(m_get_Downed))
					{
						localPawnInstruction = new CodeInstruction(last);
						found1 = true;
					}

					if (instruction.LoadsField(f_killIncappedTarget) && localPawnInstruction != null)
					{
						yield return localPawnInstruction;

						instruction.opcode = OpCodes.Call;
						instruction.operand = SymbolExtensions.GetMethodInfo(() => IncappedTargetCheck(null, null));
						found2 = true;
					}
					yield return instruction;
					last = instruction;
				}

				if (!found1 || !found2)
					Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);
			}
		}
		[HarmonyPatch(typeof(Stance_Warmup))]
		[HarmonyPatch(nameof(Stance_Warmup.StanceTick))]
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
			static IEnumerable<MethodBase> TargetMethods()
			{
				var m_Downed = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Downed));
				return typeof(Toils_Jump)
					.InnerMethodsStartingWith("<JumpIfTargetDowned>")
					.Where(method => PatchProcessor.GetCurrentInstructions(method).Any(code => code.Calls(m_Downed)));
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				return Tools.DownedReplacer(instructions);
			}
		}
		[HarmonyPatch(typeof(Pawn_MindState))]
		[HarmonyPatch(nameof(Pawn_MindState.MeleeThreatStillThreat), MethodType.Getter)]
		static class Pawn_MindState_MeleeThreatStillThreat_Patch
		{
			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				return Tools.DownedReplacer(instructions);
			}
		}
		[HarmonyPatch]
		static class JobDriver_AttackStatic_TickAction_Patch
		{
			static IEnumerable<MethodBase> TargetMethods()
			{
				var m_Downed = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Downed));
				return typeof(JobDriver_AttackStatic)
					.InnerMethodsStartingWith("*")
					.Where(method => PatchProcessor.GetCurrentInstructions(method).Any(code => code.Calls(m_Downed)));
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				return Tools.DownedReplacer(instructions);
			}
		}
		[HarmonyPatch(typeof(TargetingParameters))]
		[HarmonyPatch(nameof(TargetingParameters.CanTarget))]
		static class TargetingParameters_CanTarget_Patch
		{
			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				return Tools.DownedReplacer(instructions);
			}
		}
		*/

		// makes downed zombie crawl rotated to their destination
		//
		[HarmonyPatch(typeof(PawnDownedWiggler))]
		[HarmonyPatch(nameof(PawnDownedWiggler.ProcessPostTickVisuals))]
		static class PawnDownedWiggler_WigglerTick_Patch
		{
			static void Postfix(PawnDownedWiggler __instance, Pawn ___pawn)
			{
				if (___pawn is not Zombie zombie || zombie.health.Downed == false)
					return;
				var vec = ___pawn.pather.Destination.Cell - ___pawn.Position;
				var pos = ___pawn.DrawPos;
				__instance.downedAngle = vec.AngleFlat + 15f * Mathf.Sin(6f * pos.x) * Mathf.Cos(6f * pos.z);
			}
		}
		[HarmonyPatch(typeof(PawnRenderer))]
		[HarmonyPatch(nameof(PawnRenderer.BodyAngle))]
		static class PawnRenderer_BodyAngle_Patch
		{
			static bool Prefix(Pawn ___pawn, PawnDownedWiggler ___wiggler, ref float __result)
			{
				if (___pawn is Zombie zombie && zombie.health.Downed)
				{
					var angle = ___wiggler.downedAngle + 360;
					if (zombie.currentDownedAngle == -1)
						zombie.currentDownedAngle = angle;
					zombie.currentDownedAngle = (zombie.currentDownedAngle * 15 + angle) / 16;
					__result = zombie.currentDownedAngle;
					return false;
				}
				return true;
			}
		}

		// make zombies without head not have a headstump
		//
		[HarmonyPatch(typeof(PawnGraphicSet))]
		[HarmonyPatch(nameof(PawnGraphicSet.HeadMatAt))]
		static class PawnGraphicSet_HeadMatAt_Patch
		{
			static void Postfix(Pawn ___pawn, bool stump, ref Material __result)
			{
				if (stump == false || ___pawn is not Zombie)
					return;
				__result = new Material(__result) { color = new Color(142f / 255f, 0, 0) };
			}
		}

		// update electrical zombie humming
		//
		[HarmonyPatch(typeof(Root_Play))]
		[HarmonyPatch(nameof(Root_Play.Update))]
		static class Root_Play_Update_Patch
		{
			static void Postfix()
			{
				var tickManager = Find.CurrentMap?.GetComponent<TickManager>();
				if (tickManager == null)
					return;
				tickManager.UpdateElectricalHumming();
				tickManager.UpdateTankMovement();
			}
		}

		// use default mod settings for quick test play
		//
		[HarmonyPatch(typeof(Root_Play))]
		[HarmonyPatch(nameof(Root_Play.SetupForQuickTestPlay))]
		static class Root_Play_SetupForQuickTestPlay_Patch
		{
			static void Postfix()
			{
				ZombieSettings.ApplyDefaults();
			}
		}

		[HarmonyPatch(typeof(PawnRenderer))]
		[HarmonyPatch(nameof(PawnRenderer.RenderPawnAt))]
		[HarmonyPatch(new Type[] { typeof(Vector3), typeof(Rot4?), typeof(bool) })]
		static class PawnRenderer_RenderPawnAt_Patch
		{
			static readonly float moteAltitute = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead);
			static Vector3 leftEyeOffset = new(-0.092f, 0f, -0.08f);
			static Vector3 rightEyeOffset = new(0.092f, 0f, -0.08f);

			static Vector3 toxicAuraOffset = new(0f, 0f, 0.1f);
			const float leanAngle = 15f;

			static readonly Color white50 = new(1f, 1f, 1f, 0.5f);

			static readonly Mesh bodyMesh = MeshPool.GridPlane(new Vector2(1.5f, 1.5f));
			static readonly Mesh bodyMesh_flipped = MeshPool.GridPlaneFlip(new Vector2(1.5f, 1.5f));

			static readonly Mesh headMesh = MeshPool.GridPlane(new Vector2(1.5f, 1.5f));
			static readonly Mesh headMesh_flipped = MeshPool.GridPlaneFlip(new Vector2(1.5f, 1.5f));

			static readonly Mesh shieldMesh = MeshPool.GridPlane(new Vector2(2f, 2f));
			static readonly Mesh shieldMesh_flipped = MeshPool.GridPlaneFlip(new Vector2(2f, 2f));

			[HarmonyPriority(Priority.First)]
			static bool Prefix(PawnRenderer __instance, Vector3 drawLoc)
			{
				if (__instance.graphics.pawn is not Zombie zombie)
					return true;

				if (zombie.needsGraphics)
				{
					var tickManager = zombie.Map?.GetComponent<TickManager>();
					if (tickManager != null)
						tickManager.AllZombies().DoIf(z => z.needsGraphics, z =>
						{
							z.needsGraphics = false;
							ZombieGenerator.AssignNewGraphics(z);
						});
					else
					{
						zombie.needsGraphics = false;
						ZombieGenerator.AssignNewGraphics(zombie);
					}
				}

				if (zombie.state == ZombieState.Emerging)
				{
					zombie.Render(__instance, drawLoc);
					return false;
				}

				return true;
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var list = instructions.ToList();
				var ret = list.Last();
				if (ret.opcode != OpCodes.Ret)
					Error("Expected ret in PawnRenderer.RenderPawnAt");
				ret.opcode = OpCodes.Ldarg_0;
				list.Add(new CodeInstruction(OpCodes.Ldarg_1));
				list.Add(CodeInstruction.Call(() => RenderExtras(null, Vector3.zero)));
				list.Add(new CodeInstruction(OpCodes.Ret));
				return list;
			}

			[HarmonyPriority(Priority.First)]
			static void Postfix(PawnRenderer __instance, Vector3 drawLoc)
			{
				if (__instance.graphics.pawn is not Zombie zombie)
					return;

				if (zombie.isAlbino && zombie.scream > 0)
				{
					var f1 = zombie.scream / 400f;
					var f2 = Mathf.Sin(Mathf.PI * f1);
					var f3 = Math.Max(0, Mathf.Sin(Mathf.PI * f1 * 1.5f));

					var mat = new Material(Constants.SCREAM)
					{
						color = new Color(1f, 1f, 1f, f2)
					};
					var size = f1 * 4f;
					var center = drawLoc + new Vector3(0, 0.1f, 0.25f);

					GraphicToolbox.DrawScaledMesh(Constants.screamMesh, mat, center, Quaternion.identity, size, size);

					mat = new Material(Constants.SCREAMSHADOW)
					{
						color = new Color(1f, 1f, 1f, f3)
					};
					var q = Quaternion.AngleAxis(f2 * 360f, Vector3.up);
					GraphicToolbox.DrawScaledMesh(MeshPool.plane20, mat, center, q, 1.5f, 1.5f);
				}

				if (zombie.Dead)
					return;

				if (zombie.IsRopedOrConfused)
				{
					var confLoc = drawLoc + new Vector3(0, moteAltitute / 2, 0.75f);
					if (zombie.Rotation == Rot4.West)
						confLoc.x -= 0.09f;
					if (zombie.Rotation == Rot4.East)
						confLoc.x += 0.09f;

					var t = GenTicks.TicksAbs;
					var n = t % 12;
					if (n > 6)
						n = 12 - n;
					var scale = 1f;
					if (zombie.ropedBy == null)
					{
						var ticks = GenTicks.TicksAbs;
						if (zombie.paralyzedUntil > ticks)
							scale = Mathf.Clamp((zombie.paralyzedUntil - ticks) / (float)(GenDate.TicksPerHour / 4), 0, 1);
					}
					GraphicToolbox.DrawScaledMesh(MeshPool.plane05, Constants.CONFUSED[n], confLoc, Quaternion.Euler(0, t, 0), scale, scale);
				}

				if (zombie.ropedBy != null && zombie.Spawned && zombie.Dead == false)
				{
					var f = zombie.RopingFactorTo(zombie.ropedBy);
					var n = f <= 0.5f ? 2 : (f <= 0.8f ? 1 : 0);
					var mat = Constants.RopeLineMat[n];
					GenDraw.DrawLineBetween(zombie.DrawPos.Yto0(), zombie.ropedBy.DrawPos.Yto0(), AltitudeLayer.PawnRope.AltitudeFor(), mat, 0.2f);
				}
			}

			// we don't use a postfix so that someone that patches and skips RenderPawnAt will also skip RenderExtras
			static void RenderExtras(PawnRenderer renderer, Vector3 drawLoc)
			{
				if (renderer.graphics.pawn is not Zombie zombie)
					return;
				if (zombie.state == ZombieState.Emerging || zombie.GetPosture() != PawnPosture.Standing)
					return;

				// general zombie drawing

				Verse.TickManager tm = null;
				var orientation = zombie.Rotation;

				if (zombie.IsSuicideBomber)
				{
					tm = Find.TickManager;
					var currentTick = tm.TicksAbs;
					var interval = (int)zombie.bombTickingInterval;
					if (currentTick >= zombie.lastBombTick + interval)
						zombie.lastBombTick = currentTick;
					else if (currentTick <= zombie.lastBombTick + interval / 2)
					{
						if (zombie.state != ZombieState.Emerging)
						{
							var bombLightLoc = drawLoc + new Vector3(0, 0.1f, -0.2f);
							var scale = 1f;
							if (orientation == Rot4.South || orientation == Rot4.North)
								bombLightLoc.z += 0.05f;
							if (orientation == Rot4.North)
							{ bombLightLoc.y -= 0.1f; scale = 1.5f; }
							if (orientation == Rot4.West)
							{ bombLightLoc.x -= 0.25f; bombLightLoc.z -= 0.05f; }
							if (orientation == Rot4.East)
							{ bombLightLoc.x += 0.25f; bombLightLoc.z -= 0.05f; }
							GraphicToolbox.DrawScaledMesh(MeshPool.plane10, Constants.BOMB_LIGHT, bombLightLoc, Quaternion.identity, scale, scale);
						}
					}
				}

				if (zombie.isHealer && zombie.state != ZombieState.Emerging && zombie.healInfo.Count > 0)
				{
					var i = 0;
					var isNotPaused = Find.TickManager.Paused == false;
					while (i < zombie.healInfo.Count)
					{
						var info = zombie.healInfo[i];
						if (info.step >= 60)
						{
							zombie.healInfo.RemoveAt(i);
							continue;
						}

						var beingHealedIndex = (int)GenMath.LerpDoubleClamped(0, 60, 0, 8, info.step);
						var mat = Constants.BEING_HEALED[beingHealedIndex];

						var healTarget = info.pawn;
						float angle = healTarget.drawer.renderer.BodyAngle();
						if (healTarget.Rotation == Rot4.West)
							angle -= leanAngle;
						if (healTarget.Rotation == Rot4.East)
							angle += leanAngle;
						var healingPos = healTarget.DrawPos + toxicAuraOffset;
						var quat = Quaternion.AngleAxis(angle, Vector3.up);
						GraphicToolbox.DrawScaledMesh(MeshPool.plane20, mat, healingPos, quat, 1.5f, 1.5f);
						GenDraw.DrawLineBetween(zombie.DrawPos, healingPos, GenDraw.LineMatCyan, 0.2f);

						if (isNotPaused)
							info.step++;
						i++;
					}
				}

				var location = drawLoc;
				location.y += Altitudes.AltInc / 2f;
				if (orientation == Rot4.North)
					location.y += Altitudes.AltInc / 12f;

				if (zombie.hasTankySuit > 0f && zombie.hasTankySuit <= 1f)
				{
					var n = (int)(zombie.hasTankySuit * 4f + 0.5f);

					var pos = location;
					var f = 25f * (zombie.pather.nextCellCostLeft / zombie.pather.nextCellCostTotal);
					pos.z += (Mathf.Max(0.5f, Mathf.Cos(f)) - 0.7f) / 20f;

					if (orientation == Rot4.South || orientation == Rot4.North)
					{
						var rot = Quaternion.identity;
						var frontBack = (int)(orientation == Rot4.South ? FacingIndex.South : FacingIndex.North);
						GraphicToolbox.DrawScaledMesh(bodyMesh, Constants.TANKYSUITS[frontBack][n], pos, rot, 1f, 1f);
					}
					else
					{
						var rot = Quaternion.identity;
						var mesh = orientation == Rot4.West ? bodyMesh_flipped : bodyMesh;
						GraphicToolbox.DrawScaledMesh(mesh, Constants.TANKYSUITS[(int)FacingIndex.East][n], pos, rot, 1f, 1f);
					}
				}

				if (zombie.hasTankyHelmet > 0f && zombie.hasTankyHelmet <= 1f)
				{
					var n = (int)(zombie.hasTankyHelmet * 4f + 0.5f);
					var headOffset = zombie.Drawer.renderer.BaseHeadOffsetAt(orientation);
					headOffset.y += Altitudes.AltInc / 2f;

					var pos = location;
					var f = 25f * (zombie.pather.nextCellCostLeft / zombie.pather.nextCellCostTotal);
					pos.z += (Mathf.Max(0.5f, Mathf.Cos(f + 0.8f)) - 0.7f) / 20f;

					if (orientation == Rot4.South || orientation == Rot4.North)
					{
						var rot = Quaternion.identity;
						var frontBack = (int)(orientation == Rot4.South ? FacingIndex.South : FacingIndex.North);
						GraphicToolbox.DrawScaledMesh(headMesh, Constants.TANKYHELMETS[frontBack][n], pos + headOffset, rot, 1f, 1f);
					}
					else
					{
						var rot = Quaternion.identity;
						var mesh = orientation == Rot4.West ? headMesh_flipped : headMesh;
						GraphicToolbox.DrawScaledMesh(mesh, Constants.TANKYHELMETS[(int)FacingIndex.East][n], pos + headOffset, rot, 1f, 1f);
					}
				}

				if (zombie.hasTankyShield > 0f && zombie.hasTankyShield <= 1f)
				{
					var n = (int)(zombie.hasTankyShield * 4f + 0.5f);
					var f = Mathf.PI * 4f * (zombie.pather.nextCellCostLeft / zombie.pather.nextCellCostTotal);

					if (orientation == Rot4.South || orientation == Rot4.North)
					{
						var x = Mathf.Sin(f) * 0.03f;
						var dx = x + (orientation == Rot4.South ? 0.2f : -0.2f);
						var dy = orientation == Rot4.South ? 0.2f : -0.2f;
						var dz = Mathf.Abs(Mathf.Cos(f) * 0.05f) + (orientation == Rot4.South ? -0.2f : 0.2f);
						var rot = Quaternion.Euler(0f, x * 100f, 0f);
						var mesh = orientation == Rot4.South ? shieldMesh : shieldMesh_flipped;
						GraphicToolbox.DrawScaledMesh(mesh, Constants.TANKYSHIELDS[(int)FacingIndex.South][n], drawLoc + new Vector3(dx, dy, dz), rot, 0.52f, 0.52f);
					}
					else
					{
						var dx = orientation == Rot4.West ? -0.45f : 0.45f;
						var dy = 0.3f;
						var dz = Mathf.Abs(Mathf.Cos(f) * 0.05f);
						var rot = Quaternion.Euler(0f, dx * 22f, 0f);
						var mesh = orientation == Rot4.West ? shieldMesh_flipped : shieldMesh;
						GraphicToolbox.DrawScaledMesh(mesh, Constants.TANKYSHIELDS[(int)FacingIndex.East][n], drawLoc + new Vector3(dx, dy, dz), rot, 0.62f, 0.62f);
					}
				}

				if (zombie.isToxicSplasher)
				{
					float angle = zombie.drawer.renderer.BodyAngle();
					if (zombie.Rotation == Rot4.West)
						angle -= leanAngle;
					if (zombie.Rotation == Rot4.East)
						angle += leanAngle;
					var quat = Quaternion.AngleAxis(angle, Vector3.up);

					var idx = ((GenTicks.TicksGame + zombie.thingIDNumber) / 10) % 8;
					if (idx >= 5)
						idx = 8 - idx;
					GraphicToolbox.DrawScaledMesh(MeshPool.plane20, Constants.TOXIC_AURAS[idx], drawLoc + toxicAuraOffset, quat, 1f, 1f);
				}

				if (zombie.isMiner)
				{
					var headOffset = zombie.Drawer.renderer.BaseHeadOffsetAt(orientation);
					headOffset.y += Altitudes.AltInc / 2f;

					var pos = location;
					var f = 25f * (zombie.pather.nextCellCostLeft / zombie.pather.nextCellCostTotal);
					pos.z += (Mathf.Max(0.5f, Mathf.Cos(f + 0.8f)) - 0.7f) / 20f;
					var helmetWiggleAngle = orientation == Rot4.South || orientation == Rot4.North ? 0f : (Mathf.Sin(f) + Mathf.Cos(f + zombie.HashOffset())) * 3f;
					if (orientation == Rot4.West)
						helmetWiggleAngle += 5f;
					if (orientation == Rot4.East)
						helmetWiggleAngle -= 5f;
					var rot = Quaternion.AngleAxis(helmetWiggleAngle, Vector3.up);
					GraphicToolbox.DrawScaledMesh(headMesh, Constants.MINERHELMET[orientation.AsInt][0], pos + headOffset, rot, 1f, 1f);
				}

				if (zombie.IsActiveElectric && zombie.health.Downed == false)
				{
					tm ??= Find.TickManager;
					var flicker = (tm.TicksAbs / (2 + zombie.thingIDNumber % 2) + zombie.thingIDNumber) % 3;
					if (flicker != 0 || tm.Paused)
					{
						var glowLoc = drawLoc;
						glowLoc.y -= Altitudes.AltInc / 2f;

						var mesh = MeshPool.humanlikeBodySet.MeshAt(orientation);
						var glowingMaterials = Constants.ELECTRIC_GLOWING[zombie.story.bodyType];
						var idx = orientation == Rot4.East || orientation == Rot4.West ? 0 : (orientation == Rot4.North ? 1 : 2);
						GraphicToolbox.DrawScaledMesh(mesh, glowingMaterials[idx], glowLoc, Quaternion.identity, 1f, 1f);
					}

					// stage: 0 2 4 6 8 10 12 14 16 18
					// shine: x - x x x  x  x  -  x  -
					// arc  : - - - x -  x  -  -  -  -
					// new  :                        x

					zombie.electricCounter--;
					if (zombie.electricCounter <= 0)
					{
						var stage = -zombie.electricCounter;

						if (stage == 0)
						{
							var info = SoundInfo.InMap(zombie);
							CustomDefs.ElectricShock.PlayOneShot(info);
						}

						if (stage == 0 || (stage >= 4 && stage <= 12) || stage == 16)
						{
							var behind = drawLoc;
							behind.x += 0.25f;
							behind.y -= 0.5f;
							//GraphicToolbox.DrawScaledMesh(MeshPool.plane20, Constants.ELECTRIC_SHINE, behind, quat, 1f, 1f);
						}

						if (stage == 6 || stage == 7 || stage == 10 || stage == 11)
						{
							if (Rand.Chance(0.1f))
								zombie.electricAngle = Rand.RangeInclusive(0, 359);
							var quat = Quaternion.Euler(0, zombie.electricAngle, 0);
							var idx = Rand.RangeInclusive(0, 3);
							GraphicToolbox.DrawScaledMesh(MeshPool.plane20, Constants.ELECTRIC_ARCS[idx], drawLoc, quat, 1.5f, 1.5f);
						}

						if (stage >= 18)
						{
							zombie.electricCounter = Rand.RangeInclusive(60, 180);
							if (Find.TickManager.Paused)
								zombie.electricCounter += Rand.RangeInclusive(300, 600);
							zombie.electricAngle = Rand.RangeInclusive(0, 359);
						}
					}

					if (zombie.absorbAttack.Count > 0)
					{
						var pair = zombie.absorbAttack.Pop();
						var idx = pair.Value;
						if (idx >= 0)
						{
							var facing = pair.Key;
							var center = drawLoc + Quaternion.AngleAxis(facing + 225f, Vector3.up) * new Vector3(-0.4f, 0, 0.4f);
							var quat = Quaternion.AngleAxis(facing + 225f, Vector3.up);
							GraphicToolbox.DrawScaledMesh(MeshPool.plane14, Constants.ELECTRIC_ABSORB[idx], center, quat, 1f, 1f);
							Tools.PlayAbsorb(zombie);
						}
						else if (idx == -2)
						{
							for (var facing = 0; facing < 360; facing += 90)
							{
								var center = drawLoc + Quaternion.AngleAxis(facing + 225f, Vector3.up) * new Vector3(-0.4f, 0, 0.4f);
								var quat = Quaternion.AngleAxis(facing + 225f, Vector3.up);
								GraphicToolbox.DrawScaledMesh(MeshPool.plane14, Constants.ELECTRIC_ABSORB[Rand.RangeInclusive(0, 3)], center, quat, 1f, 1f);
							}
							Tools.PlayAbsorb(zombie);
						}
					}
				}

				if (zombie.raging == 0 && zombie.isAlbino == false)
					return;

				// raging zombies and albino eyes drawing

				drawLoc.y = moteAltitute;
				var quickHeadCenter = drawLoc + new Vector3(0, 0, 0.35f);

				if (Find.CameraDriver.CurrentZoom <= CameraZoomRange.Middle)
				{
					tm ??= Find.TickManager;
					var blinkPeriod = 60 + zombie.thingIDNumber % 180; // between 2-5s
					var eyesOpen = (tm.TicksAbs % blinkPeriod) > 3;
					if (eyesOpen || tm.CurTimeSpeed == TimeSpeed.Paused)
					{
						// the following constant comes from PawnRenderer.RenderPawnInternal
						var loc = drawLoc + renderer.BaseHeadOffsetAt(orientation) + new Vector3(0, 0.0281250011f, 0);

						var x = zombie.sideEyeOffset.x;
						var z = zombie.sideEyeOffset.z;
						if (x != 0 && z != 0)
						{
							// not clear why 75 but it seems to fit
							var eyeX = x / 75f;
							var eyeZ = z / 75f;
							var eyeScale = zombie.isAlbino ? 0.25f : 0.5f;
							var eyeMat = zombie.isAlbino ? new Material(Constants.RAGE_EYE) { color = white50 } : Constants.RAGE_EYE;

							if (orientation == Rot4.West)
								GraphicToolbox.DrawScaledMesh(MeshPool.plane05, eyeMat, loc + new Vector3(-eyeX, 0, eyeZ), Quaternion.identity, eyeScale, eyeScale);

							else if (orientation == Rot4.East)
								GraphicToolbox.DrawScaledMesh(MeshPool.plane05, eyeMat, loc + new Vector3(eyeX, 0, eyeZ), Quaternion.identity, eyeScale, eyeScale);

							if (orientation == Rot4.South)
							{
								GraphicToolbox.DrawScaledMesh(MeshPool.plane05, eyeMat, quickHeadCenter + leftEyeOffset, Quaternion.identity, eyeScale, eyeScale);
								GraphicToolbox.DrawScaledMesh(MeshPool.plane05, eyeMat, quickHeadCenter + rightEyeOffset, Quaternion.identity, eyeScale, eyeScale);
							}
						}
					}
				}

				if (orientation == Rot4.West)
					quickHeadCenter.x -= 0.09f;
				if (orientation == Rot4.East)
					quickHeadCenter.x += 0.09f;

				if (zombie.isAlbino == false)
					GraphicToolbox.DrawScaledMesh(MeshPool.plane20, Constants.RAGE_AURAS[Find.CameraDriver.CurrentZoom], quickHeadCenter, Quaternion.identity, 1f, 1f);
			}
		}

		// patch to draw floating zombies
		//
		[HarmonyPatch(typeof(Map))]
		[HarmonyPatch(nameof(Map.MapUpdate))]
		static class Map_MapUpdate_Patch
		{
			static readonly Mesh fullMesh = MeshPool.GridPlane(new Vector2(8f, 8f));

			static bool Prepare() => SoSTools.isInstalled;

			static void Postfix(Map __instance)
			{
				if (WorldRendererUtility.WorldRenderedNow)
					return;
				if (ZombieSettings.Values.floatingZombies == false)
					return;
				if (Find.CurrentMap != __instance)
					return;
				if (__instance.Biome != SoSTools.sosOuterSpaceBiomeDef)
					return;

				var tickManager = __instance.GetComponent<TickManager>();
				if (tickManager == null)
					return;

				List<SoSTools.Floater> floaters;

				floaters = tickManager.floatingSpaceZombiesBack;
				if (floaters == null || floaters.Count < SoSTools.Floater.backCount)
					return;
				var mPos = UI.MouseMapPosition();
				for (var i = 0; i < floaters.Count; i++)
				{
					var floater = floaters[i];
					floater.Update(i, floaters.Count, mPos);
					var quat = Quaternion.Euler(0, floater.angle, 0);
					GraphicToolbox.DrawScaledMesh(fullMesh, floater.material, floater.position, quat, floater.Size.x, floater.Size.y);
				}

				floaters = tickManager.floatingSpaceZombiesFore;
				if (floaters == null || floaters.Count < SoSTools.Floater.foreCount)
					return;
				for (var i = 0; i < floaters.Count; i++)
				{
					var floater = floaters[i];
					floater.Update(i, floaters.Count, mPos);
					var quat = Quaternion.Euler(0, floater.angle, 0);
					GraphicToolbox.DrawScaledMesh(fullMesh, floater.material, floater.position, quat, floater.Size.x, floater.Size.y);
				}
			}
		}

		// patch to exclude any zombieland apparel from being used at all
		// (we fake our own apparel via the patch below)
		//
		[HarmonyPatch]
		static class PawnApparelGenerator_PossibleApparelSet_PairOverlapsAnything_Patch
		{
			static MethodBase TargetMethod()
			{
				var inner = AccessTools.Inner(typeof(PawnApparelGenerator), "PossibleApparelSet");
				return AccessTools.Method(inner, "PairOverlapsAnything");
			}

			[HarmonyPriority(Priority.First)]
			static bool Prefix(ThingStuffPair pair, ref bool __result)
			{
				if (pair.thing?.IsZombieDef() ?? false)
				{
					__result = true;
					return false;
				}
				if (pair.stuff?.IsZombieDef() ?? false)
				{
					__result = true;
					return false;
				}
				return true;
			}
		}

		// patch for giving zombies accessories like bomb vests or tanky suits
		//
		[HarmonyPatch(typeof(PawnGraphicSet))]
		[HarmonyPatch(nameof(PawnGraphicSet.ResolveApparelGraphics))]
		static class PawnGraphicSet_ResolveApparelGraphics_Patch
		{
			[HarmonyPriority(Priority.Last)]
			static void Postfix(PawnGraphicSet __instance)
			{
				if (__instance.pawn is not Zombie zombie)
					return;

				if (zombie.IsSuicideBomber)
				{
					var apparel = new Apparel() { def = CustomDefs.Apparel_BombVest };
					if (__instance.apparelGraphics.Any(a => a.sourceApparel.def == CustomDefs.Apparel_BombVest) == false)
						if (ApparelGraphicRecordGetter.TryGetGraphicApparel(apparel, BodyTypeDefOf.Hulk, out var record))
							__instance.apparelGraphics.Add(record);
				}
			}
		}

		// patch to inform zombie generator that apparel texture could not load
		[HarmonyPatch(typeof(Graphic_Multi))]
		[HarmonyPatch(nameof(Graphic_Multi.Init))]
		public static class Graphic_Multi_Init_Patch
		{
			public static bool suppressError = false;
			public static bool textureError = false;

			static void Error(string text)
			{
				textureError = true;
				if (suppressError == false)
					Error(text);
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var m1 = SymbolExtensions.GetMethodInfo(() => Error(""));
				var m2 = SymbolExtensions.GetMethodInfo(() => Error(""));
				return Transpilers.MethodReplacer(instructions, m1, m2);
			}
		}

		// patch for reducing the warmup smash time for raging zombies
		//
		[HarmonyPatch(typeof(Verb))]
		[HarmonyPatch(nameof(Verb.TryStartCastOn))]
		[HarmonyPatch(new Type[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
		static class Verb_TryStartCastOn_Patch
		{
			static int ModifyTicks(float seconds, Verb verb)
			{
				var ticks = seconds.SecondsToTicks();
				if (verb?.caster is Zombie zombie && (zombie.raging > 0 || zombie.wasMapPawnBefore))
				{
					var grid = zombie.Map.GetGrid();
					var count = grid.GetZombieCount(zombie.Position);
					if (count > 0)
						ticks /= count;
				}
				return ticks;
			}

			[HarmonyPriority(Priority.Last)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var m_SecondsToTicks = SymbolExtensions.GetMethodInfo(() => GenTicks.SecondsToTicks(0f));

				var found = false;
				foreach (var instruction in instructions)
				{
					if (instruction.Calls(m_SecondsToTicks))
					{
						yield return new CodeInstruction(OpCodes.Ldarg_0);
						instruction.opcode = OpCodes.Call;
						instruction.operand = SymbolExtensions.GetMethodInfo(() => ModifyTicks(0, null));
						found = true;
					}
					yield return instruction;
				}

				if (!found)
					Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);
			}
		}

		// patch for variable zombie stats (speed, pain, melee, dodge)
		//
		[HarmonyPatch(typeof(StatExtension))]
		[HarmonyPatch(nameof(StatExtension.GetStatValue))]
		static class StatExtension_GetStatValue_Patch
		{
			static readonly float defaultHumanMoveSpeed = ThingDefOf.Human.statBases.First(mod => mod.stat == StatDefOf.MoveSpeed).value;
			static readonly HashSet<StatDef> ignoredStats = new StatDef[]
			{
				DefDatabase<StatDef>.GetNamed("SmokeSensitivity", false),
				DefDatabase<StatDef>.GetNamed("Suppressability", false)
			}
			.OfType<StatDef>()
			.ToHashSet();

			[HarmonyPriority(Priority.First)]
			static bool Prefix(Thing thing, StatDef stat, ref float __result)
			{
				if (thing is not Zombie zombie)
					return true;

				if (stat == StatDefOf.PainShockThreshold)
				{
					if (zombie.wasMapPawnBefore)
					{
						__result = 4000f;
						return false;
					}
					if (zombie.raging > 0)
					{
						__result = 1000f;
						return false;
					}
					if (zombie.hasTankyShield != -1f || zombie.hasTankyHelmet != -1f || zombie.hasTankySuit != -1f)
					{
						__result = 5000f;
						return false;
					}

					var bodyType = zombie.story.bodyType;
					if (bodyType == BodyTypeDefOf.Thin)
					{
						__result = 0.1f;
						return false;
					}
					if (bodyType == BodyTypeDefOf.Hulk)
					{
						__result = 0.8f;
						return false;
					}
					else if (bodyType == BodyTypeDefOf.Fat)
					{
						__result = 10f;
						return false;
					}
					__result = 0.8f;
					return false;
				}

				if (stat == StatDefOf.MeleeHitChance)
				{
					if (zombie.wasMapPawnBefore)
					{
						__result = 1f;
						return false;
					}

					if (zombie.health.Downed)
					{
						__result = 0.1f;
						return false;
					}

					if (zombie.hasTankyShield != -1f)
					{
						__result = 1.0f;
						return false;
					}

					if (zombie.hasTankyHelmet != -1f || zombie.hasTankySuit != -1f)
					{
						__result = 0.9f;
						return false;
					}

					if (zombie.story.bodyType == BodyTypeDefOf.Fat)
					{
						__result = 0.8f;
						return false;
					}

					if (zombie.state == ZombieState.Tracking || zombie.raging > 0)
						__result = Constants.ZOMBIE_HIT_CHANCE_TRACKING;
					else
						__result = Constants.ZOMBIE_HIT_CHANCE_IDLE;
					return false;
				}

				if (stat == StatDefOf.MeleeDodgeChance)
				{
					if (zombie.wasMapPawnBefore)
					{
						__result = 0.9f;
						return false;
					}

					if (zombie.isAlbino)
						__result = 0f;
					else
						__result = 0.02f;
					return false;
				}

				if (stat == StatDefOf.MoveSpeed)
				{
					var tm = Find.TickManager;
					var multiplier = defaultHumanMoveSpeed / ZombieTicker.PercentTicking;

					if (zombie.health.Downed)
					{
						__result = (zombie.ropedBy != null ? 0.4f : 0.004f) * tm.TickRateMultiplier;
						return false;
					}

					if (zombie.IsTanky)
					{
						__result = 0.004f * multiplier * tm.TickRateMultiplier;
						return false;
					}

					var albinoSpeed = 1f;
					if (zombie.isAlbino)
					{
						var albinoPos = zombie.Position;
						var colonists = zombie.Map.mapPawns.FreeColonistsAndPrisonersSpawned;
						var minDistSquared = colonists.Any() ? colonists.Min(colonist => colonist.Position.DistanceToSquared(albinoPos)) : 450;
						albinoSpeed = GenMath.LerpDoubleClamped(36, 900, 5f, 1f, minDistSquared);
					}

					float speed;
					if (albinoSpeed > 1f || zombie.state == ZombieState.Tracking || zombie.raging > 0 || zombie.wasMapPawnBefore)
						speed = ZombieSettings.Values.moveSpeedTracking;
					else
						speed = ZombieSettings.Values.moveSpeedIdle;

					var factor = 1f;
					var bodyType = zombie.story.bodyType;
					if (bodyType == BodyTypeDefOf.Thin)
						factor = 0.8f;
					else if (bodyType == BodyTypeDefOf.Hulk)
						factor = 0.8f;
					else if (bodyType == BodyTypeDefOf.Fat)
						factor = 0.7f;

					__result = speed * factor * multiplier * albinoSpeed;
					if (zombie.wasMapPawnBefore)
						__result *= 2f;
					if (zombie.isDarkSlimer)
						__result /= 1.5f;
					if (zombie.isHealer)
						__result *= 0.9f;

					return false;
				}

				if (zombie.hasTankySuit != -1f || zombie.hasTankyHelmet != -1f)
				{
					if (stat == StatDefOf.ComfyTemperatureMin)
					{
						__result = -999;
						return false;
					}
					if (stat == StatDefOf.ComfyTemperatureMax)
					{
						__result = 999f;
						return false;
					}
				}

				if (ignoredStats.Contains(stat))
				{
					__result = 0f;
					return false;
				}

				return true;
			}
		}

		// patch for variable zombie damage factor
		//
		[HarmonyPatch(typeof(VerbProperties))]
		[HarmonyPatch(nameof(VerbProperties.GetDamageFactorFor), typeof(Tool), typeof(Pawn), typeof(HediffComp_VerbGiver))]
		static class Verb_GetDamageFactorFor_Patch
		{
			static void Postfix(Pawn attacker, ref float __result)
			{
				if (attacker is not Zombie zombie)
					return;

				if (zombie.hasTankyShield > 0f || zombie.hasTankyHelmet > 0f || zombie.hasTankySuit > 0f)
				{
					var val = 0f;
					if (zombie.hasTankyShield > 0f)
						val += 30f;
					if (zombie.hasTankyHelmet > 0f)
						val += 10f;
					if (zombie.hasTankySuit > 0f)
						val += 20f;
					__result *= val;
					return;
				}

				var settings = ZombieSettings.Values.damageFactor;
				var bodyType = zombie.story.bodyType;
				if (bodyType == BodyTypeDefOf.Thin)
					__result *= 0.5f * settings;
				else if (bodyType == BodyTypeDefOf.Hulk)
					__result *= 3f * settings;
				else if (bodyType == BodyTypeDefOf.Fat)
					__result *= 4f * settings;

				if (zombie.wasMapPawnBefore)
					__result *= 5f;
			}
		}

		// patch zombies having no genes
		//
		[HarmonyPatch(typeof(Pawn_GeneTracker))]
		[HarmonyPatch(nameof(Pawn_GeneTracker.AddGene))]
		[HarmonyPatch(new[] { typeof(Gene), typeof(bool) })]
		static class Pawn_GeneTracker_AddGene_Gene_Patch
		{
			static bool Prefix(Pawn_GeneTracker __instance, ref Gene __result)
			{
				var pawn = __instance.pawn;
				if (pawn is Zombie || pawn is ZombieSpitter)
				{
					__result = null;
					return false;
				}
				return true;
			}
		}
		[HarmonyPatch(typeof(Pawn_GeneTracker))]
		[HarmonyPatch(nameof(Pawn_GeneTracker.AddGene))]
		[HarmonyPatch(new[] { typeof(GeneDef), typeof(bool) })]
		static class Pawn_GeneTracker_AddGene_GeneDef_Patch
		{
			static bool Prefix(Pawn_GeneTracker __instance, ref Gene __result)
			{
				var pawn = __instance.pawn;
				if (pawn is Zombie || pawn is ZombieSpitter)
				{
					__result = null;
					return false;
				}
				return true;
			}
		}
		[HarmonyPatch(typeof(Pawn_StoryTracker))]
		[HarmonyPatch(nameof(Pawn_StoryTracker.SkinColorBase), MethodType.Getter)]
		static class Pawn_StoryTracker_SkinColorBase_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn_StoryTracker __instance, ref Color __result)
			{
				var pawn = __instance.pawn;
				if (pawn is Zombie || pawn is ZombieSpitter)
				{
					__result = Color.white;
					return false;
				}
				return true;
			}
		}

		// patch for zombies handling extreme weather
		//
		[HarmonyPatch(typeof(Thing))]
		[HarmonyPatch(nameof(Thing.AmbientTemperature), MethodType.Getter)]
		static class Thing_AmbientTemperature_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Thing __instance, ref float __result)
			{
				if (__instance is Zombie || __instance is ZombieSpitter || __instance is ZombieCorpse)
				{
					__result = 21f; // fake normal conditions
					return false;
				}
				return true;
			}
		}

		// add start/stop extracting zombie serum gizmo
		//
		[HarmonyPatch(typeof(PriorityWork))]
		[HarmonyPatch(nameof(PriorityWork.GetGizmos))]
		[StaticConstructorOnStartup]
		static class PriorityWork_GetGizmos_Patch
		{
			static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn ___pawn)
			{
				foreach (var gizmo in gizmos)
					yield return gizmo;

				if (ZombieSettings.Values.betterZombieAvoidance)
				{
					var gizmo = Gizmos.ZombieAvoidance(___pawn);
					if (gizmo != null)
						yield return gizmo;
				}
				if (ZombieSettings.Values.corpsesExtractAmount > 0)
				{
					var gizmo = Gizmos.ExtractSerum(___pawn);
					if (gizmo != null)
						yield return gizmo;
				}
				if (ZombieSettings.Values.hoursAfterDeathToBecomeZombie > 0)
				{
					var gizmo = Gizmos.DoubleTap(___pawn);
					if (gizmo != null)
						yield return gizmo;
				}
			}
		}

		// patch to set zombie bite injuries as non natural healing to avoid
		// the healing cross mote
		//
		[HarmonyPatch(typeof(HediffUtility))]
		[HarmonyPatch(nameof(HediffUtility.CanHealNaturally))]
		static class HediffUtility_CanHealNaturally_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Hediff_Injury hd, ref bool __result)
			{
				if (hd is not Hediff_Injury_ZombieBite zombieBite)
					return true;

				var pawn = zombieBite.pawn;

				if (pawn.RaceProps.Humanlike && pawn.RaceProps.IsFlesh
					&& AlienTools.IsFleshPawn(pawn) && SoSTools.IsHologram(pawn) == false
				)
				{
					var tendDuration = zombieBite.TendDuration;
					if (tendDuration != null)
					{
						var state = tendDuration.GetInfectionState();
						__result = (state != InfectionState.BittenNotVisible && state < InfectionState.BittenInfectable);
						return false;
					}
				}
				return true;
			}
		}

		// patch to allow amputation of biten body parts
		//
		[HarmonyPatch(typeof(Recipe_RemoveBodyPart))]
		[HarmonyPatch(nameof(Recipe_RemoveBodyPart.GetPartsToApplyOn))]
		static class Recipe_RemoveBodyPart_GetPartsToApplyOn_Patch
		{
			static List<Hediff_Injury_ZombieBite> tmpHediffInjuryZombieBite = new();

			[HarmonyPriority(Priority.Last)]
			static IEnumerable<BodyPartRecord> Postfix(IEnumerable<BodyPartRecord> parts, Pawn pawn, RecipeDef recipe)
			{
				foreach (var part in parts)
					yield return part;
				if (recipe != RecipeDefOf.RemoveBodyPart)
					yield break;

				tmpHediffInjuryZombieBite.Clear();
				pawn.health.hediffSet.GetHediffs(ref tmpHediffInjuryZombieBite);
				var bites = tmpHediffInjuryZombieBite.Select(bite => bite.Part);
				foreach (var bite in bites)
					yield return bite;
			}
		}

		// patch to keep zombie bite injuries even after tending if they have to stay around
		//
		[HarmonyPatch(typeof(Hediff))]
		[HarmonyPatch(nameof(Hediff.ShouldRemove), MethodType.Getter)]
		static class Hediff_ShouldRemove_Patch
		{
			[HarmonyPriority(Priority.Last)]
			static void Postfix(Hediff __instance, ref bool __result)
			{
				if (__result == false)
					return;
				var pawn = __instance.pawn;

				// do not remove our zombie hediffs from dead pawns
				if (__instance.pawn != null && __instance.pawn.Dead && __instance.def.IsZombieHediff())
				{
					__result = false;
					return;
				}

				if (__instance is not Hediff_Injury_ZombieBite zombieBite)
					return;

				if (pawn.RaceProps.Humanlike && pawn.RaceProps.IsFlesh
					&& AlienTools.IsFleshPawn(pawn) && SoSTools.IsHologram(pawn) == false
				)
				{
					var tendDuration = zombieBite.TendDuration;
					if (tendDuration != null)
					{
						var state = tendDuration.GetInfectionState();
						if (state == InfectionState.BittenNotVisible || state >= InfectionState.BittenInfectable)
							__result = false;
					}
				}
			}
		}

		// patch for making burning zombies keep their fire (even when it rains)
		//
		[HarmonyPatch(typeof(Fire))]
		[HarmonyPatch(nameof(Fire.VulnerableToRain))]
		static class Fire_VulnerableToRain_Patch
		{
			static void Postfix(Fire __instance, ref bool __result)
			{
				if (__result == false)
					return;

				if (__instance.parent is Zombie && ZombieSettings.Values.zombiesBurnLonger && Rand.Chance(0.2f))
					__result = false;
			}
		}

		// patch for making zombies burn slower
		//
		[HarmonyPatch(typeof(Fire))]
		[HarmonyPatch(nameof(Fire.DoFireDamage))]
		static class Fire_DoFireDamage_Patch
		{
			static int FireDamagePatch(float f, Pawn pawn)
			{
				var num = GenMath.RoundRandom(f);
				if (ZombieSettings.Values.zombiesBurnLonger == false)
					return num;

				if (pawn is not Zombie)
					return num;

				return Math.Max(2, num / 2);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var m_RoundRandom = SymbolExtensions.GetMethodInfo(() => GenMath.RoundRandom(0f));
				var m_FireDamagePatch = SymbolExtensions.GetMethodInfo(() => FireDamagePatch(0f, null));

				var list = instructions.ToList();
				var idx = list.FirstIndexOf(code => code.Calls(m_RoundRandom));
				if (idx > 0 && idx < list.Count())
				{
					list[idx].opcode = OpCodes.Ldarg_1; // first argument of instance method
					list[idx].operand = null;
					list.Insert(idx + 1, new CodeInstruction(OpCodes.Call, m_FireDamagePatch));
				}
				else
					Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);

				return list;
			}
		}

		// patch for excluding burning zombies from total fire count
		//
		[HarmonyPatch(typeof(FireWatcher))]
		[HarmonyPatch(nameof(FireWatcher.UpdateObservations))]
		static class FireWatcher_UpdateObservations_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, IEnumerable<CodeInstruction> instructions)
			{
				var found1 = false;
				var found2 = false;

				var n = 0;
				var label = generator.DefineLabel();

				foreach (var instruction in instructions)
				{
					yield return instruction;

					if (instruction.opcode == OpCodes.Stloc_2)
					{
						yield return new CodeInstruction(OpCodes.Ldloc_2);
						yield return new CodeInstruction(OpCodes.Ldfld, typeof(AttachableThing).Field(nameof(AttachableThing.parent)));
						yield return new CodeInstruction(OpCodes.Isinst, typeof(Zombie));
						yield return new CodeInstruction(OpCodes.Brtrue, label);
						found1 = true;
					}

					if (n >= 0 && instruction.opcode == OpCodes.Add)
						n++;

					if (instruction.opcode == OpCodes.Ldloc_1 && n == 2)
					{
						instruction.labels.Add(label);
						n = -1;
						found2 = true;
					}
				}

				if (!found1 || !found2)
					Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);
			}
		}

		// patch to prevent errors in combat log
		//
		[HarmonyPatch(typeof(DamageWorker.DamageResult))]
		[HarmonyPatch(nameof(DamageWorker.DamageResult.AssociateWithLog))]
		public static class DamageWorker_DamageResult_AssociateWithLog_Patch
		{
			static bool Prefix(DamageWorker.DamageResult __instance)
			{
				return __instance.hitThing is not Zombie;
			}
		}

		// patch to prevent errors for empty corpses (seems like a bug in rimworld)
		//
		[HarmonyPatch(typeof(Alert_ColonistLeftUnburied))]
		[HarmonyPatch(nameof(Alert_ColonistLeftUnburied.IsCorpseOfColonist))]
		public static class Alert_ColonistLeftUnburied_IsCorpseOfColonist_Patch
		{
			static bool Prefix(Corpse corpse, ref bool __result)
			{
				if (corpse?.InnerPawn == null)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}

		// patch to make zombies in tar smoke un-hitable
		//
		[HarmonyPatch(typeof(Verb))]
		[HarmonyPatch(nameof(Verb.CanHitTargetFrom))]
		static class ShotReport_HitReportFor_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Verb __instance, LocalTargetInfo targ, ref bool __result)
			{
				if (__instance.IsMeleeAttack)
					return true;
				var thing = targ.Thing;
				var map = thing?.Map;
				if (map == null)
					return true;
				if (thing.Position.GetGas(map)?.def != CustomDefs.TarSmoke)
					return true;
				__result = false;
				return false;
			}
		}

		// patch to make tar smoke really affect hit chance a lot
		//
		[HarmonyPatch(typeof(ShotReport))]
		[HarmonyPatch(nameof(ShotReport.AimOnTargetChance_StandardTarget), MethodType.Getter)]
		public static class ShotReport_AimOnTargetChance_StandardTarget_Patch
		{
			public static bool Prefix(ref float __result, List<CoverInfo> ___covers)
			{
				if (___covers.Any(c => c.thingInt.def == CustomDefs.TarSmoke) == false)
					return true;
				__result = 0f;
				return false;
			}
		}

		// patch to deactivate electrical zombies with emp
		//
		[HarmonyPatch(typeof(StunHandler))]
		[HarmonyPatch(nameof(StunHandler.Notify_DamageApplied))]
		static class DamageFlasher_Notify_DamageApplied_Patch
		{
			[HarmonyPriority(Priority.First)]
			static void Prefix(StunHandler __instance, DamageInfo dinfo)
			{
				if (dinfo.Def != DamageDefOf.EMP && dinfo.Def != DamageDefOf.Stun)
					return;
				if (__instance.parent is Zombie zombie && zombie.Downed == false && zombie.Dead == false)
					if (zombie.IsActiveElectric)
						zombie.DisableElectric((int)(dinfo.Amount * 60));
			}
		}

		// patch to remove non-melee damage from electrifier zombies
		//
		[HarmonyPatch(typeof(DamageWorker_AddInjury))]
		[HarmonyPatch(nameof(DamageWorker_AddInjury.ApplyDamageToPart))]
		public static class DamageWorker_AddInjury_ApplyDamageToPart_Patch
		{
			static bool Prefix(ref DamageInfo dinfo, Pawn pawn)
			{
				if (pawn is not Zombie zombie)
					return true;

				if (zombie.health.Downed)
					return true;

				if (zombie.wasMapPawnBefore)
				{
					dinfo.SetAllowDamagePropagation(false);
					dinfo.SetInstantPermanentInjury(false);
					var f1 = GenMath.LerpDouble(0, 5, 1, 10, Tools.Difficulty()) + (ShipCountdown.CountingDown ? 2f : 1f);
					dinfo.SetAmount(dinfo.Amount / f1);
					return true;
				}

				var def = dinfo.Def;

				if (zombie.isAlbino)
					return def.isExplosive || Rand.Chance(0.25f);

				if (zombie.isDarkSlimer)
				{
					var pos = zombie.Position;
					var map = zombie.Map;
					if (map != null && pos.GetGas(map) == null)
					{
						var difficulty = Tools.Difficulty();
						var alpha = GenMath.LerpDoubleClamped(0, 5, 0.25f, 1f, difficulty);
						var min = GenMath.LerpDoubleClamped(0, 5, 2, 60, difficulty);
						var max = GenMath.LerpDoubleClamped(0, 5, min, 90, difficulty);
						CustomDefs.TarSmoke.graphicData.color = new Color(0, 0, 0, alpha);
						CustomDefs.TarSmoke.gas.expireSeconds = new FloatRange(min, max);
						GenExplosion.DoExplosion(pos, map, 1 + difficulty, DamageDefOf.Smoke, null, (int)(50 * difficulty), -1f, CustomDefs.TarSmokePop, null, null, null, CustomDefs.TarSmoke, 1f, 1, GasType.BlindSmoke, false, null, 0f, 1, 0f, false, null, null);
					}
				}

				if (zombie.IsActiveElectric)
				{
					if (def.isRanged == false || (def.isRanged && def.isExplosive))
						return true;

					var indices = new List<int>() { 0, 1, 2, 3 };
					indices.Shuffle();
					for (var i = 0; i < Rand.RangeInclusive(1, 4); i++)
					{
						zombie.absorbAttack.Add(new KeyValuePair<float, int>(dinfo.Angle, i));
						if (Rand.Chance(0.9f))
							zombie.absorbAttack.Add(new KeyValuePair<float, int>(0f, -1));
					}
					return false;
				}

				var f2 = Mathf.Max(1f, Tools.Difficulty()) + (ShipCountdown.CountingDown ? 2f : 1f);
				dinfo.SetAmount(dinfo.Amount / f2);
				return true;
			}
		}

		// patch to prevent damage if zombie has armor
		//
		[HarmonyPatch(typeof(ArmorUtility))]
		[HarmonyPatch(nameof(ArmorUtility.GetPostArmorDamage))]
		public static class ArmorUtility_GetPostArmorDamage_Patch
		{
			static void ApplyDamage(ref float armor, ref float amount, float reducer)
			{
				var damage = amount / reducer;
				if (armor >= damage)
				{
					armor -= damage;
					amount = 0f;
					return;
				}
				amount = (damage - armor) * reducer;
				armor = -1f;
			}

			[HarmonyPriority(Priority.First)]
			public static bool Prefix(Pawn pawn, ref float amount, BodyPartRecord part, float armorPenetration, out bool deflectedByMetalArmor, out bool diminishedByMetalArmor, ref float __result)
			{
				deflectedByMetalArmor = false;
				diminishedByMetalArmor = false;

				if (pawn is not Zombie zombie)
					return true;

				var penetration = Math.Max(armorPenetration - 0.25f, 0f);
				amount *= (1f + 2 * penetration);

				var skip = false;
				var difficulty = Tools.Difficulty();

				if (amount > 0f && zombie.hasTankyShield > 0f)
				{
					ApplyDamage(ref zombie.hasTankyShield, ref amount, 1f + difficulty * 100f);
					diminishedByMetalArmor |= zombie.hasTankyShield > 0f;
					__result = -1f;
					skip = true;
				}

				var fakeHeadShot = (zombie.hasTankySuit <= 0f && Rand.Chance(0.25f));
				if (part.groups.Contains(BodyPartGroupDefOf.FullHead) || fakeHeadShot)
				{
					if (amount > 0f && zombie.hasTankyHelmet > 0f)
					{
						ApplyDamage(ref zombie.hasTankyHelmet, ref amount, 1f + difficulty * 10f);
						diminishedByMetalArmor |= zombie.hasTankyHelmet > 0f;
						__result = -1f;
						skip = true;
					}
				}

				if (amount > 0f && zombie.hasTankySuit > 0f)
				{
					ApplyDamage(ref zombie.hasTankySuit, ref amount, 1f + difficulty * 50f);
					diminishedByMetalArmor |= zombie.hasTankySuit > 0f;
					__result = -1f;
					skip = true;
				}

				deflectedByMetalArmor = amount == 0f;
				if (diminishedByMetalArmor)
					Tools.PlayTink(zombie);

				// still a tough zombie even if we hit the body but some armor is left
				if (amount > 0f && (zombie.hasTankyHelmet > 0f || zombie.hasTankySuit > 0f))
				{
					var toughnessLevel = Tools.Difficulty() / 2;
					amount = (amount + toughnessLevel) / (toughnessLevel + 1);
				}

				return skip == false;
			}

			static bool GetAfterArmorDamagePrefix(ref DamageInfo originalDinfo, Pawn pawn, BodyPartRecord hitPart, out bool shieldAbsorbed, ref DamageInfo __result)
			{
				__result = originalDinfo;
				var dinfo = new DamageInfo(originalDinfo);
				var dmgAmount = dinfo.Amount;

				shieldAbsorbed = false;
				if (pawn == null || hitPart == null)
					return true;
				var prefixResult = 0f;
				var result = Prefix(pawn, ref dmgAmount, hitPart, dinfo.ArmorPenetrationInt, out var deflect, out var diminish, ref prefixResult);
				if (result && originalDinfo.Instigator != null)
					return (pawn.Spawned && pawn.Dead == false
						&& pawn.Destroyed == false
						&& originalDinfo.Instigator.Spawned
						&& originalDinfo.Instigator.Destroyed == false);

				dinfo.SetAmount(dmgAmount);
				originalDinfo = dinfo;
				__result = dinfo;
				shieldAbsorbed = deflect || diminish;

				return false;
			}
		}

		// patch for not slowing down time if pawn attacks a zombie
		//
		[HarmonyPatch(typeof(Verb))]
		[HarmonyPatch(nameof(Verb.CausesTimeSlowdown))]
		class Verb_CausesTimeSlowdown_Patch
		{
			static void Postfix(Verb __instance, ref bool __result, LocalTargetInfo castTarg)
			{
				var caster = __instance.caster;

				if (__result == false || castTarg == null || castTarg.HasThing == false)
					return;
				if (caster is Zombie || caster is ZombieSpitter)
					return;

				if (castTarg.Thing is not Zombie zombie)
					return;

				var dist = caster.Position.DistanceToSquared(zombie.Position);
				if (dist >= Constants.HUMAN_PHEROMONE_RADIUS * Constants.HUMAN_PHEROMONE_RADIUS)
					__result = false;
			}
		}

		// patch to exclude electric zombies from ranged combat
		//
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch(nameof(Pawn.TryGetAttackVerb))]
		static class Pawn_TryGetAttackVerb_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn __instance, Thing target, ref Verb __result)
			{
				// zombie spitter never attacks or responds to attacks
				if (__instance is ZombieSpitter)
				{
					__result = null;
					return false;
				}

				if (target is not Zombie zombie || zombie.IsActiveElectric == false)
					return true;

				if (__instance.equipment?.Primary != null && __instance.equipment.PrimaryEq.PrimaryVerb.targetParams.canTargetLocations)
					return true;

				__result = __instance.meleeVerbs.TryGetMeleeVerb(target);
				return false;
			}
		}

		// patch for simpler attack verb handling on zombies (story work tab confict)
		//
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch(nameof(Pawn.TryStartAttack))]
		static class Pawn_TryStartAttack_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn __instance, LocalTargetInfo targ, ref bool __result)
			{
				if (__instance is not Zombie)
					return true;

				var verb = __instance.TryGetAttackVerb(targ.Thing);
				__result = verb != null && verb.TryStartCastOn(targ, false, true);
				return false;
			}
		}

		// patch to handle various things when someone dies
		//
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch(nameof(Pawn.Kill))]
		static class Pawn_Kill_Patch
		{
			[HarmonyPriority(Priority.First)]
			static void Prefix(Pawn __instance)
			{
				// remove current job of zombie immediately when killed
				if (__instance is Zombie zombie)
				{
					if (zombie.jobs != null && zombie.CurJob != null)
						zombie.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
					Tools.DropLoot(zombie);
					return;
				}

				// make spitters drop loot
				if (__instance is ZombieSpitter)
				{
					Tools.DropLoot(__instance);
					return;
				}

				var pawn = __instance;
				var raceProps = pawn.RaceProps;

				if (raceProps.Humanlike == false || raceProps.IsFlesh == false)
					return;

				if (AlienTools.IsFleshPawn(pawn) == false || SoSTools.IsHologram(pawn))
					return;

				if (Customization.CannotBecomeZombie(pawn))
					return;

				var hediffSet = pawn.health?.hediffSet;
				if (hediffSet == null)
					return;

				// flag zombie bites to be infectious when pawn dies
				pawn.GetHediffsList<Hediff_Injury_ZombieBite>()
					.Where(zombieBite => zombieBite.TendDuration.GetInfectionState() >= InfectionState.BittenInfectable)
					.Do(zombieBite => zombieBite.mayBecomeZombieWhenDead = true);

				// if death means becoming a zombie, install zombie infection
				if (ZombieSettings.Values.hoursAfterDeathToBecomeZombie > -1)
				{
					try
					{
						var brain = hediffSet.GetBrain();
						if (brain != null)
						{
							var hediff = HediffMaker.MakeHediff(CustomDefs.ZombieInfection, pawn, brain) as Hediff_ZombieInfection;
							hediff.InitializeExpiringDate();
							hediffSet.AddDirect(hediff, null, null);
						}
					}
					catch
					{
					}
				}
			}
		}

		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch(nameof(Pawn.Destroy))]
		static class Pawn_Destroy_Patch
		{
			[HarmonyPriority(Priority.First)]
			static void Prefix(Pawn __instance)
			{
				_ = ZombieAreaManager.pawnsInDanger.Remove(__instance);
				if (__instance is not Zombie && __instance.RaceProps.Humanlike)
					ColonistSettings.Values.RemoveColonist(__instance);
			}
		}

		// patch headshot to kill zombies right away
		//
		[HarmonyPatch(typeof(DamageWorker_AddInjury))]
		[HarmonyPatch(nameof(DamageWorker_AddInjury.IsHeadshot))]
		static class DamageWorker_AddInjury_IsHeadshot_Patch
		{
			static void Postfix(Pawn pawn, bool __result)
			{
				if (__result == false)
					return;
				if (pawn is Zombie zombie)
					zombie.state = ZombieState.ShouldDie;
			}
		}
		//
		[HarmonyPatch(typeof(HediffSet))]
		[HarmonyPatch(nameof(HediffSet.AddDirect))]
		static class HediffSet_AddDirect_Patch
		{
			static void Postfix(Pawn ___pawn, Hediff hediff)
			{
				if (___pawn is not Zombie zombie)
					return;
				if (hediff == null)
					return;
				if (hediff.Part != null && hediff.def.isBad && hediff.Part.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource))
					zombie.state = ZombieState.ShouldDie;
			}
		}

		// simplify fire lookup by updating isOnFire on zombies
		//
		[HarmonyPatch(typeof(CompAttachBase))]
		[HarmonyPatch(nameof(CompAttachBase.AddAttachment))]
		static class CompAttachBase_AddAttachment_Patch
		{
			static void Postfix(AttachableThing t, ThingWithComps ___parent)
			{
				if (t.def != ThingDefOf.Fire)
					return;
				if (___parent is Zombie zombie)
					zombie.isOnFire = true;
			}
		}
		//
		[HarmonyPatch(typeof(CompAttachBase))]
		[HarmonyPatch(nameof(CompAttachBase.RemoveAttachment))]
		static class CompAttachBase_RemoveAttachment_Patch
		{
			static void Postfix(AttachableThing t, ThingWithComps ___parent, List<AttachableThing> ___attachments)
			{
				if (t.def != ThingDefOf.Fire)
					return;
				if (___parent is Zombie zombie)
					zombie.isOnFire = ___attachments.Any(a => a.def == ThingDefOf.Fire);
			}
		}

		// patch for disallowing social interaction with zombies
		//
		[HarmonyPatch(typeof(RelationsUtility))]
		[HarmonyPatch(nameof(RelationsUtility.HasAnySocialMemoryWith))]
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
		[HarmonyPatch(nameof(Pawn_RelationsTracker.OpinionOf))]
		static class Pawn_RelationsTracker_OpinionOf_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, IEnumerable<CodeInstruction> instructions)
			{
				var returnZeroLabel = generator.DefineLabel();

				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldfld, typeof(Pawn_RelationsTracker).Field("pawn"));
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
		[HarmonyPatch(nameof(RelationsUtility.PawnsKnowEachOther))]
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
		[HarmonyPatch(nameof(ThoughtHandler.GetSocialThoughts))]
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
		[HarmonyPatch(nameof(SituationalThoughtHandler.AppendSocialThoughts))]
		static class SituationalThoughtHandler_AppendSocialThoughts_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(SituationalThoughtHandler __instance, Pawn otherPawn)
			{
				return !(otherPawn is Zombie || __instance.pawn is Zombie);
			}
		}
		[HarmonyPatch(typeof(Corpse))]
		[HarmonyPatch(nameof(Corpse.GiveObservedThought))]
		static class Corpse_GiveObservedThought_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Corpse __instance)
			{
				return __instance is not ZombieCorpse;
			}
		}
		[HarmonyPatch(typeof(Corpse))]
		[HarmonyPatch(nameof(Corpse.GiveObservedHistoryEvent))]
		static class Corpse_GiveObservedHistoryEvent_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Corpse __instance)
			{
				return __instance is not ZombieCorpse;
			}
		}

		// patch for disallowing thoughts on zombies
		//
		[HarmonyPatch(typeof(ThoughtUtility))]
		[HarmonyPatch(nameof(ThoughtUtility.CanGetThought))]
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
		[HarmonyPatch(nameof(ForbidUtility.SetForbiddenIfOutsideHomeArea))]
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
		[HarmonyPatch(nameof(Pawn_InteractionsTracker.TryInteractWith))]
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
		[HarmonyPatch(nameof(Pawn_InteractionsTracker.InteractionsTrackerTick))]
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
		[HarmonyPatch(nameof(PawnNameColorUtility.PawnNameColorOf))]
		static class PawnNameColorUtility_PawnNameColorOf_Patch
		{
			static Color zombieLabelColor = new(0.7f, 1f, 0.7f);

			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn pawn, ref Color __result)
			{
				if (pawn is Zombie zombie && zombie.wasMapPawnBefore)
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
		[HarmonyPatch(nameof(ThingSelectionUtility.SelectableByMapClick))]
		static class ThingSelectionUtility_SelectableByMapClick_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Thing t, ref bool __result)
			{
				if (t is Zombie zombie && zombie.wasMapPawnBefore)
				{
					__result = true;
					return false;
				}
				return true;
			}
		}

		// patch to exclude anything zombie from listings
		// TODO: prevents zombie extract from showing up https://discord.com/channels/900081000942567454/900149546787680256/1137350430263885834
		//
		[HarmonyPatch(typeof(ThingFilter))]
		[HarmonyPatch(nameof(ThingFilter.SetAllow))]
		[HarmonyPatch(new Type[] { typeof(ThingDef), typeof(bool) })]
		static class ThingFilter_SetAllow_Patch
		{
			public static bool IsZombieDef(ThingDef thingDef)
			{
				if (thingDef == null)
					return false;

				var defName = thingDef.defName?.ToLower();
				if (defName != null)
				{
					if (defName.Contains("zombie")
						&& defName.Contains("serum") == false
						&& defName.Contains("extract") == false)
						return true;
				}

				var description = thingDef.description?.ToLower();
				if (description != null)
				{
					if (description.Contains("zombie")
						&& description.Contains("serum") == false
						&& description.Contains("extract") == false)
						return true;
				}

				return false;
			}

			static bool Prefix(ThingDef thingDef)
			{
				return IsZombieDef(thingDef) == false;
			}
		}
		[HarmonyPatch(typeof(Listing_TreeThingFilter))]
		[HarmonyPatch(nameof(Listing_TreeThingFilter.Visible))]
		[HarmonyPatch(new Type[] { typeof(ThingDef) })]
		static class Listing_TreeThingFilter_Visible_Patch
		{
			static bool Prefix(ThingDef td, ref bool __result)
			{
				if (ThingFilter_SetAllow_Patch.IsZombieDef(td))
				{
					__result = false;
					return false;
				}
				return true;
			}
		}

		// patch for a custom zombie corpse class
		//
		[HarmonyPatch(typeof(ThingMaker))]
		[HarmonyPatch(nameof(ThingMaker.MakeThing))]
		static class ThingMaker_MakeThing_Patch
		{
			static void Prefix(ThingDef def)
			{
				if (def == null || def.IsCorpse == false)
					return;
				if (def.ingestible == null)
					return;
				if (def.ingestible.sourceDef is ThingDef_Zombie)
				{
					def.selectable = false;
					def.smeltable = false;
					def.mineable = false;
					def.stealable = false;
					def.burnableByRecipe = false;
					def.canLoadIntoCaravan = false;
					def.neverMultiSelect = true;
					def.butcherProducts = null;
					def.smeltProducts = null;
					def.drawGUIOverlay = false;
					def.hasTooltip = false;
					def.hideAtSnowDepth = 99f;
					def.inspectorTabs = new List<Type>();
					def.passability = Traversability.Standable;
					def.stackLimit = 1;
					def.thingClass = typeof(ZombieCorpse);
				}
				if (def.ingestible.sourceDef is ThingDef_ZombieSpitter)
				{
					def.selectable = false;
					def.smeltable = false;
					def.mineable = false;
					def.stealable = false;
					def.burnableByRecipe = false;
					def.canLoadIntoCaravan = false;
					def.neverMultiSelect = true;
					def.butcherProducts = null;
					def.smeltProducts = null;
					def.drawGUIOverlay = false;
					def.hasTooltip = false;
					def.hideAtSnowDepth = 99f;
					def.inspectorTabs = new List<Type>();
					def.passability = Traversability.Standable;
					def.stackLimit = 1;
					def.thingClass = typeof(ZombieSpitterCorpse);
				}
			}
		}

		// patch to make zombies always awake
		//
		[HarmonyPatch(typeof(PawnCapacitiesHandler))]
		[HarmonyPatch(nameof(PawnCapacitiesHandler.CanBeAwake), MethodType.Getter)]
		static class PawnCapacitiesHandler_CanBeAwake_Patch
		{
			static void Postfix(Pawn ___pawn, ref bool __result)
			{
				if (___pawn is Zombie)
					__result = true;
			}
		}

		// patch to handle targets downed so that we update our grid
		//
		[HarmonyPatch(typeof(Pawn_HealthTracker))]
		[HarmonyPatch(nameof(Pawn_HealthTracker.MakeDowned))]
		static class Pawn_HealthTracker_MakeDowned_Patch
		{
			static void Postfix(Pawn ___pawn)
			{
				if (___pawn is Zombie || ___pawn is ZombieSpitter)
					return;
				if (___pawn == null || ___pawn.Map == null)
					return;

				var grid = ___pawn.Map.GetGrid();
				if (Constants.KILL_CIRCLE_RADIUS_MULTIPLIER > 0)
				{
					var timestamp = grid.GetTimestamp(___pawn.Position);
					if (timestamp > 0)
					{
						var radius = Tools.RadiusForPawn(___pawn) * Constants.KILL_CIRCLE_RADIUS_MULTIPLIER;
						radius /= ZombieSettings.Values.zombieInstinct.HalfToDoubleValue();
						Tools.GetCircle(radius).Do(vec =>
						{
							var pos = ___pawn.Position + vec;
							var cell = grid.GetPheromone(pos, false);
							if (cell != null && cell.timestamp > 0 && cell.timestamp <= timestamp)
								cell.timestamp = 0;
						});
					}
				}
				grid.SetTimestamp(___pawn.Position, 0);
			}
		}

		// patch to update twinkie graphics
		//
		[HarmonyPatch(typeof(Game))]
		[HarmonyPatch(nameof(Game.FinalizeInit))]
		static class Game_FinalizeInit_Patch
		{
			static void Postfix()
			{
				Tools.EnableTwinkie(ZombieSettings.Values.replaceTwinkie);
				CustomDefs.Zombie.race.baseHealthScale = ZombieSettings.Values.healthFactor;
			}
		}

		// patches to update our zombie count grid
		//
		[HarmonyPatch(typeof(Map))]
		[HarmonyPatch(nameof(Map.FinalizeLoading))]
		static class Map_FinalizeLoading_Patch
		{
			static void Prefix(Map __instance)
			{
				var grid = __instance.GetGrid();
				grid.IterateCellsQuick(cell => cell.zombieCount = 0);
			}
		}

		// patches to keep track of frame time
		//
		[HarmonyPatch(typeof(Root))]
		[HarmonyPatch(nameof(Root.Update))]
		static class Root_Update_Patch
		{
			static void Prefix()
			{
				ZombielandMod.frameWatch.Restart();
			}
		}

		// patches to clean up after us
		//
		[HarmonyPatch(typeof(Root))]
		[HarmonyPatch(nameof(Root.Shutdown))]
		static class Root_Shutdown_Patch
		{
			static void Prefix()
			{
				Tools.avoider.running = false;

				// var maps = Find.Maps;
				// if (maps != null)
				// 	foreach (var map in maps)
				// 		map?.GetComponent<TickManager>()?.MapRemoved();
				// 
				// MemoryUtility.ClearAllMapsAndWorld();
			}
		}

		// convert dying infected pawns when they start rotting
		//
		[HarmonyPatch(typeof(Corpse))]
		[HarmonyPatch(nameof(Corpse.RotStageChanged))]
		static class Corpse_RotStageChanged_Patch
		{
			static void Postfix(Corpse __instance)
			{
				var pawn = __instance.InnerPawn;
				if (pawn == null || pawn is Zombie || pawn.health == null || pawn.RaceProps.Humanlike == false)
					return;

				var rotStage = __instance.GetRotStage();
				if (rotStage == RotStage.Fresh || rotStage == RotStage.Dessicated)
					return;

				var hasBrain = pawn.health.hediffSet.GetBrain() != null;
				if (hasBrain == false)
					return;

				var shouldBecomeZombie = pawn.GetHediffsList<Hediff_Injury_ZombieBite>()
					.Any(zombieBite => zombieBite.TendDuration.GetInfectionState() >= InfectionState.BittenInfectable);

				if (shouldBecomeZombie)
				{
					var map = ThingOwnerUtility.GetRootMap(__instance);
					if (map != null)
						Tools.QueueConvertToZombie(__instance, map);
				}
			}
		}

		[HarmonyPatch(typeof(Corpse))]
		[HarmonyPatch(nameof(Corpse.TickRare))]
		static class Corpse_TickRare_Patch
		{
			static List<Hediff_ZombieInfection> tmpHediffZombieInfections = new();

			static void Postfix(Corpse __instance)
			{
				var pawn = __instance.InnerPawn;
				if (pawn == null || pawn is Zombie || pawn.health == null || pawn.RaceProps.Humanlike == false)
					return;

				var rotStage = __instance.GetRotStage();
				if (rotStage == RotStage.Dessicated)
					return;

				var hasBrain = pawn.health.hediffSet.GetBrain() != null;
				if (hasBrain == false)
					return;

				var ticks = GenTicks.TicksGame;
				tmpHediffZombieInfections.Clear();
				pawn.health.hediffSet.GetHediffs(ref tmpHediffZombieInfections);
				var shouldBecomeZombie = tmpHediffZombieInfections.Any(infection => ticks > infection.ticksWhenBecomingZombie);

				if (shouldBecomeZombie)
				{
					var map = ThingOwnerUtility.GetRootMap(__instance);
					if (map != null)
						Tools.QueueConvertToZombie(__instance, map);
				}
			}
		}

		// show infection on dead pawns
		//
		[HarmonyPatch(typeof(HealthCardUtility))]
		[HarmonyPatch(nameof(HealthCardUtility.DrawOverviewTab))]
		static class HealthCardUtility_DrawOverviewTab_Patch
		{
			static List<Hediff_Injury_ZombieBite> tmpHediffInjuryZombieBites = new();

			static void Postfix(Pawn pawn, Rect leftRect, ref float __result)
			{
				if (pawn == null || pawn.health == null)
					return;

				if (pawn.health.hediffSet.GetBrain() == null)
					return;

				if (pawn.Dead)
				{
					tmpHediffInjuryZombieBites.Clear();
					pawn.health.hediffSet.GetHediffs(ref tmpHediffInjuryZombieBites);
					if (tmpHediffInjuryZombieBites.All(zombieBite => zombieBite.mayBecomeZombieWhenDead == false))
						return;
				}
				else
				{
					if (pawn.InfectionState() < InfectionState.BittenInfectable)
						return;
				}

				__result += 15f;
				GUI.color = Color.red;
				var text = "BodyIsInfectedLabel".Translate();
				var textHeight = Text.CalcHeight(text, leftRect.width);
				Widgets.Label(new Rect(0f, __result, leftRect.width, textHeight), text);
				TooltipHandler.TipRegion(new Rect(0f, __result, leftRect.width, textHeight), "BodyIsInfectedTooltip".Translate());
				__result += textHeight;
				GUI.color = Color.white;
			}
		}

		// patch to handle targets deaths so that we update our grid
		//
		[HarmonyPatch(typeof(PawnComponentsUtility))]
		[HarmonyPatch(nameof(PawnComponentsUtility.RemoveComponentsOnKilled))]
		static class PawnComponentsUtility_RemoveComponentsOnKilled_Patch
		{
			static void Postfix(Pawn pawn)
			{
				if (pawn is Zombie || pawn is ZombieSpitter || pawn.Map == null)
					return;

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
		[HarmonyPatch(nameof(PawnDiedOrDownedThoughtsUtility.TryGiveThoughts))]
		[HarmonyPatch(new Type[] { typeof(Pawn), typeof(DamageInfo?), typeof(PawnDiedOrDownedThoughtsKind) })]
		static class PawnDiedOrDownedThoughtsUtility_TryGiveThoughts_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn victim)
			{
				return victim is not Zombie || (victim is Zombie zombie && zombie.DevelopmentalStage.Child());
			}
		}

		// patch to allow child killed thoughts to be milder
		//
		[HarmonyPatch(typeof(IndividualThoughtToAdd), MethodType.Constructor)]
		[HarmonyPatch(new[] { typeof(ThoughtDef), typeof(Pawn), typeof(Pawn), typeof(float), typeof(float) })]
		static class IndividualThoughtToAdd_Constructor_Patch
		{
			static void Prefix(ThoughtDef thoughtDef, Pawn otherPawn, ref float moodPowerFactor)
			{
				if (thoughtDef == ThoughtDefOf.KilledChild && otherPawn is Zombie)
					moodPowerFactor *= 0.5f;
			}
		}
		[HarmonyPatch(typeof(Thought_Tale))]
		[HarmonyPatch(nameof(Thought_Tale.OpinionOffset))]
		static class Thought_Tale_OpinionOffset_Patch
		{
			static void Postfix(Thought_Tale __instance, ref float __result)
			{
				if (__instance.def.taleDef != TaleDefOf.KilledChild)
					return;
				var tale = Find.TaleManager.GetLatestTale(__instance.def.taleDef, __instance.otherPawn);
				if (tale is not Tale_DoublePawn doublePawn || doublePawn.secondPawnData.faction.def != ZombieDefOf.Zombies)
					return;
				__result *= 0.25f;
			}
		}

		// patch to remove immunity ticks on zombies
		//
		[HarmonyPatch(typeof(ImmunityHandler))]
		[HarmonyPatch(nameof(ImmunityHandler.ImmunityHandlerTick))]
		static class ImmunityHandler_ImmunityHandlerTick_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(ImmunityHandler __instance)
			{
				return __instance.pawn is not Zombie;
			}
		}

		// patch to trigger on gun shots
		//
		[HarmonyPatch(typeof(Projectile))]
		[HarmonyPatch(nameof(Projectile.Launch))]
		[HarmonyPatch(new Type[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) })]
		public static class Projectile_Launch_Patch
		{
			static void Postfix(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget)
			{
				if (launcher is not Pawn pawn || pawn.Map == null || launcher is ZombieSpitter)
					return;

				var noiseScale = 1f;
				if (pawn.equipment?.PrimaryEq?.PrimaryVerb?.verbProps != null)
					noiseScale = pawn.equipment.PrimaryEq.PrimaryVerb.verbProps.muzzleFlashScale / Constants.BASE_MUZZLE_FLASH_VALUE;

				var now = Tools.Ticks();
				var pos = origin.ToIntVec3();
				var magnitude = usedTarget == null ? (Constants.WEAPON_RANGE[0] + Constants.WEAPON_RANGE[1]) / 2 : (usedTarget.CenterVector3 - origin).magnitude * noiseScale * Math.Min(1f, ZombieSettings.Values.zombieInstinct.HalfToDoubleValue());
				var radius = Tools.Boxed(magnitude, Constants.WEAPON_RANGE[0], Constants.WEAPON_RANGE[1]);
				var grid = pawn.Map.GetGrid();
				Tools.GetCircle(radius).Do(vec => grid.BumpTimestamp(pos + vec, now - vec.LengthHorizontalSquared));
			}
		}

		// patch to allow zombies to occupy the same spot without collision
		//
		[HarmonyPatch(typeof(Pawn_PathFollower))]
		[HarmonyPatch(nameof(Pawn_PathFollower.WillCollideWithPawnOnNextPathCell))]
		static class Pawn_PathFollower_WillCollideWithPawnOnNextPathCell_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn ___pawn, ref bool __result)
			{
				if (___pawn is Zombie)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}
		//
		[HarmonyPatch(typeof(PawnCollisionTweenerUtility))]
		[HarmonyPatch(nameof(PawnCollisionTweenerUtility.PawnCollisionPosOffsetFor))]
		static class PawnCollisionTweenerUtility_PawnCollisionPosOffsetFor_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn pawn, ref Vector3 __result)
			{
				if (pawn is not Zombie)
					return true;
				__result = Vector3.zero;
				return false;
			}
		}

		// patches so that zombies do not have needs
		//
		[HarmonyPatch(typeof(Pawn_NeedsTracker))]
		[HarmonyPatch(nameof(Pawn_NeedsTracker.AllNeeds), MethodType.Getter)]
		static class Pawn_NeedsTracker_AllNeeds_Patch
		{
			static List<Need> Replacement()
			{
				return new List<Need>();
			}

			static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
			{
				var conditions = Tools.NotZombieInstructions(generator, method);
				var replacement = SymbolExtensions.GetMethodInfo(() => Replacement());
				var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method, replacement);
				return transpiler(generator, instructions);
			}
		}
		[HarmonyPatch(typeof(Pawn_NeedsTracker))]
		[HarmonyPatch(nameof(Pawn_NeedsTracker.AddOrRemoveNeedsAsAppropriate))]
		static class Pawn_NeedsTracker_AddOrRemoveNeedsAsAppropriate_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
			{
				var conditions = Tools.NotZombieInstructions(generator, method);
				var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
				return transpiler(generator, instructions);
			}
		}

		// patches so zombies don't use clamors at all
		//
		[HarmonyPatch(typeof(GenClamor))]
		[HarmonyPatch(nameof(GenClamor.DoClamor))]
		[HarmonyPatch(new[] { typeof(Thing), typeof(IntVec3), typeof(float), typeof(ClamorDef) })]
		static class GenClamor_DoClamor_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Thing source)
			{
				return (source is Zombie) == false;
			}
		}
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch(nameof(Pawn.HearClamor))]
		static class Pawn_HearClamor_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Thing source)
			{
				return (source is Zombie) == false;
			}
		}

		// patches so that zombies have no records
		//
		[HarmonyPatch]
		static class Pawn_RecordsTracker_Increment_Patch
		{
			static IEnumerable<MethodBase> TargetMethods()
			{
				var type = typeof(Pawn_RecordsTracker);
				yield return AccessTools.Method(type, nameof(Pawn_RecordsTracker.AddTo));
				yield return AccessTools.Method(type, nameof(Pawn_RecordsTracker.RecordsTickUpdate));
				yield return AccessTools.Method(type, nameof(Pawn_RecordsTracker.Increment));
			}

			static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
			{
				var conditions = Tools.NotZombieInstructions(generator, method);
				var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
				return transpiler(generator, instructions);
			}
		}
		[HarmonyPatch(typeof(Pawn_RecordsTracker))]
		[HarmonyPatch(nameof(Pawn_RecordsTracker.GetValue))]
		static class Pawn_RecordsTracker_GetValue_Patch
		{
			static bool Prefix(Pawn ___pawn, ref float __result)
			{
				if (___pawn is Zombie)
				{
					__result = 0;
					return false;
				}
				return true;
			}
		}
		[HarmonyPatch(typeof(Pawn_RecordsTracker))]
		[HarmonyPatch(nameof(Pawn_RecordsTracker.GetAsInt))]
		static class Pawn_RecordsTracker_GetAsInt_Patch
		{
			static bool Prefix(Pawn ___pawn, ref int __result)
			{
				if (___pawn is Zombie)
				{
					__result = 0;
					return false;
				}
				return true;
			}
		}

		// patch so zombies get less move cost from tar slime
		//
		[HarmonyPatch(typeof(Pawn_PathFollower))]
		[HarmonyPatch(nameof(Pawn_PathFollower.CostToMoveIntoCell))]
		[HarmonyPatch(new[] { typeof(Pawn), typeof(IntVec3) })]
		static class Pawn_PathFollower_CostToMoveIntoCell_Patch
		{
			static void Postfix(Pawn pawn, IntVec3 c, ref int __result)
			{
				if ((pawn is Zombie) == false)
					return;
				if (__result < 450)
					return;

				if (pawn.Map.thingGrid.ThingAt<TarSlime>(c) != null)
					__result = 100;
			}
		}

		// patch so zombies do not bleed
		//
		[HarmonyPatch(typeof(Pawn_HealthTracker))]
		[HarmonyPatch(nameof(Pawn_HealthTracker.DropBloodFilth))]
		static class Pawn_HealthTracker_DropBloodFilth_Patch
		{
			static bool SkipDropBlood(Pawn pawn)
			{
				if (pawn is not Zombie zombie)
					return false;
				if (ZombieSettings.Values.zombiesDropBlood == false)
					return true;
				if (zombie.hasTankyShield > 0 || zombie.hasTankySuit > 0)
					return true;
				return false;
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
			{
				var jump = generator.DefineLabel();

				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldfld, typeof(Pawn_HealthTracker).Field("pawn"));
				yield return new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => SkipDropBlood(null)));
				yield return new CodeInstruction(OpCodes.Brfalse, jump);
				yield return new CodeInstruction(OpCodes.Ret);

				var list = instructions.ToList();
				list[0].labels.Add(jump);

				foreach (var instr in list)
					yield return instr;
			}
		}

		// patch to insert our difficulty settings into the custom storyteller UI
		//
		[HarmonyPatch(typeof(StorytellerUI))]
		[HarmonyPatch(nameof(StorytellerUI.DrawCustomLeft))]
		static class StorytellerUI_DrawCustomLeft_Patch
		{
			static readonly MethodInfo m_DrawCustomDifficultySlider = AccessTools.Method(typeof(StorytellerUI), nameof(StorytellerUI.DrawCustomDifficultySlider));

			static void DrawZombielandDifficultySettings(Listing_Standard listing_Standard)
			{
				StorytellerUI.DrawCustomDifficultySlider(listing_Standard, "zombielandThreatScale", ref ZombieSettings.Values.threatScale, ToStringStyle.PercentZero, ToStringNumberSense.Absolute, 0f, 5f, 0.01f, false, 1000f);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var list = instructions.ToList();
				var idx = list.FirstIndexOf(code => code.Calls(m_DrawCustomDifficultySlider));
				if (idx > 0 && idx < list.Count())
				{
					var localVar = list[idx + 1].Clone();
					if (localVar.IsLdloc())
						list.InsertRange(idx + 1, new[]
						{
							localVar,
							CodeInstruction.Call(() => DrawZombielandDifficultySettings(default))
						});
				}
				return list;
			}
		}

		[HarmonyPatch(typeof(Page_SelectScenario))]
		[HarmonyPatch(nameof(Page_SelectScenario.BeginScenarioConfiguration))]
		static class Page_SelectScenario_BeginScenarioConfiguration_Patch
		{
			static void Prefix()
			{
				ZombieSettings.ApplyDefaults();
			}
		}

		// patch to insert our settings page
		//
		[HarmonyPatch(typeof(PageUtility))]
		[HarmonyPatch(nameof(PageUtility.StitchedPages))]
		static class PageUtility_StitchedPages_Patch
		{
			static void Prefix(List<Page> pages)
			{
				pages.Insert(1, new Dialog_Settings());
			}
		}

		// set hostility response to attack as default
		//
		[HarmonyPatch(typeof(Game))]
		[HarmonyPatch(nameof(Game.InitNewGame))]
		class Game_InitNewGame_Patch
		{
			static void Postfix()
			{
				Find.CurrentMap?.mapPawns.FreeColonists
					.Do(pawn => pawn.playerSettings.hostilityResponse = HostilityResponseMode.Attack);
			}
		}

		// suppress memories of zombie violence
		//
		[HarmonyPatch(typeof(Pawn_HealthTracker))]
		[HarmonyPatch(nameof(Pawn_HealthTracker.PreApplyDamage))]
		static class Pawn_HealthTracker_PreApplyDamage_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
			{
				var m_TryGainMemory = typeof(MemoryThoughtHandler).MethodNamed(nameof(MemoryThoughtHandler.TryGainMemory), new Type[] { typeof(ThoughtDef), typeof(Pawn), typeof(Precept) });
				var f_pawn = typeof(Pawn_HealthTracker).Field("pawn");

				var found1 = false;
				var found2 = false;

				var list = instructions.ToList();
				var jumpIndex = list.FirstIndexOf(instr => instr.Calls(m_TryGainMemory)) + 1;
				if (jumpIndex > 0 && jumpIndex < list.Count())
				{
					var skipLabel = generator.DefineLabel();
					list[jumpIndex].labels.Add(skipLabel);
					found1 = true;

					for (var i = jumpIndex; i >= 0; i--)
						if (list[i].IsLdarg(0))
						{
							var j = i;
							list.Insert(j++, new CodeInstruction(OpCodes.Ldarg_0));
							list.Insert(j++, new CodeInstruction(OpCodes.Ldfld, f_pawn));
							list.Insert(j++, new CodeInstruction(OpCodes.Isinst, typeof(Zombie)));
							list.Insert(j++, new CodeInstruction(OpCodes.Brtrue_S, skipLabel));

							found2 = true;
							break;
						}
				}

				foreach (var instruction in list)
					yield return instruction;

				if (!found1 || !found2)
					Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);
			}
		}

		// patch to add our settings to the main bottom-right menu
		//
		[HarmonyPatch(typeof(MainTabWindow_Menu))]
		[HarmonyPatch(nameof(MainTabWindow_Menu.RequestedTabSize), MethodType.Getter)]
		static class MainTabWindow_Menu_RequestedTabSize_Path
		{
			static void Postfix(ref Vector2 __result)
			{
				__result.y += MainMenuDrawer_DoMainMenuControls_Path.addedHeight;
			}
		}
		[HarmonyPatch(typeof(MainTabWindow_Menu))]
		[HarmonyPatch(nameof(MainTabWindow_Menu.DoWindowContents))]
		static class MainTabWindow_Menu_DoWindowContents_Path
		{
			static void Prefix(ref Rect rect)
			{
				rect.height += MainMenuDrawer_DoMainMenuControls_Path.addedHeight;
			}
		}
		[HarmonyPatch(typeof(Widgets))]
		[HarmonyPatch(nameof(Widgets.ButtonTextWorker))]
		static class Widgets_ButtonText_Path
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
				var from = typeof(Widgets).MethodNamed(nameof(Widgets.DrawAtlas), new Type[] { typeof(Rect), typeof(Texture2D) });
				var to = SymbolExtensions.GetMethodInfo(() => NewDrawAtlas(Rect.zero, null, null));

				var found = false;
				foreach (var instruction in instructions)
				{
					if (instruction.Calls(from))
					{
						instruction.operand = to;
						yield return new CodeInstruction(OpCodes.Ldarg_1);
						found = true;
					}
					yield return instruction;
				}

				if (!found)
					Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);
			}
		}
		[HarmonyPatch(typeof(MainMenuDrawer))]
		[HarmonyPatch(nameof(MainMenuDrawer.DoMainMenuControls))]
		static class MainMenuDrawer_DoMainMenuControls_Path
		{
			// called from MainTabWindow_Menu_RequestedTabSize_Path
			public static float addedHeight = 45f + 7f; // default height ListableOption + OptionListingUtility.DrawOptionListing spacing

			static readonly MethodInfo[] patchMethods = new MethodInfo[] {
				SymbolExtensions.GetMethodInfo(() => DrawOptionListingPatch1(Rect.zero, null)),
				SymbolExtensions.GetMethodInfo(() => DrawOptionListingPatch2(Rect.zero, null))
			};

			static float DrawOptionListingPatch1(Rect rect, List<ListableOption> optList)
			{
				if (Current.ProgramState == ProgramState.Playing)
				{
					var label = "Options".Translate();
					var idx = optList.FirstIndexOf(opt => opt.label == label);
					if (idx > 0 && idx < optList.Count())
						optList.Insert(idx, new ListableOption("Zombieland", delegate
						{
							MainMenuDrawer.CloseMainTab();
							var me = LoadedModManager.GetMod<ZombielandMod>();
							var dialog = new Dialog_ModSettings(me);
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
				var m_DrawOptionListing = SymbolExtensions.GetMethodInfo(() => OptionListingUtility.DrawOptionListing(Rect.zero, null));

				var counter = 0;
				foreach (var instruction in instructions)
				{
					if (instruction.Calls(m_DrawOptionListing))
						instruction.operand = patchMethods[counter++];
					yield return instruction;
				}

				if (counter != 2)
					Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);
			}
		}

		// update zombie pathing
		//
		[HarmonyPatch(typeof(RegionAndRoomUpdater))]
		[HarmonyPatch(nameof(RegionAndRoomUpdater.CreateOrUpdateRooms))]
		static class RegionAndRoomUpdater_CreateOrUpdateRooms_Patch
		{
			static void Postfix(Map ___map)
			{
				___map.GetComponent<TickManager>()?.zombiePathing?.UpdateRegions();
			}
		}

		// adds sudden zombies to unfogged rooms
		//
		[HarmonyPatch(typeof(Building), nameof(Building.DeSpawn))]
		static class Building_DeSpawn_Patch
		{
			public static void Prefix(Building __instance, DestroyMode mode)
			{
				if (Current.ProgramState != ProgramState.Playing)
					return;

				if (mode == DestroyMode.WillReplace)
					return;

				if (__instance.def.MakeFog == false)
					return;

				var map = __instance.Map;
				if (map == null)
					return;

				var fogGrid = map.fogGrid;
				if (fogGrid == null)
					return;

				var pos = __instance.Position;
				var validCells = GenAdj.AdjacentCells.Select(v => pos + v).Where(c => c.InBounds(map));
				var shouldUnfog = validCells.Any(c => fogGrid.IsFogged(c) == false);
				if (shouldUnfog == false)
					return;

				bool ShouldSpawn(IntVec3 c)
				{
					if (fogGrid.IsFogged(c) == false)
						return false;

					var edifice = c.GetEdifice(map);
					return (edifice == null || !edifice.def.MakeFog);
				}

				validCells.DoIf(ShouldSpawn, c => Tools.SpawnZombiesInRoom(map, c));
			}
		}
		//
		[HarmonyPatch(typeof(FogGrid), nameof(FogGrid.Notify_PawnEnteringDoor))]
		static class FogGrid_Notify_PawnEnteringDoor_Patch
		{
			public static void Prefix(Building_Door door, Pawn pawn)
			{
				if (pawn.Faction != Faction.OfPlayer && pawn.HostFaction != Faction.OfPlayer)
					return;

				var pos = door.Position;
				var map = door.Map;
				if (map == null)
					return;

				GenAdj.AdjacentCells.Select(v => pos + v)
					.DoIf(c => c.InBounds(map), c => Tools.SpawnZombiesInRoom(map, c));
			}
		}

		// add job to turn on zombie shocker
		// add roping job
		//
		[HarmonyPatch(typeof(FloatMenuMakerMap))]
		[HarmonyPatch(nameof(FloatMenuMakerMap.AddHumanlikeOrders))]
		static class FloatMenuMakerMap_AddHumanlikeOrders_Patch
		{
			public static readonly string zapZombiesLabel = "ZapZombies".Translate();
			public static readonly string ropeZombieLabel = "RopeZombie".Translate();

			static void Postfix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> opts)
			{
				var shocker = pawn.Map.thingGrid.ThingAt<ZombieShocker>(IntVec3.FromVector3(clickPos));
				if (shocker != null)
				{
					if (pawn.CanReach(shocker, PathEndMode.ClosestTouch, Danger.Deadly, false, false, TraverseMode.ByPawn))
						if (pawn.CanReserve(shocker) && shocker.compPowerTrader.PowerOn && shocker.HasValidRoom())
						{
							void job()
							{
								var job = JobMaker.MakeJob(CustomDefs.ZapZombies, shocker);
								_ = pawn.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
							};
							opts.Add(new FloatMenuOption(zapZombiesLabel, job));
						}
				}

				var ropableZombie = pawn.Map.GetComponent<TickManager>().GetRopableZombie(clickPos);
				if (ropableZombie != null)
				{
					void job()
					{
						var job = JobMaker.MakeJob(CustomDefs.RopeZombie, ropableZombie);
						pawn.drafter.Drafted = true;
						_ = pawn.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
					};
					opts.Add(new FloatMenuOption(ropeZombieLabel, job));
				}
			}
		}

		// draw dangerous area info at top of screen
		//
		[HarmonyPatch(typeof(Messages), nameof(Messages.MessagesDoGUI))]
		static class Messages_MessagesDoGUI_Patch
		{
			static void Prefix()
			{
				ZombieAreaManager.DangerAlertsOnGUI();
			}
		}

		// move messages down when dangerous area info shows
		//
		[HarmonyPatch(typeof(Message), nameof(Message.Draw))]
		static class Message_Draw_Patch
		{
			static void Prefix(ref int yOffset)
			{
				if (ZombieAreaManager.warningShowing)
					yOffset += 29;
			}
		}

		// suppress no-ideo warning when loading zombies
		[HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.ExposeData))]
		static class Pawn_IdeoTracker_ExposeData_Patch
		{
			static readonly FieldInfo f_mode = AccessTools.Field(typeof(Scribe), nameof(Scribe.mode));
			static readonly FieldInfo f_pawn = AccessTools.Field(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.pawn));

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
			{
				var endLabel = generator.DefineLabel();
				foreach (var instruction in instructions)
				{
					if (instruction.LoadsField(f_mode))
					{
						yield return new CodeInstruction(OpCodes.Ldarg_0);
						yield return new CodeInstruction(OpCodes.Ldfld, f_pawn);
						yield return new CodeInstruction(OpCodes.Isinst, typeof(Zombie));
						yield return new CodeInstruction(OpCodes.Brtrue, endLabel);
					}
					if (instruction.opcode == OpCodes.Ret)
						instruction.labels.Add(endLabel);
					yield return instruction;
				}
			}
		}
	}
}
