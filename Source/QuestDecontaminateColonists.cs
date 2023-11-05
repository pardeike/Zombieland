using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class QuestNode_DecontaminateColonists : QuestNode
	{
		[NoTranslate] public SlateRef<string> inSignalEnable;
		[NoTranslate] public SlateRef<string> outSignalComplete;
		[NoTranslate] public SlateRef<string> outSignalColonistsDied;

		public SlateRef<Thing> shuttle;
		public SlateRef<Pawn> factionToSendTo;
		public SlateRef<int> returnColonistsInTicks;

		public override void RunInt()
		{
			var slate = QuestGen.slate;
			var text = QuestGenUtility.HardcodedSignalWithQuestID(inSignalEnable.GetValue(slate)) ?? QuestGen.slate.Get<string>("inSignal", null, false);
			var questPart = new QuestPart_DecontaminateColonists()
			{
				inSignalEnable = text,
				shuttle = shuttle.GetValue(slate),
				factionToSendTo = factionToSendTo.GetValue(slate)?.Faction,
				returnColonistsInTicks = returnColonistsInTicks.GetValue(slate),
				returnMap = slate.Get<Map>("map", null, false).Parent
			};

			if (outSignalComplete.GetValue(slate).NullOrEmpty() == false)
				questPart.outSignalsCompleted.Add(QuestGenUtility.HardcodedSignalWithQuestID(outSignalComplete.GetValue(slate)));

			if (outSignalColonistsDied.GetValue(slate).NullOrEmpty() == false)
				questPart.outSignalColonistsDied = QuestGenUtility.HardcodedSignalWithQuestID(outSignalColonistsDied.GetValue(slate));

			QuestGen.quest.AddPart(questPart);
			QuestGen.quest.TendPawnsWithMedicine(ThingDefOf.MedicineIndustrial, true, null, shuttle.GetValue(slate), text);
		}

		public override bool TestRunInt(Slate slate) => factionToSendTo.GetValue(slate) != null;
	}

	public class QuestNode_GetRandomAlliedFactionLeader : QuestNode
	{
		[NoTranslate] public SlateRef<string> storeAs;

		public static Pawn GetAlliedFactionLeader()
		{
			var allies = Find.FactionManager.GetFactions(false, false, true, TechLevel.Medieval, false)
				.Where(faction => faction.PlayerRelationKind == FactionRelationKind.Ally);
			return allies.RandomElementWithFallback()?.leader;
		}

		public override void RunInt()
		{
			var slate = QuestGen.slate;
			slate.Set(storeAs.GetValue(slate), GetAlliedFactionLeader(), false);
		}

		public override bool TestRunInt(Slate slate)
		{
			var pawn = GetAlliedFactionLeader();
			slate.Set(storeAs.GetValue(slate), pawn, false);
			return pawn != null;
		}
	}

	public class QuestPart_DecontaminateColonists : QuestPartActivable
	{
		public Thing shuttle;
		public Faction factionToSendTo;
		public int returnColonistsInTicks = -1;
		public MapParent returnMap;
		public string outSignalColonistsDied;

		private int returnColonistsOnTick;
		private Pawn subject;

		public int ReturnPawnsInDurationTicks => Mathf.Max(returnColonistsOnTick - GenTicks.TicksGame, 0);

		public override void Enable(SignalArgs receivedArgs)
		{
			base.Enable(receivedArgs);
			var compTransporter = shuttle.TryGetComp<CompTransporter>();
			if (factionToSendTo == null || compTransporter == null)
				return;
			subject = compTransporter.innerContainer.First() as Pawn;
			returnColonistsOnTick = GenTicks.TicksGame + returnColonistsInTicks;
		}

		public override string DescriptionPart
		{
			get
			{
				if (State == QuestPartState.Disabled || subject == null)
					return null;
				// we reuse that translation key, it has no special "lent" text in it
				return "PawnsLent".Translate(subject.LabelShort, ReturnPawnsInDurationTicks.ToStringTicksToDays("0.0"));
			}
		}

		public override void QuestPartTick()
		{
			base.QuestPartTick();
			if (Find.TickManager.TicksGame >= enableTick + returnColonistsInTicks)
				Complete();
		}

		public override void Complete(SignalArgs signalArgs)
		{
			var map = returnMap?.Map ?? Find.AnyPlayerHomeMap;
			if (map == null)
				return;

			if (Constants.CONTAMINATION)
				subject.ClearContamination();
			base.Complete(new SignalArgs(new LookTargets(subject).Named("SUBJECT")));
			if (factionToSendTo != null && factionToSendTo == Faction.OfEmpire)
			{
				var thing = ThingMaker.MakeThing(ThingDefOf.Shuttle, null);
				thing.SetFaction(Faction.OfEmpire, null);
				var transportShip = TransportShipMaker.MakeTransportShip(TransportShipDefOf.Ship_Shuttle, new[] { subject }, thing);
				transportShip.ArriveAt(DropCellFinder.GetBestShuttleLandingSpot(map, Faction.OfEmpire), map.Parent);
				transportShip.AddJobs(new ShipJobDef[]
				{
					ShipJobDefOf.Unload,
					ShipJobDefOf.FlyAway
				});
				return;
			}
			DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(map), map, new[] { subject });
		}

		private void ReturnDead(Corpse corpse)
		{
			var anyPlayerHomeMap = Find.AnyPlayerHomeMap;
			if (anyPlayerHomeMap != null)
				DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(anyPlayerHomeMap), anyPlayerHomeMap, Gen.YieldSingle(corpse));
		}

		public override void Notify_PawnKilled(Pawn pawn, DamageInfo? dinfo)
		{
			if (subject == null)
				return;

			var building_Grave = pawn.ownership?.AssignedGrave;
			var corpse = pawn.MakeCorpse(building_Grave, null);
			ReturnDead(corpse);
			if (outSignalColonistsDied.NullOrEmpty() == false)
				Find.SignalManager.SendSignal(new Signal(outSignalColonistsDied));
		}

		public override void DoDebugWindowContents(Rect innerRect, ref float curY)
		{
			if (State != QuestPartState.Enabled)
				return;

			var rect = new Rect(innerRect.x, curY, 500f, 25f);
			if (Widgets.ButtonText(rect, "End " + ToString()))
				Complete();
			curY += rect.height + 4f;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_References.Look(ref shuttle, "shuttle", false);
			Scribe_References.Look(ref factionToSendTo, "factionToSendTo", false);
			Scribe_Values.Look(ref returnColonistsInTicks, "returnColonistsInTicks", 0, false);
			Scribe_Values.Look(ref returnColonistsOnTick, "colonistsReturnOnTick", 0, false);
			Scribe_References.Look(ref subject, "subject", false);
			Scribe_References.Look(ref returnMap, "returnMap", false);
			Scribe_Values.Look(ref outSignalColonistsDied, "outSignalColonistsDied", null, false);
		}
	}
}