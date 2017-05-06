using Harmony;
using RimWorld;
using System;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public static class ZombieGenerator
	{
		static Traverse GiveShuffledBioTo = Traverse.Create(typeof(PawnBioAndNameGenerator)).Method("GiveShuffledBioTo", new Type[] { typeof(Pawn), typeof(FactionDef), typeof(string) });

		public static Backstory MinimalBackstory()
		{
			var bs = new Backstory();
			bs.baseDesc = "Unknown";
			bs.bodyTypeMale = BodyType.Male;
			bs.bodyTypeFemale = BodyType.Female;
			bs.bodyTypeGlobal = BodyType.Undefined;
			return bs;
		}

		public static Zombie GeneratePawn(Map map)
		{
			var kindDef = ZombieDefOf.Zombie;

			var pawn = (Zombie)ThingMaker.MakeThing(kindDef.race, null);

			pawn.gender = Rand.Bool ? Gender.Male : Gender.Female;
			var factionDef = ZombieDefOf.Zombies;
			var faction = FactionUtility.DefaultFactionFrom(factionDef);
			pawn.kindDef = kindDef;
			pawn.SetFactionDirect(faction);

			PawnComponentsUtility.CreateInitialComponents(pawn);

			pawn.pather = new Zombie_PathFollower(pawn);
			Traverse.Create(pawn).Field("drawer").SetValue(new Zombie_DrawTracker(pawn));

			pawn.health = new Zombie_HealthTracker(pawn);
			pawn.caller = new Zombie_CallTracker(pawn);
			pawn.apparel = new Zombie_ApparelTracker(pawn);
			pawn.filth = new Zombie_FilthTracker(pawn);
			pawn.relations = new Zombie_RelationsTracker(pawn);
			pawn.interactions = new Zombie_InteractionsTracker(pawn);
			pawn.needs = new Zombie_NeedsTracker(pawn);
			pawn.guest = new Zombie_GuestTracker(pawn);
			pawn.records = new Zombie_RecordsTracker(pawn);
			pawn.natives = new Zombie_NativeVerbs(pawn);
			pawn.stances = new Zombie_StanceTracker(pawn);
			pawn.verbTracker = new Zombie_VerbTracker(pawn);
			pawn.mindState = new Zombie_MindState(pawn);
			pawn.carryTracker = new Zombie_CarryTracker(pawn);
			pawn.equipment = new Zombie_EquipmentTracker(pawn);
			pawn.skills = new Zombie_SkillTracker(pawn);
			pawn.inventory = new Zombie_InventoryTracker(pawn);
			pawn.drafter = new Zombie_DraftController(pawn);
			pawn.ageTracker = new Zombie_AgeTracker(pawn)
			{
				AgeBiologicalTicks = ((long)(Rand.Range(0, 0x9c4) * 3600000f)) + Rand.Range(0, 0x36ee80),
				AgeChronologicalTicks = pawn.ageTracker.AgeBiologicalTicks,
				BirthAbsTicks = GenTicks.TicksAbs - pawn.ageTracker.AgeBiologicalTicks
			};

			pawn.needs.SetInitialLevels();

			pawn.Name = new NameSingle("Zombie"); // PawnBioAndNameGenerator.GeneratePawnName(pawn, NameStyle.Full);
			pawn.story.childhood = MinimalBackstory();
			if (pawn.ageTracker.AgeBiologicalYearsFloat >= 20f)
				pawn.story.adulthood = pawn.story.childhood;
			// GiveShuffledBioTo.GetValue(pawn, factionDef, null);
			// PawnBioAndNameGenerator.GiveAppropriateBioAndNameTo(pawn, "Z");

			pawn.story.melanin = 0.01f * Rand.Range(10, 90);
			pawn.story.crownType = CrownType.Average;

			var graphicPath = GraphicDatabaseHeadRecords.GetHeadRandom(pawn.gender, pawn.story.SkinColor, pawn.story.crownType).GraphicPath;
			Traverse.Create(pawn.story).Field("headGraphicPath").SetValue(graphicPath);

			pawn.story.hairColor = PawnHairColors.RandomHairColor(pawn.story.SkinColor, pawn.ageTracker.AgeBiologicalYears);
			pawn.story.hairDef = PawnHairChooser.RandomHairDefFor(pawn, factionDef);
			pawn.story.bodyType = (pawn.gender != Gender.Female) ? BodyType.Male : BodyType.Female;

			var request = new PawnGenerationRequest(pawn.kindDef);
			PawnApparelGenerator.GenerateStartingApparelFor(pawn, request);
			pawn.apparel.WornApparel.Do(apparel =>
			{
				apparel.DrawColor = apparel.DrawColor.SaturationChanged(0.5f);
			});

			return pawn;
		}
	}
}