using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	static class ZombieStains
	{
		public static int maxStainPoints = 6;

		static readonly Dictionary<string, int> stainsHead;
		static readonly Dictionary<string, int> stainsBody;

		static ZombieStains()
		{
			stainsHead = new Dictionary<string, int>
			{
				{ "Stains/Scratch", 4 },
				{ "Stains/Hole", 4 },
				{ "Stains/Stain2", 2 },
				{ "Stains/Stain1", 1 },
				{ "Stains/Scar", 1 }
			};
			stainsBody = new Dictionary<string, int>
			{
				{ "Stains/Ribs3", 5 },
				{ "Stains/Ribs2", 5 },
				{ "Stains/Ribs4", 4 },
				{ "Stains/Ribs1", 4 },
				{ "Stains/Chain", 4 },
				{ "Stains/Hole", 3 },
				{ "Stains/Scratch", 2 },
				{ "Stains/Stain2", 2 },
				{ "Stains/Stain1", 1 },
				{ "Stains/Scar", 1 }
			};
		}

		public static KeyValuePair<string, int> GetRandom(int remainingPoints, bool isBody)
		{
			var stains = isBody ? stainsBody : stainsHead;
			return stains.Where(st => st.Value <= remainingPoints).RandomElement();
		}
	}
}