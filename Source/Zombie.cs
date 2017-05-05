using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	public class Rubble : IExposable
	{
		public float destX, destY;
		public float pX, pY;
		public float drop, dropSpeed;

		public float scale;
		public float rot;

		public static Rubble Create(float progress)
		{
			var revProgress = (1f - progress) * 1.2f;
			var dx = Rand.Range(-revProgress, revProgress);
			return new Rubble()
			{
				destX = dx,
				destY = Rand.Range(0, progress),
				scale = Rand.Range(0f, 0.5f + progress / 2f),
				pX = dx * Rand.Range(0f, 0.5f),
				pY = Rand.Range(progress / 2f, progress),
				drop = 0f,
				dropSpeed = 0f,
				rot = Rand.Range(-0.05f, 0.05f)
			};
		}

		public void ExposeData()
		{
			Scribe_Values.LookValue(ref destX, "destX");
			Scribe_Values.LookValue(ref destX, "destY");
			Scribe_Values.LookValue(ref pX, "pX");
			Scribe_Values.LookValue(ref pY, "pY");
			Scribe_Values.LookValue(ref drop, "drop");
			Scribe_Values.LookValue(ref dropSpeed, "dropSpeed");
			Scribe_Values.LookValue(ref dropSpeed, "dropSpeed");
			Scribe_Values.LookValue(ref scale, "scale");
			Scribe_Values.LookValue(ref rot, "rot");
		}
	}

	public enum ZombieState
	{
		Emerging,
		Wandering,
		Tracking,
		ShouldDie
	}

	public class Zombie : Pawn
	{
		public static Type type = typeof(Zombie);
		public ZombieState state = ZombieState.Emerging;

		long nextRubbleTicks = 0;
		int rubbleCounter = 0;
		List<Rubble> rubbles;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.LookValue<ZombieState>(ref state, "zstate");
			Scribe_Values.LookValue(ref nextRubbleTicks, "nextRubbleTicks");
			Scribe_Values.LookValue(ref rubbleCounter, "rubbleCounter");
			Scribe_Collections.LookList(ref rubbles, "rubbles", LookMode.Deep, new object[0]);
		}

		static Type[] RenderPawnInternalParameterTypes = new Type[]
		{
			typeof(Vector3),
			typeof(Quaternion),
			typeof(bool),
			typeof(Rot4),
			typeof(Rot4),
			typeof(RotDrawMode),
			typeof(bool)
		};

		static int minDeltaTicks = 20;
		static int maxDeltaTicks = 4;
		static int rubbleAmount = 50;
		static float maxHeight = 0.6f;
		static float minScale = 0.05f;
		static float maxScale = 0.3f;
		static float zombieLayer = Altitudes.AltitudeFor(AltitudeLayer.Pawn) - 0.005f;
		static float emergeDelay = 0.8f;

		void HandleRubble()
		{
			if (rubbles == null) rubbles = new List<Rubble>();

			var ticks = ageTracker.AgeBiologicalTicks;
			if (rubbleCounter < rubbleAmount && ticks > nextRubbleTicks)
			{
				var idx = Rand.Range(rubbleCounter * 4 / 5, rubbleCounter);
				rubbles.Insert(idx, Rubble.Create(rubbleCounter / (float)rubbleAmount));

				var deltaTicks = minDeltaTicks + (float)(maxDeltaTicks - minDeltaTicks) / Math.Min(1, rubbleCounter * 2 - rubbleAmount);
				nextRubbleTicks = ticks + (int)deltaTicks;

				rubbleCounter++;
				if (rubbleCounter == rubbleAmount)
					state = ZombieState.Wandering;
			}

			foreach (var r in rubbles)
			{
				var dx = Math.Sign(r.pX) / 2f - r.pX;
				r.pX += (r.destX - r.pX) * 0.5f;
				var dy = r.destY - r.pY;
				r.pY += dy * 0.5f + Math.Abs(0.5f - dx) / 10f;
				r.rot = r.rot * 0.95f - (r.destX - r.pX) / 2f;

				if (dy < 0.1f)
				{
					r.dropSpeed += 0.01f;
					if (r.drop < 0.3f) r.drop += r.dropSpeed;
				}
			}
		}

		void RenderRubble(Vector3 drawLoc)
		{
			var altitude = Altitudes.AltitudeFor(AltitudeLayer.Pawn) + 0.005f;
			if (rubbles == null) rubbles = new List<Rubble>();
			foreach (var r in rubbles)
			{
				var scale = minScale + (maxScale - minScale) * r.scale;
				var x = 0f + r.pX / 2f;
				var y = -0.5f + Math.Max(0f, r.pY - r.drop) * (maxHeight - scale / 2f) + (scale - maxScale) / 2f;
				var pos = drawLoc + new Vector3(x, altitude, y);
				var rot = Quaternion.Euler(0f, r.rot * 360f, 0f);
				Tools.DrawScaledMesh(MeshPool.plane10, Main.rubble, pos, rot, scale, scale);
			}
		}

		public void SelectiveDrawTrackerTick()
		{
			if (!Spawned) return;
			if (Current.ProgramState == ProgramState.Playing && !Find.CameraDriver.CurrentViewRect.ExpandedBy(3).Contains(Position)) return;

			Traverse.Create(Drawer).Field("jitterer").GetValue<JitterHandler>().JitterHandlerTick();
			Drawer.leaner.LeanerTick();
			Drawer.rotator.PawnRotatorTick();
			Drawer.renderer.RendererTick();
		}

		public void SelectiveTick()
		{
			if (pather != null) pather.PatherTick();
			if (jobs != null) jobs.JobTrackerTick();
			if (health != null) health.HealthTick();
			if (Drawer != null) SelectiveDrawTrackerTick();
		}

		public override void Tick()
		{
			if (Spawned) SelectiveTick(); //base.Tick();
			if (rubbleCounter == 0)
			{
				if (Main.USE_SOUND)
				{
					var info = SoundInfo.InMap(new TargetInfo(Position, Map));
					SoundDef.Named("ZombieDigOut").PlayOneShot(info);
				}
			}

			HandleRubble();
		}

		public void Render(PawnRenderer renderer, Vector3 drawLoc, RotDrawMode bodyDrawType)
		{
			/*if (rubbleCounter == 0)
			{
				if (Main.USE_SOUND)
				{
					var info = SoundInfo.InMap(new TargetInfo(Position, Map));
					SoundDef.Named("ZombieDigOut").PlayOneShot(info);
				}
			}*/

			drawLoc.x = (int)(drawLoc.x) + 0.5f;
			RenderRubble(drawLoc);

			var progress = rubbleCounter / (float)rubbleAmount;
			if (progress < emergeDelay) return;

			var headRot = GenMath.LerpDouble(emergeDelay, 1, 35, 0, progress);
			var headOffset = GenMath.LerpDouble(emergeDelay, 1, -0.85f, 0, progress);
			var bodyRot = GenMath.LerpDouble(emergeDelay, 1, 90, 0, progress);
			var bodyOffset = GenMath.LerpDouble(emergeDelay, 1, -0.45f, 0, progress);
			var headScale = GenMath.LerpDouble(emergeDelay, 1, 0.25f, 1, progress);
			var facing = this.Rotation;

			/* only head
			Traverse.Create(renderer)
				.Method("RenderPawnInternal", RenderPawnInternalParameterTypes)
				.GetValue(drawLoc + new Vector3(0, 0, headOffset), Quaternion.Euler(headRot, 0, 0), false, facing, facing, bodyDrawType, false); */

			if (!renderer.graphics.AllResolved)
				renderer.graphics.ResolveAllGraphics();

			var quat = Quaternion.Euler(headRot, 0, 0);
			var rootLoc = drawLoc + new Vector3(0, 0, headOffset);
			var vector4 = rootLoc;
			if (facing != Rot4.North)
				vector4.y += 0.03f;
			else
				vector4.y += 0.025f;

			var headMeshSize = MeshPool.humanlikeHeadSet.MeshAt(facing).bounds.size.x;
			var hairMeshSize = renderer.graphics.HairMeshSet.MeshAt(facing).bounds.size.x;

			Vector3 vector5 = (Vector3)(quat * renderer.BaseHeadOffsetAt(facing));
			Mesh mesh2 = new GraphicMeshSet(headMeshSize * headScale).MeshAt(facing); // MeshPool.humanlikeHeadSet.MeshAt(facing);
			Material mat = renderer.graphics.HeadMatAt(facing, bodyDrawType);
			GenDraw.DrawMeshNowOrLater(mesh2, vector4 + vector5, quat, mat, false);
			Vector3 vector6 = rootLoc + vector5;
			vector6.y += 0.035f;
			bool flag = false;
			Mesh mesh3 = new GraphicMeshSet(hairMeshSize * headScale).MeshAt(facing); // renderer.graphics.HairMeshSet.MeshAt(facing);
			List<ApparelGraphicRecord> apparelGraphics = renderer.graphics.apparelGraphics;
			for (int j = 0; j < apparelGraphics.Count; j++)
			{
				ApparelGraphicRecord record2 = apparelGraphics[j];
				if (record2.sourceApparel.def.apparel.LastLayer == ApparelLayer.Overhead)
				{
					flag = true;
					ApparelGraphicRecord record3 = apparelGraphics[j];
					Material baseMat = record3.graphic.MatAt(facing, null);
					baseMat = renderer.graphics.flasher.GetDamagedMat(baseMat);
					GenDraw.DrawMeshNowOrLater(mesh3, vector6, quat, baseMat, false);
				}
			}
			if (!flag && (bodyDrawType != RotDrawMode.Dessicated))
			{
				// Mesh mesh4 = new GraphicMeshSet(hairMeshSize * headScale).MeshAt(facing); // renderer.graphics.HairMeshSet.MeshAt(facing);
				Material material4 = renderer.graphics.HairMatAt(facing);
				GenDraw.DrawMeshNowOrLater(mesh3, vector6, quat, material4, false);
			}

			// only body
			var headGraphic = renderer.graphics.headGraphic;
			renderer.graphics.headGraphic = null;
			Traverse.Create(renderer)
				.Method("RenderPawnInternal", RenderPawnInternalParameterTypes)
				.GetValue(drawLoc + new Vector3(0, 0, bodyOffset), Quaternion.Euler(bodyRot, 0, 0), true, facing, facing, bodyDrawType, false);
			renderer.graphics.headGraphic = headGraphic;

			/* Mesh mesh = MeshPool.humanlikeBodySet.MeshAt(facing);
			Vector3 pos = drawLoc + new Vector3(0f, 0f, bodyOffset);
			Vector3 vector = pos;
			List<Material> list = renderer.graphics.MatsBodyBaseAt(facing, bodyDrawType);
			for (int i = 0; i < list.Count; i++)
			{
				vector.y += 0.005f;
				var originalTexture = list[i].mainTexture;
				var mat = new Material(list[i])
				{
					mainTexture = originalTexture,
					mainTextureOffset = new Vector2(0, 1 - progress),
					shader = ShaderDatabase.CutoutComplex
				};
				Graphics.DrawMesh(mesh, vector, Quaternion.identity, mat, 0);
			}*/

			/*
			pos.y += 0.005f;
			renderer.graphics.apparelGraphics.Where(record => record.sourceApparel.def.apparel.LastLayer == ApparelLayer.Shell).Do(record =>
			{
				var mat = new Material(record.graphic.MatAt(facing, null))
				{
					mainTextureOffset = new Vector2(0, -1 + progress)
				};
				Graphics.DrawMesh(mesh, pos, Quaternion.identity, mat, 0);
			});
			*/

		}
	}
}