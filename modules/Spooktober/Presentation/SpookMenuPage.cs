using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Spooktober.Domain;
using BoscaliSummer.Modules.Spooktober.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Spooktober.Presentation
{
    /// <summary>
    /// The SPOOKTOBER page (a MISC tab, always shown): an evidence locker of three files, one per
    /// monster, each drawn as its own kind of evidence card. Nothing is locked by date or order. The KAIJU is a manila specimen tag
    /// (hazard tape, scale ruler, claw marks), the FALLEN ACE a cold MIA personnel file (crossed-out photo, paperclip, redacted
    /// callsign), HELL'S ARMY a charred breach sheet (scorch marks, tally of waves, ember ink). A rubber stamp says
    /// where each one is; under every card its own two buttons spawn and send it away (host only). Glitches are a pure function
    /// of <see cref="SpookConsole.Clock"/>.
    /// </summary>
    internal static class SpookMenuPage
    {
        private static readonly string[] Captions = { "EYES ONLY · ACCESS LOGGED", "IT HEARS YOUR RADIO", "DO NOT LOOK BACK", "COUNT YOUR WINGMEN" };

        /// <summary>One monster's file: its paper, its inks and its words.</summary>
        private sealed class Theme
        {
            public SpookBoss Boss;
            public string Name, Numeral, Header, CaseNo, Exhibit, Note, Spawn, Send, Out, Idle;
            /// <summary>How the photo is pinned to the file, degrees.</summary>
            public float Tilt;
            public (string label, string value)[] Fields;
            public AvIcon Icon;
            public Color Paper, Ink, Band, BandInk, Photo, Stamp;
        }

        private static readonly Theme[] Monsters =
        {
            new Theme
            {
                Boss = SpookBoss.Kaiju, Name = "KAIJU", Numeral = "0313", Tilt = -3f, Header = "FILE BSC-0313", CaseNo = "LAST SEEN · SEA FLOOR", Exhibit = "FIG.1 · SONAR 03:13",
                Icon = AvIcon.Activity, Spawn = "SUMMON", Send = "BANISH", Out = "ACTIVE", Idle = "DORMANT",
                Fields = new[] { ("THREAT TYPE", "TITAN"), ("THREAT LEVEL", "RED · TITANIC"), ("WEAK POINTS", "21 · HEART LAST") },
                Note = "BSC-0313 sleeps under the sea floor; sonar watch at all times. If it rises, all flights strike the 21 weak " +
                       "points. The heart opens last. Do not let it reach an airbase. [REDACTED]",
                Paper = new Color32(201, 176, 124, 255), Ink = new Color32(58, 38, 18, 255), Band = new Color32(122, 84, 34, 255),
                BandInk = new Color32(248, 228, 186, 255), Photo = new Color32(44, 34, 22, 255), Stamp = new Color32(176, 36, 22, 255)
            },
            new Theme
            {
                Boss = SpookBoss.FallenAce, Name = "FALLEN ACE", Numeral = "1987", Tilt = 2.5f, Header = "FILE BSC-1987", CaseNo = "LAST SEEN · HANGAR 4", Exhibit = "FIG.1 · KIA 1987",
                Icon = AvIcon.Plane, Spawn = "SUMMON", Send = "BANISH", Out = "AIRBORNE", Idle = "MISSING",
                Fields = new[] { ("CALLSIGN", ""), ("THREAT TYPE", "SPECTRE"), ("THREAT LEVEL", "RED · GUN ONLY") },
                Note = "Struck from the roster in 1987; still logs sorties. Hostile to every side. Three lives. Missiles fired from " +
                       "range chase a phantom. Personnel must not answer it on guard. [REDACTED]",
                Paper = new Color32(178, 192, 200, 255), Ink = new Color32(18, 32, 50, 255), Band = new Color32(34, 56, 84, 255),
                BandInk = new Color32(214, 230, 242, 255), Photo = new Color32(22, 30, 40, 255), Stamp = new Color32(160, 24, 30, 255)
            },
            new Theme
            {
                Boss = SpookBoss.HellsArmy, Name = SpookMenu.HellTitle, Numeral = "0666", Tilt = -1.5f, Header = "FILE BSC-0666", CaseNo = "LAST SEEN · [REDACTED]", Exhibit = "FIG.1 · THE GATE",
                Icon = AvIcon.Flame, Spawn = "SUMMON", Send = "BANISH", Out = "GATE OPEN", Idle = "SEALED",
                Fields = new[] { ("THREAT TYPE", "LEGION"), ("THREAT LEVEL", "BLACK · ENDLESS"), ("ORIGIN", "A GATE IN THE GROUND") },
                Note = "BSC-0666 cannot be fought to the end, only shut. A gate opens on the ground and an army keeps coming " +
                       "through. To close it, fly through the gate. What waits inside is [REDACTED]",
                Paper = new Color32(30, 13, 11, 255), Ink = new Color32(255, 112, 60, 255), Band = new Color32(122, 14, 8, 255),
                BandInk = new Color32(255, 214, 170, 255), Photo = new Color32(10, 4, 4, 255), Stamp = new Color32(255, 60, 32, 255)
            },
        };

        public static void Build(int page, AvFlow flow, AvConsole av, SpookConsole console, Func<bool> kaijuAvailable)
        {
            AvSection section = flow.Section(AvIcon.Skull, "BOSCALI · INCIDENT FILES", Captions[0]);
            RoutingSlip slip = flow.Add(new RoutingSlip(flow.Content)); // the warning, as a slip clipped on top of the files
            var cards = new EvidenceCard[Monsters.Length];
            var spawns = new AvControl[Monsters.Length];
            var sends = new AvControl[Monsters.Length];
            for (int i = 0; i < Monsters.Length; i++)
            {
                Theme theme = Monsters[i];
                cards[i] = flow.Add(new EvidenceCard(flow.Content, theme));
                AvButtons buttons = flow.Buttons(
                    new AvControl.Spec(theme.Spawn, () => { SpookAudio.Play(SpookAudio.Stamp); console.Say(SpookBosses.Spawn(theme.Boss), 3f); },
                        AvButtonStyle.Primary, theme.Icon),
                    new AvControl.Spec(theme.Send, () => { SpookAudio.Play(SpookAudio.Stamp, .6f); console.Say(SpookBosses.Dismiss(), 3f); },
                        AvButtonStyle.Quiet, AvIcon.X));
                spawns[i] = buttons.Controls[0];
                sends[i] = buttons.Controls[1];
                sends[i].Help = "Sends it away. That is not a kill.";
            }

            float lastSlow = float.NegativeInfinity;
            int whisperedSlot = -1;
            void Slow()
            {
                float now = SpookConsole.Clock();
                // Slow ticks run only while the page shows: a gap means the files were just opened again.
                bool opened = now - lastSlow > 1.5f;
                if (opened && lastSlow > float.NegativeInfinity) SpookAudio.Menu(SpookAudio.File, flow.Content, .7f);
                lastSlow = now;
                int slot = (int)(now / 6f);
                bool creepy = Roll(slot, 1) % 4 == 0;
                section.SetCaption(creepy ? Captions[1 + Roll(slot, 2) % 3] : Captions[0]);
                if (creepy && slot != whisperedSlot && !opened)
                {
                    whisperedSlot = slot; // the caption turns, and something on the net whispers once
                    SpookAudio.Menu(SpookAudio.Whisper, flow.Content, .45f);
                }
                slip.Refresh(now);
                SpookProgress progress = SpookStore.Current;
                SpookBoss? active = SpookBosses.Active;
                bool host = !SpookMenu.SpawnBlocked(GameAccess.IsServer());
                for (int i = 0; i < Monsters.Length; i++)
                {
                    Theme theme = Monsters[i];
                    bool kaiju = theme.Boss == SpookBoss.Kaiju;
                    bool available = !kaiju || kaijuAvailable();
                    bool out_ = active == theme.Boss;
                    cards[i].Refresh(now, out_, progress.Beaten(theme.Boss), available, kaiju ? SpookMenu.KaijuCard(available) : theme.Name);
                    spawns[i].Label = host ? theme.Spawn : "HOST ONLY";
                    spawns[i].Interactable = host && available;
                    spawns[i].Help = host ? "Summons it ahead of your aircraft. Banishes any other monster already out." : "Only the host can summon.";
                    sends[i].Interactable = host && (out_ || kaiju && SpookBosses.KaijuBodyOut);
                }
            }
            Slow();
            flow.Ticker.Add(page, AvTickRate.Slow, Slow);
            console.RegisterMessage(page, flow, menu: true);
            av.Orders(page,
                "One monster on the field at a time. A new summon banishes the last.",
                "Only the host summons. Every pilot's kills are filed.",
                "Banishing is not a kill.",
                "If the radio says your name, do not answer. Report it.");
        }

        /// <summary>
        /// One evidence card, drawn by hand: paper, a double rule, the header band, a photo box with the monster's mark, three
        /// typed fields and a status line on ruled lines, a handwritten note, a chain-of-custody footer with a barcode, the
        /// theme's own marks, and a rotated rubber stamp on top. Fixed height; the width follows the page.
        /// </summary>
        private sealed class EvidenceCard : AvPart
        {
            private const float Height = 282f, Band = 32f, PhotoW = 112f, PhotoH = 118f, Top = 44f, Pad = 14f, Row = 24f;
            private readonly Theme theme;
            private readonly Image paper, band, photo, foot;
            private readonly AvFrame rule;
            private readonly TMP_Text header, caseNo, mark, exhibit, procedures, note, custody, record;
            private readonly TMP_Text[] labels, values;
            private readonly Image[] lines;
            private readonly Image[] bars = new Image[28];
            private readonly RectTransform stamp;
            private readonly AvFrame stampFrame;
            private readonly TMP_Text stampText;
            private readonly Image[] extras;
            private readonly TMP_Text[] extraText;
            private readonly RectTransform pinned;
            private readonly TMP_Text watermark, glow, live;
            private readonly Image[] grain = new Image[9], mounts = new Image[4];
            private readonly Image liveDot, ember, eyeletRing, eyeletHole;
            private readonly AvFrame stampInner;
            private readonly AvFrame classTag;
            private readonly int classRow = -1;
            private string shownName;

            public EvidenceCard(RectTransform parent, Theme theme)
            {
                this.theme = theme;
                Rect = AvLay.Child(parent, "Evidence " + theme.Name);
                Rect.gameObject.AddComponent<RectMask2D>(); // tape, scorch and stamp stay on the paper
                paper = AvLay.Solid(Rect, "Paper", theme.Paper);
                // Grain: faint stripes of lighter and darker paper.
                for (int i = 0; i < grain.Length; i++)
                    grain[i] = AvLay.Solid(Rect, "Grain", (i % 2 == 0 ? Color.white : Color.black).WithAlpha(.035f + .02f * (Roll(i, 13) % 3)));
                // The file's numeral, huge and faint, behind everything typed on it.
                watermark = Text("Watermark", AvTextRole.Display, theme.Numeral, theme.Ink.WithAlpha(.07f), 84f, TextAlignmentOptions.Center);
                rule = AvFrame.Add(Rect, "Rule", AvChamfer.Diagonal(0f));
                rule.Fill = false;
                rule.Stroke = 1f;
                rule.Paint(Color.clear, theme.Ink.WithAlpha(.55f));
                band = AvLay.Solid(Rect, "Band", theme.Band);
                header = Text("Header", AvTextRole.Title, theme.Header, theme.BandInk, 15f);
                header.characterSpacing = 6f;
                caseNo = Text("Case", AvTextRole.DataSmall, theme.CaseNo, theme.BandInk.WithAlpha(.85f), 11f, TextAlignmentOptions.MidlineRight);
                // LIVE: a lamp and a word in the band, lit only while the monster is out there.
                liveDot = AvLay.Solid(Rect, "LiveDot", new Color32(255, 40, 30, 255));
                live = Text("Live", AvTextRole.DataStrong, "LIVE", new Color32(255, 70, 50, 255), 13f);

                // The photo, pinned at a slant: dark print, the monster's mark (hell's glows), four corner mounts.
                pinned = AvLay.Child(Rect, "Pinned");
                photo = AvLay.Solid(pinned, "Photo", theme.Photo);
                glow = AvIcons.Make(pinned, theme.Icon, 92f, theme.Ink.WithAlpha(theme.Boss == SpookBoss.HellsArmy ? .28f : 0f));
                Color markInk = theme.Boss == SpookBoss.HellsArmy ? theme.Ink : theme.Paper.WithAlpha(.85f);
                mark = AvIcons.Make(pinned, theme.Icon, 64f, markInk);
                for (int i = 0; i < mounts.Length; i++) mounts[i] = AvLay.Solid(pinned, "Mount", theme.Ink.WithAlpha(.85f));
                exhibit = Text("Exhibit", AvTextRole.Micro, theme.Exhibit, theme.Ink, 10f, TextAlignmentOptions.Center);

                int n = theme.Fields.Length + 2; // SUBJECT, the theme's fields, STATUS
                labels = new TMP_Text[n];
                values = new TMP_Text[n];
                lines = new Image[n];
                for (int i = 0; i < n; i++)
                {
                    string label = i == 0 ? "DESIGNATION" : i == n - 1 ? "STATUS" : theme.Fields[i - 1].label;
                    string value = i == 0 ? theme.Name : i == n - 1 ? "" : theme.Fields[i - 1].value;
                    // Field names: small but readable on every paper (85 % ink), tracked like a typed form.
                    labels[i] = Text("Label " + label, AvTextRole.Micro, label, theme.Ink.WithAlpha(.85f), 10f, TextAlignmentOptions.BottomLeft);
                    labels[i].characterSpacing = 1.5f;
                    values[i] = Text("Value " + label, i == 0 ? AvTextRole.DataStrong : AvTextRole.Data, value, theme.Ink, i == 0 ? 15f : 12f,
                        TextAlignmentOptions.BottomLeft);
                    lines[i] = AvLay.Solid(Rect, "Line " + label, theme.Ink.WithAlpha(.35f));
                    if (label != "THREAT TYPE") continue;
                    // The threat type is the word that matters most on the file: bold, in its own colour (the word still says it).
                    classRow = i;
                    values[i].fontStyle = FontStyles.Bold;
                    values[i].characterSpacing = 3f;
                    values[i].color = value == "TITAN" ? new Color32(150, 14, 14, 255) : value == "SPECTRE" ? new Color32(140, 84, 0, 255) : Color.white;
                }
                // LEGION wears a black tag with an ember rim: the one that cannot be fought to the end.
                classTag = AvFrame.Add(Rect, "ClassTag", AvChamfer.Diagonal(3f));
                classTag.Stroke = 1f;
                classTag.Paint(Color.black, theme.Ink);
                classTag.gameObject.SetActive(classRow >= 0 && values[classRow].text == "LEGION");
                if (classRow >= 0) classTag.transform.SetSiblingIndex(values[classRow].transform.GetSiblingIndex());
                procedures = Text("Procedures", AvTextRole.Micro, "FIELD ORDERS:", theme.Ink, 10f);
                procedures.characterSpacing = 2f;
                note = Text("Note", AvTextRole.ProseSmall, theme.Note, theme.Ink, 12f, TextAlignmentOptions.TopLeft);
                note.lineSpacing = 14f; // ~1.5 line height: the one paragraph worth reading
                note.enableWordWrapping = true;
                foot = AvLay.Solid(Rect, "Foot", theme.Ink.WithAlpha(.12f));
                custody = Text("Custody", AvTextRole.Micro, theme.Header + " · REV 7 · EYES ONLY · DO NOT COPY", theme.Ink.WithAlpha(.9f), 9f);
                record = Text("Record", AvTextRole.Label, "", theme.Ink, 11f);
                for (int i = 0; i < bars.Length; i++) bars[i] = AvLay.Solid(Rect, "Bar", theme.Ink.WithAlpha(.8f));
                // A punched eyelet on the tag (kaiju), an ember edge where the sheet burnt (hell).
                eyeletRing = AvLay.Solid(Rect, "EyeletRing", theme.BandInk.WithAlpha(theme.Boss == SpookBoss.Kaiju ? .9f : 0f));
                eyeletHole = AvLay.Solid(Rect, "EyeletHole", new Color(0f, 0f, 0f, theme.Boss == SpookBoss.Kaiju ? .85f : 0f));
                ember = AvLay.Solid(Rect, "Ember", theme.Ink.WithAlpha(theme.Boss == SpookBoss.HellsArmy ? .8f : 0f));

                (extras, extraText) = Decorations();
                // What is drawn on the photo itself tilts with it.
                if (theme.Boss == SpookBoss.Kaiju)
                {
                    for (int i = 3; i < 18; i++) extras[i].rectTransform.SetParent(pinned, false);
                    extraText[1].rectTransform.SetParent(pinned, false);
                }
                else if (theme.Boss == SpookBoss.FallenAce)
                    for (int i = 0; i < 4; i++) extras[i].rectTransform.SetParent(pinned, false);

                stamp = AvLay.Child(Rect, "Stamp");
                stampFrame = AvFrame.Add(stamp, "StampFrame", AvChamfer.Diagonal(0f));
                stampFrame.Fill = false;
                stampFrame.Stroke = 3f;
                AvLay.Fill(stampFrame.rectTransform);
                stampInner = AvFrame.Add(stamp, "StampInner", AvChamfer.Diagonal(0f));
                stampInner.Fill = false;
                stampInner.Stroke = 1.2f;
                AvLay.Fill(stampInner.rectTransform, 5f);
                stampText = AvText.Make(stamp, "StampText", AvTextRole.Display, "", TextAlignmentOptions.Center);
                stampText.fontSize = 17f;
                stampText.characterSpacing = 4f;
                AvLay.Fill(stampText.rectTransform);
            }

            private TMP_Text Text(string name, AvTextRole role, string text, Color color, float size,
                TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
            {
                TMP_Text t = AvText.Make(Rect, name, role, text, align);
                t.color = color;
                t.fontSize = size;
                return t;
            }

            /// <summary>Each file's own marks, made once; <see cref="Place"/> lays them out.</summary>
            private (Image[], TMP_Text[]) Decorations()
            {
                switch (theme.Boss)
                {
                    case SpookBoss.Kaiju:
                        // Hazard tape across the corner, a scale ruler under the photo, three claw marks through the fields.
                        var kaiju = new Image[3 + 12 + 3];
                        kaiju[0] = AvLay.Solid(Rect, "Tape", new Color32(232, 196, 40, 245));
                        kaiju[1] = AvLay.Solid(Rect, "TapeEdgeA", new Color32(24, 20, 10, 255));
                        kaiju[2] = AvLay.Solid(Rect, "TapeEdgeB", new Color32(24, 20, 10, 255));
                        for (int i = 3; i < 15; i++) kaiju[i] = AvLay.Solid(Rect, "Tick", theme.Ink);
                        for (int i = 15; i < 18; i++) kaiju[i] = AvLay.Solid(Rect, "Claw", theme.Paper.WithAlpha(.5f));
                        return (kaiju, new[] { Text("TapeText", AvTextRole.Micro, "DO NOT OPEN · BIOHAZARD", new Color32(24, 20, 10, 255), 9f, TextAlignmentOptions.Center),
                            Text("Scale", AvTextRole.Micro, "SCALE 1:15", theme.Ink.WithAlpha(.8f), 8f, TextAlignmentOptions.Center) });
                    case SpookBoss.FallenAce:
                        // A red X through the photo, a paperclip on its top edge, a redaction bar over the callsign.
                        var ace = new[]
                        {
                            AvLay.Solid(Rect, "CrossA", new Color32(176, 26, 30, 220)), AvLay.Solid(Rect, "CrossB", new Color32(176, 26, 30, 220)),
                            AvLay.Solid(Rect, "Clip", new Color32(120, 128, 136, 255)), AvLay.Solid(Rect, "ClipInner", theme.Paper),
                            AvLay.Solid(Rect, "Redact", new Color32(8, 10, 14, 255)),
                        };
                        return (ace, new[] { Text("RedactNote", AvTextRole.Micro, "WITHHELD BY ORDER", new Color32(150, 160, 170, 255), 8f, TextAlignmentOptions.Center),
                            Text("TopSecret", AvTextRole.Display, "TOP SECRET", theme.Stamp.WithAlpha(.09f), 46f, TextAlignmentOptions.Center) });
                    default:
                        // Scorch marks on the edges and a tally of waves scratched in ember.
                        var hell = new Image[5 + 6];
                        for (int i = 0; i < 5; i++) hell[i] = AvLay.Solid(Rect, "Scorch", new Color32(0, 0, 0, 80));
                        for (int i = 5; i < 11; i++) hell[i] = AvLay.Solid(Rect, "Tally", theme.Ink.WithAlpha(.85f));
                        return (hell, new[] { Text("TallyLabel", AvTextRole.Micro, "WAVES", theme.Ink.WithAlpha(.7f), 8f) });
                }
            }

            public override float Measure(float width) => Height;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float w = s.W;
                AvLay.Place(paper.rectTransform, 0f, 0f, w, Height);
                for (int i = 0; i < grain.Length; i++)
                    AvLay.Place(grain[i].rectTransform, 0f, Band + i * (Height - Band) / grain.Length + Roll(i, 17) % 9, w, 2f + Roll(i, 19) % 7);
                AvLay.Place(watermark, w - 260f, Band, 250f, 120f);
                AvLay.Place(rule.rectTransform, 4f, 4f, w - 8f, Height - 8f);
                AvLay.Place(band.rectTransform, 0f, 0f, w, Band);
                float hx = theme.Boss == SpookBoss.Kaiju ? Pad + 22f : Pad;
                AvLay.Place(eyeletRing.rectTransform, Pad - 2f, Band * .5f - 8f, 16f, 16f);
                AvLay.Place(eyeletHole.rectTransform, Pad + 2f, Band * .5f - 4f, 8f, 8f);
                AvLay.Place(header, hx, 0f, w * .62f, Band);
                AvLay.Place(caseNo, w * .55f, 0f, w * .45f - Pad, Band);
                AvLay.Place(liveDot.rectTransform, w * .36f - 6f, Band * .5f - 5f, 10f, 10f);
                AvLay.Place(live, w * .36f + 9f, 0f, 48f, Band);
                AvLay.Place(ember.rectTransform, 0f, Height - 3f, w, 3f);

                Turn(pinned, Pad, Top, PhotoW, PhotoH, theme.Tilt);
                AvLay.Place(photo.rectTransform, 0f, 0f, PhotoW, PhotoH);
                AvLay.Place(glow, 0f, 0f, PhotoW, PhotoH - 10f);
                glow.alignment = TextAlignmentOptions.Center;
                AvLay.Place(mark, 0f, 0f, PhotoW, PhotoH - 14f);
                mark.alignment = TextAlignmentOptions.Center;
                // Corner mounts: small dark diamonds half off each corner.
                for (int i = 0; i < mounts.Length; i++)
                    Turn(mounts[i].rectTransform, (i % 2 == 0 ? 0f : PhotoW) - 7f, (i < 2 ? 0f : PhotoH) - 7f, 14f, 14f, 45f);
                AvLay.Place(exhibit, Pad, Top + PhotoH + 2f, PhotoW, 14f);

                float fx = Pad + PhotoW + 14f, fw = w - fx - Pad;
                for (int i = 0; i < labels.Length; i++)
                {
                    float y = Top + i * Row;
                    AvLay.Place(labels[i], fx, y, 84f, Row - 4f);
                    AvLay.Place(values[i], fx + 86f, y - 2f, fw - 86f, Row - 2f);
                    AvLay.Place(lines[i].rectTransform, fx, y + Row - 3f, fw, 1f);
                    if (i == classRow) AvLay.Place(classTag.rectTransform, fx + 80f, y + 1f, values[i].preferredWidth + 14f, Row - 5f);
                }
                float ny = Top + PhotoH + 18f;
                AvLay.Place(procedures, Pad, ny, w - 2f * Pad, 13f);
                AvLay.Place(note, Pad, ny + 15f, w - 2f * Pad, 48f);
                AvLay.Place(foot.rectTransform, 4f, Height - 34f, w - 8f, 30f);
                AvLay.Place(custody, Pad, Height - 34f, w * .6f, 15f);
                AvLay.Place(record, Pad, Height - 20f, w * .6f, 15f);

                // A barcode from the case number: the same bars every time.
                float bx = w - Pad - 110f;
                for (int i = 0; i < bars.Length; i++)
                {
                    float bw = 1f + Roll(i, theme.CaseNo.Length + (int)theme.Boss) % 3;
                    AvLay.Place(bars[i].rectTransform, bx, Height - 31f, bw, 24f);
                    bx += bw + 1.6f;
                }
                PlaceDecorations(w);
                // The rubber stamp sits low on the file, across the status line and the note, as if thumped on in a hurry.
                // Stamped on the photo, where it hides nothing anyone needs to read.
                Turn(stamp, Pad - 4f, Top + PhotoH - 46f, PhotoW + 8f, 36f, theme.Boss == SpookBoss.FallenAce ? -8f : theme.Boss == SpookBoss.Kaiju ? -13f : 7f);
                stamp.SetAsLastSibling();
            }

            private void PlaceDecorations(float w)
            {
                switch (theme.Boss)
                {
                    case SpookBoss.Kaiju:
                        // The hazard tape seals the file across its bottom corner (clear of the header and the procedures).
                        Turn(extras[0].rectTransform, w - 150f, Height - 22f, 200f, 20f, -14f);
                        Turn(extras[1].rectTransform, w - 150f, Height - 24f, 200f, 2f, -14f);
                        Turn(extras[2].rectTransform, w - 150f, Height - 4f, 200f, 2f, -14f);
                        Turn(extraText[0].rectTransform, w - 150f, Height - 22f, 200f, 20f, -14f);
                        // On the photo (its own coordinates): the scale ruler along its foot, three claw marks torn through it.
                        for (int i = 0; i < 12; i++)
                            AvLay.Place(extras[3 + i].rectTransform, 4f + i * ((PhotoW - 8f) / 11f), PhotoH - 10f, 1f, i % 5 == 0 ? 10f : 5f);
                        AvLay.Place(extraText[1], 0f, PhotoH - 24f, PhotoW, 12f);
                        for (int i = 0; i < 3; i++) Turn(extras[15 + i].rectTransform, 52f + i * 13f, 6f, 4f, PhotoH - 30f, 24f);
                        break;
                    case SpookBoss.FallenAce:
                        // On the photo: the red X and the paperclip biting its top edge.
                        Turn(extras[0].rectTransform, -10f, PhotoH * .5f - 2f, PhotoW + 20f, 4f, 46f);
                        Turn(extras[1].rectTransform, -10f, PhotoH * .5f - 2f, PhotoW + 20f, 4f, -46f);
                        AvLay.Place(extras[2].rectTransform, 18f, -8f, 14f, 30f);
                        AvLay.Place(extras[3].rectTransform, 21f, -5f, 8f, 22f);
                        float fx = Pad + PhotoW + 14f + 86f;
                        AvLay.Place(extras[4].rectTransform, fx, Top + Row + 4f, 118f, 15f);
                        AvLay.Place(extraText[0], fx, Top + Row + 4f, 118f, 15f);
                        Turn(extraText[1].rectTransform, w * .5f - 160f, Top + 30f, 320f, 60f, -16f);
                        extraText[1].rectTransform.SetSiblingIndex(watermark.rectTransform.GetSiblingIndex() + 1); // under the typing
                        break;
                    default:
                        float[] sx = { -8f, w - 30f, -10f, w - 24f, w * .45f };
                        float[] sy = { Height - 14f, Band + 2f, Height - 40f, Height - 60f, Height - 8f };
                        for (int i = 0; i < 5; i++) Turn(extras[i].rectTransform, sx[i], sy[i], 34f + i * 6f, 12f + i * 2f, i * 37f); // small burns at the edges
                        // The tally of waves, scratched into the footer beside the barcode.
                        float tx = w - Pad - 172f;
                        for (int i = 0; i < 5; i++) AvLay.Place(extras[5 + i].rectTransform, tx + i * 6f, Height - 31f, 2f, 14f);
                        Turn(extras[10].rectTransform, tx - 2f, Height - 25f, 30f, 2f, -24f);
                        AvLay.Place(extraText[0], tx, Height - 15f, 44f, 11f);
                        break;
                }
            }

            /// <summary>Places a rect and turns it about its centre.</summary>
            internal static void Turn(RectTransform t, float x, float y, float w, float h, float degrees)
            {
                AvLay.Place(t, x, y, w, h);
                t.pivot = new Vector2(.5f, .5f);
                t.anchoredPosition = new Vector2(x + w * .5f, -(y + h * .5f));
                t.localEulerAngles = new Vector3(0f, 0f, degrees);
            }

            /// <summary>2 Hz: the stamp, the status line, the record and the odd glitch.</summary>
            public void Refresh(float now, bool out_, bool beaten, bool available, string name)
            {
                // The subject's name glitches for one tick now and then, never while it is out there (you need to read it then).
                if (!out_ && Roll((int)(now * 2f), 3 + (int)theme.Boss) % 17 == 0) name = SpookText.Glitch(name, (int)now, 0.4f);
                if (name != shownName)
                {
                    shownName = name;
                    values[0].text = name;
                }
                string word = !available ? "OFFLINE" : out_ ? theme.Out : beaten ? "DESTROYED" : theme.Idle;
                Color ink = out_ ? theme.Stamp : beaten ? new Color32(36, 120, 64, 255) : !available ? theme.Ink.WithAlpha(.6f) : theme.Stamp;
                ink.a = out_ ? ((int)(now * 2f) % 2 == 0 ? 1f : .7f) : .78f;
                if (stampText.text != word) stampText.text = word;
                stampText.color = ink;
                stampFrame.Paint(Color.clear, ink);
                stampInner.Paint(Color.clear, ink.WithAlpha(ink.a * .8f));

                bool lit = out_ && (int)(now * 2f) % 2 == 0;
                liveDot.enabled = live.enabled = out_;
                liveDot.color = liveDot.color.WithAlpha(lit ? 1f : .35f);
                live.alpha = lit ? 1f : .55f;
                if (theme.Boss == SpookBoss.HellsArmy)
                {
                    // The burnt edge smoulders; out there it flares.
                    float heat = (out_ ? .75f : .35f) + (out_ ? .25f : .2f) * (Roll((int)(now * 2f), 23) / 65535f);
                    ember.color = theme.Ink.WithAlpha(heat);
                    glow.alpha = out_ ? .45f : .25f;
                }
                values[values.Length - 1].text = out_ ? "ACTIVE · ON THE FIELD" : !available ? "MODULE OFFLINE" : beaten ? "DESTROYED" : "DORMANT";
                record.text = beaten ? "AFTER-ACTION: DESTROYED BY [YOU]" : "AFTER-ACTION: NO KILL ON FILE";
            }
        }

        /// <summary>
        /// The routing slip clipped on top of the files, in the cards' own paper language: blood-red stock with grain and a cream
        /// double rule, a rotated EYES ONLY stamp, three typed lines (routing, who opened it and when, the rule) and a perforated
        /// tear line along the foot. The OPENED line ticks with the clock.
        /// </summary>
        private sealed class RoutingSlip : AvPart
        {
            private const float Height = 78f, Pad = 14f, StampW = 112f;
            private static readonly Color Stock = new Color32(104, 16, 14, 255), Cream = new Color32(238, 222, 196, 255);
            private readonly Image paper;
            private readonly Image[] grain = new Image[5], perforation = new Image[40];
            private readonly AvFrame rule, stampOuter, stampInner;
            private readonly RectTransform stamp;
            private readonly TMP_Text stampText, routing, opened, warning, slipNo;

            public RoutingSlip(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "RoutingSlip");
                Rect.gameObject.AddComponent<RectMask2D>();
                paper = AvLay.Solid(Rect, "Paper", Stock);
                for (int i = 0; i < grain.Length; i++) grain[i] = AvLay.Solid(Rect, "Grain", Color.black.WithAlpha(.08f + .04f * (i % 2)));
                rule = AvFrame.Add(Rect, "Rule", AvChamfer.Diagonal(0f));
                rule.Fill = false;
                rule.Stroke = 1f;
                rule.Paint(Color.clear, Cream.WithAlpha(.5f));
                for (int i = 0; i < perforation.Length; i++) perforation[i] = AvLay.Solid(Rect, "Hole", new Color(0f, 0f, 0f, .55f));

                stamp = AvLay.Child(Rect, "Stamp");
                stampOuter = AvFrame.Add(stamp, "Outer", AvChamfer.Diagonal(0f));
                stampOuter.Fill = false;
                stampOuter.Stroke = 3f;
                stampOuter.Paint(Color.clear, Cream.WithAlpha(.9f));
                AvLay.Fill(stampOuter.rectTransform);
                stampInner = AvFrame.Add(stamp, "Inner", AvChamfer.Diagonal(0f));
                stampInner.Fill = false;
                stampInner.Stroke = 1.2f;
                stampInner.Paint(Color.clear, Cream.WithAlpha(.75f));
                AvLay.Fill(stampInner.rectTransform, 5f);
                stampText = AvText.Make(stamp, "Word", AvTextRole.Display, "EYES ONLY", TextAlignmentOptions.Center);
                stampText.fontSize = 17f;
                stampText.characterSpacing = 3f;
                stampText.color = Cream.WithAlpha(.95f);
                AvLay.Fill(stampText.rectTransform);

                routing = Typed("Routing", "ROUTING · BOSCALI INCIDENT FILES · 3 ENCLOSED", 11f, 1f);
                opened = Typed("Opened", "", 11f, .9f);
                warning = Typed("Warning", "DO NOT READ ALOUD ON THE NET.", 11f, 1f);
                warning.fontStyle = FontStyles.Bold;
                slipNo = Typed("SlipNo", "SLIP 31-0", 9f, .6f);
                slipNo.alignment = TextAlignmentOptions.TopRight;
            }

            private TMP_Text Typed(string name, string text, float size, float alpha)
            {
                TMP_Text t = AvText.Make(Rect, name, AvTextRole.Data, text, TextAlignmentOptions.MidlineLeft);
                t.fontSize = size;
                t.color = Cream.WithAlpha(alpha);
                return t;
            }

            /// <summary>Who opened the files and when, to the second.</summary>
            public void Refresh(float now)
            {
                int s = Mathf.Max(0, (int)now);
                string line = "OPENED BY: [YOU] · LOGGED T+" + (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
                if (opened.text != line) opened.text = line;
            }

            public override float Measure(float width) => Height;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float w = s.W;
                AvLay.Place(paper.rectTransform, 0f, 0f, w, Height);
                for (int i = 0; i < grain.Length; i++) AvLay.Place(grain[i].rectTransform, 0f, 6f + i * 13f + Roll(i, 31) % 5, w, 2f + Roll(i, 37) % 4);
                AvLay.Place(rule.rectTransform, 4f, 4f, w - 8f, Height - 16f);
                // The tear line: small square holes along the foot.
                for (int i = 0; i < perforation.Length; i++) AvLay.Place(perforation[i].rectTransform, 6f + i * (w - 12f) / perforation.Length, Height - 7f, 4f, 4f);
                EvidenceCard.Turn(stamp, Pad, 14f, StampW, 38f, -8f);
                float tx = Pad + StampW + 16f, tw = w - tx - Pad;
                AvLay.Place(routing, tx, 8f, tw, 18f);
                AvLay.Place(opened, tx, 26f, tw, 18f);
                AvLay.Place(warning, tx, 44f, tw, 18f);
                AvLay.Place(slipNo, w - 90f, Height - 28f, 80f, 12f);
            }
        }

        /// <summary>Deterministic noise for an integer slot: the same slot always rolls the same.</summary>
        private static int Roll(int slot, int salt) => (int)(((uint)slot * 2654435761u ^ (uint)salt * 40503u) >> 8) & 0xffff;
    }
}
