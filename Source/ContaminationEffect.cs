using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class ContaminationRangeAttribute : Attribute
	{
		public float min;
		public float max;

		public ContaminationRangeAttribute(float min, float max)
		{
			this.min = min;
			this.max = max;
		}
	}

	public class ContaminationEffectManager
	{
		public Dictionary<Pawn, ContaminationEffect> pawns = new();
		public void Tick() => pawns.Values.Do(p => p.Tick());
		public void Add(Pawn pawn) => pawns.TryAdd(pawn, new ContaminationEffect(pawn));
		public void Remove(Pawn pawn) => pawns.Remove(pawn);
	}

	public class ContaminationEffect
	{
		static readonly List<(float, float, Func<Pawn, float, bool>)> effects;

		public Pawn pawn;
		public int nextEffectTick;

		static ContaminationEffect()
		{
			effects = new();
			foreach (var method in AccessTools.GetDeclaredMethods(typeof(ContaminationEffect)))
			{
				if (method.GetCustomAttributes(typeof(ContaminationRangeAttribute), false).FirstOrDefault() is not ContaminationRangeAttribute attribute)
					continue;
				var func = (Func<Pawn, float, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, float, bool>), method);
				effects.Add((attribute.min, attribute.max, func));
			}
		}

		void UpdateNextEffectTicks()
		{
			// https://www.desmos.com/calculator/3exjhcupym
			const float a = -0.43f;
			const float b = 2.43f;
			nextEffectTick = Find.TickManager.TicksGame + (int)(GenDate.TicksPerDay * (a * ZombieSettings.Values.contaminationBaseFactor + b));
		}

		public ContaminationEffect(Pawn pawn)
		{
			this.pawn = pawn;
			UpdateNextEffectTicks();
		}

		public void Tick()
		{
			if (Find.TickManager.TicksGame < nextEffectTick)
				return;
			UpdateNextEffectTicks();
			var contamination = pawn.GetContamination();
			var validEffects = effects.Where(e => contamination >= e.Item1 && contamination <= e.Item2).ToHashSet();
			while (validEffects.Count > 0)
			{
				var effect = validEffects.RandomElementByWeight(ef => ef.Item1);
				validEffects.Remove(effect);
				var factor = (effect.Item1 - contamination) / (effect.Item2 - effect.Item1);
				if (effect.Item3(pawn, factor))
					break;
			}
		}

		[ContaminationRange(0.35f, 0.50f)]
		public static bool Hallucination(Pawn pawn, float factor)
		{
			Log.Warning("Hallucination");

			if (pawn.mindState.mentalStateHandler.TryStartMentalState(CustomDefs.ContaminationHallucination) == false)
				return false;
			var interval = GenDate.TicksPerHour / 20;
			var expiryInterval = interval * (int)(1 + factor * 7);
			pawn.mindState.mentalStateHandler.CurState.forceRecoverAfterTicks = expiryInterval;

			var job = JobMaker.MakeJob(CustomDefs.ContaminationJobHallucination);
			job.expiryInterval = expiryInterval;
			pawn.jobs.ClearQueuedJobs();
			pawn.jobs.StartJob(job, JobCondition.Incompletable, null);
			return true;
		}

		[ContaminationRange(0.40f, 0.50f)]
		public static bool Sleepwalk(Pawn pawn, float factor)
		{
			Log.Warning("Sleepwalk");
			_ = pawn;
			_ = factor;
			return false;
		}

		[ContaminationRange(0.45f, 0.60f)]
		public static bool Hoarding(Pawn pawn, float factor)
		{
			Log.Warning("Hoarding");
			_ = pawn;
			_ = factor;
			return false;
		}

		[ContaminationRange(0.50f, 1.00f)]
		public static bool Mimicing(Pawn pawn, float factor)
		{
			Log.Warning("Mimicing");
			_ = pawn;
			_ = factor;
			return false;
		}

		[ContaminationRange(0.60f, 0.80f)]
		public static bool Breakdown(Pawn pawn, float factor)
		{
			Log.Warning("Breakdown");
			_ = pawn;
			_ = factor;
			return false;
		}

		[ContaminationRange(0.65f, 0.85f)]
		public static bool Relocating(Pawn pawn, float factor)
		{
			Log.Warning("Relocating");
			_ = pawn;
			_ = factor;
			return false;
		}

		[ContaminationRange(0.70f, 1.00f)]
		public static bool Biting(Pawn pawn, float factor)
		{
			Log.Warning("Biting");
			_ = pawn;
			_ = factor;
			return false;
		}

		[ContaminationRange(0.75f, 1.00f)]
		public static bool Refusal(Pawn pawn, float factor)
		{
			Log.Warning("Refusal");
			_ = pawn;
			_ = factor;
			return false;
		}

		[ContaminationRange(0.80f, 1.00f)]
		public static bool Pathing(Pawn pawn, float factor)
		{
			Log.Warning("Pathing");
			_ = pawn;
			_ = factor;
			return false;
		}

		[ContaminationRange(0.85f, 1.00f)]
		public static bool Forgetting(Pawn pawn, float factor)
		{
			Log.Warning("Forgetting");
			_ = pawn;
			_ = factor;
			return false;
		}

		[ContaminationRange(0.90f, 1.00f)]
		public static bool Sabotage(Pawn pawn, float factor)
		{
			Log.Warning("Sabotage");
			_ = pawn;
			_ = factor;
			return false;
		}
	}
}
