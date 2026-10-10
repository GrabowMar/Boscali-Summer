using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Immersion.Configuration;
using BoscaliSummer.Modules.Immersion.Domain;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// The kneeboard's card gallery: the automatic mission card plus embedded picture cards, picked in SQD STUDIO.
    /// One picture is decoded at a time, only when chosen; picture cards need no text render at all.
    /// Pictures are drawn by tools/Build-KneeboardCards.py; a slug here is the PNG's file name.
    /// </summary>
    internal sealed class KneeboardGallery : IKneeboardGallery
    {
        private static readonly (string Name, string Slug)[] Cards =
        {
            ("MISSION CARD", null),
            ("LANDING CHECKLIST", "landing-checklist"), ("SORTIE BINGO", "sortie-bingo"), ("KILROY", "kilroy"),
            ("LUCKY DUCK", "lucky-duck"), ("COME HOME SAFE", "come-home-safe"), ("HOW TO LAND", "how-to-land"),
            ("THREAT PLOT", "threat-plot"), ("APPROACH PLATE", "approach-plate"), ("TALLY BOARD", "tally-board"),
            ("CAS 9-LINE", "nine-line"), ("SQUADRON PATCH", "squadron-patch"), ("HISTORY EXAM", "history-exam"),
            ("YO BUDDY", "yo-buddy"), ("THREE STRIKES", "three-strikes"), ("TUNNEL RUN", "tunnel-run"),
            ("MISSILE KNOWS", "missile-knows"), ("ORANGE SKIES", "orange-skies"), ("COLD START", "cold-start"),
            ("DO NOT POST", "do-not-post"), ("MOAR BOOSTERS", "moar-boosters"), ("FOX CALLS", "fox-calls"),
            ("KILL COUNT", "kill-count"), ("FORM N-1", "nuke-form"), ("BUTTER", "butter"), ("SCOREBOARD", "scoreboard"),
            ("FLAT DOG", "flat-dog"), ("BARREL ROLL", "barrel-roll")
        };
        private readonly ImmersionSettings settings;
        private readonly Action changed;
        private Texture2D texture;
        private Sprite sprite;
        private int loaded = -1;

        internal KneeboardGallery(ImmersionSettings settings, Action changed) { this.settings = settings; this.changed = changed; }

        public int Count => Cards.Length;

        public int Selected
        {
            get => KneeboardCard.Wrap(settings.KneeboardCard.Value, Count);
            set
            {
                int next = KneeboardCard.Wrap(value, Count);
                if (next == settings.KneeboardCard.Value) return;
                settings.KneeboardCard.Value = next;
                changed?.Invoke();
            }
        }

        public string Name(int index) => Cards[KneeboardCard.Wrap(index, Count)].Name;
        public Texture2D Card(int index) { Load(index); return texture; }
        public Sprite Preview(int index) { Load(index); return sprite; }

        internal void Release()
        {
            if (sprite != null) Object.Destroy(sprite);
            if (texture != null) Object.Destroy(texture);
            sprite = null; texture = null; loaded = -1;
        }

        private void Load(int index)
        {
            index = KneeboardCard.Wrap(index, Count);
            if (index == loaded) return;
            Release();
            loaded = index;
            string slug = Cards[index].Slug;
            if (slug == null) return;
            byte[] data = EmbeddedResources.ReadAll(typeof(KneeboardGallery).Assembly,
                "BoscaliSummer.Immersion.Kneeboard." + slug + ".png", 512 * 1024);
            if (data != null) PngSprites.TryLoad(data, 1024, "Boscali.Kneeboard." + slug, out texture, out sprite);
        }
    }
}
