using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public sealed class ZombieFreeEventWindow : IExposable
	{
		public int startTick;
		public int endTick;
		public bool startHandled;
		public bool letterSent;

		public ZombieFreeEventWindow()
		{
		}

		public ZombieFreeEventWindow(int startTick, int endTick)
		{
			this.startTick = startTick;
			this.endTick = endTick;
		}

		public int DurationTicks => Mathf.Max(0, endTick - startTick);

		public bool ActiveAt(int tick)
		{
			return tick >= startTick && tick < endTick;
		}

		public bool Overlaps(int start, int end)
		{
			return startTick < end && endTick > start;
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref startTick, "startTick");
			Scribe_Values.Look(ref endTick, "endTick");
			Scribe_Values.Look(ref startHandled, "startHandled");
			Scribe_Values.Look(ref letterSent, "letterSent");
		}
	}

	public sealed class ZombieFreeEventManager : WorldComponent
	{
		const int MinEventDurationTicks = GenDate.TicksPerDay;
		const int MinEventGapTicks = GenDate.TicksPerDay * 2;
		const int ForecastHorizonTicks = GenDate.TicksPerQuadrum * 4 + GenDate.TicksPerDay * 2;
		const int ExpiredWindowKeepTicks = GenDate.TicksPerDay;
		const float MinClusterPeriodDays = 45f;
		const float MaxClusterPeriodDays = 60f;
		const float MaxEventOffsetFractionOfPeriod = 1f / 6f;
		const float EventDurationReductionDays = 1.5f;

		public const float EventZeroThreatDeathChance = 0.01f;

		List<ZombieFreeEventWindow> windows = new();
		int nextClusterStartTick = -1;

		public ZombieFreeEventManager(World world) : base(world)
		{
		}

		public static ZombieFreeEventManager Current => Find.World?.GetComponent<ZombieFreeEventManager>();

		public static bool IsEnabled()
		{
			return IsEnabledAtGameTick(GenTicks.TicksGame);
		}

		public static bool IsEnabledAtGameTick(int gameTick)
		{
			return ZombieSettings.ZombieFreeEventsAtGameTick(gameTick);
		}

		public static int InitialGraceEndTick()
		{
			var days = Mathf.Max(0, ZombieSettings.ThreatSettingsAtGameTick(0).daysBeforeZombiesCome);
			return Mathf.CeilToInt(days * GenDate.TicksPerDay);
		}

		public static bool IsInitialGraceActiveNow()
		{
			return IsInitialGraceActiveAtGameTick(GenTicks.TicksGame);
		}

		public static bool IsInitialGraceActiveAtGameTick(int gameTick)
		{
			return gameTick >= 0 && gameTick < InitialGraceEndTick();
		}

		public static int GameTickForAbsTick(int absTick)
		{
			return absTick - GenTicks.TicksAbs + GenTicks.TicksGame;
		}

		public static int AbsTickForGameTick(int gameTick)
		{
			return gameTick - GenTicks.TicksGame + GenTicks.TicksAbs;
		}

		public static bool IsActiveNow()
		{
			var gameTick = GenTicks.TicksGame;
			return IsInitialGraceActiveAtGameTick(gameTick)
				|| Current?.IsEnabledSilenceActiveAtGameTick(gameTick) == true;
		}

		public static bool IsActiveAtAbsTick(int absTick)
		{
			var gameTick = GameTickForAbsTick(absTick);
			return IsInitialGraceActiveAtGameTick(gameTick)
				|| Current?.IsEnabledSilenceActiveAtGameTick(gameTick) == true;
		}

		public static bool IsEnabledSilenceActiveNow()
		{
			return Current?.IsEnabledSilenceActiveAtGameTick(GenTicks.TicksGame) == true;
		}

		public static bool IsEnabledSilenceActiveAtAbsTick(int absTick)
		{
			return Current?.IsEnabledSilenceActiveAtGameTick(GameTickForAbsTick(absTick)) == true;
		}

		public static List<ZombieFreeEventWindow> WindowsForAbsRange(int absStartTick, int absEndTick)
		{
			var manager = Current;
			if (manager == null)
				return new List<ZombieFreeEventWindow>();
			var gameStartTick = GameTickForAbsTick(absStartTick);
			var gameEndTick = GameTickForAbsTick(absEndTick);
			return manager.WindowsForGameRange(gameStartTick, gameEndTick);
		}

		public bool IsActiveAtGameTick(int gameTick)
		{
			return IsInitialGraceActiveAtGameTick(gameTick) || IsEnabledSilenceActiveAtGameTick(gameTick);
		}

		public bool IsEnabledSilenceActiveAtGameTick(int gameTick)
		{
			if (IsEnabledAtGameTick(gameTick) == false)
				return false;
			EnsureScheduleThrough(gameTick);
			return ActiveWindowAt(gameTick) != null;
		}

		public List<ZombieFreeEventWindow> WindowsForGameRange(int gameStartTick, int gameEndTick)
		{
			EnsureScheduleThrough(gameEndTick);
			return windows
				.Where(window => window.Overlaps(gameStartTick, gameEndTick))
				.SelectMany(window => EnabledSegmentsForWindow(window, gameStartTick, gameEndTick))
				.OrderBy(window => window.startTick)
				.ToList();
		}

		public ZombieFreeEventWindow ActiveWindowAt(int gameTick)
		{
			return ActiveWindowsAt(gameTick).FirstOrDefault();
		}

		public ZombieFreeEventWindow DebugForceWindowStartingNow(int durationTicks)
		{
			var ticks = GenTicks.TicksGame;
			var activeWindow = ActiveWindowAt(ticks);
			if (activeWindow == null)
				activeWindow = AddWindowStartingAt(ticks, durationTicks);

			nextClusterStartTick = Mathf.Max(nextClusterStartTick, activeWindow.endTick + ClusterPeriodTicksFor(DifficultyAtGameTick(activeWindow.endTick)));
			if (activeWindow.ActiveAt(ticks))
				StartWindows(ActiveWindowsAt(ticks), ticks);
			return activeWindow;
		}

		public void DebugClearSchedule()
		{
			windows = new List<ZombieFreeEventWindow>();
			nextClusterStartTick = -1;
			StopGameCondition();
		}

		public void DebugRefreshCurrentWindowState()
		{
			var ticks = GenTicks.TicksGame;
			if (IsEnabledAtGameTick(ticks) == false)
			{
				StopGameCondition();
				return;
			}

			var activeWindows = ActiveWindowsAt(ticks);
			if (activeWindows.Count == 0)
			{
				StopGameCondition();
				return;
			}

			if (activeWindows.Any(window => window.startHandled == false))
				StartWindows(activeWindows, ticks);
			else
				EnsureGameCondition(activeWindows, ticks);
		}

		public void DebugRebuildScheduleThrough(int gameTick, int seed)
		{
			windows = new List<ZombieFreeEventWindow>();
			nextClusterStartTick = -1;
			Rand.PushState(seed);
			try
			{
				EnsureScheduleThrough(gameTick);
			}
			finally
			{
				Rand.PopState();
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Collections.Look(ref windows, "zombieFreeEventWindows", LookMode.Deep);
			Scribe_Values.Look(ref nextClusterStartTick, "nextZombieFreeEventClusterStartTick", -1);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				windows ??= new List<ZombieFreeEventWindow>();
				SanitizeWindows(windows);
			}
		}

		public override void WorldComponentTick()
		{
			base.WorldComponentTick();
			if (Verse.Current.Game == null || Verse.Current.ProgramState != ProgramState.Playing)
				return;

			var ticks = GenTicks.TicksGame;
			if (IsEnabledAtGameTick(ticks) == false)
			{
				StopGameCondition();
				return;
			}

			EnsureScheduleThrough(ticks + ForecastHorizonTicks);
			CleanupExpiredWindows(ticks);

			var activeWindows = ActiveWindowsAt(ticks);
			if (activeWindows.Count == 0)
				return;

			if (activeWindows.Any(window => window.startHandled == false))
				StartWindows(activeWindows, ticks);
			else
				EnsureGameCondition(activeWindows, ticks);
		}

		void StartWindows(List<ZombieFreeEventWindow> activeWindows, int ticks)
		{
			if (activeWindows.NullOrEmpty())
				return;

			for (var i = 0; i < activeWindows.Count; i++)
				activeWindows[i].startHandled = true;
			StartSpittersLeavingAllMaps();
			EnsureGameCondition(activeWindows, ticks);
			SendStartLetter(activeWindows);
		}

		void EnsureGameCondition(List<ZombieFreeEventWindow> activeWindows, int ticks)
		{
			if (activeWindows.NullOrEmpty())
				return;

			var manager = Find.World?.gameConditionManager;
			if (manager == null || CustomDefs.ZombieFreeEvent == null)
				return;

			var endTick = activeWindows.Max(window => window.endTick);
			var existing = manager.GetActiveCondition(CustomDefs.ZombieFreeEvent);
			if (existing != null)
			{
				var remaining = Mathf.Max(GenDate.TicksPerHour, endTick - ticks);
				if (existing.TicksLeft < remaining)
					existing.TicksLeft = remaining;
				return;
			}

			var duration = Mathf.Max(GenDate.TicksPerHour, endTick - ticks);
			var condition = GameConditionMaker.MakeCondition(CustomDefs.ZombieFreeEvent, duration);
			manager.RegisterCondition(condition);
		}

		static void StopGameCondition()
		{
			var manager = Find.World?.gameConditionManager;
			if (manager == null || CustomDefs.ZombieFreeEvent == null)
				return;

			var existing = manager.GetActiveCondition(CustomDefs.ZombieFreeEvent);
			if (existing != null)
				existing.TicksLeft = 0;
		}

		void SendStartLetter(List<ZombieFreeEventWindow> activeWindows)
		{
			if (activeWindows.NullOrEmpty())
				return;

			if (activeWindows.Any(window => window.letterSent))
			{
				for (var i = 0; i < activeWindows.Count; i++)
					activeWindows[i].letterSent = true;
				return;
			}

			for (var i = 0; i < activeWindows.Count; i++)
				activeWindows[i].letterSent = true;

			if (ZombieAwarenessCues.ShouldShowZombieEventLetter() == false || Find.LetterStack == null)
				return;

			var label = "LetterLabelZombieFreeEvent".Translate();
			var startTick = activeWindows.Min(window => window.startTick);
			var endTick = activeWindows.Max(window => window.endTick);
			var text = "ZombieFreeEventLetter".Translate((endTick - startTick).ToStringTicksToPeriod());
			var targetMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
			var target = targetMap == null ? LookTargets.Invalid : new LookTargets(targetMap.Center, targetMap);
			Find.LetterStack.ReceiveLetter(label, text, CustomDefs.ZombieFreeEventLetter ?? LetterDefOf.PositiveEvent, target);
		}

		static void StartSpittersLeavingAllMaps()
		{
			var maps = Find.Maps;
			if (maps == null)
				return;
			for (var i = 0; i < maps.Count; i++)
			{
				var spitters = maps[i].mapPawns?.AllPawnsSpawned?
					.OfType<ZombieSpitter>()
					.ToArray();
				if (spitters == null)
					continue;
				for (var j = 0; j < spitters.Length; j++)
					spitters[j].StartLeavingMap();
			}
		}

		void CleanupExpiredWindows(int ticks)
		{
			windows.RemoveAll(window => window.endTick < ticks - ExpiredWindowKeepTicks);
		}

		static IEnumerable<ZombieFreeEventWindow> EnabledSegmentsForWindow(ZombieFreeEventWindow window, int rangeStartTick, int rangeEndTick)
		{
			var startTick = Mathf.Max(rangeStartTick, window.startTick);
			var endTick = Mathf.Min(rangeEndTick, window.endTick);
			if (endTick <= startTick)
				yield break;

			var boundaries = ZombieSettings.ValuesOverTime?
				.Select(keyFrame => keyFrame?.Ticks ?? -1)
				.Where(tick => tick > startTick && tick < endTick)
				.Distinct()
				.OrderBy(tick => tick)
				.ToArray() ?? Array.Empty<int>();

			var segmentStart = startTick;
			for (var i = 0; i <= boundaries.Length; i++)
			{
				var segmentEnd = i < boundaries.Length ? boundaries[i] : endTick;
				if (segmentEnd > segmentStart && IsEnabledAtGameTick(segmentStart))
				{
					yield return new ZombieFreeEventWindow(segmentStart, segmentEnd)
					{
						startHandled = window.startHandled,
						letterSent = window.letterSent
					};
				}
				segmentStart = segmentEnd;
			}
		}

		List<ZombieFreeEventWindow> ActiveWindowsAt(int gameTick)
		{
			SanitizeWindows(windows);
			var activeWindows = windows
				.Where(window => window.ActiveAt(gameTick))
				.ToList();
			if (activeWindows.Count == 0)
				return activeWindows;

			var startTick = activeWindows.Min(window => window.startTick);
			var endTick = activeWindows.Max(window => window.endTick);
			for (var changed = true; changed;)
			{
				changed = false;
				for (var i = 0; i < windows.Count; i++)
				{
					var window = windows[i];
					if (window.startTick >= endTick || window.endTick <= startTick)
						continue;
					if (activeWindows.Contains(window))
						continue;

					activeWindows.Add(window);
					startTick = Mathf.Min(startTick, window.startTick);
					endTick = Mathf.Max(endTick, window.endTick);
					changed = true;
				}
			}

			return activeWindows
				.OrderBy(window => window.startTick)
				.ToList();
		}

		void EnsureScheduleThrough(int gameTick)
		{
			windows ??= new List<ZombieFreeEventWindow>();
			EnsureInitialGraceDisplayWindow();
			if (nextClusterStartTick <= 0)
				nextClusterStartTick = InitialClusterStartTick();

			var lastEndTick = windows.Count == 0 ? 0 : windows.Max(window => window.endTick);
			while (lastEndTick < gameTick || nextClusterStartTick < gameTick)
			{
				var clusterStartTick = nextClusterStartTick;
				AddCluster(clusterStartTick);
				lastEndTick = windows.Max(window => window.endTick);
				nextClusterStartTick += Mathf.RoundToInt(ClusterPeriodTicksFor(DifficultyAtGameTick(clusterStartTick)) * Rand.Range(0.9f, 1.1f));
			}
		}

		void EnsureInitialGraceDisplayWindow()
		{
			var graceEnd = InitialGraceEndTick();
			if (graceEnd <= GenTicks.TicksGame)
				return;
			if (windows.Any(window => window.startTick == 0 && window.endTick == graceEnd))
				return;

			windows.Add(new ZombieFreeEventWindow(0, graceEnd)
			{
				letterSent = true
			});
			SanitizeWindows(windows);
		}

		int InitialClusterStartTick()
		{
			var ticks = GenTicks.TicksGame;
			var graceEnd = InitialGraceEndTick();
			var startFloor = Mathf.Max(ticks, graceEnd);
			var period = ClusterPeriodTicksFor(DifficultyAtGameTick(startFloor));
			return startFloor + Mathf.RoundToInt(period * Rand.Range(0.25f, 0.75f));
		}

		void AddCluster(int clusterStartTick)
		{
			var clusterDifficulty = DifficultyAtGameTick(clusterStartTick);
			var period = ClusterPeriodTicksFor(clusterDifficulty);
			var startA = Mathf.Max(0, clusterStartTick + EventOffsetTicksFor(clusterDifficulty, period));
			var durationA = EventDurationTicksFor(DifficultyAtGameTick(startA));
			var windowA = AddWindow(startA, durationA);

			var preferredB = clusterStartTick
				+ Mathf.RoundToInt(period * Rand.Range(0.38f, 0.45f))
				+ EventOffsetTicksFor(clusterDifficulty, period);
			var durationB = EventDurationTicksFor(DifficultyAtGameTick(preferredB));
			var earliestB = windowA.endTick + MinEventGapTicks;
			var latestB = clusterStartTick + period - durationB - GenDate.TicksPerDay;
			var startB = Mathf.Clamp(Mathf.Max(preferredB, earliestB), earliestB, Mathf.Max(earliestB, latestB));
			AddWindow(startB, durationB);
		}

		ZombieFreeEventWindow AddWindow(int startTick, int durationTicks)
		{
			return AddWindowAvoidingOverlap(windows, startTick, durationTicks);
		}

		ZombieFreeEventWindow AddWindowStartingAt(int startTick, int durationTicks)
		{
			SanitizeWindows(windows);
			var duration = Mathf.Max(MinEventDurationTicks, durationTicks);
			startTick = Mathf.Max(0, startTick);
			var result = new ZombieFreeEventWindow(startTick, startTick + duration);
			windows.Add(result);
			SanitizeWindows(windows);

			var cursor = result.endTick + MinEventGapTicks;
			for (var i = 0; i < windows.Count; i++)
			{
				var window = windows[i];
				if (ReferenceEquals(window, result) || window.endTick <= result.startTick)
					continue;

				var windowDuration = window.DurationTicks;
				if (window.startTick < cursor)
				{
					window.startTick = cursor;
					window.endTick = cursor + windowDuration;
				}
				cursor = window.endTick + MinEventGapTicks;
			}

			SanitizeWindows(windows);
			return result;
		}

		static ZombieFreeEventWindow AddWindowAvoidingOverlap(List<ZombieFreeEventWindow> list, int requestedStartTick, int durationTicks)
		{
			SanitizeWindows(list);
			var duration = Mathf.Max(MinEventDurationTicks, durationTicks);
			var startTick = Mathf.Max(0, requestedStartTick);

			for (var i = 0; i < list.Count; i++)
			{
				var window = list[i];
				if (window.endTick + MinEventGapTicks <= startTick)
					continue;

				if (startTick + duration + MinEventGapTicks <= window.startTick)
					break;

				startTick = window.endTick + MinEventGapTicks;
			}

			var result = new ZombieFreeEventWindow(startTick, startTick + duration);
			list.Add(result);
			SanitizeWindows(list);
			return result;
		}

		static void SanitizeWindows(List<ZombieFreeEventWindow> list)
		{
			if (list == null)
				return;

			list.RemoveAll(window => window == null || window.endTick <= window.startTick);
			list.Sort((a, b) =>
			{
				var result = a.startTick.CompareTo(b.startTick);
				return result != 0 ? result : a.endTick.CompareTo(b.endTick);
			});
		}

		static float DifficultyAtGameTick(int gameTick)
		{
			return ZombieSettings.ThreatScaleAtGameTick(gameTick);
		}

		public static float DifficultyFactorFor(float difficulty)
		{
			return Mathf.InverseLerp(0.5f, 5f, Mathf.Clamp(difficulty, 0.5f, 5f));
		}

		public static float ClusterPeriodDaysFor(float difficulty)
		{
			return Mathf.Lerp(MinClusterPeriodDays, MaxClusterPeriodDays, DifficultyFactorFor(difficulty));
		}

		public static float EventDurationMeanDaysFor(float difficulty)
		{
			return Mathf.Max(1f, Mathf.Lerp(8f, 2f, DifficultyFactorFor(difficulty)) - EventDurationReductionDays);
		}

		public static float EventDurationJitterDaysFor(float difficulty)
		{
			return Mathf.Lerp(2f, 1f, DifficultyFactorFor(difficulty));
		}

		public static float EventOffsetMaxDaysFor(float difficulty)
		{
			return ClusterPeriodDaysFor(difficulty) * MaxEventOffsetFractionOfPeriod * DifficultyFactorFor(difficulty);
		}

		public static int ClusterPeriodTicksFor(float difficulty)
		{
			return Mathf.RoundToInt(ClusterPeriodDaysFor(difficulty) * GenDate.TicksPerDay);
		}

		public static int EventDurationTicksFor(float difficulty)
		{
			var meanDays = EventDurationMeanDaysFor(difficulty);
			var jitterDays = EventDurationJitterDaysFor(difficulty);
			var days = meanDays + Rand.Range(-jitterDays, jitterDays);
			return Mathf.Max(MinEventDurationTicks, Mathf.RoundToInt(days * GenDate.TicksPerDay));
		}

		public static int EventOffsetTicksFor(float difficulty, int periodTicks)
		{
			var maxOffset = periodTicks * MaxEventOffsetFractionOfPeriod * DifficultyFactorFor(difficulty);
			return Mathf.RoundToInt(Rand.Range(-maxOffset, maxOffset));
		}

		public static List<ZombieFreeEventWindow> DebugPreviewWindows(float difficulty, int seed, int horizonTicks, float initialSilenceDays)
		{
			return DebugPreviewWindows(_ => difficulty, seed, horizonTicks, initialSilenceDays);
		}

		public static List<ZombieFreeEventWindow> DebugPreviewWindows(Func<int, float> difficultyAtGameTick, int seed, int horizonTicks, float initialSilenceDays)
		{
			var result = new List<ZombieFreeEventWindow>();
			var initialSilenceTicks = Mathf.CeilToInt(Mathf.Max(0f, initialSilenceDays) * GenDate.TicksPerDay);
			if (initialSilenceTicks > 0)
			{
				result.Add(new ZombieFreeEventWindow(0, initialSilenceTicks)
				{
					letterSent = true
				});
			}

			Rand.PushState(seed);
			try
			{
				var initialDifficulty = difficultyAtGameTick(Mathf.Max(0, initialSilenceTicks));
				var initialPeriod = ClusterPeriodTicksFor(initialDifficulty);
				var nextClusterStartTick = initialSilenceTicks + Mathf.RoundToInt(initialPeriod * Rand.Range(0.25f, 0.75f));
				while (nextClusterStartTick < horizonTicks)
				{
					var clusterDifficulty = difficultyAtGameTick(nextClusterStartTick);
					var period = ClusterPeriodTicksFor(clusterDifficulty);
					AddPreviewCluster(result, difficultyAtGameTick, nextClusterStartTick);
					nextClusterStartTick += Mathf.RoundToInt(period * Rand.Range(0.9f, 1.1f));
				}
			}
			finally
			{
				Rand.PopState();
			}

			SanitizeWindows(result);
			return result
				.Where(window => window.startTick < horizonTicks)
				.OrderBy(window => window.startTick)
				.ToList();
		}

		static void AddPreviewCluster(List<ZombieFreeEventWindow> result, Func<int, float> difficultyAtGameTick, int clusterStartTick)
		{
			var clusterDifficulty = difficultyAtGameTick(clusterStartTick);
			var period = ClusterPeriodTicksFor(clusterDifficulty);
			var startA = Mathf.Max(0, clusterStartTick + EventOffsetTicksFor(clusterDifficulty, period));
			var durationA = EventDurationTicksFor(difficultyAtGameTick(startA));
			var windowA = AddPreviewWindow(result, startA, durationA);

			var preferredB = clusterStartTick
				+ Mathf.RoundToInt(period * Rand.Range(0.38f, 0.45f))
				+ EventOffsetTicksFor(clusterDifficulty, period);
			var durationB = EventDurationTicksFor(difficultyAtGameTick(preferredB));
			var earliestB = windowA.endTick + MinEventGapTicks;
			var latestB = clusterStartTick + period - durationB - GenDate.TicksPerDay;
			var startB = Mathf.Clamp(Mathf.Max(preferredB, earliestB), earliestB, Mathf.Max(earliestB, latestB));
			AddPreviewWindow(result, startB, durationB);
		}

		static ZombieFreeEventWindow AddPreviewWindow(List<ZombieFreeEventWindow> result, int startTick, int durationTicks)
		{
			return AddWindowAvoidingOverlap(result, startTick, durationTicks);
		}
	}
}
