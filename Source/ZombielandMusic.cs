using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	static class ZombielandMusic
	{
		const string DefPrefix = "ZombielandMusic_";
		const string MusicFolder = "music";
		const string EntryScreenClipPath = MusicFolder + "/entry-screen";
		const string AnomalyReplacementFolder = MusicFolder + "/anomaly/";
		static readonly HashSet<string> supportedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ogg", ".wav" };
		static readonly string[] anomalySongDefNames =
		{
			"Abandoned_By_The_Light",
			"Blood_Rain_Falling",
			"Shamblers_Blues",
			"A_Twisted_Path",
			"Have_They_Come_For_Us",
			"Is_It_Truly_Alive",
			"They_See_You",
			"Death_Pall_Rising",
			"Shadow_Work",
			"Spent_Introject"
		};
		static readonly HashSet<string> generatedDefNames = new();
		static readonly HashSet<string> sequenceReplacementDefNames = new();
		static readonly List<SongDef> generatedSongs = new();
		static readonly List<SongDef> zombielandShuffleBag = new();
		static readonly Dictionary<SongDef, SongDef> sequenceReplacements = new();
		static readonly object registrationLock = new();
		static readonly MethodInfo appropriateNowMethod = AccessTools.Method(typeof(MusicManagerPlay), "AppropriateNow", new[] { typeof(SongDef) });
		static readonly FieldInfo entryAudioSourceField = AccessTools.Field(typeof(MusicManagerEntry), "audioSource");
		static SongDef originalEntrySong;
		static bool originalEntrySongCaptured;
		static SongDef playbackModeOverriddenSong;
		static bool playbackModeOverriddenSongTense;
		static bool registered;
		static string lastRegistrationError;
		static SongDef lastZombielandSong;

		public static int NormalizeShare(int share)
		{
			return Mathf.Clamp(Mathf.RoundToInt(share / 10f) * 10, 0, 100);
		}

		public static string ShareLabel(int share)
		{
			share = Mathf.Clamp(share, 0, 100);
			if (share == 0)
				return "ZombielandMusicShare_OnlyOther".Translate().ToString();
			if (share == 100)
				return "ZombielandMusicShare_OnlyZombieland".Translate().ToString();
			return "ZombielandMusicShare_Percent".Translate(share).ToString();
		}

		public static void RegisterDynamicSongDefs()
		{
			lock (registrationLock)
			{
				if (registered)
					return;

				registered = true;
				try
				{
					var soundsRoot = Tools.GetModContentPath("Sounds");
					var musicRoot = Path.Combine(soundsRoot, MusicFolder);
					if (Directory.Exists(musicRoot) == false)
						return;

					var files = Directory.GetFiles(musicRoot, "*", SearchOption.AllDirectories)
						.Where(file => supportedExtensions.Contains(Path.GetExtension(file)))
						.OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
						.ToArray();
					if (files.Length == 0)
						return;

					var loaded = 0;
					foreach (var file in files)
						if (TryRegisterSong(soundsRoot, file))
							loaded++;

					Log.Message($"Zombieland registered {loaded} dynamic soundtrack song defs from {musicRoot}.");
				}
				catch (Exception ex)
				{
					lastRegistrationError = ex.GetBaseException().Message;
					Log.Warning($"Zombieland could not register dynamic soundtrack songs: {ex}");
				}
			}
		}

		public static bool IsZombielandSong(SongDef song)
		{
			if (song == null)
				return false;
			if (generatedDefNames.Contains(song.defName))
				return true;
			return song.defName?.StartsWith(DefPrefix, StringComparison.Ordinal) == true
				&& song.clipPath?.StartsWith(MusicFolder + "/", StringComparison.OrdinalIgnoreCase) == true;
		}

		public static bool TryChooseNextSong(MusicManagerPlay manager, Queue<SongDef> recentSongs, out SongDef song)
		{
			song = null;
			if (manager == null || appropriateNowMethod == null)
				return false;

			RegisterDynamicSongDefs();

			var settings = Current.Game == null
				? ZombieSettingsDefaults.group ?? new SettingsGroup()
				: ZombieSettings.ValuesAtGameTick(GenTicks.TicksGame);
			var share = Mathf.Clamp(settings.zombielandMusicShare, 0, 100);

			try
			{
				while (recentSongs != null && recentSongs.Count > 7)
					recentSongs.Dequeue();

				var candidates = AppropriateSongs(manager, settings.mixZombielandMusicModes).ToList();
				if (candidates.Count == 0 && recentSongs != null)
				{
					recentSongs.Clear();
					candidates = AppropriateSongs(manager, settings.mixZombielandMusicModes).ToList();
				}
				if (candidates.Count == 0)
					return false;

				var zombielandSongs = candidates.Where(IsShufflePoolSong).ToList();
				var otherSongs = candidates
					.Where(candidate => IsZombielandSong(candidate) == false)
					.Where(candidate => candidate.commonality > 0f)
					.ToList();

				if (settings.playZombielandMusic == false || share <= 0)
				{
					if (TryChooseOtherSong(otherSongs, out song))
						return true;
					if (recentSongs != null && candidates.Any(IsZombielandSong))
					{
						recentSongs.Clear();
						candidates = AppropriateSongs(manager, settings.mixZombielandMusicModes).ToList();
						otherSongs = candidates
							.Where(candidate => IsZombielandSong(candidate) == false)
							.Where(candidate => candidate.commonality > 0f)
							.ToList();
						if (TryChooseOtherSong(otherSongs, out song))
							return true;
					}
					return false;
				}

				if (share >= 100)
				{
					if (TryChooseZombielandSong(zombielandSongs, out song))
						return true;
					if (recentSongs != null && recentSongs.Any(IsZombielandSong))
					{
						recentSongs.Clear();
						candidates = AppropriateSongs(manager, settings.mixZombielandMusicModes).ToList();
						zombielandSongs = candidates.Where(IsShufflePoolSong).ToList();
						otherSongs = candidates
							.Where(candidate => IsZombielandSong(candidate) == false)
							.Where(candidate => candidate.commonality > 0f)
							.ToList();
						if (TryChooseZombielandSong(zombielandSongs, out song))
							return true;
					}
					return TryChooseOtherSong(otherSongs, out song);
				}

				var preferZombieland = Rand.Value < share / 100f;
				if (preferZombieland)
					return TryChooseZombielandSong(zombielandSongs, out song) || TryChooseOtherSong(otherSongs, out song);

				return TryChooseOtherSong(otherSongs, out song) || TryChooseZombielandSong(zombielandSongs, out song);
			}
			catch (Exception ex)
			{
				Log.Warning($"Zombieland music selection failed open to RimWorld music selection: {ex}");
				song = null;
				return false;
			}
		}

		public static object DebugState()
		{
			RegisterDynamicSongDefs();
			var entrySong = EntryScreenSong();
			var activeEntryClip = ActiveEntryAudioSource()?.clip;
			return new
			{
				registered,
				folder = Tools.GetModContentPath("Sounds", MusicFolder),
				supportedExtensions = supportedExtensions.OrderBy(extension => extension).ToArray(),
				generatedSongCount = generatedSongs.Count,
				shuffleBagCount = zombielandShuffleBag.Count,
				lastZombielandSong = lastZombielandSong?.defName,
				entrySong = new
				{
					defName = entrySong?.defName,
					clipPath = entrySong?.clipPath,
					hasClip = entrySong?.clip != null,
					installed = entrySong != null && SongDefOf.EntrySong == entrySong,
					activeClip = activeEntryClip?.name,
					activeClipMatchesSelection = activeEntryClip != null && activeEntryClip == SongDefOf.EntrySong?.clip,
					defaultSettingsAllow = DefaultSettingsAllowZombielandMusic()
				},
				lastRegistrationError,
				sequenceReplacements = sequenceReplacements
					.OrderBy(pair => pair.Key.defName, StringComparer.Ordinal)
					.Select(pair => new
					{
						originalDefName = pair.Key.defName,
						replacementDefName = pair.Value.defName,
						pair.Value.clipPath,
						pair.Value.label,
						pair.Value.volume,
						pair.Value.commonality,
						pair.Value.playOnMap,
						pair.Value.tense,
						allowedTimeOfDay = pair.Value.allowedTimeOfDay.ToString()
					})
					.ToArray(),
				songs = generatedSongs
					.Select(song => new
					{
						song.defName,
						song.clipPath,
						song.tense,
						allowedTimeOfDay = song.allowedTimeOfDay.ToString(),
						hasClip = song.clip != null,
						hasDisplayName = HasDisplayName(song),
						label = song.label
					})
					.ToArray()
			};
		}

		static bool TryRegisterSong(string soundsRoot, string file)
		{
			var clipPath = ClipPathFor(soundsRoot, file);
			if (clipPath.NullOrEmpty())
				return false;

			var clip = ContentFinder<AudioClip>.Get(clipPath, false);
			if (clip == null)
			{
				Log.Warning($"Zombieland skipped soundtrack file because RimWorld could not load it as an AudioClip: {clipPath}");
				return false;
			}
			if (IsAnomalyReplacementClip(clipPath))
				return TryRegisterAnomalyReplacement(clipPath, clip);

			var defName = DefNameFor(clipPath);
			var existing = DefDatabase<SongDef>.GetNamedSilentFail(defName);
			if (existing != null)
			{
				EnsureDisplayMetadata(existing, clipPath);
				ApplySongRole(existing, clipPath);
				RegisterGeneratedSong(existing);
				return true;
			}

			var isEntryScreenSong = IsEntryScreenClip(clipPath);
			var song = new SongDef
			{
				defName = defName,
				label = LabelFor(clipPath),
				clipPath = clipPath,
				clip = clip,
				playOnMap = isEntryScreenSong == false,
				commonality = 0f,
				volume = 1f,
				tense = isEntryScreenSong == false && IsTenseClip(clipPath),
				allowedTimeOfDay = isEntryScreenSong ? TimeOfDay.Any : TimeOfDayFor(clipPath),
				modContentPack = LoadedModManager.GetMod<ZombielandMod>()?.Content
			};

			DefGenerator.AddImpliedDef(song);
			RegisterGeneratedSong(song);
			return true;
		}

		static bool TryRegisterAnomalyReplacement(string clipPath, AudioClip clip)
		{
			if (TryGetAnomalySongDefName(clipPath, out var originalDefName) == false)
			{
				Log.Warning($"Zombieland skipped malformed Anomaly soundtrack replacement path: {clipPath}");
				return false;
			}

			var original = DefDatabase<SongDef>.GetNamedSilentFail(originalDefName);
			if (original == null)
			{
				if (ModsConfig.AnomalyActive)
					Log.Warning($"Zombieland could not find Anomaly soundtrack song def {originalDefName} for {clipPath}.");
				return false;
			}

			var defName = DefNameFor(clipPath);
			var replacement = DefDatabase<SongDef>.GetNamedSilentFail(defName);
			if (replacement == null)
			{
				replacement = new SongDef
				{
					defName = defName,
					label = LabelFor(clipPath),
					clipPath = clipPath,
					clip = clip,
					modContentPack = LoadedModManager.GetMod<ZombielandMod>()?.Content
				};
				DefGenerator.AddImpliedDef(replacement);
			}

			CopySongSettings(original, replacement);
			RegisterGeneratedSong(replacement);
			sequenceReplacementDefNames.Add(replacement.defName);
			sequenceReplacements[original] = replacement;
			return true;
		}

		static void CopySongSettings(SongDef source, SongDef destination)
		{
			destination.volume = source.volume;
			destination.playOnMap = source.playOnMap;
			destination.commonality = source.commonality;
			destination.tense = source.tense;
			destination.allowedTimeOfDay = source.allowedTimeOfDay;
			destination.allowedSeasons = source.allowedSeasons?.ToList();
			destination.minRoyalTitle = source.minRoyalTitle;
		}

		public static void ApplyEntrySongReplacement(bool updatePlayingSong = false)
		{
			CaptureOriginalEntrySong();
			if (Constants.TITLE_SCREEN_MUSIC && EntryScreenSong() is { clip: not null } entrySong)
			{
				SongDefOf.EntrySong = entrySong;
			}
			else
				RestoreOriginalEntrySong();

			if (updatePlayingSong)
				UpdatePlayingEntrySong();
		}

		static AudioSource ActiveEntryAudioSource()
		{
			if (Current.Root is not Root_Entry root || root.musicManagerEntry == null)
				return null;
			return entryAudioSourceField?.GetValue(root.musicManagerEntry) as AudioSource;
		}

		static void UpdatePlayingEntrySong()
		{
			var audioSource = ActiveEntryAudioSource();
			var clip = SongDefOf.EntrySong?.clip;
			if (audioSource == null || clip == null || audioSource.clip == clip)
				return;

			var wasPlaying = audioSource.isPlaying;
			audioSource.Stop();
			audioSource.clip = clip;
			if (wasPlaying)
				audioSource.Play();
		}

		public static SongDef PrepareSongForPlayback(MusicManagerPlay manager, SongDef song)
		{
			RestorePlaybackModeOverride();
			if (manager == null || song == null)
				return song;

			var settings = Current.Game == null
				? ZombieSettingsDefaults.group ?? new SettingsGroup()
				: ZombieSettings.ValuesAtGameTick(GenTicks.TicksGame);
			song = SelectSequenceReplacement(song, settings);
			if (IsZombielandSong(song) == false || IsEntryScreenSong(song) || IsSequenceReplacementSong(song))
				return song;
			if (settings.playZombielandMusic == false || settings.mixZombielandMusicModes == false)
				return song;

			var playbackTense = manager.DangerMusicMode;
			if (song.tense == playbackTense)
				return song;

			playbackModeOverriddenSong = song;
			playbackModeOverriddenSongTense = song.tense;
			song.tense = playbackTense;
			return song;
		}

		static SongDef SelectSequenceReplacement(SongDef song, SettingsGroup settings)
		{
			if (sequenceReplacements.ContainsKey(song) == false)
				return song;
			if (settings?.playZombielandMusic != true)
				return song;
			var share = Mathf.Clamp(settings?.zombielandMusicShare ?? 0, 0, 100);
			if (share <= 0)
				return song;
			var frequencyRoll = share > 0 && share < 100 ? Rand.Value : 0f;
			return ResolveSequenceReplacement(song, settings, frequencyRoll);
		}

		public static SongDef ResolveSequenceReplacement(SongDef song, SettingsGroup settings, float frequencyRoll)
		{
			if (song == null || sequenceReplacements.TryGetValue(song, out var replacement) == false)
				return song;
			var share = Mathf.Clamp(settings?.zombielandMusicShare ?? 0, 0, 100);
			if (settings?.playZombielandMusic != true || share <= 0)
				return song;
			return share >= 100 || Mathf.Clamp01(frequencyRoll) < share / 100f ? replacement : song;
		}

		public static object AnomalyReplacementContractState()
		{
			RegisterDynamicSongDefs();
			var disabled = new SettingsGroup { playZombielandMusic = false, zombielandMusicShare = 50 };
			var zero = new SettingsGroup { playZombielandMusic = true, zombielandMusicShare = 0 };
			var half = new SettingsGroup { playZombielandMusic = true, zombielandMusicShare = 50 };
			var full = new SettingsGroup { playZombielandMusic = true, zombielandMusicShare = 100 };
			var mappings = anomalySongDefNames
				.Select(originalDefName =>
				{
					var original = DefDatabase<SongDef>.GetNamedSilentFail(originalDefName);
					SongDef replacement = null;
					if (original != null)
						sequenceReplacements.TryGetValue(original, out replacement);
					return new
					{
						originalDefName,
						replacementDefName = replacement?.defName,
						replacementClipPath = replacement?.clipPath,
						hasOriginal = original != null,
						hasReplacement = replacement != null,
						settingsMatch = original != null && replacement != null && SongSettingsMatch(original, replacement),
						excludedFromShufflePool = replacement != null && IsShufflePoolSong(replacement) == false,
						disabledKeepsOriginal = original != null && ResolveSequenceReplacement(original, disabled, 0f) == original,
						disabledPreservesRandomState = original != null && SequenceSelectionPreservesRandomState(original, disabled),
						zeroKeepsOriginal = original != null && ResolveSequenceReplacement(original, zero, 0f) == original,
						belowHalfUsesReplacement = original != null && ResolveSequenceReplacement(original, half, 0.499f) == replacement,
						halfKeepsOriginal = original != null && ResolveSequenceReplacement(original, half, 0.5f) == original,
						fullUsesReplacement = original != null && ResolveSequenceReplacement(original, full, 1f) == replacement
					};
				})
				.ToArray();
			var expectedCount = ModsConfig.AnomalyActive ? anomalySongDefNames.Length : 0;
			return new
			{
				success = sequenceReplacements.Count == expectedCount
					&& (ModsConfig.AnomalyActive == false || mappings.All(mapping => mapping.hasOriginal
						&& mapping.hasReplacement
						&& mapping.settingsMatch
						&& mapping.excludedFromShufflePool
						&& mapping.disabledKeepsOriginal
						&& mapping.disabledPreservesRandomState
						&& mapping.zeroKeepsOriginal
						&& mapping.belowHalfUsesReplacement
						&& mapping.halfKeepsOriginal
						&& mapping.fullUsesReplacement)),
				anomalyActive = ModsConfig.AnomalyActive,
				expectedCount,
				actualCount = sequenceReplacements.Count,
				mappings
			};
		}

		static bool SequenceSelectionPreservesRandomState(SongDef song, SettingsGroup settings)
		{
			const int seed = 91357;
			float expectedNextValue;
			Rand.PushState(seed);
			try
			{
				expectedNextValue = Rand.Value;
			}
			finally
			{
				Rand.PopState();
			}

			SongDef selected;
			float actualNextValue;
			Rand.PushState(seed);
			try
			{
				selected = SelectSequenceReplacement(song, settings);
				actualNextValue = Rand.Value;
			}
			finally
			{
				Rand.PopState();
			}
			return selected == song && actualNextValue == expectedNextValue;
		}

		static bool SongSettingsMatch(SongDef original, SongDef replacement)
		{
			return original.volume == replacement.volume
				&& original.playOnMap == replacement.playOnMap
				&& original.commonality == replacement.commonality
				&& original.tense == replacement.tense
				&& original.allowedTimeOfDay == replacement.allowedTimeOfDay
				&& Equals(original.minRoyalTitle, replacement.minRoyalTitle)
				&& (original.allowedSeasons ?? new List<Season>()).SequenceEqual(replacement.allowedSeasons ?? new List<Season>());
		}

		static void RestorePlaybackModeOverride()
		{
			if (playbackModeOverriddenSong != null)
				playbackModeOverriddenSong.tense = playbackModeOverriddenSongTense;
			playbackModeOverriddenSong = null;
		}

		static IEnumerable<SongDef> AppropriateSongs(MusicManagerPlay manager, bool mixZombielandMusicModes)
		{
			foreach (var song in DefDatabase<SongDef>.AllDefs)
				if (song?.clip != null && HasDisplayName(song) && IsAppropriateNow(manager, song, mixZombielandMusicModes))
					yield return song;
		}

		static bool IsAppropriateNow(MusicManagerPlay manager, SongDef song, bool mixZombielandMusicModes)
		{
			var ignoreMusicMode = mixZombielandMusicModes && IsZombielandSong(song) && IsEntryScreenSong(song) == false;
			var originalTense = song.tense;
			try
			{
				if (ignoreMusicMode)
					song.tense = manager.DangerMusicMode;
				return (bool)appropriateNowMethod.Invoke(manager, new object[] { song });
			}
			catch (Exception ex)
			{
				lastRegistrationError = ex.GetBaseException().Message;
				return false;
			}
			finally
			{
				if (ignoreMusicMode)
					song.tense = originalTense;
			}
		}

		static bool TryChooseZombielandSong(List<SongDef> songs, out SongDef song)
		{
			song = null;
			if (songs.Count == 0)
				return false;

			var candidates = songs.Distinct().ToList();
			var candidateSet = new HashSet<SongDef>(candidates);
			zombielandShuffleBag.RemoveAll(existing => existing == null || candidateSet.Contains(existing) == false);
			if (zombielandShuffleBag.Count == 0)
				RefillZombielandShuffleBag(candidates);
			if (zombielandShuffleBag.Count == 0)
				return false;

			song = zombielandShuffleBag[0];
			zombielandShuffleBag.RemoveAt(0);
			lastZombielandSong = song;
			return true;
		}

		static void RefillZombielandShuffleBag(List<SongDef> candidates)
		{
			zombielandShuffleBag.Clear();
			zombielandShuffleBag.AddRange(candidates);
			for (var i = zombielandShuffleBag.Count - 1; i > 0; i--)
			{
				var j = Rand.RangeInclusive(0, i);
				(zombielandShuffleBag[i], zombielandShuffleBag[j]) = (zombielandShuffleBag[j], zombielandShuffleBag[i]);
			}

			if (zombielandShuffleBag.Count > 1 && zombielandShuffleBag[0] == lastZombielandSong)
			{
				var swapIndex = Rand.RangeInclusive(1, zombielandShuffleBag.Count - 1);
				(zombielandShuffleBag[0], zombielandShuffleBag[swapIndex]) = (zombielandShuffleBag[swapIndex], zombielandShuffleBag[0]);
			}
		}

		static bool TryChooseOtherSong(List<SongDef> songs, out SongDef song)
		{
			song = null;
			if (songs.Count == 0)
				return false;
			song = songs.RandomElementByWeight(candidate => Mathf.Max(candidate.commonality, 0.001f));
			return true;
		}

		static void RegisterGeneratedSong(SongDef song)
		{
			if (song == null)
				return;
			EnsureDisplayMetadata(song, song.clipPath);
			ApplySongRole(song, song.clipPath);
			generatedDefNames.Add(song.defName);
			if (generatedSongs.Contains(song) == false)
				generatedSongs.Add(song);
		}

		static void CaptureOriginalEntrySong()
		{
			if (originalEntrySongCaptured)
				return;
			originalEntrySong = SongDefOf.EntrySong;
			originalEntrySongCaptured = true;
		}

		static void RestoreOriginalEntrySong()
		{
			if (originalEntrySongCaptured && originalEntrySong != null)
				SongDefOf.EntrySong = originalEntrySong;
		}

		public static bool DefaultSettingsAllowZombielandMusic()
		{
			if (ZombieSettingsDefaults.group == null)
			{
				try
				{
					_ = LoadedModManager.GetMod<ZombielandMod>()?.GetSettings<ZombieSettingsDefaults>();
				}
				catch { }
			}
			var defaults = ZombieSettingsDefaults.group ?? new SettingsGroup();
			return defaults.playZombielandMusic && Mathf.Clamp(defaults.zombielandMusicShare, 0, 100) > 0;
		}

		static SongDef EntryScreenSong()
		{
			RegisterDynamicSongDefs();
			return generatedSongs.FirstOrDefault(IsEntryScreenSong)
				?? DefDatabase<SongDef>.AllDefs.FirstOrDefault(IsEntryScreenSong);
		}

		static bool IsEntryScreenSong(SongDef song)
			=> IsEntryScreenClip(song?.clipPath);

		static bool IsSequenceReplacementSong(SongDef song)
			=> song != null && sequenceReplacementDefNames.Contains(song.defName);

		static bool IsShufflePoolSong(SongDef song)
			=> IsZombielandSong(song) && IsEntryScreenSong(song) == false && IsSequenceReplacementSong(song) == false;

		static bool IsEntryScreenClip(string clipPath)
			=> string.Equals(clipPath, EntryScreenClipPath, StringComparison.OrdinalIgnoreCase);

		static bool IsAnomalyReplacementClip(string clipPath)
			=> clipPath?.StartsWith(AnomalyReplacementFolder, StringComparison.OrdinalIgnoreCase) == true;

		static bool TryGetAnomalySongDefName(string clipPath, out string originalDefName)
		{
			originalDefName = null;
			if (IsAnomalyReplacementClip(clipPath) == false)
				return false;
			var fileName = clipPath.Substring(AnomalyReplacementFolder.Length);
			if (fileName.Length < 3 || char.IsDigit(fileName[0]) == false || char.IsDigit(fileName[1]) == false || char.IsWhiteSpace(fileName[2]) == false)
				return false;
			var number = (fileName[0] - '0') * 10 + fileName[1] - '0';
			if (number < 1 || number > anomalySongDefNames.Length)
				return false;
			originalDefName = anomalySongDefNames[number - 1];
			return true;
		}

		static void ApplySongRole(SongDef song, string clipPath)
		{
			if (song == null || IsEntryScreenClip(clipPath) == false)
				return;
			song.playOnMap = false;
			song.commonality = 0f;
			song.tense = false;
			song.allowedTimeOfDay = TimeOfDay.Any;
		}

		static bool HasDisplayName(SongDef song)
		{
			return song != null
				&& (song.label.NullOrEmpty() == false || song.clipPath.NullOrEmpty() == false);
		}

		static void EnsureDisplayMetadata(SongDef song, string clipPath)
		{
			if (song == null)
				return;
			if (song.label.NullOrEmpty())
				song.label = LabelFor(clipPath);
		}

		static string LabelFor(string clipPath)
		{
			var name = clipPath ?? "";
			var slash = name.LastIndexOf('/');
			if (slash >= 0)
				name = name.Substring(slash + 1);
			name = name.Replace('_', ' ').Trim();
			return name.NullOrEmpty() ? "Zombieland music" : name;
		}

		static string ClipPathFor(string soundsRoot, string file)
		{
			var root = Path.GetFullPath(soundsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			var fullPath = Path.GetFullPath(file);
			if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) == false)
				return null;

			var relative = fullPath.Substring(root.Length);
			var withoutExtension = Path.Combine(Path.GetDirectoryName(relative) ?? "", Path.GetFileNameWithoutExtension(relative));
			return withoutExtension
				.Replace(Path.DirectorySeparatorChar, '/')
				.Replace(Path.AltDirectorySeparatorChar, '/');
		}

		static string DefNameFor(string clipPath)
		{
			var builder = new StringBuilder(DefPrefix);
			foreach (var ch in clipPath)
				builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
			builder.Append('_');
			builder.Append(StableHash(clipPath).ToString("x8"));
			return builder.ToString();
		}

		static uint StableHash(string value)
		{
			unchecked
			{
				var hash = 2166136261u;
				foreach (var ch in value)
				{
					hash ^= char.ToLowerInvariant(ch);
					hash *= 16777619u;
				}
				return hash;
			}
		}

		static bool IsTenseClip(string clipPath)
		{
			return PathParts(clipPath).Any(part => part == "tense" || part == "danger" || part == "combat");
		}

		static TimeOfDay TimeOfDayFor(string clipPath)
		{
			var parts = PathParts(clipPath);
			if (parts.Contains("night"))
				return TimeOfDay.Night;
			if (parts.Contains("day"))
				return TimeOfDay.Day;
			return TimeOfDay.Any;
		}

		static HashSet<string> PathParts(string clipPath)
		{
			return new HashSet<string>(
				(clipPath ?? "")
					.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
					.Select(part => part.Trim().ToLowerInvariant()),
				StringComparer.OrdinalIgnoreCase);
		}
	}
}
