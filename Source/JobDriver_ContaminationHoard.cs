using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_ContaminationHoard : JobDriver
	{
		public enum State
		{
			findThing,
			moveToThing,
			moveToStorage,
		}

		public List<IntVec3> storage = new();
		public Room room;
		public Thing thing;
		public IntVec3 cell = IntVec3.Invalid;
		public State state;

		public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

		public override IEnumerable<Toil> MakeNewToils()
		{
			yield return new Toil()
			{
				initAction = new Action(InitAction),
				tickAction = new Action(TickAction),
				defaultCompleteMode = ToilCompleteMode.Never
			};
		}

		public override void ExposeData()
		{
			base.ExposeData();
		}

		Thing FindNextThing()
		{
			var things = Map.regionGrid.allRooms
				.Where(r => r.IsHuge == false && r != room)
				.SelectMany(room => room.ContainedAndAdjacentThings)
				.Where(t => t.def.EverHaulable && pawn.holdingOwner.CanAcceptAnyOf(t, true) && pawn.CanReach(t, PathEndMode.ClosestTouch, Danger.Deadly))
				.ToList();
			if (things.Count == 0)
				return null;
			return things.RandomElementByWeightWithDefault(thing => thing.MarketValue, 0f);
		}

		void InitAction()
		{
			var ownedBed = Map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>()
				.FirstOrDefault(bed => bed.GetAssignedPawn() == pawn);
			room = ownedBed?.GetRoom(RegionType.Set_All);
			if (room == null)
			{
				EndJobWith(JobCondition.Succeeded);
				return;
			}

			storage = room.Cells.Where(cell => cell.Standable(Map) && pawn.CanReach(cell, PathEndMode.ClosestTouch, Danger.Deadly)).ToList();
			if (storage.Count == 0)
			{
				EndJobWith(JobCondition.Succeeded);
				return;
			}

			state = State.findThing;
		}

		void TickAction()
		{
			if (state == State.findThing)
			{
				cell = storage.RandomElement();
				storage.Remove(cell);
				thing = FindNextThing();
				if (thing == null || storage.Count == 0)
				{
					EndJobWith(JobCondition.Succeeded);
					return;
				}
				pawn.pather.StartPath(thing.Position, PathEndMode.ClosestTouch);
				state = State.moveToThing;
			}
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();

			switch (state)
			{
				case State.moveToThing:
				{
					int num = pawn.carryTracker.AvailableStackSpace(thing.def);
					int num2 = Mathf.Min(num, thing.stackCount);
					if (pawn.carryTracker.TryStartCarry(thing, num2, true) > 0)
					{
						pawn.pather.StartPath(cell, PathEndMode.ClosestTouch);
						state = State.moveToStorage;
					}
					else
						state = State.findThing;
					break;
				}
				case State.moveToStorage:
				{
					var slotGroup = pawn.Map.haulDestinationManager.SlotGroupAt(cell);
					if (slotGroup != null && slotGroup.Settings.AllowedToAccept(pawn.carryTracker.CarriedThing))
						pawn.Map.designationManager.TryRemoveDesignationOn(pawn.carryTracker.CarriedThing, DesignationDefOf.Haul);
					if (!pawn.carryTracker.TryDropCarriedThing(cell, ThingPlaceMode.Direct, out _, null))
						_ = pawn.carryTracker.TryDropCarriedThing(cell, ThingPlaceMode.Direct, out _);
					state = State.findThing;
					break;
				}
			}
		}

		public override void Notify_PatherFailed()
		{
			base.Notify_PatherFailed();
		}
	}
}
