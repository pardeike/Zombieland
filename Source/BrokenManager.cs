using System.Collections.Generic;
using Verse;

namespace ZombieLand
{
	public class BrokenManager : MapComponent
	{
		//private readonly List<CompBreakable> comps = new();
		public readonly HashSet<Thing> brokenThings = new();

		public BrokenManager(Map map) : base(map) { }

		public void Register(CompBreakable c)
		{
			//comps.Add(c);
			if (c.broken)
				_ = brokenThings.Add(c.parent);
		}

		public void Deregister(CompBreakable c)
		{
			//_ = comps.Remove(c);
			_ = brokenThings.Remove(c.parent);
		}

		public void Notify_BrokenDown(Thing thing)
		{
			_ = brokenThings.Add(thing);
		}

		public void Notify_Repaired(Thing thing)
		{
			_ = brokenThings.Remove(thing);
		}
	}
}
