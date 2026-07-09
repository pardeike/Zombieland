using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public class AlbinoSabotageMemory : MapComponent
	{
		List<Thing> enoughHackedItems = new();
		HashSet<Thing> enoughHackedItemSet = new();
		int lastCleanupTick = -1;

		public AlbinoSabotageMemory(Map map) : base(map)
		{
		}

		public static AlbinoSabotageMemory GetOrCreate(Map map)
		{
			if (map == null)
				return null;

			var memory = map.GetComponent<AlbinoSabotageMemory>();
			if (memory != null)
				return memory;

			memory = new AlbinoSabotageMemory(map);
			map.components?.Add(memory);
			return memory;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			if (Scribe.mode == LoadSaveMode.Saving)
				CleanupEnoughHackedItems(true);
			Scribe_Collections.Look(ref enoughHackedItems, "albinoEnoughHackedItems", LookMode.Reference);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
				CleanupEnoughHackedItems(true);
		}

		public static bool IsEnoughHackedItemCandidate(Thing thing)
		{
			return thing?.def?.IsRangedWeapon == true && thing.def.useHitPoints;
		}

		public bool IsEnoughHackedItem(Thing thing)
		{
			if (IsEnoughHackedItemCandidate(thing) == false)
				return false;

			CleanupEnoughHackedItems();
			return IsValidRememberedItem(thing) && enoughHackedItemSet.Contains(thing);
		}

		public void RememberEnoughHackedItem(Thing thing)
		{
			if (IsEnoughHackedItemCandidate(thing) == false || BelongsToThisMap(thing) == false)
				return;

			CleanupEnoughHackedItems();
			if (enoughHackedItemSet.Add(thing))
				enoughHackedItems.Add(thing);
		}

		public Thing[] EnoughHackedItemsSnapshot()
		{
			CleanupEnoughHackedItems();
			return enoughHackedItems.ToArray();
		}

		public int EnoughHackedItemCount()
		{
			CleanupEnoughHackedItems();
			return enoughHackedItems.Count;
		}

		public void CleanupEnoughHackedItems(bool force = false)
		{
			enoughHackedItems ??= new List<Thing>();
			var tick = Find.TickManager?.TicksGame ?? -1;
			if (force == false && tick >= 0 && lastCleanupTick == tick)
				return;
			if (tick >= 0)
				lastCleanupTick = tick;

			var removed = enoughHackedItems.RemoveAll(thing => IsValidRememberedItem(thing) == false);
			if (force || removed > 0 || enoughHackedItemSet == null || enoughHackedItemSet.Count != enoughHackedItems.Count)
				enoughHackedItemSet = enoughHackedItems.ToHashSet();
		}

		bool IsValidRememberedItem(Thing thing)
		{
			return thing != null
				&& thing.Destroyed == false
				&& IsEnoughHackedItemCandidate(thing)
				&& BelongsToThisMap(thing);
		}

		bool BelongsToThisMap(Thing thing)
		{
			return thing != null && (thing.MapHeld == map || thing.Spawned && thing.Map == map);
		}
	}
}
