using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	static class Gizmos
	{
		static readonly Texture2D AvoidingEnabled = Tools.LoadTexture("AvoidingEnabled", true);
		static readonly Texture2D AvoidingDisabled = Tools.LoadTexture("AvoidingDisabled", true);
		public static Gizmo ZombieAvoidance(Pawn pawn)
		{
			var config = ColonistSettings.Values.ConfigFor(pawn);
			if (config == null)
				return null;

			var autoAvoidZombies = config.autoAvoidZombies;
			var description = autoAvoidZombies ? "AutoAvoidZombiesEnabledDescription" : "AutoAvoidZombiesDisabledDescription";

			var doesAttract = Customization.DoesAttractsZombies(pawn);
			return new Command_Action
			{
				disabled = doesAttract == false,
				defaultDesc = description.Translate(),
				icon = autoAvoidZombies && doesAttract ? AvoidingEnabled : AvoidingDisabled,
				activateSound = autoAvoidZombies ? SoundDefOf.Designate_ZoneAdd : SoundDefOf.Designate_ZoneDelete,
				action = doesAttract ? config.ToggleAutoAvoidZombies : null
			};
		}

		static readonly Texture2D ExtractingAllowed = Tools.LoadTexture("ExtractingAllowed", true);
		static readonly Texture2D ExtractingForbidden = Tools.LoadTexture("ExtractingForbidden", true);
		static readonly Texture2D ExtractingDisabled = Tools.LoadTexture("ZombieExtract", true); // auto-dimmed
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

		static readonly Texture2D DoubleTapAllowed = Tools.LoadTexture("DoubleTapAllowed", true);
		static readonly Texture2D DoubleTapForbidden = Tools.LoadTexture("DoubleTapForbidden", true);
		static readonly Texture2D DoubleTapDisabled = Tools.LoadTexture("DoubleTap", true); // auto-dimmed
		public static Gizmo DoubleTap(Pawn pawn)
		{
			var description = "AutoDoubleTapDisabledDescription";
			var icon = DoubleTapDisabled;
			SoundDef activateSound = null;
			Action action = null;

			var canHunt = pawn.CanHunt();
			if (canHunt)
			{
				var config = canHunt ? ColonistSettings.Values.ConfigFor(pawn) : null;
				if (config != null)
				{
					var autoDoubleTap = config.autoDoubleTap;
					description = autoDoubleTap ? "AutoDoubleTapAllowedDescription" : "AutoDoubleTapForbiddenDescription";
					icon = autoDoubleTap ? DoubleTapAllowed : DoubleTapForbidden;
					activateSound = autoDoubleTap ? SoundDefOf.Designate_ZoneAdd : SoundDefOf.Designate_ZoneDelete;
					action = config.ToggleAutoDoubleTap;
				}
			}

			return new Command_Action
			{
				disabled = canHunt == false,
				defaultDesc = description.Translate(),
				icon = icon,
				activateSound = activateSound,
				action = action
			};
		}
	}
}
