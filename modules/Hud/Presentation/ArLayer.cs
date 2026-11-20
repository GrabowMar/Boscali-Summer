using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The AR helmet layer (2026-11-14, docs/superpowers/specs/2026-11-14-ar-helmet-layer-design.md): symbols locked to things outside the canopy, each drawn only
    /// while it answers a question the pilot has now (<see cref="ArRules"/>).
    /// <list type="bullet">
    /// <item>PATH: retired; no flight-path line or terrain impact geometry is drawn.</item>
    /// <item>DOMES: retired; BoardOptions ignores the saved switch so the SAM wire cages remain off.</item>
    /// <item>MISSILES: own missiles to their targets (dashed line, IMPACT nS), inbound missiles boxed with their next two seconds.</item>
    /// <item>BEACONS: a pillar over the way home (JOKER, BINGO or damage) and over a live contract zone.</item>
    /// <item>LOCK SPIN-IN and HIT GLITCH: the HUD reacting to the fight.</item>
    /// </list>
    /// Our own objects on the AV layer's <see cref="HudLayer.Under"/> root (inside the native HUDCanvas, vanilla owns visibility; its own nested canvas, so the per-frame redraw
    /// never rebuilds the rest of the HUD). Data at the board's 10 Hz (<see cref="Read"/>, the SAM picture once a second); projection and paint every frame in the layer's late
    /// Paint phase (<see cref="Frame"/>, through the frame's <see cref="HudView"/>, after the native boxes were repaired) on the CPU into two <see cref="AvVector"/>s, labels
    /// written only when their words change. Ink round a target reads the target's native box (the repaired one), so the two never slip apart; distant ink fades by
    /// <see cref="HudInk.Range"/> (never the selected target's, never an inbound missile's).
    /// </summary>
    internal sealed class ArLayer
    {
        private const int LabelMax = 12, SpinMax = 6, InboundMax = 6;
        private const float Wire = 1.2f, Bold = 2.2f, PillarM = 800f, InboundLeadS = 2f, BoxHalf = 14f;
        /// <summary>The missile's private guidance target, as the shot book reads it (set from the synced target id; a seeker drops it when it loses the target).</summary>
        private static readonly HarmonyLib.AccessTools.FieldRef<Missile, Unit> TargetRef = NativeRefs.Field<Missile, Unit>("target");

        private RectTransform root;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private AvVector lines, fx;
        private readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[LabelMax];
        private readonly int[] labelKeys = new int[LabelMax];
        private int labelsUsed;
        private readonly char[] buf = new char[48];
        private float lastK;

        // Projection, once per frame: the frame's view (canvas units round the canvas centre) over k = the layer's 1080-high reference units.
        private HudView view;
        private float k = 1f, halfW, halfH;

        private bool on, showDomes, showMissiles, showBeacons, showFx;
        private Aircraft aircraft;
        private AvUnits units = AvUnits.Metric;

        // DOMES
        private struct Dome
        {
            public float X, GroundY, Z, MaxRange, MinAltitude, MaxAltitude, Distance;
            public bool Inside;
        }
        private readonly AirDefenceRing[] rings = new AirDefenceRing[ThreatPictureLimits.MaximumRings];
        private readonly Dome[] domes = new Dome[ArRules.DomeMax];
        private int domeCount;
        private float nextDomes;
        private IThreatPicture picture;
        private readonly Vector3[] ringPoints = new Vector3[ArRules.DomeSegments];

        // BEACONS
        private Airbase home;
        private bool objectiveOn;
        private float objectiveX, objectiveZ, objectiveY;
        private int objectiveId;

        // LOCK SPIN-IN
        private readonly List<Unit> lastTargets = new List<Unit>(16);
        private readonly Unit[] spinUnits = new Unit[SpinMax];
        private readonly float[] spinAt = new float[SpinMax];

        // HIT GLITCH
        private DamageState lastDamage;
        private Aircraft damageAircraft;
        private float glitchAt = float.NegativeInfinity;
        private int glitchSeed;

        // MISSILES
        private readonly Missile[] inbound = new Missile[InboundMax];
        private readonly int[] inboundNumbers = new int[InboundMax];

        /// <summary>The glitch's sideways jitter at <paramref name="now"/> in reference pixels (the TAC BOARD shakes with the layer; a pure function of the clock, so the board reads it in LateUpdate before the layer paints).</summary>
        internal float Jitter(float now) => on && showFx && !AvFxDriver.ReducedMotion ? ArRules.Jitter(now - glitchAt, glitchSeed) : 0f;

        // What the layer drew on its last frame (the sim probe in HudAutomation reads these).
        internal bool PathShown { get; private set; }
        internal bool PathAlert { get; private set; }
        internal int DomesShown { get; private set; }
        internal bool InsideDome { get; private set; }
        internal int OwnMissilesShown { get; private set; }
        internal int InboundShown { get; private set; }
        internal bool HomeShown { get; private set; }
        internal bool ObjectiveShown { get; private set; }
        internal int SpinsShown { get; private set; }
        /// <summary>SAM rings the faction knows (any distance) and how far the nearest is in km (-1: none), as of the last once-a-second read.</summary>
        internal int RingsKnown { get; private set; }
        internal float NearestRingKm { get; private set; } = -1f;
        /// <summary>Spin-ins running, on screen or not.</summary>
        internal int SpinsActive { get; private set; }
        internal bool Glitching { get; private set; }
        internal int Quads => lines != null ? lines.Buffer.Count + fx.Buffer.Count : 0;

#if !PUBLIC_RELEASE
        /// <summary>Legacy automation hook retained for existing scenarios; the retired flight path stays off.</summary>
        internal float ForcePullG = float.NaN;
        /// <summary>Legacy automation turn fixture; no geometry is drawn.</summary>
        internal float ForceTurnDegS = float.NaN;
        /// <summary>Legacy automation prediction fixture; no geometry is drawn.</summary>
        internal float ForcePitchDeg = float.NaN;
        /// <summary>Test and showcase hook: stand the home beacon whatever the fuel and damage.</summary>
        internal bool ForceHome;
#else
        private const bool ForceHome = false;
#endif

        public bool Ensure()
        {
            if (root != null) return true;
            if (!HudLayer.Ensure()) return false;
            VanillaHudStyle.TryHudFont(out font, out fontMaterial);

            root = Holder(HudLayer.Under, "BoscaliArLayer");      // the layer's Under root: its own batch, under the game's own symbology (the world is behind the HUD)
            lines = AvVector.Create(root, "World", 1600, BoscaliSummer.Core.Contracts.HudGlow.Stroke, BoscaliSummer.Core.Contracts.HudGlow.Alpha);
            fx = AvVector.Create(root, "Fx", 96);
            for (int i = 0; i < LabelMax; i++)
            {
                TextMeshProUGUI t = HudText.Make(root, "Label", font, fontMaterial, TextAlignmentOptions.Center, TopCentre.LabelPt);
                RectTransform rect = t.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(220f, TopCentre.LabelPt * 1.5f);
                t.characterSpacing = 2f;
                t.richText = false;
                t.fontStyle |= FontStyles.Bold;
                t.gameObject.SetActive(false);
                labels[i] = t;
                labelKeys[i] = int.MinValue;
            }
            root.gameObject.SetActive(false);
            lastK = 0f;
            return true;
        }

        /// <summary>
        /// The board's 10 Hz read: the beacons, new locks and new damage. Retired overlay switches cannot re-enable their geometry.
        /// <paramref name="show"/> false (every switch off, a pause, the map, no aircraft) clears the layer.
        /// </summary>
        public void Read(bool show, in BoardOptions options, Aircraft own, BoardFrame frame, Airbase field, float now)
        {
            if (!show || own == null || root == null)
            {
                on = false;
                aircraft = null;
                return;
            }
            on = true;
            aircraft = own;
            units = frame.Units;
            showDomes = options.ArDomes;
            showMissiles = options.ArMissiles;
            showBeacons = options.ArBeacons;
            showFx = options.HudFx;
            if (!showDomes) domeCount = 0;
            else if (now >= nextDomes)
            {
                nextDomes = now + 1f;
                ReadDomes(own);
            }
            ReadBeacons(own, frame, field);
            ReadLocks(now);
            ReadHits(own, frame, now);
        }

        /// <summary>The faction's own known SAM rings, nearest <see cref="ArRules.DomeMax"/> within reach; the ground under each found once per read.</summary>
        private void ReadDomes(Aircraft own)
        {
            domeCount = 0;
            FactionHQ hq = own.NetworkHQ;
            if (picture == null) ModuleServices.TryGet(out picture);
            if (picture == null || hq == null) return;
            int observer = hq.GetInstanceID();
            if (!picture.IsReady(observer)) return;
            int n = picture.CopyAirDefence(observer, rings);
            Vector3 at = own.transform.position.ToGlobalPosition().AsVector3();
            RingsKnown = 0;
            NearestRingKm = -1f;
            for (int i = 0; i < n; i++)
            {
                AirDefenceRing r = rings[i];
                if (!ArRules.IsDome(r.Kind)) continue;
                float dx = r.X - at.x, dz = r.Z - at.z, distance = Mathf.Sqrt(dx * dx + dz * dz);
                RingsKnown++;
                if (NearestRingKm < 0f || distance * 0.001f < NearestRingKm) NearestRingKm = distance * 0.001f;
                if (!ArRules.DomeNear(distance, r.MaxRange)) continue;
                // Kept sorted, nearest first; a full list takes a ring only if it is nearer than its last.
                int slot;
                if (domeCount < ArRules.DomeMax) slot = domeCount++;
                else if (distance < domes[ArRules.DomeMax - 1].Distance) slot = ArRules.DomeMax - 1;
                else continue;
                while (slot > 0 && domes[slot - 1].Distance > distance) { domes[slot] = domes[slot - 1]; slot--; }
                domes[slot] = new Dome
                {
                    X = r.X, Z = r.Z, MaxRange = r.MaxRange, MinAltitude = r.MinAltitude, MaxAltitude = r.MaxAltitude, Distance = distance,
                    GroundY = Ground(r.X, r.Z),
                    Inside = distance < r.EffectiveRadius(own.radarAlt),
                };
            }
        }

        /// <summary>Global height of the ground (or the sea) under a global x, z.</summary>
        private static float Ground(float x, float z)
        {
            Vector3 top = new GlobalPosition(x, 12000f, z).ToLocalPosition();
            float sea = Datum.LocalSeaY;
            float y = Physics.Raycast(top, Vector3.down, out RaycastHit hit, 14000f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) && hit.point.y > sea ? hit.point.y : sea;
            return y - top.y + 12000f;
        }

        private void ReadBeacons(Aircraft own, BoardFrame frame, Airbase field)
        {
            home = null;
            objectiveOn = false;
            if (!showBeacons) return;
            DamageState d = frame.Damage;
            if (field != null && field.center != null && (ForceHome || ArRules.HomeWanted(frame.Fuel.Joker, frame.Fuel.Bingo, d.Flags, d.EnginesOut, d.DetachedRatio)))
                home = field;
            if (ScopeFeeds.TryRead(out ObjectiveScope o) && o.Id != 0 && o.HasMarker)
            {
                Vector3 at = own.transform.position.ToGlobalPosition().AsVector3();
                float dx = o.X - at.x, dz = o.Z - at.z;
                if (dx * dx + dz * dz > ArRules.ObjectiveMinM * ArRules.ObjectiveMinM)
                {
                    objectiveOn = true;
                    if (o.Id != objectiveId || o.X != objectiveX || o.Z != objectiveZ) objectiveY = Ground(o.X, o.Z);      // one long raycast per objective, not one a frame
                    objectiveId = o.Id;
                    objectiveX = o.X;
                    objectiveZ = o.Z;
                }
            }
        }

        /// <summary>A unit that joined the selected targets since the last read starts a spin-in.</summary>
        private void ReadLocks(float now)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            List<Unit> targets = hud != null ? hud.GetTargetList() : null;
            if (targets == null) { lastTargets.Clear(); return; }
            if (showFx)
                for (int i = 0; i < targets.Count; i++)
                {
                    Unit u = targets[i];
                    if (u == null || lastTargets.Contains(u)) continue;
                    int slot = 0;
                    for (int s = 1; s < SpinMax; s++) if (spinAt[s] < spinAt[slot]) slot = s;      // the oldest spin gives way
                    spinUnits[slot] = u;
                    spinAt[slot] = now;
                }
            lastTargets.Clear();
            lastTargets.AddRange(targets);
        }

        private void ReadHits(Aircraft own, BoardFrame frame, float now)
        {
            if (own != damageAircraft)
            {
                damageAircraft = own;
                lastDamage = frame.Damage;
                return;
            }
            if (showFx && ArRules.IsHit(lastDamage, frame.Damage) && now - glitchAt >= ArRules.GlitchGapS) Glitch(now);
            lastDamage = frame.Damage;
        }

        /// <summary>Start a hit glitch now (also the sims' showcase hook).</summary>
        internal void Glitch(float now)
        {
            glitchAt = now;
            glitchSeed++;
        }

        /// <summary>Every frame, in the layer's Paint phase: project through <paramref name="frameView"/> and paint what the read decided (the camera and everything in the world move every frame).</summary>
        public void Frame(MissileTracker tracker, ThreatWatch threats, float now, in HudView frameView)
        {
            if (root == null) return;
            bool show = on && aircraft != null && !aircraft.disabled;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!show) { Clear(); return; }
            float scale = frameView.CanvasSize.y / 1080f;
            if (!frameView.Valid || !(scale > 0.01f)) return;
            view = frameView;
            k = scale;
            if (k != lastK)
            {
                root.localScale = new Vector3(k, k, 1f);
                lastK = k;
            }
            halfW = view.CanvasSize.x / k * 0.5f;
            halfH = 540f;

            VanillaHudStyle.Palette palette = VanillaHudStyle.Colours;
            bool calm = AvFxDriver.ReducedMotion;
            AvQuadBuffer b = lines.Buffer, f = fx.Buffer;
            b.Clear();
            f.Clear();
            labelsUsed = 0;
            Vector3 eye = view.Eye;

            PaintDomes(b, palette, eye, now, calm);
            PaintBeacons(b, palette, eye);
            PaintMissiles(b, palette, tracker, threats, now, calm);
            PaintSpins(f, palette, eye, now, calm);
            PaintGlitch(f, palette, now, calm);

            for (int i = labelsUsed; i < LabelMax; i++)
                if (labels[i].gameObject.activeSelf) labels[i].gameObject.SetActive(false);
            lines.Commit();
            fx.Commit();
            var jitter = new Vector2(Jitter(now), 0f);
            if (root.anchoredPosition != jitter) root.anchoredPosition = jitter;
        }

        // ------------------------------------------------------------------ DOMES

        private void PaintDomes(AvQuadBuffer b, VanillaHudStyle.Palette palette, Vector3 eye, float now, bool calm)
        {
            DomesShown = 0;
            InsideDome = false;
            if (!showDomes) return;
            float ownY = aircraft.transform.position.GlobalY(), agl = aircraft.radarAlt;
            for (int d = 0; d < domeCount; d++)
            {
                Dome dome = domes[d];
                Vector3 site = new GlobalPosition(dome.X, dome.GroundY, dome.Z).ToLocalPosition();
                Rgba ink = HudInk.Range((dome.Inside ? palette.Alert : palette.Warning).ToRgba().WithAlpha(dome.Inside ? 0.7f : 0.5f), Vector3.Distance(eye, site));
                float reach = AirDefenceRing.EffectiveRadius(dome.MaxRange, dome.MinAltitude, dome.MaxAltitude, agl);
                if (dome.Inside)
                {
                    // Inside, the wire all round reads as lines across the horizon: show the way out instead.
                    PaintExit(b, dome, site, reach, ownY, palette.Alert.ToRgba(), now, calm, d);
                    InsideDome = true;
                    DomesShown++;
                    continue;
                }
                // A cage round your own height, not the whole envelope (its struts to a 10 km ceiling ran edge to edge across the screen, in game): the floor where
                // full reach starts, a cap 1.5 km above you (or the ceiling), the struts between, and the cone down to the site.
                ArRules.Level(dome.MaxRange, dome.MinAltitude, dome.MaxAltitude, 0, out float floorHeight, out float floorRadius);
                float ownHeight = ownY - dome.GroundY;
                float capHeight = Mathf.Min(dome.MaxAltitude, Mathf.Max(ownHeight, floorHeight) + 1500f);
                float capRadius = AirDefenceRing.EffectiveRadius(dome.MaxRange, dome.MinAltitude, dome.MaxAltitude, capHeight);
                Rgba wire = ink.WithAlpha(ink.A * 0.5f);
                if (floorRadius > 50f) Ring(b, site + Vector3.up * floorHeight, floorRadius, Wire, ink);
                if (capRadius > 50f && capHeight > floorHeight + 1f) Ring(b, site + Vector3.up * capHeight, capRadius, Wire, wire);
                Vector3 fromSite = aircraft.transform.position - site;
                float facing = Mathf.Atan2(fromSite.z, fromSite.x);
                for (int s = 0; s < ArRules.DomeStruts; s++)
                {
                    float a = s * Mathf.PI * 2f / ArRules.DomeStruts;
                    if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, facing * Mathf.Rad2Deg)) < 35f) continue;      // the near side is the fence's; a strut there runs down the reticle
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 floor = site + dir * floorRadius + Vector3.up * floorHeight, top = site + dir * capRadius + Vector3.up * capHeight;
                    if (floorHeight > 1f) Seg(b, site, floor, Wire, wire);
                    if (capHeight > floorHeight + 1f) Seg(b, floor, top, Wire, wire);
                }
                if (reach > 50f) PaintFence(b, dome, reach, ownY, ink, d);
                DomesShown++;
            }
        }

        /// <summary>
        /// Where the SAM would reach you at your height: the near arc of that ring (100 degrees facing you) as a fence of short posts, bold, with the distance to it.
        /// A whole ring at eye level is edge-on and reads as a line along the horizon; posts on the near arc read as a wall you are flying toward.
        /// </summary>
        private void PaintFence(AvQuadBuffer b, in Dome dome, float reach, float ownY, Rgba ink, int slot)
        {
            Vector3 own = aircraft.transform.position, centre = new GlobalPosition(dome.X, ownY, dome.Z).ToLocalPosition();
            Vector3 toward = own - centre;
            toward.y = 0f;
            float bearing = Mathf.Atan2(toward.z, toward.x);
            Rgba bold = ink.WithAlpha(Mathf.Min(1f, ink.A * 1.8f));
            const int Steps = 20;
            const float Arc = 100f * Mathf.Deg2Rad, Post = 250f;
            Rgba rail = ink.WithAlpha(ink.A * 0.8f);
            Vector3 prev = default, up = Vector3.up * Post;
            // A band 500 m tall round your height: the bold line where it reaches you, thin rails above and below, posts between: it reads as a wall, not a horizon.
            for (int i = 0; i <= Steps; i++)
            {
                float a = bearing - Arc * 0.5f + Arc * i / Steps;
                Vector3 p = centre + new Vector3(Mathf.Cos(a) * reach, 0f, Mathf.Sin(a) * reach);
                if (i > 0)
                {
                    Seg(b, prev, p, Bold, bold);
                    Seg(b, prev + up, p + up, Wire, rail);
                    Seg(b, prev - up, p - up, Wire, rail);
                }
                if ((i & 1) == 0) Seg(b, p - up, p + up, Wire, rail);
                prev = p;
            }
            Vector3 near = centre + new Vector3(Mathf.Cos(bearing) * reach, 0f, Mathf.Sin(bearing) * reach);
            if (Project(near + Vector3.up * Post, out float lx, out float ly))
                LabelDistance(lx, ly + 6f, 3_000_000 + slot * 100_000, bold, "SAM EDGE ", Mathf.Max(0f, dome.Distance - reach), TopCentre.DetailPt);
        }

        /// <summary>
        /// Inside an envelope: a gate on its edge at your height, straight out from the site (the shortest way out), chevrons through it pointing out, and the distance to it;
        /// the site itself gets a short pillar and IN SAM ENVELOPE. The gate breathes slowly (still under REDUCED MOTION).
        /// </summary>
        private void PaintExit(AvQuadBuffer b, in Dome dome, Vector3 site, float reach, float ownY, Rgba ink, float now, bool calm, int slot)
        {
            Vector3 own = aircraft.transform.position, centre = new GlobalPosition(dome.X, ownY, dome.Z).ToLocalPosition();
            Vector3 away = own - centre;
            away.y = 0f;
            if (away.sqrMagnitude < 1f) { away = aircraft.transform.forward; away.y = 0f; }
            away.Normalize();
            Vector3 exit = centre + away * reach, side = Vector3.Cross(Vector3.up, away);
            float breathe = calm ? 1f : 0.75f + 0.25f * Mathf.Sin(now * 4f);
            Rgba gate = ink.WithAlpha(breathe);
            const float HalfW = 220f, HalfH = 120f;
            Vector3 a = exit - side * HalfW - Vector3.up * HalfH, c = exit + side * HalfW - Vector3.up * HalfH;
            Vector3 e = exit + side * HalfW + Vector3.up * HalfH, f = exit - side * HalfW + Vector3.up * HalfH;
            Seg(b, a, c, Bold, gate); Seg(b, c, e, Bold, gate); Seg(b, e, f, Bold, gate); Seg(b, f, a, Bold, gate);
            if (Project(exit, out float gx, out float gy) && Project(exit + away * 400f, out float ox, out float oy))
            {
                float angle = Mathf.Atan2(oy - gy, ox - gx) * Mathf.Rad2Deg;
                AvStrokes.Chevron(b, gx, gy, 12f, angle, 2f, gate);
                LabelDistance(gx, gy + 18f, 2_100_000 + slot * 100_000, ink, "SAM EXIT ", Mathf.Max(0f, reach - Vector3.Distance(own, centre)), TopCentre.DetailPt);
            }
            Seg(b, site, site + Vector3.up * 600f, Wire, ink.WithAlpha(0.6f));
            if (Project(site + Vector3.up * 600f, out float sx, out float sy)) Label(sx, sy + 6f, 2_000_000 + slot, ink, "IN SAM ENVELOPE", -1, null);
        }

        // ------------------------------------------------------------------ BEACONS

        private void PaintBeacons(AvQuadBuffer b, VanillaHudStyle.Palette palette, Vector3 eye)
        {
            HomeShown = ObjectiveShown = false;
            if (!showBeacons) return;
            if (home != null && home.center != null)
            {
                Vector3 at = home.center.position;
                float far = Vector3.Distance(eye, at);
                Rgba ink = HudInk.Range(palette.AllClear.ToRgba(), far);
                Pillar(b, at, ink, far, diamond: true);
                if (Project(at + Vector3.up * PillarHeight(far), out float x, out float y))
                    LabelDistance(x, y + 12f, 4_000_000, ink, "HOME ", Vector3.Distance(aircraft.transform.position, at));
                HomeShown = true;
            }
            if (objectiveOn)
            {
                Vector3 at = new GlobalPosition(objectiveX, objectiveY, objectiveZ).ToLocalPosition();
                float far = Vector3.Distance(eye, at);
                Rgba ink = HudInk.Range(palette.Warning.ToRgba(), far);
                Pillar(b, at, ink, far, diamond: false);
                if (Project(at + Vector3.up * PillarHeight(far), out float x, out float y))
                    LabelDistance(x, y + 12f, 5_000_000, ink, "OBJ ", Vector3.Distance(aircraft.transform.position, at));
                ObjectiveShown = true;
            }
        }

        /// <summary>A pillar's height: about 2.5 degrees tall from far out, never under <see cref="PillarM"/> nor over 6 km.</summary>
        private static float PillarHeight(float distance) => Mathf.Clamp(distance * 0.045f, PillarM, 6000f);

        /// <summary>A beam of light from the ground: the line, three ticks up it, a diamond (home) or a ring (objective) on top; <paramref name="ink"/> comes range-inked.</summary>
        private void Pillar(AvQuadBuffer b, Vector3 foot, Rgba ink, float far, bool diamond)
        {
            float height = PillarHeight(far);
            Vector3 top = foot + Vector3.up * height;
            Seg(b, foot, top, Bold, ink);
            for (int t = 1; t <= 3; t++)
                if (Project(foot + Vector3.up * (height * t / 4f), out float x, out float y))
                    AvStrokes.Line(b, x - 5f, y, x + 5f, y, Wire, ink.WithAlpha(ink.A * 0.7f));
            if (!Project(top, out float tx, out float ty)) return;
            if (diamond)
            {
                AvStrokes.Diamond(b, tx, ty, 7f, 1.6f, ink);
                AvStrokes.Diamond(b, tx, ty, 2.5f, 2.5f, ink);
            }
            else
            {
                AvStrokes.Ring(b, tx, ty, 6f, 16, 1.6f, ink);
                AvStrokes.Fill(b, tx - 2f, ty - 2f, 4f, 4f, ink);
            }
        }

        // ------------------------------------------------------------------ MISSILES

        private void PaintMissiles(AvQuadBuffer b, VanillaHudStyle.Palette palette, MissileTracker tracker, ThreatWatch threats, float now, bool calm)
        {
            OwnMissilesShown = InboundShown = 0;
            if (!showMissiles) return;
            Rgba ink = palette.AllClear.ToRgba();
            IReadOnlyList<Missile> own = tracker.Missiles;
            for (int i = 0; i < own.Count && OwnMissilesShown < 4; i++)
            {
                Missile m = own[i];
                if (m == null || m.disabled) continue;
                Vector3 pos = m.transform.position;
                Vector3 velocity = m.rb != null ? m.rb.velocity : Vector3.zero;
                if (Project(pos, out float mx, out float my) && Project(pos + velocity * 0.2f, out float ax, out float ay))
                    AvStrokes.Chevron(b, mx, my, 9f, Mathf.Atan2(ay - my, ax - mx) * Mathf.Rad2Deg, 1.8f, ink);
                Unit target = NativeRefs.Get(TargetRef, m);
                if (target != null && !target.disabled)
                {
                    bool first = true;
                    for (int j = 0; j < i; j++) if (own[j] != null && NativeRefs.Get(TargetRef, own[j]) == target) first = false;
                    // The ring sits on the target's native box (repaired this frame); the line runs to where the box says the target is.
                    bool boxed = Anchor(target, out float tx, out float ty, out float half, out Vector3 to);
                    float range = Vector3.Distance(pos, to);
                    Vector3 targetVelocity = target.rb != null ? target.rb.velocity : Vector3.zero;
                    float closure = range > 1f ? Vector3.Dot(velocity - targetVelocity, (to - pos) / range) : 0f;
                    Rgba near = Ramp(ink, target, Vector3.Distance(view.Eye, to));
                    DashedSeg(b, pos, to, near.WithAlpha(near.A * 0.55f));
                    if (first && boxed)
                    {
                        float r = half + 4f;
                        AvStrokes.Ring(b, tx, ty, r, 32, HudStroke.Standard, near);
                        if (closure > 5f)
                        {
                            int seconds = Mathf.Clamp(Mathf.CeilToInt(range / closure), 0, 99);
                            Label(tx + r + 44f, ty - 7f, 6_000_000 + i * 1000 + seconds, near, "IMPACT ", seconds, "S");      // right of the ring: the game writes the name above the box, the range under it
                        }
                    }
                }
                OwnMissilesShown++;
            }

            Rgba alert = palette.Alert.ToRgba();
            int n = threats.LiveMissiles(inbound, inboundNumbers);
            for (int i = 0; i < n && i < InboundMax; i++)
            {
                Missile m = inbound[i];
                if (m == null) continue;
                Vector3 pos = m.transform.position;
                if (!Project(pos, out float x, out float y)) continue;
                Vector3 velocity = m.rb != null ? m.rb.velocity : Vector3.zero;
                Seg(b, pos, pos + velocity * InboundLeadS, Bold, alert.WithAlpha(0.8f));
                // A diamond turning on the missile (still under REDUCED MOTION), and its number from the threat ring.
                float spin = calm ? 0f : now * 240f;
                RotatedSquare(b, x, y, 11f, spin + 45f, 1.8f, alert);
                Label(x, y + 14f, 7_000_000 + inboundNumbers[i], alert, "M", inboundNumbers[i], null);
                InboundShown++;
            }
            System.Array.Clear(inbound, 0, inbound.Length);
        }

        // ------------------------------------------------------------------ LOCK SPIN-IN

        /// <summary>A new lock: two opposed arcs of one ring turn in and close onto the target's native box, then go (AllClear, never red; full colour while the target stays selected).</summary>
        private void PaintSpins(AvQuadBuffer f, VanillaHudStyle.Palette palette, Vector3 eye, float now, bool calm)
        {
            SpinsShown = SpinsActive = 0;
            Rgba ink = palette.AllClear.ToRgba();
            for (int s = 0; s < SpinMax; s++)
            {
                Unit u = spinUnits[s];
                if (u == null) continue;
                if (u.disabled || !ArRules.Spin(now - spinAt[s], calm, out float close, out float angle, out float alpha))
                {
                    spinUnits[s] = null;
                    continue;
                }
                SpinsActive++;
                if (!Anchor(u, out float x, out float y, out float half, out Vector3 at)) continue;
                Rgba c = Ramp(ink, u, Vector3.Distance(eye, at));
                c = c.WithAlpha(c.A * alpha);
                float r = ArRules.SpinRadius(close, half);
                AvStrokes.Arc(f, x, y, r, angle - 45f, angle + 45f, 12, HudStroke.Standard, c);
                AvStrokes.Arc(f, x, y, r, angle + 135f, angle + 225f, 12, HudStroke.Standard, c);
                SpinsShown++;
            }
        }

        // ------------------------------------------------------------------ HIT GLITCH

        private void PaintGlitch(AvQuadBuffer f, VanillaHudStyle.Palette palette, float now, bool calm)
        {
            float age = now - glitchAt;
            Glitching = showFx && age >= 0f && age < ArRules.GlitchS;
            if (!Glitching) return;
            float life = 1f - age / ArRules.GlitchS;
            Rgba ink = palette.Alert.ToRgba();
            if (calm)
            {
                // REDUCED MOTION: one still line across the top of the view, no shake.
                AvStrokes.Line(f, -halfW, halfH - 3f, halfW, halfH - 3f, HudStroke.Hairline, ink.WithAlpha(0.35f * life));
                return;
            }
            // Scan lines, 1 px (no filled bands); the sideways shake is Jitter's.
            for (int i = 0; i < ArRules.GlitchBars; i++)
            {
                ArRules.Bar(glitchSeed, i, out float share, out _);
                float y = -halfH + share * halfH * 2f + ArRules.Noise(glitchSeed, (int)(age * 30f) + i) * 6f;
                AvStrokes.Line(f, -halfW, y, halfW, y, HudStroke.Hairline, ink.WithAlpha(0.3f * life));
            }
        }

        // ------------------------------------------------------------------ drawing helpers

        /// <summary>A world point (local) to the layer's reference units; false behind the near cut or well off the screen.</summary>
        private bool Project(Vector3 world, out float x, out float y)
        {
            if (!view.Project(world, out Vector2 c)) { x = y = 0f; return false; }
            x = c.x / k;
            y = c.y / k;
            return x > -halfW * 1.5f && x < halfW * 1.5f && y > -halfH * 1.5f && y < halfH * 1.5f;
        }

        /// <summary>
        /// Where a unit's native box is this frame, in the layer's units, and its half-size: the box itself (the layer's Repair placed it already, so ink round it never slips),
        /// else the unit's known position projected (a stale track where the game knows it, not where it is). <paramref name="world"/>: the point the box stands for.
        /// </summary>
        private bool Anchor(Unit u, out float x, out float y, out float half, out Vector3 world)
        {
            half = BoxHalf;
            world = u.transform.position;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || !hud.TryGetMarker(u, out HUDUnitMarker m) || m == null || m.image == null) return Project(world, out x, out y);
            FactionHQ hq = aircraft.NetworkHQ;
            if (m.outdated && hq != null && hq.TryGetKnownPosition(u, out GlobalPosition known)) world = known.ToLocalPosition();
            if (!m.image.isActiveAndEnabled) return Project(world, out x, out y);
            RectTransform box = m.image.rectTransform;
            Vector2 c = view.FromScreen(box.position);      // an overlay canvas: world units are screen pixels
            x = c.x / k;
            y = c.y / k;
            float size = box.rect.width * 0.5f * box.lossyScale.x / view.Scale / k;
            if (size > 2f && size < 200f) half = size;
            return true;
        }

        /// <summary>Mod ink tied to a target, by range (<see cref="HudInk.Range"/>); full colour while the target is selected.</summary>
        private Rgba Ramp(Rgba ink, Unit target, float metres) => lastTargets.Contains(target) ? ink : HudInk.Range(ink, metres);

        /// <summary>A world segment, cut at the near plane and to the view (<see cref="HudView.Clip"/>).</summary>
        private void Seg(AvQuadBuffer b, Vector3 a, Vector3 c, float width, Rgba ink)
        {
            if (!view.Clip(a, c, out Vector2 p, out Vector2 q)) return;
            AvStrokes.Line(b, p.x / k, p.y / k, q.x / k, q.y / k, width, ink);
        }

        private void DashedSeg(AvQuadBuffer b, Vector3 a, Vector3 c, Rgba ink)
        {
            if (!view.Clip(a, c, out Vector2 p, out Vector2 q)) return;
            AvStrokes.DashedLine(b, p.x / k, p.y / k, q.x / k, q.y / k, 6f, 5f, 1.4f, ink);
        }

        /// <summary>A horizontal ring in the world, its points worked out once and joined.</summary>
        private void Ring(AvQuadBuffer b, Vector3 centre, float radius, float width, Rgba ink)
        {
            int n = ArRules.DomeSegments;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                ringPoints[i] = centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }
            for (int i = 0; i < n; i++) Seg(b, ringPoints[i], ringPoints[(i + 1) % n], width, ink);
        }

        private static void RotatedSquare(AvQuadBuffer b, float x, float y, float r, float angleDeg, float width, Rgba ink)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            float px = Mathf.Cos(a) * r, py = Mathf.Sin(a) * r;
            // Corners at a, a+90, a+180, a+270 degrees.
            AvStrokes.Line(b, x + px, y + py, x - py, y + px, width, ink);
            AvStrokes.Line(b, x - py, y + px, x - px, y - py, width, ink);
            AvStrokes.Line(b, x - px, y - py, x + py, y - px, width, ink);
            AvStrokes.Line(b, x + py, y - px, x + px, y + py, width, ink);
        }

        // ------------------------------------------------------------------ labels

        /// <summary>A label at (x, y): "<paramref name="head"/><paramref name="number"/><paramref name="tail"/>" (just the head for a negative number), written only when <paramref name="key"/> changed.</summary>
        private void Label(float x, float y, int key, Rgba ink, string head, int number, string tail, float size = TopCentre.LabelPt)
        {
            TextMeshProUGUI t = NextLabel(x, y, ink, size);
            if (t == null || labelKeys[labelsUsed - 1] == key) return;
            labelKeys[labelsUsed - 1] = key;
            int n = AvNumFormat.Append(buf, 0, head);
            if (number >= 0)
            {
                n = AvNumFormat.Write(buf, n, number, 0);
                n = AvNumFormat.Append(buf, n, tail);
            }
            t.SetText(buf, 0, n);      // no string built: nothing allocated
        }

        /// <summary>A distance label in the pilot's units ("SAM 18KM"), rewritten only when the reading moves by 100 m.</summary>
        private void LabelDistance(float x, float y, int keyBase, Rgba ink, string head, float metres, float size = TopCentre.LabelPt)
        {
            TextMeshProUGUI t = NextLabel(x, y, ink, size);
            if (t == null) return;
            int key = keyBase + Mathf.Clamp(Mathf.RoundToInt(metres / 100f), 0, 49_999) * 2 + (units == AvUnits.Imperial ? 1 : 0);
            if (labelKeys[labelsUsed - 1] == key) return;
            labelKeys[labelsUsed - 1] = key;
            int n = AvNumFormat.Append(buf, 0, head);
            n = AvUnitTable.DistanceReading(buf, n, metres, units);
            for (int i = head.Length; i < n; i++) buf[i] = char.ToUpperInvariant(buf[i]);
            t.SetText(buf, 0, n);
        }

        private TextMeshProUGUI NextLabel(float x, float y, Rgba ink, float size)
        {
            if (labelsUsed >= LabelMax) return null;
            TextMeshProUGUI t = labels[labelsUsed++];
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            if (t.fontSize != size) t.fontSize = size;
            var at = new Vector2(x, y);
            if (t.rectTransform.anchoredPosition != at) t.rectTransform.anchoredPosition = at;
            Color c = new Color(ink.R, ink.G, ink.B, ink.A);
            if (t.color != c) t.color = c;
            return t;
        }

        private void Clear()
        {
            PathShown = PathAlert = InsideDome = HomeShown = ObjectiveShown = Glitching = false;
            DomesShown = OwnMissilesShown = InboundShown = SpinsShown = 0;
        }

        private static RectTransform Holder(RectTransform parent, string name)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            return rect;
        }

        public void Hide()
        {
            on = false;
            aircraft = null;
            Clear();
            if (root != null) root.gameObject.SetActive(false);
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            Hide();
            domeCount = 0;
            lastTargets.Clear();
            System.Array.Clear(spinUnits, 0, SpinMax);
            damageAircraft = null;
            picture = null;
        }
    }
}
