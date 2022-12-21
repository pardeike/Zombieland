using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public class SoSTools
	{
		public delegate bool IsHologramDelegate(Pawn pawn);

		public static bool isInstalled;
		public static BiomeDef sosOuterSpaceBiomeDef = null;
		public static WorldObjectDef sosShipOrbitingWorldObjectDef = null;
		public static IsHologramDelegate IsHologram = _ => false;

		static SoSTools()
		{
			var sosType = AccessTools.TypeByName("SaveOurShip2.ShipInteriorMod2");
			isInstalled = sosType != null;
			if (isInstalled)
			{
				sosOuterSpaceBiomeDef = DefDatabase<BiomeDef>.GetNamed("OuterSpaceBiome", false);
				sosShipOrbitingWorldObjectDef = DefDatabase<WorldObjectDef>.GetNamed("ShipOrbiting", false);

				var method = AccessTools.Method(sosType, "IsHologram");
				if (method != null)
					IsHologram = AccessTools.MethodDelegate<IsHologramDelegate>(AccessTools.Method(sosType, "IsHologram"));
			}
		}

		public class Floater
		{
			public const int backCount = 100;
			public const int foreCount = 10;

			public IntVec3 mapSize;
			public Material material;
			public Vector3 position = new Vector3(-1000, 0, -1000);
			public float angle = 0;
			public bool foreground;
			public float alpha = 1f;

			Vector3 drift;
			float rotation;
			float scale;
			float speed;
			int delay = Rand.Range(0, 900);

			public Vector2 Size => Vector2.one * scale;

			public void Update(int i, int count, Vector3 mousePos)
			{
				if (delay > 0)
				{
					delay--;
					return;
				}

				if (scale == 0 || position.x < -4 || position.z < -4 || position.x > mapSize.x + 4 || position.z > mapSize.z + 4)
				{
					var f = GenMath.LerpDoubleClamped(0, count - 1, 0, 1, i);
					f = f * f * f;

					var minSize = foreground ? 0.4f : 0.1f;
					var maxSize = foreground ? 3f : 0.2f;

					var minSpeed = foreground ? 1f : 0.15f;
					var maxSpeed = foreground ? 4f : 0.75f;

					scale = Mathf.Lerp(minSize, maxSize, foreground ? f * f : f);
					speed = Mathf.Lerp(minSpeed, maxSpeed, f * f * f);
					var yAltitute = (foreground ? 0.5f : -0.5f) + i / 1000f;
					angle = Rand.Range(0f, 359f);
					var rot = Rand.Chance(0.1f) ? new[] { 2f, 3f } : new[] { 0.1f, 0.4f };
					rotation = Rand.Range(rot[0], rot[1]) * Rand.Sign;

					switch (Rand.Int % 4)
					{
						case 0: // bottom
							position = new Vector3(Rand.Range(0, mapSize.x), yAltitute, -3.9f);
							drift = new Vector3(Rand.Range(-0.1f, 0.1f), 0, Rand.Range(0.05f, 0.15f));
							break;
						case 1: // top
							position = new Vector3(Rand.Range(0, mapSize.x), yAltitute, mapSize.z + 3.9f);
							drift = new Vector3(Rand.Range(-0.1f, 0.1f), 0, Rand.Range(-0.15f, -0.05f));
							break;
						case 2: // left
							position = new Vector3(-3.9f, yAltitute, Rand.Range(0, mapSize.z));
							drift = new Vector3(Rand.Range(0.05f, 0.15f), 0, Rand.Range(-0.15f, -0.05f));
							break;
						case 3: // right
							position = new Vector3(mapSize.x + 3.9f, yAltitute, Rand.Range(0, mapSize.z));
							drift = new Vector3(Rand.Range(-0.15f, -0.05f), 0, Rand.Range(-0.15f, -0.05f));
							break;
					}
				}

				angle += rotation;
				position += drift * speed;

				if (foreground)
				{
					var a = GenMath.LerpDoubleClamped(scale * 2, scale * 4, 0, 1, (mousePos - position).MagnitudeHorizontal());
					if (a != alpha)
					{
						alpha = a;
						material.color = new Color(1, 1, 1, alpha);
					}
				}
			}
		}
	}

	// adds zombies to ships if appropiate
	//
	[HarmonyPatch]
	static class RimWorld_ShipCombatManager_GenerateShip_Patch
	{
		static bool Prepare() => TargetMethod() != null;
		static MethodBase TargetMethod() => AccessTools.Method("RimWorld.ShipCombatManager:GenerateShip");

		public static void Postfix(Map map, TradeShip tradeShip, Faction fac, Lord lord)
		{
			if (tradeShip != null)
				return;
			if (fac == Faction.OfPlayer)
				return;
			if (Rand.Chance(ZombieSettings.Values.infectedRaidsChance) == false)
				return;

			var pawns = lord.ownedPawns.Where(p => p.RaceProps.Humanlike && p?.Faction.IsPlayerSafe() == false).ToList();
			var turned = pawns.Take(Rand.Range(0, pawns.Count)).ToList();
			foreach (var pawn in turned)
			{
				Tools.ConvertToZombie(pawn, map, true);
				lord.RemovePawn(pawn);
			}

			var zombieCount = Math.Min(
				Mathf.FloorToInt(
					(pawns.Count - turned.Count) * ZombieSettings.Values.colonyMultiplier * ZombieSettings.Values.baseNumberOfZombiesinEvent
				),
				ZombieSettings.Values.maximumNumberOfZombies
			);
			for (var i = 0; i < zombieCount; i++)
			{
				var room = map.regionGrid.allRooms.SafeRandomElement();
				var cell = room.Cells.Where(cell => cell.Standable(map)).SafeRandomElement();
				if (cell.IsValid)
				{
					var zombie = ZombieGenerator.SpawnZombie(cell, map, ZombieType.Normal);
					zombie.rubbleCounter = Constants.RUBBLE_AMOUNT;
					zombie.state = ZombieState.Wandering;
					zombie.Rotation = Rot4.Random;

					var tickManager = Find.CurrentMap.GetComponent<TickManager>();
					_ = tickManager.allZombiesCached.Add(zombie);
				}
			}
		}
	}

	// moves earth background mesh below zombie altitude
	//
	[HarmonyPatch]
	static class SaveOurShip2_GenerateSpaceSubMesh_GenerateMesh_Patch
	{
		static bool Prepare() => TargetMethod() != null;
		static MethodBase TargetMethod() => AccessTools.Method("SaveOurShip2.GenerateSpaceSubMesh:GenerateMesh");

		public static void PrintMeshWithChangedAltitute(SectionLayer layer, Matrix4x4 tsr, Mesh mesh, Material mat)
		{
			Vector3 position = tsr.GetColumn(3);
			position.y = -1f;
			tsr = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one);
			Printer_Mesh.PrintMesh(layer, tsr, mesh, mat);
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = SymbolExtensions.GetMethodInfo(() => Printer_Mesh.PrintMesh(default, default, default, default));
			var to = SymbolExtensions.GetMethodInfo(() => PrintMeshWithChangedAltitute(default, default, default, default));
			return instructions.MethodReplacer(from, to);
		}
	}
}
