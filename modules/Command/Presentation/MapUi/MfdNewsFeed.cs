using System;
using System.Collections.Generic;
using System.Text;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Pure, deterministic headline generator and event processor for the tactical news ticker.
    ///
    /// Traffic discipline keeps the wire readable: consequential, named events become
    /// headlines, while the kill-feed firehose (missile interceptions, anonymous armor
    /// losses) is counted and periodically condensed into a single digest. Kill streaks,
    /// first blood, casualty tallies and follow-up commentary supply the color; atmospheric
    /// "LARP" bulletins fill the quiet stretches.
    /// </summary>
    internal sealed class MfdNewsFeed
    {
        internal enum Priority
        {
            Routine = 0,
            Notable = 1,
            Major = 2,
            Critical = 3,
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
                ColorHex = colorHex ?? "#44EE88";
                Text = text ?? "";
                Priority = priority;
                IsLarp = isLarp;
            }

            public string FormattedText => IsUrgent
                ? $"<b><color={ColorHex}>[{Tag}]</color> {Text}</b>"
                : $"<color={ColorHex}>[{Tag}]</color> {Text}";
        }

        public const string Separator = "  <color=#2B5D3A>+++</color>  ";

        private const int MaxActiveQueue = 16;
        private const int MaxHistory = 96;
        private const int LarpKeepAfterCycle = 7;
        private const int MaxStreakTracks = 24;
        private const float StreakWindowSeconds = 90f;
        private const float InterceptDigestSeconds = 30f;
        private const int InterceptDigestMinimum = 3;
        private const float ArmorDigestSeconds = 45f;
        private const int ArmorDigestMinimum = 4;
        private const float WreckDigestSeconds = 60f;
        private const int WreckDigestMinimum = 3;
        private const float LedgerSeconds = 150f;
        private const int LedgerMinimum = 8;
        private const float LogisticsThrottleSeconds = 30f;
        private const float TheaterHeadlineSeconds = 45f;
        private const float ReactionSeconds = 25f;

        private sealed class StreakTrack
        {
            public int Kills;
            public float LastKill;
        }

        private static readonly string[] ArmorDigestStyles =
        {
            "ARMOR ATTRITION: {0} VEHICLES CONFIRMED WRECKED IN CONTESTED SECTORS",
            "FRONT REPORT: {0} ARMORED HULKS ADDED TO THE THEATER LEDGER",
            "GROUND WAR DIGEST: {0} VEHICLE LOSSES LOGGED BY FORWARD OBSERVERS",
        };

        private static readonly Dictionary<string, string> Reactions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "FLASH", "PROVISIONAL AUTHORITY ASSUMES CONTROL OF THE CAPTURED FACILITY — CIVIL AFFAIRS TEAMS EN ROUTE" },
            { "CRITICAL ALERT", "FALLOUT MONITORING STATIONS ACTIVATED THEATER-WIDE — PROTECTIVE POSTURE ORDERED" },
            { "AIR SUPREMACY", "ENEMY PROPAGANDA DISMISSES THE LOSS AS A 'SCHEDULED PILOT ROTATION'" },
            { "CRITICAL KILL", "ESCORT SCREENS TIGHTENED AND SALVAGE TUGS TASKED ACROSS THE APPROACH CORRIDORS" },
            { "BASE DEFENSE", "CIVIL DEFENSE CREDITS AIR DEFENSE CREWS FOR A TEXTBOOK SHIELD" },
            { "POW", "RED CROSS RELAY REQUESTED FOR AIRCREW HELD IN THE SECTOR" },
        };

        private readonly List<HeadlineItem> activeQueue = new List<HeadlineItem>(MaxActiveQueue);
        private readonly HashSet<string> seenEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> seenOrder = new Queue<string>(MaxHistory);
        private readonly Dictionary<string, StreakTrack> streaks =
            new Dictionary<string, StreakTrack>(StringComparer.OrdinalIgnoreCase);

        private int larpPoolIndex;
        private int theaterIndex;
        private int armorDigestIndex;
        private float lastTheaterHeadlineTime = -60f;
        private float lastUrgentTime = -60f;
        private float lastLogisticsTime = -60f;
        private float lastReactionTime = -60f;
        private float lastInterceptDigestTime = -60f;
        private float lastArmorDigestTime = -60f;
        private float lastWreckDigestTime = -60f;
        private float lastLedgerTime = -60f;
        private int pendingIntercepts;
        private int pendingArmorLosses;
        private int pendingWrecks;
        private int aircraftLost;
        private int armorLost;
        private int missilesIntercepted;
        private bool firstBloodSeen;

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
        /// Returns true when a headline became visible on the wire; routine traffic is
        /// counted for a later digest and reports false.
        /// </summary>
        public bool IngestGameEvent(string rawLine)
        {
            return IngestGameEvent(rawLine, 0f);
        }

        public bool IngestGameEvent(string rawLine, float now)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return false;

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

            bool queued;
            switch (parsed.Priority)
            {
                case Priority.Routine:
                    CountRoutine(parsed);
                    queued = false;
                    break;
                case Priority.Notable:
                    if (parsed.Tag == "LOGISTICS") lastLogisticsTime = now;
                    Enqueue(parsed);
                    queued = true;
                    break;
                default:
                    MaybeReact(parsed, now);
                    PushUrgent(parsed, now);
                    queued = true;
                    break;
            }

            if (parsed.Tag == "AIR COMBAT")
            {
                aircraftLost++;
                if (TrySplit(clean, " shot down ", out string shooter, out _))
                {
                    RegisterAirKill(shooter, now);
                }
            }

            return queued;
        }

        /// <summary>
        /// Emits due digests and the session casualty ledger. Cheap to call every frame;
        /// all windows are elapsed-time gated and every counter is bounded by the queue cap.
        /// </summary>
        public void Tick(float now)
        {
            if (pendingIntercepts >= InterceptDigestMinimum && now - lastInterceptDigestTime >= InterceptDigestSeconds)
            {
                Enqueue(new HeadlineItem("AIR DEFENSE", "#44EEFF",
                    $"THEATER AIR DEFENSE NET REPELS {pendingIntercepts} INCOMING STORES ACROSS THE FRONT", Priority.Notable));
                pendingIntercepts = 0;
                lastInterceptDigestTime = now;
            }

            if (pendingArmorLosses >= ArmorDigestMinimum && now - lastArmorDigestTime >= ArmorDigestSeconds)
            {
                string style = ArmorDigestStyles[armorDigestIndex++ % ArmorDigestStyles.Length];
                Enqueue(new HeadlineItem("FRONT REPORT", "#FFAA33",
                    string.Format(style, pendingArmorLosses), Priority.Notable));
                pendingArmorLosses = 0;
                lastArmorDigestTime = now;
            }

            if (pendingWrecks >= WreckDigestMinimum && now - lastWreckDigestTime >= WreckDigestSeconds)
            {
                Enqueue(new HeadlineItem("RECOVERY", "#AA8866",
                    $"SEARCH-AND-RESCUE: {pendingWrecks} NEW WRECK SITES MARKED FOR RECOVERY", Priority.Notable));
                pendingWrecks = 0;
                lastWreckDigestTime = now;
            }

            int total = aircraftLost + armorLost + missilesIntercepted;
            if (total >= LedgerMinimum && now - lastLedgerTime >= LedgerSeconds)
            {
                Enqueue(new HeadlineItem("THEATER LEDGER", "#66CCFF",
                    $"SESSION TALLY — {aircraftLost} AIRFRAMES DOWN, {armorLost} ARMOR WRECKED, {missilesIntercepted} MUNITIONS INTERCEPTED",
                    Priority.Notable));
                lastLedgerTime = now;
            }
        }

        public void Enqueue(HeadlineItem item)
        {
            if (item == null) return;
            activeQueue.Insert(0, item);
            while (activeQueue.Count > MaxActiveQueue)
            {
                activeQueue.RemoveAt(activeQueue.Count - 1);
            }
        }

        /// <summary>
        /// Translates a raw game log or kill-feed line into a wire headline. Returns null
        /// for traffic the wire deliberately ignores (repairs, chatter, routine spacing).
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

            // 6. Air-to-air / SAM shootdown
            if (TrySplit(line, " shot down ", out string shooter, out string shotDown))
            {
                return new HeadlineItem(
                    "AIR COMBAT",
                    "#FFB347",
                    $"{shotDown.ToUpperInvariant()} SHOT DOWN BY {shooter.ToUpperInvariant()} — AIRKILL CONFIRMED",
                    Priority.Notable);
            }

            // 7. Ship loss
            if (TrySplit(line, " sank ", out string raider, out string sunk))
            {
                bool capital = IsCapitalShip(sunk);
                return new HeadlineItem(
                    capital ? "CRITICAL KILL" : "VESSEL LOSS",
                    capital ? "#FF4444" : "#66CCFF",
                    $"{sunk.ToUpperInvariant()} SENT TO THE BOTTOM BY {raider.ToUpperInvariant()}",
                    capital ? Priority.Critical : Priority.Major);
            }

            // 8. Structure demolition
            if (TrySplit(line, " demolished ", out string demolitionist, out string structure))
            {
                return new HeadlineItem(
                    "DEMOLITION",
                    "#FFAA33",
                    $"{structure.ToUpperInvariant()} LEVELLED BY {demolitionist.ToUpperInvariant()}",
                    Priority.Notable);
            }

            // 9. Missile interception: routine wave traffic, never a headline on its own
            if (TrySplit(line, " intercepted ", out string defender, out string ordnance))
            {
                bool nuclear = Contains(ordnance, "nuclear") || Contains(ordnance, "nuke");
                return new HeadlineItem(
                    nuclear ? "CRITICAL ALERT" : "AIR DEFENSE",
                    nuclear ? "#FF2222" : "#44EEFF",
                    $"{defender.ToUpperInvariant()} INTERCEPTED INCOMING {ordnance.ToUpperInvariant()}",
                    nuclear ? Priority.Critical : Priority.Routine);
            }

            // 10. Un-attributed losses
            if (Contains(line, " crashed") || Contains(line, " was destroyed") || Contains(line, " collapsed"))
            {
                return new HeadlineItem(
                    "WRECK",
                    "#AA8866",
                    "UNATTRIBUTED LOSS REPORTED — RECOVERY TEAMS DIVERTED TO THE CRASH SITE",
                    Priority.Routine);
            }

            // 11. Attributed vehicle kill
            if (TrySplit(line, " destroyed ", out string attacker, out string victim))
            {
                bool strategic = IsStrategicTarget(victim);
                return new HeadlineItem(
                    strategic ? "CRITICAL KILL" : "COMBAT REPORT",
                    strategic ? "#FF4444" : "#FFAA33",
                    $"{victim.ToUpperInvariant()} REPORTED DESTROYED IN COMBAT BY {attacker.ToUpperInvariant()}",
                    strategic ? Priority.Critical : Priority.Routine);
            }

            // 12. Logistics contributions: informative, but throttled to one voice at a time
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

            return null;
        }

        /// <summary>
        /// Periodically injects tactical theater status based on strategic theater telemetry.
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
                    $"FORWARD CONTEST: HEAVY RESISTANCE REPORTED AROUND {contestedAirbases} CONTESTED BASE PERIMETERS",
                    Priority.Major), now);
                return;
            }

            if (defcon <= 2)
            {
                PushUrgent(new HeadlineItem(
                    "DEFCON ALERT",
                    "#FF2222",
                    $"THEATER DEFCON {defcon} DECLARED — STRATEGIC ALERT IN EFFECT ACROSS ALL SECTORS",
                    Priority.Major), now);
                return;
            }

            if (activeClashes > 3)
            {
                Enqueue(new HeadlineItem("HOT ZONE", "#FFAA22",
                    $"FRONT ENGAGEMENTS: INTENSE SQUAD CLASHES SPREAD ACROSS {activeClashes} SECTORS"));
                return;
            }

            if (territoryRatio > 0.65f)
            {
                Enqueue(new HeadlineItem("FRONT ADVANCE", "#44EE88",
                    $"STRATEGIC ASSESSMENT: ALLIED GROUND UNITS HOLDING {(int)(territoryRatio * 100f)}% THEATER CONTROL"));
                return;
            }

            if (territoryRatio < 0.35f)
            {
                Enqueue(new HeadlineItem("DEFENSE ALERT", "#FF6644",
                    "FRONT ASSESSMENT: HEAVY THEATER PRESSURE — ALLIED FORCES RETRENCHING DEFENSE CORRIDORS"));
                return;
            }

            switch (theaterIndex++ % 3)
            {
                case 0:
                    Enqueue(new HeadlineItem("SITREP", "#66CCFF",
                        $"AIR CORRIDOR DOMINANCE EVALUATED AT {(int)(airSuperiorityRatio * 100f)}% EFFECTIVENESS"));
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
            streaks.Clear();
            larpPoolIndex = 42 % LarpHeadlines.Length;
            theaterIndex = 0;
            armorDigestIndex = 0;
            lastTheaterHeadlineTime = -60f;
            lastUrgentTime = -60f;
            lastLogisticsTime = -60f;
            lastReactionTime = -60f;
            lastInterceptDigestTime = -60f;
            lastArmorDigestTime = -60f;
            lastWreckDigestTime = -60f;
            lastLedgerTime = -60f;
            pendingIntercepts = 0;
            pendingArmorLosses = 0;
            pendingWrecks = 0;
            aircraftLost = 0;
            armorLost = 0;
            missilesIntercepted = 0;
            firstBloodSeen = false;
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

        private void CountRoutine(HeadlineItem item)
        {
            switch (item.Tag)
            {
                case "AIR DEFENSE":
                    pendingIntercepts++;
                    missilesIntercepted++;
                    break;
                case "COMBAT REPORT":
                    pendingArmorLosses++;
                    armorLost++;
                    break;
                default:
                    pendingWrecks++;
                    break;
            }
        }

        private void RegisterAirKill(string attacker, float now)
        {
            if (string.IsNullOrEmpty(attacker)) return;

            if (!streaks.TryGetValue(attacker, out StreakTrack track))
            {
                if (streaks.Count >= MaxStreakTracks) PruneStreaks(now);
                track = new StreakTrack();
                streaks[attacker] = track;
            }

            if (now - track.LastKill > StreakWindowSeconds) track.Kills = 0;
            track.LastKill = now;
            track.Kills++;

            if (!firstBloodSeen)
            {
                firstBloodSeen = true;
                Enqueue(new HeadlineItem(
                    "WIRE FOLLOW-UP",
                    "#88C0AA",
                    "NEUTRAL OBSERVERS CONFIRM OPENING SHOTS EXCHANGED ALONG THE FRONT"));
                PushUrgent(new HeadlineItem(
                    "FLASH",
                    "#FF4444",
                    "OPENING SHOTS OF THE THEATER CONFIRMED — FIRST AIR-TO-AIR KILL LOGGED",
                    Priority.Major), now);
            }

            if (track.Kills == 3 || track.Kills == 5)
            {
                string headline = track.Kills == 3
                    ? $"{attacker.ToUpperInvariant()} ON A TEAR — 3 CONFIRMED SPLASHES IN UNDER 90 SECONDS"
                    : $"{attacker.ToUpperInvariant()} REACHES ACE STATUS — 5 CONFIRMED SPLASHES";
                PushUrgent(new HeadlineItem("ACE WATCH", "#FF9900", headline, Priority.Major), now);
            }
        }

        private void PruneStreaks(float now)
        {
            var stale = new List<string>();
            foreach (KeyValuePair<string, StreakTrack> pair in streaks)
            {
                if (now - pair.Value.LastKill > StreakWindowSeconds) stale.Add(pair.Key);
            }
            for (int i = 0; i < stale.Count; i++) streaks.Remove(stale[i]);

            while (streaks.Count >= MaxStreakTracks)
            {
                foreach (string key in streaks.Keys)
                {
                    streaks.Remove(key);
                    break;
                }
            }
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

        private static string CleanTags(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
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
