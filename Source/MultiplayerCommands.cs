using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Verse;

namespace ZombieLand
{
	public enum ColonistFlag
	{
		AutoAvoidZombies,
		AutoExtractZombieSerum,
		AutoDoubleTap
	}

	public enum SymbiantDebugCommand
	{
		AddCell,
		RemoveCell,
		Move,
		UnassignHost
	}

	// Carries an edited settings timeline through a synchronized command as Scribe XML.
	public class SettingsTimelinePayload : IExposable
	{
		public List<SettingsKeyFrame> timeline = new();

		public void ExposeData() => Scribe_Collections.Look(ref timeline, "timeline", LookMode.Deep);
	}

	// UI-originated simulation changes. Without a Multiplayer session every [SyncMethod]
	// runs directly, so normal play keeps a single code path. NoInlining keeps callers
	// on the method body that Multiplayer patches.
	static class MultiplayerCommands
	{
		// A UI action may change simulation state: always without a session, and during one
		// only from the interface. Calls from ticks or commands would run on one peer only.
		internal static bool CanIssue => MultiplayerSupport.IsActive == false || MP.InInterface;

		// Local feedback such as a window belongs to the peer that issued the command.
		internal static bool IsLocalIssuer => MultiplayerSupport.IsActive == false || MP.IsExecutingSyncCommandIssuedBySelf;

		[SyncMethod]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void SetColonistFlag(Pawn pawn, ColonistFlag flag, bool value)
		{
			var config = pawn == null ? null : ColonistSettings.Values?.ConfigFor(pawn);
			if (config == null)
				return;
			switch (flag)
			{
				case ColonistFlag.AutoAvoidZombies:
					config.autoAvoidZombies = value;
					break;
				case ColonistFlag.AutoExtractZombieSerum:
					config.autoExtractZombieSerum = value;
					break;
				case ColonistFlag.AutoDoubleTap:
					config.autoDoubleTap = value;
					break;
			}
		}

		[SyncMethod]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void SetSwitchState(Thing thing, bool active)
		{
			var comp = thing?.TryGetComp<CompSwitchable>();
			if (comp != null)
				comp.isActive = active;
		}

		[SyncMethod]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void SetThumperSettings(Thing thing, float intensity, int intervalTicks)
		{
			if (thing is not ZombieThumper thumper)
				return;
			thumper.intensity = UnityEngine.Mathf.Clamp01(intensity);
			thumper.intervalTicks = Math.Max(1, intervalTicks);
		}

		[SyncMethod]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void SetChainsawMotor(Pawn pawn, bool on)
		{
			if (pawn?.equipment?.Primary is not Chainsaw chainsaw || chainsaw.pawn != pawn)
				return;
			if (on)
			{
				if (chainsaw.running == false)
					chainsaw.StartMotor(false);
			}
			else
				chainsaw.StopMotor();
		}

		[SyncMethod]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void SetAreaRiskMode(Area area, AreaRiskMode mode)
		{
			Dialog_ManageAreas_Patches.SetMode(area, mode);
		}

		[SyncMethod]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void SetStorageContaminationRange(StorageSettings settings, float min, float max)
		{
			ContaminationStorageRange.SetRange(settings, new FloatRange(min, max));
		}

		[SyncMethod]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void AcknowledgeSymbiantDiscovery(Thing thing)
		{
			(thing as ZombieSymbiant)?.AcknowledgeDiscovery();
		}

		// Debug incidents read the local mouse cell and map. The command carries both to every peer.
		// Not debugOnly: only the dev-mode debug menu calls it, and test automation can trigger it.
		[SyncMethod]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void TriggerZombieIncident(Map map, IntVec3 cell, int size, bool ignoreLimit)
		{
			if (map == null || size <= 0)
				return;
			if (ZombiesRising.TryExecute(map, size, cell, false, ignoreLimit) == false && cell.IsValid == false)
				Log.Error("Incident creation failed. Most likely no valid spawn point found.");
		}

		[SyncMethod(debugOnly = true)]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void RunSymbiantDebugCommand(Thing thing, SymbiantDebugCommand command)
		{
			if (thing is not ZombieSymbiant symbiant)
				return;
			var (succeeded, failure) = command switch
			{
				SymbiantDebugCommand.AddCell => (symbiant.DebugExpansionPulse(), "Could not add a Symbiant cell. The Symbiant may be capped or have no valid expansion target."),
				SymbiantDebugCommand.RemoveCell => (symbiant.DebugShrinkPulse(), "Could not remove a Symbiant cell."),
				SymbiantDebugCommand.Move => (symbiant.DebugMovePulse(), "Could not move a Symbiant cell."),
				SymbiantDebugCommand.UnassignHost => (symbiant.DebugUnassignHost(), "Could not unassign the Symbiant host."),
				_ => (true, null)
			};
			if (succeeded == false && IsLocalIssuer)
				Log.Warning(failure);
		}

		[SyncMethod(debugOnly = true)]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void AssignSymbiantHost(Thing thing, Pawn pawn)
		{
			if (thing is not ZombieSymbiant symbiant || pawn == null)
				return;
			if (symbiant.DebugAssignHost(pawn) == false && IsLocalIssuer)
				Log.Warning($"Could not assign {pawn.LabelShortCap} to the Symbiant.");
		}

		[SyncMethod(debugOnly = true)]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void ApplyContaminationEffect(Pawn pawn, string methodName, float factor)
		{
			if (pawn?.Map == null || methodName.NullOrEmpty())
				return;
			var method = AccessTools.GetDeclaredMethods(typeof(ContaminationEffect))
				.FirstOrDefault(m => m.Name == methodName && m.IsDefined(typeof(ContaminationRangeAttribute), false));
			_ = method?.Invoke(null, new object[] { pawn, factor });
		}

		[SyncMethod(debugOnly = true)]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void EndDecontaminationQuestPart(QuestPart part)
		{
			(part as QuestPart_DecontaminateColonists)?.DebugEnd();
		}

		internal static string SettingsFingerprint(List<SettingsKeyFrame> timeline)
			=> Scribe.saver.DebugOutputFor(new SettingsTimelinePayload { timeline = timeline });

		// Sends an edited working copy of the in-game settings timeline when this player changed it.
		// Comparing with the opening state, not the live timeline, keeps a window that was open while
		// another player applied changes from reverting them.
		internal static void SubmitGameSettings(List<SettingsKeyFrame> workingCopy, string openingFingerprint)
		{
			if (workingCopy == null || ZombieSettings.ValuesOverTime == null || MultiplayerSupport.IsActive == false || MP.InInterface == false)
				return;
			if (SettingsFingerprint(workingCopy) == openingFingerprint)
				return;
			ApplyGameSettings(new SettingsTimelinePayload { timeline = workingCopy });
		}

		[SyncMethod(exposeParameters = new[] { 0 })]
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void ApplyGameSettings(SettingsTimelinePayload payload)
		{
			var timeline = payload?.timeline;
			if (timeline == null || ZombieSettings.GetGameSettings() == null)
				return;

			// Area risk modes are edited through SetAreaRiskMode and keep their live dictionary.
			var dangerousAreas = ZombieSettings.Values?.dangerousAreas ?? new Dictionary<Area, AreaRiskMode>();
			_ = ZombieSettingsDefaults.NormalizeTimeline(ref timeline, ZombieSettings.Values);
			foreach (var frame in timeline)
				frame.values.dangerousAreas = dangerousAreas;

			ZombieSettings.ValuesOverTime = timeline;
			var values = ZombieSettings.CalculateInterpolation(timeline, GenTicks.TicksGame);
			ContaminationFactors.ApplyBaseFactor(values.contamination, values.contaminationBaseFactor);
			ZombieSettings.Values = values;
			Tools.UpdateBiomeBlacklist(values.biomesWithoutZombies);
			Tools.EnableTwinkie(values.replaceTwinkie);
		}
	}

	// Advanced settings (Constants) live in a local JSON file that Multiplayer does not share.
	// During a session the save carries the host's simulation constants and every peer applies them.
	static class MultiplayerConstants
	{
		const string SaveKey = "multiplayerConstants";
		static Dictionary<string, string> localBackup;
		static bool contaminationWarningShown;

		static bool IsLocalOnly(string name)
			=> name.StartsWith("SHOW_")
			|| name.StartsWith("TITLE_SCREEN_")
			|| name == nameof(Constants.USE_SOUND)
			|| name == nameof(Constants.MAX_CELLS_FOR_DETAILED_CONTAMINATION);

		internal static void ExposeData()
		{
			if (Scribe.mode == LoadSaveMode.Saving && MultiplayerSupport.IsActive)
			{
				var shared = Capture();
				Scribe_Collections.Look(ref shared, SaveKey, LookMode.Value, LookMode.Value);
			}
			else if (Scribe.mode == LoadSaveMode.LoadingVars)
			{
				Dictionary<string, string> shared = null;
				Scribe_Collections.Look(ref shared, SaveKey, LookMode.Value, LookMode.Value);
				Restore(shared);
			}
		}

		static Dictionary<string, string> Capture()
		{
			var values = new Dictionary<string, string>();
			foreach (var (name, field, _) in Constants.AllSettings)
				if (IsLocalOnly(name) == false)
					values[name] = Encode(field.GetValue(null));
			return values;
		}

		static void Restore(Dictionary<string, string> shared)
		{
			if (MultiplayerSupport.IsActive == false || shared == null)
			{
				RestoreLocal();
				return;
			}
			localBackup ??= Capture();
			Apply(shared);
			if (shared.TryGetValue(nameof(Constants.CONTAMINATION), out var encoded)
				&& bool.TryParse(encoded, out var hostContamination)
				&& hostContamination != Constants.CONTAMINATION
				&& contaminationWarningShown == false)
			{
				contaminationWarningShown = true;
				Log.Warning($"Zombieland: the host plays with CONTAMINATION={hostContamination}, this game has {Constants.CONTAMINATION}. The patch sets differ and the session will desync.");
				LongEventHandler.ExecuteWhenFinished(() => Find.WindowStack.Add(new Dialog_MessageBox("ZombielandMultiplayerContaminationMismatch".Translate())));
			}
		}

		// Puts the local advanced settings back after a Multiplayer session replaced them.
		internal static void RestoreLocal()
		{
			if (localBackup == null)
				return;
			var backup = localBackup;
			localBackup = null;
			Apply(backup);
		}

		static void Apply(Dictionary<string, string> values)
		{
			foreach (var (name, field, _) in Constants.AllSettings)
			{
				// CONTAMINATION selects patches at startup and cannot change while RimWorld runs.
				if (IsLocalOnly(name) || name == nameof(Constants.CONTAMINATION) || values.TryGetValue(name, out var encoded) == false)
					continue;
				try
				{
					field.SetValue(null, Decode(encoded, field.FieldType));
				}
				catch (Exception ex)
				{
					Log.Warning($"Zombieland: cannot apply the shared advanced setting {name}={encoded}: {ex.Message}");
				}
			}
		}

		static string Encode(object value) => value switch
		{
			float f => f.ToString("R", CultureInfo.InvariantCulture),
			int[] ints => string.Join(",", ints.Select(i => i.ToString(CultureInfo.InvariantCulture))),
			float[] floats => string.Join(",", floats.Select(f => f.ToString("R", CultureInfo.InvariantCulture))),
			_ => Convert.ToString(value, CultureInfo.InvariantCulture)
		};

		static object Decode(string encoded, Type type)
		{
			if (type == typeof(int[]))
				return encoded.Length == 0 ? Array.Empty<int>() : encoded.Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
			if (type == typeof(float[]))
				return encoded.Length == 0 ? Array.Empty<float>() : encoded.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
			return Convert.ChangeType(encoded, type, CultureInfo.InvariantCulture);
		}
	}
}
