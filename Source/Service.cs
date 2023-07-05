using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Profile;

namespace ZombieLand
{
	[HarmonyPatch(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))]
	public static class TimeControlService
	{
		private static readonly Dictionary<object, Action<TimeSpeed>> subscribers = new();
		private static TimeSpeed? curTimeSpeed = null;

		static void Postfix()
		{
			var actualTimeSpeed = Find.TickManager.curTimeSpeed;
			if (curTimeSpeed != actualTimeSpeed)
				foreach (var subscriber in subscribers)
					subscriber.Value(actualTimeSpeed);
			curTimeSpeed = actualTimeSpeed;
		}

		public static void Subscribe(object subscriber, Action<TimeSpeed> callback)
		{
			if (subscribers.ContainsKey(subscriber) == false)
				subscribers.Add(subscriber, callback);
		}

		public static void Unsubscribe(object subscriber)
		{
			if (subscribers.ContainsKey(subscriber))
				subscribers.Remove(subscriber);
		}
	}

	[HarmonyPatch(typeof(MemoryUtility), nameof(MemoryUtility.ClearAllMapsAndWorld))]
	public static class ClearMapsService
	{
		private static readonly Dictionary<object, Action> subscribers = new();

		static void Prefix()
		{
			foreach (var subscriber in subscribers.ToArray())
				subscriber.Value();
		}

		public static void Subscribe(object subscriber, Action callback)
		{
			if (subscribers.ContainsKey(subscriber) == false)
				subscribers.Add(subscriber, callback);
		}

		public static void Unsubscribe(object subscriber)
		{
			if (subscribers.ContainsKey(subscriber))
				subscribers.Remove(subscriber);
		}
	}
}