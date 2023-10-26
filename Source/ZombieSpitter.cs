using RimWorld;
using RimWorld.Planet;
using System;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
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

			var f = ZombieSettings.Values.spitterThreat;
			spitter.aggressive = ShipCountdown.CountingDown || Rand.Chance(f / 2f);
			spitter.waves = Mathf.FloorToInt(spitter.aggressive ? ZombieLand.Tools.SpitterRandRange(1, 2, 4, 10) : ZombieLand.Tools.SpitterRandRange(2, 15, 4, 30));
			if (spitter.waves < 1)
				spitter.waves = 1;
			spitter.idleCounter = 0;
			spitter.firstShot = true;

			spitter.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Spitter));

			var headline = "LetterLabelZombiesSpitter".Translate();
			var text = "ZombiesSpitter".Translate();
			Find.LetterStack.ReceiveLetter(headline, text, LetterDefOf.ThreatSmall, new GlobalTargetInfo(cell, map));

			if (Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
				CustomDefs.ZombiesRising.PlayOneShotOnCamera(null);
		}

		public override void Draw()
		{
			mesh ??= MeshMakerPlanes.NewPlaneMesh(3f);
			var v = new Vector3(0.1f, 0f, 0f) * Mathf.Sin(2 * Mathf.PI * Drawer.tweener.MovedPercent());
			var h = new Vector3(0f, 0.01f, 0f);
			var materials = aggressive ? Constants.SpitterAggressive : Constants.Spitter;
			Graphics.DrawMesh(mesh, DrawPos + v, Quaternion.identity, materials[0], 0);
			Graphics.DrawMesh(mesh, DrawPos + h, Quaternion.identity, materials[1], 0);
			Graphics.DrawMesh(mesh, DrawPos - v + h + h, Quaternion.identity, materials[2], 0);
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