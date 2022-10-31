namespace ZombieLand
{
	/*public class Gas : Thing
	{
		public int destroyTick;

		public float graphicRotation;

		public float graphicRotationSpeed;

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			while (true)
			{
				var gas = Position.GetGas(map);
				if (gas == null)
					break;
				gas.Destroy(DestroyMode.Vanish);
			}
			SpawnSetup(map, respawningAfterLoad);
			if (!respawningAfterLoad)
				destroyTick = Find.TickManager.TicksGame + def.gas.expireSeconds.RandomInRange.SecondsToTicks();
			graphicRotationSpeed = Rand.Range(-def.gas.rotationSpeed, def.gas.rotationSpeed) / 60f;
		}

		public override void Tick()
		{
			if (destroyTick <= Find.TickManager.TicksGame)
				Destroy(DestroyMode.Vanish);
			graphicRotation += graphicRotationSpeed;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look<int>(ref destroyTick, "destroyTick", 0, false);
		}
	}*/
}
