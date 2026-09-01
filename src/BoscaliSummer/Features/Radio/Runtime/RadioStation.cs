using System;
using System.Collections.Generic;
using BoscaliSummer.Core;
using UnityEngine;

namespace BoscaliSummer.Features.Radio.Runtime
{
    internal sealed class RadioStationTrack
    {
        public string Title { get; }
        public string LocalPath { get; }
        public string Extension { get; }
        public AudioClip VanillaClip { get; }
        public bool IsLocal => !string.IsNullOrEmpty(LocalPath);

        private RadioStationTrack(
            string title, string localPath, string extension, AudioClip vanillaClip)
        {
            Title = title;
            LocalPath = localPath;
            Extension = extension;
            VanillaClip = vanillaClip;
        }

        public static RadioStationTrack Local(RadioTrack track) =>
            new RadioStationTrack(track.Title, track.Path, track.Extension, null);

        public static RadioStationTrack Vanilla(AudioClip clip) =>
            new RadioStationTrack(
                clip == null || string.IsNullOrWhiteSpace(clip.name)
                    ? "Original soundtrack"
                    : clip.name,
                null, null, clip);
    }

    internal sealed class RadioStation
    {
        public string Id { get; }
        public string Code { get; }
        public string Name { get; }
        public string Slogan { get; }
        public string IconPath { get; }
        public RadioStationTrack[] Tracks { get; }

        public RadioStation(
            string id, string code, string name, string slogan, string iconPath,
            RadioStationTrack[] tracks)
        {
            Id = id;
            Code = code;
            Name = name;
            Slogan = slogan;
            IconPath = iconPath;
            Tracks = tracks;
        }
    }

    internal sealed class VanillaSoundtrackCatalog
    {
        public const int MaximumClips = 30;

        public AudioClip AgrapolSeed { get; private set; }
        public AudioClip MarisSeed { get; private set; }
        public AudioClip[] All { get; private set; }

        public static bool TryCreate(out VanillaSoundtrackCatalog catalog)
        {
            catalog = null;
            MapSettings map;
            try
            {
                LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                map = level == null ? null : level.LoadedMapSettings;
            }
            catch
            {
                return false;
            }
            if (map == null || FactionRegistry.factions.Count == 0) return false;

            var factions = new List<Faction>(FactionRegistry.factions);
            factions.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(
                left == null ? string.Empty : left.factionName,
                right == null ? string.Empty : right.factionName));

            var clips = new List<AudioClip>();
            var clipIds = new HashSet<int>();
            AudioClip agrapol = null;
            AudioClip maris = null;

            for (int i = 0; i < factions.Count; i++)
            {
                Faction faction = factions[i];
                if (faction == null) continue;
                AudioClip start = map.GetStartMusic(faction);
                AudioClip strategic = map.GetStrategicMusic(faction);
                AudioClip tactical = map.GetTacticalMusic(faction);
                AddUnique(clips, clipIds, start);
                AddUnique(clips, clipIds, strategic);
                AddUnique(clips, clipIds, tactical);

                string name = faction.factionName ?? string.Empty;
                if (agrapol == null && name.IndexOf("Boscali", StringComparison.OrdinalIgnoreCase) >= 0)
                    agrapol = strategic ?? tactical ?? start;
                if (maris == null && name.IndexOf("Primeva", StringComparison.OrdinalIgnoreCase) >= 0)
                    maris = strategic ?? tactical ?? start;
            }

            if (clips.Count == 0) return false;
            if (agrapol == null) agrapol = clips[0];
            if (maris == null) maris = clips.Count > 1 ? clips[1] : clips[0];
            catalog = new VanillaSoundtrackCatalog
            {
                AgrapolSeed = agrapol,
                MarisSeed = maris,
                All = clips.ToArray()
            };
            return true;
        }

        private static void AddUnique(List<AudioClip> clips, HashSet<int> ids, AudioClip clip)
        {
            if (clips.Count < MaximumClips && clip != null && ids.Add(clip.GetInstanceID()))
                clips.Add(clip);
        }
    }
}
