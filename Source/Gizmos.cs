using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	static class Gizmos
	{
		static readonly Texture2D AvoidingEnabled = ContentFinder<Texture2D>.Get("AvoidingEnabled", true);
		static readonly Texture2D AvoidingDisabled = ContentFinder<Texture2D>.Get("AvoidingDisabled", true);

		public static Gizmo ZombieAvoidance(Pawn pawn)
		{
			var config = ColonistSettings.Values.ConfigFor(pawn);
			if (config == null)
				return null;

			var autoAvoidZombies = config.autoAvoidZombies;
			var description = autoAvoidZombies ? "AutoAvoidZombiesEnabledDescription" : "AutoAvoidZombiesDisabledDescription";

			return new Command_Action
			{
				defaultDesc = description.Translate(),
				icon = autoAvoidZombies ? AvoidingEnabled : AvoidingDisabled,
				activateSound = autoAvoidZombies ? SoundDefOf.Designate_ZoneAdd : SoundDefOf.Designate_ZoneDelete,
				action = config.ToggleAutoAvoidZombies
			};
		}

		static readonly Texture2D ExtractingAllowed = ContentFinder<Texture2D>.Get("ExtractingAllowed", true);
		static readonly Texture2D ExtractingForbidden = ContentFinder<Texture2D>.Get("ExtractingForbidden", true);
		static readonly Texture2D ExtractingDisabled = ContentFinder<Texture2D>.Get("ZombieExtract", true); // auto-dimmed
		public static Gizmo ExtractSerum(Pawn pawn)
		{
			var description = "AutoExtractDisabledDescription";
			var icon = ExtractingDisabled;
			SoundDef activateSound = null;
			Action action = null;

			var canDoctor = pawn.CanDoctor();
			if (canDoctor)
			{
				var config = canDoctor ? ColonistSettings.Values.ConfigFor(pawn) : null;
				if (config != null)
				{
					var autoExtractZombieSerum = config.autoExtractZombieSerum;
					description = autoExtractZombieSerum ? "AutoExtractAllowedDescription" : "AutoExtractForbiddenDescription";
					icon = autoExtractZombieSerum ? ExtractingAllowed : ExtractingForbidden;
					activateSound = autoExtractZombieSerum ? SoundDefOf.Designate_ZoneAdd : SoundDefOf.Designate_ZoneDelete;
					action = config.ToggleAutoExtractZombieSerum;
				}
			}

			return new Command_Action
			{
				disabled = canDoctor == false,
				defaultDesc = description.Translate(),
				icon = icon,
				activateSound = activateSound,
				action = action
			};
		}
	}
}