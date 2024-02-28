using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public class ZombieBlob : Pawn
	{
		const int MAX_METABALLS = 64;
		static readonly Color color = new(0, 0.8f, 0);
		static readonly float elementPower = 1f;
		static readonly float elementRadius = 0.011f;
		static readonly float[] elementSizes = [2.5f, 2.4f, 1.6f, 1.2f, 1f, 0.9f, 0.9f, 1f, 1f];
		// static readonly float[] elementSizes = [1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f];

		static readonly Material debugMaterial = SolidColorMaterials.SimpleSolidColorMaterial(Color.red.ToTransparent(0.2f));

		struct Metaball
		{
			public float radius;
			public float size;
			public float power;
			public Vector2 position;
			public Vector2 direction;
			public Vector4 color;
		}

		readonly HashSet<IntVec3> cells = [];
		readonly List<Metaball> metaballs = [];
		readonly ComputeBuffer metaballBuffer = new(MAX_METABALLS, Marshal.SizeOf(typeof(Metaball)));

		Mesh mesh = null;
		Material metaballMaterial;

		Mesh debugMesh = null;

		float radius, power, centerX, centerZ;

		public static void Spawn(Map map, IntVec3 cell)
		{
			var blob = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieBlob, null) as ZombieBlob;
			blob.Position = cell;
			blob.metaballMaterial = new Material(Assets.MetaballShader);
			blob.radius = elementRadius * 9f;
			blob.power = elementPower;
			blob.cells.Add(IntVec3.Zero);

			blob.UpdateAll();

			blob.SetFactionDirect(Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies));
			GenSpawn.Spawn(blob, cell, map, Rot4.Random, WipeMode.Vanish, false);

			blob.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Blob));

			// TODO: enable later
			//
			// var headline = "LetterLabelZombieBlob".Translate();
			// var text = "ZombieBlob".Translate();
			// Find.LetterStack.ReceiveLetter(headline, text, LetterDefOf.ThreatSmall, new GlobalTargetInfo(cell, map));
			// 
			// if (Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
			//	CustomDefs.ZombiesRising.PlayOneShotOnCamera(null);
		}

		public static void AddCell(Map map, IntVec3 cell)
		{
			var blob = map.mapPawns.AllPawns.OfType<ZombieBlob>()
				.OrderBy(blob => blob.Position.DistanceTo(cell))
				.FirstOrDefault();
			blob?.AddCell(cell);
		}

		~ZombieBlob()
		{
			Object.Destroy(metaballMaterial);
			metaballBuffer.Dispose();

			Object.Destroy(mesh);
			Object.Destroy(debugMesh);
		}

		void AddCell(IntVec3 newCell)
		{
			cells.Add(newCell - Position);
			UpdateAll();
		}

		void UpdateAll()
		{
			var min_x = cells.Min(c => c.x) - 1f;
			var min_z = cells.Min(c => c.z) - 1f;
			var max_x = cells.Max(c => c.x) + 1f;
			var max_z = cells.Max(c => c.z) + 1f;

			centerX = (min_x + max_x) / 2;
			centerZ = (min_z + max_z) / 2;

			var dx = max_x - min_x;
			var dz = max_z - min_z;
			var totalSize = Mathf.Max(dx, dz);
			if (dx < totalSize)
			{
				min_x -= (totalSize - dx) / 2;
				max_x += (totalSize - dx) / 2;
			}
			if (dz < totalSize)
			{
				min_z -= (totalSize - dz) / 2;
				max_z += (totalSize - dz) / 2;
			}

			var size2 = new Vector2(totalSize, totalSize);

			if (mesh != null)
				Object.Destroy(mesh);
			mesh = MeshMakerPlanes.NewPlaneMesh(size2, false, false, false);

			if (debugMesh != null)
				Object.Destroy(debugMesh);
			debugMesh = MeshMakerPlanes.NewPlaneMesh(size2, false, false, false);

			var allCells = cells.ToArray();
			var cellCount = allCells.Length;

			while (metaballs.Count < cellCount)
			{
				var cell = allCells[metaballs.Count];
				var x = GenMath.LerpDouble(min_x, max_x, 0, 1, cell.x);
				var y = GenMath.LerpDouble(min_z, max_z, 0, 1, cell.z);
				metaballs.Add(new()
				{
					position = new Vector2(x, y),
					direction = Vector2.zero,
					color = color,
				});
			}
			while (metaballs.Count > cellCount)
				metaballs.RemoveLast();

			for (var i = 0; i < cellCount; i++)
			{
				var mb = metaballs[i];
				mb.radius = radius;
				mb.power = power;
				mb.size = GetSize(allCells[i]) / totalSize;
				metaballs[i] = mb;
			}

			metaballBuffer.SetData(metaballs, 0, 0, metaballs.Count);
			metaballMaterial.SetBuffer("_MetaballBuffer", metaballBuffer);
		}

		float GetSize(IntVec3 cell)
		{
			var (x, y) = (cell.x, cell.z);
			var count = 0;
			for (var dx = -1; dx <= 1; dx++)
				for (var dy = -1; dy <= 1; dy++)
				{
					if (dx == 0 && dy == 0)
						continue;
					if (cells.Contains(new IntVec3(x + dx, 0, y + dy)))
						count++;
				}
			return elementSizes[count];
		}

		public override void Draw()
		{
			var offset = new Vector3(centerX, 0, centerZ);
			Graphics.DrawMesh(debugMesh, DrawPos + offset + new Vector3(0, -0.0001f, 0), Quaternion.identity, debugMaterial, 0);
			Graphics.DrawMesh(mesh, DrawPos + offset, Quaternion.identity, metaballMaterial, 0);
		}

		public override string GetInspectString()
		{
			return "Zombie Blob";
		}

		public override void ExposeData()
		{
			base.ExposeData();
		}
	}
}