using HarmonyLib;
using System;
using System.Collections.Generic;
using Verse;

namespace ZombieLand
{
	public class Debouncer
	{
		private Debouncer() { }
		private readonly int tickThrottle;
		private readonly bool onlyWhenRunning;
		private int nextExecution;
		private Action delayedAction;

		private static readonly List<Debouncer> debouncers = new();
		private static int gameTicks;
		private static Harmony harmony;
		private const string harmonyID = "brrainz.debouncer";

		public Debouncer(int tickThrottle, bool onlyWhenRunning)
		{
			this.tickThrottle = tickThrottle;
			this.onlyWhenRunning = onlyWhenRunning;
			if (harmony == null)
			{
				var m_UpdatePlay = SymbolExtensions.GetMethodInfo((Game game) => game.UpdatePlay());
				var m_Tick = SymbolExtensions.GetMethodInfo(() => Tick());
				harmony = new Harmony(harmonyID);
				harmony.Patch(m_UpdatePlay, postfix: new HarmonyMethod(m_Tick) { priority = Priority.VeryLow });
			}
			debouncers.Add(this);
		}

		private static void Tick()
		{
			gameTicks++;
			var debouncerCount = debouncers.Count;
			var running = Find.TickManager.Paused == false;
			for (var i = 0; i < debouncerCount; i++)
			{
				var debouncer = debouncers[i];
				if (gameTicks >= debouncer.nextExecution && (debouncer.onlyWhenRunning == false || running))
				{
					var action = debouncer.delayedAction;
					if (action != null)
					{
						debouncer.nextExecution = gameTicks + debouncer.tickThrottle;
						debouncer.delayedAction = null;
						action();
						return;
					}
				}
			}
		}

		public void Remove() => debouncers.Remove(this);

		public void Run(Action action)
		{
			if (delayedAction == null && tickThrottle > 0 && gameTicks >= nextExecution)
			{
				nextExecution = gameTicks + tickThrottle;
				delayedAction = null;
				action();
				return;
			}
			delayedAction = action;
		}
	}
}