using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class GameCondition_ZombieFreeEvent : GameCondition
	{
		public override string TooltipString
		{
			get
			{
				var text = def.LabelCap.ToString();
				var map = SingleMap ?? Find.CurrentMap ?? Find.AnyPlayerHomeMap;

				if (Permanent && forceDisplayAsDuration == false && def.showPermanentInTooltip)
				{
					text += "\n" + "Permanent".Translate().CapitalizeFirst();
				}
				else
				{
					var location = map != null ? Find.WorldGrid.LongLatOf(map.Tile) : Vector2.zero;
					text = string.Concat(text, "\n", "Started".Translate(), ": ", GenDate.DateFullStringAt(GenDate.TickGameToAbs(startTick), location).Colorize(ColoredText.DateTimeColor));
					text = string.Concat(text, "\n", "ZombieFreeEventTimeLeft".Translate(), ": ", TicksLeft.ToStringTicksToPeriod().Colorize(ColoredText.DateTimeColor));
				}

				text += "\n";
				text = text + "\n" + Description.ResolveTags();
				if (conditionCauser != null && hideSource == false && CameraJumper.CanJump(conditionCauser))
					text = text + "\n\n" + def.jumpToSourceKey.Translate().Resolve();
				else if (quest != null && quest.hidden == false)
					text = text + "\n\n" + "CausedByQuest".Translate(quest.name).Resolve();
				else if (psychicRitualDef != null)
					text += string.Format("\n\n{0}: {1}", "CausedByPsychicRitual".Translate(), psychicRitualDef.label.CapitalizeFirst());
				else if (def.natural == false)
					text += string.Format("\n\n{0}", "SourceUnknown".Translate());

				if (map != null && MapExcludedByFilter(def, map))
					text += string.Format("\n\n{0}", "ThisWillNotAffectLayer".Translate(map.Tile.LayerDef.gerundLabel.Named("GERUND"), map.Tile.LayerDef.label.Named("LAYER")));

				return text;
			}
		}
	}
}
