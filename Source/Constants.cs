using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public static class Constants
	{
		// general debugging and testing
		//
		public static readonly bool DEBUGGRID = false;
		public static readonly bool USE_SOUND = true;
		public static readonly int DEBUG_COLONY_POINTS = 0;
		public static readonly bool USE_CUSTOM_TEXTURES = true;

		// timing
		//
		public static readonly float FRAME_TIME_FACTOR = 0.5f;
		public static readonly long PHEROMONE_FADEOFF = 1000L * GenTicks.SecondsToTicks(90f);
		public static readonly int JOBDRIVER_TICKS_DELAY = GenTicks.SecondsToTicks(1f);
		public static readonly int TICKMANAGER_RECALCULATE_DELAY = GenTicks.SecondsToTicks(2f);

		// zombie spawning
		// the following hours continue after 23h with 24, 25, 26...
		//
		public static readonly int HOUR_START_OF_DUSK = 18;
		public static readonly int HOUR_START_OF_NIGHT = 22;
		public static readonly int HOUR_END_OF_NIGHT = 28;
		public static readonly int HOUR_START_OF_DAWN = 32;

		// zombie stats
		//
		public static readonly float ZOMBIE_CHAINING_RADIUS = 1.5f;
		public static readonly float ANIMAL_PHEROMONE_RADIUS = 3f;
		public static readonly float HUMAN_PHEROMONE_RADIUS = 5f;
		public static readonly float ZOMBIE_MOVE_SPEED_IDLE = 0.2f;
		public static readonly float ZOMBIE_MOVE_SPEED_TRACKING = 1.5f;
		public static readonly float ZOMBIE_HIT_CHANCE_IDLE = 0.2f;
		public static readonly float ZOMBIE_HIT_CHANCE_TRACKING = 0.7f;
		public static readonly int NUMBER_OF_TOP_MOVEMENT_PICKS = 3;
		public static readonly float STANDING_STILL_CHANCE = 0.6f;
		public static readonly int ZOMBIE_CLOGGING_FACTOR = 10000;
		public static readonly float KILL_CIRCLE_RADIUS_MULTIPLIER = 0f;

		// rubble
		//
		public static readonly int MIN_DELTA_TICKS = 20;
		public static readonly int MAX_DELTA_TICKS = 4;
		public static readonly int RUBBLE_AMOUNT = 50;
		public static readonly float MAX_HEIGHT = 0.6f;
		public static readonly float MIN_SCALE = 0.05f;
		public static readonly float MAX_SCALE = 0.3f;
		public static readonly float ZOMBIE_LAYER = Altitudes.AltitudeFor(AltitudeLayer.Pawn) - 0.005f;
		public static readonly float EMERGE_DELAY = 0.8f;

		// zombie incidents
		//
		public static readonly int SPAWN_INCIDENT_RADIUS = 10;
		public static readonly int MIN_ZOMBIE_SPAWN_CELL_COUNT = 6;
		public static readonly int NUMBER_OF_ZOMBIES_IN_INCIDENT = 40;

		// misc
		//
		public static readonly float MIN_WEAPON_RANGE = 2f;
		public static readonly float MAX_WEAPON_RANGE = 30f;
		public static readonly Material RUBBLE = MaterialPool.MatFrom("Rubble", ShaderDatabase.Cutout);
		public static readonly Material BRRAINZ = MaterialPool.MatFrom("Brrainz", ShaderDatabase.Cutout);
		public static readonly System.Random random = new System.Random();
		public static readonly ColorHSV ZOMBIE_SKIN_COLOR = new ColorHSV(0.33f, 0.5f, 0.5f);
	}
}