using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	public partial class Chainsaw : ThingWithComps
	{
		public Pawn pawn;
		public bool running;
		public float angle;
		public bool swinging;
		public int inactiveCounter;

		Sustainer idleSustainer;
		Sustainer workSustainer;

		CompRefuelable refuelable;
		CompBreakable breakable;

		public void Prepare()
		{
			angle = -1f;
			swinging = false;
			inactiveCounter = 0;
		}

		public void Cleanup()
		{
			pawn = null;
			angle = -1f;
			swinging = false;
			inactiveCounter = 0;

			StopMotor();
		}

		public void Drop()
		{
			_ = pawn.equipment.TryDropEquipment(this, out var _, pawn.Position);
		}

		public override void Tick()
		{
			base.Tick();

			var map = pawn?.Map;
			if (map == null)
				return;
			var pos = pawn.Position;

			if (running)
			{
				refuelable.ConsumeFuel(refuelable.ConsumptionRatePerTick * (workSustainer != null ? 10f : 1f));
				if (refuelable.HasFuel == false)
					StopMotor();
			}

			var cells = GenAdj.AdjacentCellsAround.Select(c => c + pos).ToArray();
			var affectedCells = new List<Thing>[8];
			var grid = map.thingGrid;
			for (var i = 0; i < 8; i++)
				affectedCells[i] = grid.ThingsListAt(cells[i]);
			var hostileCells = new List<Pawn>[8];
			for (var i = 0; i < 8; i++)
				hostileCells[i] = affectedCells[i].OfType<Pawn>().Where(victim => victim.HostileTo(pawn)).ToList();

			var maxEmpty = MaxEmptyCells(hostileCells);
			if (maxEmpty <= 4)
			{
				Drop();
				return;
			}

			if (running == false)
				return;

			var noHostiles = (maxEmpty == 8);
			if (noHostiles)
			{
				inactiveCounter++;
				if (inactiveCounter > 60)
				{
					workSustainer?.End();
					workSustainer = null;
					swinging = false;
				}
				return;
			}

			workSustainer ??= CustomDefs.ChainsawWork.TrySpawnSustainer(SoundInfo.InMap(pawn, MaintenanceType.None));

			swinging = true;
			inactiveCounter = 0;
			if (angle == -1f)
				angle = pawn.Rotation.AsAngle;

			var idx = (int)angle / 45;
			var things = affectedCells[idx].Where(victim => (pawn.DrawPos - victim.DrawPos).MagnitudeHorizontalSquared() <= 2f);

			var victim = things.OfType<Pawn>().FirstOrDefault();
			if (victim != null)
			{
				Slaughter(victim);
				return;
			}
			var building = things.OfType<Building>().FirstOrDefault();
			if (building != null)
			{
				_ = building.TakeDamage(new DamageInfo(DamageDefOf.Crush, 80f));
				Drop();
				return;
			}

			var nextIndex = -1;
			for (var i = 1; i <= 4 && nextIndex == -1; i++)
			{
				var leftIdx = (idx - i + 8) % 8;
				var rightIdx = (idx + i + 8) % 8;

				var leftHostiles = hostileCells[leftIdx].Count;
				var rightHostiles = hostileCells[rightIdx].Count;

				if (leftHostiles > 0)
					nextIndex = leftIdx;
				if (rightHostiles > 0 && rightHostiles > leftHostiles)
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

		public override IEnumerable<Gizmo> GetGizmos()
		{
			if (pawn == null)
				foreach (var gizmo in base.GetGizmos().OfType<Gizmo>())
					yield return gizmo;
			else if (comps != null)
				foreach (var comp in comps)
				{
					if (comp is CompForbiddable || comp is CompRefuelable)
						continue;
					foreach (var gizmo in comp.CompGetGizmosExtra().OfType<Gizmo>())
						yield return gizmo;
				}

			if (pawn != null && pawn.Drafted)
			{
				var hasFuel = refuelable.HasFuel;
				var disabled = pawn == null || pawn.MentalStateDef != null || hasFuel == false || breakable.broken;
				var description = (running ? "ChainsawOff" : "ChainsawOn").Translate();
				if (disabled)
				{
					if (breakable.broken)
						description = "ChainsawBroken".Translate();
					else
						description = (hasFuel ? "ChainsawDisabled" : "ChainsawNoFuel").Translate();
				}
				yield return new Command_Action
				{
					defaultDesc = description,
					disabled = disabled,
					icon = Constants.Chainsaw[running ? 1 : 0],
					hotKey = KeyBindingDefOf.Misc6,
					action = delegate ()
					{
						running = !running;
						if (running)
							StartMotor(false);
						else
							StopMotor();
					}
				};
			}

			yield return new Gizmo_RefuelableFuelStatus { refuelable = refuelable };
		}

		//

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_References.Look(ref pawn, "pawn");
			Scribe_Values.Look(ref running, "on");
			Scribe_Values.Look(ref angle, "angle");
			Scribe_Values.Look(ref swinging, "swinging");
			Scribe_Values.Look(ref inactiveCounter, "inactiveCounter");

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				factionInt = Faction.OfPlayer;
				if (running)
					LongEventHandler.ExecuteWhenFinished(() => StartMotor(true));
			}
		}

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);

			refuelable = GetComp<CompRefuelable>();
			breakable = GetComp<CompBreakable>();

			factionInt = Faction.OfPlayer;
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

		//

		public void StartMotor(bool fromLoad)
		{
			if (fromLoad == false)
				CustomDefs.ChainsawStart.PlayOneShot(SoundInfo.InMap(pawn));
			idleSustainer ??= CustomDefs.ChainsawIdle.TrySpawnSustainer(SoundInfo.InMap(pawn, MaintenanceType.None));
			running = true;
		}

		public void StopMotor()
		{
			idleSustainer?.End();
			idleSustainer = null;

			workSustainer?.End();
			workSustainer = null;

			running = false;
			swinging = false;
		}

		static int MaxEmptyCells(List<Pawn>[] cells)
		{
			var idx = 7;
			while (idx >= 0)
			{
				if (cells[idx]?.Any() ?? false)
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
					maxEmpty = Mathf.Max(maxEmpty, empty);
					empty = 0;
				}
				idx = (idx + 1) % 8;
			}
			return Mathf.Max(maxEmpty, empty);
		}

		void Slaughter(Pawn victim)
		{
			var head = victim?.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined, null, null).FirstOrDefault((BodyPartRecord x) => x.def == BodyPartDefOf.Head);
			if (head != null)
			{
				var mat = GetHead(victim);
				var pos = victim.DrawPos;
				pos.y = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead);
				victim.Map?.GetComponent<TickManager>()?.victimHeads.Add(new VictimHead()
				{
					t = 0,
					material = mat,
					alpha = 1f,
					position = pos,
					quat = Quaternion.AngleAxis(victim.Rotation.AsAngle, Vector3.up),
					rotAngle = Rand.Range(-10f, 10f)
				});

				var part2 = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, victim, null);
				part2.IsFresh = true;
				part2.lastInjury = HediffDefOf.Shredded;
				part2.Part = head;
				victim.health.hediffSet.AddDirect(part2, null, null);
			}
			CustomDefs.Crush.PlayOneShot(SoundInfo.InMap(victim));
			_ = FilthMaker.TryMakeFilth(victim.Position, victim.Map, ThingDefOf.Human.race.BloodDef, 4, FilthSourceFlags.None, true);
			victim.Kill(null);
			if (victim is not Zombie)
				Drop();
		}

		static Material GetHead(Pawn victim)
		{
			var renderTexture = RenderTexture.GetTemporary(128, 128, 32, RenderTextureFormat.ARGB32);
			Find.PawnCacheRenderer.RenderPawn(victim, renderTexture, new Vector3(0, 0, 0.4f), 1.75f, 0f, Rot4.South, true, false, true, false, true, default, null, null, false);
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