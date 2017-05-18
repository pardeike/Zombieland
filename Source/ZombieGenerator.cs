using Harmony;
using RimWorld;
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
	}

	[StaticConstructorOnStartup]
	public class ZombieGenerator
	{
		Semaphore requestsAvailable;

		Queue<ZombieRequest> requestQueue;
		Dictionary<Map, Queue<ZombieRequest>> resultQueues;

		Thread workerThread;

		public Queue<ZombieRequest> QueueForMap(Map map)
		{
			Queue<ZombieRequest> queue;
			if (resultQueues.TryGetValue(map, out queue) == false)
			{
				queue = new Queue<ZombieRequest>();
				resultQueues.Add(map, queue);
			}
			return queue;
		}

		public ZombieGenerator()
		{
			requestsAvailable = new Semaphore(0, int.MaxValue);

			requestQueue = new Queue<ZombieRequest>();
			resultQueues = new Dictionary<Map, Queue<ZombieRequest>>();

			workerThread = new Thread(() =>
			{
				while (requestsAvailable.WaitOne())
				{
					ZombieRequest request;
					lock (requestQueue)
						request = requestQueue.Dequeue();
					if (request != null)
					{
						if (request.zombie == null)
							request.zombie = GeneratePawn();
						lock (resultQueues)
						{
							var queue = QueueForMap(request.map);
							queue.Enqueue(request);
						}
					}
				}
			});
			workerThread.Start();
		}

		public int ZombiesQueued(Map map)
		{
			lock (requestQueue)
				lock (resultQueues)
				{
					var queue = QueueForMap(map);
					return requestQueue.Where(q => q.map == map).Count() + queue.Count;
				}
		}

		public void SpawnZombieAt(Map map, IntVec3 cell)
		{
			lock (requestQueue)
				requestQueue.Enqueue(new ZombieRequest() { map = map, cell = cell, zombie = null });
			requestsAvailable.Release();
		}

		public ZombieRequest TryGetNextGeneratedZombie(Map map)
		{
			lock (resultQueues)
			{
				var queue = QueueForMap(map);
				if (queue.Count == 0) return null;
				return queue.Dequeue();
			}
		}

		public void RequeueZombie(ZombieRequest request)
		{
			lock (resultQueues)
			{
				var queue = QueueForMap(request.map);
				queue.Enqueue(request);
			}
		}

		static Color HairColor()
		{
			float num3 = Rand.Value;
			if (num3 < 0.25f)
				return new Color(0.2f, 0.2f, 0.2f);

			if (num3 < 0.5f)
				return new Color(0.31f, 0.28f, 0.26f);

			if (num3 < 0.75f)
				return new Color(0.25f, 0.2f, 0.15f);

			return new Color(0.3f, 0.2f, 0.1f);
		}

		public static Zombie GeneratePawn()
		{
			var kindDef = ZombieDefOf.Zombie;

			var pawn = (Zombie)ThingMaker.MakeThing(kindDef.race, null);

			pawn.gender = Rand.Bool ? Gender.Male : Gender.Female;
			var factionDef = ZombieDefOf.Zombies;
			var faction = FactionUtility.DefaultFactionFrom(factionDef);
			pawn.kindDef = kindDef;
			pawn.SetFactionDirect(faction);

			PawnComponentsUtility.CreateInitialComponents(pawn);

			pawn.ageTracker.AgeBiologicalTicks = ((long)(Rand.Range(0, 0x9c4) * 3600000f)) + Rand.Range(0, 0x36ee80);
			pawn.ageTracker.AgeChronologicalTicks = pawn.ageTracker.AgeBiologicalTicks;
			pawn.ageTracker.BirthAbsTicks = GenTicks.TicksAbs - pawn.ageTracker.AgeBiologicalTicks;

			pawn.needs.SetInitialLevels();
			pawn.needs.mood = new Need_Mood(pawn);

			var nameGenerator = pawn.RaceProps.GetNameGenerator(pawn.gender);
			var name = PawnNameDatabaseSolid.GetListForGender((pawn.gender == Gender.Female) ? GenderPossibility.Female : GenderPossibility.Male).RandomElement();
			var n1 = name.First.Replace('s', 'z').Replace('S', 'Z');
			var n2 = name.Last.Replace('s', 'z').Replace('S', 'Z');
			var n3 = name.Nick.Replace('s', 'z').Replace('S', 'Z');
			pawn.Name = new NameTriple(n1, n3, n2);

			// faster: use MinimalBackstory()
			pawn.story.childhood = BackstoryDatabase.allBackstories
				.Where(kvp => kvp.Value.slot == BackstorySlot.Childhood)
				.RandomElement().Value;
			if (pawn.ageTracker.AgeBiologicalYearsFloat >= 20f)
				pawn.story.adulthood = BackstoryDatabase.allBackstories
				.Where(kvp => kvp.Value.slot == BackstorySlot.Adulthood)
				.RandomElement().Value;

			pawn.story.melanin = 0.01f * Rand.Range(10, 90);
			pawn.story.crownType = Rand.Bool ? CrownType.Average : CrownType.Narrow;

			pawn.story.hairColor = HairColor();
			pawn.story.hairDef = PawnHairChooser.RandomHairDefFor(pawn, factionDef);
			pawn.story.bodyType = (pawn.gender == Gender.Female) ? BodyType.Female : BodyType.Male;
			if (pawn.story.bodyType == BodyType.Male)
				switch (Rand.Range(1, 6))
				{
					case 1:
						pawn.story.bodyType = BodyType.Thin;
						break;
					case 2:
						pawn.story.bodyType = BodyType.Fat;
						break;
					case 3:
						pawn.story.bodyType = BodyType.Hulk;
						break;
				}

			return pawn;
		}

		public static void FinalizeZombieGeneration(Zombie zombie)
		{
			var graphicPath = GraphicDatabaseHeadRecords.GetHeadRandom(zombie.gender, zombie.story.SkinColor, zombie.story.crownType).GraphicPath;
			Traverse.Create(zombie.story).Field("headGraphicPath").SetValue(graphicPath);

			var request = new PawnGenerationRequest(zombie.kindDef);
			PawnApparelGenerator.GenerateStartingApparelFor(zombie, request);
			zombie.apparel.WornApparel.Do(apparel =>
			{
				apparel.DrawColor = apparel.DrawColor.SaturationChanged(0.5f);
			});
		}
	}
}