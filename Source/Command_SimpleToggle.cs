using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Command_SimpleToggle : Command
	{
		public Func<bool> isActive;
		public Action toggleAction;
		public SoundDef turnOnSound = SoundDefOf.Checkbox_TurnedOn;
		public SoundDef turnOffSound = SoundDefOf.Checkbox_TurnedOff;

		public bool activateIfAmbiguous = true;

		public override SoundDef CurActivateSound => isActive() ? turnOffSound : turnOnSound;

		public override void ProcessInput(Event ev)
		{
			base.ProcessInput(ev);
			toggleAction();
		}

		public override GizmoResult GizmoOnGUI(Vector2 loc, float maxWidth, GizmoRenderParms parms)
		{
			var result = base.GizmoOnGUI(loc, maxWidth, parms);
			if (isActive())
			{
				var rect = new Rect(loc.x, loc.y, GetWidth(maxWidth), 75f);
				var position = new Rect(rect.x + rect.width - 24f, rect.y, 24f, 24f);
				GUI.DrawTexture(position, Widgets.CheckboxOnTex);
			}
			return result;
		}

		public override bool InheritInteractionsFrom(Gizmo other)
		{
			return other is Command_Toggle command_Toggle && command_Toggle.isActive() == isActive();
		}
	}
}
