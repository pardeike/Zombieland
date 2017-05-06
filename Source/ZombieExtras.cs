using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class PawnKindDef_Zombie : PawnKindDef { }
	public class ThingDef_Zombie : ThingDef
	{
		public static Type type = typeof(ThingDef_Zombie);
	}

	//

	public class ZombieCorpse : Corpse
	{
		public int vanishAfter;
		public static Type type = typeof(ZombieCorpse);

		public override void SpawnSetup(Map map)
		{
			base.SpawnSetup(map);
			InnerPawn.Rotation = Rot4.Random;
			vanishAfter = Age + GenTicks.SecondsToTicks(60);
			ForbidUtility.SetForbidden(this, false, false);

			GetComps<CompRottable>()
				.Select(comp => comp.props)
				.OfType<CompProperties_Rottable>()
				.Cast<CompProperties_Rottable>()
				.Do(rotcomp =>
				{
					rotcomp.daysToRotStart = 1f * GenTicks.SecondsToTicks(10) / 60000f;
					rotcomp.daysToDessicated = 1f * GenTicks.SecondsToTicks(30) / 60000f;
				});
		}

		public override void DrawExtraSelectionOverlays()
		{
		}

		public override void DrawGUIOverlay()
		{
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.LookValue(ref vanishAfter, "vanishAfter");
		}
	}

	//

	public class Zombie_DrawTracker : Pawn_DrawTracker
	{
		public Zombie_DrawTracker(Pawn zombie) : base(zombie) { }

		public new void DrawTrackerTick() { }
	}

	public class Zombie_PathFollower : Pawn_PathFollower
	{
		static readonly Traverse destination = Traverse.Create(typeof(Pawn_PathFollower)).Field("destination");
		static readonly Traverse costToMoveIntoCell = Traverse.Create(typeof(Pawn_PathFollower)).Method("CostToMoveIntoCell");

		public Zombie_PathFollower(Pawn zombie) : base(zombie) { }

		public new void PatherTick()
		{
			if (nextCellCostLeft > 0f)
			{
				nextCellCostLeft -= nextCellCostTotal / 450f;
			}
			else
			{
				pawn.Position = nextCell;

				// TODO: make clamors work
				// 
				// cellsUntilClamor--;
				// if (cellsUntilClamor <= 0)
				// {
				// 	GenClamor.DoClamor(pawn, 7f, ClamorType.Movement);
				// 	cellsUntilClamor = 12;
				// }

				if (pawn.BodySize > 0.9f)
					pawn.Map.snowGrid.AddDepth(pawn.Position, -0.001f);

				if (pawn.Position == destination.GetValue<LocalTargetInfo>().Cell)
				{
					pawn.jobs.curDriver.Notify_PatherArrived();
				}
				else
				{
					if (curPath.NodesLeftCount == 0)
					{
						pawn.jobs.curDriver.Notify_PatherArrived();
						return;
					}
				}
				nextCell = curPath.ConsumeNextNode();

				var num = (float)costToMoveIntoCell.GetValue<int>();
				nextCellCostTotal = num;
				nextCellCostLeft = num;
			}
		}
	}

	public class Zombie_StanceTracker : Pawn_StanceTracker
	{
		public Zombie_StanceTracker(Pawn zombie) : base(zombie) { }

		public new void StanceTrackerTick() { }
	}

	public class Zombie_MindState : Pawn_MindState
	{
		public Zombie_MindState(Pawn zombie) : base(zombie) { }

		public new void MindStateTick() { }
	}

	public class Zombie_VerbTracker : VerbTracker
	{
		public Zombie_VerbTracker(Pawn zombie) : base(zombie) { }

		public new void VerbsTick() { }
	}

	public class Zombie_CarryTracker : Pawn_CarryTracker
	{
		public Zombie_CarryTracker(Pawn zombie) : base(zombie) { }

		public new void CarryHandsTick() { }
	}

	public class Zombie_EquipmentTracker : Pawn_EquipmentTracker
	{
		public Zombie_EquipmentTracker(Pawn zombie) : base(zombie) { }

		public new void EquipmentTrackerTick() { }
	}

	public class Zombie_SkillTracker : Pawn_SkillTracker
	{
		public Zombie_SkillTracker(Pawn zombie) : base(zombie) { }

		public new void SkillsTick() { }
	}

	public class Zombie_InventoryTracker : Pawn_InventoryTracker
	{
		public Zombie_InventoryTracker(Pawn zombie) : base(zombie) { }

		public new void InventoryTrackerTick() { }
	}

	public class Zombie_DraftController : Pawn_DraftController
	{
		public Zombie_DraftController(Pawn zombie) : base(zombie) { }

		public new void DraftControllerTick() { }
	}

	public class Zombie_AgeTracker : Pawn_AgeTracker
	{
		public Zombie_AgeTracker(Pawn zombie) : base(zombie) { }

		public new void AgeTick() { }
	}

	public class Zombie_NeedsTracker : Pawn_NeedsTracker
	{
		public Zombie_NeedsTracker(Pawn zombie) : base(zombie) { }

		public new void AddOrRemoveNeedsAsAppropriate() { }
		public new void NeedsTrackerTick() { }
		public new List<Need> AllNeeds
		{
			get
			{
				return new List<Need>();
			}
		}
		public new T TryGetNeed<T>() where T : Need { return null; }
		public new Need TryGetNeed(NeedDef def) { return null; }
	}

	public class Zombie_InteractionsTracker : Pawn_InteractionsTracker
	{
		public Zombie_InteractionsTracker(Pawn zombie) : base(zombie) { }

		public new bool TryInteractWith(Pawn recipient, InteractionDef intDef) { return false; }
		public new void StartSocialFight(Pawn otherPawn) { }
		public new float SocialFightChance(InteractionDef interaction, Pawn initiator) { return 0f; }
		public new void InteractionsTrackerTick() { }
		public new bool InteractedTooRecentlyToInteract() { return true; }
		public new bool CheckSocialFightStart(InteractionDef interaction, Pawn initiator) { return false; }
	}

	public class Zombie_RelationsTracker : Pawn_RelationsTracker
	{
		public Zombie_RelationsTracker(Pawn zombie) : base(zombie) { }

		public new float CompatibilityWith(Pawn otherPawn) { return 0f; }
		public new float ConstantPerPawnsPairCompatibilityOffset(int otherPawnID) { return 0f; }
		public new bool DirectRelationExists(PawnRelationDef def, Pawn otherPawn) { return false; }
		public new DirectPawnRelation GetDirectRelation(PawnRelationDef def, Pawn otherPawn) { return null; }
		public new Pawn GetFirstDirectRelationPawn(PawnRelationDef def, Predicate<Pawn> predicate) { return null; }
		public new float GetFriendDiedThoughtPowerFactor(int opinion) { return 0f; }
		public new float GetRivalDiedThoughtPowerFactor(int opinion) { return 0f; }
		public new void Notify_PawnKidnapped() { }
		internal void Notify_PawnKilled(DamageInfo? dinfo, Map mapBeforeDeath) { }
		public new void Notify_PawnSold(Pawn playerNegotiator) { }
		public new void Notify_RescuedBy(Pawn rescuer) { }
		public new string OpinionExplanation(Pawn other) { return String.Empty; }
		public new int OpinionOf(Pawn other) { return 0; }
		public new void RemoveDirectRelation(DirectPawnRelation relation) { }
		public new void RemoveDirectRelation(PawnRelationDef def, Pawn otherPawn) { }
		public new float SecondaryRomanceChanceFactor(Pawn otherPawn) { return 0f; }
		public new void SocialTrackerTick() { }
		public new bool TryRemoveDirectRelation(PawnRelationDef def, Pawn otherPawn) { return false; }
		public new bool RelatedToAnyoneOrAnyoneRelatedToMe
		{
			get
			{
				return false;
			}
		}
	}

	public class Zombie_FilthTracker : Pawn_FilthTracker
	{
		public Zombie_FilthTracker(Pawn zombie) : base(zombie) { }

		public new void GainFilth(ThingDef filthDef) { }
		public new void GainFilth(ThingDef filthDef, IEnumerable<string> sources) { }
		public new void Notify_EnteredNewCell() { }
	}

	public class Zombie_ApparelTracker : Pawn_ApparelTracker
	{
		public Zombie_ApparelTracker(Pawn pawn) : base(pawn) { }

		public new void ApparelTrackerTick() { }
		public new void ApparelTrackerTickRare() { }
		public new void Notify_LostBodyPart() { }
		public new void Notify_PawnKilled(DamageInfo? dinfo) { }
	}

	public class Zombie_CallTracker : Pawn_CallTracker
	{
		public Zombie_CallTracker(Pawn pawn) : base(pawn) { }

		public new void CallTrackerTick() { }
		public new void DoCall() { }
		public new void Notify_DidMeleeAttack() { }
		public new void Notify_InAggroMentalState() { }
		public new void Notify_Released() { }
	}

	public class Zombie_ImmunityHandler : ImmunityHandler
	{
		public Zombie_ImmunityHandler(Pawn pawn) : base(pawn) { }

		public new float DiseaseContractChanceFactor(HediffDef diseaseDef, BodyPartRecord part) { return 0f; }
		internal void ImmunityHandlerTick() { }
		public new bool ImmunityRecordExists(HediffDef def) { return true; }
	}

	public class Zombie_HealthTracker : Pawn_HealthTracker
	{
		public Zombie_HealthTracker(Pawn pawn) : base(pawn)
		{
			immunity = new Zombie_ImmunityHandler(pawn);
		}

		protected new void TryDropBloodFilth() { }
		public new bool HasHediffsNeedingTend(bool forAlert) { return false; }
		public new bool HasHediffsNeedingTendByColony(bool forAlert) { return false; }
		public new void DropBloodFilth() { }
	}


	public class Zombie_GuestTracker : Pawn_GuestTracker
	{
		public Zombie_GuestTracker(Pawn pawn) : base(pawn) { }

		public new void GuestTrackerTick() { }
	}

	public class Zombie_RecordsTracker : Pawn_RecordsTracker
	{
		public Zombie_RecordsTracker(Pawn pawn) : base(pawn) { }

		public new void RecordsTick() { }
		public new void AddTo(RecordDef def, float value) { }
		public new void Increment(RecordDef def) { }
	}

	public class Zombie_NativeVerbs : Pawn_NativeVerbs
	{
		public Zombie_NativeVerbs(Pawn pawn) : base(pawn) { }

		public new void NativeVerbsTick() { }
	}
}