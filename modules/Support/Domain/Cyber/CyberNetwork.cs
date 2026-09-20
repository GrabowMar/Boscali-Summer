using System;

namespace BoscaliSummer.Features.Support.Domain.Cyber
{
    /// <summary>What an operator can do to a site or an incident from the console. Wire-stable.</summary>
    internal enum CyberVerb : byte
    {
        /// <summary>Toggle: cut a site's links. Stops an intrusion there; the site stops working.</summary>
        Isolate = 0,

        /// <summary>Channel a clean image onto a compromised site.</summary>
        Patch = 1,

        /// <summary>Dress a site as bait: an intrusion that reaches it stalls and traces faster.</summary>
        Honeypot = 2,

        /// <summary>Follow an intrusion or detected operation back to its origin. Needs SIGINT.</summary>
        Trace = 3,

        /// <summary>Overpower a jamming raid with a jammer inside or beside it.</summary>
        BurnThrough = 4
    }

    /// <summary>Why a verb or a site order is refused, in check order.</summary>
    internal enum CyberDenial : byte
    {
        None = 0,
        NoCommand,
        NoTarget,
        Deploying,
        Lost,
        OffNet,
        CommandProtected,
        NotCompromised,
        AlreadyPatching,
        AlreadyBaited,
        NeedsSigint,
        NeedsJammer,
        NotTraceable,
        AlreadyTracing,
        LowBandwidth,
        Recharging
    }

    /// <summary>Why a site cannot be ordered.</summary>
    internal enum SitePlacement : byte
    {
        None = 0,
        NeedsCommand,
        CopyLimit,
        NetworkFull,
        NotPlaceable
    }

    /// <summary>One slot of the network. Kind None is a free slot.</summary>
    internal struct CyberSite
    {
        public CyberSiteKind Kind;
        public float X, Z;

        /// <summary>The site's vehicle is still driving to its mark.</summary>
        public bool Deploying;

        /// <summary>The site's vehicle was destroyed; the slot frees shortly after.</summary>
        public bool Lost;

        public bool Isolated;
        public bool Compromised;

        /// <summary>Jammer mode; meaningless for other kinds.</summary>
        public EwPosture Mode;

        /// <summary>When a running patch completes; 0 when none.</summary>
        public double PatchDone;

        public double HoneypotUntil;
        public double CompromisedAt;
        public double LostAt;
        public float Paid;

        /// <summary>Airbase infrastructure (Cyber Command or a gateway): raised and removed by the host
        /// with the base, linked to every other static node by the backbone.</summary>
        public bool Static;

        /// <summary>A static node whose anchor building on the airbase is destroyed: offline until repaired.</summary>
        public bool Down;

        /// <summary>Host identity of the airbase behind a static node. Never on the wire.</summary>
        public int Anchor;
    }

    /// <summary>Network totals the panel and the console show.</summary>
    internal readonly struct CyberStats
    {
        public readonly int Sites;
        public readonly int OnNet;
        public readonly int Links;
        public readonly float Produced;
        public readonly float Drawn;
        public readonly float Capacity;

        public CyberStats(int sites, int onNet, int links, float produced, float drawn, float capacity)
        {
            Sites = sites;
            OnNet = onNet;
            Links = links;
            Produced = produced;
            Drawn = drawn;
            Capacity = capacity;
        }

        public float Net => Produced - Drawn;
        public bool Congested => Drawn > Produced + 0.001f;
    }

    /// <summary>
    /// One faction's integrated spectrum-defence network: airbase infrastructure the host raises
    /// by itself (Cyber Command plus up to five gateways, wired to each other by the backbone),
    /// up to ten field sites linked by radio range to any node on the net, a bandwidth pool the
    /// console verbs draw on, and the adversary campaign run against it
    /// (<c>CyberNetwork.Campaign.cs</c>). Pure: the host owns every mutation and tells it which
    /// airbases it holds and where the site vehicles are; a client rebuilds it from
    /// <see cref="CyberSnapshot"/>s with relative clocks.
    /// </summary>
    internal sealed partial class CyberNetwork
    {
        public const int SlotCount = 16;

        /// <summary>Cyber Command plus the gateways.</summary>
        public const int StaticSlots = 1 + CyberSites.MaximumGateways;

        /// <summary>Slots left for field sites whatever the host limit says.</summary>
        public const int FieldSlots = SlotCount - StaticSlots;

        public const int VerbCount = 5;
        public const float BaseCapacity = 40f;
        public const float RelayCapacity = 10f;
        public const float GatewayCapacity = 5f;
        public const float IdleRegeneration = 0.25f;
        public const float PatchSeconds = 8f;
        public const float HoneypotSeconds = 60f;
        public const double LostLingerSeconds = 30.0;
        public const float OffNetScale = 0.5f;
        public const float CongestedScale = 0.75f;

        /// <summary>Bandwidth Cyber Command arrives with, so the first incident can be answered.</summary>
        public const float StarterBandwidth = 24f;

        private readonly CyberSite[] sites = new CyberSite[SlotCount];
        private readonly bool[] onNet = new bool[SlotCount];
        private readonly bool[,] links = new bool[SlotCount, SlotCount];
        private readonly int[] hops = new int[SlotCount];
        private readonly double[] verbReady = new double[VerbCount];
        private readonly int[] linkQueue = new int[SlotCount];
        private readonly bool[] mirrorSeen = new bool[SlotCount];
        private int linkCount;

        public float Bandwidth { get; private set; }

        /// <summary>Hostile radar seekers the jammers have broken this scene; host-counted.</summary>
        public int SeekersDefeated { get; private set; }

        /// <summary>Host-set: the enemy factions an incident may name, in snapshot order.</summary>
        public int OriginCount { get; set; }

        public static float VerbCost(CyberVerb verb)
        {
            switch (verb)
            {
                case CyberVerb.Isolate: return 8f;
                case CyberVerb.Patch: return 18f;
                case CyberVerb.Honeypot: return 14f;
                case CyberVerb.Trace: return 22f;
                default: return 20f;
            }
        }

        public static float VerbRecharge(CyberVerb verb)
        {
            switch (verb)
            {
                case CyberVerb.Isolate: return 4f;
                case CyberVerb.Patch: return 12f;
                case CyberVerb.Honeypot: return 25f;
                case CyberVerb.Trace: return 20f;
                default: return 25f;
            }
        }

        public static bool TargetsIncident(CyberVerb verb) => verb == CyberVerb.Trace || verb == CyberVerb.BurnThrough;

        // ---- Sites --------------------------------------------------------------------------

        public CyberSite Site(int slot) => slot >= 0 && slot < SlotCount ? sites[slot] : default;

        public bool Exists(int slot) => slot >= 0 && slot < SlotCount && sites[slot].Kind != CyberSiteKind.None;

        /// <summary>Built, arrived, alive and, for airbase nodes, with the anchor building standing.</summary>
        public bool Online(int slot) =>
            Exists(slot) && !sites[slot].Deploying && !sites[slot].Lost && !sites[slot].Down;

        public bool Static(int slot) => Exists(slot) && sites[slot].Static;

        /// <summary>Both ends are airbase nodes: the link is the backbone, not radio range.</summary>
        public bool Backbone(int a, int b) => Linked(a, b) && sites[a].Static && sites[b].Static;

        public bool OnNet(int slot) => slot >= 0 && slot < SlotCount && onNet[slot];

        public bool Linked(int a, int b) =>
            a >= 0 && b >= 0 && a < SlotCount && b < SlotCount && links[a, b];

        /// <summary>Hops from Cyber Command along live links; -1 off the net.</summary>
        public int Hops(int slot) => OnNet(slot) ? hops[slot] : -1;

        /// <summary>The site is doing its job: online, not isolated, not compromised.</summary>
        public bool Working(int slot) =>
            Online(slot) && !sites[slot].Isolated && !sites[slot].Compromised;

        public int CommandSlot
        {
            get
            {
                for (int i = 0; i < SlotCount; i++)
                    if (sites[i].Kind == CyberSiteKind.Command && !sites[i].Lost) return i;
                return -1;
            }
        }

        public bool HasCommand => CommandSlot >= 0;

        public bool CommandOnline
        {
            get
            {
                int slot = CommandSlot;
                return slot >= 0 && Online(slot);
            }
        }

        public bool CommandCompromised
        {
            get
            {
                int slot = CommandSlot;
                return slot >= 0 && sites[slot].Compromised;
            }
        }

        public int Count(CyberSiteKind kind)
        {
            int count = 0;
            for (int i = 0; i < SlotCount; i++)
                if (sites[i].Kind == kind && !sites[i].Lost) count++;
            return count;
        }

        public int SiteCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < SlotCount; i++)
                    if (sites[i].Kind != CyberSiteKind.None && !sites[i].Lost) count++;
                return count;
            }
        }

        /// <summary>Live field sites: the ones the host limit counts.</summary>
        public int FieldCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < SlotCount; i++)
                    if (sites[i].Kind != CyberSiteKind.None && !sites[i].Lost && !sites[i].Static) count++;
                return count;
            }
        }

        /// <summary>
        /// How strongly a site's passive effect runs, 0..1: nothing unless it is working, half
        /// off the net or inside a jamming raid, three quarters on a congested net.
        /// </summary>
        public float EffectScale(int slot, double now)
        {
            if (!Working(slot)) return 0f;
            float scale = onNet[slot] ? 1f : OffNetScale;
            if (Jammed(slot, now)) scale *= 0.5f;
            if (Stats().Congested) scale *= CongestedScale;
            return scale;
        }

        public float EffectRadius(int slot, double now) =>
            Exists(slot) ? CyberSites.Info(sites[slot].Kind).EffectRadius * (Jammed(slot, now) ? 0.5f : 1f) : 0f;

        /// <summary>Whether a field site of this kind may be ordered. <paramref name="limit"/> is the
        /// host's field-site limit; airbase infrastructure never counts against it.</summary>
        public SitePlacement CheckPlacement(CyberSiteKind kind, int limit)
        {
            if (!CyberSites.Fieldable((byte)kind)) return SitePlacement.NotPlaceable;
            if (!HasCommand) return SitePlacement.NeedsCommand;
            if (Count(kind) >= CyberSites.Info(kind).CopyLimit) return SitePlacement.CopyLimit;
            int cap = Math.Max(1, Math.Min(FieldSlots, limit));
            if (FieldCount >= cap || FreeSlot() < 0) return SitePlacement.NetworkFull;
            return SitePlacement.None;
        }

        /// <summary>Orders a field site; its vehicle is en route until <see cref="SetPosition"/> says it arrived.</summary>
        public int TryBuild(CyberSiteKind kind, float x, float z, float paid, int limit)
        {
            if (CheckPlacement(kind, limit) != SitePlacement.None || !Finite(x) || !Finite(z)) return -1;
            int slot = FreeSlot();
            sites[slot] = new CyberSite
            {
                Kind = kind,
                X = x,
                Z = z,
                Deploying = true,
                Mode = EwPostures.Default,
                Paid = Math.Max(0f, paid)
            };
            RebuildLinks(0.0);
            return slot;
        }

        /// <summary>Removes a live field site; returns false for an empty, lost or airbase slot.</summary>
        public bool TryScrap(int slot, out CyberSiteKind kind, out float paid)
        {
            kind = CyberSiteKind.None;
            paid = 0f;
            if (!Exists(slot) || sites[slot].Lost || sites[slot].Static) return false;
            kind = sites[slot].Kind;
            paid = sites[slot].Paid;
            sites[slot] = default;
            DropSiteReferences(slot);
            RebuildLinks(0.0);
            return true;
        }

        /// <summary>Host: the site's vehicle position this tick.</summary>
        public void SetPosition(int slot, float x, float z, bool arrived)
        {
            if (!Exists(slot) || sites[slot].Lost || !Finite(x) || !Finite(z)) return;
            sites[slot].X = x;
            sites[slot].Z = z;
            if (arrived && sites[slot].Deploying)
            {
                sites[slot].Deploying = false;
                if (sites[slot].Kind == CyberSiteKind.Command && Bandwidth < StarterBandwidth)
                    Bandwidth = StarterBandwidth;
                Notify(CyberNotice.SiteOnline, slot, 0, lastTick);
            }
        }

        /// <summary>Host: the site starts driving to a new mark. It is off the net until it arrives.</summary>
        public bool TryRelocate(int slot)
        {
            if (!Online(slot) || sites[slot].Static) return false;
            sites[slot].Deploying = true;
            sites[slot].PatchDone = 0.0;
            RebuildLinks(lastTick);
            return true;
        }

        /// <summary>Host: the site's vehicle is gone.</summary>
        public void MarkLost(int slot, double now)
        {
            if (!Exists(slot) || sites[slot].Lost) return;
            sites[slot].Lost = true;
            sites[slot].LostAt = now;
            sites[slot].PatchDone = 0.0;
            sites[slot].HoneypotUntil = 0.0;
            Notify(sites[slot].Kind == CyberSiteKind.Command ? CyberNotice.CommandLost : CyberNotice.SiteLost, slot, 0, now);
            RebuildLinks(now);
        }

        public bool TrySetMode(int slot, EwPosture mode)
        {
            if (!Online(slot) || sites[slot].Kind != CyberSiteKind.Jammer || (byte)mode > (byte)EwPosture.GhostSpoofing)
                return false;
            sites[slot].Mode = mode;
            return true;
        }

        /// <summary>How loud the site is right now; a jammer in EMCON is silent.</summary>
        public Emission EmissionOf(int slot)
        {
            if (!Online(slot) || sites[slot].Isolated) return Emission.Silent;
            CyberSite site = sites[slot];
            if (site.Kind == CyberSiteKind.Jammer && site.Mode == EwPosture.SigintPassive) return Emission.Silent;
            return CyberSites.Info(site.Kind).Emission;
        }

        /// <summary>A working jammer that is emitting (not in EMCON) within <paramref name="radius"/> of a point.</summary>
        public bool EmittingJammerCovers(float x, float z, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!EmittingJammer(i)) continue;
                float dx = sites[i].X - x, dz = sites[i].Z - z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        public bool AnyWorking(CyberSiteKind kind)
        {
            for (int i = 0; i < SlotCount; i++)
                if (sites[i].Kind == kind && Working(i)) return true;
            return false;
        }

        /// <summary>Any working jammer in NOISE or DECEPTION: what backs a station operation.</summary>
        public bool AnyEmittingJammer()
        {
            for (int i = 0; i < SlotCount; i++)
                if (EmittingJammer(i)) return true;
            return false;
        }

        private bool EmittingJammer(int slot) =>
            sites[slot].Kind == CyberSiteKind.Jammer && Working(slot) && EwPostures.Emitting(sites[slot].Mode);

        /// <summary>A working SIGINT post whose ear reaches the point.</summary>
        public bool SigintCovers(float x, float z, double now)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (sites[i].Kind != CyberSiteKind.Sigint || !Working(i)) continue;
                float radius = EffectRadius(i, now);
                float dx = sites[i].X - x, dz = sites[i].Z - z;
                if (dx * dx + dz * dz <= radius * radius) return true;
            }
            return false;
        }

        public CyberStats Stats()
        {
            float produced = 0f, drawn = 0f, capacity = BaseCapacity;
            int count = 0, net = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (sites[i].Kind == CyberSiteKind.None || sites[i].Lost) continue;
                count++;
                if (!onNet[i]) continue;
                net++;
                float band = CyberSites.Info(sites[i].Kind).Bandwidth;
                if (band >= 0f)
                {
                    if (!sites[i].Compromised && !sites[i].Isolated) produced += band;
                }
                else drawn -= band;
                if (sites[i].Kind == CyberSiteKind.Relay) capacity += RelayCapacity;
                else if (sites[i].Kind == CyberSiteKind.Gateway) capacity += GatewayCapacity;
            }
            return new CyberStats(count, net, linkCount, produced, drawn, capacity);
        }

        // ---- Verbs --------------------------------------------------------------------------

        public float RechargeRemaining(CyberVerb verb, double now) =>
            (byte)verb < VerbCount ? (float)Math.Max(0.0, verbReady[(byte)verb] - now) : 0f;

        /// <summary>Everything the host re-checks for a verb. <paramref name="target"/> is a slot or,
        /// for TRACE and BURN THROUGH, an incident index.</summary>
        public CyberDenial Check(CyberVerb verb, int target, double now)
        {
            if ((byte)verb >= VerbCount) return CyberDenial.NoTarget;
            if (!HasCommand) return CyberDenial.NoCommand;
            if (TargetsIncident(verb))
            {
                if (!IncidentActive(target)) return CyberDenial.NoTarget;
                CyberIncident incident = incidents[target];
                if (verb == CyberVerb.Trace)
                {
                    if (!Traceable(incident.Kind)) return CyberDenial.NotTraceable;
                    if (incident.Tracing) return CyberDenial.AlreadyTracing;
                    if (!AnyWorking(CyberSiteKind.Sigint)) return CyberDenial.NeedsSigint;
                }
                else
                {
                    if (incident.Kind != IncidentKind.Raid) return CyberDenial.NotTraceable;
                    if (!JammerReaches(incident.X, incident.Z, RaidRadius, now)) return CyberDenial.NeedsJammer;
                }
            }
            else
            {
                if (!Exists(target)) return CyberDenial.NoTarget;
                CyberSite site = sites[target];
                if (site.Lost) return CyberDenial.Lost;
                if (site.Deploying) return CyberDenial.Deploying;
                switch (verb)
                {
                    case CyberVerb.Isolate:
                        if (site.Kind == CyberSiteKind.Command) return CyberDenial.CommandProtected;
                        break;
                    case CyberVerb.Patch:
                        if (!site.Compromised) return CyberDenial.NotCompromised;
                        if (site.PatchDone > 0.0) return CyberDenial.AlreadyPatching;
                        break;
                    case CyberVerb.Honeypot:
                        if (site.Kind == CyberSiteKind.Command) return CyberDenial.CommandProtected;
                        if (site.HoneypotUntil > now) return CyberDenial.AlreadyBaited;
                        break;
                }
                // Isolating is a local switch; everything else rides the net.
                if (verb != CyberVerb.Isolate && !onNet[target] && !site.Isolated) return CyberDenial.OffNet;
            }
            // Re-joining an isolated site is free and immediate.
            if (verb == CyberVerb.Isolate && Exists(target) && sites[target].Isolated) return CyberDenial.None;
            if (RechargeRemaining(verb, now) > 0f) return CyberDenial.Recharging;
            if (Bandwidth + 0.001f < VerbCost(verb)) return CyberDenial.LowBandwidth;
            return CyberDenial.None;
        }

        public CyberDenial TryVerb(CyberVerb verb, int target, double now)
        {
            CyberDenial denial = Check(verb, target, now);
            if (denial != CyberDenial.None) return denial;
            bool free = verb == CyberVerb.Isolate && sites[target].Isolated;
            switch (verb)
            {
                case CyberVerb.Isolate:
                    sites[target].Isolated = !sites[target].Isolated;
                    Notify(sites[target].Isolated ? CyberNotice.Isolated : CyberNotice.Rejoined, target, 0, now);
                    RebuildLinks(now);
                    break;
                case CyberVerb.Patch:
                    sites[target].PatchDone = now + PatchSeconds;
                    break;
                case CyberVerb.Honeypot:
                    sites[target].HoneypotUntil = now + HoneypotSeconds;
                    Notify(CyberNotice.Baited, target, 0, now);
                    break;
                case CyberVerb.Trace:
                    incidents[target].Tracing = true;
                    Notify(CyberNotice.TraceStarted, incidents[target].Site, incidents[target].Origin, now);
                    break;
                case CyberVerb.BurnThrough:
                    Resolve(target, IncidentOutcome.Broken, now);
                    Notify(CyberNotice.RaidBroken, -1, incidents[target].Origin, now);
                    RebuildLinks(now);
                    break;
            }
            if (!free)
            {
                Bandwidth = Math.Max(0f, Bandwidth - VerbCost(verb));
                verbReady[(byte)verb] = now + VerbRecharge(verb);
            }
            return CyberDenial.None;
        }

        // ---- Tick ---------------------------------------------------------------------------

        private double lastTick;

        /// <summary>Host: bandwidth, patches, bait expiry, freed slots, links and the campaign.</summary>
        public void Tick(double now, float deltaTime, float campaignIntensity)
        {
            lastTick = now;
            float dt = Math.Max(0f, Math.Min(deltaTime, 5f));
            for (int i = 0; i < SlotCount; i++)
            {
                if (sites[i].Kind == CyberSiteKind.None) continue;
                if (sites[i].Lost && now - sites[i].LostAt >= LostLingerSeconds)
                {
                    sites[i] = default;
                    DropSiteReferences(i);
                    continue;
                }
                if (sites[i].PatchDone > 0.0 && now >= sites[i].PatchDone)
                {
                    sites[i].PatchDone = 0.0;
                    sites[i].Compromised = false;
                    Notify(CyberNotice.Patched, i, 0, now);
                }
                if (sites[i].HoneypotUntil > 0.0 && now >= sites[i].HoneypotUntil) sites[i].HoneypotUntil = 0.0;
            }
            SelfRepair(now);
            RebuildLinks(now);

            CyberStats stats = Stats();
            if (HasCommand && CommandOnline)
            {
                float regen = IdleRegeneration + Math.Max(0f, stats.Net) * 0.5f;
                Bandwidth = Math.Min(stats.Capacity, Bandwidth + regen * dt);
            }
            Bandwidth = Math.Min(Bandwidth, stats.Capacity);

            TickCampaign(now, dt, campaignIntensity);
        }

        private void RebuildLinks(double now)
        {
            Array.Clear(links, 0, links.Length);
            Array.Clear(onNet, 0, onNet.Length);
            linkCount = 0;
            for (int a = 0; a < SlotCount; a++)
            {
                if (!Linkable(a)) continue;
                for (int b = a + 1; b < SlotCount; b++)
                {
                    if (!Linkable(b)) continue;
                    // Airbases are wired to each other: the backbone ignores range and raids.
                    if (!(sites[a].Static && sites[b].Static))
                    {
                        float range = Math.Max(LinkRange(a, now), LinkRange(b, now));
                        float dx = sites[a].X - sites[b].X, dz = sites[a].Z - sites[b].Z;
                        if (dx * dx + dz * dz > range * range) continue;
                    }
                    links[a, b] = links[b, a] = true;
                    linkCount++;
                }
            }

            int root = CommandSlot;
            if (root < 0 || !Online(root)) return;
            // Breadth-first from Cyber Command.
            int[] queue = linkQueue;
            int head = 0, tail = 0;
            onNet[root] = true;
            hops[root] = 0;
            queue[tail++] = root;
            while (head < tail)
            {
                int from = queue[head++];
                for (int to = 0; to < SlotCount; to++)
                {
                    if (!links[from, to] || onNet[to]) continue;
                    onNet[to] = true;
                    hops[to] = hops[from] + 1;
                    queue[tail++] = to;
                }
            }
        }

        private bool Linkable(int slot) => Online(slot) && !sites[slot].Isolated;

        // ---- Airbase infrastructure ------------------------------------------------------------

        private readonly bool[] staticSeen = new bool[SlotCount];

        /// <summary>Host, once a second: start a reconcile of the airbase nodes.</summary>
        public void BeginInfrastructure() => Array.Clear(staticSeen, 0, SlotCount);

        /// <summary>
        /// Host: this airbase is a node this pass. <paramref name="anchor"/> identifies the base;
        /// <paramref name="down"/> says its anchor building is destroyed. Returns the slot, or -1
        /// when the network has no room.
        /// </summary>
        public int ReportInfrastructure(int anchor, CyberSiteKind kind, float x, float z, bool down, double now)
        {
            if (!CyberSites.IsStatic(kind) || !Finite(x) || !Finite(z)) return -1;
            int slot = -1;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!sites[i].Static || sites[i].Anchor != anchor || sites[i].Kind == CyberSiteKind.None) continue;
                slot = i;
                break;
            }
            if (slot < 0)
            {
                slot = FreeSlot();
                if (slot < 0) return -1;
                sites[slot] = new CyberSite { Kind = kind, X = x, Z = z, Static = true, Anchor = anchor, Down = down };
                staticSeen[slot] = true;
                if (kind == CyberSiteKind.Command && Bandwidth < StarterBandwidth) Bandwidth = StarterBandwidth;
                Notify(kind == CyberSiteKind.Command ? CyberNotice.CommandUp : CyberNotice.GatewayJoined, slot, 0, now);
                return slot;
            }

            staticSeen[slot] = true;
            CyberSite site = sites[slot];
            if (site.Kind != kind)
            {
                site.Kind = kind;
                site.Isolated = site.Isolated && kind != CyberSiteKind.Command;
                if (kind == CyberSiteKind.Command) Notify(CyberNotice.CommandMoved, slot, 0, now);
            }
            if (site.Down != down) Notify(down ? CyberNotice.NodeDown : CyberNotice.NodeRestored, slot, 0, now);
            site.Down = down;
            site.X = x;
            site.Z = z;
            sites[slot] = site;
            return slot;
        }

        /// <summary>Host: every airbase node not reported since <see cref="BeginInfrastructure"/>
        /// went with its base.</summary>
        public void EndInfrastructure(double now)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (!sites[i].Static || staticSeen[i] || sites[i].Kind == CyberSiteKind.None) continue;
                bool command = sites[i].Kind == CyberSiteKind.Command;
                Notify(command ? CyberNotice.CommandLost : CyberNotice.GatewayLost, i, 0, now);
                sites[i] = default;
                DropSiteReferences(i);
            }
            RebuildLinks(now);
        }

        /// <summary>Test and single-base seam: one airbase node, reported alongside the ones already there.</summary>
        public int PlaceStatic(int anchor, CyberSiteKind kind, float x, float z, double now = 0.0)
        {
            for (int i = 0; i < SlotCount; i++) staticSeen[i] = sites[i].Static;
            int slot = ReportInfrastructure(anchor, kind, x, z, false, now);
            RebuildLinks(now);
            return slot;
        }

        private float LinkRange(int slot, double now) =>
            CyberSites.Info(sites[slot].Kind).LinkRange * (Jammed(slot, now) ? 0.5f : 1f);

        private int FreeSlot()
        {
            for (int i = 0; i < SlotCount; i++)
                if (sites[i].Kind == CyberSiteKind.None) return i;
            return -1;
        }

        public void Clear()
        {
            Array.Clear(sites, 0, SlotCount);
            Array.Clear(verbReady, 0, VerbCount);
            Bandwidth = 0f;
            SeekersDefeated = 0;
            OriginCount = 0;
            lastTick = 0.0;
            ClearCampaign();
            RebuildLinks(0.0);
        }

        // ---- Snapshot -----------------------------------------------------------------------

        /// <summary>Host: the network with clocks relative to <paramref name="now"/>.</summary>
        public void Export(double now, CyberSnapshot into)
        {
            into.Clear();
            int count = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                CyberSite site = sites[i];
                if (site.Kind == CyberSiteKind.None) continue;
                into.Slot[count] = (byte)i;
                into.Kind[count] = (byte)site.Kind;
                into.X[count] = site.X;
                into.Z[count] = site.Z;
                into.Flags[count] = (byte)((site.Deploying ? 1 : 0) | (site.Lost ? 2 : 0) | (site.Isolated ? 4 : 0) |
                                           (site.Compromised ? 8 : 0) | (site.Static ? 16 : 0) | (site.Down ? 32 : 0));
                into.Mode[count] = (byte)site.Mode;
                into.PatchIn[count] = site.PatchDone > 0.0 ? (float)Math.Max(0.01, site.PatchDone - now) : 0f;
                into.BaitIn[count] = site.HoneypotUntil > now ? (float)(site.HoneypotUntil - now) : 0f;
                count++;
            }
            into.SiteCount = (byte)count;
            into.Bandwidth = Bandwidth;
            into.Defeated = SeekersDefeated;
            for (int v = 0; v < VerbCount; v++) into.Recharge[v] = RechargeRemaining((CyberVerb)v, now);
            ExportCampaign(now, into);
        }

        /// <summary>Client: rebuild from a host snapshot. Out-of-range values are dropped or clamped.</summary>
        public void Mirror(CyberSnapshot from, double now)
        {
            if (from == null) return;
            bool[] seen = mirrorSeen;
            Array.Clear(seen, 0, SlotCount);
            int count = Math.Min((int)from.SiteCount, SlotCount);
            for (int i = 0; i < count; i++)
            {
                int slot = from.Slot[i];
                if (slot >= SlotCount || !CyberSites.Known(from.Kind[i]) || !Finite(from.X[i]) || !Finite(from.Z[i]))
                    continue;
                seen[slot] = true;
                byte flags = from.Flags[i];
                CyberSite site = sites[slot];
                site.Kind = (CyberSiteKind)from.Kind[i];
                site.X = from.X[i];
                site.Z = from.Z[i];
                site.Deploying = (flags & 1) != 0;
                bool lost = (flags & 2) != 0;
                if (lost && !site.Lost) site.LostAt = now;
                site.Lost = lost;
                site.Isolated = (flags & 4) != 0;
                site.Compromised = (flags & 8) != 0;
                site.Static = (flags & 16) != 0 && CyberSites.IsStatic(site.Kind);
                site.Down = (flags & 32) != 0 && site.Static;
                site.Mode = EwPostures.Clamp(from.Mode[i]);
                site.PatchDone = Rebase(site.PatchDone, from.PatchIn[i], now, PatchSeconds);
                site.HoneypotUntil = Rebase(site.HoneypotUntil, from.BaitIn[i], now, HoneypotSeconds);
                sites[slot] = site;
            }
            for (int i = 0; i < SlotCount; i++)
                if (!seen[i]) sites[i] = default;

            Bandwidth = Finite(from.Bandwidth) ? Math.Max(0f, Math.Min(from.Bandwidth, 1000f)) : 0f;
            SeekersDefeated = Math.Max(0, from.Defeated);
            for (int v = 0; v < VerbCount; v++)
                verbReady[v] = Rebase(verbReady[v], from.Recharge[v], now, 120f);
            MirrorCampaign(from, now);
            RebuildLinks(now);
        }

        /// <summary>Keeps a local absolute clock unless the host's relative reading moved it by
        /// more than half a second; zero or garbage clears it.</summary>
        internal static double Rebase(double current, float remaining, double now, float maximum)
        {
            if (!Finite(remaining) || remaining <= 0f) return 0.0;
            double target = now + Math.Min(remaining, maximum);
            return Math.Abs(current - target) > 0.5 ? target : current;
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Wire-shaped copy of a network: sites, bandwidth, recharges, campaign and notices.</summary>
    internal sealed class CyberSnapshot
    {
        public byte SiteCount;
        public readonly byte[] Slot = new byte[CyberNetwork.SlotCount];
        public readonly byte[] Kind = new byte[CyberNetwork.SlotCount];
        public readonly float[] X = new float[CyberNetwork.SlotCount];
        public readonly float[] Z = new float[CyberNetwork.SlotCount];
        public readonly byte[] Flags = new byte[CyberNetwork.SlotCount];
        public readonly byte[] Mode = new byte[CyberNetwork.SlotCount];
        public readonly float[] PatchIn = new float[CyberNetwork.SlotCount];
        public readonly float[] BaitIn = new float[CyberNetwork.SlotCount];
        public float Bandwidth;
        public int Defeated;
        public int Defended;
        public int Breached;
        public readonly float[] Recharge = new float[CyberNetwork.VerbCount];

        public byte Heat;
        public float NextIncidentIn;
        public float ExposedIn;
        public byte IncidentCount;
        public readonly byte[] IncidentKind = new byte[CyberNetwork.IncidentSlots];
        public readonly byte[] IncidentState = new byte[CyberNetwork.IncidentSlots];
        public readonly byte[] IncidentSite = new byte[CyberNetwork.IncidentSlots];
        public readonly byte[] IncidentOrigin = new byte[CyberNetwork.IncidentSlots];
        public readonly float[] IncidentX = new float[CyberNetwork.IncidentSlots];
        public readonly float[] IncidentZ = new float[CyberNetwork.IncidentSlots];
        public readonly float[] IncidentAge = new float[CyberNetwork.IncidentSlots];
        public readonly float[] IncidentLeft = new float[CyberNetwork.IncidentSlots];
        public readonly byte[] IncidentTrace = new byte[CyberNetwork.IncidentSlots];
        public readonly byte[] Foothold = new byte[CyberNetwork.MaximumOrigins];

        public int NoticeSerial;
        public byte NoticeCount;
        public readonly byte[] NoticeKind = new byte[CyberNetwork.NoticeSlots];
        public readonly byte[] NoticeSite = new byte[CyberNetwork.NoticeSlots];
        public readonly byte[] NoticeOrigin = new byte[CyberNetwork.NoticeSlots];

        public void Clear()
        {
            SiteCount = 0;
            Array.Clear(Slot, 0, Slot.Length);
            Array.Clear(Kind, 0, Kind.Length);
            Array.Clear(X, 0, X.Length);
            Array.Clear(Z, 0, Z.Length);
            Array.Clear(Flags, 0, Flags.Length);
            Array.Clear(Mode, 0, Mode.Length);
            Array.Clear(PatchIn, 0, PatchIn.Length);
            Array.Clear(BaitIn, 0, BaitIn.Length);
            Bandwidth = 0f;
            Defeated = 0;
            Defended = Breached = 0;
            Array.Clear(Recharge, 0, Recharge.Length);
            Heat = 0;
            NextIncidentIn = 0f;
            ExposedIn = 0f;
            IncidentCount = 0;
            Array.Clear(IncidentKind, 0, IncidentKind.Length);
            Array.Clear(IncidentState, 0, IncidentState.Length);
            Array.Clear(IncidentSite, 0, IncidentSite.Length);
            Array.Clear(IncidentOrigin, 0, IncidentOrigin.Length);
            Array.Clear(IncidentX, 0, IncidentX.Length);
            Array.Clear(IncidentZ, 0, IncidentZ.Length);
            Array.Clear(IncidentAge, 0, IncidentAge.Length);
            Array.Clear(IncidentLeft, 0, IncidentLeft.Length);
            Array.Clear(IncidentTrace, 0, IncidentTrace.Length);
            Array.Clear(Foothold, 0, Foothold.Length);
            NoticeSerial = 0;
            NoticeCount = 0;
            Array.Clear(NoticeKind, 0, NoticeKind.Length);
            Array.Clear(NoticeSite, 0, NoticeSite.Length);
            Array.Clear(NoticeOrigin, 0, NoticeOrigin.Length);
        }
    }
}
