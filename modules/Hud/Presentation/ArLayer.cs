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
    /// <item>PATH: where the aircraft will be over the next 8 s (constant-turn prediction); in the alert colour with an X, a ground ring and TERRAIN nS where it meets
    /// the ground, faint during a hard pull.</item>
    /// <item>DOMES: the engagement envelope of every known hostile SAM near enough to matter, in the game's own cone-then-cylinder shape, the ring at the aircraft's
    /// own height bold; IN SAM ENVELOPE inside.</item>
    /// <item>MISSILES: own missiles to their targets (dashed line, IMPACT nS), inbound missiles boxed with their next two seconds.</item>
    /// <item>BEACONS: a pillar over the way home (JOKER, BINGO or damage) and over a live contract zone.</item>
    /// <item>LOCK SPIN-IN and HIT GLITCH: the HUD reacting to the fight.</item>
    /// </list>
    /// Our own objects under the native HUDCanvas (vanilla owns visibility) on a nested Canvas, so the per-frame redraw never rebuilds the rest of the HUD. Data at the
    /// board's 10 Hz (<see cref="Read"/>, the SAM picture once a second); projection and paint every frame (<see cref="Frame"/>) on the CPU into two
    /// <see cref="AvVector"/>s, labels written only when their words change.
    /// </summary>
    internal sealed class ArLayer
    {
        private const int LabelMax = 12, SpinMax = 6, InboundMax = 6;
        private const float Near = 0.5f, Wire = 1.2f, Bold = 2.2f, PillarM = 800f, InboundLeadS = 2f;
        /// <summary>The missile's private guidance target, as the shot book reads it (set from the synced target id; a seeker drops it when it loses the target).</summary>
        private static readonly HarmonyLib.AccessTools.FieldRef<Missile, Unit> TargetRef = NativeRefs.Field<Missile, Unit>("target");

        private RectTransform canvasRoot, root;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private AvVector lines, fx;
        private readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[LabelMax];
        private readonly int[] labelKeys = new int[LabelMax];
        private int labelsUsed;
        private readonly char[] buf = new char[48];
        private float lastK;

        // Projection, once per frame: world -> clip -> the layer's 1080-high reference units round the screen centre.
        private Matrix4x4 vp;
        private float halfW, halfH;

        private bool on, showPath, showDomes, showMissiles, showBeacons, showFx;
        private Aircraft aircraft;
        private AvUnits units = AvUnits.Metric;

        // PATH
        private readonly float[] path = new float[ArRules.PathSamples * 3];
        private Vector3 lastVelocity;
        private float lastVelocityAt = float.NaN, wx, wy, wz, collisionS = float.NaN;
        private int pathReach;
        private bool pathOn;

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
        private float objectiveX, objectiveZ;

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

        /// <summary>The glitch's sideways jitter this frame in reference pixels (the TAC BOARD shakes with the layer).</summary>
        internal float JitterX { get; private set; }

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

        /// <summary>Test and showcase hook (dev sims): pretend the pull is this hard, so the path draws in level flight; NaN off.</summary>
        internal float ForcePullG = float.NaN;
        /// <summary>Test and showcase hook: predict as if the flight path turned right at this many degrees a second (a level turn); NaN off.</summary>
        internal float ForceTurnDegS = float.NaN;
        /// <summary>Test and showcase hook: predict as if the flight path pointed this many degrees further nose-down (no G on the pilot); NaN off.</summary>
        internal float ForcePitchDeg = float.NaN;
        /// <summary>Test and showcase hook: stand the home beacon whatever the fuel and damage.</summary>
        internal bool ForceHome;

        public bool Ensure()
        {
            if (root != null && canvasRoot != null) return true;
            root = null;
            if (!NativeRefs.HudCanvas(out _, out Canvas canvas)) return false;
            canvasRoot = (RectTransform)canvas.transform;
            VanillaHudStyle.TryHudFont(out font, out fontMaterial);

            root = Holder(canvasRoot, "BoscaliArLayer");
            root.SetAsFirstSibling();      // under the game's own symbology and the mod's centre alerts: the world is behind the HUD
            Canvas nested = root.gameObject.AddComponent<Canvas>();      // its own batch: a per-frame redraw never rebuilds the rest of the HUD
            nested.overrideSorting = false;
            lines = AvVector.Create(root, "World", 1600);
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
        /// The board's 10 Hz read: the path prediction and its ground check, the SAM rings near enough (once a second), the beacons, new locks and new damage.
        /// <paramref name="show"/> false (every switch off, a pause, the map, no aircraft) clears the layer.
        /// </summary>
        public void Read(bool show, in BoardOptions options, Aircraft own, BoardFrame frame, Airbase field, float now)
        {
            if (!show || own == null || root == null)
            {
                on = false;
                aircraft = null;
                lastVelocityAt = float.NaN;
                return;
            }
            on = true;
            aircraft = own;
            units = frame.Units;
            showPath = options.ArPath;
            showDomes = options.ArDomes;
            showMissiles = options.ArMissiles;
            showBeacons = options.ArBeacons;
            showFx = options.HudFx;
            ReadPath(own);
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

        private void ReadPath(Aircraft own)
        {
            pathOn = false;
            if (!showPath || own.rb == null) { lastVelocityAt = float.NaN; return; }
            Vector3 v = own.rb.velocity;
            float t = Time.time;
            if (!float.IsNaN(lastVelocityAt))
            {
                ArRules.TurnRate(lastVelocity.x, lastVelocity.y, lastVelocity.z, v.x, v.y, v.z, t - lastVelocityAt, out float mx, out float my, out float mz);
                // Half-way toward the new reading: steady enough to read, quick enough to follow a reversal.
                wx += (mx - wx) * 0.5f; wy += (my - wy) * 0.5f; wz += (mz - wz) * 0.5f;
            }
            lastVelocity = v;
            lastVelocityAt = t;
            if (!float.IsNaN(ForceTurnDegS)) { wx = 0f; wy = ForceTurnDegS * Mathf.Deg2Rad; wz = 0f; }
            if (!float.IsNaN(ForcePitchDeg)) v = Quaternion.AngleAxis(ForcePitchDeg, own.transform.right) * v;      // the prediction only; the aircraft flies on
            ArRules.Predict(v.x, v.y, v.z, wx, wy, wz, ArRules.PathStepS, ArRules.PathSamples, path);

            // The first leg that meets the ground (or the sea) ends the path there.
            collisionS = float.NaN;
            pathReach = ArRules.PathSamples;
            Vector3 origin = own.transform.position, from = origin;
            float sea = Datum.LocalSeaY;
            for (int i = 0; i < ArRules.PathSamples; i++)
            {
                Vector3 to = origin + new Vector3(path[i * 3], path[i * 3 + 1], path[i * 3 + 2]);
                float share = float.NaN;
                if (Physics.Linecast(from, to, out RaycastHit hit, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                    share = (hit.point - from).magnitude / Mathf.Max(0.01f, (to - from).magnitude);
                else if (to.y < sea && from.y >= sea)
                    share = (from.y - sea) / Mathf.Max(0.01f, from.y - to.y);
                if (!float.IsNaN(share))
                {
                    collisionS = (i + Mathf.Clamp01(share)) * ArRules.PathStepS;
                    Vector3 at = Vector3.Lerp(from, to, Mathf.Clamp01(share)) - origin;
                    path[i * 3] = at.x; path[i * 3 + 1] = at.y; path[i * 3 + 2] = at.z;
                    pathReach = i + 1;
                    break;
                }
                from = to;
            }
            bool gearDown = own.gearState == LandingGear.GearState.LockedExtended;
            float pull = float.IsNaN(ForcePullG) ? ArRules.PullG(v.magnitude, wx, wy, wz) : ForcePullG;
            pathOn = own.radarAlt > 8f && ArRules.ShowPath(collisionS, gearDown, pull);
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

        /// <summary>Every frame: project and paint what the read decided (the camera and everything in the world move every frame).</summary>
        public void Frame(MissileTracker tracker, ThreatWatch threats, float now)
        {
            if (root == null) return;
            bool show = on && aircraft != null && !aircraft.disabled;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            JitterX = 0f;
            if (!show) { Clear(); return; }
            CameraStateManager state = SceneSingleton<CameraStateManager>.i;
            Camera cam = state != null ? state.mainCamera : null;
            float k = canvasRoot.rect.height / 1080f;
            if (cam == null || !(k > 0.01f)) return;
            if (k != lastK)
            {
                root.localScale = new Vector3(k, k, 1f);
                lastK = k;
            }
            halfW = canvasRoot.rect.width / k * 0.5f;
            halfH = 540f;
            vp = cam.projectionMatrix * cam.worldToCameraMatrix;

            VanillaHudStyle.Palette palette = VanillaHudStyle.Colours;
            bool calm = AvFxDriver.ReducedMotion;
            AvQuadBuffer b = lines.Buffer, f = fx.Buffer;
            b.Clear();
            f.Clear();
            labelsUsed = 0;
            Vector3 eye = cam.transform.position;

            PaintPath(b, palette);
            PaintDomes(b, palette, eye, now, calm);
            PaintBeacons(b, palette, eye);
            PaintMissiles(b, palette, tracker, threats, now, calm);
            PaintSpins(f, palette, now, calm);
            PaintGlitch(f, palette, now, calm);

            for (int i = labelsUsed; i < LabelMax; i++)
                if (labels[i].gameObject.activeSelf) labels[i].gameObject.SetActive(false);
            lines.Commit();
            fx.Commit();
            root.anchoredPosition = new Vector2(JitterX, 0f);
        }

        // ------------------------------------------------------------------ PATH

        private void PaintPath(AvQuadBuffer b, VanillaHudStyle.Palette palette)
        {
            PathShown = PathAlert = false;
            if (!pathOn || pathReach <= 0) return;
            bool alert = !float.IsNaN(collisionS);
            Rgba ink = (alert ? palette.Alert : palette.AllClear).ToRgba();
            Vector3 origin = aircraft.transform.position, prev = origin;
            float baseAlpha = alert ? 1f : 0.85f;
            for (int i = 0; i < pathReach; i++)
            {
                Vector3 p = origin + new Vector3(path[i * 3], path[i * 3 + 1], path[i * 3 + 2]);
                float fade = baseAlpha * (1f - 0.6f * i / ArRules.PathSamples);
                if (i > 0) Seg(b, prev, p, 1.8f, ink.WithAlpha(fade * 0.85f));
                if (Project(p, out float x, out float y))
                {
                    // A chevron each second pointing along the path, a bead between: the eye reads direction and pace at once.
                    if ((i & 1) == 1 && Project(prev, out float px, out float py))
                        AvStrokes.Chevron(b, x, y, 11f, Mathf.Atan2(y - py, x - px) * Mathf.Rad2Deg, 2.2f, ink.WithAlpha(fade));
                    else AvStrokes.Diamond(b, x, y, 3.5f, 1.8f, ink.WithAlpha(fade));
                }
                prev = p;
            }
            PathShown = true;
            if (!alert) return;
            PathAlert = true;
            // The impact: an X on the spot, a ring on the ground round it, and the seconds left.
            Ring(b, prev, 60f, Bold, ink);
            if (Project(prev, out float ix, out float iy))
            {
                AvStrokes.Cross(b, ix, iy, 14f, 3f, ink);
                int seconds = Mathf.Max(0, Mathf.CeilToInt(collisionS));
                Label(ix, iy + 20f, 1_000_000 + seconds, ink, "TERRAIN ", seconds, "S", TopCentre.NamePt);
            }
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
                float fade = ArRules.Fade(Vector3.Distance(eye, site), 4000f, 60000f);
                Rgba ink = (dome.Inside ? palette.Alert : palette.Warning).ToRgba().WithAlpha(fade * (dome.Inside ? 0.7f : 0.5f));
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
                Pillar(b, at, palette.AllClear.ToRgba(), eye, diamond: true);
                if (Project(at + Vector3.up * PillarHeight(Vector3.Distance(eye, at)), out float x, out float y))
                    LabelDistance(x, y + 12f, 4_000_000, palette.AllClear.ToRgba(), "HOME ", Vector3.Distance(aircraft.transform.position, at));
                HomeShown = true;
            }
            if (objectiveOn)
            {
                Vector3 at = new GlobalPosition(objectiveX, Ground(objectiveX, objectiveZ), objectiveZ).ToLocalPosition();
                Rgba ink = palette.Warning.ToRgba();
                Pillar(b, at, ink, eye, diamond: false);
                if (Project(at + Vector3.up * PillarHeight(Vector3.Distance(eye, at)), out float x, out float y))
                    LabelDistance(x, y + 12f, 5_000_000, ink, "OBJ ", Vector3.Distance(aircraft.transform.position, at));
                ObjectiveShown = true;
            }
        }

        /// <summary>A pillar's height: about 2.5 degrees tall from far out, never under <see cref="PillarM"/> nor over 6 km.</summary>
        private static float PillarHeight(float distance) => Mathf.Clamp(distance * 0.045f, PillarM, 6000f);

        /// <summary>A beam of light from the ground: the line, three ticks up it, a diamond (home) or a square (objective) on top; whole up close, a third far out.</summary>
        private void Pillar(AvQuadBuffer b, Vector3 foot, Rgba ink, Vector3 eye, bool diamond)
        {
            float far = Vector3.Distance(eye, foot);
            ink = ink.WithAlpha(ArRules.Fade(far, 3000f, 50000f));
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
                AvStrokes.Bracket(b, tx - 6f, ty - 6f, 12f, 12f, 4f, 1.6f, ink);
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
                    Vector3 to = target.transform.position;
                    float range = Vector3.Distance(pos, to);
                    Vector3 targetVelocity = target.rb != null ? target.rb.velocity : Vector3.zero;
                    float closure = range > 1f ? Vector3.Dot(velocity - targetVelocity, (to - pos) / range) : 0f;
                    DashedSeg(b, pos, to, ink.WithAlpha(0.55f));
                    bool first = true;
                    for (int j = 0; j < i; j++) if (own[j] != null && NativeRefs.Get(TargetRef, own[j]) == target) first = false;
                    if (first && Project(to, out float tx, out float ty))
                    {
                        AvStrokes.Bracket(b, tx - 14f, ty - 14f, 28f, 28f, 6f, 1.6f, ink);
                        if (closure > 5f)
                        {
                            int seconds = Mathf.Clamp(Mathf.CeilToInt(range / closure), 0, 99);
                            Label(tx + 62f, ty - 7f, 6_000_000 + i * 1000 + seconds, ink, "IMPACT ", seconds, "S");      // right of the bracket: the game writes the name above it, the range under it
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

        private void PaintSpins(AvQuadBuffer f, VanillaHudStyle.Palette palette, float now, bool calm)
        {
            SpinsShown = SpinsActive = 0;
            Rgba ink = palette.AllClear.ToRgba();
            for (int s = 0; s < SpinMax; s++)
            {
                Unit u = spinUnits[s];
                if (u == null) continue;
                if (u.disabled || !ArRules.Spin(now - spinAt[s], calm, out float scale, out float angle, out float alpha))
                {
                    spinUnits[s] = null;
                    continue;
                }
                SpinsActive++;
                if (!Project(u.transform.position, out float x, out float y)) continue;
                RotatedBracket(f, x, y, 28f * scale, angle, 2f, ink.WithAlpha(alpha));      // closes just outside the game's own target box
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
                // REDUCED MOTION: one still band across the top of the view, no shake.
                AvStrokes.Fill(f, -halfW, halfH - 6f, halfW * 2f, 6f, ink.WithAlpha(0.25f * life));
                return;
            }
            JitterX = ArRules.Jitter(age, glitchSeed);
            for (int i = 0; i < ArRules.GlitchBars; i++)
            {
                ArRules.Bar(glitchSeed, i, out float share, out float thick);
                float y = -halfH + share * halfH * 2f + ArRules.Noise(glitchSeed, (int)(age * 30f) + i) * 6f;
                AvStrokes.Fill(f, -halfW, y, halfW * 2f, thick, ink.WithAlpha(0.18f * life));
            }
        }

        // ------------------------------------------------------------------ drawing helpers

        private bool Project(Vector3 world, out float x, out float y)
        {
            Vector4 c = vp * new Vector4(world.x, world.y, world.z, 1f);
            if (c.w < Near) { x = y = 0f; return false; }
            x = c.x / c.w * halfW;
            y = c.y / c.w * halfH;
            return x > -halfW * 1.5f && x < halfW * 1.5f && y > -halfH * 1.5f && y < halfH * 1.5f;
        }

        /// <summary>A world segment, cut at the near plane and dropped when it lies wholly off one side of the view.</summary>
        private void Seg(AvQuadBuffer b, Vector3 a, Vector3 c, float width, Rgba ink)
        {
            if (!Clip(a, c, out float ax, out float ay, out float bx, out float by)) return;
            AvStrokes.Line(b, ax, ay, bx, by, width, ink);
        }

        private void DashedSeg(AvQuadBuffer b, Vector3 a, Vector3 c, Rgba ink)
        {
            if (!Clip(a, c, out float ax, out float ay, out float bx, out float by)) return;
            AvStrokes.DashedLine(b, ax, ay, bx, by, 6f, 5f, 1.4f, ink);
        }

        private bool Clip(Vector3 a, Vector3 c, out float ax, out float ay, out float bx, out float by)
        {
            ax = ay = bx = by = 0f;
            Vector4 p = vp * new Vector4(a.x, a.y, a.z, 1f), q = vp * new Vector4(c.x, c.y, c.z, 1f);
            if (p.w < Near && q.w < Near) return false;
            if (p.w < Near) p = Vector4.Lerp(p, q, (Near - p.w) / (q.w - p.w));
            else if (q.w < Near) q = Vector4.Lerp(q, p, (Near - q.w) / (p.w - q.w));
            ax = p.x / p.w * halfW; ay = p.y / p.w * halfH;
            bx = q.x / q.w * halfW; by = q.y / q.w * halfH;
            // Cut to the screen (Liang-Barsky): a line running far off-screen (a point just past the near plane projects absurdly far) costs only what shows, and a
            // dashed one never fills the buffer with dashes nobody sees.
            float lx = halfW * 1.05f, ly = halfH * 1.05f, dx = bx - ax, dy = by - ay, t0 = 0f, t1 = 1f;
            if (!Edge(-dx, ax + lx, ref t0, ref t1) || !Edge(dx, lx - ax, ref t0, ref t1) || !Edge(-dy, ay + ly, ref t0, ref t1) || !Edge(dy, ly - ay, ref t0, ref t1))
                return false;
            float sx = ax, sy = ay;
            ax = sx + dx * t0; ay = sy + dy * t0;
            bx = sx + dx * t1; by = sy + dy * t1;
            return true;
        }

        /// <summary>One edge of the Liang-Barsky clip: false when the line lies wholly outside it.</summary>
        private static bool Edge(float p, float q, ref float t0, ref float t1)
        {
            if (p == 0f) return q >= 0f;
            float r = q / p;
            if (p < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
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

        /// <summary>Four corner arms of a square of half-size <paramref name="h"/> turned by <paramref name="angleDeg"/>.</summary>
        private static void RotatedBracket(AvQuadBuffer b, float x, float y, float h, float angleDeg, float width, Rgba ink)
        {
            float a = angleDeg * Mathf.Deg2Rad, cos = Mathf.Cos(a), sin = Mathf.Sin(a), arm = Mathf.Min(8f, h * 0.45f);
            for (int c = 0; c < 4; c++)
            {
                float sx = (c & 1) == 0 ? -1f : 1f, sy = (c & 2) == 0 ? -1f : 1f;
                float cx = sx * h, cy = sy * h;
                // Each corner: one arm along x toward the centre, one along y.
                Arm(b, x, y, cx, cy, cx - sx * arm, cy, cos, sin, width, ink);
                Arm(b, x, y, cx, cy, cx, cy - sy * arm, cos, sin, width, ink);
            }
        }

        private static void Arm(AvQuadBuffer b, float x, float y, float ax, float ay, float bx, float by, float cos, float sin, float width, Rgba ink) =>
            AvStrokes.Line(b, x + ax * cos - ay * sin, y + ax * sin + ay * cos, x + bx * cos - by * sin, y + bx * sin + by * cos, width, ink);

        // ------------------------------------------------------------------ labels

        /// <summary>A label at (x, y): "<paramref name="head"/><paramref name="number"/><paramref name="tail"/>" (just the head for a negative number), written only when <paramref name="key"/> changed.</summary>
        private void Label(float x, float y, int key, Rgba ink, string head, int number, string tail, float size = TopCentre.LabelPt)
        {
            TextMeshProUGUI t = NextLabel(x, y, ink, size);
            if (t == null || labelKeys[labelsUsed - 1] == key) return;
            labelKeys[labelsUsed - 1] = key;
            t.text = number < 0 ? head : head + number + tail;
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
            t.rectTransform.anchoredPosition = new Vector2(x, y);
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
            JitterX = 0f;
            Clear();
            if (root != null) root.gameObject.SetActive(false);
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            canvasRoot = null;
            Hide();
            domeCount = 0;
            lastTargets.Clear();
            System.Array.Clear(spinUnits, 0, SpinMax);
            damageAircraft = null;
            picture = null;
        }
    }
}
