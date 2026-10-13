using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Autopilot.Configuration;
using BoscaliSummer.Modules.Autopilot.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// Autopilot's provider of the APPROACH page's feed (<see cref="ApproachScope"/>, HUD v8): where the local aircraft is against the runway it is landing on, the ILS
    /// deviations, the speed it should fly and the configuration. While the landing autopilot flies, the runway, the chain stage and the speed target are the controller's own
    /// (<see cref="AutopilotLandController.TryHudLanding"/>); by hand it is the nearest friendly field's landing runway, taken from its nearer end (so the localizer's sign is right
    /// from either side) and held for a second at a time, gear out and not climbing within twelve kilometres, or rolling out on that runway after an approach. A vertical lander
    /// over a pad gets the pad's place instead of a runway. Answers from the aircraft and the runway's transforms (a few vector operations, no scans except the once-a-second
    /// runway pick, no allocation); it is called every frame while the page shows. Client-local: everything is read from the local aircraft and the synced airbase.
    /// </summary>
    internal sealed class ApproachScopeFeed : MonoBehaviour, ISceneService
    {
        private const float PickSeconds = 1f, NearM = 12000f, RollMinMps = 8f, RollGraceSeconds = 60f, ClimbMps = 2f, ForwardFlightMps = 25f;

        private AutopilotSettings settings;
        private AutopilotLandController controller;
        private ScopeProvider<ApproachScope> provider;

        // The hand-flown pick: the field, its landing runway and which end is the threshold (kept unless the other end is clearly nearer).
        private Airbase field;
        private Airbase.Runway runway;
        private bool reverse, hasPick;
        private float nextPick, airborneAt = -1000f;
        private Airbase namedField, namedRunwayField;
        private Airbase.Runway namedRunway;
        private bool namedReverse;
        private string fieldName, runwayName;

        internal void Configure(AutopilotSettings config, AutopilotLandController landing)
        {
            settings = config;
            controller = landing;
        }

        private void Awake()
        {
            provider = Read;
            ScopeFeeds.Register(provider);
        }

        private void OnDestroy() => ScopeFeeds.Unregister(provider);

        public void ResetForScene()
        {
            field = null;
            runway = null;
            hasPick = false;
            nextPick = 0f;
            airborneAt = -1000f;
            namedField = null;
            namedRunway = null;
        }

        private bool Read(out ApproachScope d)
        {
            d = default;
            if (!GameManager.GetLocalAircraft(out Aircraft a) || a == null || a.disabled || a.HasEjected()) return false;
            AutolandView view = default;
            bool auto = controller != null && controller.TryHudLanding(out view);
            if (auto && view.Pad) return ReadPad(a, view, out d);

            Airbase.Runway.RunwayUsage use;
            if (auto) use = view.Usage;
            else
            {
                if (settings == null || !settings.IlsHud.Value || a.gearState == LandingGear.GearState.LockedRetracted || !PickRunway(a, out use)) return false;
            }
            if (use.Runway == null || use.GetStart() == null || use.GetEnd() == null) return false;

            // The runway frame: origin at the threshold, +along down the landing direction, +cross to its right.
            Vector3 raw = use.GetDirection(), dir = new Vector3(raw.x, 0f, raw.z);
            if (dir.sqrMagnitude < 0.01f) return false;
            dir.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            GlobalPosition me = a.GlobalPosition(), start = use.GetStart().GlobalPosition(), touch = use.GetTouchdownPoint();
            Vector3 fromStart = me - start, fromTouch = me - touch, startToTouch = touch - start;
            float aim = Vector3.Dot(startToTouch, dir), along = Vector3.Dot(fromStart, dir), cross = Vector3.Dot(fromStart, right);
            float flat = Mathf.Sqrt(fromStart.x * fromStart.x + fromStart.z * fromStart.z);
            float height = Mathf.Max(0f, fromTouch.y - a.definition.spawnOffset.y);
            Airbase.Runway rw = use.Runway;

            Vector3 vel = a.rb != null ? a.rb.velocity : Vector3.zero;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            Vector3 wind = level != null ? level.GetWind(me) : Vector3.zero;
            float ground = Mathf.Sqrt(vel.x * vel.x + vel.z * vel.z);
            float closing = Vector3.Dot(vel - rw.GetVelocity(), (-fromTouch).normalized);
            float touchdownS = closing <= 0.01f ? 30f : Mathf.Min(fromTouch.magnitude / closing, 30f);
            bool landed = a.IsLanded();

            if (!auto)
            {
                if (landed)
                {
                    bool onRunway = along > -50f && along < rw.Length + 50f && Mathf.Abs(cross) < rw.GetWidth();
                    if (!onRunway || ground < RollMinMps || Time.unscaledTime - airborneAt > RollGraceSeconds) return false;
                }
                else
                {
                    airborneAt = Time.unscaledTime;
                    if (flat > NearM || vel.y > ClimbMps) return false;
                    // A helicopter hovering about a field is not on an approach: a vertical lander only gets the runway funnel in forward flight.
                    AircraftParameters flown = a.GetAircraftParameters();
                    if (flown != null && flown.verticalLanding && ground < ForwardFlightMps) return false;
                }
            }

            float toTouchdown = Mathf.Max(0f, aim - along);
            float slope = auto ? IlsGuidance.AutolandSlopeDegrees : settings.Slope;
            float loc = IlsGuidance.LocalizerDegrees(cross, toTouchdown);
            ApproachStage stage = auto ? view.Stage : IlsGuidance.ManualStage(landed, loc, flat, touchdownS);

            d.Autoland = auto;
            d.Stage = stage;
            d.Field = FieldName(auto ? view.Airbase : field);
            d.Runway = RunwayName(auto ? view.Airbase : field, use);
            d.RunwayLength = rw.Length;
            d.RunwayWidth = rw.GetWidth();
            d.AimM = aim;
            d.AlongM = along;
            d.CrossM = cross;
            d.HeightM = height;
            d.RadarAltM = a.radarAlt;
            d.PathDeg = ground > 5f ? Mathf.Atan2(-vel.y, ground) * Mathf.Rad2Deg : 0f;
            d.HasIls = stage <= ApproachStage.Glide;
            d.LocDeg = loc;
            d.GsDeg = IlsGuidance.GlideslopeErrorDegrees(height, toTouchdown, slope);
            d.SlopeDeg = slope;
            d.SpeedMps = Vector3.Dot(a.transform.forward, vel - wind);
            d.GroundMps = ground;
            d.SinkMps = -vel.y;
            if (auto) { d.HasTarget = view.HasTarget; d.TargetMps = view.TargetSpeed; }
            else
            {
                AircraftParameters p = a.GetAircraftParameters();
                d.HasTarget = p != null && p.approachSpeed > 0f && stage != ApproachStage.Rollout;
                d.TargetMps = d.HasTarget ? IlsGuidance.ManualTarget(p.approachSpeed, p.landingSpeed, a.radarAlt) : 0f;
            }
            d.TouchdownS = touchdownS;
            d.HasWind = level != null;
            d.CrosswindMps = Vector3.Dot(wind, right);
            Equipment(a, ref d);
            d.Occupied = rw.occupied;
            d.DistanceM = flat;
            return true;
        }

        /// <summary>A vertical lander flying to (or over) a pad: the pad's place in the aircraft's heading frame; no runway, no ILS.</summary>
        private bool ReadPad(Aircraft a, in AutolandView view, out ApproachScope d)
        {
            d = default;
            if (view.PadPoint == null) return false;
            GlobalPosition me = a.GlobalPosition(), pad = view.PadPoint.GlobalPosition();
            Vector3 rel = pad - me, fwd = new Vector3(a.transform.forward.x, 0f, a.transform.forward.z);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 vel = a.rb != null ? a.rb.velocity : Vector3.zero;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            Vector3 wind = level != null ? level.GetWind(me) : Vector3.zero;
            d.Autoland = true;
            d.Pad = true;
            d.Stage = view.Stage;
            d.Field = FieldName(view.Airbase);
            d.RadarAltM = a.radarAlt;
            d.HeightM = Mathf.Max(0f, a.radarAlt);
            d.GroundMps = Mathf.Sqrt(vel.x * vel.x + vel.z * vel.z);
            d.SpeedMps = d.GroundMps;
            d.SinkMps = -vel.y;
            d.HasWind = level != null;
            d.CrosswindMps = Vector3.Dot(wind, right);
            Equipment(a, ref d);
            d.PadX = Vector3.Dot(rel, right);
            d.PadY = Vector3.Dot(rel, fwd);
            d.DistanceM = Mathf.Sqrt(rel.x * rel.x + rel.z * rel.z);
            return true;
        }

        private static void Equipment(Aircraft a, ref ApproachScope d)
        {
            d.GearDown = a.gearState == LandingGear.GearState.LockedExtended;
            d.GearMoving = a.gearState == LandingGear.GearState.Extending || a.gearState == LandingGear.GearState.Retracting;
            ControlInputs inputs = a.GetInputs();
            d.Airbrake = inputs != null && inputs.brake > 0.5f;
        }

        /// <summary>The nearest friendly field's landing runway with its nearer end as the threshold, re-picked once a second (the end only flips when the other is clearly nearer).</summary>
        private bool PickRunway(Aircraft a, out Airbase.Runway.RunwayUsage use)
        {
            // On the ground the pick is frozen: re-picking mid roll-out would flip the threshold end once the aircraft is past ~61 % of the runway.
            if (hasPick && runway != null && field != null && a.IsLanded())
            {
                use = new Airbase.Runway.RunwayUsage(runway, reverse);
                return true;
            }
            float now = Time.unscaledTime;
            if (now >= nextPick)
            {
                nextPick = now + PickSeconds;
                Airbase.Runway before = runway;
                hasPick = false;
                field = null;
                runway = null;
                if (a.NetworkHQ != null)
                {
                    var query = new RunwayQuery { RunwayType = RunwayQueryType.Landing, MinSize = 0f, LandingSpeed = 0f, TailHook = false };
                    Airbase f = a.NetworkHQ.GetNearestAirbase(a.transform.position, query);
                    Airbase.Runway r = f != null ? f.GetLandingRunway() : null;
                    if (r != null && r.Start != null && r.End != null)
                    {
                        Vector3 p = a.transform.position;
                        float toStart = (r.Start.position - p).sqrMagnitude, toEnd = (r.End.position - p).sqrMagnitude;
                        bool toEndNearer = toEnd < toStart;
                        bool keep = ReferenceEquals(before, r);
                        // Threshold = the nearer end: reverse means the end is the start of the landing run.
                        bool wantReverse = keep ? (reverse ? toStart >= toEnd * 0.64f : toEnd < toStart * 0.64f) : toEndNearer;
                        reverse = wantReverse;
                        field = f;
                        runway = r;
                        hasPick = true;
                    }
                }
            }
            use = hasPick && runway != null ? new Airbase.Runway.RunwayUsage(runway, reverse) : default;
            return hasPick && runway != null && field != null;
        }

        private string FieldName(Airbase f)
        {
            if (f == null) return null;
            if (!ReferenceEquals(f, namedField))
            {
                namedField = f;
                fieldName = string.IsNullOrWhiteSpace(f.name) ? null : MfdAirbaseFormatter.Format(f).ToUpperInvariant();
            }
            return fieldName;
        }

        private string RunwayName(Airbase f, in Airbase.Runway.RunwayUsage use)
        {
            if (!ReferenceEquals(use.Runway, namedRunway) || use.Reverse != namedReverse || !ReferenceEquals(f, namedRunwayField))
            {
                namedRunway = use.Runway;
                namedReverse = use.Reverse;
                namedRunwayField = f;
                string n = use.GetName();
                n = string.IsNullOrWhiteSpace(n) ? null : n.Trim().ToUpperInvariant();
                runwayName = n != null && n.StartsWith("RUNWAY ", StringComparison.Ordinal) ? "RWY " + n.Substring(7) : n;     // the game's "Runway 27" as the board's "RWY 27"
            }
            return runwayName;
        }
    }
}
