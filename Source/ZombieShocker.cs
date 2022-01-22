using RimWorld;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public class ZombieShocker : Building
	{
		public CompPowerTrader compPowerTrader;
		SubEffecter_ZombieShocker subEffecter = null;

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

		public override void Tick()
		{
			base.Tick();
			subEffecter?.SubEffectTick(TargetInfo.Invalid, TargetInfo.Invalid);
		}

		public override void ReceiveCompSignal(string signal)
		{
			if (signal == "Activate")
			{
				var map = Find.CurrentMap;
				var cell = Position + IntVec3.North.RotatedBy(Rotation);

				var effecter = new Effecter(CustomDefs.ZombieShockerRoom);
				subEffecter = effecter.children.OfType<SubEffecter_ZombieShocker>().FirstOrDefault();
				subEffecter.compPowerTrader = compPowerTrader;
				effecter.Trigger(new TargetInfo(cell, map, false), TargetInfo.Invalid);
				effecter.Cleanup();
			}
		}
	}
}
