using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Spooktober.Domain;
using BoscaliSummer.Modules.Spooktober.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Presentation
{
    /// <summary>
    /// The SPOOKTOBER page (the sixth PILOT tab, shown only once the seal is whole): three boss cards in chain order. An open card spawns and
    /// dismisses its boss (host only; everyone else reads HOST ONLY), a locked one is static noise, the last is a placeholder that never opens.
    /// </summary>
    internal static class SpookMenuPage
    {
        private static readonly Color Ember = new Color32(255, 138, 61, 255), Sick = new Color32(143, 209, 79, 255), Dead = new Color32(96, 122, 70, 255);

        public static void Build(int page, AvFlow flow, SpookConsole console, Func<bool> kaijuAvailable)
        {
            flow.Section(AvIcon.Skull, "BOSS CHAIN", "ONE AT A TIME");
            var cards = new[]
            {
                new Card(flow, console, SpookBoss.Kaiju, "KAIJU", "Rises ahead of your aircraft. Big, slow to kill.", kaijuAvailable),
                new Card(flow, console, SpookBoss.FallenAce, "FALLEN ACE", "Gun only. Hostile to everyone. Three lives.", kaijuAvailable),
                new Card(flow, console, SpookBoss.Redacted, SpookMenu.RedactedTitle, "", kaijuAvailable)
            };
            void Refresh() { foreach (Card c in cards) c.Refresh(); }
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
        }

        private sealed class Card
        {
            private readonly SpookConsole console;
            private readonly SpookBoss boss;
            private readonly string name, blurbText;
            private readonly Func<bool> kaijuAvailable;
            private readonly AvNote title, blurb;
            private readonly AvButtons buttons;
            private readonly AvControl spawn, dismiss;

            public Card(AvFlow flow, SpookConsole console, SpookBoss boss, string name, string blurbText, Func<bool> kaijuAvailable)
            {
                this.console = console;
                this.boss = boss;
                this.name = name;
                this.blurbText = blurbText;
                this.kaijuAvailable = kaijuAvailable;
                var card = new AvCard(flow.Content, flow.Ticker, flow.Inner, null, true, "raised");
                title = card.Flow.Add(new AvNote(card.Flow.Content, AvTextRole.Head) { MinHeight = 16f });
                blurb = card.Flow.Add(new AvNote(card.Flow.Content, AvTextRole.ProseSmall) { MinHeight = 14f });
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
                Refresh();
            }

            private void Dismiss()
            {
                console.Say(SpookBosses.Dismiss(), 3f);
                Refresh();
            }

            public void Refresh()
            {
                bool open = SpookStore.Current.BossOpen(boss);
                bool blocked = SpookMenu.SpawnBlocked(GameAccess.IsServer());
                bool available = boss != SpookBoss.Kaiju || kaijuAvailable();
                bool onField = SpookBosses.Active == boss;

                if (boss == SpookBoss.Redacted) title.Set(name);
                else if (!open) title.Set(SpookText.Glitch(name, 7, 0.6f));
                else title.Set(boss == SpookBoss.Kaiju ? SpookMenu.KaijuCard(available) : name);
                title.Color = open ? Ember : Dead;

                blurb.Set(!open ? "LOCKED" : onField ? "ON THE FIELD" : blurbText);
                blurb.Color = !open ? Dead : onField ? Ember : Sick;

                buttons.SetShown(open);
                spawn.Label = blocked ? "HOST ONLY" : "SPAWN";
                spawn.Interactable = !blocked && available;
                spawn.Help = blocked ? "Only the host can spawn bosses." : "Spawn it ahead of your aircraft. Replaces any boss on the field.";
                dismiss.Interactable = !blocked && onField;
            }
        }
    }
}
