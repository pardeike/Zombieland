using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	public class SubEffecter_ZombieShocker : SubEffecter
	{
		static readonly ThingDef[] zaps = new ThingDef[] { CustomDefs.ZombieZapA, CustomDefs.ZombieZapB, CustomDefs.ZombieZapC, CustomDefs.ZombieZapD };
		static readonly Vector3 zapBaseVec = new Vector3(0f, 0f, -0.25f);
		static readonly int zapDelay = 45;

		public CompPowerTrader compPowerTrader;

		private ZombieShocker shocker;
		private IntVec3 cell;
		private Map map;
		private Room room;
		private readonly Queue<IntVec3> cells = new Queue<IntVec3>();
		private const float amount = 6;
		private int zappingState = -1;

		public SubEffecter_ZombieShocker(SubEffecterDef def, Effecter parent) : base(def, parent)
		{
		}

		void RandomZap(Vector3 pos, float rot, float alpha)
		{
			var mote = (Mote)ThingMaker.MakeThing(zaps[Random.Range(0, 3)], null);
			var scale = Random.Range(1.5f, 2f);
			if (rot == -1)
				rot = Random.Range(0f, 359f);
			var color = Color.white;
			color.a = alpha;
			mote.exactScale = new Vector3(scale, 1, scale);
			mote.exactRotation = rot;
			mote.exactPosition = pos + Quaternion.Euler(0, rot, 0) * zapBaseVec;
			mote.instanceColor = color;
			_ = GenSpawn.Spawn(mote, pos.ToIntVec3(), map, WipeMode.Vanish);
		}

		void ZapZombie(Zombie zombie)
		{
			for (var i = 1; i <= 8; i++)
				RandomZap(zombie.DrawPos, -1, 1);

			zombie.Unrope();
			CustomDefs.ShockingZombie.PlayOneShot(SoundInfo.InMap(new TargetInfo(zombie.Position, map, false), MaintenanceType.None));
		}

		void EndZapping()
		{
			zappingState = -1;
			cells.Clear();
		}

		void ZapNextCell()
		{
			if (cells.Count == 0)
			{
				EndZapping();
				return;
			}

			var battery = compPowerTrader.PowerNet.batteryComps
				.Where(comp => comp.StoredEnergy >= amount)
				.SafeRandomElement();

			if (battery == null)
			{
				EndZapping();
				Messages.Message("ZombieShockerLowBatteryState".Translate(), shocker, MessageTypeDefOf.RejectInput, null, false);
				return;
			}

			var c = cells.Dequeue();
			battery.DrawPower(amount);

			var zombies = map.thingGrid.ThingsAt(c).OfType<Zombie>();
			if (zombies.Any())
				zombies.Do(zombie => ZapZombie(zombie));
			else
			{
				var rot = Random.Range(0f, 359f);
				for (var i = 1; i <= 4; i++)
				{
					RandomZap(c.ToVector3Shifted(), rot, 0.25f);
					rot += 90;
				}
			}
		}

		public override void SubTrigger(TargetInfo A, TargetInfo B, int overrideSpawnTick)
		{
			shocker = A.Thing as ZombieShocker;
			if (shocker == null)
				return;

			cell = shocker.Position + IntVec3.North.RotatedBy(shocker.Rotation);
			map = shocker.Map;

			room = ZombieShocker.GetValidRoom(map, cell);
			if (room == null)
				return;

			room.Cells.Where(c => c.Standable(map)).OrderBy(c => c.DistanceTo(cell)).Do(c => cells.Enqueue(c));
			if (cells.Count == 0)
			{
				var failEffecter = new Effecter(EffecterDefOf.Interceptor_BlockedProjectile);
				failEffecter.Trigger(new TargetInfo(cell, map, false), TargetInfo.Invalid);
				failEffecter.Cleanup();
				return;
			}

			zappingState = zapDelay;
		}

		public override void SubEffectTick(TargetInfo A, TargetInfo B)
		{
			if (zappingState == -1 || shocker.Spawned == false)
				return;
			if (shocker.HasValidRoom() == false)
			{
				EndZapping();
				return;
			}

			if (zappingState == zapDelay)
				CustomDefs.ShockingRoom.PlayOneShot(SoundInfo.InMap(new TargetInfo(cell, map, false), MaintenanceType.None));

			if (zappingState > 0)
			{
				zappingState--;
				return;
			}

			ZapNextCell();
		}
	}
}
