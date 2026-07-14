using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	static class MainMenuBackgroundPostEffect
	{
		static readonly Color multiplyColor = ColorFromHex(0xEB, 0xAE, 0xAE);
		static readonly Color colorBurnColor = ColorFromHex(0xD4, 0xD4, 0xD4);
		static readonly Color darkerColor = ColorFromHex(0x48, 0xAA, 0x48);
		static readonly Texture2D titleTexture = Tools.LoadTexture("ZombielandTitle", true, false);
		static Material material;
		static bool disabledAfterError;

		public static void Draw()
		{
			if (disabledAfterError || Event.current?.type != EventType.Repaint)
				return;
			if (ZombielandMusic.DefaultSettingsAllowZombielandMusic() == false)
				return;

			var shader = Assets.MainMenuBackgroundEffectShader;
			if (shader == null)
				return;

			try
			{
				if (material == null || material.shader != shader)
				{
					material = new Material(shader)
					{
						name = "Zombieland main menu background effect",
						hideFlags = HideFlags.HideAndDontSave
					};
					material.SetColor("_MultiplyColor", multiplyColor);
					material.SetColor("_ColorBurnColor", colorBurnColor);
					material.SetColor("_DarkerColor", darkerColor);
				}
				material.SetFloat("_GlitchEnabled", Constants.DISABLE_START_SCREEN_COLOR_FLICKER ? 0f : 1f);

				var rect = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);
				for (var pass = 0; pass < material.passCount; pass++)
					Graphics.DrawTexture(rect, BaseContent.WhiteTex, material, pass);

				DrawTitle();
			}
			catch (Exception ex)
			{
				disabledAfterError = true;
				Log.Warning($"Zombieland disabled the main menu background post effect after an error: {ex}");
			}
		}

		static void DrawTitle()
		{
			var width = UI.screenWidth * 0.3375f;
			var height = width * titleTexture.height / titleTexture.width;
			var rect = new Rect((UI.screenWidth - width) / 2f, 72f, width, height);

			var previousColor = GUI.color;
			try
			{
				GUI.color = new Color(1f, 1f, 1f, 0.9f);
				GUI.DrawTexture(rect, titleTexture, ScaleMode.StretchToFill, true);
			}
			finally
			{
				GUI.color = previousColor;
			}

			TitleBloodDrip.Draw(rect);
		}

		static Color ColorFromHex(int r, int g, int b)
			=> new(r / 255f, g / 255f, b / 255f, 1f);
	}

	static partial class Patches
	{
		[HarmonyPatch(typeof(UIRoot_Entry), "DoMainMenu")]
		static class UIRoot_Entry_DoMainMenu_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var codes = instructions.ToList();
				var backgroundOnGUI = AccessTools.Method(typeof(UIMenuBackground), nameof(UIMenuBackground.BackgroundOnGUI));
				if (backgroundOnGUI == null)
				{
					Log.Warning("Zombieland could not insert the main menu background post effect because UIMenuBackground.BackgroundOnGUI was not found.");
					return codes;
				}

				var index = codes.FindIndex(code => code.Calls(backgroundOnGUI));
				if (index < 0)
				{
					Log.Warning("Zombieland could not insert the main menu background post effect because the background draw call was not found.");
					return codes;
				}

				codes.Insert(index + 1, CodeInstruction.Call(() => MainMenuBackgroundPostEffect.Draw()));
				return codes;
			}
		}
	}
}
