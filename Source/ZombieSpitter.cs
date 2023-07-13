using RimWorld;
using RimWorld.Planet;
using System;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	public class ZombieSpitter : Pawn
	{
		static Mesh mesh = null;

		public static void Spawn(Map map, IntVec3? location = null)
		{
			if (location.HasValue == false && RCellFinder.TryFindRandomPawnEntryCell(out var entryCell, map, 0.5f))
				location = entryCell;
			if (location.HasValue == false)
				return;

			var cell = location.Value;

			var spitter = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSpitter, null);
			spitter.SetFactionDirect(Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies));
			GenSpawn.Spawn(spitter, cell, map, Rot4.Random, WipeMode.Vanish, false);
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
			Graphics.DrawMesh(mesh, DrawPos + v, Quaternion.identity, Constants.Spitter[0], 0);
			Graphics.DrawMesh(mesh, DrawPos + h, Quaternion.identity, Constants.Spitter[1], 0);
			Graphics.DrawMesh(mesh, DrawPos - v + h + h, Quaternion.identity, Constants.Spitter[2], 0);
		}

		public override string GetInspectString()
		{
			var result = new StringBuilder();
			var spitter = jobs.curDriver as JobDriver_Spitter;
			result.Append("Mode".Translate()).Append(": ").AppendLine(spitter.aggressive ? "Aggressive".Translate() : "Calm".Translate());
			if (spitter.waves > 0)
				result.Append("Waves".Translate()).Append(": ").Append(spitter.waves).Append(", ");
			result.AppendLine(("SpitterState" + Enum.GetName(typeof(SpitterState), spitter.state)).Translate());
			return result.ToString().TrimEndNewlines();
		}
	}
}