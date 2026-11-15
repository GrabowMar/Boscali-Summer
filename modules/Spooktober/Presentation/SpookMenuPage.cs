using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Spooktober.Domain;
using BoscaliSummer.Modules.Spooktober.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Spooktober.Presentation
{
    /// <summary>Three complete case-file images. Live encounter information and controls stay below the paperwork.</summary>
    internal static class SpookMenuPage
    {
        private static readonly string[] Captions = { "EYES ONLY · THREE ENCOUNTERS", "IT HEARS YOUR RADIO", "DO NOT LOOK BACK", "COUNT YOUR WINGMEN" };

        private sealed class Theme
        {
            public SpookBoss Boss;
            public string Name, Out, Idle;
            public AvIcon Icon;
            public Color Accent;
        }

        private static readonly Theme[] Monsters =
        {
            new Theme
            {
                Boss = SpookBoss.Kaiju, Name = "KAIJU", Icon = AvIcon.Activity, Out = "ACTIVE", Idle = "DORMANT",
                Accent = new Color32(224, 186, 108, 255)
            },
            new Theme
            {
                Boss = SpookBoss.FallenAce, Name = "FALLEN ACE", Icon = AvIcon.Plane, Out = "AIRBORNE", Idle = "MISSING",
                Accent = new Color32(137, 205, 226, 255)
            },
            new Theme
            {
                Boss = SpookBoss.HellsArmy, Name = SpookMenu.HellTitle, Icon = AvIcon.Flame, Out = "GATE OPEN", Idle = "SEALED",
                Accent = new Color32(246, 153, 111, 255)
            }
        };

        public static void Build(int page, AvFlow flow, AvConsole av, SpookConsole console, Func<bool> kaijuAvailable)
        {
            AvSection section = flow.Section(AvIcon.Skull, "INCIDENT FILES", Captions[0]);
            AvNote briefing = flow.Add(new AvNote(flow.Content, AvTextRole.Prose, emphasize: true));
            var cards = new EvidenceCard[Monsters.Length];
            for (int i = 0; i < Monsters.Length; i++)
            {
                Theme theme = Monsters[i];
                EvidenceCard card = null;
                cards[i] = card = flow.Add(new EvidenceCard(flow.Content, theme,
                    () =>
                    {
                        SpookAudio.Play(SpookAudio.Stamp);
                        string reply = SpookBosses.Spawn(theme.Boss);
                        card.Say(reply);
                        console.Say(reply, 3f);
                    },
                    () =>
                    {
                        // A slow tick may still show the previous encounter. Never banish a different card's monster.
                        if (SpookBosses.Active != theme.Boss && !(theme.Boss == SpookBoss.Kaiju && SpookBosses.KaijuBodyOut)) return;
                        SpookAudio.Play(SpookAudio.Stamp, .6f);
                        string reply = SpookBosses.Dismiss();
                        card.Say(reply);
                        console.Say(reply, 3f);
                    }));
            }

            float lastSlow = float.NegativeInfinity;
            int whisperedSlot = -1;
            void Slow()
            {
                float now = SpookConsole.Clock();
                bool opened = now - lastSlow > 1.5f;
                if (opened && lastSlow > float.NegativeInfinity) SpookAudio.Menu(SpookAudio.File, flow.Content, .7f);
                lastSlow = now;
                int slot = (int)(now / 6f);
                bool creepy = Roll(slot, 1) % 4 == 0;
                section.SetCaption(creepy ? Captions[1 + Roll(slot, 2) % 3] : Captions[0]);
                if (creepy && slot != whisperedSlot && !opened)
                {
                    whisperedSlot = slot;
                    SpookAudio.Menu(SpookAudio.Whisper, flow.Content, .45f);
                }

                SpookProgress progress = SpookStore.Current;
                SpookBoss? active = SpookBosses.Active;
                bool host = !SpookMenu.SpawnBlocked(GameAccess.IsServer());
                briefing.Set(host
                    ? "Summon ahead of your aircraft. One encounter at a time; a new summon replaces the last."
                    : "Only the host can summon or banish. Your encounter defeats still count.");
                for (int i = 0; i < Monsters.Length; i++)
                {
                    Theme theme = Monsters[i];
                    bool kaiju = theme.Boss == SpookBoss.Kaiju;
                    cards[i].Refresh(active, progress.Beaten(theme.Boss), !kaiju || kaijuAvailable(), host,
                        kaiju && SpookBosses.KaijuBodyOut);
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

        private static readonly Dictionary<SpookBoss, Texture2D> dossiers = new Dictionary<SpookBoss, Texture2D>(3);

        private static Texture2D Dossier(SpookBoss boss)
        {
            if (dossiers.TryGetValue(boss, out Texture2D hit) && (ReferenceEquals(hit, null) || hit != null)) return hit;
            string file = boss == SpookBoss.Kaiju ? "kaiju" : boss == SpookBoss.FallenAce ? "ace" : "hell";
            byte[] data = EmbeddedResources.ReadAll(typeof(SpookMenuPage).Assembly, "BoscaliSummer.Spooktober.Dossiers." + file + ".png", 16 << 20);
            Texture2D texture = null;
            if (data != null && PngSprites.TryLoad(data, 4096, "SpookDossier." + file, out texture, out Sprite sprite))
            {
                texture.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.Destroy(sprite); // the full case file is one RawImage
            }
            return dossiers[boss] = texture;
        }

        /// <summary>The entire dossier is one image. Only the separate live action strip uses UI text and controls.</summary>
        private sealed class EvidenceCard : AvPart
        {
            private const float Pad = 12f, Gap = 8f, StatusHeight = 24f;
            private static readonly Color Muted = new Color32(177, 187, 188, 255);
            private readonly Theme theme;
            private readonly RawImage dossier;
            private readonly Image actionBack;
            private readonly TMP_Text status, record, feedback, unavailable;
            private readonly AvControl spawn, send;
            private float feedbackUntil;

            public EvidenceCard(RectTransform parent, Theme theme, Action summon, Action banish)
            {
                this.theme = theme;
                Rect = AvLay.Child(parent, "Evidence " + theme.Name);
                Texture2D image = Dossier(theme.Boss);
                if (image != null)
                {
                    RectTransform art = AvLay.Child(Rect, "Dossier");
                    dossier = art.gameObject.AddComponent<RawImage>();
                    dossier.texture = image;
                    dossier.raycastTarget = false;
                }

                actionBack = AvLay.Solid(Rect, "LiveActions", new Color32(18, 25, 29, 255));
                unavailable = Text("AssetUnavailable", AvTextRole.Prose,
                    image == null ? theme.Name + " · DOSSIER IMAGE UNAVAILABLE" : "", Muted, 13f, wrap: true);
                unavailable.enabled = image == null;
                status = Text("Status", AvTextRole.DataStrong, "", theme.Accent, 13f, TextAlignmentOptions.MidlineLeft);
                record = Text("Record", AvTextRole.Data, "", Muted, 11f, TextAlignmentOptions.MidlineRight);
                spawn = AvControl.Make(Rect, new AvControl.Spec("SUMMON", summon, AvButtonStyle.Primary, theme.Icon));
                spawn.Accent = theme.Accent;
                send = AvControl.Make(Rect, new AvControl.Spec("BANISH", banish, AvButtonStyle.Quiet, AvIcon.X));
                send.Accent = theme.Accent;
                send.Help = "Sends this encounter away. Banishing does not record a defeat.";
                feedback = Text("Feedback", AvTextRole.Prose, "", theme.Accent, 13f, wrap: true);
            }

            private TMP_Text Text(string name, AvTextRole role, string text, Color color, float size,
                TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool wrap = false)
            {
                TMP_Text t = AvText.Make(Rect, name, role, text, align, wrap);
                t.fontSize = size;
                t.color = color;
                t.raycastTarget = false;
                return t;
            }

            private float ImageHeight(float width) => dossier != null
                ? width * dossier.texture.height / Mathf.Max(1f, dossier.texture.width)
                : width * (2f / 3f);

            private float UnavailableHeight(float width) => unavailable.enabled
                ? AvText.Height(unavailable, width - 2f * Pad) + Gap
                : 0f;

            private float ButtonHeight(float width)
            {
                float w = (width - 2f * Pad - Gap) * .5f;
                return Mathf.Max(38f, Mathf.Max(spawn.PreferredHeight(w), send.PreferredHeight(w)));
            }

            private float FeedbackHeight(float width) => string.IsNullOrEmpty(feedback.text)
                ? 0f
                : Gap + AvText.Height(feedback, width - 2f * Pad);

            public override float Measure(float width) =>
                ImageHeight(width) + Gap + 2f * Pad + UnavailableHeight(width) + StatusHeight + Gap +
                ButtonHeight(width) + FeedbackHeight(width);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float w = s.W, imageHeight = ImageHeight(w), actionTop = imageHeight + Gap, inner = w - 2f * Pad;
                if (dossier != null) AvLay.Place(dossier.rectTransform, 0f, 0f, w, imageHeight);
                AvLay.Place(actionBack.rectTransform, 0f, actionTop, w, s.H - actionTop);
                float y = actionTop + Pad, missing = UnavailableHeight(w);
                AvLay.Place(unavailable, Pad, y, inner, Mathf.Max(0f, missing - Gap));
                y += missing;
                float statusWidth = (inner - Gap) * .48f;
                AvLay.Place(status, Pad, y, statusWidth, StatusHeight);
                AvLay.Place(record, Pad + statusWidth + Gap, y, inner - statusWidth - Gap, StatusHeight);
                y += StatusHeight + Gap;
                float bh = ButtonHeight(w), bw = (inner - Gap) * .5f;
                AvLay.Place(spawn.Rect, Pad, y, bw, bh);
                AvLay.Place(send.Rect, Pad + bw + Gap, y, bw, bh);
                AvLay.Place(feedback, Pad, y + bh + Gap, inner, Mathf.Max(0f, FeedbackHeight(w) - Gap));
            }

            public override void Restyle() { spawn.Restyle(); send.Restyle(); }

            public void Say(string reply)
            {
                AvText.Set(feedback, reply);
                feedbackUntil = SpookConsole.Clock() + 6f;
                Changed();
            }

            public void Refresh(SpookBoss? active, bool beaten, bool available, bool host, bool kaijuBodyOut)
            {
                if (SpookConsole.Clock() >= feedbackUntil && AvText.Set(feedback, "")) Changed();
                bool out_ = active == theme.Boss;
                AvText.Set(status, theme.Name + " · " + (out_ ? theme.Out : !available ? "MODULE OFFLINE" : theme.Idle));
                status.color = out_ ? new Color32(255, 195, 143, 255) : available ? theme.Accent : Muted;
                AvText.Set(record, beaten ? "DEFEAT ON FILE" : "NO DEFEAT RECORDED");
                record.color = beaten ? new Color32(149, 218, 174, 255) : Muted;
                spawn.Label = !host ? "HOST ONLY" : !available ? "MODULE OFFLINE" : out_ ? "SUMMON AGAIN" : active.HasValue ? "REPLACE ACTIVE" : "SUMMON";
                spawn.Interactable = host && available;
                spawn.Help = !host ? "Only the host can summon."
                    : !available ? "Enable the Kaiju module to summon this encounter."
                    : active.HasValue ? "Replaces the active monster and starts this encounter ahead of your aircraft."
                    : "Summons this encounter ahead of your aircraft.";
                send.Interactable = host && (out_ || kaijuBodyOut);
            }
        }
        private static int Roll(int slot, int salt) => (int)(((uint)slot * 2654435761u ^ (uint)salt * 40503u) >> 8) & 0xffff;
    }
}
