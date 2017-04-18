using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

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
		int vanishAfter;
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
					//rotcomp.rotDestroys = true;
					rotcomp.daysToDessicated = 1f * GenTicks.SecondsToTicks(30) / 60000f;
				});
		}

		public override void Destroy(DestroyMode mode)
		{
			if (InnerPawn == null) return;

			InnerPawn.inventory.DestroyAll(DestroyMode.Vanish);
			if (InnerPawn.apparel != null)
				InnerPawn.apparel.DestroyAll(DestroyMode.Vanish);

			base.Destroy(mode);

			if (Find.WorldPawns.Contains(InnerPawn))
				Find.WorldPawns.DiscardIfUnimportant(InnerPawn);
		}

		public override void DrawExtraSelectionOverlays()
		{
		}

		public override void DrawGUIOverlay()
		{
		}

		public bool ShouldVanish()
		{
			return Age >= this.vanishAfter;
		}
	}

	//

	public class Zombie_NeedsTracker : Pawn_NeedsTracker
	{
		public Zombie_NeedsTracker(Pawn zombie) : base(zombie)
		{
		}

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
		public Zombie_InteractionsTracker(Pawn zombie) : base(zombie)
		{
		}

		public new bool TryInteractWith(Pawn recipient, InteractionDef intDef) { return false; }
		public new void StartSocialFight(Pawn otherPawn) { }
		public new float SocialFightChance(InteractionDef interaction, Pawn initiator) { return 0f; }
		public new void InteractionsTrackerTick() { }
		public new bool InteractedTooRecentlyToInteract() { return true; }
		public new bool CheckSocialFightStart(InteractionDef interaction, Pawn initiator) { return false; }
	}

	public class Zombie_RelationsTracker : Pawn_RelationsTracker
	{
		public Zombie_RelationsTracker(Pawn zombie) : base(zombie)
		{
		}

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

	public class Zombie_PawnObserver : PawnObserver
	{
		public Zombie_PawnObserver(Pawn zombie) : base(zombie)
		{
		}

		public new void ObserverInterval() { }
	}

	public class Zombie_Need_Mood : Need_Mood
	{
		public Zombie_Need_Mood(Pawn zombie) : base(zombie)
		{
			observer = new PawnObserver(zombie);
		}
	}

	/*
	public class Zombie_PawnUIOverlay : PawnUIOverlay
	{
		public Zombie_PawnUIOverlay(Pawn zombie) : base(zombie)
		{
		}

		public new void DrawPawnGUIOverlay() { }
	}

	public class Zombie_PawnRenderer : PawnRenderer
	{
		public Zombie_PawnRenderer(Pawn zombie) : base(zombie)
		{
		}

		public new void RenderPawnAt(Vector3 drawLoc, RotDrawMode bodyDrawType)
		{
		}
	}

	public class Zombie_DrawTracker : Pawn_DrawTracker
	{
		public Zombie_DrawTracker(Pawn zombie) : base(zombie)
		{
			renderer = new PawnRenderer(zombie);
			ui = new Zombie_PawnUIOverlay(zombie);
		}
	}
	*/

	//

	public class Zombie : Pawn
	{
		public static Type type = typeof(Zombie);

		public bool isSniffing = false;

		public override void SpawnSetup(Map map)
		{
			base.SpawnSetup(map);
			needs = new Zombie_NeedsTracker(this);
			needs.mood = new Zombie_Need_Mood(this);
			interactions = new Zombie_InteractionsTracker(this);
			relations = new Zombie_RelationsTracker(this);
			//Traverse.Create(this).Field("drawer").SetValue(new Zombie_DrawTracker(this));
		}
	}
}