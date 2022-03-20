using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
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
						var constant = typeof(Constants).Field(field);

						switch (constant.FieldType.Name)
						{
							case "Boolean":
								{
									if (bool.TryParse(value, out var result))
										constant.SetValue(null, result);
									else
										Log.Error("Cannot parse boolean '" + value + "' of constant " + field);
									break;
								}
							case "Int32":
								{
									if (int.TryParse(value, out var result))
										constant.SetValue(null, result);
									else
										Log.Error("Cannot parse int '" + value + "' of constant " + field);
									break;
								}
							case "Single":
								{
									if (float.TryParse(value, out var result))
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
				});

			var edge = new Color(1, 1, 1, 0.5f);
			dot.SetPixel(0, 0, edge);
			dot.SetPixel(1, 0, Color.white);
			dot.SetPixel(2, 0, edge);
			dot.SetPixel(0, 1, Color.white);
			dot.SetPixel(1, 1, Color.white);
			dot.SetPixel(2, 1, Color.white);
			dot.SetPixel(0, 2, edge);
			dot.SetPixel(1, 2, Color.white);
			dot.SetPixel(2, 2, edge);
			dot.Apply(true);
		}

		// general debugging and testing
		//
		public static readonly bool DEBUGGRID;
		public static readonly bool USE_SOUND = true;
		public static readonly int DEBUG_MAX_ZOMBIE_COUNT = -1;

		// timing
		//
		public static readonly float PHEROMONE_FADEOFF = 180f;
		public static readonly int TICKMANAGER_RECALCULATE_DELAY = 900;
		public static readonly float TICKMANAGER_AVOIDGRID_DELAY = 0.25f;
		public static readonly int EAT_DELAY_TICKS = 1200;

		// zombie spawning
		// the following hours continue after 23h with 24, 25, 26...
		//
		public static readonly int HOUR_START_OF_DUSK = 18;
		public static readonly int HOUR_START_OF_NIGHT = 22;
		public static readonly int HOUR_END_OF_NIGHT = 28;
		public static readonly int HOUR_START_OF_DAWN = 32;

		// zombie stats
		//
		public static readonly float ANIMAL_PHEROMONE_RADIUS = 2f;
		public static readonly float HUMAN_PHEROMONE_RADIUS = 4f;
		public static readonly float TANKY_PHEROMONE_RADIUS = 6f;
		public static readonly float ZOMBIE_HIT_CHANCE_IDLE = 0.2f;
		public static readonly float ZOMBIE_HIT_CHANCE_TRACKING = 0.7f;
		public static readonly int NUMBER_OF_TOP_MOVEMENT_PICKS = 3;
		public static readonly float CLUSTER_AVOIDANCE_CHANCE = 0.25f;
		public static readonly float DIVERTING_FROM_RAGE = 0.1f;
		public static readonly int ZOMBIE_CLOGGING_FACTOR = 10000;
		public static readonly float KILL_CIRCLE_RADIUS_MULTIPLIER;
		public static readonly float BASE_MUZZLE_FLASH_VALUE = 9f;
		public static readonly float SURROUNDING_ZOMBIES_TO_TRIGGER_RAGE = 14;
		public static readonly float COLONISTS_HIT_ZOMBIES_CHANCE = 0.9f;
		public static readonly float COMBAT_EXTENDED_ARMOR_PENETRATION = 0.1f;
		public static readonly int ZOMBIE_HEALING_TICKS = 3000;

		// zombie incidents
		//
		public static readonly int SPAWN_INCIDENT_RADIUS = 10;
		public static readonly int MIN_ZOMBIE_SPAWN_CELL_COUNT = 6;

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

		// misc
		//
		public static readonly float MIN_WEAPON_RANGE = 2f;
		public static readonly float MAX_WEAPON_RANGE = 30f;
		public static readonly float MIN_ATTACKDISTANCE_SQUARED = 2.25f;
		public static readonly float MIN_CONSCIOUSNESS = 0.25f;
		public static readonly float MAX_ROPING_DISTANCE_SQUARED = 144;

		public static readonly Material RAGE_EYE = MaterialPool.MatFrom("RageEye", ShaderDatabase.Mote);
		public static readonly Material BOMB_LIGHT = MaterialPool.MatFrom("BombLight", ShaderDatabase.MoteGlow);
		public static readonly Material[][] TANKYSHIELDS = Tools.GetDamageableGraphics("TankyShield", 2, 4);
		public static readonly Material[][] TANKYHELMETS = Tools.GetDamageableGraphics("TankyHelmet", 3, 4);
		public static readonly Material[][] MINERHELMET = Tools.GetDamageableGraphics("MinerHelmet", 4, 0);
		public static readonly Material[][] TANKYSUITS = Tools.GetDamageableGraphics("TankySuit", 3, 4);
		public static readonly Dictionary<CameraZoomRange, Material> RAGE_AURAS = new Dictionary<CameraZoomRange, Material> {
			{ CameraZoomRange.Closest, MaterialPool.MatFrom("RageAura", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.15f)) },
			{ CameraZoomRange.Close, MaterialPool.MatFrom("RageAura", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.175f)) },
			{ CameraZoomRange.Middle, MaterialPool.MatFrom("RageAura", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.2f)) },
			{ CameraZoomRange.Far, MaterialPool.MatFrom("RageAura", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.25f)) },
			{ CameraZoomRange.Furthest, MaterialPool.MatFrom("RageAura", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.3f)) }
		};
		public static readonly Material[] TOXIC_AURAS = new Material[] {
			MaterialPool.MatFrom("ToxicAura", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.3f)),
			MaterialPool.MatFrom("ToxicAura", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.5f)),
			MaterialPool.MatFrom("ToxicAura", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.7f)),
			MaterialPool.MatFrom("ToxicAura", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.9f)),
			MaterialPool.MatFrom("ToxicAura", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 1.0f))
		};
		public static readonly Material ELECTRIC_SHINE = MaterialPool.MatFrom("Electrifier/Shine", ShaderDatabase.Mote, Color.white);
		public static readonly Material[] ELECTRIC_ARCS = new Material[] {
			MaterialPool.MatFrom("Electrifier/Arc0", ShaderDatabase.Mote, Color.white),
			MaterialPool.MatFrom("Electrifier/Arc1", ShaderDatabase.Mote, Color.white),
			MaterialPool.MatFrom("Electrifier/Arc2", ShaderDatabase.Mote, Color.white),
			MaterialPool.MatFrom("Electrifier/Arc3", ShaderDatabase.Mote, Color.white),
		};
		public static readonly Dictionary<BodyTypeDef, Material[]> ELECTRIC_GLOWING = new Dictionary<BodyTypeDef, Material[]>
		{
			{
				BodyTypeDefOf.Fat, new Material[]
				{
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Fat_east", ShaderDatabase.Mote, Color.white),
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Fat_north", ShaderDatabase.Mote, Color.white),
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Fat_south", ShaderDatabase.Mote, Color.white),
				}
			},
			{
				BodyTypeDefOf.Female, new Material[]
				{
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Female_east", ShaderDatabase.Mote, Color.white),
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Female_north", ShaderDatabase.Mote, Color.white),
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Female_south", ShaderDatabase.Mote, Color.white),
				}
			},
			{
				BodyTypeDefOf.Male, new Material[]
				{
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Male_east", ShaderDatabase.Mote, Color.white),
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Male_north", ShaderDatabase.Mote, Color.white),
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Male_south", ShaderDatabase.Mote, Color.white),
				}
			},
			{
				BodyTypeDefOf.Thin, new Material[]
				{
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Thin_east", ShaderDatabase.Mote, Color.white),
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Thin_north", ShaderDatabase.Mote, Color.white),
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Thin_south", ShaderDatabase.Mote, Color.white),
				}
			}
		};
		public static readonly Material[] ELECTRIC_ABSORB = new Material[] {
			MaterialPool.MatFrom("Electrifier/Absorb0", ShaderDatabase.Mote, Color.white),
			MaterialPool.MatFrom("Electrifier/Absorb1", ShaderDatabase.Mote, Color.white),
			MaterialPool.MatFrom("Electrifier/Absorb2", ShaderDatabase.Mote, Color.white),
			MaterialPool.MatFrom("Electrifier/Absorb3", ShaderDatabase.Mote, Color.white),
		};
		public static readonly Material SCREAM = MaterialPool.MatFrom("Scream", ShaderDatabase.Mote);
		public static readonly Material SCREAMSHADOW = MaterialPool.MatFrom("ScreamShadow", ShaderDatabase.Mote);
		public static readonly Material RAGING = MaterialPool.MatFrom("Rage", ShaderDatabase.Cutout);
		public static readonly Material RUBBLE = MaterialPool.MatFrom("Rubble", ShaderDatabase.Cutout);
		public static readonly Material BRRAINZ = MaterialPool.MatFrom("Brrainz", ShaderDatabase.Cutout);
		public static readonly Material EATING = MaterialPool.MatFrom("Eating", ShaderDatabase.Cutout);
		public static readonly Material HACKING = MaterialPool.MatFrom("Hacking", ShaderDatabase.Cutout);
		public static readonly Material[] CONFUSED = new Material[] {
			MaterialPool.MatFrom("Confused/Confused1", ShaderDatabase.Cutout),
			MaterialPool.MatFrom("Confused/Confused2", ShaderDatabase.Cutout),
			MaterialPool.MatFrom("Confused/Confused3", ShaderDatabase.Cutout),
			MaterialPool.MatFrom("Confused/Confused4", ShaderDatabase.Cutout),
			MaterialPool.MatFrom("Confused/Confused5", ShaderDatabase.Cutout),
			MaterialPool.MatFrom("Confused/Confused6", ShaderDatabase.Cutout),
			MaterialPool.MatFrom("Confused/Confused7", ShaderDatabase.Cutout),
		};
		public static readonly Material[][] TARSLIMES = new Material[][] {
			new Material[] {
				MaterialPool.MatFrom("TarSlime/TarSlime0", ShaderDatabase.Mote, new Color(1,1,1,0.25f)),
				MaterialPool.MatFrom("TarSlime/TarSlime1", ShaderDatabase.Mote, new Color(1,1,1,0.25f)),
				MaterialPool.MatFrom("TarSlime/TarSlime2", ShaderDatabase.Mote, new Color(1,1,1,0.25f)),
				MaterialPool.MatFrom("TarSlime/TarSlime3", ShaderDatabase.Mote, new Color(1,1,1,0.25f)),
				MaterialPool.MatFrom("TarSlime/TarSlime4", ShaderDatabase.Mote, new Color(1,1,1,0.25f)),
				MaterialPool.MatFrom("TarSlime/TarSlime5", ShaderDatabase.Mote, new Color(1,1,1,0.25f)),
				MaterialPool.MatFrom("TarSlime/TarSlime6", ShaderDatabase.Mote, new Color(1,1,1,0.25f)),
				MaterialPool.MatFrom("TarSlime/TarSlime7", ShaderDatabase.Mote, new Color(1,1,1,0.25f)),
			},
			new Material[] {
				MaterialPool.MatFrom("TarSlime/TarSlime0", ShaderDatabase.Mote, new Color(1,1,1,0.5f)),
				MaterialPool.MatFrom("TarSlime/TarSlime1", ShaderDatabase.Mote, new Color(1,1,1,0.5f)),
				MaterialPool.MatFrom("TarSlime/TarSlime2", ShaderDatabase.Mote, new Color(1,1,1,0.5f)),
				MaterialPool.MatFrom("TarSlime/TarSlime3", ShaderDatabase.Mote, new Color(1,1,1,0.5f)),
				MaterialPool.MatFrom("TarSlime/TarSlime4", ShaderDatabase.Mote, new Color(1,1,1,0.5f)),
				MaterialPool.MatFrom("TarSlime/TarSlime5", ShaderDatabase.Mote, new Color(1,1,1,0.5f)),
				MaterialPool.MatFrom("TarSlime/TarSlime6", ShaderDatabase.Mote, new Color(1,1,1,0.5f)),
				MaterialPool.MatFrom("TarSlime/TarSlime7", ShaderDatabase.Mote, new Color(1,1,1,0.5f)),
			},
			new Material[] {
				MaterialPool.MatFrom("TarSlime/TarSlime0", ShaderDatabase.Mote, new Color(1,1,1,0.75f)),
				MaterialPool.MatFrom("TarSlime/TarSlime1", ShaderDatabase.Mote, new Color(1,1,1,0.75f)),
				MaterialPool.MatFrom("TarSlime/TarSlime2", ShaderDatabase.Mote, new Color(1,1,1,0.75f)),
				MaterialPool.MatFrom("TarSlime/TarSlime3", ShaderDatabase.Mote, new Color(1,1,1,0.75f)),
				MaterialPool.MatFrom("TarSlime/TarSlime4", ShaderDatabase.Mote, new Color(1,1,1,0.75f)),
				MaterialPool.MatFrom("TarSlime/TarSlime5", ShaderDatabase.Mote, new Color(1,1,1,0.75f)),
				MaterialPool.MatFrom("TarSlime/TarSlime6", ShaderDatabase.Mote, new Color(1,1,1,0.75f)),
				MaterialPool.MatFrom("TarSlime/TarSlime7", ShaderDatabase.Mote, new Color(1,1,1,0.75f)),
			},
			new Material[] {
				MaterialPool.MatFrom("TarSlime/TarSlime0", ShaderDatabase.Mote, new Color(1,1,1,1)),
				MaterialPool.MatFrom("TarSlime/TarSlime1", ShaderDatabase.Mote, new Color(1,1,1,1)),
				MaterialPool.MatFrom("TarSlime/TarSlime2", ShaderDatabase.Mote, new Color(1,1,1,1)),
				MaterialPool.MatFrom("TarSlime/TarSlime3", ShaderDatabase.Mote, new Color(1,1,1,1)),
				MaterialPool.MatFrom("TarSlime/TarSlime4", ShaderDatabase.Mote, new Color(1,1,1,1)),
				MaterialPool.MatFrom("TarSlime/TarSlime5", ShaderDatabase.Mote, new Color(1,1,1,1)),
				MaterialPool.MatFrom("TarSlime/TarSlime6", ShaderDatabase.Mote, new Color(1,1,1,1)),
				MaterialPool.MatFrom("TarSlime/TarSlime7", ShaderDatabase.Mote, new Color(1,1,1,1)),
			},
		};
		public static readonly Material[] BEING_HEALED = new Material[] {
			MaterialPool.MatFrom("BeingHealed", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.1f)),
			MaterialPool.MatFrom("BeingHealed", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.2f)),
			MaterialPool.MatFrom("BeingHealed", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.3f)),
			MaterialPool.MatFrom("BeingHealed", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.5f)),
			MaterialPool.MatFrom("BeingHealed", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.7f)),
			MaterialPool.MatFrom("BeingHealed", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.7f)),
			MaterialPool.MatFrom("BeingHealed", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.4f)),
			MaterialPool.MatFrom("BeingHealed", ShaderDatabase.Mote, new Color(1f, 1f, 1f, 0.1f))
		};
		public static readonly Material[] RopeLineMat = new[]
		{
			MaterialPool.MatFrom(Pawn_RopeTracker.RopeTexPath, ShaderDatabase.Transparent, GenColor.FromBytes(255, 0, 0, 255)),
			MaterialPool.MatFrom(Pawn_RopeTracker.RopeTexPath, ShaderDatabase.Transparent, GenColor.FromBytes(209, 135, 62, 255)),
			MaterialPool.MatFrom(Pawn_RopeTracker.RopeTexPath, ShaderDatabase.Transparent, GenColor.FromBytes(99, 70, 41, 255)),
		};

		public static readonly Texture2D PlusButton = ContentFinder<Texture2D>.Get("PlusButton", true);
		public static readonly Texture2D MinusButton = ContentFinder<Texture2D>.Get("MinusButton", true);
		public static readonly Texture2D Lock = ContentFinder<Texture2D>.Get("Lock", true);
		public static readonly Texture2D[] ButtonAdd = new[] { ContentFinder<Texture2D>.Get("ButtonAdd0", true), ContentFinder<Texture2D>.Get("ButtonAdd1", true) };
		public static readonly Texture2D[] ButtonDel = new[] { ContentFinder<Texture2D>.Get("ButtonDel0", true), ContentFinder<Texture2D>.Get("ButtonDel1", true) };
		public static readonly Texture2D[] ButtonDup = new[] { ContentFinder<Texture2D>.Get("ButtonDup0", true), ContentFinder<Texture2D>.Get("ButtonDup1", true) };
		public static readonly Texture2D[] ButtonDown = new[] { ContentFinder<Texture2D>.Get("ButtonDown0", true), ContentFinder<Texture2D>.Get("ButtonDown1", true) };
		public static readonly Texture2D[] ButtonUp = new[] { ContentFinder<Texture2D>.Get("ButtonUp0", true), ContentFinder<Texture2D>.Get("ButtonUp1", true) };
		public static readonly Texture2D Danger = ContentFinder<Texture2D>.Get("Danger", true);

		public static readonly System.Random random = new System.Random();

		public static readonly Mesh screamMesh = MeshMakerPlanes.NewPlaneMesh(8f);
		public static readonly Mesh healMesh = MeshMakerPlanes.NewPlaneMesh(4f);

		public static Texture2D dot = new Texture2D(3, 3);

		public static readonly Texture2D healthBarFrame = SolidColorMaterials.NewSolidColorTexture(Color.black);
		public static readonly Color healthBarBG = new Color(1, 1, 1, 0.25f);
	}
}
