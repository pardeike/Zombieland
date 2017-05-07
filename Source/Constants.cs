using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public static class Constants
	{
		// general debugging and testing
		//
		public static bool DEBUGGRID = false;
		public static bool USE_SOUND = true;
		public static int DEBUG_COLONY_POINTS = 0;
		public static bool SPAWN_ALL_ZOMBIES = false;

		// timing
		//
		public static long PHEROMONE_FADEOFF = 1000L * GenTicks.SecondsToTicks(90f);
		public static int JOBDRIVER_TICKS_DELAY = GenTicks.SecondsToTicks(1f);

		// zombie spawning
		// the following hours continue after 23h with 24, 25, 26...
		//
		public static int HOUR_START_OF_DUSK = 18;
		public static int HOUR_START_OF_NIGHT = 22;
		public static int HOUR_END_OF_NIGHT = 28;
		public static int HOUR_START_OF_DAWN = 32;

		// zombie stats
		//
		public static float ZOMBIE_CHAINING_RADIUS = 1.5f;
		public static float ANIMAL_PHEROMONE_RADIUS = 3f;
		public static float HUMAN_PHEROMONE_RADIUS = 5f;
		public static float ZOMBIE_MOVE_SPEED_IDLE = 0.2f;
		public static float ZOMBIE_MOVE_SPEED_TRACKING = 1.5f;
		public static int NUMBER_OF_TOP_MOVEMENT_PICKS = 3;
		public static float STANDING_STILL_CHANCE = 0.6f;
		public static int ZOMBIE_CLOGGING_FACTOR = 10000;
		public static float KILL_CIRCLE_RADIUS_MULTIPLIER = 0f;

		// zombie incidents
		//
		public static int MIN_ZOMBIE_SPAWN_CELL_COUNT = 6;
		public static int NUMBER_OF_ZOMBIES_IN_INCIDENT = 40;

		// misc
		//
		public static float MIN_WEAPON_RANGE = 2f;
		public static float MAX_WEAPON_RANGE = 30f;
		public static Material RUBBLE = MaterialPool.MatFrom("Rubble", ShaderDatabase.Cutout);
		public static System.Random random = new System.Random();
		public static Color ZOMBIE_SKIN_COLOR = new Color(
			79f / 255f,
			130f / 255f,
			68f / 255f
		);
	}
}