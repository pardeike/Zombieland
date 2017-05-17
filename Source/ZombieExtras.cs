using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	/*
	[HarmonyPatch(typeof(Pawn_PathFollower))]
	[HarmonyPatch("StartPath")]
	public class Pawn_PathFollower_StartPath_Patch
	{
		static void StartPath(LocalTargetInfo dest, PathEndMode peMode,
			ref LocalTargetInfo destination, ref Pawn pawn, ref PawnPath curPath, ref Boolean moving)
		{
			if (dest.HasThing && dest.ThingDestroyed) return;
			if (destination == dest) return;

			peMode = PathEndMode.OnCell;
			destination = dest;

			if (!dest.HasThing && (pawn.Map.pawnDestinationManager.DestinationReservedFor(pawn) != dest.Cell))
				pawn.Map.pawnDestinationManager.UnreserveAllFor(pawn);

			if (curPath != null)
				curPath.ReleaseToPool();
			curPath = null;
			moving = true;
		}

		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var replacement = AccessTools.Method(MethodBase.GetCurrentMethod().DeclaringType, method.Name);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method, replacement);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_PathFollower))]
	[HarmonyPatch("PatherTick")]
	public class Pawn_PathFollower_PatherTick_Patch
	{
		static readonly Traverse costToMoveIntoCell = Traverse.Create(typeof(Pawn_PathFollower)).Method("CostToMoveIntoCell");

		static void PatherTick(ref Pawn pawn, ref float nextCellCostLeft, ref float nextCellCostTotal, ref IntVec3 nextCell,
			ref LocalTargetInfo destination, ref PawnPath curPath, ref int lastMovedTick, ref IntVec3 lastPathedTargetPosition)
		{
			if (curPath == null)
			{
				lastPathedTargetPosition = destination.Cell;
				// curPath = pawn.Map.pathFinder.FindPath(pawn.Position, destination, pawn, PathEndMode.OnCell);
				curPath = new PawnPath();
				curPath.AddNode(pawn.Position);
				curPath.AddNode(destination.Cell);
				curPath.SetupFound((float)1f);
			}

			lastMovedTick = Find.TickManager.TicksGame;
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

				if (pawn.Position == destination.Cell)
				{
					pawn.jobs.curDriver.Notify_PatherArrived();
					return;
				}

				if (curPath.NodesLeftCount == 1)
				{
					pawn.jobs.curDriver.Notify_PatherArrived();
					return;
				}

				nextCell = curPath.ConsumeNextNode();

				var num = (float)costToMoveIntoCell.GetValue<int>();
				nextCellCostTotal = num;
				nextCellCostLeft = num;
			}
		}

		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var replacement = AccessTools.Method(MethodBase.GetCurrentMethod().DeclaringType, method.Name);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method, replacement);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_DrawTracker))]
	[HarmonyPatch("DrawTrackerTick")]
	public class Pawn_DrawTracker_DrawTrackerTick_Patch
	{
		public void DrawTrackerTick(Pawn pawn, JitterHandler jitterer, PawnRotator rotator, PawnRenderer renderer)
		{
			if (pawn.Spawned && ((Current.ProgramState != ProgramState.Playing) || Find.CameraDriver.CurrentViewRect.ExpandedBy(3).Contains(pawn.Position)))
			{
				jitterer.JitterHandlerTick();
				rotator.PawnRotatorTick();
				renderer.RendererTick();
			}
		}

		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var replacement = AccessTools.Method(MethodBase.GetCurrentMethod().DeclaringType, method.Name);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_StanceTracker))]
	[HarmonyPatch("StanceTrackerTick")]
	public class Pawn_StanceTracker_StanceTrackerTick_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}
	*/

	/*[HarmonyPatch(typeof(Pawn_MindState))]
	[HarmonyPatch("MindStateTick")]
	public class Pawn_MindState_MindStateTick_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}*/

	[HarmonyPatch(typeof(PawnCollisionTweenerUtility))]
	[HarmonyPatch("PawnCollisionPosOffsetFor")]
	public class Pawn_DrawTracker_DrawTrackerTick_Patch
	{
		static bool Prefix(Pawn pawn, ref Vector3 __result)
		{
			if (!(pawn is Zombie)) return true;
			__result = Vector3.zero;
			return false;
		}
	}


	/*[HarmonyPatch(typeof(Pawn_DrawTracker))]
	[HarmonyPatch("DrawTrackerTick")]
	public class Pawn_DrawTracker_DrawTrackerTick_Patch
	{
		public void DrawTrackerTick(
			Pawn pawn,
			JitterHandler jitterer,
			PawnFootprintMaker footprintMaker,
			PawnBreathMoteMaker breathMoteMaker,
			PawnLeaner leaner,
			PawnRotator rotator,
			PawnRenderer renderer)
		{
			if (pawn.Spawned && ((Current.ProgramState != ProgramState.Playing) || Find.CameraDriver.CurrentViewRect.ExpandedBy(3).Contains(pawn.Position)))
			{
				jitterer.JitterHandlerTick();
				footprintMaker.FootprintMakerTick();
				breathMoteMaker.BreathMoteMakerTick();
				leaner.LeanerTick();
				rotator.PawnRotatorTick();
				renderer.RendererTick();
			}
		}

		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var replacement = AccessTools.Method(MethodBase.GetCurrentMethod().DeclaringType, method.Name);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}*/

	[HarmonyPatch(typeof(Pawn_CarryTracker))]
	[HarmonyPatch("CarryHandsTick")]
	public class Pawn_CarryTracker_CarryHandsTick_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_EquipmentTracker))]
	[HarmonyPatch("EquipmentTrackerTick")]
	public class Pawn_EquipmentTracker_EquipmentTrackerTick_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_SkillTracker))]
	[HarmonyPatch("SkillsTick")]
	public class Pawn_SkillTracker_SkillsTick_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_InventoryTracker))]
	[HarmonyPatch("InventoryTrackerTick")]
	public class Pawn_InventoryTracker_InventoryTrackerTick_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_AgeTracker))]
	[HarmonyPatch("AgeTick")]
	public class Pawn_AgeTracker_AgeTick_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_NeedsTracker))]
	[HarmonyPatch("AllNeeds", PropertyMethod.Getter)]
	public class Pawn_NeedsTracker_AllNeeds_Patch
	{
		static List<Need> AllNeeds
		{
			get
			{
				return new List<Need>();
			}
		}

		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var replacement = AccessTools.Method(MethodBase.GetCurrentMethod().DeclaringType, method.Name);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method, replacement);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_NeedsTracker))]
	[HarmonyPatch("NeedsTrackerTick")]
	public class Pawn_NeedsTracker_NeedsTrackerTick_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_NeedsTracker))]
	[HarmonyPatch("AddOrRemoveNeedsAsAppropriate")]
	public class Pawn_NeedsTracker_AddOrRemoveNeedsAsAppropriate_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_InteractionsTracker))]
	[HarmonyPatch("InteractionsTrackerTick")]
	public class Pawn_InteractionsTracker_InteractionsTrackerTick_Patch
	{
		// public new bool TryInteractWith(Pawn recipient, InteractionDef intDef) { return false; }
		// public new void StartSocialFight(Pawn otherPawn) { }
		// public new float SocialFightChance(InteractionDef interaction, Pawn initiator) { return 0f; }
		// public new bool InteractedTooRecentlyToInteract() { return true; }
		// public new bool CheckSocialFightStart(InteractionDef interaction, Pawn initiator) { return false; }

		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_RelationsTracker))]
	[HarmonyPatch("SocialTrackerTick")]
	public class Pawn_RelationsTracker_SocialTrackerTick_Patch
	{
		//public new float CompatibilityWith(Pawn otherPawn) { return 0f; }
		//public new float ConstantPerPawnsPairCompatibilityOffset(int otherPawnID) { return 0f; }
		//public new bool DirectRelationExists(PawnRelationDef def, Pawn otherPawn) { return false; }
		//public new DirectPawnRelation GetDirectRelation(PawnRelationDef def, Pawn otherPawn) { return null; }
		//public new Pawn GetFirstDirectRelationPawn(PawnRelationDef def, Predicate<Pawn> predicate) { return null; }
		//public new float GetFriendDiedThoughtPowerFactor(int opinion) { return 0f; }
		//public new float GetRivalDiedThoughtPowerFactor(int opinion) { return 0f; }
		//public new void Notify_PawnKidnapped() { }
		//internal void Notify_PawnKilled(DamageInfo? dinfo, Map mapBeforeDeath) { }
		//public new void Notify_PawnSold(Pawn playerNegotiator) { }
		//public new void Notify_RescuedBy(Pawn rescuer) { }
		//public new string OpinionExplanation(Pawn other) { return String.Empty; }
		//public new int OpinionOf(Pawn other) { return 0; }
		//public new void RemoveDirectRelation(DirectPawnRelation relation) { }
		//public new void RemoveDirectRelation(PawnRelationDef def, Pawn otherPawn) { }
		//public new float SecondaryRomanceChanceFactor(Pawn otherPawn) { return 0f; }
		//public new bool TryRemoveDirectRelation(PawnRelationDef def, Pawn otherPawn) { return false; }
		//public new bool RelatedToAnyoneOrAnyoneRelatedToMe
		//{
		//	get
		//	{
		//		return false;
		//	}
		//}

		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_ApparelTracker))]
	[HarmonyPatch("ApparelTrackerTick")]
	public class Pawn_ApparelTracker_ApparelTrackerTick_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_CallTracker))]
	[HarmonyPatch("CallTrackerTick")]
	public class Pawn_CallTracker_CallTrackerTick_Patch
	{
		// public new void DoCall() { }
		// public new void Notify_DidMeleeAttack() { }
		// public new void Notify_InAggroMentalState() { }
		// public new void Notify_Released() { }

		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_HealthTracker))]
	[HarmonyPatch("HealthTick")]
	public class Pawn_HealthTracker_HealthTick_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(ImmunityHandler))]
	[HarmonyPatch("ImmunityHandlerTick")]
	public class ImmunityHandler_ImmunityHandlerTick_Patch
	{
		// public new float DiseaseContractChanceFactor(HediffDef diseaseDef, BodyPartRecord part) { return 0f; }
		// public new bool ImmunityRecordExists(HediffDef def) { return true; }

		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_GuestTracker))]
	[HarmonyPatch("GuestTrackerTick")]
	public class Pawn_GuestTracker_GuestTrackerTick_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_RecordsTracker))]
	[HarmonyPatch("AddTo")]
	public class Pawn_RecordsTracker_AddTo_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}

	[HarmonyPatch(typeof(Pawn_RecordsTracker))]
	[HarmonyPatch("Increment")]
	public class Pawn_RecordsTracker_Increment_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, MethodBase method, IEnumerable<CodeInstruction> instructions)
		{
			var conditions = Tools.NotZombieInstructions(generator, method);
			var transpiler = Tools.GenerateReplacementCallTranspiler(conditions, method);
			return transpiler(generator, instructions);
		}
	}
}