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
		const int ForecastHorizonTicks = GenDate.TicksPerQuadrum * 4 + GenDate.TicksPerDay * 2;
		const int ExpiredWindowKeepTicks = GenDate.TicksPerDay;

		public const float EventZeroThreatDeathChance = 0.01f;

		List<ZombieFreeEventWindow> windows = new();
		int nextClusterStartTick = -1;

		public ZombieFreeEventManager(World world) : base(world)
		{
		}

		public static ZombieFreeEventManager Current => Find.World?.GetComponent<ZombieFreeEventManager>();

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
			return Current?.IsActiveAtGameTick(GenTicks.TicksGame) == true;
		}

		public static bool IsActiveAtAbsTick(int absTick)
		{
			return Current?.IsActiveAtGameTick(GameTickForAbsTick(absTick)) == true;
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
			EnsureScheduleThrough(gameTick);
			return ActiveWindowAt(gameTick) != null;
		}

		public List<ZombieFreeEventWindow> WindowsForGameRange(int gameStartTick, int gameEndTick)
		{
			EnsureScheduleThrough(gameEndTick);
			return windows
				.Where(window => window.Overlaps(gameStartTick, gameEndTick))
				.OrderBy(window => window.startTick)
				.ToList();
		}

		public ZombieFreeEventWindow ActiveWindowAt(int gameTick)
		{
			return windows.FirstOrDefault(window => window.ActiveAt(gameTick));
		}

		public ZombieFreeEventWindow DebugForceWindowStartingNow(int durationTicks)
		{
			var ticks = GenTicks.TicksGame;
			var window = new ZombieFreeEventWindow(ticks, ticks + Mathf.Max(MinEventDurationTicks, durationTicks));
			windows.Add(window);
			windows = windows.OrderBy(item => item.startTick).ToList();
			nextClusterStartTick = Mathf.Max(nextClusterStartTick, window.endTick + ClusterPeriodTicks());
			StartWindow(window, ticks);
			return window;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Collections.Look(ref windows, "zombieFreeEventWindows", LookMode.Deep);
			Scribe_Values.Look(ref nextClusterStartTick, "nextZombieFreeEventClusterStartTick", -1);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				windows ??= new List<ZombieFreeEventWindow>();
				windows.RemoveAll(window => window == null || window.endTick <= window.startTick);
				windows = windows.OrderBy(window => window.startTick).ToList();
			}
		}

		public override void WorldComponentTick()
		{
			base.WorldComponentTick();
			if (Verse.Current.Game == null || Verse.Current.ProgramState != ProgramState.Playing)
				return;

			var ticks = GenTicks.TicksGame;
			EnsureScheduleThrough(ticks + ForecastHorizonTicks);
			CleanupExpiredWindows(ticks);

			var activeWindow = ActiveWindowAt(ticks);
			if (activeWindow == null)
				return;

			if (activeWindow.startHandled == false)
				StartWindow(activeWindow, ticks);
			EnsureGameCondition(activeWindow, ticks);
		}

		void StartWindow(ZombieFreeEventWindow window, int ticks)
		{
			window.startHandled = true;
			StartSpittersLeavingAllMaps();
			EnsureGameCondition(window, ticks);
			SendStartLetter(window);
		}

		void EnsureGameCondition(ZombieFreeEventWindow window, int ticks)
		{
			var manager = Find.World?.gameConditionManager;
			if (manager == null || CustomDefs.ZombieFreeEvent == null)
				return;

			var existing = manager.GetActiveCondition(CustomDefs.ZombieFreeEvent);
			if (existing != null)
			{
				var remaining = Mathf.Max(GenDate.TicksPerHour, window.endTick - ticks);
				if (existing.TicksLeft < remaining)
					existing.TicksLeft = remaining;
				return;
			}

			var duration = Mathf.Max(GenDate.TicksPerHour, window.endTick - ticks);
			var condition = GameConditionMaker.MakeCondition(CustomDefs.ZombieFreeEvent, duration);
			manager.RegisterCondition(condition);
		}

		void SendStartLetter(ZombieFreeEventWindow window)
		{
			if (window.letterSent)
				return;
			window.letterSent = true;
			if (ZombieAwarenessCues.ShouldShowZombieEventLetter() == false || Find.LetterStack == null)
				return;

			var label = "LetterLabelZombieFreeEvent".Translate();
			var text = "ZombieFreeEventLetter".Translate(window.DurationTicks.ToStringTicksToPeriod());
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

		void EnsureScheduleThrough(int gameTick)
		{
			windows ??= new List<ZombieFreeEventWindow>();
			if (nextClusterStartTick <= 0)
				nextClusterStartTick = InitialClusterStartTick();

			var lastEndTick = windows.Count == 0 ? 0 : windows.Max(window => window.endTick);
			while (lastEndTick < gameTick || nextClusterStartTick < gameTick)
			{
				AddCluster(nextClusterStartTick);
				lastEndTick = windows.Max(window => window.endTick);
				nextClusterStartTick += Mathf.RoundToInt(ClusterPeriodTicks() * Rand.Range(0.9f, 1.1f));
			}
		}

		int InitialClusterStartTick()
		{
			var ticks = GenTicks.TicksGame;
			var graceEnd = Mathf.CeilToInt(ZombieSettings.Values.daysBeforeZombiesCome * GenDate.TicksPerDay);
			var startFloor = Mathf.Max(ticks, graceEnd);
			var period = ClusterPeriodTicks();
			return startFloor + Mathf.RoundToInt(period * Rand.Range(0.25f, 0.75f));
		}

		void AddCluster(int clusterStartTick)
		{
			var period = ClusterPeriodTicks();
			var durationA = EventDurationTicks();
			AddWindow(clusterStartTick, durationA);

			var durationB = EventDurationTicks();
			var preferredB = clusterStartTick + Mathf.RoundToInt(period * Rand.Range(0.38f, 0.45f));
			var earliestB = clusterStartTick + durationA + GenDate.TicksPerDay * 2;
			var latestB = clusterStartTick + period - durationB - GenDate.TicksPerDay;
			var startB = Mathf.Clamp(Mathf.Max(preferredB, earliestB), earliestB, Mathf.Max(earliestB, latestB));
			AddWindow(startB, durationB);

			windows = windows.OrderBy(window => window.startTick).ToList();
		}

		void AddWindow(int startTick, int durationTicks)
		{
			var endTick = startTick + Mathf.Max(MinEventDurationTicks, durationTicks);
			if (windows.Any(window => window.startTick == startTick && window.endTick == endTick))
				return;
			windows.Add(new ZombieFreeEventWindow(startTick, endTick));
		}

		static float DifficultyFactor()
		{
			return Mathf.InverseLerp(1f, 5f, Mathf.Clamp(Tools.Difficulty(), 1f, 5f));
		}

		static int ClusterPeriodTicks()
		{
			return Mathf.RoundToInt(Mathf.Lerp(30f, 60f, DifficultyFactor()) * GenDate.TicksPerDay);
		}

		static int EventDurationTicks()
		{
			var factor = DifficultyFactor();
			var meanDays = Mathf.Lerp(8f, 2f, factor);
			var jitterDays = Mathf.Lerp(2f, 1f, factor);
			var days = meanDays + Rand.Range(-jitterDays, jitterDays);
			return Mathf.Max(MinEventDurationTicks, Mathf.RoundToInt(days * GenDate.TicksPerDay));
		}
	}
}
