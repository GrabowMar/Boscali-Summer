using System;
using BepInEx.Configuration;
using BoscaliSummer.Core.Config;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Ares.Domain;
using BoscaliSummer.Modules.Ares.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Ares.Presentation
{
    /// <summary>
    /// ARES's three MISC tabs, kit parts only. Every button calls the matching <see cref="AresHost"/> method; the host's answer shows in
    /// the ARES line at the end of each page, and the map pick in progress latches its button.
    /// </summary>
    internal sealed class AresTabs : IMiscTabs
    {
        private const int Spawn = 0, Powers = 1, Cheats = 2;

        private static readonly string[] Labels = { "SPAWN", "POWERS", "CHEATS" };
        private static readonly string[] Titles = { "ARES · SPAWN", "ARES · POWERS", "ARES · CHEATS" };
        private static readonly AvIcon[] Icons = { AvIcon.MapPin, AvIcon.Bolt, AvIcon.Star };
        private static readonly string[] Helps =
        {
            "Spawn aircraft, ground units, ships, buildings or whole kits on map clicks, for any side.",
            "Strikes anywhere, the weather console, the clock, free OPS casts, fire and levelling.",
            "God mode, infinite fuel and ammo, boost, low gravity, reveal all, time stop and more. This session only."
        };

        private readonly AresHost host;
        private readonly ModConfiguration settings;

        public AresTabs(AresHost host, ModConfiguration settings)
        {
            this.host = host;
            this.settings = settings;
        }

        public int Count => Labels.Length;
        public AvIcon Icon(int tab) => Icons[tab];
        public string Label(int tab) => Labels[tab];
        public string Title(int tab) => Titles[tab];
        public string Help(int tab) => Helps[tab];

        public void Build(int tab, int page, AvFlow flow, AvConsole console)
        {
            switch (tab)
            {
                case Spawn: BuildSpawn(page, flow, console); break;
                case Powers: BuildPowers(page, flow, console); break;
                case Cheats: BuildCheats(page, flow, console); break;
            }
            AvNote line = flow.Add(new AvNote(flow.Content, AvTextRole.ProseSmall) { MinHeight = 14f });
            flow.Ticker.Add(page, AvTickRate.Slow, () => line.Set(host.Result, AvState.Info));
        }

        private static AvControl.Spec Button(string label, Action press, AvIcon icon, AvButtonStyle style = AvButtonStyle.Default) =>
            new AvControl.Spec(label, press, style, icon);

        /// <summary>A button that arms a map pick: it glows amber and reads ARMED while that pick waits for its click.</summary>
        private void PickButton(AvFlow flow, int page, AvControl control, string pick)
        {
            control.Help = "Then click the map. Esc cancels.";
            string idle = control.Label;
            flow.Ticker.Add(page, AvTickRate.Fast, () =>
            {
                bool on = host.Armed && host.PickLabel == pick;
                control.Armed = on;
                control.Label = on ? "ARMED · CLICK MAP" : idle;
            });
        }

        /// <summary>Arms the pick, or cancels it when it is already waiting.</summary>
        private Action Pick(string pick, Func<string> arm) => () =>
        {
            if (host.Armed && host.PickLabel == pick) host.Disarm();
            else arm();
        };

        // ------------------------------------------------------------------ SPAWN

        private void BuildSpawn(int page, AvFlow flow, AvConsole console)
        {
            flow.Section(AvIcon.MapPin, "SPAWN", "HOST ONLY");
            AvSegmented side = flow.Add(AvSegmented.Strip(flow.Content, AresHost.SideLabels, () => host.Side, v => host.Side = v));
            AvList list = null;
            AvSegmented kind = flow.Add(AvSegmented.Strip(flow.Content, new[] { "AIR", "GROUND", "NAVAL", "STRUCT", "KITS" },
                () => (int)host.Kind, v =>
                {
                    host.Kind = (AresKind)v;
                    host.Item = 0;
                    list.Reveal(0);
                    list.SetCount(host.ItemCount);
                }));
            list = flow.Add(new AvList(flow.Content, flow.Ticker, 7, (i, row) =>
            {
                bool chosen = i == host.Item;
                row.Set(host.ItemName(i), host.ItemDetail(i), chosen ? "SELECTED" : "", chosen ? AvState.Ready : AvState.Inert);
                row.Armed = chosen;
            }));
            list.RowClicked = i =>
            {
                host.Item = i;
                list.SetCount(host.ItemCount);
            };
            var count = new AvStepper(flow.Content, "COUNT", () => host.Count.ToString(), () => host.Count--, () => host.Count++);
            var altitude = new AvStepper(flow.Content, "ALT (AIR)", () => Mathf.RoundToInt(host.Altitude) + " M",
                () => host.Altitude -= host.Altitude > 3000f ? 1000f : 300f, () => host.Altitude += host.Altitude >= 3000f ? 1000f : 300f);
            flow.Row(count, altitude);
            AvButtons actions = flow.Buttons(
                Button("PLACE", () => host.TogglePlace(), AvIcon.MapPin, AvButtonStyle.Primary),
                Button("UNDO", () => host.Undo(), AvIcon.ArrowBackUp),
                Button("CLEAR ALL", () => host.ClearAll(), AvIcon.Trash, AvButtonStyle.Quiet));
            actions.Controls[0].Help = "Arms the map: every click spawns the selection, a drag sets its heading. Esc or PLACE again stops.";
            actions.Controls[1].Help = "Removes the last thing you placed.";
            actions.Controls[2].Help = "Removes everything ARES spawned this mission.";
            AvNote tally = flow.Add(new AvNote(flow.Content, AvTextRole.Micro) { MinHeight = 12f });

            int shownKind = -1;
            void Refresh()
            {
                side.Refresh();
                kind.Refresh();
                if (shownKind != (int)host.Kind)
                {
                    shownKind = (int)host.Kind;
                    list.SetCount(host.ItemCount);
                }
                for (int i = 0; i < AresHost.SideLabels.Length; i++) side.Options[i].Interactable = AresHost.SideHq(i) != null;
                count.Refresh();
                altitude.Refresh();
                actions.Controls[0].Latched = host.Placing;
                actions.Controls[0].Label = host.Placing ? "STOP" : "PLACE";
                tally.Set(host.Spawned + " ARES UNIT(S) OUT · " + AresHost.SideName(host.Side) + " · " + host.ItemName(host.Item), AvState.Inert);
            }
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
            console.Orders(page,
                "Pick a side and a unit, then PLACE and click the map.",
                "Press, drag and release to face what you place.",
                "UNDO takes back the last placement; CLEAR ALL takes back every one.");
        }

        // ------------------------------------------------------------------ POWERS

        private void BuildPowers(int page, AvFlow flow, AvConsole console)
        {
            AvSection strikeHead = flow.Section(AvIcon.Bolt, "STRIKE", "FOR THE SPAWN SIDE");
            var munition = new AvStepper(flow.Content, "MUNITION", () => host.MunitionName, () => host.Munition--, () => host.Munition++);
            flow.Add(munition);
            AvSegmented pattern = flow.Add(AvSegmented.Strip(flow.Content, AresHost.Patterns, () => host.Pattern, v => host.Pattern = v));
            AvButtons strike = flow.Buttons(Button("STRIKE", Pick("STRIKE", host.ArmStrike), AvIcon.Target));
            PickButton(flow, page, strike.Controls[0], "STRIKE");
            strike.Controls[0].Help = "Arms the map: the next click is the target. Press again or Esc to cancel.";
            AvNote brief = flow.Add(new AvNote(flow.Content, AvTextRole.Micro) { MinHeight = 12f });
            flow.Ticker.Add(page, AvTickRate.Fast, () =>
            {
                bool armed = host.Armed && host.PickLabel == "STRIKE";
                strikeHead.SetCaption(armed ? "ARMED · ESC CANCELS" : "FOR THE SPAWN SIDE");
                brief.Set((armed ? "TARGETING · " : "") + host.MunitionName + " · " + PatternBrief[host.Pattern], armed ? AvState.Caution : AvState.Inert);
            });

            flow.Section(AvIcon.CloudRain, "WEATHER", "CTRL+O");
            AvButtons weather = flow.Buttons(Button("OPEN WEATHER CONSOLE", () => host.OpenWeatherConsole(), AvIcon.CloudRain));
            weather.Controls[0].Help = "The Met Office: hold states, set-pieces, scenarios, back to live.";

            flow.Section(AvIcon.Clock, "TIME", "HOST");
            var hour = new AvStepper(flow.Content, "HOUR", () => AresMath.Hour(host.Hour), () => host.SetHour(host.Hour - 1f), () => host.SetHour(host.Hour + 1f));
            var speed = new AvStepper(flow.Content, "SPEED (SP)", () => "x" + AresHost.Speeds[host.SpeedIndex].ToString("0.##"),
                () => host.SetSpeed(host.SpeedIndex - 1), () => host.SetSpeed(host.SpeedIndex + 1));
            flow.Row(hour, speed);

            flow.Section(AvIcon.CallDown, "OPS", "FREE CAST");
            AvSegmented ops = flow.Add(AvSegmented.Strip(flow.Content, AresHost.OpsNames, () => host.OpsAction, v => host.OpsAction = v));
            AvButtons cast = flow.Buttons(Button("CAST", Pick("CAST", host.ArmCast), AvIcon.CallDown));
            PickButton(flow, page, cast.Controls[0], "CAST");
            cast.Controls[0].Help = "The OPS action at a map click, as your side. Switches BYPASS REQS and NO OPS COOLDOWN on.";

            flow.Section(AvIcon.Flame, "GROUND", "AT A CLICK");
            AvButtons ground = flow.Buttons(
                Button("FIRE", Pick("FIRE", host.ArmFire), AvIcon.Flame),
                Button("LEVEL 200 M", Pick("LEVEL", host.ArmLevel), AvIcon.Building));
            PickButton(flow, page, ground.Controls[0], "FIRE");
            PickButton(flow, page, ground.Controls[1], "LEVEL");

            void Refresh()
            {
                munition.Refresh();
                pattern.Refresh();
                hour.Refresh();
                speed.Refresh();
                ops.Refresh();
            }
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
            console.Orders(page,
                "Strikes fly for the side picked on SPAWN.",
                "An armed button glows amber until the map click lands.",
                "Weather lives in the Ctrl+O console; the clock moves the sun now.");
        }

        private static readonly string[] PatternBrief = { "ONE ROUND FROM 1.5 KM", "8 ROUNDS OVER 250 M", "ONE ROUND FROM 12 KM OUT" };

        // ------------------------------------------------------------------ CHEATS

        private void BuildCheats(int page, AvFlow flow, AvConsole console)
        {
            flow.Section(AvIcon.Star, "CHEATS", "TESTING AIDS");
            AvCellGrid grid = flow.Grid(2);
            ConfigToggle(flow, grid, page, "BYPASS REQS", settings.Diagnostics.BypassRequirements);
            ConfigToggle(flow, grid, page, "NO OPS COOLDOWN", settings.Diagnostics.DisableOpsCooldowns);

            flow.Section(AvIcon.Pilot, "PILOT", "YOUR AIRCRAFT");
            AvCellGrid pilot = flow.Grid(2);
            Toggle(flow, pilot, page, "GOD MODE", "Your aircraft and you take no damage of any kind.", () => AresCheats.God, v => AresCheats.God = v);
            Toggle(flow, pilot, page, "INFINITE FUEL", "Your engines burn nothing.", () => AresCheats.Fuel, v => AresCheats.Fuel = v);
            Toggle(flow, pilot, page, "INFINITE AMMO", "Every station refills twice a second.", () => AresCheats.Ammo, v => AresCheats.Ammo = v);
            Toggle(flow, pilot, page, "INFINITE FLARES", "Flares and chaff refill twice a second.", () => AresCheats.Flares, v => AresCheats.Flares = v);
            Toggle(flow, pilot, page, "NO BLACKOUT", "G-LOC never dims or takes the stick.", () => AresCheats.NoBlackout, v => AresCheats.NoBlackout = v);
            Toggle(flow, pilot, page, "ONE-HIT KILLS", "What you hit takes fifty times the damage.", () => AresCheats.OneHit, v => AresCheats.OneHit = v);
            Toggle(flow, pilot, page, "BOOST x2", "As much thrust again as your engines make.", () => AresCheats.Boost, v => AresCheats.Boost = v);
            Toggle(flow, pilot, page, "LOW GRAVITY", "Everything with physics on this machine falls at 30 %.", () => AresCheats.LowGravity, v => host.SetLowGravity(v));

            flow.Section(AvIcon.World, "WORLD", "EVERYONE");
            AvCellGrid world = flow.Grid(2);
            Toggle(flow, world, page, "REVEAL ALL", "Every enemy unit is tracked by your side and on your map.", () => AresCheats.Reveal, v => AresCheats.Reveal = v);
            Toggle(flow, world, page, "TIME STOP", "Every unit but your aircraft freezes where it is.", () => AresCheats.TimeStop, v => host.SetTimeStop(v));
            Toggle(flow, world, page, "BULLET TIME", "The whole game at 35 % speed (single player).", () => AresCheats.BulletTime, v => host.SetBulletTime(v));
            AvButtons quick = flow.Buttons(
                Button("FIX ME", () => host.FixMe(), AvIcon.Refresh, AvButtonStyle.Primary),
                Button("TELEPORT ME", Pick("TELEPORT ME", host.ArmTeleportMe), AvIcon.ArrowUpRight),
                Button("+1000 CREDIT", () => host.AddCredit(), AvIcon.Coins));
            PickButton(flow, page, quick.Controls[1], "TELEPORT ME");
            quick.Controls[0].Help = "Repairs, rearms and refuels your aircraft now.";

            BuildHuntingWing(page, flow);
            console.Orders(page,
                "Testing aids, never in a public build.",
                "On a server only the host's BYPASS and COOLDOWN count.",
                "Cheat switches last until the game closes.");
        }

        private void BuildHuntingWing(int page, AvFlow flow)
        {
            var squad = settings.Squad;
            flow.Section(AvIcon.Skull, "HUNTING WING", "HOST ONLY");
            ConfigEntry<int> tier = squad.DebugWingTier;
            var tierRow = new AvRow(flow.Content);
            AvControl minus = tierRow.AddTrailing(new AvControl.Spec("", () => tier.Value = Mathf.Max(1, tier.Value - 1), AvButtonStyle.Quiet, AvIcon.Minus));
            AvControl plus = tierRow.AddTrailing(new AvControl.Spec("", () => tier.Value = Mathf.Min(5, tier.Value + 1), AvButtonStyle.Quiet, AvIcon.Plus));
            tierRow.Help = tier.Description.Description;
            flow.Add(tierRow);
            var result = new AvNote(flow.Content, "Host only. Fly an aircraft in a running mission.");
            AvButtons actions = flow.Buttons(
                new AvControl.Spec("SPAWN", () => result.Set(squad.SpawnDebugWing?.Invoke(tier.Value) ?? "", AvState.Info), AvButtonStyle.Primary, AvIcon.Skull),
                new AvControl.Spec("CLEAR MINE", () => result.Set(squad.ClearDebugWings?.Invoke() ?? "", AvState.Info), AvButtonStyle.Quiet, AvIcon.Trash));
            flow.Add(result);
            void Refresh()
            {
                tierRow.Set("TIER", tier.Value <= 2 ? "Two aircraft" : tier.Value <= 4 ? "Three aircraft" : "Four aircraft", tier.Value + "/5", AvState.Info);
                minus.Interactable = tier.Value > 1;
                plus.Interactable = tier.Value < 5;
                bool installed = squad.SpawnDebugWing != null;
                actions.Controls[0].Interactable = installed;
                actions.Controls[1].Interactable = squad.ClearDebugWings != null;
                if (!installed) result.Set("Enable Progression and restart to install Squad.", AvState.Caution);
            }
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
        }

        private static void Toggle(AvFlow flow, AvCellGrid grid, int page, string title, string help, Func<bool> get, Action<bool> set)
        {
            AvCell cell = grid.Toggle(title, "", get, set);
            cell.Help = help;
            flow.Ticker.Add(page, AvTickRate.Slow, cell.Refresh);
        }

        /// <summary>A [Debug] config switch; its own description is the hover help.</summary>
        private static void ConfigToggle(AvFlow flow, AvCellGrid grid, int page, string title, ConfigEntry<bool> entry) =>
            Toggle(flow, grid, page, title, entry.Description.Description, () => entry.Value, v => entry.Value = v);
    }
}
