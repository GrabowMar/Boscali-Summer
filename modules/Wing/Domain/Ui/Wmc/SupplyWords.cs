using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>SUPPLY's page words (spec WMC rebuild §SUPPLY): tile code and name, step chips, the FIT choice, REQUISITION, the
    /// FUNDS and STOCK captions and the strip hint — each capped at the source, never with "…". The quote, blocker, dispatch
    /// line and tile foot are <see cref="ShopRules"/>' words.</summary>
    internal static class SupplyWords
    {
        public const int ChipChars = 15, CaptionChars = 22, CodeChars = 12, NameChars = 20, FitChars = 16, DispatchChars = 79, RowChars = 70;

        public const string PilotTitle = "PILOT & CREW", AirframeTitle = "AIRFRAME", FitTitle = "FIT & FUEL", BaseTitle = "LAUNCH BASE",
            InboundTitle = "INBOUND";

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

        public static string Fuel(int percent) => "FUEL " + N(percent) + "%";

        public static string BaseChip(int on, int total) =>
            total <= 0 ? "NO FIELD" : on <= 0 ? "ALL OFF" : on == 1 ? "1 BASE ON" : N(on) + " BASES ON";

        /// <summary>The fit's word: AUTO (the game's own pick, the default), YOUR LOADOUT, or a template's name (≤ 16).</summary>
        public static string Fit(string fit, string templateName)
        {
            if (fit == null) return "AUTO";
            if (fit == CallSpec.YourLoadout) return "YOUR LOADOUT";
            return WmcText.Cut(string.IsNullOrEmpty(templateName) ? "TEMPLATE" : templateName.ToUpperInvariant(), FitChars);
        }

        public const string EditLabel = "EDIT ›";

        public static string FitDetail(string fit, bool templates)
        {
            if (fit == CallSpec.YourLoadout) return "YOUR LOADOUT: the loadout you set for this airframe, as you would fly it.";
            if (fit != null) return "A template saved on LOADOUT for this airframe.";
            return "AUTO: the game arms it for the mission, as it arms its own AI." + (templates ? "" : " Templates are made on LOADOUT.");
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

        public static string Crew(int members, int pending, int max) => "CREW " + N(members + pending) + "/" + N(max);

        /// <summary>The dispatch card's next-call line: price, funds after it, crew, and what stock is left.</summary>
        public static string NextCall(bool sandbox, float price, float funds, int members, int pending, int max, int stock)
        {
            string s = sandbox ? "NEXT CALL FREE · SANDBOX" : "NEXT CALL " + Credits.Price(price) + " · FUNDS AFTER " + Credits.Text(funds - price);
            s += " · " + Crew(members, pending, max);
            return sandbox ? s : s + (stock > 0 ? " · STOCK " + N(stock) : " · NONE LEFT");
        }

        public static string FieldDistance(float km) => N((int)Math.Round(km)) + " KM";

        /// <summary>An INBOUND strip's progress, 0..1: queued, spawning, taxiing, departing, then joining.</summary>
        public static float InboundProgress(InboundPhase p) => 0.1f + 0.2f * (int)p;

        public static string Requisition(float price) => "REQUISITION · " + Credits.Price(price);

        public static string Hint(bool client, int inbound)
        {
            if (client) return "The host runs SUPPLY; you can look, the host requisitions.";
            if (inbound > 0) return N(inbound) + " inbound · they join as they take off";
            return "Pick a pilot, an airframe, its fit and a base; REQUISITION launches it.";
        }

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
