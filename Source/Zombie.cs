using Harmony;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	public enum NthTick
	{
		Every2,
		Every10,
		Every15,
		Every60
	}

	[StaticConstructorOnStartup]
	public class Zombie : Pawn, IDisposable
	{
		public ZombieState state = ZombieState.Emerging;
		public int raging = 0;
		public IntVec3 wanderDestination = IntVec3.Invalid;

		int rubbleTicks;
		public int rubbleCounter;
		List<Rubble> rubbles = new List<Rubble>();

		public IntVec2 sideEyeOffset;
		public bool wasColonist;
		public IntVec3 lastGotoPosition = IntVec3.Invalid;

		public bool bombWillGoOff = false;
		public int lastBombTick = 0;
		public float bombTickingInterval = -1f;

		private bool disposed = false;
		public VariableGraphic customHeadGraphic; // not saved
		public VariableGraphic customBodyGraphic; // not saved

		static int totalNthTicks = 0;
		static public int[] nthTickValues;
		static Zombie()
		{
			var nths = Enum.GetNames(typeof(NthTick));
			totalNthTicks = nths.Length;
			nthTickValues = new int[totalNthTicks];
			for (var n = 0; n < totalNthTicks; n++)
			{
				var vstr = nths[n].ReplaceFirst("Every", "");
				nthTickValues[n] = int.Parse(vstr);
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref state, "zstate");
			Scribe_Values.Look(ref raging, "raging");
			Scribe_Values.Look(ref wanderDestination, "wanderDestination");
			Scribe_Values.Look(ref rubbleTicks, "rubbleTicks");
			Scribe_Values.Look(ref rubbleCounter, "rubbleCounter");
			Scribe_Collections.Look(ref rubbles, "rubbles", LookMode.Deep);
			Scribe_Values.Look(ref wasColonist, "wasColonist");
			Scribe_Values.Look(ref bombWillGoOff, "bombWillGoOff");
			Scribe_Values.Look(ref bombTickingInterval, "bombTickingInterval");

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				// fix for old zombies

				if ((Drawer.leaner is ZombieLeaner) == false)
					Drawer.leaner = new ZombieLeaner(this);

				if (bombTickingInterval == 0f)
					bombTickingInterval = -1f;

				ZombieGenerator.AssignNewCustomGraphics(this);
			}

			if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
			{
				var idx = ageTracker.CurLifeStageIndex; // trigger calculations
			}
		}

		~Zombie()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			customHeadGraphic?.Dispose();
			customHeadGraphic = null;

			customBodyGraphic?.Dispose();
			customBodyGraphic = null;

			var head = Drawer.renderer.graphics.headGraphic as VariableGraphic;
			head?.Dispose();
			Drawer.renderer.graphics.headGraphic = null;

			var naked = Drawer.renderer.graphics.nakedGraphic as VariableGraphic;
			naked?.Dispose();
			Drawer.renderer.graphics.nakedGraphic = null;
		}

		public override void Kill(DamageInfo? dinfo, Hediff exactCulprit = null)
		{
			if (bombTickingInterval != -1f)
			{
				bombTickingInterval = -1f;
				bombWillGoOff = false;

				var def = ThingDef.Named("Apparel_BombVest");
				Drawer.renderer.graphics.apparelGraphics
					.RemoveAll(record => record.sourceApparel?.def == def);

				Map.GetComponent<TickManager>().AddExplosion(Position);
			}

			base.Kill(dinfo);
		}

		public override void DeSpawn()
		{
			var grid = Map.GetGrid();
			grid.ChangeZombieCount(lastGotoPosition, -1);
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

		private int[] nextNthTick = new int[totalNthTicks];
		public bool EveryNTick(NthTick interval)
		{
			var n = (int)interval;
			var t = GenTicks.TicksAbs;
			if (t > nextNthTick[n])
			{
				var d = nthTickValues[n];
				nextNthTick[n] = t + d;
				return true;
			}
			return false;
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
					pather?.PatherTick();
					jobs?.JobTrackerTick();
					stances?.StanceTrackerTick();
					verbTracker?.VerbsTick();
					natives?.NativeVerbsTick();
					Drawer?.DrawTrackerTick();
					rotationTracker?.RotationTrackerTick();
				}

				health?.HealthTick();
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

		static MethodInfo m_RenderPawnInternal = AccessTools.Method(typeof(PawnRenderer), "RenderPawnInternal", RenderPawnInternalParameterTypes);
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

				m_RenderPawnInternal.Invoke(renderer, new object[] {
					drawLoc + new Vector3(0, 0, bodyOffset), Quaternion.Euler(bodyRot, 0, 0), true, Rot4.North, Rot4.North, bodyDrawType, false, false
				});
			}

			RenderRubble(drawLoc);
		}
	}
}