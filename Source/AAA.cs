using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace ZombieLand
{
	public class FOO
	{
		static List<Need> AllNeeds
		{
			get
			{
				return new List<Need>();
			}
		}
	}

	public class ZZZ : FOO
	{
	}

	public class AAA
	{
		public FOO pawn;

		public AAA()
		{
			pawn = new ZZZ();
		}

		public void Test(IntVec3 vec, String str, ref int nr)
		{
			if (pawn is ZZZ)
			{
				AAAPatch.TestPatched(this, ref pawn, vec, str, ref nr);
				return;
			}

			Console.WriteLine("hello");
		}
	}

	public class AAAPatch
	{
		public static void TestPatched(AAA instance, ref FOO zombie, IntVec3 vec, String str, ref int nr)
		{
			if (zombie == null)
				zombie = new ZZZ();
		}
	}
}