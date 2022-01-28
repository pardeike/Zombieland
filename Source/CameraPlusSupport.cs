using UnityEngine;
using Verse;

namespace CameraPlusSupport
{
	[StaticConstructorOnStartup]
	class Methods
	{
		static Color defaultColor = new Color(0.3019608f, 0.215686277f, 0.0431372561f);
		static Color minerColor = new Color(0.8745098f, 0.5411765f, 0.168627456f);
		static Color tankyColor = new Color(0.294117659f, 0.294117659f, 0.294117659f);
		static Color darkColor = new Color(0.1f, 0.1f, 0.1f);
		static Color bombBlinkColor = new Color(1f, 0f, 0.2901961f);
		static Color turnedColor = new Color(0.105882354f, 0.403921574f, 0.105882354f);
		static Color trackColor = new Color(1f, 0.8509804f, 0.8509804f);
		static Color rageColor = new Color(1f, 0.6509804f, 0.6509804f);

		static readonly Texture2D innerMarkerTexture = ContentFinder<Texture2D>.Get("InnerCameraMarker", true);
		static readonly Texture2D outerMarkerTexture = ContentFinder<Texture2D>.Get("OuterCameraMarker", true);

		// return colors as [inner, outer] to display marker, return <null> to show label instead
		//
		static Color[] GetCameraPlusColors(Pawn pawn)
		{
			if (!(pawn is ZombieLand.Zombie zombie)) return null;
			if (zombie.state == ZombieLand.ZombieState.Floating) return null;

			var innerColor = defaultColor;
			if (zombie.isToxicSplasher)
				innerColor = Color.green;
			if (zombie.isElectrifier)
				innerColor = Color.cyan;
			else if (zombie.isAlbino)
				innerColor = Color.white;
			else if (zombie.isHealer)
				innerColor = Color.cyan;
			else if (zombie.isDarkSlimer)
				innerColor = darkColor;
			else if (zombie.isMiner)
				innerColor = minerColor;
			else if (zombie.IsTanky)
				innerColor = tankyColor;
			else if (zombie.wasMapPawnBefore)
				innerColor = turnedColor;
			else if (zombie.IsSuicideBomber)
			{
				var tm = Find.TickManager;
				var currentTick = tm.TicksAbs;
				var interval = (int)zombie.bombTickingInterval;
				if (currentTick >= zombie.lastBombTick + interval)
					zombie.lastBombTick = currentTick;
				if (currentTick <= zombie.lastBombTick + interval / 2)
					innerColor = bombBlinkColor;
			}

			var outerColor = Color.white;
			if (zombie.state == ZombieLand.ZombieState.Tracking)
				outerColor = trackColor;
			if (zombie.raging > 0)
				outerColor = rageColor;

			return new Color[] { innerColor, outerColor };
		}

		// return textures as [inner, outer] or <null> for default textures (size should be 64 x 64)
		// this method is optional
		//
		static Texture2D[] GetCameraPlusMarkers(Pawn pawn)
		{
			if (!(pawn is ZombieLand.Zombie zombie)) return null;
			return new Texture2D[] { innerMarkerTexture, outerMarkerTexture };
		}
	}
}
