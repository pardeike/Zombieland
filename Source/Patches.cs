using Brrainz;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Unity.Collections;
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
	static partial class Patches
	{
		static readonly List<string> errors = new();

		static Patches()
		{
			var harmony = new Harmony("net.pardeike.zombieland");
			errors = new List<string>();
			try
			{
				PatchGroups.ApplyAll(harmony, Assembly.GetExecutingAssembly());
			}
			catch (Exception ex)
			{
				var error = ex.ToString();
				Log.Error(error);
				PatchGroups.RecordExternalFailure(PatchGroups.Startup, "Patch grouping failed", error);
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
				PatchGroups.RunLateAction(PatchGroups.Optional, "AlienTools", AlienTools.Init);
				PatchGroups.RunLateAction(PatchGroups.Optional, "VehicleTools", VehicleTools.Init);
				PatchGroups.RunLateAction(PatchGroups.Optional, "Customization", Customization.Init);
				PatchGroups.RunLateAction(PatchGroups.Optional, "DubsTools", DubsTools.Init);
				PatchGroups.TryShowFailureDialogAtStartScreen();
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
			Log.Error(error);
			PatchGroups.ThrowActiveFailure(error);
			errors.Add(error);
			PatchGroups.RecordExternalFailure(PatchGroups.Misc, error);
		}

		static bool IsConcretePatchTarget(MethodInfo method)
		{
			return method != null
				&& method.IsAbstract == false
				&& method.ContainsGenericParameters == false
				&& (method.DeclaringType?.ContainsGenericParameters ?? false) == false;
		}

		static void SpawnTarSmoke(IntVec3 center, Map map, float radius, float difficulty, bool playSound = true)
		{
			if (map == null)
				return;

			var alpha = GenMath.LerpDoubleClamped(0, 5, 0.85f, 1f, difficulty);
			var min = GenMath.LerpDoubleClamped(0, 5, 2, 60, difficulty);
			var max = GenMath.LerpDoubleClamped(0, 5, min, 90, difficulty);
			CustomDefs.TarSmoke.graphicData.color = new Color(0, 0, 0, alpha);
			CustomDefs.TarSmoke.gas.expireSeconds = new FloatRange(min, max);
			foreach (var cell in GenRadial.RadialCellsAround(center, radius, true))
			{
				if (cell.InBounds(map) == false || cell.GetGas(map) != null)
					continue;

				GenSpawn.Spawn(ThingMaker.MakeThing(CustomDefs.TarSmoke), cell, map);
			}
			if (playSound && ZombieAwarenessCues.ShouldPlayZombieActionSound())
				CustomDefs.TarSmokePop.PlayOneShot(SoundInfo.InMap(new TargetInfo(center, map)));
		}

		[ThreadStatic] static int burningZombieFireDamageFeedbackDepth;

		static bool IsBurningZombieFireDamage(Pawn pawn, DamageInfo dinfo)
		{
			if (IsZombielandPawn(pawn) == false || dinfo.Def != DamageDefOf.Flame)
				return false;
			if (burningZombieFireDamageFeedbackDepth > 0)
				return true;
			if (dinfo.Instigator is Fire instigatorFire && ReferenceEquals(instigatorFire.parent, pawn))
				return true;
			return false;
		}

		static bool SuppressBurningZombieFireDamageFeedback => burningZombieFireDamageFeedbackDepth > 0;

		static void PlayBurningZombieDamageSound(Thing hitThing, Map map)
		{
			var soundDef = CustomDefs.ZombieBurningDamage ?? CustomDefs.ZombieBurningSilencer;
			if (soundDef == null || hitThing == null || map == null)
				return;
			var position = hitThing.PositionHeld;
			if (position.IsValid == false)
				return;
			soundDef.PlayOneShot(SoundInfo.InMap(new TargetInfo(position, map)));
		}

		[HarmonyPatch(typeof(GenUI))]
		[HarmonyPatch(nameof(GenUI.ThingsUnderMouse))]
		static class GenUI_ThingsUnderMouse_Patch
		{
			static void Postfix(ref List<Thing> __result)
			{
				if (__result == null)
					return;
				HashSet<ZombieSymbiant> seen = null;
				for (var i = 0; i < __result.Count; i++)
				{
					if (__result[i] is not ZombieSymbiant symbiant)
						continue;
					seen ??= [];
					if (seen.Add(symbiant))
						continue;
					__result.RemoveAt(i);
					i--;
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
				if (Find.ScreenshotModeHandler.Active)
					return;
				var map = Find.CurrentMap;
				if (Tools.MapViewActiveFor(map) == false)
					return;

				// debug zombie counts
				map.GetGrid().IterateCells((x, z, cell) =>
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
				map.GetGrid().IterateCells((x, z, cell) =>
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
				if (Tools.MapViewActiveFor(map) == false)
				{
					if (Constants.CONTAMINATION)
						ContaminationManager.Instance.ClearCurrentDrawer();
					return;
				}

				var currentViewRect = Find.CameraDriver.CurrentViewRect;
				currentViewRect.ClipInsideMap(map);

				if (Constants.CONTAMINATION && ContaminationManager.Instance.showContaminationOverlay)
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
					Tools.PlayerReachableRegions(map).SelectMany(r => r.Cells).Do(c => CellRenderer.RenderSpot(c.ToVector3Shifted(), m, 0.25f));
				}

				if (Constants.SHOW_AVOIDANCE_GRID && Tools.ShouldAvoidZombies())
				{
					var tickManager = map.GetComponent<TickManager>();
					if (tickManager?.RuntimeReady == true && tickManager.avoidGrid != null)
					{
						var avoidGrid = tickManager.avoidGrid;
						foreach (var c in currentViewRect)
						{
							var cost = avoidGrid.GetCosts()[c.x + c.z * map.Size.x];
							if (cost > 0)
								Tools.DebugPosition(c.ToVector3(), new Color(0f, 1f, 0f, GenMath.LerpDouble(0, 10000, 0.4f, 1f, cost)));
						}
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
				if (Find.UIRoot.screenshotMode.FiltersCurrentEvent)
					return;

				var map = Find.CurrentMap;
				if (Tools.MapViewActiveFor(map) == false)
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
		internal static class GlobalControlsUtility_DoDate_Patch
		{
			internal const float ReadoutHeight = 24f;
			internal const float RightMargin = 7f;
			internal const int ThreatForecastTooltipWindowId = 564534346;
			internal const int ThreatForecastTooltipWidth = 720;
			internal const int ThreatForecastTooltipHeight = 320;

			static readonly Color percentageBackground = new(1, 1, 1, 0.1f);
			internal static bool LastThreatForecastVisible;
			internal static Rect LastThreatForecastRect;
			internal static Rect LastThreatForecastTooltipRect;
			internal static string LastThreatForecastLabel;
			internal static int LastThreatForecastFrame = -1;
			internal static Rect LastThreatForecastHoverRect;
			internal static Rect LastThreatForecastHoverTooltipRect;
			internal static string LastThreatForecastHoverLabel;
			internal static string LastThreatForecastHoverSource;
			internal static int LastThreatForecastHoverFrame = -1;

			internal static string FormatThreatForecast(float f1, float f2)
			{
				var n1 = Mathf.FloorToInt(f1 * 100);
				var n2 = Mathf.FloorToInt(f2 * 100);
				if (n1 == n2)
					return string.Format("{0:D0}%", n1) + " " + "ThreatLevel".Translate();
				return string.Format("{0:D0}-{1:D0}%", n1, n2) + " " + "ThreatLevel".Translate();
			}

			internal static Rect GetRightAlignedReadoutRect(float leftX, float width, float curBaseY, string text)
			{
				var zlRect = new Rect(leftX, curBaseY - ReadoutHeight, width, ReadoutHeight);
				Text.Font = GameFont.Small;
				var len = Text.CalcSize(text);
				zlRect.xMin = zlRect.xMax - Math.Min(leftX, len.x + RightMargin);
				return zlRect;
			}

			internal static Rect GetThreatForecastTooltipRect(Rect zlRect)
			{
				return new Rect(
					zlRect.xMin - 10 - ThreatForecastTooltipWidth,
					zlRect.yMax - ThreatForecastTooltipHeight,
					ThreatForecastTooltipWidth,
					ThreatForecastTooltipHeight);
			}

			static void Postfix(float leftX, float width, ref float curBaseY)
			{
				LastThreatForecastVisible = false;
				LastThreatForecastHoverRect = Rect.zero;
				LastThreatForecastHoverTooltipRect = Rect.zero;
				LastThreatForecastHoverLabel = null;
				LastThreatForecastHoverSource = null;
				LastThreatForecastHoverFrame = -1;
				var map = Find.CurrentMap;
				if (Tools.MapViewActiveFor(map) == false)
					return;

				if (map.IsBlacklisted())
					return;

				if (ZombieSettings.Values.showZombieStats)
				{
					ZombieWeather zombieWeather = null;
					string threatForecastString = null;
					bool TryGetThreatForecastString(out string forecast)
					{
						forecast = null;
						if (ZombieSettings.Values.useDynamicThreatLevel == false)
							return false;
						zombieWeather ??= map.GetComponent<ZombieWeather>();
						if (zombieWeather == null)
							return false;
						if (threatForecastString.NullOrEmpty())
						{
							var (f1, f2) = zombieWeather.GetFactorRangeFor();
							threatForecastString = FormatThreatForecast(f1, f2);
						}
						forecast = threatForecastString;
						return true;
					}

					void DrawThreatForecastHover(Rect triggerRect, string forecast, string source, bool expandHighlight)
					{
						if (forecast.NullOrEmpty() || Mouse.IsOver(triggerRect) == false)
							return;

						var highlightRect = triggerRect;
						if (expandHighlight)
							highlightRect.xMin -= 10;
						Widgets.DrawHighlight(highlightRect);

						var bgRect = GetThreatForecastTooltipRect(triggerRect);
						LastThreatForecastHoverRect = triggerRect;
						LastThreatForecastHoverTooltipRect = bgRect;
						LastThreatForecastHoverLabel = forecast;
						LastThreatForecastHoverSource = source;
						LastThreatForecastHoverFrame = Time.frameCount;
						Find.WindowStack.ImmediateWindow(ThreatForecastTooltipWindowId, bgRect, WindowLayer.Super, ZombieWeather.GenerateTooltipDrawer(bgRect.AtZero()), false, false, 1f);
					}

					var tickManager = map.GetComponent<TickManager>();
					if (tickManager == null)
						return;
					var count = tickManager.ZombieCount();
					if (count > 0)
					{
						var zombieCountString = count + " Zombies";
						var zlRect = GetRightAlignedReadoutRect(leftX, width, curBaseY, zombieCountString);

						GUI.BeginGroup(zlRect);
						Text.Anchor = TextAnchor.UpperRight;
						var rect = zlRect.AtZero();
						rect.xMax -= RightMargin;
						var percentRect = rect;
						percentRect.width *= ZombieTicker.PercentTicking;
						percentRect.xMin -= 2;
						percentRect.xMax += 2;
						percentRect.yMax -= 3;
						Widgets.DrawRectFast(percentRect, percentageBackground);
						Widgets.Label(rect, zombieCountString);
						Text.Anchor = TextAnchor.UpperLeft;
						GUI.EndGroup();

						if (TryGetThreatForecastString(out var zombieCountForecast))
							DrawThreatForecastHover(zlRect, zombieCountForecast, "zombieCount", false);
						else
						{
							TooltipHandler.TipRegion(zlRect, new TipSignal(delegate
							{
								var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
								return $"Zombieland v{currentVersion.ToString(4)}";
							}, 99799));
						}
						var cachedZombies = tickManager.allZombiesCached;
						if (Mouse.IsOver(zlRect) && cachedZombies != null && cachedZombies.Count <= 100)
							cachedZombies.Do(zombie => TargetHighlighter.Highlight(new GlobalTargetInfo(zombie), true, false, false));

						curBaseY -= zlRect.height;
					}

					if (TryGetThreatForecastString(out var zombieWeatherString))
					{
						var zlRect = GetRightAlignedReadoutRect(leftX, width, curBaseY, zombieWeatherString);
						LastThreatForecastVisible = true;
						LastThreatForecastRect = zlRect;
						LastThreatForecastTooltipRect = GetThreatForecastTooltipRect(zlRect);
						LastThreatForecastLabel = zombieWeatherString;
						LastThreatForecastFrame = Time.frameCount;

						DrawThreatForecastHover(zlRect, zombieWeatherString, "threatForecast", true);

						GUI.BeginGroup(zlRect);
						Text.Anchor = TextAnchor.UpperRight;
						var rect = zlRect.AtZero();
						rect.xMax -= RightMargin;
						Widgets.Label(rect, zombieWeatherString);
						Text.Anchor = TextAnchor.UpperLeft;
						GUI.EndGroup();

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
				if (LongEventHandler.AnyEventNowOrWaiting || LongEventHandler.ShouldWaitForEvent)
					return;
				if (Current.Game == null || Current.ProgramState != ProgramState.Playing || Scribe.mode != LoadSaveMode.Inactive)
					return;

				_ = ZombieWanderer.processor.MoveNext();
				if (Find.TickManager.Paused)
					return;

				ZombieTicker.zombiesTicked = 0;
				var managers = Find.Maps.Select(map => map.GetComponent<TickManager>()).OfType<TickManager>().ToArray();
				ZombieTicker.managers = managers;

				var curTimePerTick = __instance.CurTimePerTick;
				var realTimeToTickThrough = __instance.realTimeToTickThrough;
				if (Mathf.Abs(Time.deltaTime - curTimePerTick) < curTimePerTick * 0.1f)
					realTimeToTickThrough += curTimePerTick;
				else
					realTimeToTickThrough += Time.deltaTime;

				var n1 = realTimeToTickThrough / curTimePerTick;
				ZombieTicker.UpdateSaturation(ZombielandMod.frameWatch.ElapsedMilliseconds, n1);
				var n2 = __instance.TickRateMultiplier * 2f;
				var loopEstimate = Mathf.FloorToInt(Mathf.Min(n1, n2));

				var liveZombieCount = 0;
				for (var i = 0; i < managers.Length; i++)
				{
					var manager = managers[i];
					if (manager.TryEnsureRuntimeInitialized("Verse.TickManager.TickManagerUpdate") == false)
						continue;
					liveZombieCount += manager.LiveZombieCount();
				}

				ZombieTicker.maxTicking = Mathf.FloorToInt(loopEstimate * liveZombieCount);
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
			static List<ZombieThumper> thumpers = new();

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
				for (var i = 0; i < thumpers.Count; i++)
				{
					var thumper = thumpers[i];
					if (thumper.Map == map && thumper.IsActive && thumper.Position.DistanceTo(cell) <= thumper.Radius + 0.5f)
					{
						__result = 0f;
						return false;
					}
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
			static bool RunTicking(Pawn pawn)
			{
				if (pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter)
					return true;

				if (pawn.RaceProps.Humanlike)
				{
					var hediffs = pawn.health.hediffSet.hediffs;
					var maxState = InfectionState.None;
					for (var i = 0; i < hediffs.Count; i++)
					{
						if (hediffs[i] is not Hediff_Injury_ZombieBite bite)
							continue;
						var state = bite.TendDuration.GetInfectionState();
						if (state > maxState)
							maxState = state;
					}
					pawn.SetInfectionState(maxState);
				}

				if (Constants.CONTAMINATION)
				{
					var effectiveness = pawn.GetEffectiveness();
					if (effectiveness > 0.99f)
						return true;
					return Rand.Chance(effectiveness);
				}

				return true;
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
			{
				var label = generator.DefineLabel();
				var m_ThingWithComps_Tick = AccessTools.Method(typeof(ThingWithComps), nameof(ThingWithComps.Tick));
				var list = instructions.ToList();
				var idx = list.FirstIndexOf(code => code.Calls(m_ThingWithComps_Tick));
				if (idx < 0)
					throw new Exception("Cannot find ThingWithComps.Tick() call");
				list.InsertRange(idx + 1, new[]
				{
					new CodeInstruction(OpCodes.Ldarg_0),
					CodeInstruction.Call(() => RunTicking(default)),
					new CodeInstruction(OpCodes.Brtrue, label),
					new CodeInstruction(OpCodes.Ret)
				});
				list[idx + 5].labels.Add(label);
				return list;
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
				ZombieSymbiant.TryReduceContaminationOnLeavingSymbiantCell(___pawn);

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
				if (___pawn is ZombieSymbiant || ___pawn is ZombieSpitter)
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
			static bool Prefix(Pawn pawn, LocalTargetInfo target, ref string failStr, ref Action __result)
			{
				if (target.Thing is ZombieSymbiant)
				{
					failStr = null;
					__result = null;
					return false;
				}
				if (pawn.equipment?.Primary is Chainsaw chainsaw && chainsaw.running)
				{
					failStr = null;
					__result = null;
					return false;
				}
				return true;
			}
		}
		//
		[HarmonyPatch(typeof(FloatMenuUtility))]
		[HarmonyPatch(nameof(FloatMenuUtility.GetRangedAttackAction))]
		static class FloatMenuUtility_GetRangedAttackAction_Patch
		{
			static bool Prefix(LocalTargetInfo target, ref string failStr, ref Action __result)
			{
				if (target.Thing is ZombieSymbiant)
				{
					failStr = null;
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
		[HarmonyPatch(typeof(PawnRenderUtility))]
		[HarmonyPatch(nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
		static class PawnRenderer_DrawEquipment_Patch
		{
			static bool Prefix(Pawn pawn, Vector3 drawPos, Rot4 facing, PawnRenderFlags flags)
			{
				if (pawn.equipment?.Primary is not Chainsaw chainsaw)
					return true;

				if (pawn.Dead || pawn.Spawned == false)
					return true;
				if ((flags & PawnRenderFlags.NeverAimWeapon) != PawnRenderFlags.None)
					return true;
				if (chainsaw.running == false)
					return true;

				if (chainsaw.swinging == false/* && ___pawn.Drafted && Find.Selector.IsSelected(___pawn) == false*/)
					return true;

				var angle = chainsaw.angle;

				var vector = new Vector3(0f, (facing == Rot4.North) ? (-0.0028957527f) : 0.03474903f, 0f);
				var equipmentDrawDistanceFactor = pawn.ageTracker.CurLifeStage.equipmentDrawDistanceFactor;
				vector += drawPos + new Vector3(0f, 0f, 0.4f + CustomDefs.Chainsaw.equippedDistanceOffset).RotatedBy(angle) * equipmentDrawDistanceFactor;

				PawnRenderUtility.DrawEquipmentAiming(chainsaw, vector, angle);
				if (Find.TickManager.Paused)
					pawn.rotationTracker.Face(vector);

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
				};
				return false;
			}
		}

		[HarmonyPatch(typeof(Gizmo_SetFuelLevel))]
		[HarmonyPatch(nameof(Gizmo_SetFuelLevel.GizmoOnGUI))]
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
						lastPawnInstruction = new CodeInstruction(list[i - 1].opcode, list[i - 1].operand);
					}
					if (list[i].Calls(m_Chance) && lastPawnInstruction != null)
					{
						list.Insert(i, lastPawnInstruction);
						lastPawnInstruction = null;
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
			static bool Prefix(IAttackTarget t, ref bool __result)
			{
				var thing = t?.Thing;
				if (thing is ZombieSymbiant)
				{
					__result = false;
					return false;
				}
				if (thing is not Zombie zombie)
					return true;
				if (zombie.Spawned == false || zombie.Downed || zombie.IsRopedOrConfused)
				{
					__result = false;
					return false;
				}
				var pos = zombie.Position;
				var map = zombie.Map;
				__result = (map != null && pos.InBounds(map) && map.areaManager.Home[pos]);
				return false;
			}
		}

		// do not flee from certain zombies
		//
		[HarmonyPatch(typeof(FleeUtility))]
		[HarmonyPatch(nameof(FleeUtility.ShouldFleeFrom))]
		static class FleeUtility_ShouldFleeFrom_Patch
		{
			static void Postfix(Thing t, Pawn pawn, ref bool __result)
			{
				if (t is ZombieSymbiant)
					__result = false;
				else if (__result && t is Zombie zombie && pawn.SeesZombieAsThreat(zombie) == false)
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
				{
					__result = false;
					return false;
				}

				if (__instance.currentTarget.Thing is not Pawn target)
					return true;
				if (caster is not Zombie zombie)
				{
					if (target is Zombie targetZombie && targetZombie.IsRopedOrConfused)
					{
						target.Kill(null);
						__result = false;
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
				var thingGrid = target.Map.thingGrid;
				var targetDrawPos = target.DrawPos;
				var concurrentAttacks = 0;
				foreach (var vec in GenAdj.AdjacentCellsAround)
				{
					foreach (var thing in thingGrid.ThingsAt(pos + vec))
					{
						if (thing is not Zombie adjacentZombie || adjacentZombie.IsRopedOrConfused)
							continue;
						var zombiePos = adjacentZombie.Position;
						var dist = posX == zombiePos.x || posZ == zombiePos.z ? 1.1f : 2.2f;
						if ((targetDrawPos - adjacentZombie.DrawPos).MagnitudeHorizontalSquared() > dist)
							continue;
						concurrentAttacks += adjacentZombie.IsTanky ? 2 : 1;
						if (concurrentAttacks > limit)
							break;
					}
					if (concurrentAttacks > limit)
						break;
				}
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

				// Preserve the old prefix+transpiler effective chance while keeping one owner for this behavior.
				var chance = Constants.COLONISTS_HIT_ZOMBIES_CHANCE;
				var oldEffectiveChance = 1f - (1f - chance) * (1f - chance);
				return Rand.Chance(oldEffectiveChance);
			}

			static bool Prefix(Verb_LaunchProjectile __instance, ref bool __result)
			{
				if (SkipMissingShotsAtZombies(__instance, __instance.currentTarget) == false)
					return true;

				if (__instance.currentTarget.HasThing && __instance.currentTarget.Thing.Map != __instance.caster.Map)
				{
					__result = false;
					return false;
				}

				var projectileDef = __instance.Projectile;
				if (projectileDef == null)
				{
					__result = false;
					return false;
				}

				var hasShootLine = __instance.TryFindShootLineFromTo(__instance.caster.Position, __instance.currentTarget, out var resultingLine);
				if (__instance.verbProps.stopBurstWithoutLos && hasShootLine == false)
				{
					__result = false;
					return false;
				}

				var equipmentSource = __instance.EquipmentSource;
				if (equipmentSource != null)
				{
					equipmentSource.GetComp<CompChangeableProjectile>()?.Notify_ProjectileLaunched();
					equipmentSource.GetComp<CompApparelVerbOwner_Charged>()?.UsedOnce();
				}
				__instance.lastShotTick = Find.TickManager.TicksGame;

				Thing manningPawn = __instance.caster;
				Thing projectileEquipment = equipmentSource;
				var compMannable = __instance.caster.TryGetComp<CompMannable>();
				if (compMannable?.ManningPawn != null)
				{
					manningPawn = compMannable.ManningPawn;
					projectileEquipment = __instance.caster;
				}

				var projectile = (Projectile)GenSpawn.Spawn(projectileDef, resultingLine.Source, __instance.caster.Map);
				if (projectileEquipment != null && projectileEquipment.TryGetComp(out CompUniqueWeapon comp))
					foreach (var trait in comp.TraitsListForReading)
					{
						if (trait.damageDefOverride != null)
							projectile.damageDefOverride = trait.damageDefOverride;
						if (trait.extraDamages.NullOrEmpty() == false)
						{
							projectile.extraDamages ??= new List<ExtraDamage>();
							projectile.extraDamages.AddRange(trait.extraDamages);
						}
					}

				var projectileHitFlags = ProjectileHitFlags.IntendedTarget;
				if (__instance.canHitNonTargetPawnsNow)
					projectileHitFlags |= ProjectileHitFlags.NonTargetPawns;
				if (__instance.currentTarget.HasThing == false || __instance.currentTarget.Thing.def.Fillage == FillCategory.Full)
					projectileHitFlags |= ProjectileHitFlags.NonTargetWorld;

				projectile.Launch(manningPawn, __instance.caster.DrawPos, __instance.currentTarget, __instance.currentTarget, projectileHitFlags, __instance.preventFriendlyFire, projectileEquipment);
				__result = true;
				return false;
			}
		}

		// patch to not allow some jobs on zombies
		//
		[HarmonyPatch(typeof(Pawn_JobTracker))]
		[HarmonyPatch(nameof(Pawn_JobTracker.StartJob))]
		static class Pawn_JobTracker_StartJob_Patch
		{
			static readonly HashSet<JobDef> allowedSymbiantJobs = new()
			{
				CustomDefs.Symbiant,
				JobDefOf.Goto,
				JobDefOf.Wait,
				JobDefOf.Wait_MaintainPosture,
			};

			static readonly HashSet<JobDef> allowedJobs = new()
			{
				CustomDefs.Stumble,
				CustomDefs.Sabotage,
				CustomDefs.Symbiant,
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
				if (newJob == null || ___pawn == null)
					return true;
				if (newJob != null
					&& newJob.targetA.Thing is ZombieSymbiant
					&& (newJob.def == JobDefOf.AttackMelee || newJob.def == JobDefOf.AttackStatic)
					&& ___pawn.Faction?.HostileTo(Faction.OfPlayer) != true)
				{
					___jobsGivenThisTick = 0;
					___jobsGivenThisTickTextual = "";
					___startingNewJob = false;
					___pawn.ClearReservationsForJob(newJob);
					return false;
				}

				if (___pawn is not Zombie && ___pawn is not ZombieSymbiant && ___pawn is not ZombieSpitter)
					return true;
				if (___pawn is ZombieSymbiant && allowedSymbiantJobs.Contains(newJob.def) == false)
				{
					___jobsGivenThisTick = 0;
					___jobsGivenThisTickTextual = "";
					___startingNewJob = false;
					___pawn.ClearReservationsForJob(newJob);
					return false;
				}
				if (allowedJobs.Contains(newJob.def))
					return true;

				___jobsGivenThisTick = 0;
				___jobsGivenThisTickTextual = "";
				___startingNewJob = false;
				___pawn.ClearReservationsForJob(newJob);
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
				var method = typeof(JobDriver_AttackStatic)
					.InnerMethodsStartingWith("<MakeNewToils>b__")
					.FirstOrDefault(method =>
					{
						var parameters = method.GetParameters();
						return parameters.Length == 1 && parameters[0].ParameterType == typeof(int);
					});
				if (method != null)
				{
					var f_this = AccessTools.GetDeclaredFields(method.DeclaringType)
						.FirstOrDefault(field => field.FieldType == typeof(JobDriver_AttackStatic));
					if (f_this != null)
						_this = AccessTools.FieldRefAccess<object, JobDriver_AttackStatic>(f_this);
					else
					{
						Error($"Cannot find Verse.AI.JobDriver_AttackStatic display-class this field for {method.FullDescription()}");
						return null;
					}
				}
				else
					Error($"Cannot find Verse.AI.JobDriver_AttackStatic.MakeNewToils tickIntervalAction delegate");

				return method;
			}

			static bool Prefix(object __instance)
			{
				if (_this == null)
					return true;
				var me = _this(__instance);
				if (me == null)
					return true;
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
			static bool IsZombieBiteVerb(VerbEntry entry)
			{
				var damageDef = entry.verb.GetDamageDef();
				return damageDef == CustomDefs.ZombieBite || damageDef?.defName == "ZombieBite";
			}

			static void Postfix(Pawn_MeleeVerbs __instance, List<VerbEntry> __result)
			{
				if (__instance.Pawn is Zombie zombie && (zombie.isElectrifier || zombie.isAlbino))
					_ = __result.RemoveAll(IsZombieBiteVerb);
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
		[HarmonyPatch(new Type[] { typeof(Pawn), typeof(Thing), typeof(float) })]
		static class PawnUtility_GetManhunterOnDamageChance_Patch
		{
			static void Postfix(ref float __result, Thing instigator)
			{
				if (instigator is Zombie)
				{
					if (ZombieSettings.Values.zombiesCauseManhuntingResponse == false)
						__result = 0;
					else
						__result /= 20;
				}
				else if (instigator is ZombieSymbiant || instigator is ZombieSpitter)
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
				else if (prey is ZombieSymbiant)
					__result = -10000f;
				else if (prey is ZombieSpitter)
					__result = 0f;
			}
		}

		class ZombieAvoidGridPathCustomizer : PathRequest.IPathGridCustomizer, IDisposable
		{
			NativeArray<ushort> offsets;

			public ZombieAvoidGridPathCustomizer(int[] costs)
			{
				offsets = new NativeArray<ushort>(costs.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				for (var i = 0; i < costs.Length; i++)
					offsets[i] = (ushort)Mathf.Clamp(costs[i], 0, ushort.MaxValue);
			}

			public NativeArray<ushort> GetOffsetGrid()
			{
				return offsets;
			}

			public void Dispose()
			{
				if (offsets.IsCreated)
					offsets.Dispose();
			}
		}

		static bool TryCreateZombieAvoidGridCustomizer(Pawn pawn, out ZombieAvoidGridPathCustomizer customizer)
		{
			customizer = null;
			if (pawn == null || Tools.ShouldAvoidZombies(pawn) == false)
				return false;

			var map = pawn.Map;
			if (map == null)
				return false;

			var tickManager = map.GetComponent<TickManager>();
			if (tickManager?.RuntimeReady != true || tickManager.avoidGrid == null)
				return false;
			var avoidGrid = tickManager.avoidGrid;

			var costs = avoidGrid.GetCosts();
			var hasCosts = false;
			for (var i = 0; i < costs.Length; i++)
				if (costs[i] > 0)
				{
					hasCosts = true;
					break;
				}
			if (hasCosts == false)
				return false;

			customizer = new ZombieAvoidGridPathCustomizer(costs);
			return true;
		}

		static void AddZombieAvoidGridCustomizer(Pawn pawn, ref PathRequest.IPathGridCustomizer customizer)
		{
			if (customizer != null)
				return;
			if (TryCreateZombieAvoidGridCustomizer(pawn, out var zombieCustomizer))
				customizer = zombieCustomizer;
		}

		[HarmonyPatch(typeof(PathFinder))]
		[HarmonyPatch(nameof(PathFinder.CreateRequest))]
		[HarmonyPatch(new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(IntVec3?), typeof(TraverseParms), typeof(Nullable<PathFinderCostTuning>), typeof(PathEndMode), typeof(Pawn), typeof(PathRequest.IPathGridCustomizer) })]
		static class PathFinder_CreateRequest_Patch
		{
			static void Prefix(TraverseParms traverseParms, Pawn pawn, ref PathRequest.IPathGridCustomizer customizer)
			{
				AddZombieAvoidGridCustomizer(pawn ?? traverseParms.pawn, ref customizer);
			}
		}

		[HarmonyPatch(typeof(PathFinder))]
		[HarmonyPatch(nameof(PathFinder.FindPathNow))]
		[HarmonyPatch(new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(Nullable<PathFinderCostTuning>), typeof(PathEndMode), typeof(PathRequest.IPathGridCustomizer) })]
		static class PathFinder_FindPathNow_Patch
		{
			static void Prefix(TraverseParms traverseParms, ref PathRequest.IPathGridCustomizer customizer, out ZombieAvoidGridPathCustomizer __state)
			{
				__state = null;
				if (customizer != null)
					return;
				if (TryCreateZombieAvoidGridCustomizer(traverseParms.pawn, out var zombieCustomizer))
				{
					customizer = zombieCustomizer;
					__state = zombieCustomizer;
				}
			}

			static void Postfix(ZombieAvoidGridPathCustomizer __state)
			{
				__state?.Dispose();
			}
		}

		[HarmonyPatch(typeof(PathRequest))]
		[HarmonyPatch(nameof(PathRequest.Resolve))]
		static class PathRequest_Resolve_Patch
		{
			static void Postfix(PathRequest __instance)
			{
				(__instance.customizer as ZombieAvoidGridPathCustomizer)?.Dispose();
			}
		}

		[HarmonyPatch(typeof(PathRequest))]
		[HarmonyPatch(nameof(PathRequest.Dispose))]
		static class PathRequest_Dispose_Patch
		{
			static void Postfix(PathRequest __instance)
			{
				(__instance.customizer as ZombieAvoidGridPathCustomizer)?.Dispose();
			}
		}

		[HarmonyPatch(typeof(Pawn_PathFollower))]
		[HarmonyPatch(nameof(Pawn_PathFollower.NeedNewPath))]
		static class Pawn_PathFollower_NeedNewPath_Patch
		{
			static readonly MethodInfo m_ShouldCollideWithPawns = SymbolExtensions.GetMethodInfo(() => PawnUtility.ShouldCollideWithPawns(null));

			static bool ZombieInPath(Pawn_PathFollower __instance, Pawn pawn)
			{
				var path = __instance.curPath;
				if (path == null || path.Found == false)
					return false;
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

				if (path.NodesLeftCount < 5)
					return false;
				var lookAhead = path.Peek(4);
				var destination = path.LastNode;
				if ((lookAhead - destination).LengthHorizontalSquared < 25)
					return false;

				var map = pawn.Map;
				var tickManager = map.GetComponent<TickManager>();
				if (tickManager?.RuntimeReady != true)
					return false;
				var avoidGrid = tickManager.avoidGrid;
				if (avoidGrid == null)
					return false;
				var costs = avoidGrid.GetCosts();
				var zombieDanger = costs[lookAhead.x + lookAhead.z * map.Size.x];
				return (zombieDanger > 0);
			}

			static bool Prefix(Pawn_PathFollower __instance, ref bool __result)
			{
				if (ZombieInPath(__instance, __instance.pawn))
				{
					__result = true;
					return false;
				}
				return true;
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
				if (oldEntDef is not ThingDef thingDef)
					return true;
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

				// Do not call FreePassage here. In RimWorld 1.6 it can reach WillCloseSoon,
				// which scans nearby pawns and calls PawnCanOpen recursively.

				if (p.CurJob?.playerForced ?? false)
					return;

				if (Tools.ShouldAvoidZombies(p) == false)
					return;

				var map = p.Map;

				var tickManager = map.GetComponent<TickManager>();
				if (tickManager?.RuntimeReady != true)
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
					foreach (var cell in __instance.OccupiedRect().Cells)
					{
						if (avoidGrid.ShouldAvoid(map, cell) == false)
							continue;
						__result = false;
						break;
					}
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

				var runningJobDef = __instance.job?.def;
				if (runningJobDef == JobDefOf.Flee || runningJobDef == JobDefOf.FleeAndCower)
				{
					var fleeJob = __instance.job;
					RetargetAssignedAreaFleeJob(___pawn, ZombieFleeThreatsFor(___pawn), 23, ref fleeJob);
					return;
				}

				if (__instance.job == null || __instance.job.playerForced || Tools.ShouldAvoidZombies(___pawn) == false)
					return;

				var tickManager = ___pawn.Map?.GetComponent<TickManager>();
				if (tickManager?.RuntimeReady != true)
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
					var allowedArea = ___pawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
					var destinations = safeDestinations;
					if (allowedArea != null)
					{
						var areaDestinations = safeDestinations.Where(dest => allowedArea[dest]).ToList();
						if (areaDestinations.Count > 0)
							destinations = areaDestinations;
					}

					destinations.SortByDescending(dest => (pos - dest).LengthHorizontalSquared);
					var destination = destinations.First();
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

		[HarmonyPatch(typeof(FleeUtility))]
		[HarmonyPatch(nameof(FleeUtility.FleeJob))]
		static class FleeUtility_FleeJob_Patch
		{
			static void Postfix(Pawn pawn, Thing danger, int fleeDistance, ref Job __result)
			{
				if (__result == null || danger == null)
					return;
				if (IsZombielandFleeThreat(danger) == false)
					return;
				RetargetAssignedAreaFleeJob(pawn, new[] { danger }, fleeDistance, ref __result);
			}
		}

		[HarmonyPatch]
		static class JobGiver_ConfigurableHostilityResponse_TryGetFleeJob_Patch
		{
			static MethodBase TargetMethod()
			{
				var method = AccessTools.Method(typeof(JobGiver_ConfigurableHostilityResponse), "TryGetFleeJob", new[] { typeof(Pawn) });
				if (method == null)
					Error("Cannot find RimWorld.JobGiver_ConfigurableHostilityResponse.TryGetFleeJob");
				return method;
			}

			static void Postfix(Pawn pawn, ref Job __result)
			{
				if (__result?.def != JobDefOf.FleeAndCower)
					return;
				var threats = ZombieFleeThreatsFor(pawn);
				if (threats.Count == 0)
					return;
				RetargetAssignedAreaFleeJob(pawn, threats, 23, ref __result);
			}
		}

		static bool IsZombielandFleeThreat(Thing thing)
		{
			return thing is Pawn pawn && ZombieAreaManager.IsZombielandPawn(pawn);
		}

		static List<Thing> ZombieFleeThreatsFor(Pawn pawn)
		{
			var threats = new List<Thing>();
			var map = pawn?.Map;
			if (map == null)
				return threats;

			var potentialTargets = map.attackTargetsCache.GetPotentialTargetsFor(pawn);
			for (var i = 0; i < potentialTargets.Count; i++)
			{
				var thing = potentialTargets[i].Thing;
				if (IsZombielandFleeThreat(thing) && FleeUtility.ShouldFleeFrom(thing, pawn, false, false))
					threats.Add(thing);
			}

			var alwaysFlee = map.listerThings.ThingsInGroup(ThingRequestGroup.AlwaysFlee);
			for (var i = 0; i < alwaysFlee.Count; i++)
			{
				var thing = alwaysFlee[i];
				if (IsZombielandFleeThreat(thing) && FleeUtility.ShouldFleeFrom(thing, pawn, false, false))
					threats.Add(thing);
			}

			return threats.Distinct().ToList();
		}

		static void RetargetAssignedAreaFleeJob(Pawn pawn, IEnumerable<Thing> threats, int fleeDistance, ref Job job)
		{
			if (pawn?.Map == null || pawn.IsColonist == false)
				return;

			var allowedArea = pawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
			if (allowedArea == null)
				return;

			var currentDestination = job.targetA.Cell;
			if (currentDestination.IsValid == false || allowedArea[currentDestination])
				return;

			if (TryFindAreaRestrictedZombieFleeDestination(pawn, threats, fleeDistance, allowedArea, out var destination))
				job.SetTarget(TargetIndex.A, destination);
		}

		internal static bool TryFindAreaRestrictedZombieFleeDestination(Pawn pawn, IEnumerable<Thing> threats, float distance, Area allowedArea, out IntVec3 destination)
		{
			destination = IntVec3.Invalid;
			var threatList = threats?
				.Where(thing => thing?.Spawned == true && thing.Map == pawn?.Map)
				.ToList();
			var map = pawn?.Map;
			var region = pawn?.GetRegion();
			if (map == null || allowedArea == null || region == null || threatList == null || threatList.Count == 0)
				return false;

			var bestPos = pawn.Position;
			var bestScore = -1f;
			var traverseParms = TraverseParms.For(pawn);
			RegionTraverser.BreadthFirstTraverse(region, (from, reg) => reg.Allows(traverseParms, false), delegate (Region reg)
			{
				var danger = reg.DangerFor(pawn);
				foreach (var cell in reg.Cells)
				{
					if (allowedArea[cell] == false || cell.Standable(map) == false || reg.IsDoorway)
						continue;
					if (cell.GetTerrain(map).dangerous)
						return false;

					var closestThreat = default(Thing);
					var closestDistSq = 0f;
					for (var i = 0; i < threatList.Count; i++)
					{
						var distSq = cell.DistanceToSquared(threatList[i].Position);
						if (closestThreat == null || distSq < closestDistSq)
						{
							closestThreat = threatList[i];
							closestDistSq = distSq;
						}
					}

					var closestDist = Mathf.Sqrt(closestDistSq);
					var score = Mathf.Pow(Mathf.Min(closestDist, distance), 1.2f);
					score *= Mathf.InverseLerp(50f, 0f, (cell - pawn.Position).LengthHorizontal);
					if (cell.GetRoom(map) != closestThreat.GetRoom())
						score *= 4.2f;
					else if (closestDist < 8f)
						score *= 0.05f;
					if (map.pawnDestinationReservationManager.CanReserve(cell, pawn) == false)
						score *= 0.5f;
					if (danger == Danger.Deadly)
						score *= 0.8f;
					if (ModsConfig.AnomalyActive && (pawn.RaceProps.Humanlike || pawn.IsPlayerControlled) && map.gameConditionManager.MapBrightness < 0.1f && map.glowGrid.PsychGlowAt(cell) == PsychGlow.Dark)
						score *= 0.1f;

					if (score > bestScore)
					{
						bestPos = cell;
						bestScore = score;
					}
				}
				return false;
			}, 20);

			if (bestPos == pawn.Position || bestScore < 0f)
				return false;

			destination = bestPos;
			return true;
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

				if (map == null)
					return;

				var tickManager = map.GetComponent<TickManager>();
				if (tickManager?.RuntimeReady != true || tickManager.avoidGrid == null)
					return;
				var avoidGrid = tickManager.avoidGrid;

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
				if (tickManager?.RuntimeReady != true)
					return false;

				var avoidGrid = tickManager.avoidGrid;
				return avoidGrid != null && avoidGrid.ShouldAvoid(pawn.Map, cell);
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
				.Where(IsConcretePatchTarget)
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
				if (tickManager?.RuntimeReady != true)
					return false;

				var avoidGrid = tickManager.avoidGrid;
				return avoidGrid != null && avoidGrid.ShouldAvoid(pawn.Map, cell);
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
				.Where(IsConcretePatchTarget)
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

				var map = thing?.Map ?? pawn.Map;
				if (thing == null || map == null || thing.Position.InBounds(map) == false)
					return false;

				var tickManager = map.GetComponent<TickManager>();
				if (tickManager?.RuntimeReady != true)
					return false;

				var avoidGrid = tickManager.avoidGrid;
				return avoidGrid != null && avoidGrid.ShouldAvoid(map, thing.Position);
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
				.Where(IsConcretePatchTarget)
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

				var map = thing?.Map ?? pawn.Map;
				if (thing == null || map == null || thing.Position.InBounds(map) == false)
					return false;

				if (Tools.ShouldAvoidZombies(pawn) == false)
					return false;

				var tickManager = map.GetComponent<TickManager>();
				if (tickManager?.RuntimeReady != true)
					return false;

				var avoidGrid = tickManager.avoidGrid;
				return avoidGrid != null && avoidGrid.ShouldAvoid(map, thing.Position);
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
				.Where(IsConcretePatchTarget)
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
				if (i <= 0 || i >= list.Count())
				{
					Error("Cannot find " + from.FullDescription() + " in Pawn_PathFollower.StartPath");
					return list;
				}

				list[i - 1].opcode = OpCodes.Ldarg_1;
				list[i].operand = to;

				var f_posture = typeof(Pawn_JobTracker).Field(nameof(Pawn_JobTracker.posture));
				i = list.FindIndex(instr => instr.opcode == OpCodes.Stfld && Equals(instr.operand, f_posture)) - 1;
				if (i < 0 || list[i].LoadsConstant(0) == false)
				{
					Error("Cannot find " + f_posture.DeclaringType.FullDescription() + "." + f_posture.Name + " assignment in Pawn_PathFollower.StartPath");
					return list;
				}
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

		[HarmonyPatch(typeof(Pawn_FilthTracker), nameof(Pawn_FilthTracker.Notify_EnteredNewCell))]
		static class Pawn_FilthTracker_Notify_EnteredNewCell_SymbiantSplash_Patch
		{
			static void Postfix(Pawn_FilthTracker __instance)
			{
				// Called after pathing enters a cell; keep exits before symbiant lookup.
				if (CustomDefs.SymbiantSplash == null)
					return;
				var pawn = __instance?.pawn;
				if (pawn == null || pawn.Spawned == false || pawn.Map == null || pawn.Flying)
					return;
				if (ZombieSymbiant.IsSymbiantCellForSlowedPawn(pawn, pawn.Position, out _) == false)
					return;
				CustomDefs.SymbiantSplash.PlayOneShot(SoundInfo.InMap(pawn));
			}
		}

		[HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
		static class Pawn_PathFollower_TryEnterNextPathCell_SymbiantDoor_Patch
		{
			static void Prefix(Pawn_PathFollower __instance, Pawn ___pawn, out IntVec3 __state)
			{
				__state = IntVec3.Invalid;
				if (ZombieSymbiant.DebugDisablePathCost)
					return;
				var pawn = ___pawn;
				if (pawn == null || pawn.Spawned == false || pawn.Map == null || pawn.Flying)
					return;
				var nextCell = __instance.nextCell;
				if (nextCell.IsValid == false || nextCell == pawn.Position)
					return;
				var door = nextCell.GetDoor(pawn.Map);
				if (door == null || door.Destroyed || door.Spawned == false)
					return;
				if (ZombieSymbiant.IsSymbiantCellForSlowedPawn(pawn, nextCell, out _) == false)
					return;
				if (ZombieSymbiant.SymbiantMoveCost(pawn, __instance.nextCellCostTotal) <= __instance.nextCellCostTotal)
					return;
				if (__instance.NextCellDoorToWaitForOrManuallyOpen() == null)
					return;
				__state = pawn.Position;
			}

			static void Postfix(Pawn_PathFollower __instance, Pawn ___pawn, IntVec3 __state)
			{
				if (__state.IsValid == false)
					return;
				var pawn = ___pawn;
				if (pawn == null || pawn.Map == null || pawn.Position != __state)
					return;
				if (ZombieSymbiant.IsSymbiantCellForSlowedPawn(pawn, __instance.nextCell, out _) == false)
					return;
				var door = __instance.nextCell.GetDoor(pawn.Map);
				if (door == null || door.Destroyed || door.Spawned == false)
					return;

				var cost = ZombieSymbiant.SymbiantMoveCost(pawn, __instance.nextCellCostTotal);
				if (cost <= __instance.nextCellCostTotal)
					return;
				__instance.nextCellCostTotal = cost;
				__instance.nextCellCostLeft = Mathf.Max(__instance.nextCellCostLeft, cost);
				TryHoldDoorForSymbiantSlowdown(door, pawn, cost);
			}

			static void TryHoldDoorForSymbiantSlowdown(Building_Door door, Pawn pawn, float cost)
			{
				if (door == null || door.Destroyed || door.Spawned == false || pawn == null)
					return;
				try
				{
					door.Notify_PawnApproaching(pawn, cost);
					var holdTicks = Mathf.CeilToInt(cost) + Mathf.Max(door.TicksTillFullyOpened, 0) + 30;
					door.ticksUntilClose = Mathf.Max(door.ticksUntilClose, holdTicks);
				}
				catch (Exception ex)
				{
					Log.WarningOnce($"Zombieland skipped Symbiant door slowdown hold for door {door.def?.defName ?? "unknown"} because the door implementation rejected the vanilla door contract: {ex.GetType().Name}: {ex.Message}", 904231711);
				}
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

				if (Constants.CONTAMINATION)
				{
					var contaminationList = map.thingGrid.ThingsListAt(pos)
					.Select(t => (thing: t, contamination: t.GetContamination(includeHoldings: true)))
					.Where(pair => pair.contamination != 0)
					.Join(pair => $"{pair.thing}/{pair.contamination}", " ");
					if (contaminationList.Any())
					{
						_ = builder.AppendLine($"Contaminations: {contaminationList}");
						_ = builder.AppendLine("");
					}
				}

				if (Tools.ShouldAvoidZombies())
				{
					if (tickManager.RuntimeReady && tickManager.avoidGrid != null)
					{
						var avoidGrid = tickManager.avoidGrid;
						_ = builder.AppendLine($"Avoid cost: {avoidGrid.GetCosts()[pos.x + pos.z * map.Size.x]}");
					}
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

						var pathGrid = map.pathing.For(MapInfo.traverseParms).pathGrid;
						_ = builder.AppendLine($"Smart wandering walkable: {region.Cells.Count(pathGrid.WalkableFast)} of {region.Cells.Count()}");
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

			static void Postfix(ref string __result)
			{
				if (Current.Game == null)
					return;
				var builder = new StringBuilder(__result ?? "");
				DebugGrid(builder);
				__result = builder.ToString();
			}
		}

		// patch for adding zombie faction to new games
		//
		[HarmonyPatch(typeof(FactionGenerator))]
		[HarmonyPatch(nameof(FactionGenerator.GenerateFactionsIntoWorldLayer))]
		static class FactionGenerator_GenerateFactionsIntoWorldLayer_Patch
		{
			const string Phase = "FactionGenerator.GenerateFactionsIntoWorldLayer";

			static void Prefix(List<FactionDef> factions)
			{
				if (factions != null && factions.Contains(ZombieDefOf.Zombies) == false)
					factions.Add(ZombieDefOf.Zombies);

			}

			[HarmonyFinalizer]
			[HarmonyPriority(ZombieBootstrap.CaptureFinalizerPriority)]
			static Exception CaptureFinalizer(Exception __exception)
				=> ZombieBootstrap.CaptureFinalizerException(Phase, __exception);

			[HarmonyPriority(Priority.Last)]
			static Exception Finalizer(PlanetLayer layer, Exception __exception, bool __runOriginal)
			{
				if (ZombieBootstrap.ShouldRunFinalizerRecovery(Phase, __exception, __runOriginal, out var observedException) == false)
					return __exception;
				if (observedException == null && __runOriginal)
					return __exception;

				var phase = observedException == null ? Phase : $"{Phase} exception";
				ZombieBootstrap.EnsureZombieFaction(phase, out var recovered, layer, createIfMissing: true);
				return ZombieBootstrap.RecoveryPassthrough(phase, __exception, observedException, recovered);
			}
		}

		// patch for repairing the zombie faction after RimWorld swallowed the faction world-gen step exception
		//
		[HarmonyPatch(typeof(WorldGenStep_Factions))]
		[HarmonyPatch(nameof(WorldGenStep_Factions.GenerateFresh))]
		static class WorldGenStep_Factions_GenerateFresh_Patch
		{
			const string Phase = "WorldGenStep_Factions.GenerateFresh";

			[HarmonyFinalizer]
			[HarmonyPriority(ZombieBootstrap.CaptureFinalizerPriority)]
			static Exception CaptureFinalizer(Exception __exception)
				=> ZombieBootstrap.CaptureFinalizerException(Phase, __exception);

			[HarmonyPriority(Priority.Last)]
			static Exception Finalizer(PlanetLayer layer, Exception __exception, bool __runOriginal)
			{
				if (ZombieBootstrap.ShouldRunFinalizerRecovery(Phase, __exception, __runOriginal, out var observedException) == false)
					return __exception;
				if (observedException == null && __runOriginal)
					return __exception;

				var phase = observedException == null ? Phase : $"{Phase} exception";
				ZombieBootstrap.EnsureZombieFaction(phase, out var recovered, layer, createIfMissing: true);
				return ZombieBootstrap.RecoveryPassthrough(phase, __exception, observedException, recovered);
			}
		}

		// patch for adding zombie faction to existing games
		//
		[HarmonyPatch(typeof(FactionManager))]
		[HarmonyPatch(nameof(FactionManager.ExposeData))]
		static class FactionManager_ExposeData_Patch
		{
			const string Phase = "FactionManager.ExposeData";

			static void Postfix(FactionManager __instance, List<Faction> ___allFactions)
			{
				// Let vanilla finish all load cleanup/recache passes before mutating factions.
				if (Scribe.mode != LoadSaveMode.PostLoadInit)
					return;

				ZombieBootstrap.EnsureZombieFactionAfterPostLoad(__instance, ___allFactions);
			}

			[HarmonyFinalizer]
			[HarmonyPriority(ZombieBootstrap.CaptureFinalizerPriority)]
			static Exception CaptureFinalizer(Exception __exception)
				=> ZombieBootstrap.CaptureFinalizerException(Phase, __exception);

			[HarmonyPriority(Priority.Last)]
			static Exception Finalizer(FactionManager __instance, List<Faction> ___allFactions, Exception __exception, bool __runOriginal)
			{
				if (ZombieBootstrap.ShouldRunFinalizerRecovery(Phase, __exception, __runOriginal, out var observedException) == false)
					return __exception;
				if (Scribe.mode != LoadSaveMode.PostLoadInit)
					return __exception;
				if (observedException == null && __runOriginal)
					return __exception;

				var hadZombieFaction = ___allFactions?.Any(faction => faction?.def == ZombieDefOf.Zombies) == true;
				ZombieBootstrap.EnsureZombieFactionAfterPostLoad(__instance, ___allFactions);
				var recovered = hadZombieFaction == false && ___allFactions?.Any(faction => faction?.def == ZombieDefOf.Zombies) == true;
				return ZombieBootstrap.RecoveryPassthrough(Phase, __exception, observedException, recovered);
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

			static void TryMakeTarSlime(IntVec3 cell, Map map)
			{
				if (cell.InBounds(map))
					_ = FilthMaker.TryMakeFilth(cell, map, CustomDefs.TarSlime);
			}

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
				map.GetComponent<ZombieAttackTargetIndex>()?.InvalidateFor(pawn);
				if (pos.InBounds(map) == false || value.InBounds(map) == false)
					return;

				if (pawn is ZombieSpitter)
				{
					var now = Tools.Ticks();
					var grid = map.GetGrid();
					var radius = GenMath.LerpDouble(0, 5, 4, 32, ZombieSettings.Values.spitterThreat);
					foreach (var vec in Tools.GetCircle(radius))
					{
						if (exclude.Contains(vec))
							continue;
						grid.BumpTimestamp(value + vec, now - (long)(2f * vec.LengthHorizontal));
					}
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
						foreach (var vec in Tools.GetCircle(radius))
						{
							var vx = Math.Sign(vec.x);
							var vz = Math.Sign(vec.z);
							var vlen = vec.LengthHorizontalSquared;
							if ((vx == 0 || vx == dx) && (vz == 0 || vz == dz) && vlen > 1f)
							{
								var offset = GenMath.LerpDouble(0f, r2, fadeOff / 8f, fadeOff / 4f, vlen);
								grid.BumpTimestamp(value + vec, now - (long)offset);
							}
						}
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
						TryMakeTarSlime(value, map);
						if (Tools.Difficulty() > 1)
						{
							var x = Math.Sign(value.x - pos.x) + 1;
							var z = Math.Sign(value.z - pos.z) + 1;
							var orthIdx = x + 3 * z;
							var pair = orthogonalIndices[orthIdx];
							TryMakeTarSlime(pos + pair[0], map);
							TryMakeTarSlime(pos + pair[1], map);
						}
					}

					return;
				}

				// set zombie contact timestamp
				var isNotInfected = pawn.InfectionState() < InfectionState.Infecting;
				if (isNotInfected && pawn.IsColonist)
				{
					var tickManager = map.GetComponent<TickManager>();
					if (tickManager?.RuntimeReady == true && (tickManager.avoidGrid?.InAvoidDanger(pawn) ?? false))
						tickManager.MarkZombieContact();
				}

				// vehicles
				if (VehicleTools.BumpTimestamps(pawn, value))
					return;

				// manhunting will always trigger senses
				//
				if (AnomalyTargeting.IsForcedTarget(pawn) == false && (pawn.MentalState == null || (pawn.MentalState.def != def1 && pawn.MentalState.def != def2)))
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
					var things = pos.GetThingList(map);
					for (var i = 0; i < things.Count; i++)
					{
						if (things[i].def == CustomDefs.StickyGoo)
							HealthUtility.AdjustSeverity(pawn, HediffDefOf.ToxicBuildup, toxity);
					}
				}

				// leave pheromone trail
				if (isNotInfected && Customization.DoesAttractsZombies(pawn))
				{
					var now = Tools.Ticks();
					var grid = map.GetGrid();
					foreach (var vec in Tools.GetCircle(Tools.RadiusForPawn(pawn)))
						grid.BumpTimestamp(value + vec, now - (long)(2f * vec.LengthHorizontal));
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
				amount *= 1f - Mathf.Clamp01(ZombieSettings.Values.reducedTurretConsumption);
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
				if (dinfo.Instigator is not Zombie zombie || zombie.health?.Downed != true)
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
				if (dinfo.Instigator is not Zombie zombie || zombie.health?.Downed != true)
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

			static bool ShouldBeAverageNeed(Need need)
			{
				var pawn = need?.pawn;
				if (pawn == null)
					return false;
				if (infectedColonists.Contains(pawn))
					return true;
				if (need is Need_Mood && ZombieSymbiant.HasMoodFixedBenefit(pawn))
					return true;
				if (ZombieSymbiant.HasNoFoodOrRestBenefit(pawn) && (need.def == NeedDefOf.Food || need.def?.defName == "Rest"))
					return true;
				return false;
			}

			[HarmonyPriority(Priority.Last)]
			static void Prefix(Need __instance, ref float value)
			{
				if (ShouldBeAverageNeed(__instance))
					value = 0.5f;
			}
		}

		// patch to make infected colonists have no mental breaks
		//
		[HarmonyPatch(typeof(Pawn_JobTracker), "ShouldStartJobFromThinkTree")]
		static class Pawn_JobTracker_ShouldStartJobFromThinkTree_Patch
		{
			static readonly Type hallucinationDriver = typeof(JobDriver_ContaminationHallucination);
			static readonly Type sleepwalkDriver = typeof(JobDriver_ContaminationSleepwalk);
			static readonly Type hoardDriver = typeof(JobDriver_ContaminationHoard);
			static readonly Type mimicDriver = typeof(JobDriver_ContaminationMimic);
			static readonly Type breakdownDriver = typeof(JobDriver_ContaminationBreakdown);
			static readonly Type forceRestDriver = typeof(JobDriver_ContaminationForceRest);

			static bool IsContaminationJob(Job job, JobDriver driver)
			{
				if (job == null)
					return false;

				if (job.def != EffectDefs.ContaminationJobForceRest
					&& job.def != EffectDefs.ContaminationJobHallucination
					&& job.def != EffectDefs.ContaminationJobSleepwalk
					&& job.def != EffectDefs.ContaminationJobHoard
					&& job.def != EffectDefs.ContaminationJobMimic
					&& job.def != EffectDefs.ContaminationJobBreakdown)
					return false;

				if (driver == null)
					return true;

				var driverType = driver.GetType();
				return false
					|| job.def == EffectDefs.ContaminationJobForceRest && driverType == forceRestDriver
					|| job.def == EffectDefs.ContaminationJobHallucination && driverType == hallucinationDriver
					|| job.def == EffectDefs.ContaminationJobSleepwalk && driverType == sleepwalkDriver
					|| job.def == EffectDefs.ContaminationJobHoard && driverType == hoardDriver
					|| job.def == EffectDefs.ContaminationJobMimic && driverType == mimicDriver
					|| job.def == EffectDefs.ContaminationJobBreakdown && driverType == breakdownDriver;
			}

			static bool Prefix(Job ___curJob, JobDriver ___curDriver, ref bool __result)
			{
				if (IsContaminationJob(___curJob, ___curDriver))
				{
					__result = false;
					return false;
				}
				return true;
			}
		}

		[HarmonyPatch(typeof(MentalStateHandler))]
		[HarmonyPatch(nameof(MentalStateHandler.TryStartMentalState))]
		static class MentalStateHandler_TryStartMentalState_Patch
		{
			static bool NoMentalState(Pawn pawn)
			{
				return pawn != null && Need_CurLevel_Patch.infectedColonists.Contains(pawn);
			}

			[HarmonyPriority(Priority.Last)]
			static bool Prefix(Pawn ___pawn, ref bool __result)
			{
				if (NoMentalState(___pawn))
				{
					__result = false;
					return false;
				}
				return true;
			}

			static void Postfix(bool __result, Pawn ___pawn)
			{
				if (__result && ___pawn?.Spawned == true && ___pawn.equipment?.Primary is Chainsaw chainsaw)
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
				var pawn = __instance?.pawn;
				if (pawn != null && Need_CurLevel_Patch.infectedColonists.Contains(pawn))
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
				return pawn?.health?.Dead == false && Need_CurLevel_Patch.infectedColonists.Contains(pawn);
			}

			[HarmonyPriority(Priority.Last)]
			static bool Prefix(Pawn ___pawn, ref float __result)
			{
				if (FullLevel(___pawn))
				{
					__result = 1f;
					return false;
				}
				return true;
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
						tendDuration?.ZombieInfector?.MakeHarmless();
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
				try
				{
					ZombieSettings.Values.spawnHowType = parms.raidArrivalMode?.walkIn == true ? SpawnHowType.FromTheEdges : SpawnHowType.AllOverTheMap;
					_ = ZombiesRising.TryExecute(Find.CurrentMap, Mathf.FloorToInt(parms.points), parms.spawnCenter, false, false);
				}
				finally
				{
					ZombieSettings.Values.spawnHowType = oldMode;
				}
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

				var launchingShip = Find.Maps.Any(map =>
				{
					var reactor = map.listerBuildings.allBuildingsColonist.OfType<Building_ShipReactor>().FirstOrDefault();
					return reactor?.TryGetComp<CompHibernatable>()?.State == HibernatableStateDefOf.Starting;
				});

				if (launchingShip == false && Rand.Chance(ZombieSettings.Values.infectedRaidsChance) == false)
					return;
				if (launchingShip == false && ZombieWeather.GetThreatLevel(__result.FirstOrDefault()?.Map) == 0f)
					return;
				foreach (var pawn in __result)
					if (pawn?.RaceProps?.Humanlike == true)
						Tools.AddZombieInfection(pawn);
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
				if (request.KindDef == ZombieDefOf.ZombieSymbiant)
					return true;
				if (request.KindDef == ZombieDefOf.ZombieSpitter)
					return true;

				Zombie zombie = null;
				var map = Find.CurrentMap;
				if (map == null)
					return true;
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
				if (__instance is Zombie || __instance is ZombieSymbiant || __instance is ZombieSpitter)
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
				if (Find.ScreenshotModeHandler.Active)
					return;
				if (Find.Selector?.SelectedObjects?.Any(selected => selected is ZombieSymbiant) == true)
				{
					__result = Enumerable.Empty<InspectTabBase>();
					return;
				}
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

		[HarmonyPatch(typeof(HealthUtility))]
		[HarmonyPatch(nameof(HealthUtility.DamageUntilDowned))]
		static class HealthUtility_DamageUntilDowned_Patch
		{
			static readonly MethodInfo m_MakeDowned = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.MakeDowned));
			static readonly object[] a_MakeDowned = m_MakeDowned == null ? null : new object[m_MakeDowned.GetParameters().Length];

			static bool Prefix(Pawn p)
			{
				if (p is not Zombie || ZombieSettings.Values.doubleTapRequired == false)
					return true;
				if (p.health == null || p.health.Dead || p.health.Downed)
					return false;
				if (m_MakeDowned == null)
					return false;
				try
				{
					m_MakeDowned.Invoke(p.health, a_MakeDowned);
				}
				catch
				{
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch(nameof(Pawn.HealthScale), MethodType.Getter)]
		static class Pawn_HealthScale_Patch
		{
			static void Postfix(Pawn __instance, ref float __result)
			{
				if (__instance is ZombieSymbiant symbiant)
					__result *= symbiant.HealthScaleCellMultiplier;
			}
		}

		// patch to keep shooting even if a zombie is down (only if self-healing is on)
		//
		[HarmonyPatch]
		static class Toils_Combat_FollowAndMeleeAttack_KillIncappedTarget_Patch
		{
			static bool IncappedTargetCheck(Job curJob, Pawn target)
			{
				if (target is Zombie)
					return true;
				return curJob?.killIncappedTarget == true;
			}

			static MethodBase TargetMethod()
			{
				var method = typeof(Toils_Combat).InnerMethodsStartingWith("<FollowAndMeleeAttack>b__0").FirstOrDefault();
				if (method == null)
					Error("Cannot find Verse.AI.Toils_Combat FollowAndMeleeAttack tick delegate");
				return method;
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
		[HarmonyPatch]
		static class JobDriver_AttackStatic_TickAction_Patch
		{
			static IEnumerable<MethodBase> TargetMethods()
			{
				var m_Downed = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Downed));
				IEnumerable<MethodInfo> methods;

				methods = typeof(JobDriver_AttackStatic)
					.InnerMethodsStartingWith("*")
					.Where(method => PatchProcessor.GetCurrentInstructions(method).Any(code => code.Calls(m_Downed)));
				foreach (var method in methods)
					yield return method;

				methods = typeof(Toils_Jump)
					.InnerMethodsStartingWith("<JumpIfTargetDowned>")
					.Where(method => PatchProcessor.GetCurrentInstructions(method).Any(code => code.Calls(m_Downed)));
				foreach (var method in methods)
					yield return method;

				var candidates = new MethodBase[]
				{
					AccessTools.Method(typeof(TargetingParameters), nameof(TargetingParameters.CanTarget)),
					AccessTools.Method(typeof(VerbUtility), nameof(VerbUtility.AllowAdjacentShot)),
					AccessTools.Method(typeof(Stance_Warmup), nameof(Stance_Warmup.StanceTick)),
					AccessTools.Method(typeof(Verb_Shoot), nameof(Verb_Shoot.WarmupComplete)),
					AccessTools.PropertyGetter(typeof(Pawn_MindState), nameof(Pawn_MindState.MeleeThreatStillThreat))
				};
				foreach (var method in candidates)
					if (method != null && PatchProcessor.GetCurrentInstructions(method).Any(code => code.Calls(m_Downed)))
						yield return method;
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				return Tools.DownedReplacer(instructions);
			}
		}

		// makes downed zombie crawl rotated to their destination
		//
		[HarmonyPatch(typeof(PawnDownedWiggler))]
		[HarmonyPatch(nameof(PawnDownedWiggler.ProcessPostTickVisuals))]
		static class PawnDownedWiggler_WigglerTick_Patch
		{
			static void Postfix(PawnDownedWiggler __instance, Pawn ___pawn)
			{
				if (___pawn is not Zombie zombie || zombie.health?.Downed != true)
					return;
				if (___pawn.pather.Destination.IsValid == false)
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
				if (___pawn is Zombie zombie && zombie.health?.Downed == true && ___wiggler != null)
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
			static readonly Material rageEyeWhite50 = new(Constants.RAGE_EYE) { color = white50 };
			static readonly HashSet<int> symbiantAuraDrawnPawns = [];
			static int symbiantAuraDrawnFrame = -1;

			static readonly Mesh bodyMesh = MeshPool.GridPlane(new Vector2(1.5f, 1.5f));
			static readonly Mesh bodyMesh_flipped = MeshPool.GridPlaneFlip(new Vector2(1.5f, 1.5f));

			static readonly Mesh headMesh = MeshPool.GridPlane(new Vector2(1.5f, 1.5f));
			static readonly Mesh headMesh_flipped = MeshPool.GridPlaneFlip(new Vector2(1.5f, 1.5f));

			static readonly Mesh shieldMesh = MeshPool.GridPlane(new Vector2(2f, 2f));
			static readonly Mesh shieldMesh_flipped = MeshPool.GridPlaneFlip(new Vector2(2f, 2f));

			internal static Zombie PrepareZombieGraphics(PawnRenderer renderer)
			{
				if (ZombieRenderCompat.Pawn(renderer) is not Zombie zombie)
					return null;

				if (zombie.needsGraphics)
				{
					zombie.needsGraphics = false;
					ZombieGenerator.AssignNewGraphics(zombie);
				}

				return zombie;
			}

			static bool CanDrawPawnExtras(Pawn pawn)
				=> pawn?.Spawned == true && Tools.MapViewActiveFor(pawn.Map);

			[HarmonyPriority(Priority.First)]
			static bool Prefix(PawnRenderer __instance, Vector3 drawLoc)
			{
				var pawn = ZombieRenderCompat.Pawn(__instance);
				if (pawn is ZombieSpitter)
					return false;
				if (CanDrawPawnExtras(pawn) == false)
					return true;

				var zombie = PrepareZombieGraphics(__instance);
				if (zombie == null)
					return true;

				if (zombie.state == ZombieState.Emerging)
				{
					zombie.Render(__instance, drawLoc);
					return false;
				}

				if (zombie.isToxicSplasher && zombie.GetPosture() == PawnPosture.Standing)
					DrawToxicAura(zombie, drawLoc, true);

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
				var pawn = ZombieRenderCompat.Pawn(__instance);
				if (CanDrawPawnExtras(pawn) == false)
					return;

				if (pawn is not Zombie zombie)
					return;

				if (zombie.isAlbino && zombie.scream > 0)
				{
					var mats = Constants.screamPairs[zombie.scream];
					var f1 = zombie.scream / 400f;

					var size = f1 * 4f;
					var center = drawLoc + new Vector3(0, 0.1f, 0.25f);
					GraphicToolbox.DrawScaledMesh(Constants.screamMesh, mats.Item1, center, Quaternion.identity, size, size);

					var f2 = Mathf.Sin(Mathf.PI * f1);
					var q = Quaternion.AngleAxis(f2 * 360f, Vector3.up);
					GraphicToolbox.DrawScaledMesh(MeshPool.plane20, mats.Item2, center, q, 1.5f, 1.5f);
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
				var pawn = ZombieRenderCompat.Pawn(renderer);
				if (CanDrawPawnExtras(pawn) == false)
					return;
				DrawSymbiantHostAura(pawn, renderer, drawLoc);
				if (pawn is not Zombie zombie)
					return;
				if (zombie.state == ZombieState.Emerging || zombie.GetPosture() != PawnPosture.Standing)
					return;

				// general zombie drawing

				Verse.TickManager tm = null;
				var orientation = zombie.Rotation;

				if (zombie.IsSuicideBomber)
				{
					DrawBombVest(zombie, drawLoc, orientation);
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

				static void DrawBombVest(Zombie zombie, Vector3 drawLoc, Rot4 orientation)
				{
					var location = drawLoc;
					location.y += 0.04f;
					if (orientation == Rot4.North)
						location.y += Altitudes.AltInc / 12f;

					var f = 25f * (zombie.pather.nextCellCostLeft / zombie.pather.nextCellCostTotal);
					location.z += (Mathf.Max(0.5f, Mathf.Cos(f)) - 0.7f) / 20f;

					if (orientation == Rot4.South)
						GraphicToolbox.DrawScaledMesh(bodyMesh, Constants.BOMB_VEST[(int)FacingIndex.South], location, Quaternion.identity, 1f, 1f);
					else if (orientation == Rot4.North)
						GraphicToolbox.DrawScaledMesh(bodyMesh, Constants.BOMB_VEST[(int)FacingIndex.North], location, Quaternion.identity, 1f, 1f);
					else
					{
						var mesh = orientation == Rot4.West ? bodyMesh_flipped : bodyMesh;
						GraphicToolbox.DrawScaledMesh(mesh, Constants.BOMB_VEST[(int)FacingIndex.East], location, Quaternion.identity, 1f, 1f);
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
						float angle = healTarget.drawer.renderer.BodyAngle(PawnRenderFlags.None);
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

						var mesh = MeshPool.GetMeshSetForWidth(MeshPool.HumanlikeBodyWidth).MeshAt(orientation);
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
							if (ZombieAwarenessCues.ShouldPlayZombieActionSound())
							{
								var info = SoundInfo.InMap(zombie);
								CustomDefs.ElectricShock.PlayOneShot(info);
							}
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
							var eyeMat = zombie.isAlbino ? rageEyeWhite50 : Constants.RAGE_EYE;

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

				static void DrawSymbiantHostAura(Pawn pawn, PawnRenderer renderer, Vector3 drawLoc)
				{
					if (pawn == null || renderer == null || pawn.GetPosture() != PawnPosture.Standing)
						return;
					if (ZombieSymbiant.TryGetHostAuraFactor(pawn, out var factor) == false)
						return;
					if (TryMarkSymbiantAuraDrawn(pawn) == false)
						return;

					var angle = renderer.BodyAngle(PawnRenderFlags.None);
					if (pawn.Rotation == Rot4.West)
					angle -= leanAngle;
				if (pawn.Rotation == Rot4.East)
					angle += leanAngle;

					var loc = drawLoc + toxicAuraOffset;
					loc.y = moteAltitute;
					var scale = Mathf.Lerp(0.95f, 1.25f, Mathf.Clamp01(factor));
					GraphicToolbox.DrawScaledMesh(MeshPool.plane20, Constants.SYMBIANT_HOST_AURAS[Find.CameraDriver.CurrentZoom], loc, Quaternion.AngleAxis(angle, Vector3.up), scale, scale);
				}

				static bool TryMarkSymbiantAuraDrawn(Pawn pawn)
				{
					if (Time.frameCount != symbiantAuraDrawnFrame)
					{
						symbiantAuraDrawnFrame = Time.frameCount;
						symbiantAuraDrawnPawns.Clear();
					}
					return symbiantAuraDrawnPawns.Add(pawn.thingIDNumber);
				}

				static void DrawToxicAura(Zombie zombie, Vector3 drawLoc, bool behindBody)
			{
				float angle = zombie.drawer.renderer.BodyAngle(PawnRenderFlags.None);
				if (zombie.Rotation == Rot4.West)
					angle -= leanAngle;
				if (zombie.Rotation == Rot4.East)
					angle += leanAngle;
				var quat = Quaternion.AngleAxis(angle, Vector3.up);

				var idx = ((GenTicks.TicksGame + zombie.thingIDNumber) / 10) % 8;
				if (idx >= 5)
					idx = 8 - idx;

				var loc = drawLoc + toxicAuraOffset;
				if (behindBody)
					loc.y += PawnRenderUtility.AltitudeForLayer(-8f);
				GraphicToolbox.DrawScaledMesh(MeshPool.plane20, Constants.TOXIC_AURAS[idx], loc, quat, 1f, 1f);
			}
		}

		[HarmonyPatch(typeof(PawnRenderNode_Body))]
		[HarmonyPatch(nameof(PawnRenderNode_Body.GraphicFor))]
		static class PawnRenderNode_Body_GraphicFor_Patch
		{
			static bool Prefix(Pawn pawn, ref Graphic __result)
			{
				if (ZombieRenderCompat.TryGetBodyGraphic(pawn, out var graphic) == false)
					return true;
				__result = graphic;
				return false;
			}
		}

		[HarmonyPatch(typeof(PawnRenderNode_Head))]
		[HarmonyPatch(nameof(PawnRenderNode_Head.GraphicFor))]
		static class PawnRenderNode_Head_GraphicFor_Patch
		{
			static bool Prefix(Pawn pawn, ref Graphic __result)
			{
				if (ZombieRenderCompat.TryGetHeadGraphic(pawn, out var graphic) == false)
					return true;
				if (pawn.health?.hediffSet?.HasHead != true)
				{
					__result = null;
					return false;
				}
				__result = graphic;
				return false;
			}
		}

		// patch to suppress burning-zombie damage effecters while keeping normal attached fire visuals
		//
		[HarmonyPatch(typeof(Effecter), nameof(Effecter.Trigger))]
		static class Effecter_Trigger_Patch
		{
			static bool Prefix(EffecterDef ___def, TargetInfo A)
			{
				if (SuppressBurningZombieFireDamageFeedback)
					return false;
				if (___def != EffecterDefOf.Deflect_General)
					return true;
				return A.Thing is not Zombie;
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
				if (Tools.MapViewActiveFor(__instance) == false)
					return;
				if (ZombieSettings.Values.floatingZombies == false)
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
				if (inner == null)
				{
					Error("Cannot find RimWorld.PawnApparelGenerator.PossibleApparelSet");
					return null;
				}
				var method = AccessTools.Method(inner, "PairOverlapsAnything");
				if (method == null)
					Error("Cannot find RimWorld.PawnApparelGenerator.PossibleApparelSet.PairOverlapsAnything");
				return method;
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

		// patch to inform zombie generator that apparel texture could not load
		[HarmonyPatch(typeof(Graphic_Multi))]
		[HarmonyPatch(nameof(Graphic_Multi.Init))]
		public static class Graphic_Multi_Init_Patch
		{
			public static bool suppressError = false;
			public static bool textureError = false;

			static void CaptureTextureError(string text)
			{
				textureError = true;
				if (suppressError == false)
					Patches.Error(text);
			}

			[HarmonyPriority(Priority.First)]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var m1 = SymbolExtensions.GetMethodInfo(() => Log.Error(""));
				var m2 = SymbolExtensions.GetMethodInfo(() => CaptureTextureError(""));
				var found = false;
				foreach (var instruction in instructions)
				{
					if (instruction.Calls(m1))
					{
						instruction.opcode = OpCodes.Call;
						instruction.operand = m2;
						found = true;
					}
					yield return instruction;
				}
				if (found == false)
					Error("Expected Log.Error call in Graphic_Multi.Init");
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
					var map = zombie.Map;
					if (map == null)
						return ticks;
					var grid = map.GetGrid();
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
				var m_ModifyTicks = SymbolExtensions.GetMethodInfo(() => ModifyTicks(0, null));

				var found = false;
				foreach (var instruction in instructions)
				{
					if (instruction.Calls(m_SecondsToTicks))
					{
						var loadVerb = new CodeInstruction(OpCodes.Ldarg_0);
						loadVerb.labels.AddRange(instruction.labels);
						instruction.labels.Clear();
						yield return loadVerb;
						instruction.opcode = OpCodes.Call;
						instruction.operand = m_ModifyTicks;
						found = true;
					}
					yield return instruction;
				}

				if (!found)
					Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);
			}

		}

		[HarmonyPatch(typeof(SkillRecord))]
		[HarmonyPatch(nameof(SkillRecord.GetLevel))]
		static class SkillRecord_GetLevel_Patch
		{
			static void Postfix(SkillRecord __instance, ref int __result)
			{
				ZombieSymbiant.ApplySymbiantSkillBonus(__instance, ref __result);
			}
		}

		[HarmonyPatch(typeof(SkillRecord))]
		[HarmonyPatch(nameof(SkillRecord.GetLevelForUI))]
		static class SkillRecord_GetLevelForUI_Patch
		{
			static void Postfix(SkillRecord __instance, ref int __result)
			{
				ZombieSymbiant.ApplySymbiantSkillBonus(__instance, ref __result);
			}
		}

		// patch for variable zombie stats (speed, pain, melee, dodge)
		//
		[HarmonyPatch(typeof(StatExtension))]
		[HarmonyPatch(nameof(StatExtension.GetStatValue))]
		static class StatExtension_GetStatValue_Patch
		{
			static readonly float defaultHumanMoveSpeed = ThingDefOf.Human.statBases.First(mod => mod.stat == StatDefOf.MoveSpeed).value;
			static readonly StatDef cookingSpeed = DefDatabase<StatDef>.GetNamedSilentFail("CookingSpeed");
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
				if (thing is Zombie zombie)
				{
					if (stat == StatDefOf.MoveSpeed)
					{
						var tm = Find.TickManager;
						var multiplier = defaultHumanMoveSpeed / Mathf.Clamp(zombie.simulationTickRate, 0.05f, 1f);

						if (zombie.health?.Downed == true)
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
							var colonists = zombie.Map?.mapPawns?.FreeColonistsAndPrisonersSpawned;
							var minDistSquared = 450;
							if (colonists != null)
								for (var i = 0; i < colonists.Count; i++)
								{
									var distSquared = colonists[i].Position.DistanceToSquared(albinoPos);
									if (distSquared < minDistSquared)
										minDistSquared = distSquared;
								}
							albinoSpeed = GenMath.LerpDoubleClamped(36, 900, 5f, 1f, minDistSquared);
						}

						float speed;
						if (albinoSpeed > 1f || zombie.state == ZombieState.Tracking || zombie.raging > 0 || zombie.wasMapPawnBefore)
							speed = ZombieSettings.Values.moveSpeedTracking;
						else
							speed = ZombieSettings.Values.moveSpeedIdle;

						var factor = 1f;
						var bodyType = zombie.story?.bodyType;
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

					if (stat == StatDefOf.MeleeHitChance)
					{
						if (zombie.wasMapPawnBefore)
						{
							__result = 1f;
							return false;
						}

						if (zombie.health?.Downed == true)
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

						if (zombie.story?.bodyType == BodyTypeDefOf.Fat)
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

						var bodyType = zombie.story?.bodyType;
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
				}

				return true;
			}

			static void Postfix(Thing thing, StatDef stat, ref float __result)
			{
				if (thing is not Pawn pawn)
					return;
				if (stat == StatDefOf.MoveSpeed)
				{
					var moveBonusCount = ZombieSymbiant.MoveSpeedBenefitCount(pawn);
					if (moveBonusCount > 0)
						__result *= 1f + moveBonusCount * 0.25f;
					return;
				}
				if (stat != StatDefOf.MedicalTendSpeed
					&& stat != StatDefOf.WorkSpeedGlobal
					&& stat != StatDefOf.GeneralLaborSpeed
					&& stat != StatDefOf.CleaningSpeed
					&& stat != cookingSpeed)
					return;
				var efficiency = ZombieSymbiant.SymbiantCellEfficiencyFactor(pawn);
				if (efficiency >= 0.999f)
					return;
				__result *= efficiency;
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
				var bodyType = zombie.story?.bodyType;
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
				if (IsZombielandPawn(__instance?.pawn))
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
				if (IsZombielandPawn(__instance?.pawn))
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
				if (IsZombielandPawn(__instance?.pawn))
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
				if (__instance is ZombieCorpse || __instance is Pawn pawn && IsZombielandPawn(pawn))
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
				if (gizmos != null)
					foreach (var gizmo in gizmos)
						yield return gizmo;

				if (___pawn == null)
					yield break;

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
			internal static bool CanTrackZombieBiteInfection(Pawn pawn)
			{
				return pawn?.RaceProps?.Humanlike == true
					&& pawn.RaceProps.IsFlesh
					&& AlienTools.IsFleshPawn(pawn)
					&& SoSTools.IsHologram(pawn) == false;
			}

			[HarmonyPriority(Priority.First)]
			static bool Prefix(Hediff_Injury hd, ref bool __result)
			{
				if (hd is not Hediff_Injury_ZombieBite zombieBite)
					return true;

				if (CanTrackZombieBiteInfection(zombieBite.pawn))
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
			[HarmonyPriority(Priority.Last)]
			static IEnumerable<BodyPartRecord> Postfix(IEnumerable<BodyPartRecord> parts, Pawn pawn, RecipeDef recipe)
			{
				var yielded = new HashSet<BodyPartRecord>();
				if (parts != null)
					foreach (var part in parts)
						if (part != null && yielded.Add(part))
							yield return part;
				if (recipe != RecipeDefOf.RemoveBodyPart)
					yield break;
				if (pawn?.health?.hediffSet == null)
					yield break;

				var tmpHediffInjuryZombieBite = new List<Hediff_Injury_ZombieBite>();
				pawn.health.hediffSet.GetHediffs(ref tmpHediffInjuryZombieBite);
				for (var i = 0; i < tmpHediffInjuryZombieBite.Count; i++)
				{
					var part = tmpHediffInjuryZombieBite[i].Part;
					if (part != null && yielded.Add(part))
						yield return part;
				}
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
				if (pawn?.Dead == true && __instance.def.IsZombieHediff())
				{
					__result = false;
					return;
				}

				if (__instance is not Hediff_Injury_ZombieBite zombieBite)
					return;

				if (HediffUtility_CanHealNaturally_Patch.CanTrackZombieBiteInfection(pawn))
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

				if (IsZombielandPawn(__instance.parent as Pawn) && ZombieSettings.Values.zombiesBurnLonger && Rand.Chance(0.2f))
					__result = false;
			}
		}

		// patch for preventing eligible flame explosions from doing direct damage when they grant the fire-survival boost
		//
		sealed class FireSurvivalExplosionDamageSnapshot
		{
			public Zombie zombie;
			public bool hadFireSurvivalBoost;
			public Dictionary<Hediff_Injury, float> injurySeverities;

			public static FireSurvivalExplosionDamageSnapshot Make(Zombie zombie)
			{
				var injuries = zombie.health?.hediffSet?.hediffs?
					.OfType<Hediff_Injury>()
					.ToDictionary(injury => injury, injury => injury.Severity);
				if (injuries == null)
					return null;

				return new FireSurvivalExplosionDamageSnapshot
				{
					zombie = zombie,
					hadFireSurvivalBoost = zombie.HasFireSurvivalBoost,
					injurySeverities = injuries
				};
			}

			public void RestoreIfBoostWasGranted()
			{
				if (zombie == null || zombie.Destroyed || zombie.Dead || hadFireSurvivalBoost || zombie.HasFireSurvivalBoost == false)
					return;

				var injuries = zombie.health?.hediffSet?.hediffs?.OfType<Hediff_Injury>().ToArray();
				if (injuries == null)
					return;

				foreach (var injury in injuries)
				{
					if (injurySeverities.TryGetValue(injury, out var severity))
					{
						if (injury.Severity > severity)
							injury.Severity = severity;
					}
					else
						zombie.health.RemoveHediff(injury);
				}
			}
		}

		[HarmonyPatch(typeof(Verse.Explosion), "AffectCell")]
		static class Explosion_AffectCell_Patch
		{
			static void Prefix(Verse.Explosion __instance, IntVec3 c, out List<FireSurvivalExplosionDamageSnapshot> __state)
			{
				__state = null;
				if (CanGrantFireSurvivalBoost(__instance) == false)
					return;
				var map = __instance.Map;
				if (c.InBounds(map) == false)
					return;

				var things = c.GetThingList(map);
				for (var i = 0; i < things.Count; i++)
					if (things[i] is Zombie zombie
						&& zombie.Destroyed == false
						&& zombie.Dead == false
						&& IsEligibleZombieFireInstigator(__instance.instigator, zombie))
					{
						var snapshot = FireSurvivalExplosionDamageSnapshot.Make(zombie);
						if (snapshot != null)
						{
							__state ??= [];
							__state.Add(snapshot);
						}
					}
			}

			static void Postfix(List<FireSurvivalExplosionDamageSnapshot> __state)
			{
				if (__state == null)
					return;

				foreach (var snapshot in __state)
					snapshot.RestoreIfBoostWasGranted();
			}

			static bool CanGrantFireSurvivalBoost(Verse.Explosion explosion)
			{
				return ZombieSettings.Values.zombiesBurnLonger
					&& explosion?.Map != null
					&& (explosion.chanceToStartFire > 0f || explosion.damType == DamageDefOf.Flame);
			}
		}

		// patch for making zombies burn slower and spread fire faster on tar
		//
		[HarmonyPatch(typeof(Fire))]
		[HarmonyPatch(nameof(Fire.DoFireDamage))]
		static class Fire_DoFireDamage_Patch
		{
			static void Prefix(Fire __instance, Thing targ, out bool __state)
			{
				__state = targ is Pawn pawn && IsZombielandPawn(pawn) && ReferenceEquals(__instance.parent, pawn);
				if (__state == false)
					return;
				burningZombieFireDamageFeedbackDepth++;
			}

			static int FireDamagePatch(float f, Fire fire, Thing targ)
			{
				var num = GenMath.RoundRandom(f);
				if (ZombieSettings.Values.zombiesBurnLonger == false)
					return num;

				var pawn = targ as Pawn;
				if (IsZombielandPawn(pawn) == false)
					return num;

				if (pawn is Zombie zombie && zombie.HasFireSurvivalBoost)
				{
					if (fire?.parent == zombie)
						return Math.Max(2, num / 2);
					return Math.Max(1, num / 4);
				}

				return Math.Max(2, num / 2);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var m_RoundRandom = SymbolExtensions.GetMethodInfo(() => GenMath.RoundRandom(0f));
				var m_FireDamagePatch = SymbolExtensions.GetMethodInfo(() => FireDamagePatch(0f, null, null));

				var list = instructions.ToList();
				var idx = list.FirstIndexOf(code => code.Calls(m_RoundRandom));
				if (idx > 0 && idx < list.Count())
				{
					list[idx].opcode = OpCodes.Ldarg_0; // Fire instance
					list[idx].operand = null;
					list.Insert(idx + 1, new CodeInstruction(OpCodes.Ldarg_1)); // target thing
					list.Insert(idx + 2, new CodeInstruction(OpCodes.Call, m_FireDamagePatch));
				}
				else
					Error("Unexpected code in patch " + MethodBase.GetCurrentMethod().DeclaringType);

				return list;
			}

			static Exception Finalizer(Exception __exception, bool __state)
			{
				if (__state)
					burningZombieFireDamageFeedbackDepth = Math.Max(0, burningZombieFireDamageFeedbackDepth - 1);
				return __exception;
			}

			static void Postfix(Fire __instance, Thing targ)
			{
				if (targ is not TarSlime)
					return;
				var pos = targ.Position;
				var map = targ.Map ?? __instance.Map;
				if (map == null)
					return;
				if (__instance.fireSize < 0.5f)
					__instance.fireSize = 0.5f;
				var grid = map.thingGrid;
				foreach (var offset in GenAdj.AdjacentCellsAround)
				{
					var cell = pos + offset;
					if (cell.InBounds(map) == false)
						continue;
					var tar = grid.ThingAt<TarSlime>(cell);
					if (tar != null && tar.IsBurning() == false)
						FireUtility.TryStartFireIn(tar.Position, map, __instance.fireSize, null);
				}
			}
		}

		// patch for replacing vanilla white fire smoke with dark tar smoke on burning tar slime
		//
		[HarmonyPatch(typeof(Fire))]
		[HarmonyPatch(nameof(Fire.SpawnSmokeParticles))]
		static class Fire_SpawnSmokeParticles_Patch
		{
			static bool Prefix(Fire __instance)
			{
				var map = __instance.Map;
				if (map == null)
					return true;
				var things = __instance.Position.GetThingList(map);
				var hasTarSlime = false;
				for (var i = 0; i < things.Count; i++)
					if (things[i] is TarSlime)
					{
						hasTarSlime = true;
						break;
					}
				if (hasTarSlime == false)
					return true;

				var difficulty = Tools.Difficulty();
				SpawnTarSmoke(__instance.Position, map, Math.Max(1f, __instance.fireSize), difficulty, false);
				if (__instance.fireSize > 0.5f && __instance.parent == null)
					FleckMaker.ThrowFireGlow(__instance.Position.ToVector3Shifted(), map, __instance.fireSize);
				return false;
			}
		}

		// patch for replacing the vanilla fire crackle on burning zombies
		//
		[HarmonyPatch(typeof(SustainerAggregatorUtility))]
		[HarmonyPatch(nameof(SustainerAggregatorUtility.AggregateOrSpawnSustainerFor))]
		static class SustainerAggregatorUtility_AggregateOrSpawnSustainerFor_Patch
		{
			static void Prefix(ISizeReporter reporter, ref SoundDef def)
			{
				if (def != SoundDefOf.FireBurning)
					return;
				if (reporter is not Fire fire || IsZombielandPawn(fire.parent as Pawn) == false)
					return;
				def = CustomDefs.ZombieBurningSilencer ?? def;
			}
		}

		// patch for excluding burning zombies from total fire count
		//
		[HarmonyPatch(typeof(FireWatcher))]
		[HarmonyPatch(nameof(FireWatcher.UpdateObservations))]
		static class FireWatcher_UpdateObservations_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Map ___map, ref float ___fireDanger)
			{
				___fireDanger = 0f;
				if (___map == null)
					return false;
				var fires = ___map.listerThings.ThingsOfDef(ThingDefOf.Fire);
				for (var i = 0; i < fires.Count; i++)
				{
					var fire = fires[i] as Fire;
					if (fire == null)
						continue;
					if (IsZombielandPawn(fire?.parent as Pawn))
						continue;
					___fireDanger += 0.5f + fire.fireSize;
				}
				return false;
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
				return __instance == null || IsZombielandPawn(__instance.hitThing as Pawn) == false;
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
			public static bool Prefix(ref float __result, TargetInfo ___target, List<CoverInfo> ___covers)
			{
				var map = ___target.Map;
				if (map != null && ___target.Cell.GetGas(map)?.def == CustomDefs.TarSmoke)
				{
					__result = 0f;
					return false;
				}

				if (___covers != null)
					for (var i = 0; i < ___covers.Count; i++)
					{
						var cover = ___covers[i].thingInt;
						if (cover?.def == CustomDefs.TarSmoke)
						{
							__result = 0f;
							return false;
						}
					}
				return true;
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
				var damageDef = dinfo.Def;
				if (damageDef != DamageDefOf.EMP && damageDef != DamageDefOf.Stun)
					return;
				if (__instance?.parent is Zombie zombie && zombie.health?.Downed != true && zombie.Dead == false)
					if (zombie.IsActiveElectric)
						zombie.DisableElectric((int)(dinfo.Amount * 60));
			}
		}

		// patch to replace burning-zombie damage hit feedback with a quieter local sound
		//
		[HarmonyPatch(typeof(DamageWorker_AddInjury))]
		[HarmonyPatch("ApplyToPawn")]
		[HarmonyPatch(new[] { typeof(DamageInfo), typeof(Pawn) })]
		public static class DamageWorker_AddInjury_ApplyToPawn_Patch
		{
			static void Prefix(DamageInfo dinfo, Pawn pawn, out bool __state)
			{
				__state = IsBurningZombieFireDamage(pawn, dinfo);
				if (__state)
					burningZombieFireDamageFeedbackDepth++;
			}

			static Exception Finalizer(Exception __exception, bool __state)
			{
				if (__state)
					burningZombieFireDamageFeedbackDepth = Math.Max(0, burningZombieFireDamageFeedbackDepth - 1);
				return __exception;
			}
		}

		[HarmonyPatch(typeof(Pawn_DrawTracker))]
		[HarmonyPatch(nameof(Pawn_DrawTracker.Notify_DamageApplied))]
		public static class Pawn_DrawTracker_Notify_DamageApplied_Patch
		{
			static bool Prefix()
			{
				return SuppressBurningZombieFireDamageFeedback == false;
			}
		}

		[HarmonyPatch(typeof(ImpactSoundUtility))]
		[HarmonyPatch(nameof(ImpactSoundUtility.PlayImpactSound))]
		public static class ImpactSoundUtility_PlayImpactSound_Patch
		{
			static bool Prefix(Thing hitThing, Map map)
			{
				if (SuppressBurningZombieFireDamageFeedback == false)
					return true;
				PlayBurningZombieDamageSound(hitThing, map);
				return false;
			}
		}

		[HarmonyPatch(typeof(LifeStageUtility))]
		[HarmonyPatch(nameof(LifeStageUtility.PlayNearestLifestageSound))]
		public static class LifeStageUtility_PlayNearestLifestageSound_Patch
		{
			static bool Prefix()
			{
				return SuppressBurningZombieFireDamageFeedback == false;
			}
		}

		[HarmonyPatch(typeof(DamageWorker_AddInjury))]
		[HarmonyPatch(nameof(DamageWorker_AddInjury.ApplyDamageToPart))]
		public static class DamageWorker_AddInjury_ApplyDamageToPart_Patch
		{
			static float ReduceZombieDamage(Zombie zombie, DamageInfo dinfo, float factor)
			{
				var amount = dinfo.Amount / factor;
				if (dinfo.Def == DamageDefOf.Flame && zombie.HasFireSurvivalBoost)
					return Math.Max(1f, amount);
				return amount;
			}

			static bool Prefix(ref DamageInfo dinfo, Pawn pawn)
			{
				if (pawn is not Zombie zombie)
				{
					if (pawn is ZombieSpitter spitter)
						spitter.ApplySpitterDamageScaling(ref dinfo);
					return true;
				}

				if (zombie.health?.Downed == true)
					return true;

				if (zombie.wasMapPawnBefore)
				{
					dinfo.SetAllowDamagePropagation(false);
					dinfo.SetInstantPermanentInjury(false);
					var f1 = GenMath.LerpDouble(0, 5, 1, 10, Tools.Difficulty()) + (ShipCountdown.CountingDown ? 2f : 1f);
					dinfo.SetAmount(ReduceZombieDamage(zombie, dinfo, f1));
					return true;
				}

				var def2 = dinfo.Def;

				if (zombie.isAlbino)
					return def2?.isExplosive == true || Rand.Chance(0.25f);

				if (zombie.isDarkSlimer)
				{
					var pos = zombie.Position;
					var map = zombie.Map;
					if (map != null && pos.GetGas(map) == null)
					{
						var difficulty = Tools.Difficulty();
						SpawnTarSmoke(pos, map, 1 + difficulty, difficulty);
					}
				}

				if (zombie.IsActiveElectric)
				{
					if (def2 == null || def2.isRanged == false || def2.isExplosive)
						return true;

					var indices = new List<int>() { 0, 1, 2, 3 };
					indices.Shuffle();
					var markerCount = Rand.RangeInclusive(1, indices.Count);
					for (var i = 0; i < markerCount; i++)
					{
						zombie.absorbAttack.Add(new KeyValuePair<float, int>(dinfo.Angle, indices[i]));
						if (Rand.Chance(0.9f))
							zombie.absorbAttack.Add(new KeyValuePair<float, int>(0f, -1));
					}
					return false;
				}

				var f2 = Mathf.Max(1f, Tools.Difficulty()) + (ShipCountdown.CountingDown ? 2f : 1f);
				dinfo.SetAmount(ReduceZombieDamage(zombie, dinfo, f2));
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

				if (pawn is not Zombie zombie || part == null)
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
				if (part.groups?.Contains(BodyPartGroupDefOf.FullHead) == true || fakeHeadShot)
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

		}

		// patch for not slowing down time if pawn attacks a zombie
		//
		[HarmonyPatch(typeof(Verb))]
		[HarmonyPatch(nameof(Verb.CausesTimeSlowdown))]
		class Verb_CausesTimeSlowdown_Patch
		{
			static void Postfix(Verb __instance, ref bool __result, LocalTargetInfo castTarg)
			{
				var caster = __instance?.caster;

				if (__result == false || caster == null || castTarg.HasThing == false)
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

				var primaryVerb = __instance.equipment?.PrimaryEq?.PrimaryVerb;
				if (primaryVerb?.targetParams.canTargetLocations == true)
					return true;

				__result = __instance.meleeVerbs?.TryGetMeleeVerb(target);
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

				if (targ.HasThing == false)
				{
					__result = false;
					return false;
				}

				var verb = __instance.TryGetAttackVerb(targ.Thing);
				__result = verb?.TryStartCastOn(targ, false, true) == true;
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
				if (__instance == null)
					return;

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
				ZombieSymbiant.NotifyHostKilled(pawn);
				var raceProps = pawn.RaceProps;

				if (raceProps == null || raceProps.Humanlike == false || raceProps.IsFlesh == false)
					return;

				if (AlienTools.IsFleshPawn(pawn) == false || SoSTools.IsHologram(pawn))
					return;

				if (Customization.CannotBecomeZombie(pawn))
					return;

				var hediffSet = pawn.health?.hediffSet;
				if (hediffSet == null)
					return;

				// flag zombie bites to be infectious when pawn dies
				var zombieBites = pawn.GetHediffsList<Hediff_Injury_ZombieBite>();
				for (var i = 0; i < zombieBites.Count; i++)
				{
					var zombieBite = zombieBites[i];
					var tendDuration = zombieBite.TendDuration;
					if (tendDuration != null && tendDuration.GetInfectionState() >= InfectionState.BittenInfectable)
						zombieBite.mayBecomeZombieWhenDead = true;
				}

				// if death means becoming a zombie, install zombie infection
				if (ZombieSettings.Values.hoursAfterDeathToBecomeZombie > -1)
				{
					try
					{
						var brain = hediffSet.GetBrain();
						if (brain != null)
						{
							var hediff = HediffMaker.MakeHediff(CustomDefs.ZombieInfection, pawn, brain) as Hediff_ZombieInfection;
							if (hediff == null)
								return;
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
				if (__instance == null)
					return;
				_ = ZombieAreaManager.pawnsInDanger.Remove(__instance);
				if (IsZombielandPawn(__instance) == false && __instance.RaceProps?.Humanlike == true)
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
				var part = hediff.Part;
				if (part?.def?.tags?.Contains(BodyPartTagDefOf.ConsciousnessSource) == true && hediff.def.isBad)
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
				if (t?.def != ThingDefOf.Fire)
					return;
				if (___parent is Zombie zombie)
					zombie.NotifyFireAttached(ShouldGiveZombieFireSurvivalBoost(t as Fire, zombie));
			}
		}
		//
		[HarmonyPatch(typeof(CompAttachBase))]
		[HarmonyPatch(nameof(CompAttachBase.RemoveAttachment))]
		static class CompAttachBase_RemoveAttachment_Patch
		{
			static void Postfix(AttachableThing t, ThingWithComps ___parent, List<AttachableThing> ___attachments)
			{
				if (t?.def != ThingDefOf.Fire)
					return;
				if (___parent is not Zombie zombie)
					return;

				var anyFireRemaining = false;
				if (___attachments != null)
					for (var i = 0; i < ___attachments.Count; i++)
						if (___attachments[i]?.def == ThingDefOf.Fire)
						{
							anyFireRemaining = true;
							break;
						}
				zombie.NotifyFireRemoved(anyFireRemaining);
			}
		}

		static bool ShouldGiveZombieFireSurvivalBoost(Fire fire, Zombie zombie)
		{
			if (ZombieSettings.Values.zombiesBurnLonger == false || zombie == null || fire == null)
				return false;
			return IsEligibleZombieFireInstigator(fire.instigator, zombie);
		}

		static bool IsEligibleZombieFireInstigator(Thing instigator, Zombie target, int depth = 0)
		{
			if (depth > 4)
				return false;

			if (instigator == null)
				return true;

			if (instigator is Fire sourceFire)
			{
				if (sourceFire.parent is Zombie fireParentZombie && fireParentZombie.HasFireSurvivalBoost)
					return true;
				return IsEligibleZombieFireInstigator(sourceFire.instigator, target, depth + 1);
			}

			if (instigator is Zombie sourceZombie)
				return sourceZombie != target && sourceZombie.HasFireSurvivalBoost;

			if (instigator is Pawn pawn)
				return IsEligibleAnimalFireInstigator(pawn);

			var faction = instigator.Faction;
			if (faction?.def?.isPlayer == true || faction?.HostileTo(Faction.OfPlayer) == true)
				return false;

			return faction == null;
		}

		static bool IsEligibleAnimalFireInstigator(Pawn pawn)
		{
			if (pawn?.RaceProps?.Animal != true)
				return false;
			return pawn.Faction?.def?.isPlayer != true;
		}

		// patch for disallowing social interaction with zombies
		//
		static bool IsZombielandPawn(Pawn pawn)
		{
			return pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter;
		}

		static bool IsZombielandSocialPawn(Pawn pawn)
		{
			return IsZombielandPawn(pawn);
		}

		static bool IsZombielandCorpse(Corpse corpse)
		{
			return corpse is ZombieCorpse or ZombieSpitterCorpse || IsZombielandPawn(corpse?.InnerPawn);
		}

		[HarmonyPatch(typeof(TaleRecorder))]
		[HarmonyPatch(nameof(TaleRecorder.RecordTale))]
		static class TaleRecorder_RecordTale_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(TaleDef def, ref Tale __result)
			{
				if (def != null)
					return true;

				__result = null;
				return false;
			}
		}

		[HarmonyPatch(typeof(Thought_Memory), nameof(Thought_Memory.Save), MethodType.Getter)]
		static class Thought_Memory_Save_Patch
		{
			static void Postfix(Thought_Memory __instance, ref bool __result)
			{
				if (__result == false)
					return;
				if (__instance?.def.IsZombieDef() == true || IsZombielandSocialPawn(__instance?.otherPawn))
					__result = false;
			}
		}

		[HarmonyPatch(typeof(RelationsUtility))]
		[HarmonyPatch(nameof(RelationsUtility.HasAnySocialMemoryWith))]
		static class RelationsUtility_HasAnySocialMemoryWith_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn p, Pawn otherPawn, ref bool __result)
			{
				if (IsZombielandSocialPawn(p) || IsZombielandSocialPawn(otherPawn))
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
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn ___pawn, Pawn other, ref int __result)
			{
				if (IsZombielandSocialPawn(___pawn) || IsZombielandSocialPawn(other))
				{
					__result = 0;
					return false;
				}
				return true;
			}
		}
		[HarmonyPatch(typeof(RelationsUtility))]
		[HarmonyPatch(nameof(RelationsUtility.PawnsKnowEachOther))]
		static class RelationsUtility_PawnsKnowEachOther_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn p1, Pawn p2, ref bool __result)
			{
				if (IsZombielandSocialPawn(p1) || IsZombielandSocialPawn(p2))
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
				if (IsZombielandSocialPawn(otherPawn) || IsZombielandSocialPawn(__instance?.pawn))
				{
					outThoughts?.Clear();
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
				return !(IsZombielandSocialPawn(otherPawn) || IsZombielandSocialPawn(__instance?.pawn));
			}
		}
		[HarmonyPatch(typeof(Corpse))]
		[HarmonyPatch(nameof(Corpse.GiveObservedThought))]
		static class Corpse_GiveObservedThought_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Corpse __instance)
			{
				return IsZombielandCorpse(__instance) == false;
			}
		}
		[HarmonyPatch(typeof(Corpse))]
		[HarmonyPatch(nameof(Corpse.GiveObservedHistoryEvent))]
		static class Corpse_GiveObservedHistoryEvent_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Corpse __instance)
			{
				return IsZombielandCorpse(__instance) == false;
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
				if (IsZombielandSocialPawn(pawn))
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
				return IsZombielandCorpse(t as Corpse) == false;
			}
		}

		// patches to prevent interaction with zombies
		//
		[HarmonyPatch(typeof(Pawn_InteractionsTracker))]
		[HarmonyPatch(nameof(Pawn_InteractionsTracker.TryInteractWith))]
		static class Pawn_InteractionsTracker_TryInteractWith_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn ___pawn, Pawn recipient, ref bool __result)
			{
				if (IsZombielandSocialPawn(___pawn) || IsZombielandSocialPawn(recipient))
				{
					__result = false;
					return false;
				}
				return true;
			}
		}
		[HarmonyPatch(typeof(Pawn_InteractionsTracker))]
		[HarmonyPatch(nameof(Pawn_InteractionsTracker.InteractionsTrackerTickInterval))]
		static class Pawn_InteractionsTracker_InteractionsTrackerTickInterval_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn ___pawn)
			{
				return IsZombielandSocialPawn(___pawn) == false;
			}
		}

		// patch to colorize the label of zombies that were colonists
		//
		[HarmonyPatch(typeof(PawnNameColorUtility))]
		[HarmonyPatch(nameof(PawnNameColorUtility.PawnNameColorOf))]
		static class PawnNameColorUtility_PawnNameColorOf_Patch
		{
			static readonly Color zombieLabelColor = new(0.7f, 1f, 0.7f);

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

		// allow clicks on zombie corpses that were colonists
		//
		[HarmonyPatch(typeof(Selector))]
		[HarmonyPatch(nameof(Selector.SelectInternal))]
		static class Selector_SelectInternal_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(object obj)
			{
				if (obj is ZombieCorpse corpse && corpse.InnerPawn is Zombie zombie && zombie.wasMapPawnBefore == false)
					return false;
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
				if (t is Zombie zombie1 && zombie1.wasMapPawnBefore)
				{
					__result = true;
					return false;
				}
				if (t is ZombieCorpse corpse)
				{
					__result = corpse.InnerPawn is Zombie zombie2 && zombie2.wasMapPawnBefore;
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
			static bool ContainsBlockedZombieText(string text)
			{
				if (text == null)
					return false;
				if (text.IndexOf("zombie", StringComparison.OrdinalIgnoreCase) < 0)
					return false;
				return text.IndexOf("serum", StringComparison.OrdinalIgnoreCase) < 0
					&& text.IndexOf("extract", StringComparison.OrdinalIgnoreCase) < 0;
			}

			public static bool IsZombieDef(ThingDef thingDef)
			{
				if (thingDef == null)
					return false;

				if (ContainsBlockedZombieText(thingDef.defName))
					return true;

				if (ContainsBlockedZombieText(thingDef.description))
					return true;

				return false;
			}

			static bool Prefix(ThingDef thingDef, bool allow)
			{
				return allow == false || IsZombieDef(thingDef) == false;
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

		// patch for Death Pall raising zombie corpses as fresh Zombieland zombies
		//
		[HarmonyPatch]
		static class MutantUtility_CanResurrectAsShambler_Patch
		{
			static bool Prepare() => ModsConfig.AnomalyActive && TargetMethod() != null;

			static MethodBase TargetMethod()
			{
				return AccessTools.Method(typeof(MutantUtility), nameof(MutantUtility.CanResurrectAsShambler), new[] { typeof(Corpse), typeof(bool) });
			}

			static void Postfix(Corpse corpse, bool ignoreIndoors, ref bool __result)
			{
				if (__result == false && ZombieDeathPallUtility.CanDeathPallRaise(corpse, ignoreIndoors))
					__result = true;
			}
		}

		[HarmonyPatch]
		static class MutantUtility_ResurrectAsShambler_Patch
		{
			static bool Prepare() => ModsConfig.AnomalyActive && TargetMethod() != null;

			static MethodBase TargetMethod()
			{
				return AccessTools.Method(typeof(MutantUtility), nameof(MutantUtility.ResurrectAsShambler), new[] { typeof(Pawn), typeof(int), typeof(Faction) });
			}

			static bool Prefix(Pawn pawn)
			{
				if (pawn is Zombie zombie && zombie.Corpse is ZombieCorpse corpse)
				{
					_ = ZombieDeathPallUtility.TryRaiseZombieCorpse(corpse, out _);
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
			static void FixDef(ThingDef def)
			{
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
				def.inspectorTabs = new List<Type>();
				def.inspectorTabsResolved = new List<InspectTabBase>();
				def.passability = Traversability.Standable;
				def.stackLimit = 1;
			}

			static void Prefix(ThingDef def)
			{
				if (def == null || def.IsCorpse == false)
					return;

				var ingestibleSourceDef = def.ingestible?.sourceDef;
				var thingClass = ingestibleSourceDef switch
				{
					ThingDef_Zombie => typeof(ZombieCorpse),
					ThingDef_ZombieSpitter => typeof(ZombieSpitterCorpse),
					_ => null
				};
				if (thingClass == null)
					return;

				FixDef(def);
				def.selectable = true;
				def.thingClass = thingClass;
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
			static bool Prefix(Pawn ___pawn) => ___pawn is not ZombieSymbiant && ___pawn is not ZombieSpitter;

			static void Postfix(Pawn ___pawn)
			{
				if (IsZombielandPawn(___pawn))
					return;
				var map = ___pawn?.Map;
				if (map == null)
					return;

				var grid = map.GetGrid();
				if (grid == null)
					return;
				if (Constants.KILL_CIRCLE_RADIUS_MULTIPLIER > 0)
				{
					var timestamp = grid.GetTimestamp(___pawn.Position);
					if (timestamp > 0)
					{
						var radius = Tools.RadiusForPawn(___pawn) * Constants.KILL_CIRCLE_RADIUS_MULTIPLIER;
						radius /= ZombieSettings.Values.zombieInstinct.HalfToDoubleValue();
						foreach (var vec in Tools.GetCircle(radius))
						{
							var pos = ___pawn.Position + vec;
							var cell = grid.GetPheromone(pos, false);
							if (cell != null && cell.timestamp > 0 && cell.timestamp <= timestamp)
								cell.timestamp = 0;
						}
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
			const string Phase = "Game.FinalizeInit";

			static void Prefix()
			{
				ZombieBootstrap.ResetLogDedupers();
			}

			static void Postfix()
			{
				ApplyFinalizeInitSettings(Phase);
			}

			[HarmonyFinalizer]
			[HarmonyPriority(ZombieBootstrap.CaptureFinalizerPriority)]
			static Exception CaptureFinalizer(Exception __exception)
				=> ZombieBootstrap.CaptureFinalizerException(Phase, __exception);

			[HarmonyPriority(Priority.Last)]
			static Exception Finalizer(Exception __exception, bool __runOriginal)
			{
				if (ZombieBootstrap.ShouldRunFinalizerRecovery(Phase, __exception, __runOriginal, out var observedException, runWhenOriginalSucceeded: true) == false)
					return __exception;

				var phase = observedException == null ? Phase : $"{Phase} exception";
				// The Postfix owns normal settings. This clean-success finalizer only
				// sweeps already-loaded maps after vanilla game init reached Playing.
				if (observedException != null)
					ApplyFinalizeInitSettings(phase);

				var recovered = false;
				if (__runOriginal || observedException != null)
					ZombieBootstrap.RunSafely(phase, "map bootstrap sweep", () => Find.Maps?.Do(map => recovered |= ZombieBootstrap.EnsureMapStateAfterFinalize(phase, map, observedException == null)));
				return ZombieBootstrap.RecoveryPassthrough(phase, __exception, observedException, recovered);
			}

			static void ApplyFinalizeInitSettings(string phase)
			{
				ZombieBootstrap.RunSafely(phase, "Twinkie graphics", () => Tools.EnableTwinkie(ZombieSettings.Values.replaceTwinkie));
				ZombieBootstrap.RunSafely(phase, "zombie health scale", () => CustomDefs.Zombie.race.baseHealthScale = ZombieSettings.Values.healthFactor);
				ZombieBootstrap.RunSafely(phase, "symbiant cache clear", ZombieSymbiant.ClearActiveSymbiantCaches);
				ZombieBootstrap.RunSafely(phase, "RimHUD integration", RimHudIntegration.TryApplyForActiveGame);
			}
		}

		// patch for retrying essential map state after partial vanilla map init
		//
		[HarmonyPatch(typeof(Map))]
		[HarmonyPatch(nameof(Map.FinalizeInit))]
		static class Map_FinalizeInit_Patch
		{
			const string Phase = "Map.FinalizeInit";

			[HarmonyFinalizer]
			[HarmonyPriority(ZombieBootstrap.CaptureFinalizerPriority)]
			static Exception CaptureFinalizer(Exception __exception)
				=> ZombieBootstrap.CaptureFinalizerException(Phase, __exception);

			[HarmonyPriority(Priority.Last)]
			static Exception Finalizer(Map __instance, Exception __exception, bool __runOriginal)
			{
				if (ZombieBootstrap.ShouldRunFinalizerRecovery(Phase, __exception, __runOriginal, out var observedException, runWhenOriginalSucceeded: true) == false)
					return __exception;

				var phase = observedException == null ? Phase : $"{Phase} exception";
				var recovered = ZombieBootstrap.EnsureMapStateAfterFinalize(phase, __instance, observedException == null);
				return ZombieBootstrap.RecoveryPassthrough(phase, __exception, observedException, recovered);
			}
		}

		// patches to update our zombie count grid
		//
		[HarmonyPatch(typeof(Map))]
		[HarmonyPatch(nameof(Map.FinalizeLoading))]
		static class Map_FinalizeLoading_Patch
		{
			const string Phase = "Map.FinalizeLoading";

			static void Prefix(Map __instance)
			{
				ZombieBootstrap.ResetZombieGrid(Phase, __instance);
			}

			[HarmonyFinalizer]
			[HarmonyPriority(ZombieBootstrap.CaptureFinalizerPriority)]
			static Exception CaptureFinalizer(Exception __exception)
				=> ZombieBootstrap.CaptureFinalizerException(Phase, __exception);

			[HarmonyPriority(Priority.Last)]
			static Exception Finalizer(Map __instance, Exception __exception, bool __runOriginal)
			{
				if (ZombieBootstrap.ShouldRunFinalizerRecovery(Phase, __exception, __runOriginal, out var observedException) == false)
					return __exception;

				var phase = observedException == null ? Phase : $"{Phase} exception";
				if (observedException != null)
					ZombieBootstrap.ResetZombieGrid(phase, __instance, false);
				var recovered = false;
				if (__runOriginal || observedException != null)
					recovered = ZombieBootstrap.EnsureMapStateAfterFinalize(phase, __instance, false);
				return ZombieBootstrap.RecoveryPassthrough(phase, __exception, observedException, recovered);
			}
		}

		// patch for retrying essential map state after RimWorld's per-component init loop
		//
		[HarmonyPatch(typeof(MapComponentUtility))]
		[HarmonyPatch(nameof(MapComponentUtility.FinalizeInit))]
		static class MapComponentUtility_FinalizeInit_Patch
		{
			const string Phase = "MapComponentUtility.FinalizeInit";

			[HarmonyFinalizer]
			[HarmonyPriority(ZombieBootstrap.CaptureFinalizerPriority)]
			static Exception CaptureFinalizer(Exception __exception)
				=> ZombieBootstrap.CaptureFinalizerException(Phase, __exception);

			[HarmonyPriority(Priority.Last)]
			static Exception Finalizer(Map map, Exception __exception, bool __runOriginal)
			{
				if (ZombieBootstrap.ShouldRunFinalizerRecovery(Phase, __exception, __runOriginal, out var observedException, runWhenOriginalSucceeded: true) == false)
					return __exception;

				var phase = observedException == null ? Phase : $"{Phase} exception";
				var recovered = ZombieBootstrap.EnsureMapStateAfterFinalize(phase, map, observedException == null);
				return ZombieBootstrap.RecoveryPassthrough(phase, __exception, observedException, recovered);
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
				var pawn = __instance?.InnerPawn;
				if (pawn == null || IsZombielandPawn(pawn) || pawn.health == null || pawn.RaceProps?.Humanlike != true)
					return;

				var rotStage = __instance.GetRotStage();
				if (rotStage == RotStage.Fresh || rotStage == RotStage.Dessicated)
					return;

				var hediffSet = pawn.health.hediffSet;
				var hasBrain = hediffSet?.GetBrain() != null;
				if (hasBrain == false)
					return;

				var shouldBecomeZombie = false;
				var zombieBites = pawn.GetHediffsList<Hediff_Injury_ZombieBite>();
				for (var i = 0; i < zombieBites.Count; i++)
				{
					var tendDuration = zombieBites[i].TendDuration;
					if (tendDuration != null && tendDuration.GetInfectionState() >= InfectionState.BittenInfectable)
					{
						shouldBecomeZombie = true;
						break;
					}
				}

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
			const int CorpseRareTickInterval = 250;
			static List<Hediff_ZombieInfection> tmpHediffZombieInfections = new();

			static void Postfix(Corpse __instance)
			{
				var pawn = __instance?.InnerPawn;
				if (pawn == null || IsZombielandPawn(pawn) || pawn.health == null || pawn.RaceProps?.Humanlike != true)
					return;

				var rotStage = __instance.GetRotStage();
				if (rotStage == RotStage.Dessicated)
					return;

				var hediffSet = pawn.health.hediffSet;
				var hasBrain = hediffSet?.GetBrain() != null;
				if (hasBrain == false)
					return;

				var ticks = GenTicks.TicksGame;
				tmpHediffZombieInfections.Clear();
				hediffSet.GetHediffs(ref tmpHediffZombieInfections);
				if (ZombieFreeEventManager.IsActiveNow())
				{
					for (var i = 0; i < tmpHediffZombieInfections.Count; i++)
						if (tmpHediffZombieInfections[i].ticksWhenBecomingZombie >= 0)
							tmpHediffZombieInfections[i].ticksWhenBecomingZombie += CorpseRareTickInterval;
					return;
				}

				var shouldBecomeZombie = false;
				for (var i = 0; i < tmpHediffZombieInfections.Count; i++)
				{
					if (ticks <= tmpHediffZombieInfections[i].ticksWhenBecomingZombie)
						continue;
					shouldBecomeZombie = true;
					break;
				}

				if (shouldBecomeZombie)
				{
					var map = ThingOwnerUtility.GetRootMap(__instance);
					if (map != null)
						Tools.QueueConvertToZombie(__instance, map);
				}
			}
		}

		[HarmonyPatch(typeof(RecipeDef))]
		[HarmonyPatch(nameof(RecipeDef.PotentiallyMissingIngredients))]
		static class RecipeDef_PotentiallyMissingIngredients_Patch
		{
			static void Postfix(RecipeDef __instance, ref IEnumerable<ThingDef> __result)
			{
				if (__instance?.defName != "SeverSymbiantSymbiosis" || __result == null)
					return;

				// The health float menu disables surgeries with missing recipe ingredients,
				// but the resulting medical bill can wait for them through the normal bill path.
				__result = Enumerable.Empty<ThingDef>();
			}
		}

		// show infection on dead pawns
		//
		[HarmonyPatch(typeof(HealthCardUtility))]
		[HarmonyPatch(nameof(HealthCardUtility.DrawOverviewTab))]
		static class HealthCardUtility_DrawOverviewTab_Patch
		{
			static List<Hediff_Injury_ZombieBite> tmpHediffInjuryZombieBites = new();

			static void Postfix(Pawn pawn, Rect rect, ref float __result)
			{
				if (pawn == null || pawn.health == null)
					return;

				var hediffSet = pawn.health.hediffSet;
				if (hediffSet?.GetBrain() == null)
					return;

				if (pawn.Dead)
				{
					tmpHediffInjuryZombieBites.Clear();
					hediffSet.GetHediffs(ref tmpHediffInjuryZombieBites);
					var mayBecomeZombieWhenDead = false;
					for (var i = 0; i < tmpHediffInjuryZombieBites.Count; i++)
						if (tmpHediffInjuryZombieBites[i].mayBecomeZombieWhenDead)
						{
							mayBecomeZombieWhenDead = true;
							break;
						}
					if (mayBecomeZombieWhenDead == false)
						return;
				}
				else
				{
					if (pawn.InfectionState() < InfectionState.BittenInfectable)
						return;
				}

				__result += 15f;
				var oldColor = GUI.color;
				GUI.color = Color.red;
				var text = "BodyIsInfectedLabel".Translate();
				var textHeight = Text.CalcHeight(text, rect.width);
				Widgets.Label(new Rect(0f, __result, rect.width, textHeight), text);
				TooltipHandler.TipRegion(new Rect(0f, __result, rect.width, textHeight), "BodyIsInfectedTooltip".Translate());
				__result += textHeight;
				GUI.color = oldColor;
			}
		}

		// patch to handle targets deaths so that we update our grid
		//
		[HarmonyPatch(typeof(PawnComponentsUtility))]
		[HarmonyPatch(nameof(PawnComponentsUtility.RemoveComponentsOnKilled))]
		static class PawnComponentsUtility_RemoveComponentsOnKilled_Patch
		{
			static readonly FieldInfo prevMapField = AccessTools.Field(typeof(Pawn), "prevMap");

			static void Postfix(Pawn pawn)
			{
				if (pawn == null || IsZombielandPawn(pawn))
					return;

				var map = pawn.Map ?? pawn.MapHeld ?? prevMapField?.GetValue(pawn) as Map;
				if (map == null)
					return;

				if (Constants.KILL_CIRCLE_RADIUS_MULTIPLIER > 0)
				{
					var grid = map.GetGrid();
					if (grid == null)
						return;

					var position = pawn.Position;
					var timestamp = grid.GetTimestamp(position);
					if (timestamp <= 0)
						return;

					var radius = Tools.RadiusForPawn(pawn) * Constants.KILL_CIRCLE_RADIUS_MULTIPLIER;
					radius /= ZombieSettings.Values.zombieInstinct.HalfToDoubleValue();
					foreach (var vec in Tools.GetCircle(radius))
					{
						var pos = position + vec;
						var cell = grid.GetPheromone(pos, false);
						if (cell != null && cell.timestamp > 0 && cell.timestamp <= timestamp)
							grid.SetTimestamp(pos, 0);
					}
				}
			}
		}

		// patch to prevent thoughts on zombies
		//
		static List<Pawn> AllAlivePawnsSnapshot()
		{
			return PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive.ToList();
		}

		static List<Pawn> AllAliveColonistsSnapshot()
		{
			return PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.ToList();
		}

		static IEnumerable<CodeInstruction> ReplaceMethodOrWarn(IEnumerable<CodeInstruction> instructions, MethodInfo from, MethodInfo to, string patchName)
		{
			var list = instructions.ToList();
			if (from == null || to == null)
			{
				Log.Warning($"{patchName} skipped method replacement because a replacement endpoint was not found");
				return list;
			}

			var replaced = false;
			for (var i = 0; i < list.Count; i++)
			{
				var instruction = list[i];
				if (instruction.Calls(from))
				{
					instruction.operand = to;
					replaced = true;
				}
			}

			if (replaced == false)
				Log.Warning($"{patchName} could not find call to {from.DeclaringType?.FullName}.{from.Name}");
			return list;
		}

		[HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility), "AppendThoughts_ForHumanlike")]
		static class PawnDiedOrDownedThoughtsUtility_AppendThoughts_ForHumanlike_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var from = AccessTools.PropertyGetter(typeof(PawnsFinder), nameof(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive));
				var to = SymbolExtensions.GetMethodInfo(() => AllAlivePawnsSnapshot());
				return ReplaceMethodOrWarn(instructions, from, to, nameof(PawnDiedOrDownedThoughtsUtility_AppendThoughts_ForHumanlike_Patch));
			}
		}

		[HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility))]
		[HarmonyPatch(nameof(PawnDiedOrDownedThoughtsUtility.TryGiveThoughts))]
		[HarmonyPatch(new Type[] { typeof(Pawn), typeof(DamageInfo?), typeof(PawnDiedOrDownedThoughtsKind) })]
		static class PawnDiedOrDownedThoughtsUtility_TryGiveThoughts_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn victim)
			{
				return victim is not Zombie || victim.DevelopmentalStage.Child();
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var from = AccessTools.PropertyGetter(typeof(PawnsFinder), nameof(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists));
				var to = SymbolExtensions.GetMethodInfo(() => AllAliveColonistsSnapshot());
				return ReplaceMethodOrWarn(instructions, from, to, nameof(PawnDiedOrDownedThoughtsUtility_TryGiveThoughts_Patch));
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
				var taleDef = __instance?.def?.taleDef;
				if (taleDef == null || taleDef != TaleDefOf.KilledChild)
					return;
				var tale = Find.TaleManager?.GetLatestTale(taleDef, __instance.otherPawn);
				if (tale is not Tale_DoublePawn doublePawn || doublePawn.secondPawnData?.faction?.def != ZombieDefOf.Zombies)
					return;
				__result *= 0.25f;
			}
		}

		// patch to remove immunity ticks on zombies
		//
		[HarmonyPatch(typeof(ImmunityHandler))]
		[HarmonyPatch(nameof(ImmunityHandler.ImmunityHandlerTickInterval))]
		static class ImmunityHandler_ImmunityHandlerTickInterval_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(ImmunityHandler __instance)
			{
				return IsZombielandPawn(__instance?.pawn) == false;
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
				var verbProps = pawn.equipment?.PrimaryEq?.PrimaryVerb?.verbProps;
				if (verbProps != null)
					noiseScale = verbProps.muzzleFlashScale / Constants.BASE_MUZZLE_FLASH_VALUE;

				var now = Tools.Ticks();
				var pos = origin.ToIntVec3();
				var magnitude = usedTarget == null ? (Constants.WEAPON_RANGE[0] + Constants.WEAPON_RANGE[1]) / 2 : (usedTarget.CenterVector3 - origin).magnitude * noiseScale * Math.Min(1f, ZombieSettings.Values.zombieInstinct.HalfToDoubleValue());
				var radius = Tools.Boxed(magnitude, Constants.WEAPON_RANGE[0], Constants.WEAPON_RANGE[1]);
				var grid = pawn.Map.GetGrid();
				if (grid == null)
					return;

				foreach (var vec in Tools.GetCircle(radius))
					grid.BumpTimestamp(pos + vec, now - vec.LengthHorizontalSquared);
			}
		}

		// patch to allow zombies to occupy the same spot without collision
		//
		[HarmonyPatch(typeof(Pawn_PathFollower), "WillCollideWithPawnAt")]
		static class Pawn_PathFollower_WillCollideWithPawnAt_Patch
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
		static readonly FieldInfo needsTrackerPawnField = AccessTools.Field(typeof(Pawn_NeedsTracker), "pawn");
		static readonly FieldInfo needsTrackerNeedsField = AccessTools.Field(typeof(Pawn_NeedsTracker), "needs");
		static readonly FieldInfo needsTrackerMiscNeedsField = AccessTools.Field(typeof(Pawn_NeedsTracker), "needsMisc");

		static Pawn PawnForNeedsTracker(Pawn_NeedsTracker needsTracker)
		{
			if (needsTracker == null)
				return null;

			return needsTrackerPawnField?.GetValue(needsTracker) as Pawn;
		}

		static void ClearNeeds(Pawn_NeedsTracker needsTracker)
		{
			if (needsTracker == null)
				return;

			(needsTrackerNeedsField?.GetValue(needsTracker) as List<Need>)?.Clear();
			(needsTrackerMiscNeedsField?.GetValue(needsTracker) as List<Need>)?.Clear();
			needsTracker.BindDirectNeedFields();
		}

		[HarmonyPatch(typeof(Pawn_NeedsTracker))]
		[HarmonyPatch(nameof(Pawn_NeedsTracker.AllNeeds), MethodType.Getter)]
		static class Pawn_NeedsTracker_AllNeeds_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn_NeedsTracker __instance, ref List<Need> __result)
			{
				if (IsZombielandPawn(PawnForNeedsTracker(__instance)) == false)
					return true;

				__result = new List<Need>();
				return false;
			}
		}
		[HarmonyPatch(typeof(Pawn_NeedsTracker))]
		[HarmonyPatch(nameof(Pawn_NeedsTracker.AddOrRemoveNeedsAsAppropriate))]
		static class Pawn_NeedsTracker_AddOrRemoveNeedsAsAppropriate_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn_NeedsTracker __instance)
			{
				if (IsZombielandPawn(PawnForNeedsTracker(__instance)) == false)
					return true;

				ClearNeeds(__instance);
				return false;
			}
		}
		[HarmonyPatch(typeof(Pawn_NeedsTracker))]
		[HarmonyPatch(nameof(Pawn_NeedsTracker.NeedsTrackerTickInterval))]
		static class Pawn_NeedsTracker_NeedsTrackerTickInterval_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn_NeedsTracker __instance)
			{
				return IsZombielandPawn(PawnForNeedsTracker(__instance)) == false;
			}
		}

		// patches so zombies don't use clamors at all
		//
		static MethodInfo RequiredMethod(Type type, string methodName)
		{
			var method = AccessTools.Method(type, methodName);
			if (method == null)
				throw new MissingMethodException(type.FullName, methodName);
			return method;
		}

		[HarmonyPatch]
		static class GenClamor_DoClamor_Patch
		{
			static IEnumerable<MethodBase> TargetMethods()
			{
				var methods = AccessTools.GetDeclaredMethods(typeof(GenClamor))
					.Where(method => method.Name == nameof(GenClamor.DoClamor))
					.ToArray();
				if (methods.Length == 0)
					throw new MissingMethodException(typeof(GenClamor).FullName, nameof(GenClamor.DoClamor));
				return methods;
			}

			[HarmonyPriority(Priority.First)]
			static bool Prefix(Thing source)
			{
				return IsZombielandPawn(source as Pawn) == false;
			}
		}
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch(nameof(Pawn.HearClamor))]
		static class Pawn_HearClamor_Patch
		{
			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn __instance, Thing source)
			{
				return IsZombielandPawn(__instance) == false && IsZombielandPawn(source as Pawn) == false;
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
				yield return RequiredMethod(type, nameof(Pawn_RecordsTracker.AddTo));
				yield return RequiredMethod(type, nameof(Pawn_RecordsTracker.RecordsTickUpdate));
				yield return RequiredMethod(type, nameof(Pawn_RecordsTracker.Increment));
			}

			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn ___pawn)
			{
				return IsZombielandPawn(___pawn) == false;
			}
		}
		[HarmonyPatch(typeof(Pawn_RecordsTracker))]
		[HarmonyPatch(nameof(Pawn_RecordsTracker.GetValue))]
		static class Pawn_RecordsTracker_GetValue_Patch
		{
			static bool Prefix(Pawn ___pawn, ref float __result)
			{
				if (IsZombielandPawn(___pawn))
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
				if (IsZombielandPawn(___pawn))
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
			static void Postfix(Pawn pawn, IntVec3 c, ref float __result)
			{
				var map = pawn?.Map;
				if (map == null)
					return;
				var isZombielandPawn = IsZombielandPawn(pawn);
				if (map.thingGrid.ThingAt<TarSlime>(c) != null)
				{
					if (isZombielandPawn)
						__result = GenMath.LerpDouble(0, 5, 150, 14, Tools.Difficulty());
					else
						__result = GenMath.LerpDouble(0, 5, 14, 400, Tools.Difficulty());
				}
				if (ZombieSymbiant.DebugDisablePathCost == false
					&& ZombieSymbiant.IsSymbiantCellForSlowedPawn(pawn, c, out _))
				{
					var symbiantMoveCost = ZombieSymbiant.SymbiantMoveCost(pawn, __result);
					if (symbiantMoveCost > __result)
						__result = symbiantMoveCost;
				}
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
				if (IsZombielandPawn(pawn) == false)
					return false;
				if (ZombieSettings.Values.zombiesDropBlood == false)
					return true;
				if (pawn is not Zombie zombie)
					return false;
				if (zombie.hasTankyShield > 0 || zombie.hasTankyHelmet > 0 || zombie.hasTankySuit > 0)
					return true;
				return false;
			}

			[HarmonyPriority(Priority.First)]
			static bool Prefix(Pawn ___pawn)
			{
				return SkipDropBlood(___pawn) == false;
			}
		}

		// patch to insert our difficulty settings into the custom storyteller UI
		/*
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
		*/

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
			static void Prefix(ref IEnumerable<Page> pages)
			{
				if (pages == null)
					return;
				var list = pages as List<Page> ?? pages.ToList();
				if (list.Count == 0)
					return;
				list.Insert(Math.Min(1, list.Count), new Dialog_Settings());
				pages = list;
			}
		}

		// set hostility response to attack as default
		//
		[HarmonyPatch(typeof(Game))]
		[HarmonyPatch(nameof(Game.InitNewGame))]
		class Game_InitNewGame_Patch
		{
			static void Prefix()
			{
				ZombieBootstrap.ResetLogDedupers();
			}

			static void Postfix()
			{
				var colonists = Find.CurrentMap?.mapPawns?.FreeColonists;
				if (colonists != null)
					for (var i = 0; i < colonists.Count; i++)
					{
						var playerSettings = colonists[i]?.playerSettings;
						if (playerSettings != null)
							playerSettings.hostilityResponse = HostilityResponseMode.Attack;
					}
				RimHudIntegration.TryApplyForActiveGame();
			}
		}

		// suppress memories of zombie violence
		//
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch(nameof(Pawn.PreApplyDamage))]
		static class Pawn_PreApplyDamage_Patch
		{
			static bool Prefix(Pawn __instance, ref DamageInfo dinfo, ref bool absorbed)
			{
				if (__instance == null)
					return true;
				if (__instance is ZombieSymbiant symbiant)
				{
					symbiant.PreApplyLinkedDamage(ref dinfo, ref absorbed);
					return absorbed == false;
				}
				ZombieSymbiant.PreApplyHostLinkedDamage(__instance, ref dinfo, ref absorbed);
				return absorbed == false;
			}
		}

		[HarmonyPatch(typeof(Pawn_HealthTracker))]
		[HarmonyPatch(nameof(Pawn_HealthTracker.PreApplyDamage))]
		static class Pawn_HealthTracker_PreApplyDamage_Patch
		{
			static bool ShouldSuppressZombieDamageMemory(Pawn instigator)
			{
				return instigator is Zombie;
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
			{
				var m_TryGainMemory = typeof(MemoryThoughtHandler).MethodNamed(nameof(MemoryThoughtHandler.TryGainMemory), new Type[] { typeof(ThoughtDef), typeof(Pawn), typeof(Precept) });
				var m_ShouldSuppressZombieDamageMemory = SymbolExtensions.GetMethodInfo(() => ShouldSuppressZombieDamageMemory(null));
				var f_HarmedMe = AccessTools.Field(typeof(ThoughtDefOf), nameof(ThoughtDefOf.HarmedMe));

				var list = instructions.ToList();
				var callIndex = list.FirstIndexOf(instr => instr.Calls(m_TryGainMemory));
				if (callIndex < 3 || callIndex + 1 >= list.Count)
				{
					Log.Warning($"{nameof(Pawn_HealthTracker_PreApplyDamage_Patch)} could not find the HarmedMe TryGainMemory call");
					return list;
				}

				var harmedMeIndex = callIndex - 3;
				var instigatorIndex = callIndex - 2;
				if (Equals(list[harmedMeIndex].operand, f_HarmedMe) == false || list[instigatorIndex].IsLdloc() == false || list[callIndex - 1].opcode != OpCodes.Ldnull)
				{
					Log.Warning($"{nameof(Pawn_HealthTracker_PreApplyDamage_Patch)} found an unexpected TryGainMemory argument shape");
					return list;
				}

				var startIndex = -1;
				for (var i = callIndex; i >= 0; i--)
					if (list[i].IsLdarg(0))
					{
						startIndex = i;
						break;
					}
				if (startIndex < 0)
				{
					Log.Warning($"{nameof(Pawn_HealthTracker_PreApplyDamage_Patch)} could not find the HarmedMe memory-load start");
					return list;
				}

				var skipLabel = generator.DefineLabel();
				list[callIndex + 1].labels.Add(skipLabel);

				var loadInstigator = new CodeInstruction(list[instigatorIndex].opcode, list[instigatorIndex].operand);
				loadInstigator.labels.AddRange(list[startIndex].labels);
				list[startIndex].labels.Clear();
				list.InsertRange(startIndex, new[]
				{
					loadInstigator,
					new CodeInstruction(OpCodes.Call, m_ShouldSuppressZombieDamageMemory),
					new CodeInstruction(OpCodes.Brtrue, skipLabel)
				});

				return list;
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
		[HarmonyPatch(typeof(MainMenuDrawer))]
		[HarmonyPatch(nameof(MainMenuDrawer.DoMainMenuControls))]
		static class MainMenuDrawer_DoMainMenuControls_Path
		{
			// called from MainTabWindow_Menu_RequestedTabSize_Path
			public const float addedHeight = 45f + 7f; // default height ListableOption + OptionListingUtility.DrawOptionListing spacing

			static readonly MethodInfo[] patchMethods = new MethodInfo[] {
				SymbolExtensions.GetMethodInfo(() => DrawOptionListingPatch1(Rect.zero, null)),
				SymbolExtensions.GetMethodInfo(() => DrawOptionListingPatch2(Rect.zero, null))
			};

			static void OpenZombielandSettings()
			{
				MainMenuDrawer.CloseMainTab();
				var me = LoadedModManager.GetMod<ZombielandMod>();
				if (me == null)
				{
					Error("LoadedModManager.GetMod<ZombielandMod>() failed");
					return;
				}
				Find.WindowStack.Add(new Dialog_ModSettings(me));
			}

			static float DrawOptionListingPatch1(Rect rect, List<ListableOption> optList)
			{
				if (Current.ProgramState == ProgramState.Playing && optList != null)
				{
					var label = "Options".Translate();
					var idx = optList.FirstIndexOf(opt => opt.label == label);
					var option = new ListableOption_Zombieland(OpenZombielandSettings);
					if (idx >= 0)
						optList.Insert(idx, option);
					else
						optList.Add(option);
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

				var list = instructions.ToList();
				var counter = 0;
				for (var i = 0; i < list.Count; i++)
				{
					if (list[i].Calls(m_DrawOptionListing) == false)
						continue;
					if (counter >= patchMethods.Length)
					{
						Log.Warning($"{nameof(MainMenuDrawer_DoMainMenuControls_Path)} found more option-listing calls than expected");
						continue;
					}
					list[i].operand = patchMethods[counter++];
				}

				if (counter != patchMethods.Length)
					Log.Warning($"{nameof(MainMenuDrawer_DoMainMenuControls_Path)} found {counter} option-listing calls instead of {patchMethods.Length}");
				return list;
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
				___map?.GetComponent<TickManager>()?.zombiePathing?.UpdateRegions();
			}
		}

		// adds sudden zombies to unfogged rooms
		//
		[HarmonyPatch(typeof(Building), nameof(Building.DeSpawn))]
		static class Building_DeSpawn_Patch
		{
			static void Prefix(Building __instance, DestroyMode mode)
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
				var shouldUnfog = false;
				foreach (var offset in GenAdj.AdjacentCells)
				{
					var cell = pos + offset;
					if (cell.InBounds(map) && fogGrid.IsFogged(cell) == false)
					{
						shouldUnfog = true;
						break;
					}
				}
				if (shouldUnfog == false)
					return;

				foreach (var offset in GenAdj.AdjacentCells)
				{
					var cell = pos + offset;
					if (cell.InBounds(map) == false || fogGrid.IsFogged(cell) == false)
						continue;

					var edifice = cell.GetEdifice(map);
					if (edifice == null || edifice.def.MakeFog == false)
						Tools.SpawnZombiesInRoom(map, cell);
				}
			}
		}

		//
		[HarmonyPatch(typeof(FogGrid), nameof(FogGrid.Notify_PawnEnteringDoor))]
		static class FogGrid_Notify_PawnEnteringDoor_Patch
		{
			static void Prefix(Building_Door door, Pawn pawn)
			{
				if (door == null || pawn == null)
					return;
				if (pawn.Faction != Faction.OfPlayer && pawn.HostFaction != Faction.OfPlayer)
					return;

				var pos = door.Position;
				var map = door.Map;
				if (map == null)
					return;

				foreach (var offset in GenAdj.AdjacentCells)
				{
					var cell = pos + offset;
					if (cell.InBounds(map))
						Tools.SpawnZombiesInRoom(map, cell);
				}
			}
		}

		// add job to turn on zombie shocker
		// add roping job
		//
		[HarmonyPatch(typeof(FloatMenuContext))]
		[HarmonyPatch(MethodType.Constructor)]
		[HarmonyPatch(new[] { typeof(List<Pawn>), typeof(Vector3), typeof(Map) })]
		static class FloatMenuContext_Constructor_Patch
		{
			static void Postfix(List<Thing> ___cachedClickedThings, List<Pawn> ___cachedClickedPawns)
			{
				___cachedClickedThings?.RemoveAll(thing => thing is ZombieSymbiant);
				___cachedClickedPawns?.RemoveAll(pawn => pawn is ZombieSymbiant);
			}
		}

		[HarmonyPatch(typeof(FloatMenuMakerMap))]
		[HarmonyPatch(nameof(FloatMenuMakerMap.GetOptions))]
		static class FloatMenuMakerMap_GetOptions_Patch
		{
			public static readonly string zapZombiesLabel = "ZapZombies".Translate();
			public static readonly string ropeZombieLabel = "RopeZombie".Translate();

			static void Postfix(Vector3 clickPos, List<Pawn> selectedPawns, List<FloatMenuOption> __result)
			{
				if (__result == null || selectedPawns == null || selectedPawns.Count != 1)
					return;
				var pawn = selectedPawns[0];
				if (pawn == null)
					return;
				var map = pawn.Map;
				if (map == null)
					return;
				var clickCell = IntVec3.FromVector3(clickPos);
				if (clickCell.InBounds(map) == false)
					return;

				var opts = __result;
				var shocker = map.thingGrid.ThingAt<ZombieShocker>(clickCell);
				if (shocker != null
					&& pawn.CanReach(shocker, PathEndMode.ClosestTouch, Danger.Deadly, false, false, TraverseMode.ByPawn)
					&& pawn.CanReserve(shocker)
					&& shocker.compPowerTrader?.PowerOn == true
					&& shocker.HasValidRoom())
				{
					void job()
					{
						var job = JobMaker.MakeJob(CustomDefs.ZapZombies, shocker);
						_ = pawn.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
					}
					opts.Add(new FloatMenuOption(zapZombiesLabel, job));
				}

				var ropableZombie = map.GetComponent<TickManager>()?.GetRopableZombie(clickPos);
				if (ropableZombie != null)
				{
					void job()
					{
						var job = JobMaker.MakeJob(CustomDefs.RopeZombie, ropableZombie);
						if (pawn.drafter != null)
							pawn.drafter.Drafted = true;
						_ = pawn.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
					}
					opts.Add(new FloatMenuOption(ropeZombieLabel, job));
				}

				AddSymbiantFeedOptions(clickCell, pawn, opts);
			}

			static void AddSymbiantFeedOptions(IntVec3 clickCell, Pawn pawn, List<FloatMenuOption> opts)
			{
				if (pawn?.Map == null || clickCell.InBounds(pawn.Map) == false)
					return;
				if (ZombieSymbiant.IsSymbiantCell(pawn.Map, clickCell, out var symbiant) == false || symbiant == null)
					return;
				if (pawn.CanReach(symbiant, PathEndMode.Touch, pawn.NormalMaxDanger()) == false || pawn.CanReserve(symbiant) == false)
					return;

				var feedOptions = SymbiantFeedOptions(pawn, symbiant).ToArray();
				if (feedOptions.Length == 0)
					return;
				foreach (var feed in feedOptions)
				{
					var label = SymbiantFeedLabel(feed);
					opts.Add(new FloatMenuOption(label, () =>
					{
						var job = JobMaker.MakeJob(CustomDefs.FeedZombieSymbiant, symbiant, feed);
						job.count = 1;
						_ = pawn.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
					}));
				}
			}

			static IEnumerable<Thing> SymbiantFeedOptions(Pawn pawn, ZombieSymbiant symbiant)
			{
				bool Valid(Thing thing)
				{
					return thing is Corpse
						&& thing.DestroyedOrNull() == false
						&& thing.Spawned
						&& thing.IsForbidden(pawn) == false
						&& symbiant.CanAcceptFeed(thing)
						&& pawn.CanReserve(thing)
						&& pawn.CanReach(thing, PathEndMode.Touch, pawn.NormalMaxDanger());
				}

				var seen = new HashSet<string>();
				foreach (var feed in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)
					.Where(Valid)
					.OrderBy(thing => thing.Position.DistanceToSquared(pawn.Position) + thing.Position.DistanceToSquared(symbiant.Position)))
				{
					var category = SymbiantFeedCategory(feed);
					if (seen.Add(category))
						yield return feed;
					if (seen.Count >= 4)
						yield break;
				}
			}

			static string SymbiantFeedCategory(Thing feed)
			{
				var corpse = feed as Corpse;
				var humanlike = corpse?.InnerPawn?.RaceProps?.Humanlike == true;
				var fresh = corpse?.GetRotStage() == RotStage.Fresh;
				return $"{(humanlike ? "human" : "animal")}_{(fresh ? "fresh" : "old")}";
			}

			static string SymbiantFeedLabel(Thing feed)
			{
				var corpse = feed as Corpse;
				var freshness = corpse?.GetRotStage() == RotStage.Fresh ? "fresh" : "rotten";
				var name = corpse?.InnerPawn?.LabelShortCap ?? feed.LabelShortCap;
				var cells = ZombieSymbiant.FeedGrowthCellCount(feed);
				return "FeedZombieSymbiantFloatMenu".Translate(freshness, name, cells);
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
				var m_IsZombielandPawn = SymbolExtensions.GetMethodInfo(() => IsZombielandPawn(null));
				var list = instructions.ToList();
				var endLabel = generator.DefineLabel();
				var retIndex = list.FindLastIndex(instruction => instruction.opcode == OpCodes.Ret);
				if (retIndex < 0 || f_mode == null || f_pawn == null || m_IsZombielandPawn == null)
				{
					Log.Warning($"{nameof(Pawn_IdeoTracker_ExposeData_Patch)} could not install the Zombieland post-load guard");
					return list;
				}
				list[retIndex].labels.Add(endLabel);

				var modeIndex = -1;
				for (var i = 0; i < list.Count - 1; i++)
					if (list[i].LoadsField(f_mode) && list[i + 1].LoadsConstant((int)LoadSaveMode.PostLoadInit))
					{
						modeIndex = i;
						break;
					}
				if (modeIndex < 0)
				{
					Log.Warning($"{nameof(Pawn_IdeoTracker_ExposeData_Patch)} could not find the PostLoadInit mode check");
					return list;
				}

				var loadThis = new CodeInstruction(OpCodes.Ldarg_0);
				loadThis.labels.AddRange(list[modeIndex].labels);
				list[modeIndex].labels.Clear();
				list.InsertRange(modeIndex, new[]
				{
					loadThis,
					new CodeInstruction(OpCodes.Ldfld, f_pawn),
					new CodeInstruction(OpCodes.Call, m_IsZombielandPawn),
					new CodeInstruction(OpCodes.Brtrue, endLabel)
				});

				return list;
			}
		}
	}
}
