using Harmony;
using System.Reflection;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	class ZombielandMod : Mod
	{
		public ZombielandMod(ModContentPack content) : base(content)
		{
			// HarmonyInstance.DEBUG = true;
			var harmony = HarmonyInstance.Create("net.pardeike.zombieland");
			harmony.PatchAll(Assembly.GetExecutingAssembly());
		}

		public override void WriteSettings()
		{
			base.WriteSettings();
		}

		public override void DoSettingsWindowContents(Rect inRect)
		{
			Listing_Standard list = new Listing_Standard()
			{
				ColumnWidth = inRect.width
			};

			list.Begin(inRect);
			list.Gap();

			// list.Label("label");
			// silderVal = Mathf.RoundToInt(list.Slider(silderVal, 0, 120));

			// list.Gap();
			// list.CheckboxLabeled("label", ref checkboxVal, "description");

			list.End();
		}

		public override string SettingsCategory()
		{
			return "Zombieland";
		}
	}
}