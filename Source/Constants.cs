using Harmony;
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public static class Constants
	{
		static Constants()
		{
			var settingsPath = Tools.GetModRootDirectory() + Path.DirectorySeparatorChar + "About" + Path.DirectorySeparatorChar + "Settings.txt";
			File.ReadAllLines(settingsPath)
				.Select(line => line.Trim())
				.Where(line => line.StartsWith("/", StringComparison.Ordinal) == false && line.Length > 0)
				.Select(line =>
				{
					var parts = line.Split('=').Select(part => part.Trim()).ToList();
					parts.Insert(0, line);
					return parts.ToArray();
				})
				.Do(parts =>
				{
					if (parts.Length != 3)
						Log.Error("Unexpected line: " + parts[0]);
					else
					{
						var field = parts[1];
						var value = parts[2];
						var constant = AccessTools.Field(typeof(Constants), field);
						if (constant != null)
						{
							switch (constant.FieldType.Name)
							{
								case "Boolean":
									{
										bool result;
										if (Boolean.TryParse(value, out result))
											constant.SetValue(null, result);
										else
											Log.Error("Cannot parse boolean '" + value + "' of constant " + field);
										break;
									}
								case "Int32":
									{
										int result;
										if (Int32.TryParse(value, out result))
											constant.SetValue(null, result);
										else
											Log.Error("Cannot parse int '" + value + "' of constant " + field);
										break;
									}
								case "Single":
									{
										float result;
										if (Single.TryParse(value, out result))
											constant.SetValue(null, result);
										else
											Log.Error("Cannot parse float '" + value + "' of constant " + field);
										break;
									}
								default:
									Log.Error("Zombieland constant '" + field + "' with unknown type " + constant.FieldType.Name);
									break;
							}
						}
						else
							Log.Error("Zombieland constant '" + field + "' unknown");
					}
				});
		}

		// general debugging and testing
		//
		public static bool DEBUGGRID;
		public static bool USE_SOUND = true;
		public static int DEBUG_COLONY_POINTS;
		public static bool USE_CUSTOM_TEXTURES = true;

		// timing
		//
		public static float DAYS_BEFORE_ZOMBIES_SPAWN = 3f;
		public static float FRAME_TIME_FACTOR = 0.25f;
		public static float PHEROMONE_FADEOFF = 90f;
		public static float TICKMANAGER_RECALCULATE_DELAY = 5f;
		public static int EAT_DELAY_TICKS = 600;

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
		public static float ANIMAL_PHEROMONE_RADIUS = 2f;
		public static float HUMAN_PHEROMONE_RADIUS = 4f;
		public static float ZOMBIE_MOVE_SPEED_IDLE = 0.2f;
		public static float ZOMBIE_MOVE_SPEED_TRACKING = 1.5f;
		public static float ZOMBIE_HIT_CHANCE_IDLE = 0.2f;
		public static float ZOMBIE_HIT_CHANCE_TRACKING = 0.7f;
		public static int NUMBER_OF_TOP_MOVEMENT_PICKS = 3;
		public static float STANDING_STILL_CHANCE = 0.6f;
		public static int ZOMBIE_CLOGGING_FACTOR = 10000;
		public static float KILL_CIRCLE_RADIUS_MULTIPLIER;

		// rubble
		//
		public static int MIN_DELTA_TICKS = 20;
		public static int MAX_DELTA_TICKS = 4;
		public static int RUBBLE_AMOUNT = 50;
		public static float MAX_HEIGHT = 0.6f;
		public static float MIN_SCALE = 0.05f;
		public static float MAX_SCALE = 0.3f;
		public static float ZOMBIE_LAYER = Altitudes.AltitudeFor(AltitudeLayer.Pawn) - 0.005f;
		public static float EMERGE_DELAY = 0.8f;

		// zombie incidents
		//
		public static int SPAWN_INCIDENT_RADIUS = 10;
		public static int MIN_ZOMBIE_SPAWN_CELL_COUNT = 6;
		public static int NUMBER_OF_ZOMBIES_IN_INCIDENT = 20;

		// misc
		//
		public static float MIN_WEAPON_RANGE = 2f;
		public static float MAX_WEAPON_RANGE = 30f;
		public static Material RUBBLE = MaterialPool.MatFrom("Rubble", ShaderDatabase.Cutout);
		public static Material BRRAINZ = MaterialPool.MatFrom("Brrainz", ShaderDatabase.Cutout);
		public static Material EATING = MaterialPool.MatFrom("Eating", ShaderDatabase.Cutout);
		public static System.Random random = new System.Random();
	}
}