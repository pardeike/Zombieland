using Harmony;
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
		public IntVec3 wanderDestination = IntVec3.Invalid;

		int rubbleTicks;
		public int rubbleCounter;
		List<Rubble> rubbles = new List<Rubble>();

		public bool wasColonist;

		public VariableGraphic customHeadGraphic; // not saved
		public VariableGraphic customBodyGraphic; // not saved

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref state, "zstate");
			Scribe_Values.Look(ref wanderDestination, "wanderDestination");
			Scribe_Values.Look(ref rubbleTicks, "rubbleTicks");
			Scribe_Values.Look(ref rubbleCounter, "rubbleCounter");
			Scribe_Collections.Look(ref rubbles, "rubbles", LookMode.Deep);
			Scribe_Values.Look(ref wasColonist, "wasColonist");

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				// fix for old zombies not having correct leaner
				if ((Drawer.leaner is ZombieLeaner) == false)
					Drawer.leaner = new ZombieLeaner(this);

				ZombieGenerator.AssignNewCustomGraphics(this);
			}
		}

		public override void DeSpawn()
		{
			var grid = Map.GetGrid();
			if (pather != null)
			{
				var dest = pather.Destination;
				if (dest != null && dest != Position)
					grid.ChangeZombieCount(dest.Cell, -1);
			}
			grid.ChangeZombieCount(Position, -1);

			base.DeSpawn();
		}

		static readonly Type[] RenderPawnInternalParameterTypes = {
			typeof(Vector3),
			typeof(Quaternion),
			typeof(bool),
			typeof(Rot4),
			typeof(Rot4),
			typeof(RotDrawMode),
			typeof(bool),
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
				var bottomExtend = Math.Abs(r.pX) / 6f;
				var y = -0.5f + Math.Max(bottomExtend, r.pY - r.drop) * (Constants.MAX_HEIGHT - scale / 2f) + (scale - Constants.MAX_SCALE) / 2f;
				var pos = drawLoc + new Vector3(x, 0, y);
				pos.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn + 1);
				var rot = Quaternion.Euler(0f, r.rot * 360f, 0f);
				GraphicToolbox.DrawScaledMesh(MeshPool.plane10, Constants.RUBBLE, pos, rot, scale, scale);
			}
		}

		public override void Tick()
		{
			var comps = AllComps;
			for (var i = 0; i < comps.Count; i++)
				comps[i].CompTick();
		}

		public void CustomTick()
		{
			if (!ThingOwnerUtility.ContentsFrozen(base.ParentHolder))
			{
				if (Spawned)
				{
					if (pather != null) pather.PatherTick();
					if (jobs != null) jobs.JobTrackerTick();
					if (stances != null) stances.StanceTrackerTick();
					if (verbTracker != null) verbTracker.VerbsTick();
					if (natives != null) natives.NativeVerbsTick();
					Drawer.DrawTrackerTick();
				}

				if (health != null)
					health.HealthTick();

				//if (!Dead && mindState != null)
				//	mindState.MindStateTick();
			}

			if (state == ZombieState.Emerging)
				HandleRubble();
		}

		public override void DrawGUIOverlay()
		{
			if (wasColonist)
			{
				var pos = GenMapUI.LabelDrawPosFor(this, -0.6f);
				GenMapUI.DrawPawnLabel(this, pos, 1f, 9999f, null, GameFont.Tiny, true, true);
			}
		}

		public void Render(PawnRenderer renderer, Vector3 drawLoc, RotDrawMode bodyDrawType)
		{
			if (!renderer.graphics.AllResolved)
				renderer.graphics.ResolveAllGraphics();

			drawLoc.x = (int)(drawLoc.x) + 0.5f;

			var progress = rubbleCounter / (float)Constants.RUBBLE_AMOUNT;
			if (progress >= Constants.EMERGE_DELAY)
			{
				var bodyRot = GenMath.LerpDouble(Constants.EMERGE_DELAY, 1, 90, 0, progress);
				var bodyOffset = GenMath.LerpDouble(Constants.EMERGE_DELAY, 1, -0.45f, 0, progress);

				Traverse.Create(renderer)
					.Method("RenderPawnInternal", RenderPawnInternalParameterTypes)
					.GetValue(drawLoc + new Vector3(0, 0, bodyOffset), Quaternion.Euler(bodyRot, 0, 0), true, Rot4.North, Rot4.North, bodyDrawType, false, false);
			}

			RenderRubble(drawLoc);
		}
	}
}