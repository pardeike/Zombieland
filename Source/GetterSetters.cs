using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using static Harmony.AccessTools;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public static class GetterSetters
	{
		// Dialogs
		public static FieldRef<Listing, float> curXByRef = FieldRefAccess<Listing, float>("curX");

		// HediffComp_Zombie_TendDuration
		public static FieldRef<HediffComp_TendDuration, float> totalTendQualityByRef = FieldRefAccess<HediffComp_TendDuration, float>("totalTendQuality");

		// Patches
		public static FieldRef<DangerWatcher, int> clastColonistHarmedTickByRef = FieldRefAccess<DangerWatcher, int>("lastColonistHarmedTick");
		public static FieldRef<Faction, List<FactionRelation>> relationsByRef = FieldRefAccess<Faction, List<FactionRelation>>("relations");
		public static FieldRef<Dialog_ModSettings, Mod> selModByRef = FieldRefAccess<Dialog_ModSettings, Mod>("selMod");

		// TickManager
		public static FieldRef<PawnDestinationReservationManager, Dictionary<Faction, PawnDestinationReservationManager.PawnDestinationSet>> reservedDestinationsByRef
			= FieldRefAccess<PawnDestinationReservationManager, Dictionary<Faction, PawnDestinationReservationManager.PawnDestinationSet>>("reservedDestinations");

		// Tools
		public static FieldRef<GraphicData, Graphic> cachedGraphicByRef = FieldRefAccess<GraphicData, Graphic>("cachedGraphic");
		public static FieldRef<Thing, Graphic> graphicIntByRef = FieldRefAccess<Thing, Graphic>("graphicInt");

		// ZombieDamageFlasher
		public static FieldRef<DamageFlasher, int> lastDamageTickByRef = FieldRefAccess<DamageFlasher, int>("lastDamageTick");

		// ZombieGenerator
		public static FieldRef<Pawn_PathFollower, LocalTargetInfo> destinationByRef = FieldRefAccess<Pawn_PathFollower, LocalTargetInfo>("destination");
		public static FieldRef<Pawn_StoryTracker, string> headGraphicPathByRef = FieldRefAccess<Pawn_StoryTracker, string>("headGraphicPath");

		static GetterSetters()
		{
		}
	}
}