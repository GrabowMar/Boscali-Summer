using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Spooktober.Domain;
using BoscaliSummer.Modules.Spooktober.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Presentation
{
    /// <summary>
    /// The SPOOKTOBER page (a MISC tab, shown only once the seal is whole): a strip of three instruments (seals, chain, what is on the
    /// field), then one dossier card per boss in chain order. An open card spawns and dismisses its boss (host only; everyone else reads
    /// HOST ONLY), a locked one is static noise that names what breaks its seal, the last is a placeholder that never opens.
    /// The page haunts itself on the Slow tick, cosmetic only: the caption sometimes says something worse, an open card's name flickers,
    /// the THREAT dials breathe and, now and then, FIELD reads BEHIND YOU for a blink. All of it is a pure function of the clock.
    /// </summary>
    internal static class SpookMenuPage
    {
        private static readonly Color Ember = new Color32(255, 138, 61, 255), Sick = new Color32(143, 209, 79, 255), Dead = new Color32(96, 122, 70, 255);
        private const int Beatable = 2;
        private const string Calm = "ONE AT A TIME";
        private static readonly string[] Captions = { "IT HEARS YOUR RADIO", "DO NOT LOOK BACK", "COUNT YOUR WINGMEN", "31.10 IS CLOSER" };

        /// <summary>Deterministic noise for a time slot: the same slot always rolls the same, so a flicker lasts exactly one slot.</summary>
        private static int Roll(int slot, int salt) => (int)(((uint)slot * 2654435761u ^ (uint)salt * 40503u) >> 8) & 0xffff;

        // The console font has no block glyphs (they print as empty boxes in game): the glitch wears ASCII noise here.
        private static string Printable(string glitched) =>
            glitched.Replace('\u2588', '#').Replace('\u2593', '#').Replace('\u2592', '%').Replace('\u2591', ':');

        public static void Build(int page, AvFlow flow, AvConsole av, SpookConsole console, Func<bool> kaijuAvailable)
        {
            AvSection section = flow.Section(AvIcon.Skull, "BOSS CHAIN", Calm);
            var seals = new AvGauge(flow.Content, "SEALS") { Help = "Puzzles solved in SET. Four break the seal." };
            var chain = new AvGauge(flow.Content, "CHAIN") { Help = "Bosses beaten in order. Each defeat opens the next card." };
            var field = new AvGauge(flow.Content, "FIELD") { Help = "The boss out there now. Only one at a time." };
            flow.Row(seals, chain, field);

            var cards = new[]
            {
                new Card(flow, console, SpookBoss.Kaiju, "I", "KAIJU", "Rises ahead of your aircraft. Big, slow to kill.",
                    "", "SONAR 03:13 \u00B7 DEPTH UNKNOWN \u00B7 HEARTBEAT 4 BPM", .7f, "HIGH", kaijuAvailable),
                new Card(flow, console, SpookBoss.FallenAce, "II", "FALLEN ACE", "Gun only. Hostile to everyone. Three lives.",
                    "Bring down the KAIJU to break this seal.", "STRUCK FROM THE ROSTER 1987 \u00B7 STILL LOGGING SORTIES",
                    .9f, "EXTREME", kaijuAvailable),
                new Card(flow, console, SpookBoss.Redacted, "III", SpookMenu.RedactedTitle, "",
                    "Do not ask what it is. Do not answer it.", "FILE MISSING \u00B7 LAST EDIT 31.10 \u00B7 BY: YOU",
                    1f, "###", kaijuAvailable)
            };
            void Refresh()
            {
                float now = SpookConsole.Clock();
                int beat = (int)(now * 2f), slot = (int)(now / 6f);
                section.SetCaption(Roll(slot, 1) % 4 == 0 ? Captions[Roll(slot, 2) % Captions.Length] : Calm);
                SpookProgress progress = SpookStore.Current;
                seals.Set(progress.SolvedCount / 4f, progress.SolvedCount + "/4", progress.Unlocked ? AvState.Ready : AvState.Caution);
                int beaten = Mathf.Min(progress.ChainLevel, Beatable);
                chain.Set(beaten / (float)Beatable, beaten + "/" + Beatable, beaten == Beatable ? AvState.Ready : AvState.Info);
                SpookBoss? active = SpookBosses.Active;
                bool behind = !active.HasValue && Roll(beat, 9) % 90 == 0;
                field.Set(active.HasValue || behind ? 1f : 0f,
                    active == SpookBoss.Kaiju ? "KAIJU" : active == SpookBoss.FallenAce ? "ACE" : behind ? "BEHIND YOU" : "CLEAR",
                    active.HasValue || behind ? AvState.Danger : AvState.Inert);
                for (int i = 0; i < cards.Length; i++) cards[i].Refresh(now, Roll(beat, 3 + i) % 15 == 0);
            }
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
            console.RegisterMessage(page, flow, menu: true);
            av.Orders(page,
                "One boss on the field at a time. A new spawn replaces it.",
                "Only the host spawns. Every pilot's chain still counts.",
                "DISMISS sends a boss away. That is not a defeat.",
                "If the radio says your name, do not answer.");
        }

        private sealed class Card
        {
            private readonly SpookConsole console;
            private readonly SpookBoss boss;
            private readonly string index, name, blurbText, sealText, dossierText, threatText;
            private readonly float threatLevel;
            private readonly Func<bool> kaijuAvailable;
            private readonly AvNote title, blurb, dossier;
            private readonly AvGauge threat, status;
            private readonly AvCard card;
            private readonly AvButtons buttons;
            private readonly AvControl spawn, dismiss;

            public Card(AvFlow flow, SpookConsole console, SpookBoss boss, string index, string name, string blurbText, string sealText,
                string dossierText, float threatLevel, string threatText, Func<bool> kaijuAvailable)
            {
                this.console = console;
                this.boss = boss;
                this.index = index;
                this.name = name;
                this.blurbText = blurbText;
                this.sealText = sealText;
                this.dossierText = dossierText;
                this.threatLevel = threatLevel;
                this.threatText = threatText;
                this.kaijuAvailable = kaijuAvailable;
                card = new AvCard(flow.Content, flow.Ticker, flow.Inner, null, true, "raised");
                title = card.Flow.Add(new AvNote(card.Flow.Content, AvTextRole.Head) { MinHeight = 16f });
                blurb = card.Flow.Add(new AvNote(card.Flow.Content, AvTextRole.ProseSmall) { MinHeight = 14f });
                dossier = card.Flow.Add(new AvNote(card.Flow.Content, AvTextRole.Micro) { MinHeight = 12f });
                threat = new AvGauge(card.Flow.Content, "THREAT");
                status = new AvGauge(card.Flow.Content, "STATUS");
                card.Flow.Row(threat, status);
                buttons = card.Flow.Buttons(
                    new AvControl.Spec("SPAWN", Spawn, AvButtonStyle.Primary, AvIcon.Skull),
                    new AvControl.Spec("DISMISS", Dismiss, AvButtonStyle.Quiet, AvIcon.X));
                spawn = buttons.Controls[0];
                dismiss = buttons.Controls[1];
                dismiss.Help = "Send the boss away. That is not a defeat.";
                flow.Add(card);
            }

            private void Spawn()
            {
                console.Say(SpookBosses.Spawn(boss), 3f);
                Refresh(SpookConsole.Clock(), false);
            }

            private void Dismiss()
            {
                console.Say(SpookBosses.Dismiss(), 3f);
                Refresh(SpookConsole.Clock(), false);
            }

            /// <param name="flicker">This slot the name glitches (open cards only; the placeholder flickers on its own rhythm).</param>
            public void Refresh(float now, bool flicker)
            {
                SpookProgress progress = SpookStore.Current;
                bool open = progress.BossOpen(boss);
                bool blocked = SpookMenu.SpawnBlocked(GameAccess.IsServer());
                bool available = boss != SpookBoss.Kaiju || kaijuAvailable();
                bool onField = SpookBosses.Active == boss;
                bool beaten = boss != SpookBoss.Redacted && progress.ChainLevel > (int)boss;

                string shown = boss == SpookBoss.Redacted ? name
                    : !open ? Printable(SpookText.Glitch(name, 7, 0.6f))
                    : boss == SpookBoss.Kaiju ? SpookMenu.KaijuCard(available) : name;
                if (open && flicker || boss == SpookBoss.Redacted && Roll((int)(now * 2f), 11) % 5 == 0)
                    shown = Printable(SpookText.Glitch(shown, (int)now, 0.45f));
                title.Set(index + " · " + shown);
                title.Color = open ? Ember : Dead;

                blurb.Set(open ? blurbText : sealText);
                blurb.Color = open ? Sick : Dead;
                dossier.Set(open ? dossierText : Printable(SpookText.Glitch(dossierText, 13, 0.75f)));
                dossier.Color = Dead;

                // The dial breathes: never quite still, never quite the same twice.
                threat.Set(open ? Mathf.Clamp01(threatLevel + 0.06f * Mathf.Sin(now * 2.3f + (int)boss * 1.7f)) : 0f, open ? threatText : "???", open ? AvState.Danger : AvState.Inert);
                if (boss == SpookBoss.Redacted) status.Set(0f, "31.10", AvState.Inert);
                else if (!open) status.Set(0f, "SEALED", AvState.Inert);
                else if (onField) status.Set(1f, "ON FIELD", AvState.Danger);
                else if (!available) status.Set(0f, "OFFLINE", AvState.Caution);
                else if (beaten) status.Set(1f, "DEFEATED", AvState.Ready);
                else status.Set(.5f, "READY", AvState.Info);

                if (buttons.Shown != open)
                {
                    buttons.SetShown(open);
                    card.Changed(); // the page re-measures the card now, not on its half-second sweep
                }
                spawn.Label = blocked ? "HOST ONLY" : "SPAWN";
                spawn.Interactable = !blocked && available;
                spawn.Help = blocked ? "Only the host can spawn bosses." : "Spawn it ahead of your aircraft. Replaces any boss on the field.";
                dismiss.Interactable = !blocked && onField;
            }
        }
    }
}
