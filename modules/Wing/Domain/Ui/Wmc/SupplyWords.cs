using System;
using System.Globalization;
using static BoscaliSummer.Modules.Wing.Domain.WmcText;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>SUPPLY's page words (spec WMC rebuild §SUPPLY): tile code and name, step chips, the FIT choice, REQUISITION, the
    /// funds-after line and the strip hint — each capped at the source, never with "…". The quote, blocker, dispatch
    /// line and tile foot are <see cref="ShopRules"/>' words.</summary>
    internal static class SupplyWords
    {
        public const int CodeChars = 12, NameChars = 20;

        public const string PilotTitle = "PILOT & CREW", AirframeTitle = "AIRFRAME", FitTitle = "FIT & FUEL", BaseTitle = "LAUNCH BASE";

        /// <summary>Step 2 with nothing listed (one inert card, never a blank grid).</summary>
        public const string NotFlyingTiles = "NOT FLYING · take off to see what your faction offers.";
        public const string NoneOffered =
            "NO AIRFRAME OFFERED · your faction's supply lists none here (Squadron/SandboxFreeCalls lists every one).";

        /// <summary>Step 4 with no field in reach.</summary>
        public const string NotFlyingBases = "Not flying: take off to see the fields in reach.";
        public const string NoField = "No friendly or unowned field in reach to launch from.";

        /// <summary>The tile's code line: the game's code, else the name's first word.</summary>
        public static string Code(string code, string unitName)
        {
            string c = !string.IsNullOrEmpty(code) ? code : unitName ?? "";
            int space = c.IndexOf(' ');
            return WmcText.Cut(string.IsNullOrEmpty(code) && space > 0 ? c.Substring(0, space) : c, CodeChars);
        }

        /// <summary>The tile's name line: the unit name without its code ("FS-20 Vortex" → "Vortex").</summary>
        public static string Name(string unitName, string code)
        {
            if (string.IsNullOrEmpty(unitName)) return "";
            string s = unitName;
            if (!string.IsNullOrEmpty(code) && s.Length > code.Length + 1 && s[code.Length] == ' '
                && s.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                s = s.Substring(code.Length + 1).TrimStart();
            return WmcText.Cut(s, NameChars);
        }

        public static string BaseChip(int on, int total) =>
            total <= 0 ? "NO FIELD" : on <= 0 ? "ALL OFF" : on == 1 ? "1 BASE ON" : N(on) + " BASES ON";

        /// <summary>The fit's word: AUTO (the game's own pick, the default), YOUR LOADOUT, or CUSTOM (the airframe's LOADOUT build).</summary>
        public static string Fit(string fit) => fit == null ? "AUTO" : fit == CallSpec.YourLoadout ? "YOUR LOADOUT" : "CUSTOM";

        public const string EditLabel = "EDIT ›";

        public static string FitDetail(string fit)
        {
            if (fit == CallSpec.YourLoadout) return "YOUR LOADOUT: the loadout you set for this airframe, as you would fly it.";
            if (fit != null) return "CUSTOM: the loadout built for this airframe on LOADOUT.";
            return "AUTO: the game arms it for the mission, as it arms its own AI.";
        }

        /// <summary>FUEL's four steps, as the chips list them (the order SUPPLY has always cycled through).</summary>
        public static readonly int[] FuelSteps = { 25, 50, 75, 100 };

        public static string FuelChip(int percent) => N(percent) + (percent == 100 ? " %" : "");

        public static int FuelIndex(int percent)
        {
            for (int i = 0; i < FuelSteps.Length; i++)
                if (FuelSteps[i] == percent) return i;
            return FuelSteps.Length - 1;
        }

        /// <summary>The airframe list's caption: how many are listed and which page ("6 LISTED · PAGE 1 / 2").</summary>
        public static string AirframeCaption(int listed, int page, int pages) =>
            listed <= 0 ? "NONE LISTED" : N(listed) + " LISTED" + (pages > 1 ? " · PAGE " + N(page + 1) + " / " + N(pages) : "");

        /// <summary>The pilot step's caption: where the next pilot sits in the free roster.</summary>
        public static string PilotCaption(int index, int free) => free <= 0 ? "A NEW PILOT IS DRAFTED" : "NEXT FROM ROSTER · " + N(index + 1) + " / " + N(free);

        /// <summary>The dispatch card's figures beside REQUISITION: funds after the call and what stock is left (the price is in the title
        /// and on the button, the crew in the header).</summary>
        public static string FundsAfter(bool sandbox, float price, float funds, int stock) =>
            sandbox ? "SANDBOX · FREE CALLS" : "FUNDS AFTER " + Credits.Text(funds - price) + (stock > 0 ? " · " + N(stock) + " IN STOCK" : " · NONE IN STOCK");

        public static string FieldDistance(float km) => N((int)Math.Round(km)) + " KM";

        /// <summary>The dispatch card's caution when the launch field's runway is shorter than the airframe needs at the chosen fuel (null:
        /// none; <see cref="TakeoffRuns"/>). Less fuel is only advice while a lower step exists.</summary>
        public static string ShortRunway(string unitName, string code, float runwayMetres, int fuelPercent) =>
            TakeoffRuns.Short(unitName, runwayMetres, fuelPercent)
                ? "SHORT RUNWAY · " + N((int)Math.Round(runwayMetres)) + " M HERE · "
                  + (fuelPercent >= 100 ? "A FULLY FUELLED " + code : code + " AT " + N(fuelPercent) + "% FUEL") + " NEEDS ABOUT "
                  + (TakeoffRuns.NeedMetres(unitName, fuelPercent) / 1000f).ToString("0.0", CultureInfo.InvariantCulture)
                  + " KM AND MAY NOT GET AIRBORNE · " + (fuelPercent > FuelSteps[0] ? "LESS FUEL OR " : "") + "A LONGER FIELD"
                : null;

        /// <summary>An INBOUND strip's progress, 0..1: queued, spawning, taxiing, departing, then joining.</summary>
        public static float InboundProgress(InboundPhase p) => 0.1f + 0.2f * (int)p;

        public static string Requisition(float price) => "REQUISITION · " + Credits.Price(price);

        /// <summary>The footer's line: only what the card does not say (null when idle: the guide has the how-to).</summary>
        public static string Hint(bool client, int inbound)
        {
            if (client) return "The host runs SUPPLY; you can look, the host requisitions.";
            return inbound > 0 ? N(inbound) + " inbound · they join as they take off" : null;
        }
    }
}
