using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Collects actual static-surface contact before a disabled aircraft is removed.</summary>
    internal sealed class MegaplaneCrash : MonoBehaviour
    {
        // Wire order. Each entry names the single native art mesh that establishes ownership.
        internal static readonly string[] GroupNames = {
            "Fuselage_Center","Fuselage_Forward","Fuselage_Cockpit","Fuselage_Aft","Fuselage_TailCone","Fuselage_Hump",
            "Wing_Root_L","Wing_Inner_L","Wing_Mid_L","Wing_Outer_L","Wing_Tip_L",
            "Wing_Root_R","Wing_Inner_R","Wing_Mid_R","Wing_Outer_R","Wing_Tip_R",
            "Boom_L","Boom_R","Fin_L","Fin_R","Tailplane",
            "Pod_L1","Pod_L2","Pod_L3","Pod_L4","Pod_R1","Pod_R2","Pod_R3","Pod_R4",
            "NoseAssist_L","NoseAssist_R","Flap_Inner_L","Flap_Inner_R","Flap_Outer_L","Flap_Outer_R",
            "Aileron_L","Aileron_R","Rudder_L","Rudder_R","Strongback_L","Strongback_R","HullPlate_L","HullPlate_R",
            "ReactorCore","CoolantPack_L","CoolantPack_R"
        };

        private Aircraft aircraft;
        private Vector3 previousOrigin, incoming;
        private Quaternion previousRotation;
        private bool hasPrevious;
        private float remoteSampleTime;
        private bool checkedField;
        private bool concealed;
        private float removeAfter;
        private bool releaseSeat, followedFromSeat, alreadyEvacuated;
        internal bool Impacted { get; private set; }
        private AeroPart[] owners;
        // Sample the exterior of the actual sections, including widely separated wing and engine extremities.
        private static readonly (int group, Vector3 point)[] Keel = {
            (0,new Vector3(0,-11.8f,0)), (1,new Vector3(0,-11.8f,37)), (2,new Vector3(0,-5,108)),
            (3,new Vector3(0,-10,-35)), (4,new Vector3(0,6,-88)),
            (10,new Vector3(-167,6,-51)), (15,new Vector3(167,6,-51)),
            (16,new Vector3(-30,10,-114)), (17,new Vector3(30,10,-114)),
            (21,new Vector3(-30,-13.2f,20)), (25,new Vector3(30,-13.2f,20)),
            (22,new Vector3(-53,-12.2f,13)), (26,new Vector3(53,-12.2f,13)),
            (23,new Vector3(-111,-5.8f,-15)), (27,new Vector3(111,-5.8f,-15)),
            (24,new Vector3(-134,-5.1f,-27)), (28,new Vector3(134,-5.1f,-27))
        };

        internal static AeroPart[] GroupOwners(GameObject airframe)
        {
            var owners = new AeroPart[GroupNames.Length];
            foreach (MeshFilter filter in airframe.GetComponentsInChildren<MeshFilter>(true))
            {
                int index = Array.IndexOf(GroupNames, filter.name);
                if (index >= 0) owners[index] = filter.GetComponentInParent<AeroPart>(true);
            }
            return owners;
        }

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            owners = GroupOwners(gameObject);
        }

        internal static ulong Mask(Aircraft aircraft)
        {
            MegaplaneCrash crash = aircraft.GetComponent<MegaplaneCrash>();
            ulong mask = 0;
            for (int i = 0; i < crash.owners.Length; i++)
            {
                AeroPart p = crash.owners[i];
                if (p != null && !p.IsDetached() && p.transform.IsChildOf(aircraft.transform)) mask |= 1UL << i;
            }
            return mask;
        }

        private bool Eligible => aircraft.IsServer && !Impacted && aircraft.rb != null && Datum.origin != null &&
            aircraft.unitState != Unit.UnitState.Returned && aircraft.NetId != 0 && aircraft.persistentID.IsValid;

        private void FixedUpdate()
        {
            if (releaseSeat) FinishSeat();
            if (!checkedField && !aircraft.IsServer && aircraft.NetId != 0 && aircraft.persistentID.IsValid)
            {
                checkedField = true;
                if (MegaplaneWreck.HasField(aircraft)) Conceal();
            }
            if (!Eligible) return;
            Vector3 origin = aircraft.rb.position - Datum.originPosition;
            Quaternion rotation = aircraft.rb.rotation;
            Vector3 step = origin - previousOrigin;
            bool continuous = hasPrevious && step.sqrMagnitude < 6400f;
            // Remote owner rigidbodies can be kinematic on the server. Measure network motion
            // in global coordinates so a floating-origin shift cannot look like an impact.
            Vector3 reported = aircraft.rb.velocity;
            remoteSampleTime += Time.fixedDeltaTime;
            if (aircraft.LocalSim || reported.sqrMagnitude > 1f) incoming = reported;
            else if (continuous && step.sqrMagnitude > .0001f)
                incoming = Vector3.ClampMagnitude(step / Mathf.Max(remoteSampleTime,Time.fixedDeltaTime), 350f);
            if (!continuous || step.sqrMagnitude > .0001f) remoteSampleTime = 0f;
            if (continuous && (origin.y < 235f || aircraft.radarAlt < 230f))
            {
                float first = float.PositiveInfinity;
                Vector3 contact = default, normal = default;
                bool water = false;
                void Consider(float time, Vector3 at, Vector3 surfaceNormal, bool isWater)
                {
                    if (time > first || time == first && isWater || !Destructive(surfaceNormal,incoming)) return;
                    first = time;
                    contact = at;
                    normal = surfaceNormal;
                    water = isWater;
                }
                foreach (var keel in Keel)
                {
                    AeroPart owner = owners[keel.group];
                    if (owner == null || owner.IsDetached()) continue;
                    Vector3 a = previousOrigin + previousRotation * keel.point;
                    Vector3 b = origin + rotation * keel.point;
                    float length = (b-a).magnitude;
                    if (length > .05f && Physics.Linecast(a+Datum.originPosition,b+Datum.originPosition,
                        out RaycastHit hit,PhysicsLayers.StaticsMask,QueryTriggerInteraction.Ignore))
                        Consider(hit.distance/length,hit.point,hit.normal,IsWater(hit.collider));
                    if (a.y > 0f && b.y <= 0f)
                    {
                        float t = a.y / (a.y - b.y);
                        Vector3 sea = Vector3.Lerp(a,b,t) + Datum.originPosition;
                        // A fast coast crossing may pass both rock and sea level in one tick.
                        // Even a keel point already inside the bank must not produce a splash.
                        float rise = Mathf.Max(200f,a.y + 2f);
                        if (Physics.Raycast(sea+Vector3.up*rise,Vector3.down,out RaycastHit bank,rise+.01f,
                            PhysicsLayers.StaticsMask,QueryTriggerInteraction.Ignore) && !IsWater(bank.collider))
                        {
                            float height = bank.point.y-Datum.originPosition.y;
                            Consider(Mathf.Clamp01((a.y-height)/(a.y-b.y)),bank.point,bank.normal,false);
                        }
                        else Consider(t,sea,Vector3.up,true);
                    }
                }
                if (!float.IsInfinity(first)) Record(contact,normal,incoming,water);
            }
            previousOrigin = origin;
            previousRotation = rotation;
            hasPrevious = true;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!Eligible || collision.contactCount == 0 || collision.collider == null) return;
            Collider other = collision.collider;
            bool water = IsWater(other);
            if (!water && other.gameObject.layer != PhysicsLayers.Statics && !(other is TerrainCollider)) return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint hit = collision.GetContact(i);
                Vector3 velocity = incoming;
                if (velocity.sqrMagnitude < 1f) velocity = aircraft.rb.velocity;
                if (!Destructive(hit.normal,velocity)) continue;
                Record(hit.point,hit.normal,velocity,water);
                break;
            }
        }

        internal static bool IsWater(Collider other) => other.gameObject.layer == PhysicsLayers.Water ||
            (GameAssets.i.WaterMaterial != null && other.sharedMaterial == GameAssets.i.WaterMaterial);

        private bool Destructive(Vector3 normal, Vector3 velocity)
        {
            float closing = -Vector3.Dot(velocity, normal);
            bool scraping = (!aircraft.NetworkgearDeployed || Vector3.Dot(transform.up, normal) < .84f) &&
                closing >= 7f && velocity.sqrMagnitude > 48f * 48f;
            return normal.sqrMagnitude >= .9f && (closing >= 14f || scraping ||
                aircraft.disabled && (closing >= 3f || velocity.sqrMagnitude >= 24f * 24f));
        }

        private void Record(Vector3 contact, Vector3 normal, Vector3 velocity, bool water)
        {
            MegaplaneGear gear = GetComponent<MegaplaneGear>();
            var report = new MegaplaneCrashRecord {
                Spawn = aircraft.NetId, Unit = aircraft.persistentID.Id, Origin = aircraft.rb.position - Datum.originPosition,
                Contact = contact - Datum.originPosition, Velocity = velocity, Normal = normal,
                Rotation = aircraft.rb.rotation, Surviving = Mask(aircraft), Water = water, Age = 0f,
                GearLeg = (byte)Mathf.RoundToInt(255f*gear.Leg), GearDoor = (byte)Mathf.RoundToInt(255f*gear.Door)
            };
            for (int i = 0; i < owners.Length; i++)
            {
                ulong flag = 1UL << i;
                if ((report.Surviving & flag) == 0) continue;
                int stage = MegaplaneSkinDamage.Stage(owners[i].hitPoints);
                if (stage >= 1) report.DamagedSkin |= flag;
                if (stage >= 2) report.HeavilyDamagedSkin |= flag;
            }
            Impacted = true;
            // A dying native pilot can disable the unit immediately; let the reliable damage,
            // pilot spawn and camera handoff reach observers before the underlying identity despawns.
            removeAfter = Time.time + (aircraft.HasEjected() ? 12f : 3f);
            try
            {
                ApplyCrewImpact(Mathf.Max(0f,-Vector3.Dot(normal,velocity)));
                MegaplaneNet.Crash(report);
            }
            finally
            {
                Conceal();
                if (!aircraft.disabled) aircraft.DisableUnit();
                // A bail-out already in progress owns its seats. Each intact surviving seat
                // otherwise becomes the native rescue unit with its original player and faction.
                if (!aircraft.HasEjected())
                    for (int i=0; i<aircraft.pilots.Length; i++)
                        if (OnBoard(aircraft.pilots[i])) aircraft.SpawnEjectingPilot(i);
            }
        }

        private bool OnBoard(Pilot p) => p != null && !p.dead && !p.ejected && p.transform.IsChildOf(transform) &&
            p.GetUnitPart() != null && !p.GetUnitPart().IsDetached();

        private void ApplyCrewImpact(float closing)
        {
            // The normal loss of velocity loads the seat. Allow 40 ms for deformation of the
            // hull, and use the game's own 20 g threshold and injury curve for the resulting pulse.
            float g = closing/(9.81f*.04f);
            if (g <= 20f) return;
            float damage = .007f*(g*g-400f);
            bool localDamage = aircraft.IsClient && Mirage.RemoteCalls.ClientRpcSender.IsLocalPlayerObserver(aircraft,false);
            foreach (Pilot p in aircraft.pilots)
            {
                if (!OnBoard(p)) continue;
                p.TakeDamage(0f,0f,1f,0f,damage,PersistentID.None);
                // Mirage runs the native damage RPC locally only for observing host players.
                if (!localDamage) p.ApplyDamage(0f,0f,0f,damage);
            }
        }

        private void FinishSeat()
        {
            if (!GameManager.IsLocalAircraft(aircraft)) { releaseSeat = false; return; }
            Pilot pilot = aircraft.pilots[0];
            var player = aircraft.Player;
            PilotDismounted survivor = player.PilotDismounted;
            bool escaped = survivor != null && survivor.NetworkparentUnit == aircraft.persistentID;
            if (!escaped && !pilot.dead && !alreadyEvacuated) return;
            releaseSeat = false;
            var camera = SceneSingleton<CameraStateManager>.i;
            if (escaped && followedFromSeat && camera != null && camera.followingUnit == null)
                camera.SetFollowingUnit(survivor);
            // Native pilot death already resolves a no-respawn mission. A survivor evacuating
            // a wreck takes the same mission exit as the native airborne jettison sequence.
            if (escaped && !alreadyEvacuated && !MissionHelper.CanRespawn && GameManager.gameResolution == GameResolution.Ongoing)
                GameManager.FinishGame(GameResolution.Defeat);
            player.RemoveAircraft(aircraft);
            if (SceneSingleton<CombatHUD>.i != null) SceneSingleton<CombatHUD>.i.RemoveAircraft();
            if (!alreadyEvacuated) player.ShowMap(5f);
        }

        internal void Conceal()
        {
            if (concealed) return;
            concealed = true;
            Impacted = true;
            removeAfter = Mathf.Max(removeAfter,Time.time+3f);
            var camera = SceneSingleton<CameraStateManager>.i;
            followedFromSeat = camera != null && (camera.followingUnit == aircraft || camera.transform.IsChildOf(transform));
            releaseSeat = GameManager.IsLocalAircraft(aircraft) && aircraft.pilots.Length > 0 && aircraft.pilots[0] != null;
            alreadyEvacuated = releaseSeat && aircraft.pilots[0].ejected;
            // Cockpit and orbit both parent the native camera/pivot to the airframe. Free
            // them before this traversal or destroying the aircraft can destroy the camera.
            if (followedFromSeat) camera.SetFollowingUnit(null);
            // Stock Aircraft.OnDestroy also destroys previously detached, independent rigidbodies.
            // Give those original fragments a short final flight; never recreate them in this wreck.
            foreach (AeroPart owner in owners)
                if (owner != null && owner.IsDetached() && !owner.transform.IsChildOf(transform) &&
                    owner.gameObject.activeInHierarchy) { removeAfter = Mathf.Max(removeAfter,Time.time+12f); break; }
            // Leave the aircraft identity available for native kill reporting/despawn. The
            // only visible copy of a surviving section is now in the detached wreck field.
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = true;
            foreach (Light light in GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
            aircraft.GetComponent<MegaplaneFlight>().enabled = false;
            aircraft.GetComponent<MegaplaneDamage>().enabled = false;
            aircraft.GetComponent<MegaplaneFx>().enabled = false;
            aircraft.GetComponent<MegaplaneLights>().enabled = false;
            if (TryGetComponent(out MegaplaneDefence defence)) defence.enabled = false;
            if (TryGetComponent(out MegaplaneRailgun railgun)) railgun.enabled = false;
            foreach (WeaponManager weapons in GetComponentsInChildren<WeaponManager>(true)) weapons.enabled = false;
            foreach (Turret turret in GetComponentsInChildren<Turret>(true)) turret.enabled = false;
            if (aircraft.rb != null) aircraft.rb.isKinematic = true;
        }

        internal async UniTask RemoveAfterImpact(float vanillaDelay)
        {
            // A carrier disabled at altitude can take several minutes to reach terrain.
            // A mission return, editor removal or explicit despawn keeps its normal path.
            var token = destroyCancellationToken;
            for (float age = 0f; age < 600f && !token.IsCancellationRequested; age += 1f)
            {
                if (Impacted ? Time.time >= removeAfter :
                    age >= vanillaDelay && (!aircraft.disabled || !hasPrevious || aircraft.IsLanded())) break;
                await UniTask.Delay(1000);
            }
            if (!token.IsCancellationRequested && aircraft.IsServer) Destroy(gameObject);
        }
    }

    // A previously disabled carrier can still receive native pilot damage or finish a
    // pre-impact ejection. Those callbacks must not credit the kill twice, start a second
    // evacuation, or return the destroyed aircraft to inventory when it settles at a base.
    [HarmonyPatch]
    internal static class MegaplaneCrashLifecyclePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "ServerDisableUnit",nameof(Aircraft.ReturnToInventory),"EjectionSequence" })
            {
                MethodInfo target = AccessTools.DeclaredMethod(typeof(Aircraft),name,Type.EmptyTypes);
                if (target != null) yield return target;
            }
        }

        private static bool Prefix(Aircraft __instance, MethodBase __originalMethod)
        {
            if (!__instance.TryGetComponent(out MegaplaneCrash crash) || !crash.Impacted) return true;
            return __originalMethod.Name == "ServerDisableUnit" && !__instance.disabled;
        }
    }

    [HarmonyPatch]
    internal static class MegaplaneCrashCleanupPatch
    {
        private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(Aircraft), "WaitRemoveAircraft", new[] { typeof(float) });
        private static bool Prepare() => TargetMethod() != null;
        private static bool Prefix(Aircraft __instance, float delay, ref UniTask __result)
        {
            if (delay < 29f || !__instance.IsServer || !__instance.disabled || !MegaplaneModule.Is(__instance) ||
                __instance.unitState == Unit.UnitState.Returned || !__instance.TryGetComponent(out MegaplaneCrash crash)) return true;
            __result = crash.RemoveAfterImpact(delay);
            return false;
        }
    }
}
