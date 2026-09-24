using System;
using System.Collections.Generic;
using System.Text;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Pure, deterministic headline generator and event processor for the theater news wire.
    ///
    /// The wire carries theater-level consequence and atmosphere only: airbase captures,
    /// strategic launches, ace defeats, aircrew recovery or capture, strategic strikes and
    /// warhead interceptions become headlines, followed by one-line reactions and periodic
    /// SITREPs. Individual traffic — shootdowns, vehicle kills, interceptions, crash sites,
    /// kill streaks and casualty tallies — stays in the tactical log; atmospheric "LARP"
    /// bulletins fill the quiet stretches.
    /// </summary>
    internal sealed class MfdNewsFeed
    {
        internal enum Priority
        {
            Notable = 0,
            Major = 1,
            Critical = 2,
        }

        internal sealed class HeadlineItem
        {
            public string Tag { get; }
            public string ColorHex { get; }
            public string Text { get; }
            public Priority Priority { get; }
            public bool IsLarp { get; }
            public bool IsUrgent => Priority >= Priority.Major;

            public HeadlineItem(string tag, string colorHex, string text, bool isUrgent = false)
                : this(tag, colorHex, text, isUrgent ? Priority.Major : Priority.Notable)
            {
            }

            public HeadlineItem(string tag, string colorHex, string text, Priority priority, bool isLarp = false)
            {
                Tag = tag ?? "WIRE";
                ColorHex = colorHex ?? "#00FFA3";
                Text = text ?? "";
                Priority = priority;
                IsLarp = isLarp;
            }

            public string FormattedText => IsUrgent
                ? $"<b><mark=#243B48AA><color={ColorHex}> [{Tag}] </color></mark> {Text}</b>"
                : $"<mark=#243B48AA><color={ColorHex}> [{Tag}] </color></mark> {Text}";
        }

        public const string Separator = "  <color=#00F0FF>+++</color>  ";

        private const int MaxActiveQueue = 16;
        private const int MaxHistory = 96;
        private const int LarpKeepAfterCycle = 7;
        private const float LogisticsThrottleSeconds = 30f;
        // The wire is atmosphere first: staff SITREPs are rare punctuation, not a feed.
        private const float TheaterHeadlineSeconds = 90f;
        private const float ReactionSeconds = 25f;

        private static readonly Dictionary<string, string> Reactions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "FLASH", "PROVISIONAL AUTHORITY ASSUMES CONTROL OF THE CAPTURED FACILITY — CIVIL AFFAIRS TEAMS EN ROUTE" },
            { "CRITICAL ALERT", "FALLOUT MONITORING STATIONS ACTIVATED THEATER-WIDE — PROTECTIVE POSTURE ORDERED" },
            { "AIR SUPREMACY", "ENEMY PROPAGANDA DISMISSES THE LOSS AS A 'SCHEDULED PILOT ROTATION'" },
            { "CRITICAL KILL", "ESCORT SCREENS TIGHTENED AND SALVAGE TUGS TASKED ACROSS THE APPROACH CORRIDORS" },
            { "BASE DEFENSE", "CIVIL DEFENSE CREDITS AIR DEFENSE CREWS FOR A TEXTBOOK SHIELD" },
            { "POW", "RED CROSS RELAY REQUESTED FOR AIRCREW HELD IN THE SECTOR" },
        };

        /// <summary>
        /// Allocation-free pre-filter for the log feed. The kill feed and routine chatter
        /// dominate the line rate and can never become a headline, so they are rejected
        /// before CleanTags builds a string or ParseEvent scans the line. Permissive on
        /// purpose: a false positive only costs the old parse path.
        /// </summary>
        private static readonly string[] HeadlineTriggers =
        {
            "has been captured by", "nuclear weapon launched", "exclusion zone", "ace",
            " was rescued by ", " was captured by ", "warhead", "destroyed at ",
            " sank ", " demolished ", " intercepted ", " destroyed ",
            " donated ", " provisioned ",
        };

        private readonly List<HeadlineItem> activeQueue = new List<HeadlineItem>(MaxActiveQueue);
        private readonly HashSet<string> seenEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> seenOrder = new Queue<string>(MaxHistory);

        private int larpPoolIndex;
        private int theaterIndex;
        private float lastTheaterHeadlineTime = -60f;
        private float lastUrgentTime = -60f;
        private float lastLogisticsTime = -60f;
        private float lastReactionTime = -60f;

        public static readonly string[] LarpHeadlines = new[]
        {
            "MINISTRY OF SUPPLY DENIES RADAR TUBE SHORTAGE; HOARDING OF SILICON ARSENIDE DECLARED CAPITAL OFFENSE",
            "CIVIL DEFENSE SIREN TEST SCHEDULED 1400 HOURS; RESIDENTS INSTRUCTED TO IGNORE PERSISTENT SONIC BOOMS",
            "PRIME BROADCAST CLAIMS OF RADAR NETWORK COLLAPSE CATEGORICALLY DENIED BY REGIONAL COMMAND",
            "QUARTERMASTER ISSUES STRICT WARNING: JET FUEL IS NOT AUTHORIZED FOR HEATING BARRACKS STOVES",
            "AIR INTELLIGENCE: UNCONFIRMED SIGHTING OF EXPERIMENTAL STEALTH AIRFRAME OVER EASTERN RIDGE",
            "HIGH COMMAND REMINDS ALL AIRCREWS: STRATEGIC RELEASE CODES ROTATE AT 0000Z SHARP",
            "COUNCIL OF COMMERCE CONDEMNS INDISCRIMINATE ARTILLERY STRIKES WHILE APPROVING FURTHER MUNITIONS SHIPMENTS",
            "WEATHER SERVICE WARNS OF HEAVY THERMAL UP-DRAFTS AND ASH CORRIDORS AROUND SMOLDERING INDUSTRIAL PARKS",
            "INTERCEPTED MILITIA TRAFFIC REPORTS ENEMY REINFORCEMENTS STALLED BY WASHED-OUT BRIDGES IN SOUTHERN RIVER",
            "PROVOST MARSHAL ANNOUNCES IMMEDIATE CRACKDOWN ON BLACK MARKET TRANSISTORS AND GYROSCOPE ASSEMBLIES",
            "TACTICAL DOCTRINE ADVISORY: EXTENDED AFTERBURNER USAGE DRAMATICALLY INCREASES INFRARED LOCK ENVELOPE",
            "METEOROLOGICAL OFFICE REPORTS ANOMALOUS IONIZATION BLOOMS NEAR PREVIOUS TEST DETONATION BASINS",
            "CIVILIAN NOTICE INTERCEPT: 'CURFEW IN CAPITAL DISTRICT EXTENDED TO 0500 HOURS; BLACKOUT DRAPES MANDATORY'",
            "DEFENSE MINISTRY REAFFIRMS NUCLEAR NO-FIRST-USE PLEDGE SUBJECT TO REGIONAL THEATER DISCRETION",
            "FORWARD AIR CONTROLLERS WARN STRIKE FLIGHTS OF CONCEALED MANPADS POSITIONS IN ABANDONED QUARRY CRATERS",
            "REGIONAL LOGISTICS REPORT: CRUDE OIL SHIPMENTS BY RAIL DIVERTED TO HARDENED MOUNTAIN REFINERIES",
            "SQUADRON SCUTTLEBUTT: FIGHTER PILOT KNOWN AS 'SMOKEJUMPER' CREDITED WITH EMERGENCY DEADSTICK CARRIER TRAP",
            "ELECTRONIC WARFARE ALERT: UNIDENTIFIED BURST TRANSMISSIONS DETECTED SWEEPING LOW-FREQUENCY SENSOR ARRAYS",
            "NAVAL DISTRICT 3 ADVISES ALL MERCHANT SHIPPING TO AVOID WESTERN GULF WATERS DUE TO DRIFTING SEA MINES",
            "CIVILIAN HOSPITAL CORPS APPEALS FOR EMERGENCY BLOOD DONATIONS FOLLOWING PROLONGED ARTILLERY CLASHES",
            "ARMAMENT DEPOT 9 CONFIRMS SUCCESSFUL STATIC BENCH TESTING OF HIGH-YIELD THERMOBARIC WARHEAD VARIANT",
            "BORDER RADAR STATION REPORTED BRIEFLY ILLUMINATED BY PHASED-ARRAY SEARCH EMISSION; NO SIGHTINGS CONFIRMED",
            "DEPARTMENT OF RECONNAISSANCE IDENTIFIES EXPANDED TRENCH PERIMETERS AND HEAVY BUNKER REINFORCEMENTS",
            "SUPPLY BULLETIN: RATION COUPONS FOR CANNED COFFEE AND TOBACCO DISPATCHED TO FORWARD OPERATING BASES",
            "THEATER DISPATCH: ALL ASSETS REMINDED THAT VISUAL CONFIRMATION IS MANDATORY BEFORE FIRING INTO SMOKE FIELDS",
            "PILOT CHATTER: SIGHTING OF UNMARKED RADAR DRONE RETURNING TOWARD OFFSHORE CARRIER FLOTILLA",
            "RADIO INTERCEPT: 'PRIME LOGISTICS BATTALION EXPERIENCING SEVERE SHORTAGE OF SPARE HEAVY ROAD WHEELS'",
            "WAR CORRESPONDENT REPORT: INFANTRY SQUADS AT FORWARD OUTPOSTS CONSTRUCTING REINFORCED LOG BUNKERS",
            "DEFENSE COMMITTEE APPROVES EXPEDITED MANUFACTURE OF ANTI-RADIATION HARDENED FLIGHT AVIONICS",
            "WARNING: COASTAL RADAR BLIND SPOTS OBSERVED DURING SEVERE NIGHTTIME SEA FOG CONDENSATION",
            "SECURITY BUREAU COMMENCES INQUIRY INTO MISSING CRATES OF CHROME-TINTED PILOT CANOPY POLISH",
            "TACTICAL REMINDER: AUTOMATIC RADAR CHAFF CARTRIDGES DO NOT DECOY INFRARED-GUIDED POINT DEFENSE",
            "INTERCEPT: ENEMY AIR COMMAND EXPRESSES ANGER OVER LOSS OF HIGH-VALUE FUEL CONVOY IN NARROW PASS",
            "PUBLIC SAFETY ADVISORY: UNEXPLODED SUBMUNITIONS IN VACANT FARMLAND MUST BE REPORTED TO GENDARMERIE",
            "STRATEGIC COMMAND: AIRBORNE EARLY WARNING AND CONTROL PATROLS MAINTAINING 24-HOUR RADAR UMBRELLA",
            "FRONT LINE FIELD DISPATCH: 'TROOPS CELEBRATE TIMELY ARRIVAL OF WARM STEW AND DRY FIELD BOOTS'",
            "MINISTRY OF INFORMATION COMMENDS CIVILIAN WORKERS FOR RECORD OUTPUT AT HEAVY FORGE FACILITY 4",
            "ELECTRONIC SURVEILLANCE NOTES HIGH-DENSITY RADAR COMMUNICATONS ORIGINATING FROM UNCHARTED HILLSIDE",
            "NOTICE TO FLIGHT CREWS: HIGH-ALTITUDE CONTRAILS PERSISTING LONGER THAN USUAL DUE TO COLD AIR MASS",
            "PROVOST OFFICE REMINDS ALL PERSONNEL: SCAVENGING HEAVY COPPER FROM DOWNED ENEMY WRECKS IS STRICTLY FORBIDDEN",
            "HOME FRONT: RAIL WORKERS DECLARE CAST-IRON OUTPUT RECORD DESPITE NIGHT-SHIFT BLACKOUTS",
            "HOME FRONT: CIVILIAN EVACUATION TRAINS REROUTED AROUND THE SOUTHERN BRIDGE CORRIDORS",
            "ECONOMY: FISHING FLEETS DIVERTED WHILE MINE SWEEPERS CLEAR THE WESTERN APPROACH CHANNELS",
            "ECONOMY: SYNTHETIC RUBBER RATION WIDENED TO INCLUDE AIRCRAFT FACTORY MOLDING SHOPS",
            "RUMOR: LOUD NIGHT SORTIES FROM THE FORWARD STRIP DISMISSED AS 'TRAINING ACCIDENTS' BY LOCAL PRESS",
            "RUMOR: DESERTERS REPORT ENEMY UNITS TRADING FUEL FOR CIGARETTES IN THE CEASEFIRE VILLAGES",
            "RADIO INTERCEPT: 'PRIME COMMAND WANTS THE HILL BY DAWN — DOUBLE THE ARTILLERY, NO EXCUSES'",
            "RADIO INTERCEPT: 'WE ARE OUT OF CANNON AMMUNITION AND DOWN TO ONE SERVICEABLE RADAR VAN'",
            "WEATHER: ELECTRIC STORMS FORECAST OVER THE CENTRAL RIDGE — INSTRUMENT APPROACHES SUSPENDED",
            "DOCTRINE: INTERCEPTORS REMINDED TO PRESERVE ENERGY WHEN BUGGING OUT BEHIND FRIENDLY SAM COVER",
            "SUPPLY BULLETIN: FORWARD DEPOTS URGED TO BURY MUNITIONS UNDER SANDBAGS BEFORE THE NEXT BARRAGE",
            "HOME FRONT: MOTHERS' LEAGUE PETITIONS FOR LETTER CENSORS TO ALLOW PHOTOS OF NEWBORN CHILDREN",
            "TACTICAL DOCTRINE ADVISORY: WINGMEN REMINDED TO CALL 'BLIND' RATHER THAN GUESS A MERGE",
            "METEOROLOGICAL OFFICE: LOW CEILING EXPECTED OVER THE COAST THROUGH THE MORNING WATCH",
            "GROUND CONTROL BULLETIN: TAXIWAY LIGHTING REPAIRS COMPLETE AT THE FORWARD STRIP",
            "MINISTRY OF SUPPLY: RATIONED COPPER WIRE RELEASED FOR FIELD RADIO REPAIR KITS",
            "SQUADRON SCUTTLEBUTT: GROUND CREW WAGERS RUNNING ON WHICH FLIGHT LANDS LAST TONIGHT",
            "INTERCEPT: ENEMY GROUND CONTROLLER HEARD COUNTING AIRCRAFT ON AN OPEN CHANNEL",
            "ECONOMY: SHIPYARD REPORTS AHEAD OF SCHEDULE ON THE PATROL BOAT REFIT PROGRAM",
            "RUMOR: A DOWNED PILOT WAS RETURNED THROUGH NEUTRAL LINES UNDER A WHITE FLAG",
            "HOME FRONT: SCHOOLCHILDREN'S LETTERS TO THE FRONT DELAYED BY THE MAIL BACKLOG",
            "TACTICAL REMINDER: DECLARE BINGO FUEL EARLY — THE RECOVERY PATTERN GETS CROWDED",
            "STRATEGIC COMMAND NOTES QUIET NIGHT ALONG THE NORTHERN SECTOR FOR THE THIRD DAY RUNNING",
            "WEATHER: MORNING FOG EXPECTED TO BURN OFF BEFORE THE FIRST LAUNCH WINDOW",
            "DEFENSE MINISTRY CONFIRMS ROUTINE ROTATION OF FORWARD AIR CONTROLLERS THIS WEEK",
            "RADIO INTERCEPT: 'TELL MAINTENANCE THE LEFT GEAR DOOR IS STILL RATTLING'",
            "PROVOST OFFICE REPORTS A QUIET WEEK IN THE REAR AREAS, FOR ONCE",
            "SUPPLY BULLETIN: FIELD KITCHENS ISSUED EXTRA COAL RATIONS FOR THE COLD SNAP",
            "WAR CORRESPONDENT REPORT: GROUND CREWS PATCH A FLAK-DAMAGED WING BEFORE DAWN LAUNCH",
        };

        public MfdNewsFeed(int seed = 42)
        {
            larpPoolIndex = Math.Abs(seed) % LarpHeadlines.Length;
            SeedInitialQueue();
        }

        /// <summary>Time of the most recent urgent headline; the ticker watches this to flash.</summary>
        public float LastUrgentTime => lastUrgentTime;

        private void SeedInitialQueue()
        {
            Enqueue(new HeadlineItem("WARNET", "#44EEFF",
                "THEATER WIRE UPLINK ESTABLISHED — FIELD DESK NOW TRACKING ALL FRONTS", Priority.Notable));
            for (int i = 0; i < 5; i++)
            {
                PushLarpHeadline();
            }
        }

        public void PushLarpHeadline()
        {
            string headline = LarpHeadlines[larpPoolIndex % LarpHeadlines.Length];
            larpPoolIndex++;

            string tag = "WIRE";
            string color = "#44EE88";

            if (headline.StartsWith("MINISTRY") || headline.StartsWith("CIVIL") || headline.StartsWith("DEFENSE"))
            {
                tag = "DISPATCH";
                color = "#88FFAA";
            }
            else if (headline.StartsWith("WEATHER") || headline.StartsWith("METEOR"))
            {
                tag = "MET";
                color = "#66CCFF";
            }
            else if (headline.StartsWith("TACTICAL") || headline.StartsWith("HIGH COMMAND") || headline.StartsWith("STRATEGIC"))
            {
                tag = "DOCTRINE";
                color = "#EEDD55";
            }
            else if (headline.StartsWith("HOME FRONT"))
            {
                tag = "HOME FRONT";
                color = "#88FFAA";
            }
            else if (headline.StartsWith("ECONOMY"))
            {
                tag = "ECONOMY";
                color = "#EEDD55";
            }
            else if (headline.StartsWith("RUMOR"))
            {
                tag = "RUMOR";
                color = "#CC99FF";
            }
            else if (headline.Contains("INTERCEPT") || headline.Contains("ELECTRONIC"))
            {
                tag = "INTEL";
                color = "#FFAA33";
            }

            Append(new HeadlineItem(tag, color, headline, Priority.Notable, isLarp: true));
        }

        /// <summary>
        /// Translates a raw game log or kill-feed line into a ticker headline.
        /// Returns true when a headline reached the wire; individual traffic is
        /// deliberately ignored and reports false.
        /// </summary>
        public bool IngestGameEvent(string rawLine)
        {
            return IngestGameEvent(rawLine, 0f);
        }

        public bool IngestGameEvent(string rawLine, float now)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return false;
            // Most log lines cannot become a headline; reject them without allocating.
            if (!MightCarryHeadline(rawLine)) return false;

            string clean = CleanTags(rawLine).Trim();
            if (clean.Length == 0) return false;

            HeadlineItem parsed = ParseEvent(clean);
            if (parsed == null) return false;

            // Throttled logistics traffic is dropped before it is remembered, so a later
            // identical contribution can still be reported once the window reopens.
            if (parsed.Tag == "LOGISTICS" && now - lastLogisticsTime < LogisticsThrottleSeconds)
            {
                return false;
            }

            if (seenEvents.Contains(clean)) return false;
            seenEvents.Add(clean);
            seenOrder.Enqueue(clean);
            while (seenOrder.Count > MaxHistory)
            {
                seenEvents.Remove(seenOrder.Dequeue());
            }

            if (parsed.Priority == Priority.Notable)
            {
                if (parsed.Tag == "LOGISTICS") lastLogisticsTime = now;
                Enqueue(parsed);
                return true;
            }

            MaybeReact(parsed, now);
            PushUrgent(parsed, now);
            return true;
        }

        public void Enqueue(HeadlineItem item)
        {
            if (item == null) return;
            activeQueue.RemoveAll(existing => existing.Tag == item.Tag && existing.Text == item.Text);
            activeQueue.Insert(0, item);
            while (activeQueue.Count > MaxActiveQueue)
            {
                activeQueue.RemoveAt(activeQueue.Count - 1);
            }
        }

        /// <summary>
        /// Translates a raw game log or kill-feed line into a wire headline. Returns null
        /// for individual traffic the wire deliberately leaves to the tactical log:
        /// repairs, chatter, shootdowns, vehicle kills, crash sites and routine
        /// interceptions.
        ///
        /// Vanilla kill-feed wording comes from KillTypeExtensions.GetVerb:
        /// "shot down", "destroyed", "demolished", "intercepted", "sank", and the
        /// unattributed "crashed" / "was destroyed" / "collapsed" forms.
        /// </summary>
        public static HeadlineItem ParseEvent(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            // 1. Airbase capture
            int capturedIdx = line.IndexOf("has been captured by", StringComparison.OrdinalIgnoreCase);
            if (capturedIdx > 0)
            {
                string baseName = line.Substring(0, capturedIdx).Trim();
                string faction = line.Substring(capturedIdx + "has been captured by".Length).Trim();
                return new HeadlineItem(
                    "FLASH",
                    "#FF4444",
                    $"{baseName.ToUpperInvariant()} CAPTURED BY {faction.ToUpperInvariant()} FORCES — PERIMETER DEFENSE REPORTED SECURED",
                    Priority.Critical);
            }

            // 2. Nuclear Weapon / Exclusion Zone
            if (Contains(line, "Nuclear weapon launched") ||
                Contains(line, "exclusion zone"))
            {
                return new HeadlineItem(
                    "CRITICAL ALERT",
                    "#FF2222",
                    "STRATEGIC WEAPON LAUNCH DETECTED — THEATER EXCLUSION ZONE ACTIVE — EVACUATION CORRIDORS ENFORCED",
                    Priority.Critical);
            }

            // 3. Ace Pilot Defeat
            if (Contains(line, "ace defeated") ||
                (Contains(line, "ace") && Contains(line, "defeated")))
            {
                return new HeadlineItem(
                    "AIR SUPREMACY",
                    "#FF9900",
                    "HOSTILE ACE PILOT CONFIRMED SPLASHED OVER FRONT — COMBAT RECORD UPDATED",
                    Priority.Critical);
            }

            // 4. Downed aircrew (vanilla pilot capture / rescue)
            if (TrySplit(line, " was rescued by ", out string rescuer, out string rescued))
            {
                return new HeadlineItem(
                    "CSAR",
                    "#44EEFF",
                    $"{rescued.ToUpperInvariant()} RECOVERED BY {rescuer.ToUpperInvariant()} — COMBAT SEARCH AND RESCUE SUCCESS",
                    Priority.Major);
            }
            if (TrySplit(line, " was captured by ", out string captor, out string captive))
            {
                return new HeadlineItem(
                    "POW",
                    "#FFAA33",
                    $"{captive.ToUpperInvariant()} TAKEN PRISONER BY {captor.ToUpperInvariant()} — AIRCREW LISTED MIA",
                    Priority.Major);
            }

            // 5. Warhead destroyed by base point defense (vanilla Warning message)
            if (Contains(line, "warhead") && Contains(line, "destroyed at "))
            {
                int at = line.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
                string where = at > 0 ? line.Substring(at + " at ".Length).Trim() : "THE AIRBASE";
                return new HeadlineItem(
                    "BASE DEFENSE",
                    "#44FF88",
                    $"INCOMING WARHEAD DESTROYED BY POINT DEFENSE AT {where.ToUpperInvariant()}",
                    Priority.Major);
            }

            // 6. Capital ship loss
            if (TrySplit(line, " sank ", out string raider, out string sunk) && IsCapitalShip(sunk))
            {
                return new HeadlineItem(
                    "CRITICAL KILL",
                    "#FF4444",
                    $"{sunk.ToUpperInvariant()} SENT TO THE BOTTOM BY {raider.ToUpperInvariant()}",
                    Priority.Critical);
            }

            // 7. Strategic structure demolition
            if (TrySplit(line, " demolished ", out string demolitionist, out string structure) &&
                IsStrategicTarget(structure))
            {
                return new HeadlineItem(
                    "DEMOLITION",
                    "#FFAA33",
                    $"{structure.ToUpperInvariant()} LEVELLED BY {demolitionist.ToUpperInvariant()}",
                    Priority.Major);
            }

            // 8. Nuclear ordnance intercepted; conventional wave traffic stays in the log
            if (TrySplit(line, " intercepted ", out string defender, out string ordnance) &&
                (Contains(ordnance, "nuclear") || Contains(ordnance, "nuke")))
            {
                return new HeadlineItem(
                    "CRITICAL ALERT",
                    "#FF2222",
                    $"{defender.ToUpperInvariant()} INTERCEPTED INCOMING {ordnance.ToUpperInvariant()}",
                    Priority.Critical);
            }

            // 9. Strategic target destroyed
            if (TrySplit(line, " destroyed ", out string attacker, out string victim) &&
                IsStrategicTarget(victim))
            {
                return new HeadlineItem(
                    "CRITICAL KILL",
                    "#FF4444",
                    $"{victim.ToUpperInvariant()} REPORTED DESTROYED IN COMBAT BY {attacker.ToUpperInvariant()}",
                    Priority.Critical);
            }

            // 10. Logistics contributions: informative, but throttled to one voice at a time
            int donatedIdx = line.IndexOf(" donated ", StringComparison.OrdinalIgnoreCase);
            int provisionedIdx = line.IndexOf(" provisioned ", StringComparison.OrdinalIgnoreCase);
            if (donatedIdx > 0 || provisionedIdx > 0)
            {
                int split = donatedIdx > 0 ? donatedIdx : provisionedIdx;
                string who = line.Substring(0, split).Trim();
                string dedicated = who.Length > 0
                    ? $"{who.ToUpperInvariant()} DELIVERS A SUPPLY SHIPMENT TO THE WAR EFFORT"
                    : "VOLUNTEER PROVISIONS AND REINFORCEMENTS REACH FORWARD BASES";
                return new HeadlineItem("LOGISTICS", "#FFAA22", dedicated, Priority.Notable);
            }

            // Everything else — shootdowns, vehicle kills, crash sites, routine interceptions —
            // is individual traffic and belongs in the tactical log, not the theater wire.
            return null;
        }

        /// <summary>
        /// Periodic theater color from CommandManager. The numbers only choose which
        /// qualitative dispatch runs — the wire never prints tallies, percentages or
        /// unit counts, because the desk is writing for morale, not for the staff.
        /// </summary>
        public void UpdateTheaterStatus(
            float now,
            int defcon,
            float territoryRatio,
            float airSuperiorityRatio,
            int activeClashes,
            int contestedAirbases)
        {
            if (now - lastTheaterHeadlineTime < TheaterHeadlineSeconds) return;
            lastTheaterHeadlineTime = now;

            if (contestedAirbases > 0)
            {
                PushUrgent(new HeadlineItem(
                    "SITREP",
                    "#FF5533",
                    "FORWARD CONTEST: RESISTANCE REPORTED AT CONTESTED BASE PERIMETERS — CIVIL AFFAIRS TEAMS STANDING BY",
                    Priority.Major), now);
                return;
            }

            if (defcon <= 2)
            {
                PushUrgent(new HeadlineItem(
                    "DEFCON ALERT",
                    "#FF2222",
                    "THEATER DEFCON RAISED — STRATEGIC ALERT IN EFFECT ACROSS ALL SECTORS",
                    Priority.Major), now);
                return;
            }

            if (activeClashes > 3)
            {
                Enqueue(new HeadlineItem("HOT ZONE", "#FFAA22",
                    "FRONT ENGAGEMENTS REPORTED ACROSS THE THEATER — FIELD DESKS FILING AROUND THE CLOCK"));
                return;
            }

            if (!float.IsNaN(territoryRatio) && territoryRatio > 0.65f)
            {
                Enqueue(new HeadlineItem("FRONT ADVANCE", "#44EE88",
                    "STRATEGIC ASSESSMENT: ALLIED GROUND FORCES PRESSING THE ADVANTAGE ALONG THE FRONT"));
                return;
            }

            if (!float.IsNaN(territoryRatio) && territoryRatio < 0.35f)
            {
                Enqueue(new HeadlineItem("DEFENSE ALERT", "#FF6644",
                    "FRONT ASSESSMENT: ALLIED FORCES YIELDING GROUND — DEFENSE CORRIDORS UNDER STRAIN"));
                return;
            }

            switch (theaterIndex++ % 3)
            {
                case 0:
                    if (float.IsNaN(airSuperiorityRatio))
                    {
                        Enqueue(new HeadlineItem("PATROL", "#88FFAA",
                            "NO DECISIVE MOVEMENT ON THE FRONT — RECON PATROLS REPORT EMPTY SKIES OVER THE SECTOR"));
                    }
                    else
                    {
                        Enqueue(new HeadlineItem("SITREP", "#66CCFF",
                            "AIR CORRIDORS CONTESTED BUT HELD — INTERCEPTOR SCREENS REPORTING ON STATION"));
                    }
                    break;
                case 1:
                    Enqueue(new HeadlineItem("LOGISTICS", "#FFAA22",
                        "THEATER LOGISTICS: SUPPLY CONVOYS MOVING UNDER ESCORT — FORWARD DEPOTS AT NOMINAL STOCK"));
                    break;
                default:
                    Enqueue(new HeadlineItem("PATROL", "#88FFAA",
                        "NO DECISIVE MOVEMENT ON THE FRONT — RECON PATROLS REPORT EMPTY SKIES OVER THE SECTOR"));
                    break;
            }
        }

        /// <summary>
        /// Assembles the current headlines into a continuous formatted marquee string.
        /// Ensures there are enough items for a wide display.
        /// </summary>
        public string BuildMarqueeText(int minItems = 5)
        {
            while (activeQueue.Count < minItems)
            {
                PushLarpHeadline();
            }

            var sb = new StringBuilder();
            for (int i = 0; i < activeQueue.Count; i++)
            {
                if (i > 0) sb.Append(Separator);
                sb.Append(activeQueue[i].FormattedText);
            }
            sb.Append(Separator);
            return sb.ToString();
        }

        /// <summary>
        /// Retires the one-shot headlines that just scrolled past and refreshes the ambient
        /// pool, so the next cycle opens on fresh copy instead of repeating the last one.
        /// </summary>
        public void OnMarqueeCycleComplete()
        {
            activeQueue.RemoveAll(h => !h.IsLarp);
            while (activeQueue.Count > LarpKeepAfterCycle)
            {
                activeQueue.RemoveAt(0);
            }
            PushLarpHeadline();
        }

        public void Clear()
        {
            activeQueue.Clear();
            seenEvents.Clear();
            seenOrder.Clear();
            larpPoolIndex = 42 % LarpHeadlines.Length;
            theaterIndex = 0;
            lastTheaterHeadlineTime = -60f;
            lastUrgentTime = -60f;
            lastLogisticsTime = -60f;
            lastReactionTime = -60f;
            SeedInitialQueue();
        }

        private void Append(HeadlineItem item)
        {
            if (item == null) return;
            activeQueue.Add(item);
            while (activeQueue.Count > MaxActiveQueue)
            {
                activeQueue.RemoveAt(0);
            }
        }

        private void PushUrgent(HeadlineItem item, float now)
        {
            if (item == null) return;
            lastUrgentTime = now;
            Enqueue(item);
        }

        private void MaybeReact(HeadlineItem item, float now)
        {
            if (item == null || now - lastReactionTime < ReactionSeconds) return;
            if (!Reactions.TryGetValue(item.Tag, out string text)) return;
            lastReactionTime = now;
            Enqueue(new HeadlineItem("WIRE FOLLOW-UP", "#88C0AA", text));
        }

        private static bool TrySplit(string line, string verb, out string first, out string second)
        {
            int idx = line.IndexOf(verb, StringComparison.OrdinalIgnoreCase);
            if (idx <= 0)
            {
                first = second = null;
                return false;
            }

            first = line.Substring(0, idx).Trim();
            second = line.Substring(idx + verb.Length).Trim();
            return first.Length > 0 && second.Length > 0;
        }

        private static bool IsCapitalShip(string name) =>
            Contains(name, "Carrier") ||
            Contains(name, "Cruiser") ||
            Contains(name, "Corvette") ||
            Contains(name, "Destroyer") ||
            Contains(name, "Battleship") ||
            Contains(name, "Frigate");

        private static bool IsStrategicTarget(string name) =>
            IsCapitalShip(name) ||
            Contains(name, "Radar") ||
            Contains(name, "Factory") ||
            Contains(name, "Depot") ||
            Contains(name, "Command") ||
            Contains(name, "Refinery");

        private static bool Contains(string haystack, string needle) =>
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool MightCarryHeadline(string line)
        {
            for (int i = 0; i < HeadlineTriggers.Length; i++)
            {
                if (line.IndexOf(HeadlineTriggers[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static string CleanTags(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            // Untagged lines are the common case and are already clean.
            if (input.IndexOf('<') < 0) return input;
            var sb = new StringBuilder(input.Length);
            bool insideTag = false;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '<') insideTag = true;
                else if (c == '>') insideTag = false;
                else if (!insideTag) sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
