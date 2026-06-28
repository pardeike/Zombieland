using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class QuestNode_CreateDecontaminationPickupTransporter : QuestNode
	{
		const string DecontaminationTransporterDefName = "ZombieLand_DecontaminationTransportPod";

		[NoTranslate] public SlateRef<string> inSignal;
		[NoTranslate] public SlateRef<string> storeAs;
		public SlateRef<Pawn> factionToSendTo;

		static ThingDef PickupTransporterDef
		{
			get
			{
				return DefDatabase<ThingDef>.GetNamedSilentFail(DecontaminationTransporterDefName);
			}
		}

		public override void RunInt()
		{
			var slate = QuestGen.slate;
			var map = slate.Get<Map>("map", null, false);
			var text = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? QuestGen.slate.Get<string>("inSignal", null, false);
			var storeAsValue = storeAs.GetValue(slate);
			if (storeAsValue.NullOrEmpty())
				storeAsValue = "pickupShipThing";

			var transporter = ThingMaker.MakeThing(PickupTransporterDef);
			transporter.SetFaction(Faction.OfPlayer, null);
			slate.Set(storeAsValue, transporter, false);
			QuestGen.quest.AddPart(new QuestPart_DecontaminationPickupTransporter()
			{
				inSignal = text,
				inSignalEnable = text,
				pickupTransporter = transporter,
				mapParent = map.Parent,
				requiredColonistCount = 1
			});
		}

		public override bool TestRunInt(Slate slate)
		{
			return slate.Get<Map>("map", null, false) != null
				&& factionToSendTo.GetValue(slate) != null
				&& PickupTransporterDef != null;
		}
	}

	public class QuestPart_DecontaminationPickupTransporter : QuestPartActivable
	{
		public Thing pickupTransporter;
		public MapParent mapParent;
		public string inSignal;
		public int requiredColonistCount = 1;

		private bool spawned;
		private bool sentSatisfied;
		private bool cleanupAfterSatisfiedSignal;
		private bool pickupTransporterWasSpawned;

		public Thing PickupTransporterForTest => pickupTransporter;
		public bool SpawnedForTest => spawned;
		public bool SentSatisfiedForTest => sentSatisfied;

		public override void Notify_QuestSignalReceived(Signal signal)
		{
			base.Notify_QuestSignalReceived(signal);
			if (signal.tag == inSignal && spawned == false && sentSatisfied == false)
				SpawnTransporter();
		}

		public override void QuestPartTick()
		{
			base.QuestPartTick();
			if (cleanupAfterSatisfiedSignal)
			{
				CleanupAfterSending();
				cleanupAfterSatisfiedSignal = false;
			}
			if (spawned == false || sentSatisfied || pickupTransporter == null || pickupTransporter.Destroyed)
				return;

			var transporter = pickupTransporter.TryGetComp<CompTransporter>();
			var pawns = transporter?.innerContainer.OfType<Pawn>()
				.Where(pawn => pawn.Faction == Faction.OfPlayer && pawn.RaceProps.Humanlike)
				.ToList();
			if (pawns == null || pawns.Count < requiredColonistCount)
				return;

			var subject = pawns[0];
			sentSatisfied = true;
			QuestUtility.SendQuestTargetSignals(pickupTransporter.questTags, "SentSatisfied", new SignalArgs(new LookTargets(subject).Named("SUBJECT")));
			cleanupAfterSatisfiedSignal = true;
		}

		void SpawnTransporter()
		{
			var map = mapParent?.Map ?? Find.AnyPlayerHomeMap;
			if (map == null || pickupTransporter == null || pickupTransporter.Destroyed)
				return;

			var cell = DropCellFinder.TradeDropSpot(map);
			if (IsUsableSpawnCell(cell, map) == false
				&& CellFinderLoose.TryGetRandomCellWith(candidate => IsUsableSpawnCell(candidate, map), map, 1000, out var fallbackCell))
			{
				cell = fallbackCell;
			}
			if (IsUsableSpawnCell(cell, map) == false)
				return;

			GenSpawn.Spawn(pickupTransporter, cell, map, WipeMode.Vanish);
			spawned = true;
		}

		static bool IsUsableSpawnCell(IntVec3 cell, Map map)
		{
			return cell.IsValid
				&& cell.InBounds(map)
				&& cell.Standable(map)
				&& cell.Fogged(map) == false;
		}

		void CleanupAfterSending()
		{
			var transporter = pickupTransporter?.TryGetComp<CompTransporter>();
			if (transporter != null)
			{
				var loadedPawns = transporter.innerContainer.OfType<Pawn>().ToList();
				foreach (var pawn in loadedPawns)
				{
					transporter.innerContainer.Remove(pawn);
					Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
				}
			}
			if (pickupTransporter != null && pickupTransporter.Spawned)
				pickupTransporter.DeSpawn(DestroyMode.Vanish);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			if (Scribe.mode == LoadSaveMode.Saving)
				pickupTransporterWasSpawned = pickupTransporter != null && pickupTransporter.Spawned;
			Scribe_Values.Look(ref pickupTransporterWasSpawned, "pickupTransporterWasSpawned", false, false);
			if (pickupTransporterWasSpawned)
				Scribe_References.Look(ref pickupTransporter, "pickupTransporter", false);
			else
				Scribe_Deep.Look(ref pickupTransporter, "pickupTransporter");
			Scribe_References.Look(ref mapParent, "mapParent", false);
			Scribe_Values.Look(ref inSignal, "inSignal", null, false);
			Scribe_Values.Look(ref requiredColonistCount, "requiredColonistCount", 1, false);
			Scribe_Values.Look(ref spawned, "spawned", false, false);
			Scribe_Values.Look(ref sentSatisfied, "sentSatisfied", false, false);
			Scribe_Values.Look(ref cleanupAfterSatisfiedSignal, "cleanupAfterSatisfiedSignal", false, false);
		}
	}

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
			subject = compTransporter.innerContainer.FirstOrDefault() as Pawn;
			if (subject == null)
				return;
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
			if (State == QuestPartState.Enabled
				&& returnColonistsInTicks >= 0
				&& subject != null
				&& Find.TickManager.TicksGame >= returnColonistsOnTick)
			{
				Complete();
			}
		}

		public override void Complete(SignalArgs signalArgs)
		{
			var map = returnMap?.Map ?? Find.AnyPlayerHomeMap;
			if (map == null || subject == null)
				return;

			if (Constants.CONTAMINATION)
				ContaminationManager.Instance.GrantDecontaminationImmunity(subject, ContaminationManager.DecontaminationImmunityTicks());
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
			if (subject == null || pawn != subject)
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
