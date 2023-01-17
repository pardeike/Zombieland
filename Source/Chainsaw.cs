using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	public class Chainsaw : ThingWithComps
	{
		static readonly Mesh headMesh = MeshPool.humanlikeHeadSet.MeshAt(Rot4.South);

		public Pawn pawn;
		public float fuel;
		public float angle;
		public bool active;
		public int inactiveCounter;

		public List<ZombieHead> zombieHeads;
		Sustainer idleSustainer;
		Sustainer workSustainer;

		public class ZombieHead
		{
			public int t;
			public Material material;
			public float alpha;
			public Vector3 position;
			public Quaternion quat;
			public float rotAngle;

			public bool Tick()
			{
				if (t++ > 100)
					return true;

				quat = Quaternion.AngleAxis(rotAngle, Vector3.up) * quat;
				if (t >= 90)
					alpha -= 0.1f;
				return false;
			}

			public Vector3 Position
			{
				// https://www.desmos.com/calculator/taxvx1poha
				get
				{
					var x = t / 100f;
					const float a = 9;
					const float b = -0.38f;
					const float c = 8f;
					var d = x - b;
					var y = Mathf.Abs(Mathf.Sin(a * (x - b)) / d / d / d) / c;
					return position + new Vector3(x * Mathf.Sign(rotAngle), 0, y);
				}
			}
		}

		public void Prepare()
		{
			angle = -1f;
			active = false;
			inactiveCounter = 0;
		}

		public void Cleanup()
		{
			pawn = null;
			angle = -1f;
			active = false;
			inactiveCounter = 0;

			idleSustainer?.End();
			idleSustainer = null;

			workSustainer?.End();
			workSustainer = null;
		}

		public override void Draw()
		{
			zombieHeads ??= new();
			foreach (var head in zombieHeads)
			{
				var mat = new Material(head.material);
				mat.color = new Color(mat.color.r, mat.color.g, mat.color.b, head.alpha);
				GraphicToolbox.DrawScaledMesh(headMesh, mat, head.Position, head.quat, 0.7f, 0.7f);
			}
		}

		public override void Tick()
		{
			base.Tick();

			zombieHeads ??= new();
			var heads = zombieHeads.ToArray();
			foreach (var head in heads)
				if (head.Tick())
					_ = zombieHeads.Remove(head);

			var map = pawn?.Map;
			if (map == null)
				return;
			var pos = pawn.Position;

			var cells = GenAdj.AdjacentCellsAround.Select(c => c + pos).ToArray();
			var zombieCells = new List<Zombie>[8];
			var grid = map.thingGrid;
			for (var i = 0; i < 8; i++)
				zombieCells[i] = grid.ThingsAt(cells[i]).OfType<Zombie>().ToList();

			var maxEmpty = MaxEmptyCells(zombieCells);
			if (maxEmpty <= 4)
			{
				_ = pawn.equipment.TryDropEquipment(this, out var _, pos);
				return;
			}

			var noZombies = (maxEmpty == 8);
			if (noZombies)
			{
				inactiveCounter++;
				if (inactiveCounter > 60)
				{
					workSustainer?.End();
					workSustainer = null;
					active = false;
				}
				return;
			}

			workSustainer ??= CustomDefs.ChainsawWork.TrySpawnSustainer(SoundInfo.InMap(pawn, MaintenanceType.None));

			active = true;
			inactiveCounter = 0;
			if (angle == -1f)
				angle = pawn.Rotation.AsAngle;

			var idx = (int)angle / 45;
			var zombie = zombieCells[idx].FirstOrDefault(zombie => (pawn.DrawPos - zombie.DrawPos).MagnitudeHorizontalSquared() <= 2f);
			if (zombie != null)
				SlaughterZombie(zombie);

			var nextIndex = -1;
			for (var i = 1; i <= 4 && nextIndex == -1; i++)
			{
				var leftIdx = (idx - i + 8) % 8;
				var rightIdx = (idx + i + 8) % 8;

				var leftZombies = zombieCells[leftIdx].Count;
				var rightZombies = zombieCells[rightIdx].Count;

				if (leftZombies > 0)
					nextIndex = leftIdx;
				if (rightZombies > 0 && rightZombies > leftZombies)
					nextIndex = rightIdx;
			}
			if (nextIndex == -1)
				return;

			var nextAngle = nextIndex * 45F + 22.5f;
			var meleeSkill = pawn.skills.GetSkill(SkillDefOf.Melee).Level;
			angle = Mathf.MoveTowardsAngle(angle, nextAngle, GenMath.LerpDouble(0, 20, 2f, 20f, meleeSkill));
			if (angle >= 360)
				angle -= 360;
			else if (angle < 0)
				angle += 360;
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

		//

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_References.Look(ref pawn, "pawn");
			Scribe_Values.Look(ref fuel, "fuel");
			Scribe_Values.Look(ref angle, "angle");
			Scribe_Values.Look(ref active, "active");
			Scribe_Values.Look(ref inactiveCounter, "inactiveCounter");

			if (Scribe.mode == LoadSaveMode.PostLoadInit && active)
				idleSustainer ??= CustomDefs.ChainsawIdle.TrySpawnSustainer(SoundInfo.InMap(pawn, MaintenanceType.None));
		}

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			Prepare();
		}

		public override void Notify_Equipped(Pawn pawn)
		{
			this.pawn = pawn;
			idleSustainer ??= CustomDefs.ChainsawIdle.TrySpawnSustainer(SoundInfo.InMap(pawn, MaintenanceType.None));
			CustomDefs.ChainsawStart.PlayOneShot(SoundInfo.InMap(pawn));
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

		//

		static int MaxEmptyCells(List<Zombie>[] cells)
		{
			var idx = 7;
			while (idx >= 0)
			{
				if (cells[idx].Any())
					break;
				idx--;
			}
			if (idx == -1)
				return 8;

			var empty = 0;
			var maxEmpty = 0;
			for (var j = 0; j < 8; j++)
			{
				if (cells[idx].Count == 0)
					empty++;
				else
				{
					maxEmpty = Math.Max(maxEmpty, empty);
					empty = 0;
				}
				idx = (idx + 1) % 8;
			}
			return Math.Max(maxEmpty, empty);
		}

		void SlaughterZombie(Zombie zombie)
		{
			var head = zombie?.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined, null, null).FirstOrDefault((BodyPartRecord x) => x.def == BodyPartDefOf.Head);
			if (head != null)
			{
				var mat = GetZombieHead(zombie);
				zombieHeads ??= new();
				var pos = zombie.DrawPos;
				pos.y = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead);
				zombieHeads.Add(new ZombieHead()
				{
					t = 0,
					material = mat,
					alpha = 1f,
					position = pos,
					quat = Quaternion.AngleAxis(zombie.Rotation.AsAngle, Vector3.up),
					rotAngle = Rand.Range(-10f, 10f)
				});

				var part2 = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, zombie, null);
				part2.IsFresh = true;
				part2.lastInjury = HediffDefOf.Shredded;
				part2.Part = head;
				zombie.health.hediffSet.AddDirect(part2, null, null);
			}
			_ = FilthMaker.TryMakeFilth(zombie.Position, zombie.Map, ThingDefOf.Human.race.BloodDef, 4, FilthSourceFlags.None, true);
			zombie.Kill(null);
		}

		static Material GetZombieHead(Zombie zombie)
		{
			var renderTexture = RenderTexture.GetTemporary(128, 128, 32, RenderTextureFormat.ARGB32);
			Find.PawnCacheRenderer.RenderPawn(zombie, renderTexture, new Vector3(0, 0, 0.4f), 1.75f, 0f, Rot4.South, true, false, true, false, true, default, null, null, false);
			Graphics.Blit(Constants.blood, renderTexture, MaterialPool.MatFrom(ShaderDatabase.Wound));
			var texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false);
			RenderTexture.active = renderTexture;
			texture.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
			texture.Apply();
			RenderTexture.active = null;
			RenderTexture.ReleaseTemporary(renderTexture);

			return MaterialPool.MatFrom(new MaterialRequest(texture, ShaderDatabase.Mote, Color.white));
		}
	}
}