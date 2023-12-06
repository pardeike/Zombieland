using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class ZombieBlob : Pawn
	{
		Mesh mesh = null;
		List<IntVec3> cells = null;
		ZombieBlobRenderer renderer = null;

		public override void ExposeData()
		{
			base.ExposeData();
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
					.Where(c => ZombieBlobRenderer.ValidPosition(map, c) != null)
					.SafeRandomElement(IntVec3.Invalid);
				if (newLocation.IsValid)
					location = newLocation;
			}

			if (location.HasValue == false)
				return;

			var cell = location.Value;
			var cells = ZombieBlobRenderer.ValidPosition(map, cell);

			var blob = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieBlob, null) as ZombieBlob;
			blob.renderer = new ZombieBlobRenderer(map, cells);

			blob.SetFactionDirect(Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies));
			GenSpawn.Spawn(blob, cell, map, Rot4.Random, WipeMode.Vanish, false);

			blob.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Blob));

			var headline = "LetterLabelZombieBlob".Translate();
			var text = "ZombieBlob".Translate();
			Find.LetterStack.ReceiveLetter(headline, text, LetterDefOf.ThreatSmall, new GlobalTargetInfo(cell, map));

			// if (Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
			//	CustomDefs.ZombiesRising.PlayOneShotOnCamera(null);
		}

		public override void Draw()
		{
			mesh ??= MeshMakerPlanes.NewPlaneMesh(5f);
			_ = cells;
			renderer.Update();
			Graphics.DrawMesh(mesh, DrawPos, Quaternion.identity, renderer.metaballMaterial, 0);
		}

		public override string GetInspectString()
		{
			return "Zombie Blob";
		}
	}
}