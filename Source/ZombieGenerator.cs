using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using static ZombieLand.Patches;

namespace ZombieLand
{
	public enum ZombieType
	{
		Random = -1,
		SuicideBomber = 0,
		ToxicSplasher = 1,
		TankyOperator = 2,
		Miner = 3,
		Electrifier = 4,
		Albino = 5,
		DarkSlimer = 6,
		Healer = 7,
		Normal = 8
	}

	public static class ZombieBaseValues
	{
		public static Color HairColor()
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

		private static readonly Dictionary<string, IntVec2> eyeOffsets = new Dictionary<string, IntVec2>() {
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

		public static IntVec2 SideEyeOffset(string headType)
		{
			if (eyeOffsets.TryGetValue(headType, out var vec))
				return vec;
			return default;
		}

		public static bool IsValidHeadPath(string headPath)
		{
			var parts = headPath.Split('/');
			if (parts.Length < 2)
				return false;
			return eyeOffsets.ContainsKey(parts.Last());
		}

		static BodyTypeDef SetRandomBody(Zombie zombie)
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

		public static readonly Pair<Func<float>, Func<Zombie, BodyTypeDef>>[] zombieTypeInitializers = new Pair<Func<float>, Func<Zombie, BodyTypeDef>>[]
		{
			// suicide bomber
			new Pair<Func<float>, Func<Zombie, BodyTypeDef>>(
				() => ZombieSettings.Values.suicideBomberChance,
				zombie =>
				{
					zombie.bombTickingInterval = 60f;
					zombie.lastBombTick = Find.TickManager.TicksAbs + Rand.Range(0, (int)zombie.bombTickingInterval);
					//
					zombie.gender = Gender.Male;
					return BodyTypeDefOf.Hulk;
				}
			),

			// toxic splasher
			new Pair<Func<float>, Func<Zombie, BodyTypeDef>>(
				() => ZombieSettings.Values.toxicSplasherChance,
				zombie =>
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
			new Pair<Func<float>, Func<Zombie, BodyTypeDef>>(
				() => ZombieSettings.Values.tankyOperatorChance,
				zombie =>
				{
					zombie.hasTankyShield = 1f;
					zombie.hasTankyHelmet = 1f;
					zombie.hasTankySuit = 1f;
					//
					zombie.gender = Gender.Male;
					return BodyTypeDefOf.Fat;
				}
			),

			// miner
			new Pair<Func<float>, Func<Zombie, BodyTypeDef>>(
				() => ZombieSettings.Values.minerChance,
				zombie =>
				{
					zombie.isMiner = true;
					return SetRandomBody(zombie);
				}
			),

			// electrifier
			new Pair<Func<float>, Func<Zombie, BodyTypeDef>>(
				() => ZombieSettings.Values.electrifierChance,
				zombie =>
				{
					zombie.isElectrifier = true;
					return SetRandomBody(zombie);
				}
			),

			// albino
			new Pair<Func<float>, Func<Zombie, BodyTypeDef>>(
				() => ZombieSettings.Values.albinoChance,
				zombie =>
				{
					zombie.isAlbino = true;
					zombie.gender = Rand.Bool ? Gender.Male : Gender.Female;
					return BodyTypeDefOf.Thin;
				}
			),

			// dark slimer
			new Pair<Func<float>, Func<Zombie, BodyTypeDef>>(
				() => ZombieSettings.Values.darkSlimerChance,
				zombie =>
				{
					zombie.isDarkSlimer = true;
					zombie.gender = Gender.Male;
					return BodyTypeDefOf.Fat;
				}
			),

			// healer
			new Pair<Func<float>, Func<Zombie, BodyTypeDef>>(
				() => ZombieSettings.Values.healerChance,
				zombie =>
				{
					zombie.isHealer = true;
					return SetRandomBody(zombie);
				}
			),

			// default ordinary zombie
			new Pair<Func<float>, Func<Zombie, BodyTypeDef>>(
				() => 1f
					- ZombieSettings.Values.suicideBomberChance
					- ZombieSettings.Values.toxicSplasherChance
					- ZombieSettings.Values.tankyOperatorChance
					- ZombieSettings.Values.minerChance
					- ZombieSettings.Values.electrifierChance
					- ZombieSettings.Values.albinoChance
					- ZombieSettings.Values.darkSlimerChance
					- ZombieSettings.Values.healerChance,
				zombie =>
				{
					return SetRandomBody(zombie);
				}
			)
		};
	}

	[StaticConstructorOnStartup]
	public static class ZombieGenerator
	{
		public static int ZombiesSpawning = 0;
		public static readonly List<BackstoryDef> childBackstories = DefDatabase<BackstoryDef>.AllDefs.Where(b => b.slot == BackstorySlot.Childhood).ToList();
		public static readonly List<BackstoryDef> adultBackstories = DefDatabase<BackstoryDef>.AllDefs.Where(b => b.slot == BackstorySlot.Adulthood).ToList();

		private static BodyTypeDef PrepareZombieType(Zombie zombie, ZombieType overwriteType)
		{
			Func<Zombie, BodyTypeDef> bodyType;
			Pair<Func<float>, Func<Zombie, BodyTypeDef>> initializer;

			if (overwriteType != ZombieType.Random)
			{
				initializer = ZombieBaseValues.zombieTypeInitializers[(int)overwriteType];
				bodyType = initializer.Second;
				return bodyType(zombie);
			}

			var success = GenCollection.TryRandomElementByWeight(ZombieBaseValues.zombieTypeInitializers, pair => pair.First(), out initializer);
			if (success == false)
			{
				Log.Error("GenCollection.TryRandomElementByWeight returned false");
				return null;
			}
			return initializer.Second(zombie);
		}

		public static string FixGlowingEyeOffset(Zombie zombie)
		{
			var headShape = zombie.hasTankyHelmet == 1f ? "Wide" : headShapes[Rand.Range(0, 3)];
			var headPath = "Zombie/" + zombie.gender + "_" + ("Average"/*zombie.story.crownType*/) + "_" + headShape;
			zombie.sideEyeOffset = ZombieBaseValues.SideEyeOffset(headPath.ReplaceFirst("Zombie/", ""));
			return headPath;
		}

		public static void AssignNewGraphics(Zombie zombie)
		{
			var it = AssignNewGraphicsIterator(zombie);
			while (it.MoveNext())
				;
		}

		static readonly string[] headShapes = { "Normal", "Pointy", "Wide" };
		static IEnumerator AssignNewGraphicsIterator(Zombie zombie)
		{
			zombie.Drawer.renderer.graphics.ResolveAllGraphics();
			yield return null;

			var headPath = FixGlowingEyeOffset(zombie);
			if (zombie.IsSuicideBomber)
				zombie.lastBombTick = Find.TickManager.TicksAbs + Rand.Range(0, (int)zombie.bombTickingInterval);

			if (ZombieSettings.Values.useCustomTextures)
			{
				var renderPrecedence = 0;
				var bodyPath = "Zombie/Naked_" + zombie.story.bodyType.ToString();
				var color = GraphicToolbox.RandomSkinColorString();
				Color? specialColor = null;
				if (zombie.isToxicSplasher)
				{
					color = "toxic";
					specialColor = Color.green;
				}
				if (zombie.isMiner)
				{
					color = "miner";
					specialColor = new Color(46 / 255f, 35 / 255f, 15 / 255f);
				}
				if (zombie.isElectrifier)
				{
					color = "electric";
					specialColor = new Color(0.196078431f, 0.470588235f, 0.470588235f);
				}
				if (zombie.isAlbino)
				{
					color = "albino";
					specialColor = Color.white;
				}
				if (zombie.isDarkSlimer)
				{
					color = "dark";
					specialColor = new Color(27 / 255f, 26 / 255f, 25 / 255f);
				}
				if (zombie.isHealer)
				{
					color = "healer";
					specialColor = Color.cyan;
				}
				yield return null;
				var bodyRequest = new GraphicRequest(typeof(VariableGraphic), bodyPath, ShaderDatabase.Cutout, Vector2.one, Color.white, Color.white, null, renderPrecedence, new List<ShaderParameter>(), null);
				yield return null;

				var maxStainPoints = ZombieStains.maxStainPoints;
				if (zombie.isMiner)
					maxStainPoints *= 2;
				if (zombie.isAlbino)
					maxStainPoints = 0;
				if (zombie.isMiner)
					maxStainPoints *= 4;
				if (zombie.isHealer)
					maxStainPoints = 0;

				var customBodyGraphic = new VariableGraphic { bodyColor = color };
				yield return null;
				customBodyGraphic.Init(VariableGraphic.minimal);
				yield return null;
				for (var i = 0; i < 4; i++)
				{
					var j = 0;
					var it = customBodyGraphic.InitIterativ(bodyRequest, i, maxStainPoints);
					while (it.MoveNext())
					{
						yield return null;
						j++;
					}
				}
				zombie.Drawer.renderer.graphics.nakedGraphic = customBodyGraphic;

				var headRequest = new GraphicRequest(typeof(VariableGraphic), headPath, ShaderDatabase.Cutout, Vector2.one, Color.white, Color.white, null, renderPrecedence, new List<ShaderParameter>(), null);
				yield return null;
				var customHeadGraphic = new VariableGraphic { bodyColor = color };
				yield return null;
				customHeadGraphic.Init(VariableGraphic.minimal);
				yield return null;
				for (var i = 0; i < 4; i++)
				{
					var j = 0;
					var it = customHeadGraphic.InitIterativ(headRequest, i, maxStainPoints);
					while (it.MoveNext())
					{
						yield return null;
						j++;
					}
				}
				//zombie.Drawer.renderer.graphics.headGraphic = customHeadGraphic;
				zombie.Drawer.renderer.graphics.headGraphic = zombie.story.headType.GetGraphic(specialColor ?? color.HexColor(), false, true);
			}

			yield return null;
		}

		static List<ThingStuffPair> _allApparelPairs;
		static List<ThingStuffPair> AllApparelPairs()
		{
			_allApparelPairs ??= ThingStuffPair.AllWith((ThingDef td) => td.IsApparel)
					.Where(pair =>
					{
						var def = pair.thing;
						if (def.IsApparel == false)
							return false;
						if (def.IsZombieDef())
							return false;
						if (def == ThingDefOf.Apparel_ShieldBelt)
							return false;
						if (def == ThingDefOf.Apparel_SmokepopBelt)
							return false;
						if (def.thingClass?.Name.Contains("ApparelHolographic") ?? false)
							return false; // SoS
						var path = def.apparel.wornGraphicPath;
						return path != null && path.Length > 0;
					})
					.ToList();
			return _allApparelPairs;
		}

		static bool CanWear(Zombie zombie, ThingDef thing)
		{
			if (thing == null)
				return false;

			if (zombie.isMiner && PawnApparelGenerator.IsHeadgear(thing))
				return false;

			return ApparelUtility.HasPartsToWear(zombie, thing);
		}

		static bool GraphicFileExist(Zombie zombie, ApparelProperties apparel)
		{
			var path = apparel.wornGraphicPath;
			if (apparel.LastLayer != ApparelLayerDefOf.Overhead)
				path += "_" + zombie.story.bodyType.defName;
			return ContentFinder<Texture2D>.Get(path + "_north", false) != null;
		}

		public static IEnumerator GenerateStartingApparelFor(Zombie zombie)
		{
			var wearableApparel = AllApparelPairs().Where(pair => CanWear(zombie, pair.thing)).ToList();
			yield return null;
			var possibleApparel = wearableApparel.Where(pair => GraphicFileExist(zombie, pair.thing.apparel)).ToList();
			yield return null;
			if (possibleApparel.Count > 0)
			{
				for (var i = 0; i < Rand.Range(0, 4); i++)
				{
					var pair = possibleApparel.SafeRandomElement();
					yield return null;
					var apparel = (Apparel)ThingMaker.MakeThing(pair.thing, pair.stuff);
					yield return null;
					PawnGenerator.PostProcessGeneratedGear(apparel, zombie);
					yield return null;
					if (ApparelUtility.HasPartsToWear(zombie, apparel.def))
					{
						if (zombie.apparel.WornApparel.All(pa => ApparelUtility.CanWearTogether(pair.thing, pa.def, zombie.RaceProps.body)))
						{
							var colorComp = apparel.GetComp<CompColorable>();
							if (colorComp != null)
								colorComp.SetColor(Zombie.zombieColors[Rand.Range(0, Zombie.zombieColors.Length)].SaturationChanged(0.25f));
							Graphic_Multi_Init_Patch.suppressError = true;
							Graphic_Multi_Init_Patch.textureError = false;
							try
							{
								zombie.apparel.Wear(apparel, false);
							}
							catch (Exception)
							{
							}
							if (Graphic_Multi_Init_Patch.textureError)
								zombie.apparel.Remove(apparel);
							Graphic_Multi_Init_Patch.suppressError = false;
						}
					}
					yield return null;
				}
			}
		}

		public static Zombie SpawnZombie(IntVec3 cell, Map map, ZombieType zombieType)
		{
			Zombie result = null;
			var it = SpawnZombieIterativ(cell, map, zombieType, (zombie) => result = zombie);
			while (it.MoveNext())
				;
			return result;
		}

		public static bool RunWithFailureCheck(out Exception exception, Action action)
		{
			try
			{
				action();
				exception = null;
				return false;
			}
			catch (Exception ex)
			{
				exception = ex;
				return true;
			}
		}

		public static bool RunWithFailureCheck<T>(out T result, out Exception exception, Func<T> func)
		{
			try
			{
				exception = null;
				result = func();
				return false;
			}
			catch (Exception ex)
			{
				exception = ex;
				result = default;
				return true;
			}
		}

		public static IEnumerator SpawnZombieIterativ(IntVec3 cell, Map map, ZombieType zombieType, Action<Zombie> callback)
		{
			static void Abort(Exception ex)
			{
				Log.Error($"Zombieland caught an exception from another mod while creating a zombie: {ex}");
				ZombiesSpawning--;
			}

			ZombiesSpawning++;
			var zombie = (Zombie)ThingMaker.MakeThing(ZombieDefOf.Zombie.race, null);
			yield return null;

			if (RunWithFailureCheck(out var bodyType, out var ex1, () =>
			{
				var bodyType = PrepareZombieType(zombie, zombieType);
				zombie.kindDef = ZombieDefOf.Zombie;
				zombie.SetFactionDirect(FactionUtility.DefaultFactionFrom(ZombieDefOf.Zombies));
				return bodyType;
			}))
			{ Abort(ex1); yield break; }
			yield return null;

			if (RunWithFailureCheck(out var ex2, () =>
			{
				PawnComponentsUtility.CreateInitialComponents(zombie);
			}))
			{ Abort(ex2); yield break; }
			yield return null;

			if (RunWithFailureCheck(out var ex3, () =>
			{
				zombie.health.hediffSet.Clear();
				var ageInYears = (long)Rand.Range(14, 130);
				zombie.ageTracker.AgeBiologicalTicks = (ageInYears * 3600000);
				zombie.ageTracker.AgeChronologicalTicks = zombie.ageTracker.AgeBiologicalTicks;
				zombie.ageTracker.BirthAbsTicks = GenTicks.TicksAbs - zombie.ageTracker.AgeBiologicalTicks;
				var idx = zombie.ageTracker.CurLifeStageIndex; // trigger calculations
			}))
			{ Abort(ex3); yield break; }
			yield return null;

			if (RunWithFailureCheck(out var ex4, () =>
			{
				zombie.needs.SetInitialLevels();
				zombie.records.records.values.Clear();
			}))
			{ Abort(ex4); yield break; }
			yield return null;

			if (RunWithFailureCheck(out var ex5, () =>
			{
				zombie.needs.mood = new Need_Mood(zombie);
			}))
			{ Abort(ex5); yield break; }
			yield return null;

			if (RunWithFailureCheck(out var name, out var ex6, () =>
			{
				return PawnNameDatabaseSolid.GetListForGender((zombie.gender == Gender.Female) ? GenderPossibility.Female : GenderPossibility.Male).SafeRandomElement();
			}))
			{ Abort(ex6); yield break; }
			yield return null;

			if (RunWithFailureCheck(out var ex7, () =>
			{
				var n1 = name.First.Replace('s', 'z').Replace('S', 'Z');
				var n2 = name.Last.Replace('s', 'z').Replace('S', 'Z');
				var n3 = name.Nick.Replace('s', 'z').Replace('S', 'Z');
				zombie.Name = new NameTriple(n1, n3, n2);
			}))
			{ Abort(ex7); yield break; }
			yield return null;

			if (RunWithFailureCheck(out var ex8, () =>
			{
				zombie.story.childhood = childBackstories.SafeRandomElement();
			}))
			{ Abort(ex8); yield break; }
			yield return null;

			if (RunWithFailureCheck(out var ex9, () =>
			{
				if (zombie.ageTracker.AgeBiologicalYearsFloat >= 20f)
					zombie.story.adulthood = adultBackstories.SafeRandomElement();
			}))
			{ Abort(ex9); yield break; }
			yield return null;

			if (RunWithFailureCheck(out var ex10, () =>
			{
				var headType = DefDatabase<HeadTypeDef>.AllDefsListForReading
					.Where(def => ZombieBaseValues.IsValidHeadPath(def.graphicPath))
					.RandomElement();

				zombie.story.melanin = zombie.isAlbino || zombie.isHealer ? 1f : (zombie.isDarkSlimer ? 0f : 0.01f * Rand.Range(10, 91));
				zombie.story.bodyType = bodyType;
				zombie.story.headType = headType;
				//zombie.story.crownType = Rand.Bool ? CrownType.Average : CrownType.Narrow;
				zombie.story.hairColor = ZombieBaseValues.HairColor();
				zombie.story.hairDef = PawnStyleItemChooser.RandomHairFor(zombie);
			}))
			{ Abort(ex10); yield break; }
			yield return null;

			if (RunWithFailureCheck(out var ex11, () =>
			{
				FixVanillaHairExpanded(zombie, ZombieDefOf.Zombies);
			}))
			{ Abort(ex11); yield break; }
			yield return null;

			IEnumerator it = default;
			var looping = false;

			it = AssignNewGraphicsIterator(zombie);
			looping = true;
			while (looping)
			{
				if (RunWithFailureCheck(out var ex12, () => looping = it.MoveNext()))
				{ looping = false; Abort(ex12); yield break; }
				yield return null;
			}

			if (RunWithFailureCheck(out var ex13, () =>
			{
				zombie.Drawer.leaner = new ZombieLeaner(zombie);
				zombie.pather ??= new Pawn_PathFollower(zombie);
				zombie.pather.destination = IntVec3.Invalid;
			}))
			{ Abort(ex13); yield break; }
			yield return null;

			if (zombie.IsTanky == false && ZombieSettings.Values.disableRandomApparel == false)
			{
				it = GenerateStartingApparelFor(zombie);
				looping = true;
				while (looping)
				{
					if (RunWithFailureCheck(out var ex14, () => looping = it.MoveNext()))
					{ looping = false; Abort(ex14); yield break; }
					yield return null;
				}
			}

			if (RunWithFailureCheck(out var ex15, () =>
			{
				if (zombie.IsSuicideBomber)
					zombie.lastBombTick = Find.TickManager.TicksAbs + Rand.Range(0, (int)zombie.bombTickingInterval);
				if (cell.IsValid)
					_ = GenPlace.TryPlaceThing(zombie, cell, map, ThingPlaceMode.Direct);
			}))
			{ Abort(ex15); yield break; }
			yield return null;

			try
			{
				callback?.Invoke(zombie);
			}
			catch (Exception ex16)
			{
				Log.Warning($"Zombieland caught an exception after creating a zombie: {ex16}");
			}
			ZombiesSpawning--;

			switch (Find.TickManager.CurTimeSpeed)
			{
				case TimeSpeed.Paused:
					break;
				case TimeSpeed.Normal:
					yield return new WaitForSeconds(0.1f);
					break;
				case TimeSpeed.Fast:
					yield return new WaitForSeconds(0.25f);
					break;
				case TimeSpeed.Superfast:
					yield return new WaitForSeconds(0.5f);
					break;
				case TimeSpeed.Ultrafast:
					yield return new WaitForSeconds(1f);
					break;
			}
			if (zombie.isElectrifier)
			{
				var tickManager = map.GetComponent<TickManager>();
				_ = tickManager?.hummingZombies.Add(zombie);
				// _ = zombie.verbTracker.AllVerbs.RemoveAll(verb => verb.GetDamageDef() == Tools.ZombieBiteDamageDef);
			}
		}

		// fixes for other mods

		static readonly MethodInfo m_PawnBeardChooser_GenerateBeard = AccessTools.Method("VanillaHairExpanded.PawnBeardChooser:GenerateBeard");
		static FastInvokeHandler GenerateBeard = null;
		static void FixVanillaHairExpanded(Pawn pawn, FactionDef faction)
		{
			if (m_PawnBeardChooser_GenerateBeard != null)
			{
				GenerateBeard ??= MethodInvoker.GetHandler(m_PawnBeardChooser_GenerateBeard);
				_ = GenerateBeard(null, new object[] { pawn, faction });
			}
		}
	}
}
