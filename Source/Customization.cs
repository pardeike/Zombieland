using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace ZombieLand
{
	// To support Zombieland in your mod, define a class called ZombielandSupport (no namespace!) in your mod and add the following:

	// public static bool? CanBecomeZombie(Pawn pawn)
	// If defined, this method will be called to determine if a pawn can become a zombie
	// You are supposed to return true if the pawn can become a zombie, false if not and null if you don't care

	// public static bool? AttractsZombies(Pawn pawn)
	// If defined, this method will be called to determine if zombies are attracted to a pawn or ignore the pawn
	// You are supposed to return true if the pawn attracts zombies, false if not and null if you don't care

	public class Customization
	{
		private delegate bool CanBecomeZombie(Pawn pawn);
		private delegate bool AttractsZombies(Pawn pawn);

		private static readonly List<CanBecomeZombie> canBecomeZombieEvaluators = new();
		private static readonly List<AttractsZombies> attractsZombiesEvaluators = new();

		public static void Init()
		{
			MethodInfo method;
			AppDomain.CurrentDomain.GetAssemblies()
				.Select(asm => asm.GetType("ZombielandSupport", false))
				.OfType<Type>()
			.Do(type =>
			{
				method = AccessTools.Method(type, nameof(CanBecomeZombie));
				if (method == null)
					return;
				var canBecomeZombie = (CanBecomeZombie)Delegate.CreateDelegate(typeof(CanBecomeZombie), method, false);
				if (canBecomeZombie != null)
					canBecomeZombieEvaluators.Add(canBecomeZombie);

				method = AccessTools.Method(type, nameof(AttractsZombies));
				if (method == null)
					return;
				var attractsZombies = (AttractsZombies)Delegate.CreateDelegate(typeof(AttractsZombies), method, false);
				if (attractsZombies != null)
					attractsZombiesEvaluators.Add(attractsZombies);
			});
		}
		
		public static bool CannotBecomeZombie(Pawn pawn)
		{
			return canBecomeZombieEvaluators.Count > 0 && canBecomeZombieEvaluators.Any(evaluator => evaluator(pawn) == false);
		}

		public static bool DoesAttractsZombies(Pawn pawn)
		{
			if (pawn == null) return false;
			if (pawn is Zombie) return false;
			if (pawn.Spawned == false) return false;
			if (pawn.Dead) return false;
			if (pawn.health.Downed) return false;
			if (pawn.RaceProps.IsFlesh == false) return false;
			if (AlienTools.IsFleshPawn(pawn) == false) return false;
			if (SoSTools.IsHologram(pawn)) return false;
			if (pawn.InfectionState() >= InfectionState.Infecting) return false;
			if (attractsZombiesEvaluators.Count > 0 && attractsZombiesEvaluators.Any(evaluator => evaluator(pawn) == false)) return false;
			return ZombieSettings.Values.attackMode switch
			{
				AttackMode.Everything => true,
				AttackMode.OnlyHumans => pawn.RaceProps.Humanlike,
				AttackMode.OnlyColonists => pawn.RaceProps.Humanlike && pawn.IsColonist,
				_ => false,
			};
		}
	}
}