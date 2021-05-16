using HarmonyLib;
using RimWorld;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public class Dialog_ZombieDebugActionMenu : Dialog_DebugActionsMenu
	{
		private void SpawnZombie(ZombieType type, bool appearDirectly)
		{
			ZombieGenerator.SpawnZombie(UI.MouseCell(), Find.CurrentMap, type, (zombie) =>
			{
				if (appearDirectly)
				{
					zombie.rubbleCounter = Constants.RUBBLE_AMOUNT;
					zombie.state = ZombieState.Wandering;
				}
				zombie.Rotation = Rot4.South;

				var tickManager = Find.CurrentMap.GetComponent<TickManager>();
				tickManager.allZombiesCached.Add(zombie);
			});
		}

		public override void DoListingItems()
		{
			base.DoListingItems();

			if (Current.ProgramState != ProgramState.Playing)
				return;

			var map = Find.CurrentMap;
			if (map == null)
				return;

			DoGap();
			DoLabel("Tools - ZombieLand");

#pragma warning disable CS0618 // Type or member is obsolete
			DebugToolMap("Spawn: Zombie (dig out)", delegate
			{
				SpawnZombie(ZombieType.Normal, false);
			});
			DebugToolMap("Spawn: Zombie (standing)", delegate
			{
				SpawnZombie(ZombieType.Normal, true);
			});
			DebugToolMap("Spawn: Suicide bomber", delegate
			{
				SpawnZombie(ZombieType.SuicideBomber, true);
			});
			DebugToolMap("Spawn: Toxic Splasher", delegate
			{
				SpawnZombie(ZombieType.ToxicSplasher, true);
			});
			DebugToolMap("Spawn: Tanky Operator", delegate
			{
				SpawnZombie(ZombieType.TankyOperator, true);
			});
			DebugToolMap("Spawn: Miner", delegate
			{
				SpawnZombie(ZombieType.Miner, true);
			});
			DebugToolMap("Spawn: Electrifier", delegate
			{
				SpawnZombie(ZombieType.Electrifier, true);
			});
			DebugToolMap("Spawn: Albino", delegate
			{
				SpawnZombie(ZombieType.Albino, true);
			});
			DebugToolMap("Spawn: Dark Slimer", delegate
			{
				SpawnZombie(ZombieType.DarkSlimer, true);
			});
			DebugToolMap("Spawn: Random zombie", delegate
			{
				SpawnZombie(ZombieType.Random, true);
			});
			DebugToolMap("Spawn: Zombie incident (4)", delegate
			{
				_ = ZombiesRising.TryExecute(map, 4, UI.MouseCell(), true);
			});
			DebugToolMap("Spawn: Zombie incident (25)", delegate
			{
				_ = ZombiesRising.TryExecute(map, 25, UI.MouseCell(), true);
			});
			DebugToolMap("Spawn: Zombie incident (100)", delegate
			{
				_ = ZombiesRising.TryExecute(map, 100, UI.MouseCell(), true);
			});
			DebugToolMap("Spawn: Zombie incident (200)", delegate
			{
				_ = ZombiesRising.TryExecute(map, 200, UI.MouseCell(), true);
			});
			DebugToolMap("Convert: Make Zombie", delegate
			{
				foreach (var thing in map.thingGrid.ThingsAt(UI.MouseCell()))
				{
					if (!(thing is Pawn pawn) || pawn is Zombie) continue;
					Tools.ConvertToZombie(pawn, map, true);
				}
			});
			DebugToolMap("Apply: Trigger rotting", delegate
			{
				foreach (var thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
				{
					var compRottable = thing.TryGetComp<CompRottable>();
					if (compRottable != null)
						compRottable.RotProgress = compRottable.PropsRot.TicksToRotStart;
				}
			});
			DebugToolMap("Apply: Add zombie bite", delegate
			{
				foreach (var thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
				{
					if (!(thing is Pawn pawn) || pawn is Zombie)
						continue;

					var bodyModel = pawn.health.hediffSet;

					var bodyPart = bodyModel
						.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined, null, null)
						.Where(part =>
							part.depth == BodyPartDepth.Outside
							|| part.depth == BodyPartDepth.Inside
							&& part.def.IsSolid(part, bodyModel.hediffs)
						)
						.SafeRandomElement();

					if (bodyPart == null)
						bodyPart = bodyModel.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined).SafeRandomElement();

					var def = HediffDef.Named("ZombieBite");
					var bite = (Hediff_Injury_ZombieBite)HediffMaker.MakeHediff(def, pawn, bodyPart);

					bite.mayBecomeZombieWhenDead = true;
					bite.TendDuration.ZombieInfector.MakeHarmfull();
					var damageInfo = new DamageInfo(Tools.ZombieBiteDamageDef, 2);
					pawn.health.AddHediff(bite, bodyPart, damageInfo);
				}
			});
			DebugToolMap("Apply: Remove infections", delegate
			{
				foreach (var thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
				{
					if (!(thing is Pawn pawn) || pawn is Zombie) continue;
					pawn.health.hediffSet
						.GetHediffs<Hediff_Injury_ZombieBite>()
						.Do(bite =>
						{
							bite.mayBecomeZombieWhenDead = false;
							var tendDuration = bite.TryGetComp<HediffComp_Zombie_TendDuration>();
							tendDuration.ZombieInfector.MakeHarmless();
						});

					_ = pawn.health.hediffSet.hediffs.RemoveAll(hediff => hediff is Hediff_ZombieInfection);
				}
			});
			DebugToolMap("Apply: Zombie raging", delegate
			{
				foreach (var thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
				{
					if (!(thing is Zombie zombie)) continue;
					ZombieStateHandler.StartRage(zombie);
				}
			});
#pragma warning restore CS0618 // Type or member is obsolete
		}
	}
}
