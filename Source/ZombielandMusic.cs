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
		static readonly HashSet<string> supportedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ogg", ".wav" };
		static readonly HashSet<string> generatedDefNames = new();
		static readonly List<SongDef> generatedSongs = new();
		static readonly List<SongDef> zombielandShuffleBag = new();
		static readonly object registrationLock = new();
		static readonly MethodInfo appropriateNowMethod = AccessTools.Method(typeof(MusicManagerPlay), "AppropriateNow", new[] { typeof(SongDef) });
		static SongDef originalEntrySong;
		static bool originalEntrySongCaptured;
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

				var candidates = AppropriateSongs(manager).ToList();
				if (candidates.Count == 0 && recentSongs != null)
				{
					recentSongs.Clear();
					candidates = AppropriateSongs(manager).ToList();
				}
				if (candidates.Count == 0)
					return false;

				var zombielandSongs = candidates.Where(songCandidate => IsZombielandSong(songCandidate) && IsEntryScreenSong(songCandidate) == false).ToList();
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
						candidates = AppropriateSongs(manager).ToList();
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
						candidates = AppropriateSongs(manager).ToList();
						zombielandSongs = candidates.Where(songCandidate => IsZombielandSong(songCandidate) && IsEntryScreenSong(songCandidate) == false).ToList();
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
					defaultSettingsAllow = DefaultSettingsAllowZombielandMusic()
				},
				lastRegistrationError,
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

		public static void ApplyEntrySongReplacement()
		{
			CaptureOriginalEntrySong();
			if (DefaultSettingsAllowZombielandMusic() && EntryScreenSong() is { clip: not null } entrySong)
			{
				SongDefOf.EntrySong = entrySong;
				return;
			}
			RestoreOriginalEntrySong();
		}

		static IEnumerable<SongDef> AppropriateSongs(MusicManagerPlay manager)
		{
			foreach (var song in DefDatabase<SongDef>.AllDefs)
				if (song?.clip != null && HasDisplayName(song) && IsAppropriateNow(manager, song))
					yield return song;
		}

		static bool IsAppropriateNow(MusicManagerPlay manager, SongDef song)
		{
			try
			{
				return (bool)appropriateNowMethod.Invoke(manager, new object[] { song });
			}
			catch (Exception ex)
			{
				lastRegistrationError = ex.GetBaseException().Message;
				return false;
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

		static bool IsEntryScreenClip(string clipPath)
			=> string.Equals(clipPath, EntryScreenClipPath, StringComparison.OrdinalIgnoreCase);

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
