using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Core.Game;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>A status stamp: the state word in a double outline over a wash, rotated a few degrees like a rubber stamp (the kit has no
    /// rotated primitive, so the whole group sits in one rotated rect). The word, not the colour, carries the state.</summary>
    internal sealed class WingStamp
    {
        private readonly RectTransform root;
        private readonly Image wash;
        private readonly AvFrame outer, inner;
        private readonly TMP_Text text;
        private string word, rail = "inert";

        public WingStamp(RectTransform parent, float degrees)
        {
            root = AvLay.Child(parent, "Stamp");
            wash = AvLay.Solid(root, "Wash", Color.clear);
            AvLay.Fill(wash.rectTransform);
            outer = AvFrame.Add(root, "Outer", default(AvChamfer));
            outer.Fill = false;
            AvLay.Fill(outer.rectTransform);
            inner = AvFrame.Add(root, "Inner", default(AvChamfer));
            inner.Fill = false;
            AvLay.Fill(inner.rectTransform, 3f);
            text = AvText.Make(root, "Word", AvTextRole.Label, "", TextAlignmentOptions.Center);
            AvText.Fit(text, false);
            AvLay.Fill(text.rectTransform, 4f);
            root.localRotation = Quaternion.Euler(0f, 0f, degrees);
            Paint();
        }

        public string Word => word;

        public void Place(float x, float y, float w, float h)
        {
            AvLay.Place(root, x, y, w, h);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
        }

        public void Set(string newWord, string railClass)
        {
            if (newWord == word && railClass == rail) return;
            word = newWord;
            rail = railClass ?? "inert";
            text.text = newWord ?? "";
            Paint();
        }

        public void Restyle() => Paint();

        public void SetShown(bool on)
        {
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
        }

        private void Paint()
        {
            Color c = WmcState.Color(rail);
            text.color = c;
            wash.color = c.WithAlpha(0.1f);
            outer.Paint(Color.clear, c.WithAlpha(0.9f));
            inner.Paint(Color.clear, c.WithAlpha(0.45f));
        }
    }

    /// <summary>The squadron's header: the insignia drawn from strokes, the squadron's name and one line of counts per status.</summary>
    internal sealed class WingSquadronHead : AvPart
    {
        private const float InsigniaW = 34f, InsigniaH = 40f, MinH = 46f;
        private readonly AvVector art;
        private readonly TMP_Text title, counts;
        private readonly Image rule;

        public WingSquadronHead(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "SquadronHead");
            art = AvVector.Create(Rect, "Insignia", 48);
            title = AvText.Make(Rect, "Title", AvTextRole.Head, SquadronWords.SquadronTitle);
            AvText.Fit(title, false);
            counts = AvText.Make(Rect, "Counts", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            Restyle();
        }

        public string CountsText => counts.text;

        public void SetCounts(string line)
        {
            if (counts.text == line) return;
            counts.text = line ?? "";
            Changed();
        }

        private float TextW(float w) => w - InsigniaW - 12f;

        public override float Measure(float width) => Mathf.Max(MinH, 22f + AvText.Height(counts, TextW(width)) + 4f);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(title.rectTransform, InsigniaW + 12f, 0f, TextW(s.W), 22f);
            AvLay.Place(counts.rectTransform, InsigniaW + 12f, 22f, TextW(s.W), Mathf.Max(16f, AvText.Height(counts, TextW(s.W))));
            AvLay.Place(rule.rectTransform, 0f, s.H - 1f, s.W, 1f);
            Draw(s.H);
        }

        private void Draw(float h)
        {
            art.Buffer.Clear();
            new WingInk(art.Buffer, h).Insignia(1f, 2f, InsigniaW, InsigniaH, AvStyleHost.FuiColor("frame", AvTheme.Frame), WmcState.Color("warn"));
            art.Commit();
        }

        public override void Restyle()
        {
            title.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            counts.color = WingRankColor.Dim;
            rule.color = AvStyleHost.FuiColor("hairline", AvTheme.Hairline).WithAlpha(0.6f);
        }
    }

    /// <summary>What the personnel file's face shows (a plain record so the part holds no game types).</summary>
    internal sealed class FileFace
    {
        public string Tab = "", Callsign = "", Name = "", RankLine = "", Stats = "", Radio = "", Stamp = "", Rail = "inert", RibbonHelp = "";
        public WingRank Rank;
        public float Xp;
        public bool Dim;
        public readonly RibbonId[] Rack = new RibbonId[Ribbons.Max];
        public int RibbonCount;
    }

    /// <summary>The top of the personnel file: the file tab, the real portrait on a paper clip, the callsign and name, the rank line, the rank
    /// ladder R W V A L with its progress, the record, the radio style, the ribbon rack and the rotated status stamp.</summary>
    internal sealed class WingFileTop : AvPart
    {
        public const float H = 164f;
        private const float PortraitW = 72f, PortraitH = 96f, TextX = 84f, StampW = 104f, StampH = 24f, TabH = 16f;
        private static readonly string[] LadderLetters = { "R", "W", "V", "A", "L" };
        private readonly Image portrait, track, fill, ribbonHit;
        private readonly AvFrame portraitBox;
        private readonly Image[] ticks = new Image[4];
        private readonly AvVector art;
        private readonly TMP_Text tab, callsign, name, rankLine, stats, radio, ribbonCaption;
        private readonly TMP_Text[] letters = new TMP_Text[5];
        private readonly WingStamp stamp;
        private readonly FileFace face = new FileFace();
        private WingPilot shown;
        private int look = int.MinValue;
        private int faction = int.MinValue;
        private bool portraitSet;
        private float width = 300f;

        public WingFileTop(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "FileTop");
            tab = AvText.Make(Rect, "Tab", AvTextRole.Micro);
            AvText.Fit(tab, false);
            portraitBox = AvFrame.Add(Rect, "PortraitBox", default(AvChamfer));
            portrait = AvLay.Solid(Rect, "Portrait", Color.white);
            portrait.preserveAspect = true;
            art = AvVector.Create(Rect, "Art", 160);
            callsign = AvText.Make(Rect, "Callsign", AvTextRole.Head);
            AvText.Fit(callsign, false);
            name = AvText.Make(Rect, "Name", AvTextRole.ProseSmall);
            AvText.Fit(name, false);
            rankLine = AvText.Make(Rect, "RankLine", AvTextRole.ProseSmall);
            AvText.Fit(rankLine, false);
            track = AvLay.Solid(Rect, "XpTrack", Color.clear);
            fill = AvLay.Solid(Rect, "XpFill", Color.clear);
            for (int i = 0; i < ticks.Length; i++) ticks[i] = AvLay.Solid(Rect, "Tick" + i, Color.clear);
            for (int i = 0; i < letters.Length; i++)
            {
                letters[i] = AvText.Make(Rect, "Rank" + i, AvTextRole.Micro, LadderLetters[i], TextAlignmentOptions.MidlineLeft);
                AvText.Fit(letters[i], false);
            }
            stats = AvText.Make(Rect, "Stats", AvTextRole.DataSmall);
            AvText.Fit(stats, false);
            radio = AvText.Make(Rect, "Radio", AvTextRole.ProseSmall);
            AvText.Fit(radio, false);
            ribbonCaption = AvText.Make(Rect, "RibbonCaption", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
            AvText.Fit(ribbonCaption, false);
            ribbonHit = AvLay.Solid(Rect, "RibbonHit", Color.clear);
            ribbonHit.raycastTarget = true;
            AvHelpTip.Attach(ribbonHit.gameObject, "");
            stamp = new WingStamp(Rect, 6f);
            Restyle();
        }

        public string StampWord => stamp.Word;

        public string RankText => rankLine.text;

        public string TabText => tab.text;

        public int RibbonCount => face.RibbonCount;

        public void SetPilot(WingPilot pilot)
        {
            int currentFaction = pilot != null && pilot.PortraitFaction >= 0 ? pilot.PortraitFaction : PortraitFactions.Local;
            if (portraitSet && ReferenceEquals(pilot, shown) && look == WingPilotRoster.LookVersion && faction == currentFaction) return;
            portraitSet = true;
            shown = pilot;
            look = WingPilotRoster.LookVersion;
            faction = currentFaction;
            portrait.sprite = PilotPortrait.For(pilot);
            portrait.enabled = portrait.sprite != null;
            portrait.color = pilot != null ? Color.white : Color.white.WithAlpha(0.3f);
        }

        /// <summary>Paints the file: the words, the ladder's fill, the ribbons and the stamp; redrawn only here and on a resize.</summary>
        public void Show(FileFace f)
        {
            face.Tab = f.Tab;
            face.Callsign = f.Callsign;
            face.Name = f.Name;
            face.RankLine = f.RankLine;
            face.Stats = f.Stats;
            face.Radio = f.Radio;
            face.Stamp = f.Stamp;
            face.Rail = f.Rail;
            face.RibbonHelp = f.RibbonHelp;
            face.Rank = f.Rank;
            face.Xp = f.Xp;
            face.Dim = f.Dim;
            face.RibbonCount = f.RibbonCount;
            Array.Copy(f.Rack, face.Rack, f.Rack.Length);
            WmcKit.Set(tab, f.Tab);
            WmcKit.Set(callsign, f.Callsign);
            WmcKit.Set(name, f.Name);
            WmcKit.Set(rankLine, f.RankLine);
            WmcKit.Set(stats, f.Stats);
            WmcKit.Set(radio, f.Radio);
            WmcKit.Set(ribbonCaption, f.RibbonCount > 0 ? AvNum.Fixed(f.RibbonCount, 0) + (f.RibbonCount == 1 ? " RIBBON" : " RIBBONS") : "NO RIBBONS");
            AvHelpTip.Attach(ribbonHit.gameObject, f.RibbonHelp);
            stamp.Set(f.Stamp, f.Rail);
            stamp.SetShown(f.Stamp != WmcText.Unknown);
            PlaceFill();
            Restyle();
            Draw();
        }

        public override float Measure(float w) => H;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            float rightW = s.W - TextX;
            AvLay.Place(tab.rectTransform, 0f, 0f, s.W, TabH);
            AvLay.Place(portraitBox.rectTransform, 0f, 22f, PortraitW, PortraitH);
            AvLay.Place(portrait.rectTransform, 1f, 23f, PortraitW - 2f, PortraitH - 2f);
            AvLay.Place(callsign.rectTransform, TextX, 20f, rightW - StampW - 8f, 20f);
            AvLay.Place(name.rectTransform, TextX, 40f, rightW - StampW - 8f, 16f);
            stamp.Place(s.W - StampW, 24f, StampW, StampH);
            AvLay.Place(rankLine.rectTransform, TextX + 22f, 58f, rightW - 22f, 16f);
            AvLay.Place(track.rectTransform, TextX, 80f, rightW, 6f);
            for (int i = 0; i < ticks.Length; i++) AvLay.Place(ticks[i].rectTransform, TextX + rightW * PilotXp.Tick(i + 1), 78f, 1f, 10f);
            float cell = rightW / letters.Length;
            for (int i = 0; i < letters.Length; i++) AvLay.Place(letters[i].rectTransform, TextX + i * cell + 18f, 90f, cell - 18f, 16f);
            AvLay.Place(stats.rectTransform, TextX, 108f, rightW, 16f);
            AvLay.Place(radio.rectTransform, TextX, 124f, rightW, 16f);
            AvLay.Place(ribbonCaption.rectTransform, 0f, 124f, TextX - 6f, 16f);
            AvLay.Place(ribbonHit.rectTransform, 0f, 142f, s.W, H - 142f);
            PlaceFill();
            Draw();
        }

        private void PlaceFill()
        {
            float rightW = Mathf.Max(0f, width - TextX);
            AvLay.Place(fill.rectTransform, TextX + 1f, 81f, Mathf.Max(0f, (rightW - 2f) * Mathf.Clamp01(float.IsNaN(face.Xp) ? 0f : face.Xp)), 4f);
        }

        /// <summary>The paper clip, the rank chevrons by the rank line and under each ladder letter, and the ribbon rack.</summary>
        private void Draw()
        {
            AvQuadBuffer b = art.Buffer;
            b.Clear();
            var ink = new WingInk(b, H);
            ink.Clip(PortraitW - 22f, 14f, AvStyleHost.FuiColor("ink-dim", AvTheme.Disabled));
            float rightW = width - TextX;
            ink.Rank(TextX + 9f, 66f, 14f, face.Rank, WingRankColor.Of(face.Rank));
            float cell = rightW / letters.Length;
            for (int i = 0; i < letters.Length; i++)
            {
                bool lit = i == (int)face.Rank;
                ink.Rank(TextX + i * cell + 8f, 97f, 12f, (WingRank)i, lit ? WingRankColor.Of((WingRank)i) : WingRankColor.Dim);
            }
            // The rack: one small ribbon per earned ribbon, in the order Ribbons.For gives them (the words are in the help).
            float x = TextX;
            for (int i = 0; i < face.RibbonCount; i++)
            {
                RibbonArt(face.Rack[i], out Color field, out Color stripe);
                ink.Ribbon(x, 146f, 24f, 11f, field, stripe);
                x += 27f;
            }
            if (face.RibbonCount == 0) ink.Rect(TextX, 151f, 24f, 1f, AvStyleHost.FuiColor("hairline", AvTheme.Hairline));
            art.Commit();
        }

        private static void RibbonArt(RibbonId id, out Color field, out Color stripe)
        {
            Color danger = WmcState.Color("danger"), info = WmcState.Color("info"), warn = WmcState.Color("warn"), live = WmcState.Color("live");
            Color ink = WingRankColor.Ink, dark = AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
            switch (id)
            {
                case RibbonId.Kills5: field = danger; stripe = ink; break;
                case RibbonId.Kills10: field = danger; stripe = warn; break;
                case RibbonId.Sorties5: field = info; stripe = dark; break;
                case RibbonId.Sorties20: field = info; stripe = warn; break;
                case RibbonId.Wingman: field = live; stripe = ink; break;
                case RibbonId.Veteran: field = warn; stripe = dark; break;
                case RibbonId.Ace: field = live; stripe = warn; break;
                default: field = warn; stripe = danger; break;
            }
        }

        public override void Restyle()
        {
            tab.color = WmcState.Color("info");
            portraitBox.Paint(AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert), AvStyleHost.FuiColor("frame", AvTheme.Frame));
            callsign.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            name.color = WingRankColor.Dim;
            rankLine.color = WingRankColor.Dim;
            stats.color = WingRankColor.Ink;
            radio.color = WingRankColor.Dim;
            ribbonCaption.color = WingRankColor.Dim;
            track.color = AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
            fill.color = face.Dim ? WingRankColor.Dim : AvStyleHost.FuiColor("friendly", AvTheme.Friendly);
            Color hairline = AvStyleHost.FuiColor("hairline", AvTheme.Hairline);
            foreach (Image t in ticks) t.color = hairline;
            for (int i = 0; i < letters.Length; i++)
                letters[i].color = i == (int)face.Rank ? WingRankColor.Of((WingRank)i) : WingRankColor.Dim;
            stamp.Restyle();
        }
    }
}
