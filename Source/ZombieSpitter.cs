using RimWorld;
using RimWorld.Planet;
using System;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	public class ZombieSpitter : Pawn
	{
		static Mesh mesh = null;

		public SpitterState state = SpitterState.Idle;
		public int idleCounter = 0;
		public bool firstShot = true;
		public bool aggressive = false;
		public int moveState = -1;
		public int tickCounter = 0;
		public int spitInterval = 0;
		public int waves = 0;
		public int remainingZombies = 0;
		public float colonyDurabilityFactor = 1f;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref state, "state", SpitterState.Idle);
			Scribe_Values.Look(ref idleCounter, "idleCounter", 0);
			Scribe_Values.Look(ref firstShot, "firstShot", true);
			Scribe_Values.Look(ref aggressive, "aggressive", false);
			Scribe_Values.Look(ref moveState, "moveState", -1);
			Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
			Scribe_Values.Look(ref spitInterval, "spitInterval", 0);
			Scribe_Values.Look(ref waves, "waves", 0);
			Scribe_Values.Look(ref remainingZombies, "remainingZombies", 0);
			Scribe_Values.Look(ref colonyDurabilityFactor, "colonyDurabilityFactor", 1f);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
				colonyDurabilityFactor = NormalizeColonyDurabilityFactor(colonyDurabilityFactor);
		}

		static float MinimumColonyDurabilityFactor()
		{
			return GenMath.LerpDoubleClamped(0f, 5f, 0.35f, 0.75f, ZombieLand.Tools.Difficulty());
		}

		static float NormalizeColonyDurabilityFactor(float value)
		{
			if (float.IsNaN(value) || value <= 0f)
				return 1f;
			return Mathf.Clamp(value, 0.1f, 1f);
		}

		public static float CalculateColonyDurabilityFactor(Map map)
		{
			if (map?.mapPawns == null)
				return 1f;
			var freeColonists = map.mapPawns.FreeColonists.Count;
			var colonyPoints = ZombieLand.Tools.ColonyPoints(map);
			return CalculateColonyDurabilityFactor(freeColonists, colonyPoints[0], colonyPoints[1] + colonyPoints[2]);
		}

		public static float CalculateColonyDurabilityFactor(int freeColonists, float colonistPoints, float supportPoints)
		{
			if (freeColonists <= 0)
				return 1f;
			if (colonistPoints <= 0f)
				return MinimumColonyDurabilityFactor();

			var colonistBaseline = Mathf.Max(150f, colonistPoints);
			var weakSupport = colonistBaseline * 0.08f;
			var adequateSupport = colonistBaseline * 0.30f;
			var readiness = Mathf.InverseLerp(weakSupport, adequateSupport, Mathf.Max(0f, supportPoints));
			return Mathf.Lerp(MinimumColonyDurabilityFactor(), 1f, readiness);
		}

		public void ApplySpitterDamageScaling(ref DamageInfo dinfo)
		{
			var damageFactor = 6f - ZombieSettings.Values.spitterThreat;
			if (dinfo.Def.isRanged == false)
				dinfo.SetAmount(dinfo.Amount * damageFactor);
			else
				dinfo.SetAmount(dinfo.Amount / damageFactor);

			dinfo.SetAmount(dinfo.Amount / NormalizeColonyDurabilityFactor(colonyDurabilityFactor));
		}

		public static void Spawn(Map map, IntVec3? location = null)
		{
			if (location.HasValue == false)
			{
				var (xMax, zMax) = (map.Size.x - 1, map.Size.z - 1);
				var roofGrid = map.roofGrid;

				var newLocation = ZombieLand.Tools
					.PlayerReachableRegions(map)
					.SelectMany(r => r.Cells)
					.Where(c => c.x == 0 || c.z == 0 || c.x == xMax || c.z == zMax)
					.Where(c => c.Standable(map))
					.Where(c => roofGrid.Roofed(c) == false && c.Fogged(map) == false)
					.Where(c => RCellFinder.FindSiegePositionFrom(c, map, false, false).IsValid)
					.SafeRandomElement(IntVec3.Invalid);
				if (newLocation.IsValid)
					location = newLocation;
			}

			if (location.HasValue == false)
				return;

			var cell = location.Value;

			var spitter = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSpitter, null) as ZombieSpitter;
			spitter.SetFactionDirect(Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies));
			GenSpawn.Spawn(spitter, cell, map, Rot4.Random, WipeMode.Vanish, false);
			spitter.colonyDurabilityFactor = CalculateColonyDurabilityFactor(map);

			var f = ZombieSettings.Values.spitterThreat;
			spitter.aggressive = ShipCountdown.CountingDown || Rand.Chance(f / 2f);
			spitter.waves = Mathf.FloorToInt(spitter.aggressive ? ZombieLand.Tools.SpitterRandRange(1, 2, 4, 10) : ZombieLand.Tools.SpitterRandRange(2, 15, 4, 30));
			if (spitter.waves < 1)
				spitter.waves = 1;
			spitter.idleCounter = 0;
			spitter.firstShot = true;

			spitter.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Spitter));

			if (ZombieAwarenessCues.ShouldShowZombieEventLetter())
			{
				var headline = "LetterLabelZombiesSpitter".Translate();
				var text = "ZombiesSpitter".Translate();
				Find.LetterStack.ReceiveLetter(headline, text, LetterDefOf.ThreatSmall, new GlobalTargetInfo(cell, map));
			}

			if (ZombieAwarenessCues.ShouldPlayZombieEventSiren())
				CustomDefs.ZombiesRising.PlayOneShotOnCamera(null);
		}

		public bool StartLeavingMap()
		{
			if (Destroyed || Spawned == false)
				return false;
			if (state == SpitterState.Leaving)
				return true;

			if (jobs?.curJob?.def != CustomDefs.Spitter)
				jobs?.StartJob(JobMaker.MakeJob(CustomDefs.Spitter));

			if (RCellFinder.TryFindBestExitSpot(this, out var exitCell, TraverseMode.ByPawn, false) == false)
				return false;

			remainingZombies = 0;
			waves = 0;
			tickCounter = 0;
			pather?.StartPath(exitCell, PathEndMode.OnCell);
			state = SpitterState.Leaving;
			return true;
		}

		public override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			mesh ??= MeshMakerPlanes.NewPlaneMesh(3f);
			var v = new Vector3(0.1f, 0f, 0f) * Mathf.Sin(2 * Mathf.PI * (Find.TickManager.TicksGame % 60) / 60f);
			var h = new Vector3(0f, 0.01f, 0f);
			var materials = aggressive ? Constants.SpitterAggressive : Constants.Spitter;
			Graphics.DrawMesh(mesh, drawLoc + v, Quaternion.identity, materials[0], 0);
			Graphics.DrawMesh(mesh, drawLoc + h, Quaternion.identity, materials[1], 0);
			Graphics.DrawMesh(mesh, drawLoc - v + h + h, Quaternion.identity, materials[2], 0);
		}

		public override string GetInspectString()
		{
			var result = new StringBuilder();
			var spitter = jobs.curDriver as JobDriver_Spitter;
			result.Append("Mode".Translate()).Append(": ").AppendLine(aggressive ? "Aggressive".Translate() : "Calm".Translate());
			if (waves > 0)
				result.Append("Waves".Translate()).Append(": ").Append(waves).Append(", ");
			result.AppendLine(("SpitterState" + Enum.GetName(typeof(SpitterState), state)).Translate());
			return result.ToString().TrimEndNewlines();
		}
	}
}
