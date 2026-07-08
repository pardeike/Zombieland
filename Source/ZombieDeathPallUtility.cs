using RimWorld;
using System;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public static class ZombieDeathPallUtility
	{
		public static bool CanDeathPallRaise(Corpse corpse, bool ignoreIndoors)
		{
			if (ModsConfig.AnomalyActive == false)
				return false;
			if (corpse is not ZombieCorpse zombieCorpse)
				return false;
			if (corpse.Destroyed || corpse.Spawned == false || corpse.Map == null)
				return false;
			if (zombieCorpse.InnerPawn is not Zombie zombie)
				return false;
			if (zombie.Dead == false)
				return false;
			if (zombie.IsMutant)
				return false;
			if (ignoreIndoors == false && IsIndoorDeathPallCorpse(corpse))
				return false;
			return zombieCorpse.Appearance != null;
		}

		public static bool TryRaiseZombieCorpse(Corpse corpse, out Zombie newZombie)
		{
			newZombie = null;
			if (corpse is not ZombieCorpse zombieCorpse)
				return false;
			if (zombieCorpse.Destroyed || zombieCorpse.Spawned == false)
				return false;

			var oldZombie = zombieCorpse.InnerPawn as Zombie;
			var map = zombieCorpse.Map;
			if (oldZombie == null || map == null)
				return false;

			var cell = zombieCorpse.Position;
			if (cell.IsValid == false)
				return false;

			var snapshot = zombieCorpse.Appearance ?? ZombieCorpseAppearance.From(oldZombie);
			if (snapshot == null)
				return false;

			var rotation = oldZombie.Rotation;
			var tickManager = map.GetComponent<TickManager>();
			Zombie spawnedZombie = null;
			var iterator = ZombieGenerator.SpawnZombieIterativ(cell, map, snapshot.SpawnType, zombie =>
			{
				snapshot.ApplyTo(zombie);
				// Match infected human corpses: Death Pall raises the stronger former-pawn zombie variant.
				zombie.wasMapPawnBefore = true;
				ZombieGenerator.AssignNewGraphics(zombie);
				TransferApparel(oldZombie, zombie);
				zombie.Rotation = rotation;
				zombie.rubbleCounter = Constants.RUBBLE_AMOUNT;
				zombie.SetState(ZombieState.Wandering);
				_ = tickManager?.allZombiesCached?.Remove(oldZombie);
				_ = tickManager?.allZombiesCached?.Add(zombie);
				spawnedZombie = zombie;
			});

			while (iterator.MoveNext())
			{
			}

			newZombie = spawnedZombie;
			if (newZombie == null)
				return false;

			if (zombieCorpse.Destroyed == false)
				zombieCorpse.Destroy(DestroyMode.Vanish);
			return true;
		}

		static void TransferApparel(Zombie source, Zombie target)
		{
			if (source?.apparel == null || target?.apparel == null)
				return;

			Apparel[] wornApparel;
			try
			{
				wornApparel = source.apparel.WornApparel.ToArray();
			}
			catch
			{
				return;
			}

			var transferableApparel = wornApparel
				.Where(apparel => CanTransferApparel(apparel, target, source))
				.ToArray();
			if (transferableApparel.Length == 0)
				return;

			try
			{
				target.apparel.DestroyAll();
			}
			catch
			{
				return;
			}

			foreach (var apparel in transferableApparel)
				TryTransferApparel(source, target, apparel);
		}

		static bool CanTransferApparel(Apparel apparel, Zombie target, Pawn allowedCurrentWearer = null)
		{
			try
			{
				if (apparel == null || apparel.Destroyed || target?.apparel == null)
					return false;
				if (apparel.def == null || apparel.def.IsApparel == false || apparel.def.apparel == null)
					return false;
				if (apparel.GetType() != typeof(Apparel))
					return false;
				if (apparel.Wearer != null && apparel.Wearer != allowedCurrentWearer)
					return false;
				if (apparel.def.apparel.wornGraphicPath.NullOrEmpty())
					return false;
				if (target.story?.bodyType == null || target.RaceProps?.body == null)
					return false;
				if (apparel.def.apparel.developmentalStageFilter.Has(target.DevelopmentalStage) == false)
					return false;
				if (ApparelUtility.HasPartsToWear(target, apparel.def) == false)
					return false;
				if (CompBiocodable.IsBiocoded(apparel) && CompBiocodable.IsBiocodedFor(apparel, target) == false)
					return false;
				if (apparel.PawnCanWear(target, true) == false)
					return false;
				if (ZombieGenerator.TryResolveApparelGraphic(apparel, target.story.bodyType) == false)
					return false;
				return true;
			}
			catch
			{
				return false;
			}
		}

		static void TryTransferApparel(Zombie source, Zombie target, Apparel apparel)
		{
			Apparel movedApparel = null;
			try
			{
				if (source?.apparel == null || target?.apparel == null || source.MapHeld == null)
					return;
				if (CanTransferApparel(apparel, target, source) == false)
					return;
				if (source.apparel.TryDrop(apparel, out movedApparel, source.PositionHeld, false) == false)
					return;
				if (CanTransferApparel(movedApparel, target) == false)
				{
					DestroyLooseApparel(movedApparel);
					return;
				}

				var oldSuppressError = Patches.Graphic_Multi_Init_Patch.suppressError;
				var oldTextureError = Patches.Graphic_Multi_Init_Patch.textureError;
				try
				{
					Patches.Graphic_Multi_Init_Patch.suppressError = true;
					Patches.Graphic_Multi_Init_Patch.textureError = false;
					target.apparel.Wear(movedApparel, false);
				}
				catch
				{
				}
				finally
				{
					if (Patches.Graphic_Multi_Init_Patch.textureError && target.apparel.Contains(movedApparel))
						target.apparel.Remove(movedApparel);
					Patches.Graphic_Multi_Init_Patch.suppressError = oldSuppressError;
					Patches.Graphic_Multi_Init_Patch.textureError = oldTextureError;
				}

				if (target.apparel.Contains(movedApparel))
					movedApparel.SetForbidden(false, false);
				else
					DestroyLooseApparel(movedApparel);
			}
			catch
			{
				DestroyLooseApparel(movedApparel);
			}
		}

		static void DestroyLooseApparel(Apparel apparel)
		{
			try
			{
				if (apparel != null && apparel.Destroyed == false && apparel.Wearer == null)
					apparel.Destroy();
			}
			catch
			{
			}
		}

		static bool IsIndoorDeathPallCorpse(Corpse corpse)
		{
			var map = corpse.MapHeld;
			var cell = corpse.PositionHeld;
			if (map == null || cell.IsValid == false)
				return true;

			var room = cell.GetRoom(map);
			return room != null
				&& cell.Roofed(map)
				&& (room.ProperRoom || room.IsDoorway);
		}
	}
}
