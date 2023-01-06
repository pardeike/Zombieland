using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Dialog_ErrorMessage : Window
	{
		public string text;
		Vector2 scrollPosition;

		public override Vector2 InitialSize => new(640f, 460f);

		public Dialog_ErrorMessage(string text)
		{
			this.text = text;
			doCloseX = true;
			forcePause = true;
			absorbInputAroundWindow = true;
			onlyOneOfTypeAllowed = true;
			closeOnAccept = true;
			closeOnCancel = true;
		}

		public override void DoWindowContents(Rect inRect)
		{
			var y = inRect.y;

			Text.Font = GameFont.Small;
			Widgets.Label(new Rect(0f, y, inRect.width, 42f), "Zombieland Error");
			y += 42f;

			Text.Font = GameFont.Tiny;
			var outRect = new Rect(inRect.x, y, inRect.width, inRect.height - 35f - 5f - y);
			float width = outRect.width - 16f;
			var viewRect = new Rect(0f, 0f, width, Text.CalcHeight(text, width));
			Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect, true);
			Widgets.Label(new Rect(0f, 0f, viewRect.width, viewRect.height), text);
			Widgets.EndScrollView();
		}
	}
}