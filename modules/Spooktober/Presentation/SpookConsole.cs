using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Spooktober.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Presentation
{
    /// <summary>The menu's clock and its own message row. Never writes the console's functional footer.</summary>
    internal sealed class SpookConsole
    {
        /// <summary>Real time. The offline check drives it by hand (the override is not in the public build).</summary>
#if !PUBLIC_RELEASE
        internal static Func<float> Clock = () => Time.unscaledTime;
#else
        internal static float Clock() => Time.unscaledTime;
#endif

        private readonly AvConsole console;
        private readonly Dictionary<int, AvNote> messages = new Dictionary<int, AvNote>();
        private string line;
        private float lineUntil;
        private int messagePage;
        private int menuPage = -1;

        public SpookConsole(AvConsole console)
        {
            this.console = console;
            console.Ticker.Add(-1, AvTickRate.Slow, Tick);
        }

        /// <param name="menu">The boss menu's row: it shows the standing spawn hint between messages.</param>
        public void RegisterMessage(int page, AvFlow flow, bool menu = false)
        {
            messages[page] = flow.Add(new AvNote(flow.Content) { CollapseEmpty = true });
            if (menu) menuPage = page;
            Tick();
        }

        /// <summary>Shows <paramref name="text"/> in this page's Spooktober message row for a few seconds.</summary>
        public void Say(string text, float seconds)
        {
            line = text;
            lineUntil = Clock() + seconds;
            messagePage = console.CurrentPage;
            Tick();
        }

        // Every fourth 7-second slot the menu's hint gives way to one of these.
        private static readonly string[] Whispers =
        {
            "...is someone else on this net?",
            "Your wingman did not answer the last call.",
            "The kaiju was here first.",
            "Static on 131.0. Do not tune it.",
            "It knows which seat you fly from."
        };

        private void Tick()
        {
            float now = Clock();
            int slot = (int)(now / 7f);
            string hint = slot % 4 == 3
                ? Whispers[(slot / 4) % Whispers.Length]
                : SpookMenu.SpawnBlocked(GameAccess.IsServer())
                    ? "Only the host can spawn. Your chain still counts."
                    : "SPAWN drops a boss ahead of you. One at a time.";
            foreach (var message in messages)
                message.Value.Set(message.Key == messagePage && now < lineUntil ? line : message.Key == menuPage ? hint : "", AvState.Info);
        }
    }
}
