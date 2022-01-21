using HarmonyLib;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class ZombieShocker : Building
	{
		public CompPowerTrader compPowerTrader;

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			compPowerTrader = GetComp<CompPowerTrader>();
		}

		public override void TickRare()
		{
			base.TickRare();
			if (compPowerTrader.PowerOn)
				compPowerTrader.PowerOutput = -compPowerTrader.Props.basePowerConsumption;
		}

		public float TotalAvailableEnergy()
		{
			return compPowerTrader.PowerNet.batteryComps.Sum(comp => comp.StoredEnergy);
		}

		public void RemoveEnergy(float amount)
		{
			while (amount > 0)
			{
				var comp = compPowerTrader.PowerNet.batteryComps
					.InRandomOrder()
					.FirstOrDefault(comp => comp.StoredEnergy > 0);

				var diff = Mathf.Min(comp.StoredEnergy, amount);
				comp.DrawPower(diff);
				amount -= diff;
			}
		}

		public void Shock(Room room)
		{
			var grid = room.Map.thingGrid;
			room.Cells.Where(c => c.Standable(room.Map)).Do(cell =>
			{
				var effecter = new Effecter(EffecterDefOf.Interceptor_BlockedProjectile);
				effecter.Trigger(new TargetInfo(cell, room.Map, false), TargetInfo.Invalid);
				effecter.Cleanup();

				grid.ThingsAt(cell).OfType<Zombie>().Do(zombie => zombie.Unrope());
			});
		}

		public override void ReceiveCompSignal(string signal)
		{
			if (signal == "Activate")
			{
				var map = Find.CurrentMap;
				var cell = Position + IntVec3.North.RotatedBy(Rotation);
				var room = cell.GetRoom(map);
				var amount = room.Cells.Where(c => c.Standable(map)).Count() * 6;

				var available = TotalAvailableEnergy();
				if (amount > available)
				{
					var effecter = new Effecter(EffecterDefOf.Interceptor_BlockedProjectile);
					effecter.Trigger(new TargetInfo(Position, map, false), TargetInfo.Invalid);
					effecter.Cleanup();
					return;
				}
				Shock(room);
				RemoveEnergy(amount);
			}
		}
	}
}
