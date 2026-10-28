using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>One linkable drone as the drone views show it; refreshed at 5 Hz by <see cref="DroneLinkService"/>.</summary>
    internal sealed class DroneView
    {
        public Missile Missile;
        public VanguardRole Role;
        public string Callsign = "", Type = "", Job = "", State = "";
        public DroneMode Mode;
        /// <summary>WPT: the point (global). HAND: x = commanded course (degrees), y = commanded altitude (<see cref="DroneLink.HandPoint"/>).</summary>
        public Vector3 Point;
        /// <summary>Global position.</summary>
        public Vector3 Position;
        public float RangeM, BearingDeg, HeadingDeg, AltM, SpeedMs, Quality, Age;
        public float SampledAt = -1f;
    }

    /// <summary>
    /// The pilot's end of the VANGUARD drone link, client-local: the local aircraft's linkable drones (<see cref="DroneRoster"/>), the ONE
    /// linked drone, a mirror of the orders sent for them (the host owns the truth and applies the same link rule, as DESIGNATE does), and
    /// the one feed camera on the linked drone that the UAV terminal, the UAV MFD and the HUD feed card all show. Also the radial menu's
    /// <see cref="IDroneCommand"/>.
    /// </summary>
    [DefaultExecutionOrder(100)] // the lens is placed in LateUpdate, after the floating-origin shift (CameraStateManager, order 2)
    internal sealed class DroneLinkService : MonoBehaviour, ISceneService, IDroneCommand
    {
        private const float RefreshInterval = 0.2f, WantedFor = 0.5f, LostHold = 1.6f, SlewDegPerSec = 120f, SpiInterval = 0.2f;
        private static readonly float[] ZoomFov = { 50f, 20f, 8f };
        private static readonly string[] ZoomWords = { "1X", "3X", "7X" };

        private VanguardNet net;
        private readonly List<DroneRoster.Entry> entries = new List<DroneRoster.Entry>(16);
        private readonly List<DroneView> views = new List<DroneView>(16);
        private readonly Dictionary<Missile, SteerOrders.Order> mirror = new Dictionary<Missile, SteerOrders.Order>();
        private readonly List<Missile> stale = new List<Missile>();
        private readonly FeedCamera feed = new FeedCamera("BoscaliDroneFeed", 480, 270, FeedLook.Eo, pointFilter: false) { Fps = 20f, FieldOfView = 50f };
        private Missile linked;
        private string linkedName = "";
        private Aircraft owner;
        private float nextRefresh, wantedUntil, switchedAt = -9f, lostAt = -9f;
        private bool feedShown, feedLive;
        private int zoom;
        private DroneTerminalWindow terminal;
#pragma warning disable CS0649 // set only by the dev-build test seam
        private Texture testFeed;
#pragma warning restore CS0649
        private Missile lensOn;
        private Quaternion lens = Quaternion.identity;
        private Vector3 lensAt;
        private float nextSpi;

#if !PUBLIC_RELEASE
#endif

        public void Configure(VanguardNet network) => net = network;

        // ---- what the views read -------------------------------------------------------------------------------------------

        public int Count { get; private set; }
        public DroneView this[int index] => views[index];
        public int LinkedIndex { get; private set; } = -1;
        public DroneView Linked => LinkedIndex >= 0 ? views[LinkedIndex] : null;
        /// <summary>The feed picture of the linked drone (live, or the frozen last frame while <see cref="Lost"/>); null with none.</summary>
        public Texture Feed => testFeed != null ? testFeed : feedLive || Lost ? feed.Texture : null;
        /// <summary>The linked drone just went down: its last frame is held under SIGNAL LOST.</summary>
        public bool Lost => Time.unscaledTime - lostAt < LostHold;
        public string LostName { get; private set; } = "";
        /// <summary>Seconds since the link moved to another drone (the views' static burst).</summary>
        public float SinceSwitch => Time.unscaledTime - switchedAt;
        public string ZoomWord => ZoomWords[zoom];
        public bool TerminalOpen => terminal != null && terminal.IsOpen;
        public bool HasAircraft => owner != null || testFeed != null;
        /// <summary>The local aircraft's global position and true heading at the last refresh (the plot's centre).</summary>
        public Vector3 SelfPosition { get; private set; }
        public float SelfHeading { get; private set; }
        /// <summary>Recent link events, newest last ("19:42 REMORA 2 · HAND"), at most <see cref="LogLines"/>.</summary>
        public readonly List<string> Log = new List<string>(LogLines + 1);
        public const int LogLines = 12;

        /// <summary>The pilot's live designation (DESIGNATE), which the feed tracks and boxes; null without one.</summary>
        public Unit Mark => net != null ? net.Designated : null;
        /// <summary>The sensor ball is slewed onto <see cref="Mark"/> (the linked drone is on its own job and a designation is live).</summary>
        public bool Tracking { get; private set; }
        /// <summary>Where the ball points: TRK (the designation), FWD (hand-flown: ahead), POINT (the waypoint), STARE (down ahead).</summary>
        public string LensWord { get; private set; } = "STARE";
        /// <summary>Degrees: the ball's elevation (down is negative) and its azimuth off the drone's nose (right is positive).</summary>
        public float LensElevation { get; private set; } = -DroneLink.StareDeg;
        public float LensAzimuth { get; private set; }
        /// <summary>The lens's vertical field of view in degrees (the zoom step).</summary>
        public float FieldOfView => ZoomFov[zoom];
        /// <summary>Sensor point of interest, 5 Hz: where the boresight meets the ground (or the tracked unit), its slant range and global place.</summary>
        public bool HasSpi { get; private set; }
        public float SpiSlantM { get; private set; }
        public Vector3 SpiGlobal { get; private set; }
        /// <summary>The video frame counter the feed prints.</summary>
        public int FrameNumber => Mathf.FloorToInt(Time.unscaledTime * feed.Fps);
        /// <summary>The local aircraft (the feed marks it OWN when the lens sees it).</summary>
        public Aircraft Own => owner;

        /// <summary>Where a world point (local space) falls in the feed picture, 0..1 from the bottom left; false behind the lens.</summary>
        public bool Project(Vector3 world, out Vector2 viewport) => feed.Project(world, out viewport);

        /// <summary>The designation's place in the picture, its name and range from the linked drone; false when it is not in view.</summary>
        public bool MarkInView(out Vector2 viewport, out string name, out float rangeM)
        {
            viewport = default;
            name = "";
            rangeM = 0f;
#if !PUBLIC_RELEASE
            if (testFeed != null && Linked != null) { viewport = new Vector2(0.6f, 0.42f); name = "ZSU-23-4"; rangeM = SpiSlantM; return Tracking; }
#endif
            Unit mark = Mark;
            DroneView v = Linked;
            if (mark == null || v == null || v.Missile == null || !Project(mark.transform.position, out viewport)) return false;
            name = mark.unitName;
            rangeM = Vector3.Distance(mark.transform.position, v.Missile.transform.position);
            return viewport.x > 0f && viewport.x < 1f && viewport.y > 0f && viewport.y < 1f;
        }

        /// <summary>The linked REMORA's rounds, flares and phase (every peer has them); false for any other drone.</summary>
        public bool Stores(out RemoraView view)
        {
            view = default;
            DroneView v = Linked;
            if (v == null || v.Role != VanguardRole.Drone) return false;
#if !PUBLIC_RELEASE
            if (testFeed != null) { view = new RemoraView { Fired = 1, Flares = 4, Phase = RemoraPhase.Escort }; return true; }
#endif
            return v.Missile != null && RemoraBoard.Views.TryGetValue(v.Missile.persistentID.Id, out view);
        }

        // A burst of the same kind of order (twelve WPT SET clicks, a string of course changes) is one line, the newest.
        private void Note(string text)
        {
            string line = DroneLink.Clock(Time.timeSinceLevelLoad) + "  " + text;
            int dot = text.IndexOf(" · ");
            int space = dot < 0 ? -1 : text.IndexOf(' ', dot + 3);
            string kind = space > 0 ? text.Substring(0, space) : text;    // "REMORA 2 · CRS", "REMORA 2 · WPT"
            if (Log.Count > 0)
            {
                string last = Log[Log.Count - 1];
                if (last.Substring(last.IndexOf("  ") + 2).StartsWith(kind)) { Log[Log.Count - 1] = line; return; }
            }
            Log.Add(line);
            if (Log.Count > LogLines) Log.RemoveAt(0);
        }

        /// <summary>A view shows the feed this frame: the camera runs while someone asks.</summary>
        public void Want() => wantedUntil = Time.unscaledTime + WantedFor;

        // ---- IDroneCommand -----------------------------------------------------------------------------------------------------

        public bool Striking => net != null && net.Striking;
        public bool CanStrike => net != null && net.CanStrike;

        public int Ready
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Count; i++) if (views[i].Role == VanguardRole.Drone) n++;
                return n;
            }
        }

        public void Strike() => net?.Strike();
        public void Screen() => net?.Screen();
        public bool RamMode => net != null && net.RamMode;
        public void ToggleRamMode() => net?.SetRamMode(!RamMode);
        public int Linkable => Count;
        public string Name(int index) => index >= 0 && index < Count ? views[index].Callsign : "";
        public string State(int index) => index >= 0 && index < Count ? views[index].State : "";
        public bool Ordered => Linked != null && Linked.Mode != DroneMode.Auto;
        public bool FeedShown => feedShown;

        public void Link(int index)
        {
            if (index < 0 || index >= Count) return;
            if (views[index].Missile != linked)
            {
                linked = views[index].Missile;
                linkedName = views[index].Callsign;
                switchedAt = Time.unscaledTime;
                Note("LINKED " + linkedName);
                AvUiSound.Play(AvUiCue.Navigate);
            }
            LinkedIndex = index;
            lostAt = -9f;
        }

        /// <summary>Moves the link only: every drone keeps flying its own order (2026-10-26; the stick no longer follows the link).</summary>
        public void Cycle(int step) => Link(DroneLink.Step(Count, LinkedIndex, step));

        private static Vector3 Hold(DroneView v) => DroneLink.HandPoint(v.HeadingDeg, v.AltM);

        /// <summary>Click-to-steer on the feed (<paramref name="viewport"/> 0..1 from the bottom left): the linked drone turns to the
        /// clicked bearing and climbs or dives by how far above or below the cross the click is. Takes hand control.</summary>
        public void SteerAt(Vector2 viewport)
        {
            DroneView v = Linked;
            if (v == null || v.Missile == null || lensOn != v.Missile) return;
            Vector3 dir = DroneLink.LookDirection(lens, FieldOfView, 16f / 9f, viewport);
            Order(v, DroneMode.Hand, DroneLink.SteerTo(dir, lens * Vector3.forward, v.Mode == DroneMode.Hand ? v.Point.y : v.AltM));
        }

        /// <summary>Right-click on the feed: the linked drone flies to the ground under the click and circles it (WPT). False when the
        /// click is on open sky.</summary>
        public bool SteerGround(Vector2 viewport)
        {
            DroneView v = Linked;
            if (v == null || v.Missile == null || lensOn != v.Missile) return false;
            if (!Ground(lensAt, DroneLink.LookDirection(lens, FieldOfView, 16f / 9f, viewport), out Vector3 hit)) return false;
            SendTo(hit.ToGlobalPosition().AsVector3());
            return true;
        }

        /// <summary>Raises (or lowers) the hand-flown drone's commanded height; takes hand control on the present course first.</summary>
        public void Climb(float metres)
        {
            DroneView v = Linked;
            if (v == null) return;
            Vector3 p = v.Mode == DroneMode.Hand ? v.Point : Hold(v);
            Order(v, DroneMode.Hand, DroneLink.HandPoint(p.x, p.y + metres));
        }

        public void SendToLook()
        {
            Camera cam = SceneSingleton<CameraStateManager>.i != null ? SceneSingleton<CameraStateManager>.i.mainCamera : Camera.main;
            if (cam == null) return;
            Vector3 from = cam.transform.position, dir = cam.transform.forward;
            // Looking at the horizon finds no ground inside the link: send it that way instead, a full-link distance out.
            if (!Ground(from, dir, out Vector3 hit))
            {
                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                hit = from + (flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward) * DroneLink.FullRangeM;
            }
            SendTo(hit.ToGlobalPosition().AsVector3());
        }

        public void SendToTarget()
        {
            Unit target = net != null ? net.Target : null;
            if (target != null) SendTo(target.GlobalPosition().AsVector3());
        }

        /// <summary>FLY MY HEADING: the linked drone takes your heading and your height (the C-menu's way to steer it from the cockpit).</summary>
        public void MatchNose()
        {
            if (Linked == null) Link(0);
            DroneView v = Linked;
            if (v == null || owner == null) return;
            Order(v, DroneMode.Hand, DroneLink.HandPoint(SelfHeading, owner.GlobalPosition().y));
        }

        public void ToggleFeed()
        {
            feedShown = !feedShown;
            if (feedShown && Linked == null) Link(0);
            AvUiSound.Play(feedShown ? AvUiCue.Engage : AvUiCue.Release);
        }

        public void OpenTerminal()
        {
            if (terminal == null) terminal = DroneTerminalWindow.Create(transform, this);
            if (Linked == null) Link(0);
            terminal?.Open();
        }

        // ---- terminal-only orders --------------------------------------------------------------------------------------------

        /// <summary>WPT for the linked drone at a ground point (x/z); it circles there at its present height.</summary>
        public void SendTo(Vector3 ground)
        {
            if (Linked == null) Link(0);
            DroneView v = Linked;
            if (v == null) return;
            Order(v, DroneMode.Waypoint, new Vector3(ground.x, Mathf.Max(v.AltM, 300f), ground.z));
        }

        public void Release()
        {
            if (Linked != null && Linked.Mode != DroneMode.Auto) Order(Linked, DroneMode.Auto, Vector3.zero);
        }

        public void ToggleLook()
        {
            feed.Look = feed.Look == FeedLook.Eo ? FeedLook.WhiteHot : FeedLook.Eo;
            switchedAt = Time.unscaledTime - 0.35f; // a short tear, not a full re-acquire
            AvUiSound.Play(AvUiCue.Press);
        }

        public void CycleZoom()
        {
            zoom = (zoom + 1) % ZoomFov.Length;
            AvUiSound.Play(AvUiCue.Press);
        }

        public string LookWord => feed.Look == FeedLook.WhiteHot ? "IR WHOT" : "EO DAY";

        private void Order(DroneView v, DroneMode mode, Vector3 point)
        {
            if (mode == DroneMode.Auto) mirror.Remove(v.Missile);
            else mirror[v.Missile] = new SteerOrders.Order { Mode = mode, Point = new GlobalPosition(point) };
            bool steer = mode == DroneMode.Hand && v.Mode == DroneMode.Hand; // a new course for a drone already in hand
            net?.SendLink(v.Missile, mode, new GlobalPosition(point));
            Note(v.Callsign + " · " + (mode == DroneMode.Auto ? "BACK ON TASK" : mode == DroneMode.Waypoint ? "WPT SET"
                : (steer ? "CRS " : "HAND · CRS ") + DroneFeedView.Three(point.x) + "° · ALT " + AvNum.Fixed(point.y, 0) + " M"));
            AvUiSound.Play(steer ? AvUiCue.Press : mode == DroneMode.Hand ? AvUiCue.Engage : mode == DroneMode.Auto ? AvUiCue.Release : AvUiCue.Confirm);
            Refresh();
        }

        // ---- lifecycle -------------------------------------------------------------------------------------------------------

        public void ResetForScene()
        {
            if (terminal != null) Destroy(terminal.gameObject);
            terminal = null;
            feed.Release();
            entries.Clear();
            mirror.Clear();
            linked = null;
            owner = null;
            Count = 0;
            LinkedIndex = -1;
            feedShown = feedLive = false;
            lostAt = switchedAt = -9f;
            lensOn = null;
            HasSpi = Tracking = false;
            Log.Clear();
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        private void LateUpdate()
        {
            DroneView v = Linked;
            feedLive = v != null && v.Missile != null && !v.Missile.disabled && (Time.unscaledTime < wantedUntil || feedShown || v.Mode == DroneMode.Hand);
            if (!feedLive) { feed.Idle(); return; }
            Transform t = v.Missile.transform;
            // A stabilised sensor ball under the nose: it slews (a new link snaps), never rolls with the drone.
            Vector3 at = t.position + t.forward * (v.Missile.maxRadius + 0.6f) - t.up * 0.3f;
            Quaternion want = Aim(v, t, at);
            lens = v.Missile != lensOn ? want : Quaternion.RotateTowards(lens, want, SlewDegPerSec * Time.deltaTime);
            lensOn = v.Missile;
            lensAt = at;
            Vector3 look = lens * Vector3.forward;
            LensElevation = Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg;
            LensAzimuth = Mathf.DeltaAngle(DroneLink.Bearing(Vector3.zero, t.forward), DroneLink.Bearing(Vector3.zero, look));
            feed.FieldOfView = ZoomFov[zoom];
            feedLive = feed.Shoot(at, lens, Time.unscaledTime);
            if (Time.unscaledTime >= nextSpi) { nextSpi = Time.unscaledTime + SpiInterval; Spi(at, look); }
        }

        // HAND looks ahead (the pilot steers by the picture), WPT stares at the waypoint, a live designation is tracked on the drone's
        // own job, else a stare down ahead.
        private Quaternion Aim(DroneView v, Transform t, Vector3 at)
        {
            Vector3 flat = new Vector3(t.forward.x, 0f, t.forward.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            Quaternion ahead = Quaternion.LookRotation(flat.normalized);
            Tracking = false;
            if (v.Mode == DroneMode.Hand) { LensWord = "FWD"; return ahead * Quaternion.Euler(DroneLink.ForwardDeg, 0f, 0f); }
            Unit mark = Mark;
            Vector3 to = Vector3.zero;
            if (v.Mode == DroneMode.Waypoint)
            {
                Vector3 point = new GlobalPosition(v.Point).ToLocalPosition();
                point.y = Datum.LocalSeaY;      // the point carries the orbit height; the ball looks at the ground under it
                to = point - at;
            }
            else if (mark != null) to = mark.transform.position - at;
            if (to.sqrMagnitude > 1f && to.sqrMagnitude < DroneLink.MaxRangeM * DroneLink.MaxRangeM)
            {
                Tracking = v.Mode == DroneMode.Auto;
                LensWord = Tracking ? "TRK" : "POINT";
                return Quaternion.LookRotation(to);
            }
            LensWord = "STARE";
            return ahead * Quaternion.Euler(DroneLink.StareDeg, 0f, 0f);
        }

        // The sensor point of interest: the tracked unit, else the first terrain or the sea along the boresight.
        private void Spi(Vector3 at, Vector3 look)
        {
            Unit mark = Tracking ? Mark : null;
            Vector3 hit;
            if (mark != null) hit = mark.transform.position;
            else if (!Ground(at, look, out hit)) { HasSpi = false; return; }
            HasSpi = true;
            SpiSlantM = Vector3.Distance(at, hit);
            SpiGlobal = hit.ToGlobalPosition().AsVector3();
        }

        // The first terrain or the sea along a ray (local space), inside the link limit.
        private static bool Ground(Vector3 from, Vector3 dir, out Vector3 hit)
        {
            if (Physics.Raycast(from, dir, out RaycastHit ray, DroneLink.MaxRangeM, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)) { hit = ray.point; return true; }
            float sea = dir.y < -0.01f ? (from.y - Datum.LocalSeaY) / -dir.y : float.MaxValue;
            hit = from + dir * Mathf.Min(sea, DroneLink.MaxRangeM);
            return sea < DroneLink.MaxRangeM;
        }

        private void Refresh()
        {
            Aircraft me = GameManager.GetLocalAircraft(out Aircraft local) && local != null && !local.disabled ? local : null;
            if (me != owner)
            {
                owner = me;          // a respawn starts clean
                linked = null;
                mirror.Clear();
            }
            DroneRoster.Mine(me, entries);
            while (views.Count < entries.Count) views.Add(new DroneView());
            Count = entries.Count;
            stale.Clear();
            foreach (Missile m in mirror.Keys) if (m == null || m.disabled) stale.Add(m);
            foreach (Missile m in stale) mirror.Remove(m);

            int found = -1;
            Vector3 self = me != null ? me.GlobalPosition().AsVector3() : Vector3.zero;
            SelfPosition = self;
            SelfHeading = me != null ? DroneLink.Bearing(Vector3.zero, me.transform.forward) : 0f;
            float now = Time.unscaledTime;
            float linkScale = WatchtowerPod.LinkScale(me); // the relay pod stretches the link
            for (int i = 0; i < Count; i++)
            {
                DroneRoster.Entry e = entries[i];
                DroneView v = views[i];
                Vector3 pos = e.Missile.GlobalPosition().AsVector3();
                if (v.Missile != e.Missile)
                {
                    v.Missile = e.Missile;
                    v.Role = e.Role;
                    v.Callsign = e.Callsign;
                    v.Type = RemoraBoard.IsManta(e.Missile) ? "MANTA" : DroneLink.Type(e.Role);
                    v.Job = DroneLink.Job(e.Role);
                    v.SampledAt = -1f;
                    v.SpeedMs = 0f;
                }
                // Game time: paused, the drone stands still and its speed must not decay to zero on the clock.
                if (v.SampledAt > 0f && Time.time - v.SampledAt > 0.05f)
                    v.SpeedMs = Mathf.Lerp(v.SpeedMs, Vector3.Distance(pos, v.Position) / (Time.time - v.SampledAt), 0.5f);
                if (v.SampledAt <= 0f || Time.time - v.SampledAt > 0.05f) { v.SampledAt = Time.time; v.Position = pos; }
                v.AltM = pos.y;
                Vector3 fwd = e.Missile.transform.forward;
                v.HeadingDeg = DroneLink.Bearing(Vector3.zero, fwd);
                v.RangeM = me != null ? Vector3.Distance(self, pos) : 0f;
                v.BearingDeg = DroneLink.Bearing(self, pos);
                v.Quality = me != null ? DroneLink.Quality(v.RangeM, linkScale) : 0f;
                v.Age = Time.time - e.SeenAt;
                if (mirror.TryGetValue(e.Missile, out SteerOrders.Order order) && !DroneLink.InRange(v.RangeM, linkScale)) { mirror.Remove(e.Missile); order = default; } // the host drops it too
                v.Mode = mirror.TryGetValue(e.Missile, out order) ? order.Mode : DroneMode.Auto;
                v.Point = order.Point.AsVector3();
                v.State = DroneLink.Word(v.Mode) + " · " + AvNum.Fixed(v.RangeM / 1000f, 1) + " KM";
                if (e.Missile == linked) found = i;
            }
            if (linked != null && found < 0)
            {
                // The linked drone is gone: hold its last frame under SIGNAL LOST, then link the next one.
                LostName = linkedName;
                Note("SIGNAL LOST " + linkedName);
                lostAt = now;
                linked = null;
            }
            LinkedIndex = found;
            if (linked == null && Count > 0 && !Lost && lostAt > 0f) { lostAt = -9f; Link(0); }
        }
    }
}
