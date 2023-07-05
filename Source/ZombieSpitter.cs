using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class ZombieSpitter : Pawn
	{

	}

	/*
	public class ZombieSpitterBuilding : Building
	{
		private enum State
		{
			Hidden,
			Opening,
			Appearing,
			Idleing,
			Preparing,
			Spitting,
			Disappearing,
			Closing
		}

		private const int ticksOpening = 12;
		private const int ticksAppearing = 30;
		private const int ticksIdleing = 600;
		private const int ticksPreparing = 90;
		private const int ticksSpitting = 600;
		private const int ticksSpittingInterval = 45;
		private const int ticksDisappearing = 30;
		private const int ticksClosing = 12;

		private State state = State.Hidden;
		private int stateValue = 0;
		private Action[] nextStates = Array.Empty<Action>();
		private int liftEyebrows = 0;

		private Transform spitter;
		private Transform hole;
		private Transform monster;
		private Transform pupillaries;
		private Transform eyes;
		private MeshRenderer baseMeshRenderer;
		private Transform smoke;

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);

			spitter = Assets.NewSpitter(Position).transform;
			hole = spitter.GetChild(0).transform;
			monster = spitter.GetChild(1).transform;
			pupillaries = monster.GetChild(0).transform;
			eyes = monster.GetChild(1).transform;
			baseMeshRenderer = monster.GetChild(2).transform.GetComponent<MeshRenderer>();
			smoke = spitter.GetChild(2).transform;

			nextStates = new[] { PrepareOpen, PrepareAppear, PrepareIdle, PreparePrepare, PrepareSpit, PrepareDisappear, PrepareClose, PrepareHide };
			nextStates.Last()();

			TimeControlService.Subscribe(this, speed =>
			{
				var particleSystem = smoke.GetComponent<ParticleSystem>();
				var main = particleSystem.main;
				main.simulationSpeed = Find.TickManager.TickRateMultiplier * 0.75f;
			});

			ClearMapsService.Subscribe(this, Cleanup);
		}

		private void Cleanup()
		{
			UnityEngine.Object.Destroy(spitter.gameObject);

			TimeControlService.Unsubscribe(this);
			ClearMapsService.Unsubscribe(this);
		}

		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			base.DeSpawn(mode);
			Cleanup();
		}

		public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
		{
			base.Destroy(mode);
			Cleanup();
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref state, "state", State.Hidden);
			Scribe_Values.Look(ref stateValue, "stateValue", 0);
		}

		private void InitBaseValues()
		{
			var particleSystem = smoke.GetComponent<ParticleSystem>();
			var main = particleSystem.main;
			main.simulationSpeed = Find.TickManager.TickRateMultiplier * 0.75f;

			pupillaries.gameObject.SetActive(false);
			eyes.gameObject.SetActive(false);

			UpdateMonsterPosition(0);
		}

		public override void Tick()
		{
			base.Tick();

			stateValue--;
			if (nextStates.Length > 0 && stateValue <= 0)
				nextStates[(int)state]();

			var map = Map;
			if (map == null)
				return;
		}

		public void PrepareHide()
		{
			state = State.Hidden;
			InitBaseValues();
			UpdateHole(0f);
			stateValue = 120;
		}

		public void PrepareOpen()
		{
			state = State.Opening;
			stateValue = ticksOpening;
		}

		public void PrepareAppear()
		{
			state = State.Appearing;
			UpdateHole(1f);
			pupillaries.gameObject.SetActive(true);
			eyes.gameObject.SetActive(true);
			stateValue = ticksAppearing;
		}

		public void PrepareIdle()
		{
			state = State.Idleing;
			stateValue = ticksIdleing;
		}

		public void PreparePrepare()
		{
			state = State.Preparing;
			pupillaries.localRotation = Quaternion.identity;
			monster.localRotation = Quaternion.identity;
			ChangeMonster(2);
			stateValue = ticksPreparing;
		}

		public void PrepareSpit()
		{
			state = State.Spitting;
			pupillaries.gameObject.SetActive(false);
			eyes.gameObject.SetActive(false);
			FocusOn(Position);
			ChangeMonster(4);
			stateValue = ticksSpitting;
		}

		public void PrepareDisappear()
		{
			state = State.Disappearing;
			pupillaries.gameObject.SetActive(true);
			eyes.gameObject.SetActive(true);
			ChangeMonster(0);
			FocusOn(Position);
			stateValue = ticksDisappearing;
		}

		public void PrepareClose()
		{
			state = State.Closing;
			stateValue = ticksClosing;
		}

		private void UpdateHole(float holeSize)
		{
			hole.localScale = new Vector3(holeSize, 1f, holeSize);
			smoke.localScale = new Vector3(holeSize, holeSize, 1f);
		}

		private void UpdateMonsterPosition(float outside) => monster.localScale = Vector3.one * outside;

		private void ChangeMonster(int n) => baseMeshRenderer.materials[0].mainTexture = Assets.spitterImages[n];

		private void FocusOn(IntVec3 pos)
		{
			var fx1 = GenMath.LerpDoubleClamped(-3, 3, 4.4f, -4.4f, pos.x - Position.x);
			var fz1 = GenMath.LerpDoubleClamped(-3, 3, -2.3f, 5.3f, pos.z - Position.z);
			pupillaries.localRotation = Quaternion.Euler(fz1, 0f, fx1);
			var fx2 = GenMath.LerpDoubleClamped(-5, 5, 2f, -2f, pos.x - Position.x);
			var fz2 = GenMath.LerpDoubleClamped(-5, 5, -4f, 4f, pos.z - Position.z);
			monster.localRotation = Quaternion.Euler(fz2, 0f, fx2);
		}

		private void FollowMouse()
		{
			FocusOn(UI.MouseCell());

			if (liftEyebrows >= 0)
				liftEyebrows--;
			else if (Rand.Chance(0.1f))
				liftEyebrows = Rand.Range(40, 50);

			ChangeMonster(liftEyebrows > 0 ? 1 : 0);
		}

		private void RollEyes()
		{
			var pos = Position.ToVector3() + new Vector3(10, 0, 0).RotatedBy(stateValue * 20f);
			FocusOn(pos.ToIntVec3());

			var y = GenMath.LerpDoubleClamped(0f, ticksPreparing, 19f, -2f, stateValue);
			monster.localRotation = Quaternion.Euler(y, 0f, 0f);

			if (liftEyebrows >= 0)
				liftEyebrows--;
			else if (Rand.Chance(0.1f))
				liftEyebrows = Rand.Range(40, 50);

			ChangeMonster(2 + liftEyebrows > 0 ? 1 : 0);
		}
		
		private void Spitting()
		{
			var n = (ticksSpitting - stateValue) % ticksSpittingInterval;
			var t = n / (float)ticksSpittingInterval;
			// https://www.desmos.com/calculator/y3li2nfotg
			var val = Mathf.Cos(Mathf.PI * Mathf.Pow(t, 6) - 1.5f * Mathf.PI) / 10f + 1f;
			UpdateMonsterPosition(val);

			//if (n == 40)
			//{
			//	var zombie = ZombieGenerator.SpawnZombie(Position, Map, ZombieType.Normal);
			//	zombie.rubbleCounter = Constants.RUBBLE_AMOUNT;
			//	zombie.state = ZombieState.Wandering;
			//	zombie.Rotation = Rot4.South;
			//	_ = Map.GetComponent<TickManager>().allZombiesCached.Add(zombie);
			//}
		}

		public override void Draw()
		{
			base.Draw();

			switch (state)
			{
				case State.Hidden:
					break;
				case State.Opening:
					UpdateHole(1f - stateValue / (float)ticksOpening);
					break;
				case State.Appearing:
					FollowMouse();
					UpdateMonsterPosition(1f - stateValue / (float)ticksAppearing);
					break;
				case State.Idleing:
					FollowMouse();
					break;
				case State.Preparing:
					RollEyes();
					break;
				case State.Spitting:
					Spitting();
					break;
				case State.Disappearing:
					FollowMouse();
					UpdateMonsterPosition(stateValue / (float)ticksDisappearing);
					break;
				case State.Closing:
					UpdateHole(stateValue / (float)ticksClosing);
					break;
			}
		}
	}
	*/
}