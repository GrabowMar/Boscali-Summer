namespace BoscaliSummer.Features.Support.Domain.Cyber
{
    /// <summary>Wire-stable kind of a spectrum-defence site. One byte in the OPS snapshot.</summary>
    internal enum CyberSiteKind : byte
    {
        None = 0,

        /// <summary>Cyber Command: the root every other site must link back to. Static: the host
        /// raises it on the faction's central airbase.</summary>
        Command = 1,

        /// <summary>Early-warning radar: feeds hostile aircraft into faction tracking.</summary>
        EarlyWarning = 2,

        /// <summary>Defensive jammer: an ECM umbrella over friendly units; backs station operations.</summary>
        Jammer = 3,

        /// <summary>Passive SIGINT post: hears hostile emitters and probes; required to trace.</summary>
        Sigint = 4,

        /// <summary>Relay mast: long links and extra bandwidth.</summary>
        Relay = 5,

        /// <summary>Airbase gateway: a static backbone node the host raises on every other owned airbase.</summary>
        Gateway = 6
    }

    /// <summary>How loud a site is on hostile receivers.</summary>
    internal enum Emission : byte
    {
        Silent = 0,
        Low = 1,
        High = 2
    }

    internal readonly struct CyberSiteInfo
    {
        public readonly CyberSiteKind Kind;
        public readonly string Code;
        public readonly string Name;
        public readonly string Summary;

        /// <summary>Allocation before cost scaling.</summary>
        public readonly float Price;

        /// <summary>Bandwidth produced (positive) or drawn (negative) while on the net, Mb/s.</summary>
        public readonly float Bandwidth;

        /// <summary>Longest link this site can close, metres.</summary>
        public readonly float LinkRange;

        /// <summary>Radius of the site's effect (radar cover, jamming umbrella, SIGINT ear), metres.</summary>
        public readonly float EffectRadius;

        public readonly Emission Emission;
        public readonly int CopyLimit;

        public CyberSiteInfo(CyberSiteKind kind, string code, string name, string summary, float price,
                             float bandwidth, float linkRange, float effectRadius, Emission emission, int copyLimit)
        {
            Kind = kind;
            Code = code;
            Name = name;
            Summary = summary;
            Price = price;
            Bandwidth = bandwidth;
            LinkRange = linkRange;
            EffectRadius = effectRadius;
            Emission = emission;
            CopyLimit = copyLimit;
        }
    }

    /// <summary>
    /// The site catalogue. One table read by the host (prices, limits, effects), the panel
    /// (catalogue tiles, inspector) and the pure tests. Cyber Command and the gateways are
    /// static infrastructure the host raises on owned airbases; the four field kinds are the
    /// trucks a player deploys.
    /// </summary>
    internal static class CyberSites
    {
        public const float DefaultLinkRange = 12000f;

        /// <summary>Most gateways a faction fields besides Cyber Command.</summary>
        public const int MaximumGateways = 5;

        /// <summary>Every kind, indexed by wire byte minus one.</summary>
        public static readonly CyberSiteInfo[] All =
        {
            new CyberSiteInfo(CyberSiteKind.Command, "C2N", "CYBER COMMAND",
                "Network root on the airbase at the centre of your holdings. Comes up by itself; compromised, it locks OPERATIONS.",
                0f, 6f, DefaultLinkRange * 1.5f, 0f, Emission.Silent, 1),
            new CyberSiteInfo(CyberSiteKind.EarlyWarning, "EWR", "EARLY-WARNING RADAR",
                "Feeds hostile aircraft inside its cover into your faction picture. Loud: it can be found and hunted.",
                700f, -1f, DefaultLinkRange, 22000f, Emission.High, 3),
            new CyberSiteInfo(CyberSiteKind.Jammer, "JAM", "DEFENSIVE JAMMER",
                "ECM umbrella: hostile radar missiles homing on friendlies inside it lose lock. Backs station operations.",
                800f, -2f, DefaultLinkRange, 9000f, Emission.High, 3),
            new CyberSiteInfo(CyberSiteKind.Sigint, "SIG", "SIGINT POST",
                "Silent ear: locates hostile emitters, blocks recon probes and detects enemy operations. Needed to TRACE.",
                600f, -1f, DefaultLinkRange, 15000f, Emission.Silent, 2),
            new CyberSiteInfo(CyberSiteKind.Relay, "REL", "RELAY MAST",
                "Closes links twice as far and adds bandwidth. Low emissions.",
                350f, 2f, DefaultLinkRange * 2f, 0f, Emission.Low, 4),
            new CyberSiteInfo(CyberSiteKind.Gateway, "GWY", "AIRBASE GATEWAY",
                "Backbone node on an owned airbase. Comes up by itself, links to every other airbase node and to field sites in reach.",
                0f, 2f, DefaultLinkRange * 1.5f, 0f, Emission.Low, MaximumGateways)
        };

        /// <summary>The kinds a player deploys, in catalogue order.</summary>
        public static readonly CyberSiteKind[] Field =
        {
            CyberSiteKind.EarlyWarning, CyberSiteKind.Jammer, CyberSiteKind.Sigint, CyberSiteKind.Relay
        };

        /// <summary>A kind the wire may carry.</summary>
        public static bool Known(byte kind) => kind >= (byte)CyberSiteKind.Command && kind <= (byte)CyberSiteKind.Gateway;

        /// <summary>A kind a player may deploy as a truck.</summary>
        public static bool Fieldable(byte kind) => kind >= (byte)CyberSiteKind.EarlyWarning && kind <= (byte)CyberSiteKind.Relay;

        /// <summary>Infrastructure the host raises on airbases; never deployed, moved or scrapped by hand.</summary>
        public static bool IsStatic(CyberSiteKind kind) => kind == CyberSiteKind.Command || kind == CyberSiteKind.Gateway;

        public static CyberSiteInfo Info(CyberSiteKind kind) =>
            Known((byte)kind) ? All[(byte)kind - 1] : default;

        public static string Code(CyberSiteKind kind) => Known((byte)kind) ? Info(kind).Code : "---";

        public static string Emissions(Emission emission)
        {
            switch (emission)
            {
                case Emission.High: return "LOUD";
                case Emission.Low: return "LOW";
                default: return "SILENT";
            }
        }
    }
}
