using System;

namespace BoscaliSummer.Features.Support.Domain.Cyber
{
    /// <summary>Wire-stable threat kind on the THREAT BOARD.</summary>
    internal enum IncidentKind : byte
    {
        None = 0,

        /// <summary>The adversary maps your emitters; unblocked, it exposes them to that faction.</summary>
        Probe = 1,

        /// <summary>A break-in that walks your links toward Cyber Command, compromising sites.</summary>
        Intrusion = 2,

        /// <summary>A sector barrage: links and radar cover inside it are halved.</summary>
        Raid = 3,

        /// <summary>An enemy player's cyber operation heard by your SIGINT; traceable while it lasts.</summary>
        HostileOperation = 4
    }

    internal enum IncidentOutcome : byte
    {
        Active = 0,
        Blocked = 1,
        Exposed = 2,
        Contained = 3,
        Traced = 4,
        Withdrew = 5,
        Broken = 6,
        Faded = 7
    }

    /// <summary>What happened, for the voice loop and the host log. Wire-stable.</summary>
    internal enum CyberNotice : byte
    {
        None = 0,
        SiteOnline,
        SiteLost,
        CommandLost,
        ProbeDetected,
        ProbeBlocked,
        ProbeExposed,
        IntrusionDetected,
        SiteCompromised,
        CommandCompromised,
        IntrusionStalled,
        IntrusionContained,
        IntrusionWithdrew,
        TraceStarted,
        TraceComplete,
        RaidStarted,
        RaidBroken,
        RaidFaded,
        HostileOperation,
        Patched,
        Isolated,
        Rejoined,
        Baited,
        PhaseRaised,
        SeekerDefeated,
        SelfRepaired,
        PhaseEased,
        CommandUp,
        GatewayJoined,
        GatewayLost,
        NodeDown,
        NodeRestored,
        CommandMoved
    }

    /// <summary>Escalation of the adversary campaign.</summary>
    internal enum CampaignPhase : byte
    {
        Probing = 0,
        Active = 1,
        Offensive = 2
    }

    internal struct CyberIncident
    {
        public IncidentKind Kind;
        public IncidentOutcome Outcome;

        /// <summary>Slot the incident sits on (entry, current hop or probe target); -1 for none.</summary>
        public int Site;

        /// <summary>Index into the host's enemy-faction table.</summary>
        public byte Origin;

        public float X, Z;
        public double Started;
        public double Ends;
        public double NextStep;
        public double StallSince;
        public double ResolvedAt;
        public float Trace;
        public bool Tracing;

        /// <summary>Caught by bait: held (and traced faster) until contained, even after the bait lapses.</summary>
        public bool Held;
    }

    /// <summary>
    /// The adversary campaign against one network. Heat builds with match time and with the
    /// faction's own offensive operations; it sets the phase, and the phase sets what the
    /// adversary tries and how often. Deterministic from the seed so tests can drive it.
    /// </summary>
    internal sealed partial class CyberNetwork
    {
        public const int IncidentSlots = 6;
        public const int MaximumOrigins = 8;
        public const int NoticeSlots = 6;
        public const float HeatMaximum = 100f;
        public const float ActiveHeat = 30f;
        public const float OffensiveHeat = 65f;
        public const float HeatPerSecond = 1f / 20f;
        public const float OffensiveHeatSpike = 8f;
        public const float ProbeSeconds = 20f;
        public const float ExposureSeconds = 90f;
        public const float IntrusionLanding = 20f;
        public const float SpreadSeconds = 26f;
        public const float OffensiveSpreadSeconds = 16f;
        public const float SelfRepairSeconds = 120f;
        public const float DefendedRelief = 4f;
        public const float TracedRelief = 8f;
        public const double SeekerNoticeGap = 4.0;
        public const CyberNotice LastNotice = CyberNotice.CommandMoved;
        public const float IntrusionLifetime = 240f;
        public const float ContainSeconds = 40f;
        public const float TraceSeconds = 20f;
        public const float FootholdSeconds = 240f;
        public const float RaidSeconds = 90f;
        public const float RaidRadius = 8000f;
        public const float HostileSeconds = 45f;
        public const double ResolvedLinger = 90.0;
        public const float FirstIncidentDelay = 90f;

        private readonly CyberIncident[] incidents = new CyberIncident[IncidentSlots];
        private readonly double[] footholdUntil = new double[MaximumOrigins];
        private readonly CyberNotice[] noticeKind = new CyberNotice[NoticeSlots];
        private readonly int[] noticeSite = new int[NoticeSlots];
        private readonly byte[] noticeOrigin = new byte[NoticeSlots];
        private int noticeCount;
        private double nextIncident;
        private uint rng = 0x9E3779B9u;

        public float Heat { get; private set; }

        /// <summary>Incidents the defender won (blocked, contained, traced, burned through).</summary>
        public int Defended { get; private set; }

        /// <summary>Incidents the adversary won (emitters exposed, intruder left with what it took).</summary>
        public int Breached { get; private set; }
        public double ExposedUntil { get; private set; }
        public byte ExposedOrigin { get; private set; }
        public int NoticeSerial { get; private set; }
        public double NextIncident => nextIncident;

        public CampaignPhase Phase =>
            Heat >= OffensiveHeat ? CampaignPhase.Offensive : Heat >= ActiveHeat ? CampaignPhase.Active : CampaignPhase.Probing;

        public void Seed(int seed) => rng = seed == 0 ? 0x9E3779B9u : (uint)seed;

        public CyberIncident Incident(int index) =>
            index >= 0 && index < IncidentSlots ? incidents[index] : default;

        public bool IncidentActive(int index) =>
            index >= 0 && index < IncidentSlots && incidents[index].Kind != IncidentKind.None &&
            incidents[index].Outcome == IncidentOutcome.Active;

        public int ActiveIncidents(IncidentKind kind)
        {
            int count = 0;
            for (int i = 0; i < IncidentSlots; i++)
                if (IncidentActive(i) && (kind == IncidentKind.None || incidents[i].Kind == kind)) count++;
            return count;
        }

        /// <summary>A good outcome for the defender.</summary>
        public static bool Won(IncidentOutcome outcome) =>
            outcome == IncidentOutcome.Blocked || outcome == IncidentOutcome.Contained ||
            outcome == IncidentOutcome.Traced || outcome == IncidentOutcome.Broken;

        /// <summary>A win for the adversary; fading raids and unheard operations count for nobody.</summary>
        public static bool Lost(IncidentOutcome outcome) =>
            outcome == IncidentOutcome.Exposed || outcome == IncidentOutcome.Withdrew;

        public static bool Traceable(IncidentKind kind) => kind == IncidentKind.Intrusion || kind == IncidentKind.HostileOperation;

        public bool FootholdOn(int origin, double now) =>
            origin >= 0 && origin < MaximumOrigins && footholdUntil[origin] > now;

        public float FootholdRemaining(int origin, double now) =>
            origin >= 0 && origin < MaximumOrigins ? (float)Math.Max(0.0, footholdUntil[origin] - now) : 0f;

        public bool AnyFoothold(double now)
        {
            for (int i = 0; i < MaximumOrigins; i++)
                if (footholdUntil[i] > now) return true;
            return false;
        }

        /// <summary>Inside an active jamming raid.</summary>
        public bool Jammed(int slot, double now)
        {
            if (!Exists(slot)) return false;
            for (int i = 0; i < IncidentSlots; i++)
            {
                if (!IncidentActive(i) || incidents[i].Kind != IncidentKind.Raid || now >= incidents[i].Ends) continue;
                float dx = sites[slot].X - incidents[i].X, dz = sites[slot].Z - incidents[i].Z;
                if (dx * dx + dz * dz <= RaidRadius * RaidRadius) return true;
            }
            return false;
        }

        /// <summary>1 (normal) to 5 (grave): INFOCON reads 5 minus this, never below 1.</summary>
        public int Infocon
        {
            get
            {
                int pressure = 0;
                for (int i = 0; i < IncidentSlots; i++)
                {
                    if (!IncidentActive(i)) continue;
                    pressure += incidents[i].Kind == IncidentKind.Intrusion ? 2 : 1;
                }
                for (int i = 0; i < SlotCount; i++)
                    if (sites[i].Compromised && !sites[i].Lost) pressure++;
                if (CommandCompromised) pressure += 2;
                return Math.Max(1, 5 - pressure);
            }
        }

        /// <summary>The faction ran an offensive operation: the adversary notices.</summary>
        public void NoteOffensive() => Heat = Math.Min(HeatMaximum, Heat + OffensiveHeatSpike);

        /// <summary>
        /// Host: an enemy player's operation landed near (x, z). Heard only under a working
        /// SIGINT post; heard, it becomes a traceable incident. Returns whether it was heard.
        /// </summary>
        public bool ReportHostile(byte origin, float x, float z, double now)
        {
            if (!HasCommand || origin >= MaximumOrigins || !SigintCovers(x, z, now)) return false;
            int slot = FreeIncident(now);
            if (slot < 0) return false;
            incidents[slot] = new CyberIncident
            {
                Kind = IncidentKind.HostileOperation,
                Site = NearestSite(x, z),
                Origin = origin,
                X = x,
                Z = z,
                Started = now,
                Ends = now + HostileSeconds
            };
            Notify(CyberNotice.HostileOperation, incidents[slot].Site, origin, now);
            return true;
        }

        /// <summary>Test and debug seam: open an incident now instead of waiting for the campaign.</summary>
        public int Force(IncidentKind kind, double now)
        {
            byte origin = (byte)(OriginCount > 0 ? Next(OriginCount) : 0);
            return Spawn(kind, origin, now);
        }

        // ---- Tick ---------------------------------------------------------------------------

        private void TickCampaign(double now, float dt, float intensity)
        {
            for (int i = 0; i < IncidentSlots; i++)
            {
                if (incidents[i].Kind == IncidentKind.None) continue;
                if (incidents[i].Outcome != IncidentOutcome.Active)
                {
                    if (now - incidents[i].ResolvedAt >= ResolvedLinger) incidents[i] = default;
                    continue;
                }
                StepIncident(i, now, dt);
            }

            // Airbase nodes come up by themselves, so the adversary only starts working once the
            // faction fields a truck: the defence game stays opt-in for players who ignore CYBER.
            bool running = intensity > 0f && OriginCount > 0 && CommandOnline && FieldCount > 0;
            if (!running)
            {
                nextIncident = 0.0;
                return;
            }

            CampaignPhase before = Phase;
            Heat = Math.Min(HeatMaximum, Heat + HeatPerSecond * intensity * dt);
            if (Phase != before) Notify(CyberNotice.PhaseRaised, -1, 0, now);

            if (nextIncident <= 0.0)
            {
                nextIncident = now + FirstIncidentDelay / intensity;
                return;
            }
            if (now < nextIncident) return;
            nextIncident = now + Interval(Phase) / intensity;

            IncidentKind kind = Choose(Phase);
            if (kind == IncidentKind.None) return;
            Spawn(kind, (byte)Next(Math.Min(OriginCount, MaximumOrigins)), now);
        }

        private float Interval(CampaignPhase phase)
        {
            switch (phase)
            {
                case CampaignPhase.Offensive: return 60f + Next(40);
                case CampaignPhase.Active: return 90f + Next(60);
                default: return 150f + Next(90);
            }
        }

        private IncidentKind Choose(CampaignPhase phase)
        {
            if (phase == CampaignPhase.Probing) return IncidentKind.Probe;
            int roll = Next(100);
            int intrusionCap = phase == CampaignPhase.Offensive ? 2 : 1;
            bool intrusionRoom = ActiveIncidents(IncidentKind.Intrusion) < intrusionCap;
            bool raidRoom = ActiveIncidents(IncidentKind.Raid) < 1;
            if (phase == CampaignPhase.Active)
            {
                if (roll < 35) return IncidentKind.Probe;
                if (roll < 75) return intrusionRoom ? IncidentKind.Intrusion : IncidentKind.Probe;
                return raidRoom ? IncidentKind.Raid : IncidentKind.Probe;
            }
            if (roll < 15) return IncidentKind.Probe;
            if (roll < 70) return intrusionRoom ? IncidentKind.Intrusion : raidRoom ? IncidentKind.Raid : IncidentKind.Probe;
            return raidRoom ? IncidentKind.Raid : intrusionRoom ? IncidentKind.Intrusion : IncidentKind.Probe;
        }

        private int Spawn(IncidentKind kind, byte origin, double now)
        {
            int entry;
            switch (kind)
            {
                case IncidentKind.Probe: entry = PickSite(emitterOnly: true, edge: false, clean: true); break;
                case IncidentKind.Intrusion: entry = PickSite(emitterOnly: false, edge: true, clean: true); break;
                case IncidentKind.Raid: entry = PickSite(emitterOnly: false, edge: false, clean: false); break;
                default: return -1;
            }
            if (entry < 0) return -1;
            int slot = FreeIncident(now);
            if (slot < 0) return -1;

            var incident = new CyberIncident
            {
                Kind = kind,
                Site = entry,
                Origin = origin,
                X = sites[entry].X,
                Z = sites[entry].Z,
                Started = now
            };
            switch (kind)
            {
                case IncidentKind.Probe:
                    incident.Ends = now + ProbeSeconds;
                    Notify(CyberNotice.ProbeDetected, entry, origin, now);
                    break;
                case IncidentKind.Intrusion:
                    incident.Ends = now + IntrusionLifetime;
                    incident.NextStep = now + IntrusionLanding;
                    Notify(CyberNotice.IntrusionDetected, entry, origin, now);
                    break;
                default:
                    // Offset so the barrage reads as a sector, not a bullseye on one site.
                    float angle = Next(360) * (float)(Math.PI / 180.0);
                    float offset = 1500f + Next(3000);
                    incident.X += (float)Math.Cos(angle) * offset;
                    incident.Z += (float)Math.Sin(angle) * offset;
                    incident.Ends = now + RaidSeconds;
                    incident.Site = -1;
                    Notify(CyberNotice.RaidStarted, entry, origin, now);
                    break;
            }
            incidents[slot] = incident;
            if (kind == IncidentKind.Raid) RebuildLinks(now);
            return slot;
        }

        private void StepIncident(int index, double now, float dt)
        {
            ref CyberIncident incident = ref incidents[index];

            if (incident.Tracing && Traceable(incident.Kind))
            {
                if (AnyWorking(CyberSiteKind.Sigint))
                {
                    float rate = 1f / TraceSeconds;
                    if (incident.Held) rate *= 2f;
                    if (Count(CyberSiteKind.Sigint) > 1) rate *= 1.25f;
                    incident.Trace = Math.Min(1f, incident.Trace + rate * dt);
                }
                if (incident.Trace >= 1f)
                {
                    if (incident.Origin < MaximumOrigins) footholdUntil[incident.Origin] = now + FootholdSeconds;
                    Resolve(index, IncidentOutcome.Traced, now);
                    Notify(CyberNotice.TraceComplete, incident.Site, incident.Origin, now);
                    return;
                }
            }

            switch (incident.Kind)
            {
                case IncidentKind.Probe:
                    if (now < incident.Ends) return;
                    if (SigintCovers(incident.X, incident.Z, now))
                    {
                        Resolve(index, IncidentOutcome.Blocked, now);
                        Notify(CyberNotice.ProbeBlocked, incident.Site, incident.Origin, now);
                    }
                    else
                    {
                        ExposedUntil = now + ExposureSeconds;
                        ExposedOrigin = incident.Origin;
                        Resolve(index, IncidentOutcome.Exposed, now);
                        Notify(CyberNotice.ProbeExposed, incident.Site, incident.Origin, now);
                    }
                    return;

                case IncidentKind.Raid:
                    if (now < incident.Ends) return;
                    Resolve(index, IncidentOutcome.Faded, now);
                    Notify(CyberNotice.RaidFaded, -1, incident.Origin, now);
                    RebuildLinks(now);
                    return;

                case IncidentKind.HostileOperation:
                    if (now < incident.Ends) return;
                    Resolve(index, IncidentOutcome.Faded, now);
                    return;

                case IncidentKind.Intrusion:
                    StepIntrusion(index, ref incident, now);
                    return;
            }
        }

        private void StepIntrusion(int index, ref CyberIncident incident, double now)
        {
            if (now >= incident.Ends)
            {
                Resolve(index, IncidentOutcome.Withdrew, now);
                Notify(CyberNotice.IntrusionWithdrew, incident.Site, incident.Origin, now);
                return;
            }
            if (!Exists(incident.Site) || sites[incident.Site].Lost)
            {
                // The foothold burned with the site.
                Resolve(index, IncidentOutcome.Contained, now);
                Notify(CyberNotice.IntrusionContained, incident.Site, incident.Origin, now);
                return;
            }

            CyberSite here = sites[incident.Site];
            if (here.HoneypotUntil > now) incident.Held = true;
            bool stalled = here.Isolated || incident.Held;
            if (stalled)
            {
                if (incident.StallSince <= 0.0)
                {
                    incident.StallSince = now;
                    Notify(CyberNotice.IntrusionStalled, incident.Site, incident.Origin, now);
                }
                if (now - incident.StallSince >= ContainSeconds)
                {
                    Resolve(index, IncidentOutcome.Contained, now);
                    Notify(CyberNotice.IntrusionContained, incident.Site, incident.Origin, now);
                }
                return;
            }
            incident.StallSince = 0.0;
            if (now < incident.NextStep) return;
            incident.NextStep = now + (Phase == CampaignPhase.Offensive ? OffensiveSpreadSeconds : SpreadSeconds);

            if (!here.Compromised && here.PatchDone <= 0.0)
            {
                sites[incident.Site].Compromised = true;
                sites[incident.Site].CompromisedAt = now;
                bool command = here.Kind == CyberSiteKind.Command;
                Notify(command ? CyberNotice.CommandCompromised : CyberNotice.SiteCompromised, incident.Site,
                    incident.Origin, now);
                return;
            }

            // Walk one link toward Cyber Command; bait on the way is taken.
            int best = -1, bestHops = int.MaxValue;
            for (int to = 0; to < SlotCount; to++)
            {
                if (!links[incident.Site, to] || sites[to].Compromised) continue;
                int distance = onNet[to] ? hops[to] : SlotCount;
                if (sites[to].HoneypotUntil > now) distance = -1;
                if (distance < bestHops)
                {
                    best = to;
                    bestHops = distance;
                }
            }
            if (best < 0) return;
            incident.Site = best;
            incident.X = sites[best].X;
            incident.Z = sites[best].Z;
            incident.NextStep = now + IntrusionLanding;
        }

        private void Resolve(int index, IncidentOutcome outcome, double now)
        {
            incidents[index].Outcome = outcome;
            incidents[index].Tracing = false;
            incidents[index].ResolvedAt = now;
            if (Lost(outcome)) Breached++;
            if (!Won(outcome)) return;
            // Winning pushes the adversary back: the player steers the escalation.
            Defended++;
            CampaignPhase before = Phase;
            Heat = Math.Max(0f, Heat - (outcome == IncidentOutcome.Traced ? TracedRelief : DefendedRelief));
            if (Phase != before) Notify(CyberNotice.PhaseEased, -1, 0, now);
        }

        /// <summary>
        /// A compromised field site the adversary has left alone is reimaged by the watch floor
        /// after a while, so a network left unattended degrades instead of dying. Cyber Command
        /// itself never heals on its own.
        /// </summary>
        private void SelfRepair(double now)
        {
            if (CommandCompromised) return;
            for (int i = 0; i < SlotCount; i++)
            {
                CyberSite site = sites[i];
                if (!site.Compromised || site.Lost || site.Kind == CyberSiteKind.Command || site.PatchDone > 0.0) continue;
                if (now - site.CompromisedAt < SelfRepairSeconds || IntrusionAt(i)) continue;
                sites[i].Compromised = false;
                Notify(CyberNotice.SelfRepaired, i, 0, now);
            }
        }

        private bool IntrusionAt(int slot)
        {
            for (int i = 0; i < IncidentSlots; i++)
                if (IncidentActive(i) && incidents[i].Kind == IncidentKind.Intrusion && incidents[i].Site == slot) return true;
            return false;
        }

        private double nextSeekerNotice;

        /// <summary>Host: a jammer pushed a hostile radar seeker past its tolerance.</summary>
        public void NoteSeekerDefeated(int slot, double now)
        {
            SeekersDefeated++;
            if (now < nextSeekerNotice) return;
            nextSeekerNotice = now + SeekerNoticeGap;
            Notify(CyberNotice.SeekerDefeated, slot, 0, now);
        }

        private int FreeIncident(double now)
        {
            int oldest = -1;
            for (int i = 0; i < IncidentSlots; i++)
            {
                if (incidents[i].Kind == IncidentKind.None) return i;
                if (incidents[i].Outcome == IncidentOutcome.Active) continue;
                if (oldest < 0 || incidents[i].ResolvedAt < incidents[oldest].ResolvedAt) oldest = i;
            }
            return oldest;
        }

        /// <summary>A live site to open an incident on. Edge sites have the fewest links; probes
        /// want something that radiates; break-ins want a site they do not own yet. Cyber
        /// Command is never the entry point.</summary>
        private int PickSite(bool emitterOnly, bool edge, bool clean)
        {
            int candidates = 0;
            int fewest = int.MaxValue;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Eligible(i, emitterOnly, clean)) continue;
                candidates++;
                if (edge) fewest = Math.Min(fewest, Degree(i));
            }
            if (candidates == 0) return -1;
            int eligible = 0;
            for (int i = 0; i < SlotCount; i++)
                if (Eligible(i, emitterOnly, clean) && (!edge || Degree(i) == fewest)) eligible++;
            int pick = Next(eligible);
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Eligible(i, emitterOnly, clean) || (edge && Degree(i) != fewest)) continue;
                if (pick-- == 0) return i;
            }
            return -1;
        }

        private bool Eligible(int slot, bool emitterOnly, bool clean) =>
            Online(slot) && sites[slot].Kind != CyberSiteKind.Command && (!clean || !sites[slot].Compromised) &&
            (!emitterOnly || EmissionOf(slot) != Emission.Silent);

        private int Degree(int slot)
        {
            int degree = 0;
            for (int i = 0; i < SlotCount; i++)
                if (links[slot, i]) degree++;
            return degree;
        }

        private int NearestSite(float x, float z)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Online(i)) continue;
                float dx = sites[i].X - x, dz = sites[i].Z - z;
                float distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        private bool JammerReaches(float x, float z, float radius, double now)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (sites[i].Kind != CyberSiteKind.Jammer || !Working(i) || sites[i].Mode == EwPosture.SigintPassive)
                    continue;
                float reach = radius + CyberSites.Info(CyberSiteKind.Jammer).EffectRadius;
                float dx = sites[i].X - x, dz = sites[i].Z - z;
                if (dx * dx + dz * dz <= reach * reach) return true;
            }
            return false;
        }

        private void DropSiteReferences(int slot)
        {
            for (int i = 0; i < IncidentSlots; i++)
            {
                if (incidents[i].Kind == IncidentKind.None || incidents[i].Site != slot) continue;
                if (incidents[i].Outcome == IncidentOutcome.Active && incidents[i].Kind == IncidentKind.Probe)
                    Resolve(i, IncidentOutcome.Blocked, lastTick);
                if (incidents[i].Kind != IncidentKind.Intrusion) incidents[i].Site = -1;
            }
        }

        private int Next(int bound)
        {
            if (bound <= 1) return 0;
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return (int)(rng % (uint)bound);
        }

        // ---- Notices ------------------------------------------------------------------------

        public int NoticeCount => noticeCount;

        /// <summary>Newest first.</summary>
        public CyberNotice NoticeKind(int age) => age >= 0 && age < noticeCount ? noticeKind[age] : CyberNotice.None;
        public int NoticeSite(int age) => age >= 0 && age < noticeCount ? noticeSite[age] : -1;
        public byte NoticeOrigin(int age) => age >= 0 && age < noticeCount ? noticeOrigin[age] : (byte)0;

        private void Notify(CyberNotice kind, int site, byte origin, double now)
        {
            for (int i = NoticeSlots - 1; i > 0; i--)
            {
                noticeKind[i] = noticeKind[i - 1];
                noticeSite[i] = noticeSite[i - 1];
                noticeOrigin[i] = noticeOrigin[i - 1];
            }
            noticeKind[0] = kind;
            noticeSite[0] = site;
            noticeOrigin[0] = origin;
            noticeCount = Math.Min(NoticeSlots, noticeCount + 1);
            NoticeSerial++;
        }

        private void ClearCampaign()
        {
            Array.Clear(incidents, 0, IncidentSlots);
            Array.Clear(footholdUntil, 0, MaximumOrigins);
            Array.Clear(noticeKind, 0, NoticeSlots);
            Array.Clear(noticeSite, 0, NoticeSlots);
            Array.Clear(noticeOrigin, 0, NoticeSlots);
            noticeCount = 0;
            NoticeSerial = 0;
            nextIncident = 0.0;
            Heat = 0f;
            Defended = Breached = 0;
            nextSeekerNotice = 0.0;
            ExposedUntil = 0.0;
            ExposedOrigin = 0;
        }

        private void ExportCampaign(double now, CyberSnapshot into)
        {
            into.Heat = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(Heat)));
            into.Defended = Defended;
            into.Breached = Breached;
            into.NextIncidentIn = nextIncident > now ? (float)(nextIncident - now) : 0f;
            into.ExposedIn = ExposedUntil > now ? (float)(ExposedUntil - now) : 0f;
            int count = 0;
            for (int i = 0; i < IncidentSlots; i++)
            {
                CyberIncident incident = incidents[i];
                if (incident.Kind == IncidentKind.None) continue;
                into.IncidentKind[count] = (byte)incident.Kind;
                into.IncidentState[count] = (byte)((byte)incident.Outcome | (incident.Tracing ? 0x80 : 0) |
                                                   (incident.Held ? 0x40 : 0));
                into.IncidentSite[count] = incident.Site < 0 ? (byte)255 : (byte)incident.Site;
                into.IncidentOrigin[count] = incident.Origin;
                into.IncidentX[count] = incident.X;
                into.IncidentZ[count] = incident.Z;
                into.IncidentAge[count] = (float)Math.Max(0.0, now - incident.Started);
                into.IncidentLeft[count] = incident.Outcome == IncidentOutcome.Active
                    ? (float)Math.Max(0.0, incident.Ends - now)
                    : -(float)Math.Max(0.0, now - incident.ResolvedAt);
                into.IncidentTrace[count] = (byte)Math.Round(Math.Max(0f, Math.Min(1f, incident.Trace)) * 255f);
                count++;
            }
            into.IncidentCount = (byte)count;
            for (int i = 0; i < MaximumOrigins; i++)
                into.Foothold[i] = (byte)Math.Min(255.0, Math.Ceiling(Math.Max(0.0, footholdUntil[i] - now)));
            into.NoticeSerial = NoticeSerial;
            into.NoticeCount = (byte)noticeCount;
            for (int i = 0; i < noticeCount; i++)
            {
                into.NoticeKind[i] = (byte)noticeKind[i];
                into.NoticeSite[i] = noticeSite[i] < 0 ? (byte)255 : (byte)noticeSite[i];
                into.NoticeOrigin[i] = noticeOrigin[i];
            }
        }

        private void MirrorCampaign(CyberSnapshot from, double now)
        {
            Heat = Math.Min(HeatMaximum, from.Heat);
            Defended = Math.Max(0, from.Defended);
            Breached = Math.Max(0, from.Breached);
            nextIncident = from.NextIncidentIn > 0f && Finite(from.NextIncidentIn)
                ? now + Math.Min(from.NextIncidentIn, 3600f) : 0.0;
            ExposedUntil = Rebase(ExposedUntil, from.ExposedIn, now, ExposureSeconds);

            Array.Clear(incidents, 0, IncidentSlots);
            int count = Math.Min((int)from.IncidentCount, IncidentSlots);
            for (int i = 0; i < count; i++)
            {
                byte kind = from.IncidentKind[i];
                byte outcome = (byte)(from.IncidentState[i] & 0x3F);
                if (kind < (byte)IncidentKind.Probe || kind > (byte)IncidentKind.HostileOperation ||
                    outcome > (byte)IncidentOutcome.Faded || !Finite(from.IncidentX[i]) || !Finite(from.IncidentZ[i]) ||
                    !Finite(from.IncidentAge[i]) || !Finite(from.IncidentLeft[i]))
                    continue;
                float left = from.IncidentLeft[i];
                incidents[i] = new CyberIncident
                {
                    Kind = (IncidentKind)kind,
                    Outcome = (IncidentOutcome)outcome,
                    Tracing = (from.IncidentState[i] & 0x80) != 0,
                    Held = (from.IncidentState[i] & 0x40) != 0,
                    Site = from.IncidentSite[i] < SlotCount ? from.IncidentSite[i] : -1,
                    Origin = (byte)Math.Min((int)from.IncidentOrigin[i], MaximumOrigins - 1),
                    X = from.IncidentX[i],
                    Z = from.IncidentZ[i],
                    Started = now - Math.Min(Math.Max(0f, from.IncidentAge[i]), 3600f),
                    Ends = left > 0f ? now + Math.Min(left, 3600f) : now,
                    ResolvedAt = left < 0f ? now + Math.Max(left, -3600f) : 0.0,
                    Trace = from.IncidentTrace[i] / 255f
                };
            }
            for (int i = 0; i < MaximumOrigins; i++)
                footholdUntil[i] = Rebase(footholdUntil[i], from.Foothold[i], now, FootholdSeconds + 1f);

            noticeCount = Math.Min((int)from.NoticeCount, NoticeSlots);
            for (int i = 0; i < NoticeSlots; i++)
            {
                bool live = i < noticeCount && from.NoticeKind[i] <= (byte)LastNotice;
                noticeKind[i] = live ? (CyberNotice)from.NoticeKind[i] : CyberNotice.None;
                noticeSite[i] = live && from.NoticeSite[i] < SlotCount ? from.NoticeSite[i] : -1;
                noticeOrigin[i] = live ? from.NoticeOrigin[i] : (byte)0;
            }
            NoticeSerial = from.NoticeSerial;
        }
    }
}
