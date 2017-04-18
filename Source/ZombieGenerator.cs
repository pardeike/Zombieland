using Harmony;
using RimWorld;
using System.Reflection;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public static class ZombieGenerator
	{
		public static Zombie GeneratePawn(Map map)
		{
			var kindDef = DefDatabase<PawnKindDef>.GetNamed("Zombie");
			var pawn = (Zombie)ThingMaker.MakeThing(kindDef.race, null);
			pawn.gender = Rand.Bool ? Gender.Male : Gender.Female;

			var factionDef = FactionDef.Named("Zombies");
			var faction = FactionUtility.DefaultFactionFrom(factionDef);
			pawn.kindDef = kindDef;
			pawn.SetFactionDirect(faction);

			// pawn.relations = new Pawn_RelationsTracker(pawn);
			// pawn.natives = new Pawn_NativeVerbs(pawn);
			pawn.story = new Pawn_StoryTracker(pawn);
			pawn.jobs = new Pawn_JobTracker(pawn);
			pawn.filth = new Pawn_FilthTracker(pawn);
			pawn.apparel = new Pawn_ApparelTracker(pawn);
			pawn.meleeVerbs = new Pawn_MeleeVerbs(pawn);
			pawn.pather = new Pawn_PathFollower(pawn);
			pawn.health = new Pawn_HealthTracker(pawn);
			pawn.stances = new Pawn_StanceTracker(pawn);
			pawn.caller = new Pawn_CallTracker(pawn);
			pawn.ageTracker = new Pawn_AgeTracker(pawn);

			pawn.ageTracker.AgeBiologicalTicks = ((long)(Rand.Range(0, 0x9c4) * 3600000f)) + Rand.Range(0, 0x36ee80);
			pawn.ageTracker.AgeChronologicalTicks = pawn.ageTracker.AgeBiologicalTicks;
			pawn.ageTracker.BirthAbsTicks = GenTicks.TicksAbs - pawn.ageTracker.AgeBiologicalTicks;

			PawnComponentsUtility.CreateInitialComponents(pawn);
			pawn.needs.SetInitialLevels();
			PawnBioAndNameGenerator.GiveAppropriateBioAndNameTo(pawn, "Z");
			pawn.Name = PawnBioAndNameGenerator.GeneratePawnName(pawn);

			pawn.story.melanin = 0.01f * Rand.Range(10, 90);
			pawn.story.crownType = CrownType.Average;
			string graphicPath = GraphicDatabaseHeadRecords.GetHeadRandom(pawn.gender, pawn.story.SkinColor, pawn.story.crownType).GraphicPath;
			typeof(Pawn_StoryTracker).GetField("headGraphicPath", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pawn.story, graphicPath);
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