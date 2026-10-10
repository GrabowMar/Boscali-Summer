using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// CG-9 HAMILCAR on every peer (design/vanguard-remake/hamilcar.md section 4; joints: hamilcar-model-v6.md section 3):
    /// the folding outer wing panels, the propeller, the visor and the ramp follow HamilcarProfile.Pose; the Hexhound in
    /// the pod (the sibling "Payload") rolls off the nose and is hidden the moment the live unit takes over; the empty
    /// shell sinks away at the end. The Wing joint (centre section, booms, tail) is a fixed frame and is never turned.
    /// The host reads the exact phase off its flight. A client has no flight and reads the same phases off what the glider
    /// is seen doing. The launching pilot also gets the vg.hamilcar line and notices (HamilcarHud).
    /// </summary>
    internal sealed class HamilcarVisuals : MonoBehaviour
    {
        private const float ProbeSeconds = 0.2f; // 5 Hz: a client's look at the ground, and the launcher's HUD line
        private const float SeenSlack = 0.3f;    // m and m/s: a synced position a little off still reads as down and stopped
        private const float HuskSink = 1f, HuskSinkSeconds = 3f;
        // Roll-off, from hamilcar-model-v6.md: the deck the wheels stand on (top z 0.20) above the ground under the keel, then
        // metres of travel from the seat (y 0) to the ramp hinge (y 1.56) and to the foot of the 0.97 m ramp once it has
        // dropped 100 deg from upright, 10 deg below level: 1.56 + 0.97 x cos 10 deg = 2.515 (the model measures 2.514).
        private const float Deck = 0.20f + HamilcarProfile.RestHeight, RampTop = 1.56f, RampFoot = RampTop + 0.97f * 0.985f;
        private const float WheelRadius = 0.324f; // Hexhound wheel, as measured for the cradle (VanguardBuilder.hamilcar.cs)

        private static readonly string[] AxleNames = { "axle1", "axle2", "axle3" };
        private static bool warned; // one rig warning a session, not one a glider

        // One hinged part. Only localRotation is ever written, about a missile axis taken into the joint's parent frame at
        // rest: the FBX joints carry their own baked axes and a 100x scale, and neither may change.
        private struct Joint
        {
            public Transform Part;
            private Quaternion rest;
            private Vector3 axis;
            private float shown;

            public static Joint Find(Transform[] parts, string name, Vector3 worldAxis, ref string missing)
            {
                foreach (Transform part in parts)
                    if (part.name == name)
                        return new Joint { Part = part, rest = part.localRotation, axis = part.parent.InverseTransformDirection(worldAxis) };
                missing = missing == null ? name : missing + ", " + name;
                return default;
            }

            public void Turn(float degrees)
            {
                if (degrees == shown || Part == null) return;
                shown = degrees;
                Part.localRotation = Quaternion.AngleAxis(degrees, axis) * rest;
            }
        }

        private Missile missile;
        private VanguardFlight flight;
        private Transform body, visual, payload;
        private Vector3 visualRest, payloadRest;
        private Quaternion payloadRestRot;
        private Joint outerL, outerR, visor, ramp, prop;
        private float propAngle;
        private readonly Joint[] axles = new Joint[AxleNames.Length];
        // Outer axles ahead of and behind the Hexhound's origin, measured off the rig; one without axles follows the track as a point.
        private float axleFront = 0.01f, axleRear = -0.01f;
        private float rolled, rollGround;
        private HamilcarPhase phase;
        private float phaseAt, agl = 1000f, speed = HamilcarProfile.GlideSpeed, seenTime, nextProbe, nextHud;
        private GlobalPosition seenAt;
        private bool mine, flightShort;
        private string hudKey;

        /// <summary>Sim hook: the cradle Hexhound was handed over to the live unit (or never drew).</summary>
        internal bool PayloadHidden { get; private set; }

        /// <summary>Sim hook: the cradle Hexhound's renderers that draw right now (active, enabled, with a mesh).</summary>
        internal int PayloadRenderers
        {
            get
            {
                int drawn = 0;
                if (payload != null && payload.gameObject.activeInHierarchy)
                    foreach (MeshFilter mesh in payload.GetComponentsInChildren<MeshFilter>(false))
                        if (mesh.sharedMesh != null && mesh.TryGetComponent(out Renderer renderer) && renderer.enabled) drawn++;
                return drawn;
            }
        }

        internal static HamilcarVisuals Attach(Missile missile)
        {
            if (missile == null || missile.definition == null || VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Lander) return null;
            HamilcarVisuals view = missile.GetComponent<HamilcarVisuals>();
            if (view != null) return view;
            view = missile.gameObject.AddComponent<HamilcarVisuals>();
            view.Bind(missile);
            return view;
        }

        private void Bind(Missile glider)
        {
            missile = glider;
            body = glider.transform;
            string missing = null;
            visual = body.Find("VanguardVisual");
            if (visual != null) visualRest = visual.localPosition;
            Transform[] parts = visual != null ? visual.GetComponentsInChildren<Transform>(true) : new Transform[0];
            // Model +X is the glider's right, +Y its forward, +Z its up. The stow axes are kept in that Unity frame already.
            outerL = Joint.Find(parts, "WingOuterL", body.TransformDirection(HamilcarProfile.OuterAxisL), ref missing);
            outerR = Joint.Find(parts, "WingOuterR", body.TransformDirection(HamilcarProfile.OuterAxisR), ref missing);
            visor = Joint.Find(parts, "Visor", body.right, ref missing);
            ramp = Joint.Find(parts, "Ramp", body.right, ref missing);
            prop = Joint.Find(parts, "Prop", body.forward, ref missing);

            payload = body.Find("Payload");
            parts = payload != null ? payload.GetComponentsInChildren<Transform>(true) : new Transform[0];
            if (payload != null)
            {
                payloadRest = payload.localPosition;
                payloadRestRot = payload.localRotation;
            }
            else missing = missing == null ? "Payload" : missing + ", Payload";
            for (int i = 0; i < axles.Length; i++)
            {
                axles[i] = Joint.Find(parts, AxleNames[i], body.right, ref missing);
                if (axles[i].Part == null) continue;
                float along = payload.InverseTransformPoint(axles[i].Part.position).z;
                axleFront = Mathf.Max(axleFront, along);
                axleRear = Mathf.Min(axleRear, along);
            }
            if (missing != null && !warned)
            {
                warned = true;
                Plugin.Logger?.LogWarning("[Vanguard] HAMILCAR rig has no " + missing + ": those parts stay still");
            }
            // Stowed from the very first frame: it leaves the hold with the outer panels folded back beside the booms.
            Apply(HamilcarProfile.Pose(HamilcarPhase.Release, glider.timeSinceSpawn, 0f));
        }

        private void LateUpdate()
        {
            if (missile == null || missile.disabled)
            {
                Gone();
                return;
            }
            float age = missile.timeSinceSpawn, phaseAge;
            if (flight == null) VanguardRegistry.TryGet(missile, out flight); // made after Attach, and only where the glider is simulated
            if (flight != null) flight.HamilcarState(out phase, out phaseAge, out _, out flightShort, out _);
            else phaseAge = Observe(age);
            Apply(HamilcarProfile.Pose(phase, age, phaseAge));
            if (phase == HamilcarPhase.Husk) Husk(phaseAge);
            if (Time.unscaledTime >= nextHud) Hud();
        }

        // A client's estimate: the same phase machine, fed by what the glider is seen doing. Height stands in for the
        // distance to touchdown (the approach is a fixed line down from the gate, and the two thresholds sit just under the
        // floors the flight holds in Glide and Final). Speed comes from GlobalPosition, so an origin shift is not a jump.
        private float Observe(float age)
        {
            float now = Time.time;
            if (phase < HamilcarPhase.Unload && now >= nextProbe)
            {
                nextProbe = now + ProbeSeconds;
                GlobalPosition here = missile.GlobalPosition();
                if (seenTime > 0f) speed = (here - seenAt).magnitude / (now - seenTime);
                seenAt = here;
                seenTime = now;
                Vector3 at = body.position;
                bool ground = Physics.Raycast(at + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 3000f, (int)PhysicsLayers.StaticsMask) &&
                    hit.point.y > Datum.LocalSeaY;
                agl = at.y - (ground ? hit.point.y : Datum.LocalSeaY);
            }
            // Standing still under the flare height is down, whatever the synced height says.
            float height = speed <= SeenSlack && agl < HamilcarProfile.FlareHeight ? 0f : agl - SeenSlack;
            float distance = height > HamilcarProfile.GateHeight - 2f ? HamilcarProfile.GateRange + 1f
                : height > HamilcarProfile.FlareHeight - 0.5f ? HamilcarProfile.FlareRange + 1f : 0f;
            HamilcarPhase next = HamilcarProfile.Next(phase, age, distance, height, speed - SeenSlack, age - phaseAt, true);
            if (next != phase)
            {
                phase = next;
                phaseAt = age;
            }
            return age - phaseAt;
        }

        private void Apply(HamilcarPose pose)
        {
            // Unity turns the other way round from the model's right-handed angles (the rule is written out at
            // HamilcarProfile.OuterStow). Each outer panel swings aft onto its edge beside its boom; the Wing joint stays put.
            outerL.Turn(HamilcarProfile.OuterStow * pose.Fold);
            outerR.Turn(HamilcarProfile.OuterStow * pose.Fold);
            visor.Turn(-85f * pose.Visor); // nose up
            ramp.Turn(100f * pose.Ramp);   // from upright down onto the ground
            // Turn takes an absolute angle, so the propeller keeps a running one (rpm x 6 = deg/s); a still propeller writes nothing.
            if (pose.Spin > 0f) prop.Turn(propAngle = Mathf.Repeat(propAngle + HamilcarProfile.PropRpm * 6f * pose.Spin * Time.deltaTime, 360f));
            Roll(pose.Roll * HamilcarProfile.RollOff);
        }

        // Height of the wheel track over the ground under the keel, t metres ahead of the seat: deck, ramp, then the ground found ahead.
        private float Track(float t) =>
            Deck * (1f - Mathf.InverseLerp(RampTop, RampFoot, t)) + rollGround * Mathf.InverseLerp(RampFoot, HamilcarProfile.RollOff, t);

        // The Hexhound drives off the nose along the glider's forward. Its outer axles ride the track, so it noses down the
        // ramp and levels out on the ground; the wheels turn with the distance.
        private void Roll(float metres)
        {
            if (metres == rolled || payload == null) return;
            if (rolled == 0f)
            {
                // One look at the ground it rolls onto: the glider is pinned by now, so the answer holds for the whole roll.
                Vector3 end = body.TransformPoint(payloadRest + Vector3.forward * HamilcarProfile.RollOff);
                if (Physics.Raycast(end + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 12f, (int)PhysicsLayers.StaticsMask))
                    rollGround = Mathf.Clamp(body.InverseTransformPoint(hit.point).y + HamilcarProfile.RestHeight, -1f, 1f);
            }
            rolled = metres;
            float front = Track(metres + axleFront), rear = Track(metres + axleRear), span = axleFront - axleRear;
            payload.localPosition = payloadRest + new Vector3(0f, Mathf.LerpUnclamped(rear, front, -axleRear / span) - Deck, metres);
            payload.localRotation = Quaternion.LookRotation(new Vector3(0f, front - rear, span)) * payloadRestRot;
            float spin = metres / WheelRadius * Mathf.Rad2Deg;
            for (int i = 0; i < axles.Length; i++) axles[i].Turn(spin);
        }

        private void Husk(float husked)
        {
            if (!PayloadHidden)
            {
                PayloadHidden = true; // the live Hexhound takes over here
                if (payload != null) payload.gameObject.SetActive(false);
                if (mine) HamilcarHud.Notice(HudTone.Info, HamilcarProfile.DownText, HamilcarProfile.DownDetail);
            }
            // The empty shell sinks away over its last seconds. Nothing is put back: the unit is removed.
            float sink = Mathf.InverseLerp(VanguardFlight.HamilcarHusk - HuskSinkSeconds, VanguardFlight.HamilcarHusk, husked);
            if (sink > 0f && visual != null) visual.localPosition = visualRest + body.InverseTransformVector(Vector3.down * (HuskSink * sink));
        }

        // The launching pilot's line at 5 Hz (the board keeps a line 1.5 s after its last Set). An AI launcher has no HUD.
        private void Hud()
        {
            nextHud = Time.unscaledTime + ProbeSeconds;
            mine = GameManager.GetLocalAircraft(out Aircraft me) && me != null && me.Player != null && missile.owner == me && missile.persistentID.Id != 0;
            if (!mine) return;
            GlobalPosition here = missile.GlobalPosition();
            if (hudKey == null)
            {
                hudKey = missile.persistentID.Id.ToString();
                ShortNotice(here);
            }
            if (phase < HamilcarPhase.Husk) HamilcarHud.Held(hudKey, FastMath.Distance(here, me.GlobalPosition()), phase);
        }

        // SHORT, once at release: the flight's own verdict on the host, the same sum from the synced target on a client.
        private void ShortNotice(GlobalPosition here)
        {
            if (!UnitRegistry.TryGetUnit(missile.targetID, out Unit target) || target == null) return;
            Vector3 from = here.AsVector3(), aim = target.GlobalPosition().AsVector3();
            Vector3 centre = HamilcarProfile.Centre(from, aim, HamilcarProfile.Reach(from.y - aim.y), out bool isShort);
            if (flight != null ? flightShort : isShort)
                HamilcarHud.Notice(HudTone.Caution, HamilcarProfile.ShortText,
                    HamilcarProfile.ShortDetail(new Vector2(aim.x - centre.x, aim.z - centre.z).magnitude));
        }

        // Gone before the Hexhound was out: lost. The host's flight outlives its registry entry and says whether it gave the
        // glider up itself (no landing zone, water under the belly) or something killed it.
        private void Gone()
        {
            enabled = false;
            if (!mine) return;
            if (flight != null) flight.HamilcarState(out phase, out _, out _, out _, out _);
            if (phase == HamilcarPhase.Husk) return;
            // ponytail: a client only sees it vanish. A glider given up for want of a landing zone goes at LostHeight, so that
            // height is the tell; a phase byte from the host would settle it if the guess ever misleads.
            bool gaveUp = flight != null ? phase == HamilcarPhase.Lost
                : phase < HamilcarPhase.Slide && Mathf.Abs(agl - HamilcarProfile.LostHeight) < 6f;
            HamilcarHud.Notice(HudTone.Warning, HamilcarProfile.LostText, HamilcarProfile.LostDetail(!gaveUp));
        }
    }
}
