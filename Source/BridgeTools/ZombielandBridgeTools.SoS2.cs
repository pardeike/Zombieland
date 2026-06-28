using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI.Group;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/sos2_ship_hologram_state", Description = "Create a compact Save Our Ship 2 ship-generation and hologram-exclusion fixture through the current SoS2 runtime.")]
		public static object SoS2ShipHologramState(
			[ToolParameter(Description = "Number of hostile human pawns to place in the reflected SoS2 test ship.", Required = false, DefaultValue = 6)] int pawnCount = 6,
			[ToolParameter(Description = "Deterministic Verse.Rand seed used during ship generation and Zombieland postfix execution.", Required = false, DefaultValue = 76231)] int seed = 76231)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			if (SoSTools.isInstalled == false)
				return new { success = false, error = "Save Our Ship 2 is not installed." };

			var generateShip = AccessTools.Method("SaveOurShip2.ShipInteriorMod2:GenerateShip");
			var shipDefType = AccessTools.TypeByName("SaveOurShip2.ShipDef");
			var shipShapeType = AccessTools.TypeByName("SaveOurShip2.ShipShape");
			if (generateShip == null || shipDefType == null || shipShapeType == null)
				return new
				{
					success = false,
					error = "Current Save Our Ship 2 ship-generation members were not resolved.",
					generateShipResolved = generateShip != null,
					shipDefTypeResolved = shipDefType != null,
					shipShapeTypeResolved = shipShapeType != null
				};

			var hostileFaction = Find.FactionManager.AllFactionsVisible
				.FirstOrDefault(faction => faction != null && faction.HostileTo(Faction.OfPlayer) && faction.def?.humanlikeFaction == true)
				?? Find.FactionManager.AllFactionsVisible.FirstOrDefault(faction => faction != null && faction.HostileTo(Faction.OfPlayer))
				?? Faction.OfAncientsHostile;
			if (hostileFaction == null)
				return new { success = false, error = "No hostile faction was available for the SoS2 ship fixture." };

			var clampedPawnCount = Math.Max(2, Math.Min(pawnCount, 12));
			var offsetX = Math.Max(4, map.Size.x / 2 - 8);
			var offsetZ = Math.Max(4, map.Size.z / 2 - 8);
			var initialZombieIds = CurrentZombies(map).Select(ZombieRuntimeActions.StableThingId).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var initialPawns = map.mapPawns.AllPawnsSpawned.ToHashSet();

			var oldInfectedRaidsChance = ZombieSettings.Values.infectedRaidsChance;
			var oldBaseNumber = ZombieSettings.Values.baseNumberOfZombiesinEvent;
			var oldColonyMultiplier = ZombieSettings.Values.colonyMultiplier;
			var oldMaximum = ZombieSettings.Values.maximumNumberOfZombies;
			var oldDynamicThreat = ZombieSettings.Values.useDynamicThreatLevel;

			Lord lord = null;
			Pawn hologramPawn = null;
			Hediff hologramHediff = null;
			var hologramSafeRemoveField = AccessTools.Field("SaveOurShip2.HediffPawnIsHologram:SafeRemoveFlag");
			var previousSafeRemoveFlag = hologramSafeRemoveField?.GetValue(null);
			var cleanupThings = new List<Thing>();
			var safeRemoveEnabled = false;

			try
			{
				ZombieSettings.Values.infectedRaidsChance = 1f;
				ZombieSettings.Values.baseNumberOfZombiesinEvent = 3;
				ZombieSettings.Values.colonyMultiplier = 1f;
				ZombieSettings.Values.maximumNumberOfZombies = Math.Max(100, CurrentZombies(map).Length + 50);
				ZombieSettings.Values.useDynamicThreatLevel = false;

				lord = LordMaker.MakeNewLord(hostileFaction, new LordJob_AssaultColony(hostileFaction, canKidnap: false, canTimeoutOrFlee: false), map);
				var reflectedShipDef = CreateReflectedSoS2ShipDef(shipDefType, shipShapeType, clampedPawnCount);
				var shipArgs = new object[]
				{
					reflectedShipDef,
					map,
					null,
					hostileFaction,
					lord,
					null,
					true,
					true,
					0,
					offsetX,
					offsetZ,
					null,
					false,
					false
				};

				Rand.PushState(seed);
				try
				{
					generateShip.Invoke(null, shipArgs);
				}
				finally
				{
					Rand.PopState();
				}

				var shipPawnsAfter = map.mapPawns.AllPawnsSpawned
					.Except(initialPawns)
					.Where(pawn => ZombieAreaManager.IsZombielandPawn(pawn) == false)
					.ToArray();
				var newZombies = CurrentZombies(map)
					.Where(zombie => initialZombieIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
					.ToArray();
				var convertedZombies = newZombies.OfType<Zombie>().Where(zombie => zombie.wasMapPawnBefore).ToArray();
				var generatedZombies = newZombies.OfType<Zombie>().Where(zombie => zombie.wasMapPawnBefore == false).ToArray();

				cleanupThings.AddRange(shipPawnsAfter);
				cleanupThings.AddRange(newZombies);

				var hologramDef = DefDatabase<HediffDef>.GetNamedSilentFail("SoSHologram");
				var hologramCell = IntVec3.Invalid;
				var hologramBefore = false;
				var hologramAfter = false;
				var hologramAttracts = true;
				var hologramConverted = false;
				object hologramSpawnError = null;
				if (hologramDef != null && TryFindClearSpawnCell(map, map.Center + new IntVec3(14, 0, 0), 24f, out hologramCell, out hologramSpawnError))
				{
					hologramPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, hostileFaction);
					hologramPawn.Name = new NameTriple("ZL SoS2", "Hologram", "Fixture");
					GenSpawn.Spawn(hologramPawn, hologramCell, map, Rot4.South);
					cleanupThings.Add(hologramPawn);

					hologramHediff = HediffMaker.MakeHediff(hologramDef, hologramPawn);
					hologramPawn.health.AddHediff(hologramHediff);
					hologramBefore = SoSTools.IsHologram(hologramPawn);
					hologramAttracts = Customization.DoesAttractsZombies(hologramPawn);
					Tools.ConvertToZombie(hologramPawn, map, true);
					hologramAfter = hologramPawn.Destroyed == false && SoSTools.IsHologram(hologramPawn);
					hologramConverted = hologramPawn is Zombie || CurrentZombies(map).Any(zombie => zombie.Position == hologramCell && ZombieRuntimeActions.StableThingId(zombie) != ZombieRuntimeActions.StableThingId(hologramPawn));
				}

				var shipSuccess = newZombies.Length > 0 && convertedZombies.Length > 0 && generatedZombies.Length > 0;
				var hologramSuccess = hologramDef != null
					&& hologramSpawnError == null
					&& hologramBefore
					&& hologramAfter
					&& hologramAttracts == false
					&& hologramConverted == false;

				return new
				{
					success = shipSuccess && hologramSuccess,
					mapId = map.uniqueID,
					mapBiome = map.Biome?.defName,
					shipGeneration = new
					{
						success = shipSuccess,
						generateShip = generateShip.FullDescription(),
						shipDefType = shipDefType.FullName,
						pawnCount = clampedPawnCount,
						hostileFaction = hostileFaction.def?.defName,
						seed,
						offset = ZombieRuntimeActions.DescribeCell(new IntVec3(offsetX, 0, offsetZ)),
						remainingShipPawnCount = shipPawnsAfter.Length,
						lordOwnedPawnCount = lord?.ownedPawns?.Count ?? 0,
						newZombieCount = newZombies.Length,
						convertedZombieCount = convertedZombies.Length,
						generatedZombieCount = generatedZombies.Length,
						samples = newZombies.Take(12).Select(DescribeZombie).ToArray()
					},
					hologram = new
					{
						success = hologramSuccess,
						hediffDef = hologramDef?.defName,
						cell = hologramCell.IsValid ? ZombieRuntimeActions.DescribeCell(hologramCell) : null,
						spawnError = hologramSpawnError,
						isHologramBeforeConvert = hologramBefore,
						isHologramAfterConvert = hologramAfter,
						attractsZombies = hologramAttracts,
						converted = hologramConverted,
						pawn = DescribePawn(hologramPawn)
					}
				};
			}
			catch (Exception ex)
			{
				return new
				{
					success = false,
					error = ex.Message,
					exceptionType = ex.GetType().FullName,
					stackTrace = ex.StackTrace
				};
			}
			finally
			{
				ZombieSettings.Values.infectedRaidsChance = oldInfectedRaidsChance;
				ZombieSettings.Values.baseNumberOfZombiesinEvent = oldBaseNumber;
				ZombieSettings.Values.colonyMultiplier = oldColonyMultiplier;
				ZombieSettings.Values.maximumNumberOfZombies = oldMaximum;
				ZombieSettings.Values.useDynamicThreatLevel = oldDynamicThreat;

				if (hologramHediff != null && hologramPawn?.health?.hediffSet?.hediffs?.Contains(hologramHediff) == true)
				{
					hologramSafeRemoveField?.SetValue(null, true);
					safeRemoveEnabled = true;
					hologramPawn.health.RemoveHediff(hologramHediff);
				}
				if (safeRemoveEnabled && hologramSafeRemoveField != null)
					hologramSafeRemoveField.SetValue(null, previousSafeRemoveFlag ?? false);

				foreach (var thing in cleanupThings.Distinct().ToArray())
				{
					if (thing != null && thing.Destroyed == false)
						thing.Destroy(DestroyMode.Vanish);
				}
				if (lord != null)
					map.lordManager.RemoveLord(lord);
			}
		}

		static Def CreateReflectedSoS2ShipDef(Type shipDefType, Type shipShapeType, int pawnCount)
		{
			var shipDef = (Def)Activator.CreateInstance(shipDefType);
			shipDef.defName = "ZLRuntimeSoS2ShipFixture";
			shipDef.label = "Zombieland runtime SoS2 ship fixture";
			SetReflectedField(shipDef, "saveSysVer", 2);
			SetReflectedField(shipDef, "sizeX", 18);
			SetReflectedField(shipDef, "sizeZ", 18);
			SetReflectedField(shipDef, "neverRandom", true);
			SetReflectedField(shipDef, "neverWreck", true);
			SetReflectedField(shipDef, "neverFleet", true);

			var listType = typeof(List<>).MakeGenericType(shipShapeType);
			var parts = (IList)Activator.CreateInstance(listType);
			for (var i = 0; i < pawnCount; i++)
				parts.Add(CreateReflectedSoS2PawnShape(shipShapeType, 3 + i % 4, 3 + i / 4, "Pirate"));
			SetReflectedField(shipDef, "parts", parts);
			return shipDef;
		}

		static object CreateReflectedSoS2PawnShape(Type shipShapeType, int x, int z, string pawnKindDefName)
		{
			var shape = Activator.CreateInstance(shipShapeType);
			SetReflectedField(shape, "shapeOrDef", "PawnSpawnerGeneric");
			SetReflectedField(shape, "stuff", pawnKindDefName);
			SetReflectedField(shape, "x", x);
			SetReflectedField(shape, "z", z);
			SetReflectedField(shape, "rot", Rot4.South);
			return shape;
		}

		static void SetReflectedField(object instance, string fieldName, object value)
		{
			var field = AccessTools.Field(instance.GetType(), fieldName);
			if (field == null)
				throw new MissingFieldException(instance.GetType().FullName, fieldName);
			field.SetValue(instance, value);
		}
	}
}
