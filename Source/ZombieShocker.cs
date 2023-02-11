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
			if (OnWall())
				subEffecter?.SubEffectTick(TargetInfo.Invalid, TargetInfo.Invalid);
			else
				Destroy();
		}

		public override void ReceiveCompSignal(string signal)
		{
			if (signal == "Activate")
			{
				var effecter = new Effecter(CustomDefs.ZombieShockerRoom);
				subEffecter = effecter.children.OfType<SubEffecter_ZombieShocker>().FirstOrDefault();
				subEffecter.compPowerTrader = compPowerTrader;
				effecter.Trigger(new TargetInfo(this), TargetInfo.Invalid);
				effecter.Cleanup();
			}
		}

		public static Room GetValidRoom(Map map, IntVec3 cell)
		{
			var room = cell.GetRoom(map);
			if (room == null || room.IsHuge || room.Fogged || room.IsDoorway)
				return null;
			return room;
		}

		public bool OnWall()
		{
			var edifice = Map?.edificeGrid[Position];
			return edifice != null && edifice is Building building;
		}

		public bool HasValidRoom()
		{
			var cell = Position + IntVec3.North.RotatedBy(Rotation);
			return GetValidRoom(Map, cell) != null;
		}
	}
}
