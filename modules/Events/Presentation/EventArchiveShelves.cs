using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Events.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// The library's shelves, in tab order. The first five hold recognition cards read from the game's own
    /// encyclopedia; the last three are the module's event catalog and its two local volumes.
    /// </summary>
    internal static class ArchiveShelf
    {
        public const int Aircraft = 0, Ground = 1, Naval = 2, Structures = 3, Ordnance = 4, Events = 5, World = 6, Manual = 7;
        public const int Count = 8, Units = 5;

        public static readonly string[] Names = { "AIRCRAFT", "GROUND", "NAVAL", "STRUCTURES", "ORDNANCE", "EVENTS", "WORLD", "MANUAL" };

        public static readonly AvIcon[] Icons =
        {
            AvIcon.Plane, AvIcon.Shield, AvIcon.WaveSine, AvIcon.BuildingBank, AvIcon.Target,
            AvIcon.AlertTriangle, AvIcon.Map2, AvIcon.Bookmark,
        };

        public static readonly string[] Help =
        {
            "AIRCRAFT: a recognition card for every airframe in the game's encyclopedia, with a scaled plate and its performance set against the rest of the shelf.",
            "GROUND: recognition cards for vehicles, grouped by class code: mass, armour, road speed and cost against the rest of the shelf.",
            "NAVAL: recognition cards for ships, largest classes first: displacement, speed, armour and complement.",
            "STRUCTURES: recognition cards for buildings and emplacements: armour, capture figures and value.",
            "ORDNANCE: recognition cards for missiles, bombs and guided shells: mass, range, yield and round cost.",
            "EVENTS: a dossier for every authored theater event: price effect, reset clock, window, tier, target and timed orders.",
            "WORLD: short files on life behind the front line. Flavour only; nothing here changes the game.",
            "MANUAL: how to read a dispatch and issue orders. Start here if the desk is new to you.",
        };

        /// <summary>The native definitions a unit shelf draws on, or null while the encyclopedia is not loaded.</summary>
        internal static IEnumerable<UnitDefinition> Source(int shelf)
        {
            Encyclopedia book = Encyclopedia.i;
            if (book == null) return null;
            switch (shelf)
            {
                case Aircraft: return book.aircraft;
                case Ground: return book.vehicles;
                case Naval: return book.ships;
                case Structures: return book.buildings;
                case Ordnance: return book.missiles;
                default: return null;
            }
        }

        /// <summary>A definition earns a card when the game itself would list it and it has a model to show.</summary>
        internal static bool Held(UnitDefinition definition) =>
            definition != null && definition.unitPrefab != null && definition.IsAllowed(false);

        /// <summary>How many records a shelf holds, without building its cards (the console's DESK drawers read this).</summary>
        public static int CountOf(int shelf)
        {
            if (shelf == Events) return EventCatalog.All.Length;
            if (shelf == World) return EventDocs.World.Length;
            if (shelf == Manual) return EventDocs.Guide.Length;
            IEnumerable<UnitDefinition> source = Source(shelf);
            if (source == null) return 0;
            int n = 0;
            foreach (UnitDefinition definition in source)
                if (Held(definition) && n < ArchiveLibrary.ShelfLimit) n++;
            return n;
        }
    }

    /// <summary>
    /// One comparative reading on a recognition card. <see cref="Value"/> is the raw figure the shelf compares;
    /// zero means the definition does not establish it, and the card then prints a dash, never a 0.
    /// </summary>
    internal readonly struct ArchiveFigure
    {
        public readonly string Label, Text, Unit;
        public readonly float Value;

        public ArchiveFigure(string label, float value, string text, string unit)
        {
            Label = label;
            bool known = value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
            Value = known ? value : 0f;
            Text = known ? text : "—";
            Unit = known ? unit : "";
        }

        public bool Known => Value > 0f;
    }

    /// <summary>A recognition card: what one native definition says about itself, already formatted.</summary>
    internal sealed class ArchiveRecord
    {
        public string Code = "", Name = "", Key = "", Reported = "", Notes = "", Detail = "", Brief = "";
        public float Length, Span, Height;
        public float[] Weights;
        public int Role = -1, Order;
        public ArchiveFigure[] Figures;
        public GameObject Prefab;
        public Sprite MapIcon;
    }

    /// <summary>
    /// The unit shelves, read from <c>Encyclopedia.i</c> when the window opens and dropped when it closes. Each
    /// shelf also keeps the largest value of every figure, which is what a card's bars are scaled against.
    /// </summary>
    internal sealed class ArchiveLibrary
    {
        internal const int ShelfLimit = 160;

        internal static readonly string[] RoleNames = { "ANTI-SURFACE", "ANTI-AIR", "ANTI-MISSILE", "ANTI-RADAR" };
        private static readonly string[] RoleTags = { "ANTI-SURF", "ANTI-AIR", "ANTI-MSL", "ANTI-RDR" };

        private readonly List<ArchiveRecord>[] cards = new List<ArchiveRecord>[ArchiveShelf.Units];
        private readonly float[][] largest = new float[ArchiveShelf.Units][];
        private readonly bool[] distinct = new bool[ArchiveShelf.Units];

        public ArchiveLibrary()
        {
            for (int shelf = 0; shelf < cards.Length; shelf++) cards[shelf] = new List<ArchiveRecord>(64);
        }

        public void Clear()
        {
            foreach (List<ArchiveRecord> shelf in cards) shelf.Clear();
        }

        public void Load()
        {
            for (int shelf = 0; shelf < cards.Length; shelf++)
            {
                List<ArchiveRecord> into = cards[shelf];
                into.Clear();
                IEnumerable<UnitDefinition> source = ArchiveShelf.Source(shelf);
                if (source != null)
                    foreach (UnitDefinition definition in source)
                    {
                        if (!ArchiveShelf.Held(definition) || into.Count >= ShelfLimit) continue;
                        // A definition that cannot be read is left off the shelf; it must not take the window down.
                        try { into.Add(Card(definition, shelf)); }
                        catch (Exception) { }
                    }
                // Aircraft file by name; everything else by class code first, so a class reads as one block.
                bool byClass = shelf != ArchiveShelf.Aircraft;
                into.Sort((a, b) =>
                {
                    int order = a.Order.CompareTo(b.Order);
                    if (order == 0 && byClass) order = string.Compare(a.Code, b.Code, StringComparison.OrdinalIgnoreCase);
                    return order != 0 ? order : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                int figures = into.Count > 0 ? into[0].Figures.Length : 0;
                largest[shelf] = new float[figures];
                var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                distinct[shelf] = true;
                foreach (ArchiveRecord card in into)
                {
                    distinct[shelf] &= codes.Add(card.Code);
                    for (int i = 0; i < figures; i++)
                        largest[shelf][i] = Mathf.Max(largest[shelf][i], card.Figures[i].Value);
                }
            }
        }

        public int Count(int shelf) => shelf >= 0 && shelf < cards.Length ? cards[shelf].Count : ArchiveShelf.CountOf(shelf);

        public ArchiveRecord Card(int shelf, int item) => cards[shelf][item];

        /// <summary>A figure as a share of the largest on its shelf (the bar), 0 when it is not established.</summary>
        public float Share(int shelf, int figure, float value) =>
            value > 0f && largest[shelf][figure] > 0f ? Mathf.Clamp01(value / largest[shelf][figure]) : 0f;

        /// <summary>"3/13": third largest of the thirteen cards on the shelf that establish this figure.</summary>
        public string Rank(int shelf, int figure, float value)
        {
            if (value <= 0f) return "";
            int ahead = 0, known = 0;
            foreach (ArchiveRecord card in cards[shelf])
            {
                float other = card.Figures[figure].Value;
                if (other > 0f) known++;
                if (other > value) ahead++;
            }
            return AvNum.Fixed(ahead + 1, 0) + "/" + AvNum.Fixed(known, 0);
        }

        /// <summary>True when every card on the shelf has its own code, so a code alone names one card (a class code does not).</summary>
        public bool DistinctCodes(int shelf) => distinct[shelf];

        /// <summary>How many figures a card on this shelf carries.</summary>
        public int Figures(int shelf) => largest[shelf] != null ? largest[shelf].Length : 0;

        /// <summary>The card holding the largest value of a figure on its shelf, or -1 when no card establishes it.</summary>
        public int Leader(int shelf, int figure)
        {
            if (largest[shelf][figure] <= 0f) return -1;
            for (int i = 0; i < cards[shelf].Count; i++)
                if (cards[shelf][i].Figures[figure].Value >= largest[shelf][figure]) return i;
            return -1;
        }

        /// <summary>Other cards on the shelf with the same primary role, in shelf order (the card's cross-references).</summary>
        public void SameRole(int shelf, int item, List<int> into)
        {
            into.Clear();
            int role = cards[shelf][item].Role;
            for (int i = 0; i < cards[shelf].Count; i++)
                if (i != item && cards[shelf][i].Role == role) into.Add(i);
        }

        public static string RoleTag(int role) => role >= 0 ? RoleTags[role] : "SUPPORT";
        public static string RoleName(int role) => role >= 0 ? RoleNames[role] : "SUPPORT";

        // ---- Reading a definition --------------------------------------------------------------------------

        private static ArchiveRecord Card(UnitDefinition d, int shelf)
        {
            var card = new ArchiveRecord
            {
                Code = Clean(d.code), Name = Clean(d.unitName), Key = d.jsonKey ?? "", Reported = (d.bogeyName ?? "").Trim(),
                Notes = (d.description ?? "").Trim(), Length = d.length, Span = d.width, Height = d.height,
                Prefab = d.unitPrefab, MapIcon = d.mapIcon,
                Weights = new[] { d.roleIdentity.antiSurface, d.roleIdentity.antiAir, d.roleIdentity.antiMissile, d.roleIdentity.antiRadar },
            };
            for (int i = 0; i < card.Weights.Length; i++)
                if (card.Weights[i] > 0f && (card.Role < 0 || card.Weights[i] > card.Weights[card.Role])) card.Role = i;

            switch (shelf)
            {
                case ArchiveShelf.Aircraft:
                {
                    // Speeds are stored in km/h and the weight the game shows is the prefab mass it caches at load.
                    AircraftInfo info = d is AircraftDefinition aircraft ? aircraft.aircraftInfo : null;
                    card.Figures = new[]
                    {
                        Kmh("MAX SPEED", info != null ? info.maxSpeed : 0f),
                        Kmh("STALL SPEED", info != null ? info.stallSpeed : 0f),
                        Plain("MANOEUVRE", info != null ? info.maneuverability : 0f, "G"),
                        Kg("EMPTY WEIGHT", d.mass),
                        Kg("MAX WEIGHT", info != null ? info.maxWeight : 0f),
                        Plain("RCS", d.radarSize, ""),
                        Km("VISUAL RANGE", d.visibleRange),
                        Cost("UNIT COST", d.value),
                    };
                    break;
                }
                case ArchiveShelf.Ground:
                    card.Figures = new[]
                    {
                        Kmh("TOP SPEED", Read(() => d.unitPrefab.GetComponent<GroundVehicle>().GetTopSpeed())),
                        Kg("MASS", d.mass),
                        Plain("ARMOUR TIER", d.armorTier, ""),
                        Plain("DAMAGE TOL.", d.damageTolerance, ""),
                        Plain("MANPOWER", d.manpower, ""),
                        Plain("CAPTURE STR.", d.captureStrength, ""),
                        Km("VISUAL RANGE", d.visibleRange),
                        Cost("UNIT COST", d.value),
                    };
                    break;
                case ArchiveShelf.Naval:
                {
                    var ship = d as ShipDefinition;
                    if (ship != null)
                    {
                        card.Code = ship.shipType.ToString();
                        card.Order = (int)ship.shipType;
                    }
                    card.Figures = new[]
                    {
                        Kmh("TOP SPEED", ship != null && ship.shipInfo != null ? ship.shipInfo.topSpeed : 0f),
                        Kg("DISPLACEMENT", d.mass),
                        Plain("ARMOUR TIER", d.armorTier, ""),
                        Plain("DAMAGE TOL.", d.damageTolerance, ""),
                        Plain("MANPOWER", d.manpower, ""),
                        Plain("CAPTURE STR.", d.captureStrength, ""),
                        Km("VISUAL RANGE", d.visibleRange),
                        Cost("UNIT COST", d.value),
                    };
                    break;
                }
                case ArchiveShelf.Structures:
                    card.Figures = new[]
                    {
                        Plain("ARMOUR TIER", d.armorTier, ""),
                        Plain("DAMAGE TOL.", d.damageTolerance, ""),
                        Plain("MANPOWER", d.manpower, ""),
                        Plain("CAPTURE STR.", d.captureStrength, ""),
                        Plain("CAPTURE DEF.", d.captureDefense, ""),
                        Plain("CAPTURE CAP.", d.captureCapacity, ""),
                        Km("VISUAL RANGE", d.visibleRange),
                        Cost("UNIT COST", d.value),
                    };
                    break;
                default:
                {
                    // The same readings the game's own missile sheet takes off the prefab. Range is printed for
                    // powered rounds only, as the game does.
                    Missile round = null;
                    try { round = d.unitPrefab.GetComponentInChildren<Missile>(); }
                    catch (Exception) { }
                    WeaponInfo weapon = null;
                    if (round != null)
                    {
                        try { weapon = round.GetWeaponInfo(); }
                        catch (Exception) { }
                        try
                        {
                            MissileSeeker seeker = round.GetComponent<MissileSeeker>();
                            if (seeker != null) card.Detail = (seeker.GetSeekerType() ?? "").Trim().ToUpperInvariant();
                        }
                        catch (Exception) { }
                    }
                    bool powered = round != null && Read(() => round.CalcDeltaV()) > 0f;
                    card.Figures = new[]
                    {
                        Kg("MASS", Read(() => d is MissileDefinition missile ? missile.GetMass() : d.mass)),
                        Km("MAX RANGE", powered && weapon != null ? weapon.targetRequirements.maxRange : 0f),
                        Yield("YIELD", round != null ? Read(() => round.GetYield()) : 0f),
                        Plain("BURN TIME", round != null ? Read(() => round.GetTotalBurnTime()) : 0f, "S"),
                        Plain("RCS", d.radarSize, ""),
                        Cost("ROUND COST", weapon != null ? weapon.costPerRound : d.value),
                    };
                    break;
                }
            }
            // The index's second line: the shelf's lead figure and what the unit costs.
            int lead = shelf == ArchiveShelf.Aircraft || shelf == ArchiveShelf.Structures || shelf == ArchiveShelf.Ordnance ? 0 : 1;
            card.Brief = Brief(card.Figures[lead], card.Figures[card.Figures.Length - 1]);
            return card;
        }

        private static string Brief(ArchiveFigure a, ArchiveFigure b)
        {
            string first = a.Known ? (a.Unit.Length > 0 ? a.Text + " " + a.Unit : a.Label + " " + a.Text) : "";
            string second = b.Known ? b.Text + " " + b.Unit : "";
            return first.Length > 0 && second.Length > 0 ? first + "  ·  " + second : first + second;
        }

        private static string Clean(string text)
        {
            string trimmed = (text ?? "").Trim();
            return trimmed.Length > 0 ? trimmed : "—";
        }

        /// <summary>A reading taken off a prefab component: anything that throws or is not a number is not established.</summary>
        private static float Read(Func<float> reading)
        {
            try
            {
                float value = reading();
                return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
            }
            catch (Exception) { return 0f; }
        }

        /// <summary>
        /// Just enough decimals to tell two cards apart, with no trailing zeros: 1,725 / 36 / 12.5 / 7.5 / 0.12 /
        /// 0.002. A figure too small for three decimals says so instead of rounding to a zero it is not.
        /// </summary>
        private static string Num(double v)
        {
            if (v >= 100.0) return AvNum.Thousands(v);
            if (v < 0.001) return "<0.001";
            string text = AvNum.Fixed(v, v >= 10.0 ? 1 : v >= 0.1 ? 2 : 3);
            return text.IndexOf('.') < 0 ? text : text.TrimEnd('0').TrimEnd('.');
        }

        private static ArchiveFigure Plain(string label, float value, string unit) => new ArchiveFigure(label, value, Num(value), unit);

        private static ArchiveFigure Kmh(string label, float kmh) => new ArchiveFigure(label, kmh, AvNum.Thousands(kmh), "KM/H");

        private static ArchiveFigure Km(string label, float metres) =>
            metres >= 1000f ? new ArchiveFigure(label, metres, Num(metres / 1000.0), "KM")
            : new ArchiveFigure(label, metres, AvNum.Fixed(metres, 0), "M");

        private static ArchiveFigure Kg(string label, float kg) =>
            kg >= 10000f ? new ArchiveFigure(label, kg, Num(kg / 1000.0), "T")
            : new ArchiveFigure(label, kg, AvNum.Thousands(kg), "KG");

        /// <summary>Values are held in millions, as the game's own sheets read them.</summary>
        private static ArchiveFigure Cost(string label, float millions) =>
            millions >= 1000f ? new ArchiveFigure(label, millions, "$" + AvNum.Fixed(millions / 1000.0, 2), "B")
            : millions >= 1f ? new ArchiveFigure(label, millions, "$" + Num(millions), "M")
            : new ArchiveFigure(label, millions, "$" + Num(millions * 1000.0), "K");

        /// <summary>Kilograms of TNT; a kiloton and up reads in kilotons.</summary>
        private static ArchiveFigure Yield(string label, float kg) =>
            kg >= 1e6f ? new ArchiveFigure(label, kg, Num(kg / 1e6), "KT")
            : new ArchiveFigure(label, kg, AvNum.Thousands(kg), "KG");
    }
}
