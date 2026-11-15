using System;
using System.Collections.Generic;
using System.Text;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>What a front card stamps on itself. WAITING = no host snapshot yet, OFFLINE = no host link, DEGRADED = a current unresolved condition.</summary>
    internal enum FrontStatus : byte { Waiting, Operational, Degraded, Offline }

    /// <summary>Which phase of the shown programme the card is in: money coming in, or construction running. Never one painted as the other.</summary>
    internal enum ProgrammePhase : byte { None, Funding, Building }

    /// <summary>One front card of the OPS page, already worded.</summary>
    internal struct FrontCardView
    {
        public Front Front;
        public bool Known;
        public FrontStatus Status;
        public int Grade, Superiority;
        /// <summary>Alert = the current blocker (what is wrong NOW; empty when nothing is).</summary>
        public string Word, Directive, Building, BuildNote, Alert;
        /// <summary>The front's perk power as the card shows it: <c>POWER ×1.25</c> (empty before the host has spoken).</summary>
        public string Power;
        public int QueueCount;
        public AvState Tone, AlertTone;
        public ProgrammePhase Phase;
        public float BuildProgress;
    }

    /// <summary>One perk of the PERKS tab: a call card (brevity code, rung, price, state). Clicking it inspects it; nothing is armed or spent.</summary>
    internal struct PerkRowView
    {
        public SupportActionId Id;
        public Front Front;
        public int Rung;
        public string Code, Name, RungWord, Price, State;
        public AvState Tone;
        public bool Enabled, Armed, Pending, Inspected;
    }

    /// <summary>The pinned order strip: the inspected (or armed) perk in the strip's own lines, and what its buttons may do.</summary>
    internal struct StripView
    {
        public bool Has;
        public string Code, Stamp, Name, Meta, Price, Brief, Gate, Primary;
        public AvState StampTone, GateTone;
        public bool PrimaryEnabled, PrimaryFire, DisarmEnabled;
        /// <summary>The perk (a <see cref="SupportActionId"/>) the buttons act on.</summary>
        public int Id;
    }

    /// <summary>Everything the OPS page paints. Built each refresh by <see cref="OpsPageViews"/> from the manager, the controller and the front mirror.</summary>
    internal sealed class OpsPageView
    {

        public int Allocation, PerksReady, PerksTotal, FrontsAhead, Alerts;
        public readonly FrontCardView[] Cards = new FrontCardView[FrontRules.FrontCount];
        public readonly List<PerkRowView> Perks = new List<PerkRowView>(24);
        /// <summary>Per front: <c>POWER ×1.25 · AHEAD</c>, the caption of each PERKS group (empty before the host has spoken).</summary>
        public readonly string[] PowerLine = { "", "", "" };
        public string Words = "";
        public AvState WordsTone = AvState.Ready;
        public bool Armed, Pending;
        public string FrontWords = "";
        public StripView PerkStrip;
        public bool CruiseFlying, Steering;
        public string CruiseRoute = "";
        public int CruiseLegs;
        public AvState FrontWordsTone = AvState.Inert;
    }

    /// <summary>The pure mapping from the mirror and the tiles onto <see cref="OpsPageView"/>.</summary>
    internal static class OpsPageViews
    {
        private const string Dot = " · ";

        // ---- Fronts -----------------------------------------------------------------------------------------

        /// <param name="online">False when the host link is down: every front stamps OFFLINE (the last mirror reading is still shown).</param>
        /// <param name="grades">The local pilot's skill grade per front system (index = <see cref="Front"/>); null reads as grade 1.</param>
        public static void Cards(OpsPageView v, FrontStateData d, bool known, float now, bool online = true, int[] grades = null)
        {
            int ahead = 0, alerts = 0;
            for (int i = 0; i < FrontRules.FrontCount; i++)
            {
                var f = (Front)i;
                FrontCardView c = default;
                c.Front = f; c.Known = known;
                c.Grade = grades != null && i < grades.Length ? Math.Max(1, grades[i]) : 1;
                FrontRow row = known && d != null ? d.Fronts[i] : null;
                if (row == null)
                {
                    c.Status = FrontStatus.Waiting; c.Word = "NO LINK"; c.Tone = AvState.Inert; c.Directive = "—"; c.Power = ""; v.PowerLine[i] = "";
                    c.Building = ""; c.BuildNote = "";
                    c.Alert = "WAITING FOR HOST" + Dot + "FRONT STATE NOT RECEIVED"; c.AlertTone = AvState.Inert;
                    v.Cards[i] = c;
                    continue;
                }
                c.Superiority = row.Superiority;
                c.QueueCount = row.Queue.Count;
                c.Word = FrontSuperiority.Word(row.Superiority);
                c.Power = FrontSuperiority.PowerWord(row.Superiority);
                v.PowerLine[i] = FrontSuperiority.PowerLine(row.Superiority);
                c.Tone = row.Superiority >= 20 ? AvState.Ready : row.Superiority > -20 ? AvState.Info : row.Superiority > -60 ? AvState.Caution : AvState.Danger;
                c.Directive = FrontRules.Name(row.Directive); // the lock and its holder live in the front window, where the posture is changed
                if (row.Superiority >= 20) ahead++;

                // The current blocker: unresolved conditions only, in a fixed priority (no link, enemy superiority).
                // The newest log row is history: it never makes an alert by itself.
                int conditions = 0;
                string blocker = null;
                AvState bt = AvState.Inert;
                if (!online) { conditions++; blocker = "NO HOST LINK" + Dot + "LAST READING SHOWN"; bt = AvState.Danger; }
                if (row.Superiority <= -20)
                {
                    conditions++;
                    if (blocker == null)
                    {
                        blocker = "ENEMY HOLDS SUPERIORITY" + Dot + c.Word + " " + row.Superiority;
                        bt = row.Superiority <= -60 ? AvState.Danger : AvState.Caution;
                    }
                }
                if (blocker != null)
                {
                    if (conditions > 1) blocker += Dot + "+" + (conditions - 1) + " MORE";
                    blocker = AvStates.Glyph(bt) + blocker;
                }
                c.Alert = blocker ?? ""; c.AlertTone = bt;
                c.Status = !online ? FrontStatus.Offline : conditions > 0 ? FrontStatus.Degraded : FrontStatus.Operational;
                alerts += conditions;

                // The programme being built, else the first one filling, else nothing. Funding and building are different phases.
                int pick = -1;
                for (int q = 0; q < row.Queue.Count; q++) if (row.Queue[q].Percent >= 100) { pick = q; break; }
                if (pick < 0 && row.Queue.Count > 0) pick = 0;
                if (pick >= 0)
                {
                    FrontQueueRow q = row.Queue[pick];
                    bool building = q.Percent >= 100;
                    int left = BuildLeft(q, d, now);
                    float build = Math.Max(1f, FrontRules.BuildSeconds(q.Id));
                    c.Phase = building ? ProgrammePhase.Building : ProgrammePhase.Funding;
                    c.Building = FrontWords.Programme(q.Id, q.Kind);
                    c.BuildProgress = building ? 1f - left / build : q.Percent / 100f;
                    c.BuildNote = building ? FrontRules.Clock(left) + " LEFT" : q.Percent + " % FUNDED";
                }
                else { c.Building = ""; c.BuildNote = ""; }
                v.Cards[i] = c;
            }
            v.FrontsAhead = ahead;
            v.Alerts = alerts;
        }

        private static int BuildLeft(in FrontQueueRow q, FrontStateData d, float now) =>
            (int)Math.Max(0f, Math.Ceiling(q.BuildLeft - (q.Percent >= 100 ? now - d.Now : 0f)));

        // ---- Perks ------------------------------------------------------------------------------------------

        /// <summary>The state column word of a perk row.</summary>
        public static string StateWord(in CallTile t)
        {
            switch (t.State)
            {
                case CallState.Ready: return "READY";
                case CallState.Armed: return "ARMED";
                case CallState.Pending: return "PENDING";
                case CallState.Offline: return "NO UPLINK";
                case CallState.LowCredit: return LowAlloc(t.StateWord);
                case CallState.Cooldown: return C2Cap.StateWord(t).Replace("COOL ", "COOLDOWN ");
                default: return t.StateWord; // LOCKED carries its reason (NEEDS SPACE SKILL 4)
            }
        }

        private static string StampWord(CallState s) =>
            s == CallState.Ready ? "READY" : s == CallState.Armed ? "ARMED" : s == CallState.Pending ? "PENDING" : s == CallState.Cooldown ? "COOLDOWN"
            : s == CallState.Locked ? "LOCKED" : s == CallState.LowCredit ? "LOW AP" : "NO LINK";

        public static void Perks(OpsPageView v, IReadOnlyList<CallTile> tiles)
        {
            v.Perks.Clear();
            int ready = 0;
            foreach (CallTile t in tiles)
            {
                CallRow row; CallSheet.TryGet(t.Id, out row);
                if (t.State == CallState.Ready) ready++; // available to arm: an armed perk is spoken for and a request in flight is not available
                v.Perks.Add(new PerkRowView
                {
                    Id = t.Id, Front = row.Front, Rung = row.Grade, Code = Code(t.Id), Name = t.Label, RungWord = t.GradeWord, Price = Ap(t.CostText), State = CallsView.RowWord(StateWord(t)), Tone = Ink(t.State),
                    Enabled = t.Enabled, Armed = t.State == CallState.Armed, Pending = t.State == CallState.Pending
                });
            }
            v.PerksReady = ready;
            v.PerksTotal = tiles.Count;
        }

        public static AvState Ink(CallState s) =>
            s == CallState.Ready ? AvState.Ready : s == CallState.Armed ? AvState.Caution : s == CallState.Pending ? AvState.Info
            : s == CallState.Offline || s == CallState.LowCredit ? AvState.Danger : AvState.Inert;

        /// <summary>
        /// The pinned perk strip and the highlighted row. The strip shows the inspected perk, else the armed one, else the first ready one, else
        /// the first: it always reads a real perk. Inspecting arms nothing and spends nothing.
        /// </summary>
        public static void Order(OpsPageView v, IReadOnlyList<CallTile> tiles, AimSource aim, string aimGrid, bool pending, string words,
            SupportActionId? inspected = null)
        {
            v.Armed = false;
            foreach (CallTile t in tiles) if (t.State == CallState.Armed) { v.Armed = true; break; }
            v.Pending = pending;
            string where = aim == AimSource.Pod ? "LOCKED TARGET" : aim == AimSource.Map ? "MAP CLICK" : aim == AimSource.Team ? "SOF TEAM LASER" : "";

            int shown = -1;
            for (int i = 0; i < tiles.Count && shown < 0; i++) if (inspected.HasValue && tiles[i].Id == inspected.Value) shown = i;
            for (int i = 0; i < tiles.Count && shown < 0; i++) if (tiles[i].State == CallState.Armed) shown = i;
            for (int i = 0; i < tiles.Count && shown < 0; i++) if (tiles[i].State == CallState.Ready) shown = i;
            if (shown < 0 && tiles.Count > 0) shown = 0;

            v.PerkStrip = default;
            for (int i = 0; i < v.Perks.Count; i++)
            {
                PerkRowView p = v.Perks[i];
                p.Inspected = i == shown;
                if (p.Armed && where.Length == 0 && CallSheet.NeedsAim(p.Id)) p.State = "AIM REQUIRED"; // armed but not ready to fire: the row says what is missing
                v.Perks[i] = p;
            }
            if (shown >= 0)
            {
                CallTile t = tiles[shown];
                CallSheet.TryGet(t.Id, out CallRow row);
                bool isArmed = t.State == CallState.Armed;
                string gate; AvState gt;
                if (isArmed)
                {
                    gt = AvState.Caution;
                    gate = !CallSheet.NeedsAim(t.Id) ? "NO AIM NEEDED" + Dot + "PRESS FIRE"
                        : where.Length == 0 ? "AIM REQUIRED" + Dot + "LOCK A TARGET OR RIGHT-CLICK THE MAP"
                        : "AIM " + where + (aimGrid.Length > 0 ? Dot + aimGrid : "") + Dot + "FIRE, OR RIGHT-CLICK THE MAP";
                }
                else if (t.State == CallState.Pending) { gt = AvState.Info; gate = "ORDER SENT" + Dot + "WAITING FOR THE HOST"; }
                else if (t.State == CallState.Ready)
                {
                    gt = AvState.Ready;
                    // The cooldown the host will apply: the rung's value, divided by the front's POWER while it is ahead (the cards were mapped before this).
                    float power = FrontSuperiority.Power(v.Cards[(int)row.Front].Superiority);
                    gate = "READY" + Dot + FrontRules.Clock(PerkPower.Cooldown(CallSheet.Cooldown(row.Tier), power)) + " COOLDOWN AFTER USE" + (t.Reason.Length > 0 ? Dot + t.Reason : "");
                }
                else { gt = Ink(t.State); gate = StateWord(t); }
                v.PerkStrip = new StripView
                {
                    Has = true, Id = (int)t.Id, Code = Code(t.Id), Stamp = StampWord(t.State), StampTone = Ink(t.State), Name = t.Label, Meta = t.GradeWord,
                    Price = Ap(t.CostText), Brief = Brief(t.Id), Gate = gate, GateTone = gt,
                    Primary = isArmed ? "FIRE" : t.State == CallState.Pending ? "WAIT" : "ARM",
                    PrimaryEnabled = isArmed ? aim != AimSource.None || !CallSheet.NeedsAim(t.Id) : t.State == CallState.Ready, PrimaryFire = isArmed, DisarmEnabled = v.Armed
                };
            }
            v.Words = words ?? ""; // empty: the panel words its own tab's hint (CallsPanel)
            v.WordsTone = v.Armed ? AvState.Caution : pending ? AvState.Info : C2Cap.StartsNegative(words ?? "") ? AvState.Danger : AvState.Ready;
        }

        /// <summary>A strip with nothing to act on: a name, a line and a reason, buttons off.</summary>
        public static StripView Idle(string name, string brief, string gate, AvState tone) => new StripView
        {
            Has = true, Name = name, Brief = brief, Gate = gate, GateTone = tone, StampTone = tone, Primary = "ARM", Code = "", Stamp = "", Meta = "", Price = ""
        };

        // ---- Words and caches (bounded; filled on first use so the 0.2 s tick does not rebuild them) ----------------

        private static readonly string[] apCache = new string[128];

        /// <summary>"n AP": allocation points, the personal currency (cached for the usual prices).</summary>
        public static string Ap(int n)
        {
            if (n < 0) n = 0;
            return n < apCache.Length ? apCache[n] ?? (apCache[n] = n + " AP") : n + " AP";
        }

        /// <summary>"10 ALLOC" (a perk tile's cost text) as "10 AP"; text without a leading number passes through.</summary>
        public static string Ap(string costText)
        {
            if (string.IsNullOrEmpty(costText)) return "";
            int n = 0, i = 0;
            while (i < costText.Length && i < 6 && costText[i] >= '0' && costText[i] <= '9') n = n * 10 + (costText[i++] - '0');
            return i > 0 ? Ap(n) : costText;
        }

        private static readonly Dictionary<string, string> lowAlloc = new Dictionary<string, string>(8);

        private static string LowAlloc(string needWord)
        {
            needWord ??= "";
            if (lowAlloc.TryGetValue(needWord, out string hit)) return hit;
            string s = "LOW ALLOC" + Dot + needWord.Replace(" ALLOC", " AP");
            if (lowAlloc.Count < 32) lowAlloc[needWord] = s;
            return s;
        }

        private static readonly Dictionary<SupportActionId, string> codes = new Dictionary<SupportActionId, string>(24);

        /// <summary>
        /// A three-letter brevity code derived from the action's name (initials of its words, else its first letters), so a new perk gets one
        /// without a table: RECON PASS = REC, SAT CAMERA = SCA, SAM NET DOWN = SND.
        /// </summary>
        public static string Code(SupportActionId id)
        {
            if (codes.TryGetValue(id, out string hit)) return hit;
            string n = id.ToString();
            int words = 0;
            for (int i = 0; i < n.Length; i++) if (i == 0 || char.IsUpper(n[i])) words++;
            var sb = new StringBuilder(3);
            if (words == 1) sb.Append(n, 0, Math.Min(3, n.Length));
            else if (words == 2)
            {
                int second = 1;
                while (second < n.Length && !char.IsUpper(n[second])) second++;
                sb.Append(n[0]).Append(n, second, Math.Min(2, n.Length - second));
            }
            else for (int i = 0; i < n.Length && sb.Length < 3; i++) if (i == 0 || char.IsUpper(n[i])) sb.Append(n[i]);
            string code = sb.ToString().ToUpperInvariant();
            if (codes.Count < 64) codes[id] = code;
            return code;
        }

        // Plain effect lines of the catalogue (SupportCatalog descriptions), radius and duration only where they state them.
        // ponytail: mirrors the catalogue text; point this at SupportCatalog.Find(id).Description through a manager accessor if the numbers ever move.
        private static readonly Dictionary<SupportActionId, string> briefs = new Dictionary<SupportActionId, string>
        {
            { SupportActionId.Recon, "RADAR BIRD REVEALS GROUND CONTACTS, STATIC AND MOVING · NEEDS A LIVE UPLINK" },
            { SupportActionId.SatCamera, "OPTICAL BIRD IMAGES THE AIM IN DAYLIGHT, REVEALING UNITS IN ITS WINDOW · LIVE UPLINK" },
            { SupportActionId.Prsm, "ONE OFFBOARD BALLISTIC MISSILE ONTO THE AIM" },
            { SupportActionId.Cruise, "SALVO OF OFFBOARD CRUISE MISSILES ONTO THE AIM · COUNTS AGAINST THE FACTION CAP" },
            { SupportActionId.Artillery, "KINETIC BIRD RELEASES ONE PHYSICAL ROD ONTO THE AIM · NEEDS A LIVE UPLINK" },
            { SupportActionId.ElintSweep, "STATION SIGINT LOCATES ENEMY GROUND RADARS THAT ARE EMITTING" },
            { SupportActionId.FlareMissile, "AIRBURST IR COUNTERMEASURE CLOUD AT THE AIM" },
            { SupportActionId.RadarBlind, "HOSTILE RADAR RANGE X0.4 WITHIN 1.5 KM OF THE AIM · 90 S" },
            { SupportActionId.SamNetDown, "HOSTILE SAMS CANNOT LAUNCH WITHIN 3 KM OF THE AIM · 60 S" },
            { SupportActionId.Emp, "HOSTILE RADAR BLACKOUT FOR 30 S · BATTERIES WIDEN THE PULSE" },
            { SupportActionId.JtacMark, "SOF TEAM LASES THE NEAREST HOSTILE AT THE MARK · LASER-GUIDED WEAPONS SEE IT" },
            { SupportActionId.ReconTeam, "SOF TEAM REVEALS ENEMY GROUND UNITS WITHIN 2 KM OF THE AIM FOR 5 MIN · NEEDS A LIVE CAMP" },
            { SupportActionId.Fortify, "REINFORCES A CONTROLLED ZONE WITH DEFENDERS" },
            { SupportActionId.SabotageStrike, "SOF TEAM SABOTAGES THE NEAREST ENEMY ANCHOR WITHIN 1 KM OF THE AIM · A ROLL DECIDES IT · NEEDS A LIVE CAMP" },
            { SupportActionId.Overwatch, "RADAR BIRD FOLLOWS THE MOVING ENEMY GROUND UNITS NEAREST THE AIM FOR 60 S · NEEDS A MOVER WITHIN 3 KM" },
            { SupportActionId.Starfall, "KINETIC BIRD WALKS 3 TO 5 SMALL RODS, 300 M APART, ALONG YOUR LINE THROUGH THE AIM" },
            { SupportActionId.Siphon, "TAKES FUNDS FROM THE STRONGEST ENEMY'S FRONT BUDGETS INTO YOURS · NO AIM NEEDED" },
            { SupportActionId.DirectAction, "A READY SOF TEAM RAIDS THE REVEALED SAM, RADAR OR ENEMY ANCHOR NEAREST THE AIM · 45 S, THEN A ROLL" },
        };

        public static string Brief(SupportActionId id) => briefs.TryGetValue(id, out string text) ? text : "";
    }
}
