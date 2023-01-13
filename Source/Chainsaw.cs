using System.Collections.Generic;
using Verse;

namespace ZombieLand
{
	public class Chainsaw : ThingWithComps
	{
		public Pawn pawn;
		public bool isOn;
		public float fuel;
		public float currentAngle;
		public float destinationAngle;
		public bool damaged;

		public void Prepare()
		{
			currentAngle = 180f;
			destinationAngle = currentAngle;
			damaged = false;
		}

		public void Cleanup()
		{
			pawn = null;
		}

		public override void Draw()
		{
			base.Draw();
			// todo
		}

		public override void Tick()
		{
			base.Tick();
			// todo
		}

		public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
		{
			base.PreApplyDamage(ref dinfo, out absorbed);
			// todo
		}

		public override IEnumerable<Gizmo> GetGizmos()
		{
			// todo
			yield break;
		}

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			Prepare();
		}

		public override void Notify_Equipped(Pawn pawn)
		{
			this.pawn = pawn;
			base.Notify_Equipped(pawn);
		}
		public override void Notify_Unequipped(Pawn pawn)
		{
			Cleanup();
			base.Notify_Unequipped(pawn);
		}

		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			Cleanup();
			base.DeSpawn(mode);
		}

		public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
		{
			Cleanup();
			base.Destroy(mode);
		}
	}