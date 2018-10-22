using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public class ZombieGenerator
	{
		static List<ThingStuffPair> allApparelPairs;

		static Color HairColor()
		{
			var num3 = Rand.Value;
			if (num3 < 0.25f)
				return new Color(0.2f, 0.2f, 0.2f);

			if (num3 < 0.5f)
				return new Color(0.31f, 0.28f, 0.26f);

			if (num3 < 0.75f)
				return new Color(0.25f, 0.2f, 0.15f);

			return new Color(0.3f, 0.2f, 0.1f);
		}

		static readonly Dictionary<string, IntVec2> eyeOffsets = new Dictionary<string, IntVec2>() {
			{ "Female_Average_Normal", new IntVec2(11, -5) },
			{ "Female_Average_Pointy", new IntVec2(11, -5) },
			{ "Female_Average_Wide", new IntVec2(11, -6) },
			{ "Female_Narrow_Normal", new IntVec2(10, -7) },
			{ "Female_Narrow_Pointy", new IntVec2(8, -8) },
			{ "Female_Narrow_Wide", new IntVec2(9, -8) },
			{ "Male_Average_Normal", new IntVec2(15, -7) },
			{ "Male_Average_Pointy", new IntVec2(14, -6) },
			{ "Male_Average_Wide", new IntVec2(15, -7) },
			{ "Male_Narrow_Normal", new IntVec2(9, -8) },
			{ "Male_Narrow_Pointy", new IntVec2(8, -8) },
			{ "Male_Narrow_Wide", new IntVec2(10, -8) }
		};
		static IntVec2 SideEyeOffset(string headPath)
		{
			return eyeOffsets[headPath];
		}

		public static Zombie GeneratePawn(ZombieType overwriteType)
		{
			var thing = ThingMaker.MakeThing(ZombieDefOf.Zombie.race, null);
			var zombie = thing as Zombie;
			if (zombie == null)
			{
				Log.Error("ThingMaker.MakeThing(ZombieDefOf.Zombie.race, null) unexpectedly returned " + thing);
				return null;
			}

			var bodyType = PrepareZombieType(zombie, overwriteType);

			zombie.kindDef = ZombieDefOf.Zombie;
			zombie.SetFactionDirect(FactionUtility.DefaultFactionFrom(ZombieDefOf.Zombies));

			PawnComponentsUtility.CreateInitialComponents(zombie);
			zombie.health.hediffSet.Clear();

			var ageInYears = (long)Rand.Range(14, 130);
			zombie.ageTracker.AgeBiologicalTicks = (ageInYears * 3600000);
			zombie.ageTracker.AgeChronologicalTicks = zombie.ageTracker.AgeBiologicalTicks;
			zombie.ageTracker.BirthAbsTicks = GenTicks.TicksAbs - zombie.ageTracker.AgeBiologicalTicks;
			var idx = zombie.ageTracker.CurLifeStageIndex; // trigger calculations

			zombie.needs.SetInitialLevels();
			zombie.needs.mood = new Need_Mood(zombie);

			var name = PawnNameDatabaseSolid.GetListForGender((zombie.gender == Gender.Female) ? GenderPossibility.Female : GenderPossibility.Male).RandomElement();
			var n1 = name.First.Replace('s', 'z').Replace('S', 'Z');
			var n2 = name.Last.Replace('s', 'z').Replace('S', 'Z');
			var n3 = name.Nick.Replace('s', 'z').Replace('S', 'Z');
			zombie.Name = new NameTriple(n1, n3, n2);

			zombie.story.childhood = BackstoryDatabase.allBackstories
				.Where(kvp => kvp.Value.slot == BackstorySlot.Childhood)
				.RandomElement().Value;
			if (zombie.ageTracker.AgeBiologicalYearsFloat >= 20f)
				zombie.story.adulthood = BackstoryDatabase.allBackstories
				.Where(kvp => kvp.Value.slot == BackstorySlot.Adulthood)
				.RandomElement().Value;

			zombie.story.melanin = 0.01f * Rand.Range(10, 91);
			zombie.story.bodyType = bodyType;
			zombie.story.crownType = Rand.Bool ? CrownType.Average : CrownType.Narrow;

			zombie.story.hairColor = HairColor();
			zombie.story.hairDef = PawnHairChooser.RandomHairDefFor(zombie, ZombieDefOf.Zombies);

			if (ZombieSettings.Values.useCustomTextures)
			{
				var it = AssignNewCustomGraphics(zombie);
				while (it.MoveNext()) ;
			}

			zombie.Drawer.leaner = new ZombieLeaner(zombie);

			if (zombie.pather == null)
				zombie.pather = new Pawn_PathFollower(zombie);
			GetterSetters.destinationByRef(zombie.pather) = IntVec3.Invalid;

			return zombie;
		}

		public enum ZombieType
		{
			Random = -1,
			SuicideBomber = 0,
			ToxicSplasher = 1,
			TankyOperator = 2,
			Normal = 3
		}

		private static BodyTypeDef PrepareZombieType(Zombie zombie, ZombieType overwriteType)
		{
			var zombieTypeInitializers = new Pair<float, Func<BodyTypeDef>>[]
			{
				// suicide bomber
				new Pair<float, Func<BodyTypeDef>>(
					ZombieSettings.Values.suicideBomberChance / 2f,
					delegate
					{
						zombie.bombTickingInterval = 60f;
						zombie.lastBombTick = Find.TickManager.TicksAbs + Rand.Range(0, (int)zombie.bombTickingInterval);
						//
						zombie.gender = Gender.Male;
						return BodyTypeDefOf.Hulk;
					}
				),

				// toxic splasher
				new Pair<float, Func<BodyTypeDef>>(
					ZombieSettings.Values.toxicSplasherChance / 2f,
					delegate
					{
						zombie.isToxicSplasher = true;
						//
						switch (Rand.RangeInclusive(1, 3))
						{
							case 1:
								zombie.gender = Gender.Male;
								return BodyTypeDefOf.Male;
							case 2:
								zombie.gender = Gender.Female;
								return BodyTypeDefOf.Female;
							case 3:
								zombie.gender = Gender.Male;
								return BodyTypeDefOf.Thin;
						}
						return null;
					}
				),

				// tanky operator
				new Pair<float, Func<BodyTypeDef>>(
					ZombieSettings.Values.tankyOperatorChance / 3f,
					delegate
					{
						zombie.hasTankyShield = 1f;
						zombie.hasTankyHelmet = 1f;
						zombie.hasTankySuit = 1f;
						//
						zombie.gender = Gender.Male;
						return BodyTypeDefOf.Fat;
					}
				),

				// default ordinary zombie
				new Pair<float, Func<BodyTypeDef>>(
					float.MaxValue,
					delegate
					{
						switch (Rand.RangeInclusive(1, 4))
						{
							case 1:
								zombie.gender = Gender.Male;
								return BodyTypeDefOf.Male;
							case 2:
								zombie.gender = Gender.Female;
								return BodyTypeDefOf.Female;
							case 3:
								zombie.gender = Gender.Male;
								return BodyTypeDefOf.Thin;
							case 4:
								zombie.gender = Gender.Male;
								return BodyTypeDefOf.Fat;
						}
						return null;
					}
				)
			};

			if (overwriteType != ZombieType.Random)
			{
				var initializer = zombieTypeInitializers[(int)overwriteType];
				return initializer.Second();
			}

			var typeChance = Rand.Value;
			BodyTypeDef bodyType = null;
			foreach (var initializer in zombieTypeInitializers)
			{
				typeChance -= initializer.First;
				if (typeChance < 0f)
				{
					bodyType = initializer.Second();
					break;
				}
			}
			return bodyType;
		}

		static readonly string[] headShapes = { "Normal", "Pointy", "Wide" };
		public static IEnumerator AssignNewCustomGraphics(Zombie zombie)
		{
			var renderPrecedence = 0;
			var bodyPath = "Zombie/Naked_" + zombie.story.bodyType.ToString();
			var color = zombie.isToxicSplasher ? "toxic" : GraphicToolbox.RandomSkinColorString();
			yield return null;
			var bodyRequest = new GraphicRequest(typeof(VariableGraphic), bodyPath, ShaderDatabase.CutoutSkin, Vector2.one, Color.white, Color.white, null, renderPrecedence, new List<ShaderParameter>());
			yield return null;
			zombie.customBodyGraphic = new VariableGraphic { bodyColor = color };
			yield return null;
			zombie.customBodyGraphic.Init(VariableGraphic.minimal);
			yield return null;
			for (var i = 0; i < 4; i++)
			{
				var j = 0;
				var it = zombie.customBodyGraphic.InitIterativ(bodyRequest, i);
				while (it.MoveNext())
				{
					yield return null;
					j++;
				}
			}
			var headShape = zombie.hasTankyHelmet == 1f ? "Wide" : headShapes[Rand.Range(0, 3)];
			var headPath = "Zombie/" + zombie.gender + "_" + zombie.story.crownType + "_" + headShape;
			var headRequest = new GraphicRequest(typeof(VariableGraphic), headPath, ShaderDatabase.CutoutSkin, Vector2.one, Color.white, Color.white, null, renderPrecedence, new List<ShaderParameter>());
			yield return null;
			zombie.customHeadGraphic = new VariableGraphic { bodyColor = color };
			yield return null;
			zombie.customHeadGraphic.Init(VariableGraphic.minimal);
			yield return null;
			for (var i = 0; i < 4; i++)
			{
				var j = 0;
				var it = zombie.customHeadGraphic.InitIterativ(headRequest, i);
				while (it.MoveNext())
				{
					yield return null;
					j++;
				}
			}
			zombie.sideEyeOffset = SideEyeOffset(headPath.ReplaceFirst("Zombie/", ""));
			yield return null;
		}

		public static IEnumerator GenerateStartingApparelFor(Pawn zombie)
		{
			if (allApparelPairs == null)
				allApparelPairs = ThingStuffPair.AllWith((ThingDef td) => td.IsApparel && td.IsZombieDef() == false);
			yield return null;
			for (var i = 0; i < Rand.Range(0, 4); i++)
			{
				var pair = allApparelPairs
					.Where(ap => ap.thing.IsApparel && ap.thing != ThingDefOf.Apparel_ShieldBelt && ap.thing != ThingDefOf.Apparel_SmokepopBelt)
					.RandomElement();
				var apparel = (Apparel)ThingMaker.MakeThing(pair.thing, pair.stuff);
				PawnGenerator.PostProcessGeneratedGear(apparel, zombie);
				if (ApparelUtility.HasPartsToWear(zombie, apparel.def))
				{
					if (zombie.apparel.WornApparel.All(pa => ApparelUtility.CanWearTogether(pair.thing, pa.def, zombie.RaceProps.body)))
					{
						apparel.SetColor(Zombie.zombieColors[Rand.Range(0, Zombie.zombieColors.Length)].SaturationChanged(0.25f));
						zombie.apparel.Wear(apparel, false);
					}
				}
				yield return null;
			}
		}

		public static void SpawnZombie(IntVec3 cell, Map map, Action<Zombie> callback)
		{
			Find.CameraDriver.StartCoroutine(SpawnZombieIterativ(cell, map, callback));
		}

		public static IEnumerator SpawnZombieIterativ(IntVec3 cell, Map map, Action<Zombie> callback)
		{
			var sw = new Stopwatch();
			sw.Start();
			var thing = ThingMaker.MakeThing(ZombieDefOf.Zombie.race, null);
			yield return null;
			var zombie = thing as Zombie;
			var bodyType = PrepareZombieType(zombie, ZombieType.Random);
			zombie.kindDef = ZombieDefOf.Zombie;
			zombie.SetFactionDirect(FactionUtility.DefaultFactionFrom(ZombieDefOf.Zombies));
			yield return null;
			PawnComponentsUtility.CreateInitialComponents(zombie);
			yield return null;
			zombie.health.hediffSet.Clear();
			var ageInYears = (long)Rand.Range(14, 130);
			zombie.ageTracker.AgeBiologicalTicks = (ageInYears * 3600000);
			zombie.ageTracker.AgeChronologicalTicks = zombie.ageTracker.AgeBiologicalTicks;
			zombie.ageTracker.BirthAbsTicks = GenTicks.TicksAbs - zombie.ageTracker.AgeBiologicalTicks;
			var idx = zombie.ageTracker.CurLifeStageIndex; // trigger calculations
			yield return null;
			zombie.needs.SetInitialLevels();
			yield return null;
			zombie.needs.mood = new Need_Mood(zombie);
			yield return null;
			var name = PawnNameDatabaseSolid.GetListForGender((zombie.gender == Gender.Female) ? GenderPossibility.Female : GenderPossibility.Male).RandomElement();
			yield return null;
			var n1 = name.First.Replace('s', 'z').Replace('S', 'Z');
			var n2 = name.Last.Replace('s', 'z').Replace('S', 'Z');
			var n3 = name.Nick.Replace('s', 'z').Replace('S', 'Z');
			zombie.Name = new NameTriple(n1, n3, n2);
			yield return null;
			zombie.story.childhood = BackstoryDatabase.allBackstories
				.Where(kvp => kvp.Value.slot == BackstorySlot.Childhood)
				.RandomElement().Value;
			yield return null;
			if (zombie.ageTracker.AgeBiologicalYearsFloat >= 20f)
				zombie.story.adulthood = BackstoryDatabase.allBackstories
				.Where(kvp => kvp.Value.slot == BackstorySlot.Adulthood)
				.RandomElement().Value;
			yield return null;
			zombie.story.melanin = 0.01f * Rand.Range(10, 91);
			zombie.story.bodyType = bodyType;
			zombie.story.crownType = Rand.Bool ? CrownType.Average : CrownType.Narrow;
			zombie.story.hairColor = HairColor();
			zombie.story.hairDef = PawnHairChooser.RandomHairDefFor(zombie, ZombieDefOf.Zombies);
			yield return null;
			if (ZombieSettings.Values.useCustomTextures)
			{
				var it1 = AssignNewCustomGraphics(zombie);
				while (it1.MoveNext())
					yield return null;
			}
			zombie.Drawer.leaner = new ZombieLeaner(zombie);
			if (zombie.pather == null)
				zombie.pather = new Pawn_PathFollower(zombie);
			GetterSetters.destinationByRef(zombie.pather) = IntVec3.Invalid;
			yield return null;
			var graphicPath = GraphicDatabaseHeadRecords.GetHeadRandom(zombie.gender, zombie.story.SkinColor, zombie.story.crownType).GraphicPath;
			yield return null;
			GetterSetters.headGraphicPathByRef(zombie.story) = graphicPath;
			yield return null;
			var request = new PawnGenerationRequest(zombie.kindDef);
			yield return null;
			var it2 = GenerateStartingApparelFor(zombie);
			while (it2.MoveNext())
				yield return null;
			GenPlace.TryPlaceThing(zombie, cell, map, ThingPlaceMode.Direct);
			yield return null;
			zombie.Drawer.renderer.graphics.ResolveAllGraphics();
			yield return null;
			if (callback != null)
				callback(zombie);
		}
	}
}