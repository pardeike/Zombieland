using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	public class Zombie : Pawn
	{
		public ZombieState state = ZombieState.Emerging;

		int rubbleTicks = 0;
		int rubbleCounter = 0;
		List<Rubble> rubbles = new List<Rubble>();

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.LookValue(ref state, "zstate");
			Scribe_Values.LookValue(ref rubbleTicks, "rubbleTicks");
			Scribe_Values.LookValue(ref rubbleCounter, "rubbleCounter");
			Scribe_Collections.LookList(ref rubbles, "rubbles", LookMode.Deep);
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

		void HandleRubble()
		{
			if (rubbleCounter == 0 && Constants.USE_SOUND)
			{
				var info = SoundInfo.InMap(new TargetInfo(Position, Map));
				SoundDef.Named("ZombieDigOut").PlayOneShot(info);
			}

			if (rubbleCounter == Constants.RUBBLE_AMOUNT)
			{
				state = ZombieState.Wandering;
				rubbles = new List<Rubble>();
			}
			else if (rubbleCounter < Constants.RUBBLE_AMOUNT && rubbleTicks-- < 0)
			{
				var idx = Rand.Range(rubbleCounter * 4 / 5, rubbleCounter);
				rubbles.Insert(idx, Rubble.Create(rubbleCounter / (float)Constants.RUBBLE_AMOUNT));

				var deltaTicks = Constants.MIN_DELTA_TICKS + (float)(Constants.MAX_DELTA_TICKS - Constants.MIN_DELTA_TICKS) / Math.Min(1, rubbleCounter * 2 - Constants.RUBBLE_AMOUNT);
				rubbleTicks = (int)deltaTicks;

				rubbleCounter++;
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
			foreach (var r in rubbles)
			{
				var scale = Constants.MIN_SCALE + (Constants.MAX_SCALE - Constants.MIN_SCALE) * r.scale;
				var x = 0f + r.pX / 2f;
				var y = -0.5f + Math.Max(0f, r.pY - r.drop) * (Constants.MAX_HEIGHT - scale / 2f) + (scale - Constants.MAX_SCALE) / 2f;
				var pos = drawLoc + new Vector3(x, 0, y);
				pos.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn + 1);
				var rot = Quaternion.Euler(0f, r.rot * 360f, 0f);
				Tools.DrawScaledMesh(MeshPool.plane10, Constants.RUBBLE, pos, rot, scale, scale);
			}
		}

		public override void Tick()
		{
		}

		public void CustomTick()
		{
			if ((Find.TickManager.TicksGame % 250) == 0)
				TickRare();

			if (base.Spawned)
				pather.PatherTick();

			if (base.Spawned)
				jobs.JobTrackerTick();

			if (base.Spawned)
			{
				stances.StanceTrackerTick();
				verbTracker.VerbsTick();
				natives.NativeVerbsTick();
				Drawer.DrawTrackerTick();
			}

			health.HealthTick();

			if (!Dead)
			{
				mindState.MindStateTick();
				//carryTracker.CarryHandsTick();
				//needs.NeedsTrackerTick();
			}

			//if (equipment != null)
			//	equipment.EquipmentTrackerTick();

			//if (apparel != null)
			//	apparel.ApparelTrackerTick();

			//if (interactions != null)
			//	interactions.InteractionsTrackerTick();

			//if (caller != null)
			//	caller.CallTrackerTick();

			//if (skills != null)
			//	skills.SkillsTick();

			//if (inventory != null)
			//	inventory.InventoryTrackerTick();

			//if (drafter != null)
			//	drafter.DraftControllerTick();

			//if (relations != null)
			//	relations.SocialTrackerTick();

			//if (RaceProps.Humanlike)
			//	guest.GuestTrackerTick();

			//ageTracker.AgeTick();
			//records.RecordsTick();

			//

			if (state == ZombieState.Emerging)
				HandleRubble();
		}

		public void Render(PawnRenderer renderer, Vector3 drawLoc, RotDrawMode bodyDrawType)
		{
			if (!renderer.graphics.AllResolved)
				renderer.graphics.ResolveAllGraphics();

			drawLoc.x = (int)(drawLoc.x) + 0.5f;

			var progress = rubbleCounter / (float)Constants.RUBBLE_AMOUNT;
			if (progress >= Constants.EMERGE_DELAY)
			{
				var headRot = GenMath.LerpDouble(Constants.EMERGE_DELAY, 1, 35, 0, progress);
				var headOffset = GenMath.LerpDouble(Constants.EMERGE_DELAY, 1, -0.85f, 0, progress);
				var bodyRot = GenMath.LerpDouble(Constants.EMERGE_DELAY, 1, 90, 0, progress);
				var bodyOffset = GenMath.LerpDouble(Constants.EMERGE_DELAY, 1, -0.45f, 0, progress);
				var headScale = GenMath.LerpDouble(Constants.EMERGE_DELAY, 1, 0.25f, 1, progress);
				var facing = Rotation;

				var quat = Quaternion.Euler(headRot, 0, 0);
				var rootLoc = drawLoc + new Vector3(0, 0, headOffset);
				var vector4 = rootLoc;
				if (facing != Rot4.North)
					vector4.y += 0.03f;
				else
					vector4.y += 0.025f;

				var headMeshSize = MeshPool.humanlikeHeadSet.MeshAt(facing).bounds.size.x;
				var hairMeshSize = renderer.graphics.HairMeshSet.MeshAt(facing).bounds.size.x;

				var vector5 = quat * renderer.BaseHeadOffsetAt(facing);
				var mesh2 = new GraphicMeshSet(headMeshSize * headScale).MeshAt(facing); // MeshPool.humanlikeHeadSet.MeshAt(facing);
				var mat = renderer.graphics.HeadMatAt(facing, bodyDrawType);
				GenDraw.DrawMeshNowOrLater(mesh2, vector4 + vector5, quat, mat, false);
				var vector6 = rootLoc + vector5;
				vector6.y += 0.035f;
				bool flag = false;
				var mesh3 = new GraphicMeshSet(hairMeshSize * headScale).MeshAt(facing); // renderer.graphics.HairMeshSet.MeshAt(facing);
				var apparelGraphics = renderer.graphics.apparelGraphics;
				for (var j = 0; j < apparelGraphics.Count; j++)
				{
					var record2 = apparelGraphics[j];
					if (record2.sourceApparel.def.apparel.LastLayer == ApparelLayer.Overhead)
					{
						flag = true;
						var record3 = apparelGraphics[j];
						var baseMat = record3.graphic.MatAt(facing, null);
						baseMat = renderer.graphics.flasher.GetDamagedMat(baseMat);
						GenDraw.DrawMeshNowOrLater(mesh3, vector6, quat, baseMat, false);
					}
				}
				if (!flag && (bodyDrawType != RotDrawMode.Dessicated))
				{
					// Mesh mesh4 = new GraphicMeshSet(hairMeshSize * headScale).MeshAt(facing); // renderer.graphics.HairMeshSet.MeshAt(facing);
					var material4 = renderer.graphics.HairMatAt(facing);
					GenDraw.DrawMeshNowOrLater(mesh3, vector6, quat, material4, false);
				}

				// only body
				var headGraphic = renderer.graphics.headGraphic;
				renderer.graphics.headGraphic = null;
				Traverse.Create(renderer)
					.Method("RenderPawnInternal", RenderPawnInternalParameterTypes)
					.GetValue(drawLoc + new Vector3(0, 0, bodyOffset), Quaternion.Euler(bodyRot, 0, 0), true, facing, facing, bodyDrawType, false);
				renderer.graphics.headGraphic = headGraphic;
			}

			RenderRubble(drawLoc);
		}
	}
}