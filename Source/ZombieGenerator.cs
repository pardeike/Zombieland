using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class ZombieRequest
	{
		public Map map;
		public IntVec3 cell;
		public Zombie zombie;
		public bool isEvent;
	}

	[StaticConstructorOnStartup]
	public class ZombieGenerator
	{
		ConcurrentQueue<ZombieRequest> requestQueue;
		Dictionary<Map, ConcurrentQueue<ZombieRequest>> resultQueues;

		Thread workerThread;

		public ConcurrentQueue<ZombieRequest> QueueForMap(Map map)
		{
			ConcurrentQueue<ZombieRequest> queue;
			if (resultQueues.TryGetValue(map, out queue) == false)
			{
				queue = new ConcurrentQueue<ZombieRequest>(true);
				resultQueues.Add(map, queue);
			}
			return queue;
		}

		public ZombieGenerator()
		{
			requestQueue = new ConcurrentQueue<ZombieRequest>();
			resultQueues = new Dictionary<Map, ConcurrentQueue<ZombieRequest>>();

			workerThread = new Thread(() =>
			{
				EndlessLoop:

				try
				{
					var request = requestQueue.Dequeue();
					if (request.zombie == null)
						request.zombie = GeneratePawn();

					var queue = QueueForMap(request.map);
					queue.Enqueue(request);
				}
				catch (Exception e)
				{
					Log.Warning("ZombieAvoider thread error: " + e);
					Thread.Sleep(500);
				}

				goto EndlessLoop;
			});

			workerThread.Priority = System.Threading.ThreadPriority.Lowest;
			workerThread.Start();
		}

		public int ZombiesQueued(Map map)
		{
			var queue = QueueForMap(map);
			return requestQueue.Count(q => q.map == map) + queue.Count();
		}

		public void SpawnZombieAt(Map map, IntVec3 cell, bool isEvent)
		{
			requestQueue.Enqueue(new ZombieRequest() { map = map, cell = cell, isEvent = isEvent, zombie = null });
		}

		public ZombieRequest TryGetNextGeneratedZombie(Map map)
		{
			var queue = QueueForMap(map);
			return queue.Dequeue();
		}

		public void RequeueZombie(ZombieRequest request)
		{
			var queue = QueueForMap(request.map);
			queue.Enqueue(request);
		}

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

		static IntVec2 SideEyeOffset(string headPath)
		{
			return (new Dictionary<string, IntVec2>() {
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
			})[headPath];
		}

		public static Zombie GeneratePawn()
		{
			var zombie = (Zombie)ThingMaker.MakeThing(ZombieDefOf.Zombie.race, null);

			zombie.gender = Rand.Bool ? Gender.Male : Gender.Female;
			zombie.kindDef = ZombieDefOf.Zombie;
			zombie.SetFactionDirect(FactionUtility.DefaultFactionFrom(ZombieDefOf.Zombies));

			PawnComponentsUtility.CreateInitialComponents(zombie);
			zombie.health.hediffSet.Clear();

			zombie.ageTracker.AgeBiologicalTicks = ((long)(Rand.Range(0, 0x9c4 + 1) * 3600000f)) + Rand.Range(0, 0x36ee80);
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

			// faster: use MinimalBackstory()
			zombie.story.childhood = BackstoryDatabase.allBackstories
				.Where(kvp => kvp.Value.slot == BackstorySlot.Childhood)
				.RandomElement().Value;
			if (zombie.ageTracker.AgeBiologicalYearsFloat >= 20f)
				zombie.story.adulthood = BackstoryDatabase.allBackstories
				.Where(kvp => kvp.Value.slot == BackstorySlot.Adulthood)
				.RandomElement().Value;

			zombie.story.melanin = 0.01f * Rand.Range(10, 91);
			zombie.story.crownType = Rand.Bool ? CrownType.Average : CrownType.Narrow;

			zombie.story.hairColor = HairColor();
			zombie.story.hairDef = PawnHairChooser.RandomHairDefFor(zombie, ZombieDefOf.Zombies);
			zombie.story.bodyType = (zombie.gender == Gender.Female) ? BodyType.Female : BodyType.Male;
			if (zombie.story.bodyType == BodyType.Male)
				switch (Rand.Range(1, 6))
				{
					case 1:
						zombie.story.bodyType = BodyType.Thin;
						break;
					case 2:
						zombie.story.bodyType = BodyType.Fat;
						break;
					case 3:
						zombie.story.bodyType = BodyType.Hulk;
						break;
				}

			if (ZombieSettings.Values.useCustomTextures)
				AssignNewCustomGraphics(zombie);

			zombie.Drawer.leaner = new ZombieLeaner(zombie);
			Traverse.Create(zombie.pather).Field("destination").SetValue(IntVec3.Invalid);

			return zombie;
		}

		static string[] headShapes = { "Normal", "Pointy", "Wide" };
		public static void AssignNewCustomGraphics(Zombie zombie)
		{
			var renderPrecedence = 0;

			var bodyPath = "Zombie/Naked_" + zombie.story.bodyType.ToString();
			var bodyRequest = new GraphicRequest(typeof(VariableGraphic), bodyPath, ShaderDatabase.CutoutSkin, Vector2.one, Color.white, Color.white, null, renderPrecedence);
			zombie.customBodyGraphic = Activator.CreateInstance<VariableGraphic>();
			zombie.customBodyGraphic.Init(bodyRequest);

			var headPath = "Zombie/" + zombie.gender + "_" + zombie.story.crownType + "_" + headShapes[Rand.Range(0, 3)];
			var headRequest = new GraphicRequest(typeof(VariableGraphic), headPath, ShaderDatabase.CutoutSkin, Vector2.one, Color.white, Color.white, null, renderPrecedence);
			zombie.customHeadGraphic = Activator.CreateInstance<VariableGraphic>();
			zombie.customHeadGraphic.Init(headRequest);

			zombie.sideEyeOffset = SideEyeOffset(headPath.ReplaceFirst("Zombie/", ""));
		}

		public static void FinalizeZombieGeneration(Zombie zombie)
		{
			var graphicPath = GraphicDatabaseHeadRecords.GetHeadRandom(zombie.gender, zombie.story.SkinColor, zombie.story.crownType).GraphicPath;
			Traverse.Create(zombie.story).Field("headGraphicPath").SetValue(graphicPath);

			var request = new PawnGenerationRequest(zombie.kindDef);
			PawnApparelGenerator.GenerateStartingApparelFor(zombie, request);
			zombie.apparel.WornApparel.Do(apparel =>
			{
				Color[] colors =
				{
					"442a0a".HexColor(),
					"615951".HexColor(),
					"1f4960".HexColor(),
					"182a64".HexColor(),
					"73000d".HexColor(),
					"2c422a".HexColor(),
					"332341".HexColor()
				};
				(colors.Clone() as Color[]).Do(c =>
				{
					c.r *= Rand.Range(0.2f, 1f);
					c.g *= Rand.Range(0.2f, 1f);
					c.b *= Rand.Range(0.2f, 1f);
					colors.Add(c);
				});
				colors.Add("000000".HexColor());

				apparel.SetColor(colors[Rand.Range(0, colors.Length)]);
			});
		}
	}
}