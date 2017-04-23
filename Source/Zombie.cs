using Harmony;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Rubble
	{
		public float destX, destY;
		public float pX, pY;
		public float drop, dropSpeed;

		public float scale;
		public float rot;

		public static Rubble Create(float progress)
		{
			var revProgress = 1f - progress;
			return new Rubble()
			{
				destX = Rand.Range(-revProgress, revProgress),
				destY = Rand.Range(0, progress),
				scale = Rand.Range(0f, 0.5f + progress / 2f),
				pX = 0f,
				pY = Rand.Range(progress / 2f, progress),
				drop = 0f,
				dropSpeed = 0f,
				rot = Rand.Range(-0.05f, 0.05f)
			};
		}
	}

	public enum ZombieState
	{
		Emerging,
		Wandering,
		Tracking
	}

	public class Zombie : Pawn
	{
		public static Type type = typeof(Zombie);
		public ZombieState state = ZombieState.Emerging;

		long nextRubbleTicks = 0;
		int rubbleCounter = 0;
		List<Rubble> rubbles = new List<Rubble>();

		static Type[] args = new Type[]
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
		static int maxDeltaTicks = 8;
		static int rubbleAmount = 40;
		static float maxHeight = 0.4f;
		static float minScale = 0.05f;
		static float maxScale = 0.25f;

		void GenerateRubble()
		{
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
		}

		void AnimateRubble()
		{
			foreach (var r in rubbles)
			{
				var dx = Math.Sign(r.pX) / 2f - r.pX;
				r.pX += (r.destX - r.pX) * 0.5f;
				var dy = r.destY - r.pY;
				r.pY += dy * 0.5f + Math.Abs(0.5f - dx) / 10f;
				r.rot = r.rot * 0.95f + (r.destX - r.pX) / 2f;

				if (dy < 0.1f)
				{
					r.dropSpeed += 0.01f;
					if (r.drop < 0.3f) r.drop += r.dropSpeed;
				}
			}
		}

		void RenderRubble(Vector3 drawLoc)
		{
			var a = Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1);
			foreach (var r in rubbles)
			{
				var scale = minScale + (maxScale - minScale) * r.scale;
				var x = 0f + r.pX / 2f;
				var y = -0.5f + Math.Max(0f, r.pY - r.drop) * (maxHeight - scale / 2f) + scale / 2f;
				var pos = drawLoc + new Vector3(x, a, y);
				var rot = Quaternion.Euler(0f, r.rot * 360f, 0f);
				Tools.DrawScaledMesh(MeshPool.plane10, Main.rubble, pos, rot, scale, scale);
			}
		}

		public void Render(PawnRenderer renderer, Vector3 drawLoc, RotDrawMode bodyDrawType)
		{
			if (state == ZombieState.Emerging)
			{
				GenerateRubble();
				if (rubbles != null)
					AnimateRubble();
				RenderRubble(drawLoc);
			}
			else
			{
				var pawn = Traverse.Create(renderer).Field("pawn").GetValue<Pawn>();
				Traverse.Create(renderer)
					.Method("RenderPawnInternal", args)
					.GetValue(drawLoc, Quaternion.identity, true, pawn.Rotation, pawn.Rotation, bodyDrawType, false);
			}
		}
	}
}