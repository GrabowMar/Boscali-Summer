using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

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

        private Missile missile;
        private VanguardFlight flight;
        private Transform body, visual, payload;
        private Vector3 visualRest, payloadRest;
        private Quaternion payloadRestRot;
        private RigJoint outerL, outerR, visor, ramp, prop;
        private float propAngle;
        private readonly RigJoint[] axles = new RigJoint[AxleNames.Length];
        // Outer axles ahead of and behind the Hexhound's origin, measured off the rig; one without axles follows the track as a point.
        private float axleFront = 0.01f, axleRear = -0.01f;
        private float rolled, rollGround;
        private HamilcarPhase phase;
        private HamilcarPhase lastPhase = HamilcarPhase.Release;
        private float phaseAt, agl = 1000f, speed = HamilcarProfile.GlideSpeed, seenTime, nextProbe, nextHud;
        private float startDist = -1f, lastRange, lastPhaseAge, slideEmitAccum, nextStrobeAt;
        private GlobalPosition seenAt;
        private bool mine, flightShort;
        private string hudKey;
        private TrailRenderer trailL, trailR;
        private ParticleSystem slideDust, slideSparks, strobe;
        private Light bayLight;
        private AudioSource sfx;

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
            outerL = RigJoint.Find(parts, "WingOuterL", body.TransformDirection(HamilcarProfile.OuterAxisL), ref missing);
            outerR = RigJoint.Find(parts, "WingOuterR", body.TransformDirection(HamilcarProfile.OuterAxisR), ref missing);
            visor = RigJoint.Find(parts, "Visor", body.right, ref missing);
            ramp = RigJoint.Find(parts, "Ramp", body.right, ref missing);
            prop = RigJoint.Find(parts, "Prop", body.forward, ref missing);

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
                axles[i] = RigJoint.Find(parts, AxleNames[i], body.right, ref missing);
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
            if (!Application.isBatchMode) SetupFx();
            // Stowed from the very first frame: it leaves the hold with the outer panels folded back beside the booms.
            Apply(HamilcarProfile.Pose(HamilcarPhase.Release, glider.timeSinceSpawn, 0f));
        }

        private void SetupFx()
        {
            trailL = CreateWingtipTrail(outerL.Part, "WingtipTrailL", -1f);
            trailR = CreateWingtipTrail(outerR.Part, "WingtipTrailR", 1f);

            var lightGo = new GameObject("CargoBayFloodlight");
            lightGo.transform.SetParent(body, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.3f, 1.2f);
            bayLight = lightGo.AddComponent<Light>();
            bayLight.type = LightType.Point;
            bayLight.color = new Color(1f, 0.88f, 0.65f);
            bayLight.range = 14f;
            bayLight.intensity = 0f;
            bayLight.shadows = LightShadows.None;
            bayLight.enabled = false;

            sfx = body.gameObject.AddComponent<AudioSource>();
            sfx.spatialBlend = 1f;
            sfx.minDistance = 20f;
            sfx.maxDistance = 450f;
            sfx.rolloffMode = AudioRolloffMode.Logarithmic;
            sfx.dopplerLevel = 0f;
            sfx.playOnAwake = false;

            // shortcut: world-space slide dust/sparks live 1.4 s on the glider transform; parent under Datum.origin if long-lived ground FX are added.
            if (SmokeFx.Puff != null)
                slideDust = CreateParticleLayer("SlideDust", SmokeFx.Puff, 128, ParticleSystemSimulationSpace.World, ParticleSystemRenderMode.Billboard, -0.08f);
            if (GlowFx.Glow != null)
            {
                slideSparks = CreateParticleLayer("SlideSparks", GlowFx.Glow, 128, ParticleSystemSimulationSpace.World, ParticleSystemRenderMode.Stretch, 0.5f);
                var sr = slideSparks.GetComponent<ParticleSystemRenderer>();
                sr.lengthScale = 2.2f;
                sr.velocityScale = 0.04f;
                strobe = CreateParticleLayer("NavStrobe", GlowFx.Glow, 16, ParticleSystemSimulationSpace.Local, ParticleSystemRenderMode.Billboard, 0f);
            }
        }

        private TrailRenderer CreateWingtipTrail(Transform wingPart, string trailName, float side)
        {
            if (wingPart == null || SmokeFx.Puff == null) return null;
            Renderer[] renderers = wingPart.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            float bestDot = float.MinValue;
            Vector3 bestWorld = Vector3.zero;
            Vector3 axis = body.right * side;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                Bounds b = renderers[i].bounds;
                Vector3 c = b.center, e = b.extents;
                for (int mask = 0; mask < 8; mask++)
                {
                    Vector3 corner = new Vector3(
                        c.x + ((mask & 1) != 0 ? e.x : -e.x),
                        c.y + ((mask & 2) != 0 ? e.y : -e.y),
                        c.z + ((mask & 4) != 0 ? e.z : -e.z));
                    float dot = Vector3.Dot(corner - body.position, axis);
                    if (dot > bestDot) { bestDot = dot; bestWorld = corner; found = true; }
                }
            }
            if (!found) return null;
            var go = new GameObject(trailName);
            go.transform.SetParent(wingPart, false);
            go.transform.localPosition = wingPart.InverseTransformPoint(bestWorld - body.forward * 0.2f);
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.55f;
            tr.startWidth = 0.14f;
            tr.endWidth = 0.01f;
            tr.minVertexDistance = 0.8f;
            tr.sharedMaterial = SmokeFx.Puff;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.88f, 0.94f, 1f), 0f), new GradientColorKey(new Color(0.88f, 0.94f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = grad;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.emitting = false;
            return tr;
        }

        private ParticleSystem CreateParticleLayer(string layerName, Material mat, int budget,
            ParticleSystemSimulationSpace space, ParticleSystemRenderMode renderMode, float gravity)
        {
            var go = new GameObject(layerName);
            go.SetActive(false);
            go.transform.SetParent(body, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startSpeed = 0f;
            main.gravityModifier = gravity;
            main.maxParticles = budget;
            main.simulationSpace = space;
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var pr = ps.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = mat;
            pr.renderMode = renderMode;
            pr.maxParticleSize = 0.6f;
            pr.minParticleSize = 0f;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            go.SetActive(true);
            ps.Play(false);
            return ps;
        }

        private void PlaySfx(string clipName, float volume)
        {
            if (sfx == null || Application.isBatchMode) return;
            AudioClip clip = SfxClips.Get(clipName);
            if (clip != null) sfx.PlayOneShot(clip, volume);
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
            if (flight != null)
            {
                flight.HamilcarState(out phase, out phaseAge, out float rangeToSpot, out flightShort, out _);
                if (startDist < 0f && rangeToSpot > HamilcarProfile.GateRange) startDist = rangeToSpot;
                lastRange = rangeToSpot;
            }
            else phaseAge = Observe(age);
            lastPhaseAge = phaseAge;

            if (phase != lastPhase)
            {
                if (lastPhase == HamilcarPhase.Release && phase == HamilcarPhase.Glide) PlaySfx("gear-lever", 0.6f);
                else if (phase == HamilcarPhase.Slide)
                {
                    if (!Application.isBatchMode && GameAssets.i != null && GameAssets.i.contactDust != null)
                    {
                        GameObject dust = Instantiate(GameAssets.i.contactDust, body.position, Quaternion.identity);
                        dust.SetActive(true);
                        Destroy(dust, 3.5f);
                    }
                    PlaySfx("hull-thud", 0.85f);
                }
                else if (phase == HamilcarPhase.Unload) PlaySfx("canopy-close", 0.75f);
                lastPhase = phase;
            }

            HamilcarPose pose = HamilcarProfile.Pose(phase, age, phaseAge);
            Apply(pose);
            UpdateFx(pose, phaseAge);
            if (phase == HamilcarPhase.Husk) Husk(phaseAge);
            if (Time.unscaledTime >= nextHud) Hud();
        }

        private void UpdateFx(HamilcarPose pose, float phaseAge)
        {
            float curSpeed = flight != null && missile.rb != null ? missile.rb.velocity.magnitude : speed;
            bool emitTrails = pose.Fold < 0.05f && phase <= HamilcarPhase.Flare && curSpeed > 45f;
            if (trailL != null && trailL.emitting != emitTrails) trailL.emitting = emitTrails;
            if (trailR != null && trailR.emitting != emitTrails) trailR.emitting = emitTrails;

            if (bayLight != null)
            {
                float sink = phase == HamilcarPhase.Husk ? Mathf.InverseLerp(VanguardFlight.HamilcarHusk - HuskSinkSeconds, VanguardFlight.HamilcarHusk, phaseAge) : 0f;
                float intensity = 2.6f * pose.Visor * (1f - sink);
                bayLight.enabled = intensity > 0.02f;
                if (bayLight.enabled) bayLight.intensity = intensity;
            }

            if (strobe != null && phase <= HamilcarPhase.Flare && Time.time >= nextStrobeAt)
            {
                nextStrobeAt = Time.time + 1.6f;
                var localTail = new Vector3(0f, 0.9f, -1.8f);
                strobe.Emit(new ParticleSystem.EmitParams
                {
                    position = localTail,
                    startSize = 1.6f,
                    startLifetime = 0.12f,
                    startColor = new Color(1f, 0.85f, 0.55f, 0.35f),
                }, 1);
                strobe.Emit(new ParticleSystem.EmitParams
                {
                    position = localTail,
                    startSize = 0.45f,
                    startLifetime = 0.12f,
                    startColor = new Color(1f, 0.98f, 0.90f, 0.95f),
                }, 1);
            }

            if (phase == HamilcarPhase.Slide && !Application.isBatchMode)
            {
                slideEmitAccum += Time.deltaTime * curSpeed;
                int steps = Mathf.Min(6, (int)(slideEmitAccum / 1.2f));
                if (steps > 0)
                {
                    slideEmitAccum -= steps * 1.2f;
                    Vector3 keel = body.position - body.forward * 1.2f + Vector3.up * 0.2f;
                    for (int s = 0; s < steps; s++)
                    {
                        if (slideDust != null && curSpeed > 1.5f)
                            slideDust.Emit(new ParticleSystem.EmitParams
                            {
                                position = keel + body.right * Random.Range(-0.8f, 0.8f),
                                velocity = -body.forward * (curSpeed * 0.15f) + Vector3.up * 1.4f + body.right * Random.Range(-1.2f, 1.2f),
                                startSize = Random.Range(1.6f, 2.8f),
                                startLifetime = Random.Range(1.0f, 1.6f),
                                startColor = new Color(0.58f, 0.52f, 0.43f, 0.55f),
                            }, 1);
                        if (slideSparks != null && curSpeed > 6f)
                            for (int k = 0; k < 2; k++)
                                slideSparks.Emit(new ParticleSystem.EmitParams
                                {
                                    position = keel + body.right * Random.Range(-0.4f, 0.4f),
                                    velocity = -body.forward * (curSpeed * 0.45f) + Vector3.up * Random.Range(1.5f, 4.5f) + body.right * Random.Range(-3f, 3f),
                                    startSize = Random.Range(0.08f, 0.15f),
                                    startLifetime = Random.Range(0.28f, 0.55f),
                                    startColor = new Color(1f, 0.72f, 0.28f, 1f),
                                }, 1);
                    }
                }
            }
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
            if (phase < HamilcarPhase.Husk)
            {
                float progress = HamilcarProfile.HudProgress(phase, flight != null ? lastRange : 0f, flight != null ? startDist : 0f, lastPhaseAge);
                HamilcarHud.Held(hudKey, FastMath.Distance(here, me.GlobalPosition()), phase, progress);
            }
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
