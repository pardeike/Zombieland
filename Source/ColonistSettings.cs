using RimWorld.Planet;
using System.Collections.Generic;
using Verse;

namespace ZombieLand
{
	class ColonistConfig : IExposable
	{
		public bool autoExtractZombieSerum = true;
		public bool autoAvoidZombies = true;

		public void ToggleAutoExtractZombieSerum()
		{
			autoExtractZombieSerum = autoExtractZombieSerum == false;
		}

		public void ToggleAutoAvoidZombies()
		{
			autoAvoidZombies = autoAvoidZombies == false;
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref autoExtractZombieSerum, "autoExtractZombieSerum", true);
			Scribe_Values.Look(ref autoAvoidZombies, "autoAvoidZombies", true);
		}
	}

	class ColonistSettings : WorldComponent
	{
		public static Dictionary<Pawn, ColonistConfig> colonists = new Dictionary<Pawn, ColonistConfig>();
		private List<Pawn> colonistsKeysWorkingList;
		private List<ColonistConfig> colonistsValuesWorkingList;

		public static ColonistSettings Values => Find.World.GetComponent<ColonistSettings>();

		public ColonistSettings(World world) : base(world)
		{
		}

		public ColonistConfig ConfigFor(Pawn pawn)
		{
			if (pawn.IsColonist == false)
				return null;
			if (colonists.TryGetValue(pawn, out var config))
				return config;
			config = new ColonistConfig();
			colonists[pawn] = config;
			return config;
		}

		public void RemoveColonist(Pawn pawn)
		{
			_ = colonists.Remove(pawn);
		}

		public override void ExposeData()
		{
			base.ExposeData();

			if (Scribe.mode == LoadSaveMode.Saving)
				_ = colonists.RemoveAll(pair => pair.Key == null || pair.Key.IsColonist == false);

			Scribe_Collections.Look(ref colonists, "colonists", LookMode.Reference, LookMode.Deep, ref colonistsKeysWorkingList, ref colonistsValuesWorkingList);

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (colonists == null)
					colonists = new Dictionary<Pawn, ColonistConfig>();
				else
					_ = colonists.RemoveAll(pair => pair.Key == null || pair.Key.IsColonist == false);
			}
		}
	}
}