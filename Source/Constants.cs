using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[AttributeUsage(AttributeTargets.Field)]
	public class ConstantAttribute : Attribute
	{
		public int Version { get; }
		public string Description { get; }

		public ConstantAttribute(int version, string description)
		{
			Version = version;
			Description = description;
		}
	}

	public struct VersionedValue
	{
		public object value;
		public int version;
	}

	[StaticConstructorOnStartup]
	public static class Constants
	{
		public static readonly Dictionary<string, VersionedValue> defaultValues;
		static readonly string SettingsFilePath = Path.Combine(GenFilePaths.ConfigFolderPath, "ZombielandAdvancedSettings.json");

		static Constants()
		{
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

			if (BodyTypeDefOf.Child != null)
				ELECTRIC_GLOWING.Add(BodyTypeDefOf.Child, new Material[]
				{
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Child_east", ShaderDatabase.Mote, Color.white),
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Child_north", ShaderDatabase.Mote, Color.white),
					MaterialPool.MatFrom("Electrifier/Glowing/Naked_Child_south", ShaderDatabase.Mote, Color.white),
				});

			// merge settings
			defaultValues = Current();
			var settings = Load() ?? defaultValues;
			foreach (var newSetting in defaultValues)
			{
				if (settings.ContainsKey(newSetting.Key) == false)
					settings.Add(newSetting.Key, newSetting.Value);
				else
				{
					var oldSetting = settings[newSetting.Key];
					if (newSetting.Value.version > oldSetting.version)
						oldSetting.value = newSetting.Value.value;
				}
			}
			Save(settings);
			Apply(settings);
		}

		// grid debugging
		//
		[Constant(1, "Enable to see how zombies track enemies")]
		public static bool SHOW_PHEROMONE_GRID = false;
		[Constant(1, "Enable to show player reachable regions (for zombie spawning)")]
		public static bool SHOW_PLAYER_REACHABLE_REGIONS = false;
		[Constant(1, "Enable to see how colonists avoid zombies")]
		public static bool SHOW_AVOIDANCE_GRID = false;
		[Constant(1, "Enable to see how raging and dark zombies path towards colonists")]
		public static bool SHOW_NORMAL_PATHING_GRID = false;
		[Constant(1, "Enable to see how tank zombies path towards colonists")]
		public static bool SHOW_DIRECT_PATHING_GRID = false;

		// general debugging/testing
		//
		[Constant(1, "Turn zombie sounds on/off")]
		public static bool USE_SOUND = true;
		[Constant(1, "Set this to greater than -1 to debug the number of zombies on the map")]
		public static int DEBUG_MAX_ZOMBIE_COUNT = -1;

		// timing
		//
		[Constant(1, "The fade-off (in game-seconds) of the traces every enemy leaves behind. Set this lower will make zombies chaise you less because they loose track of you")]
		public static float PHEROMONE_FADEOFF = 180f;
		[Constant(1, "Sets how often the map is analyzed (in ticks) to find the center-of-interest and to sort zombies into a priority list in case we need to restrain cpu time")]
		public static int TICKMANAGER_RECALCULATE_DELAY = 900;
		[Constant(1, "Sets how often (in ticks) the center of interest is changed")]
		public static int CENTER_OF_INTEREST_UPDATE = 5000;
		[Constant(1, "How often (in game-seconds) the avoid grid is updated")]
		public static float TICKMANAGER_AVOIDGRID_DELAY = 0.25f;
		[Constant(1, "How long (in ticks) to wait between each bite when zombies eat")]
		public static int EAT_DELAY_TICKS = 1200;

		// zombie spawning
		[Constant(1, "This controls the hours of the day/night cycle for zombies (hours keep adding +1 after 23)")]
		public static int[] ZOMBIE_SPAWNING_HOURS = new[] { 18, 22, 28, 32 };

		// zombie stats
		//
		[Constant(1, "The distance within zombies will be aware of animals")]
		public static float ANIMAL_PHEROMONE_RADIUS = 2f;
		[Constant(1, "The distance within zombies will be aware of non-animals")]
		public static float HUMAN_PHEROMONE_RADIUS = 4f;
		[Constant(1, "The radius around tanky zombies which will draw ordinary zombies with the tankys movement")]
		public static float TANKY_PHEROMONE_RADIUS = 6f;
		[Constant(1, "The hit chance a zombie has when he is not tracking anything")]
		public static float ZOMBIE_HIT_CHANCE_IDLE = 0.2f;
		[Constant(1, "The hit chance a zombie has when he is tracking something")]
		public static float ZOMBIE_HIT_CHANCE_TRACKING = 0.7f;
		[Constant(1, "The number of cells out of the 8 surrounding cells of a zombie that get selected for moving")]
		public static int NUMBER_OF_TOP_MOVEMENT_PICKS = 3;
		[Constant(1, "The chance a zombie chooses a random movement when raging (multiplied by the number of zombies on the current and the destination positions together)")]
		public static float CLUSTER_AVOIDANCE_CHANCE = 0.25f;
		[Constant(1, "The chance a raging zombie does not go straight to their goal thus spreading them out a bit to keep it organic")]
		public static float DIVERTING_FROM_RAGE = 0.1f;
		[Constant(1, "Grouping of zombies, the higher the number the quicker will zombies loose interest in a trace if there are many zombies close to each other")]
		public static int ZOMBIE_CLOGGING_FACTOR = 10000;
		[Constant(1, "When zombies kill, this radius is applied to disburst them from the target in a random way")]
		public static float KILL_CIRCLE_RADIUS_MULTIPLIER = 0f;
		[Constant(1, "Muzzle flash value to base all other \"how loud\" calculations on")]
		public static float BASE_MUZZLE_FLASH_VALUE = 9f;
		[Constant(1, "How many zombies do need to stand close around a zombie to trigger it to become raging")]
		public static float SURROUNDING_ZOMBIES_TO_TRIGGER_RAGE = 14f;
		[Constant(1, "When easy kill is turned on, what is the chance to skip \"missed a shot\" on long distance shots from a colonist")]
		public static float COLONISTS_HIT_ZOMBIES_CHANCE = 0.9f;
		[Constant(1, "With Combat Extended, sets the output of the method CombatExtended/ArmorUtilityCE.cs:GetPenetrationValue()")]
		public static float COMBAT_EXTENDED_ARMOR_PENETRATION = 0.1f;
		[Constant(1, "The time (in ticks) between healing a zombie wounds")]
		public static int ZOMBIE_HEALING_TICKS = 3000;

		// zombie incidents
		//
		[Constant(1, "The area radius in where a spawn incident will take place")]
		public static int SPAWN_INCIDENT_RADIUS = 10;
		[Constant(1, "Tthe number of spawnable cells for a spawn area to be considered")]
		public static int MIN_ZOMBIE_SPAWN_CELL_COUNT = 6;

		// misc
		//
		[Constant(1, "Zombies will detect a fired weapon within this range")]
		public static float[] WEAPON_RANGE = new[] { 2f, 30f };
		[Constant(1, "Minimum distance (squared) between a zombie and a pawn for allowing the zombie to attack the pawn")]
		public static float MIN_ATTACKDISTANCE_SQUARED = 2.25f;
		[Constant(1, "Lower bound of consciousness for zombies to get confused and incapable of action/movement")]
		public static float MIN_CONSCIOUSNESS = 0.25f;
		[Constant(1, "Maximum distance (squared) between colonist and zombie while roping")]
		public static float MAX_ROPING_DISTANCE_SQUARED = 144f;

		// -- other constants --

		public static readonly int MIN_DELTA_TICKS = 20;
		public static readonly int MAX_DELTA_TICKS = 4;
		public static readonly int RUBBLE_AMOUNT = 50;
		public static readonly float MAX_HEIGHT = 0.6f;
		public static readonly float MIN_SCALE = 0.05f;
		public static readonly float MAX_SCALE = 0.3f;
		public static readonly float ZOMBIE_LAYER = Altitudes.AltitudeFor(AltitudeLayer.Pawn) - 0.005f;
		public static readonly float EMERGE_DELAY = 0.8f;

		public static readonly Material RAGE_EYE = MaterialPool.MatFrom("RageEye", ShaderDatabase.Mote);
		public static readonly Material BOMB_LIGHT = MaterialPool.MatFrom("BombLight", ShaderDatabase.MoteGlow);
		public static readonly Material[][] TANKYSHIELDS = Tools.GetDamageableGraphics("TankyShield", 2, 4);
		public static readonly Material[][] TANKYHELMETS = Tools.GetDamageableGraphics("TankyHelmet", 3, 4);
		public static readonly Material[][] MINERHELMET = Tools.GetDamageableGraphics("MinerHelmet", 4, 0);
		public static readonly Material[][] TANKYSUITS = Tools.GetDamageableGraphics("TankySuit", 3, 4);
		public static readonly Dictionary<CameraZoomRange, Material> RAGE_AURAS = new()
		{
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
		public static Dictionary<BodyTypeDef, Material[]> ELECTRIC_GLOWING = new()
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

		public static readonly System.Random random = new();

		public static readonly Mesh screamMesh = MeshMakerPlanes.NewPlaneMesh(8f);
		public static readonly Mesh healMesh = MeshMakerPlanes.NewPlaneMesh(4f);

		public static Texture2D dot = new(3, 3);
		public static Texture2D timeArrow = ContentFinder<Texture2D>.Get("TimeArrow", true);
		public static Texture2D[] timeKnob = new[] { ContentFinder<Texture2D>.Get("TimeKnob", true), ContentFinder<Texture2D>.Get("TimeKnobSelected", true) };
		public static Texture2D timeKnobHighlight = ContentFinder<Texture2D>.Get("TimeKnobHighlight", true);

		public static readonly Texture2D healthBarFrame = SolidColorMaterials.NewSolidColorTexture(Color.black);
		public static readonly Color healthBarBG = new(1, 1, 1, 0.25f);

		public static Texture2D zoneZombie = ContentFinder<Texture2D>.Get("ZoneZombie", true);
		public static Texture2D blood = ContentFinder<Texture2D>.Get("Blood", true);

		public static readonly Texture2D[] Chainsaw = new[]
		{
			ContentFinder<Texture2D>.Get("Chainsaw0", true),
			ContentFinder<Texture2D>.Get("Chainsaw1", true),
		};

		public static readonly Texture2D toggledOff = ContentFinder<Texture2D>.Get("ToggledOff", true);
		public static readonly Texture2D toggledOn = ContentFinder<Texture2D>.Get("ToggledOn", true);

		public static List<(string name, FieldInfo field, ConstantAttribute attr)> AllSettings
		{
			get
			{
				return AccessTools.GetFieldNames(typeof(Constants))
					.Select(name =>
					{
						var field = AccessTools.Field(typeof(Constants), name);
						var attr = (ConstantAttribute)field.GetCustomAttribute(typeof(ConstantAttribute));
						return (name, field, attr);
					})
					.Where(info => info.attr != null)
					.ToList();
			}
		}

		public static Dictionary<string, VersionedValue> Current()
		{
			return AllSettings
				.Select(info => (info.name, value: new VersionedValue() { value = info.field.GetValue(null), version = info.attr.Version }))
				.ToDictionary(pair => pair.name, pair => pair.value);
		}

		public static Dictionary<string, VersionedValue> Load()
		{
			if (File.Exists(SettingsFilePath) == false)
				return new Dictionary<string, VersionedValue>();

			var serializer = new JsonSerializer();
			using var reader = new StreamReader(SettingsFilePath);
			using var jsonReader = new JsonTextReader(reader);
			return serializer.Deserialize<Dictionary<string, VersionedValue>>(jsonReader);
		}

		public static void Save(Dictionary<string, VersionedValue> dict)
		{
			var serializer = new JsonSerializer();
			using var writer = new StreamWriter(SettingsFilePath);
			using var jsonWriter = new JsonTextWriter(writer);
			serializer.Serialize(jsonWriter, dict);
		}

		public static void Apply(Dictionary<string, VersionedValue> dict)
		{
			var trv = Traverse.Create(typeof(Constants));
			foreach (var info in dict)
			{
				var value = info.Value.value;
				if (value is double doubleValue)
					value = (float)doubleValue;
				if (value is long longValue)
					value = (int)longValue;
				if (value is JArray jArray)
				{
					var type = trv.Field(info.Key).GetValueType().GetElementType();
					if (type == typeof(int))
						value = jArray.Select(jToken => (int)jToken).ToArray();
					if (type == typeof(float))
						value = jArray.Select(jToken => (float)jToken).ToArray();
				}
				try
				{
					_ = trv.Field(info.Key).SetValue(value);
				}
				catch (Exception ex)
				{
					Log.Error($"Ex: {info.Key}:{info.Value.value} => {ex.Message}");
				}
			}
		}
	}
}
