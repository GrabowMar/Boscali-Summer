#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Free-flight starting conditions and an independent contact witness for the sandbox.</summary>
    public static class MegaplaneCrashProbe
    {
        private const string PeerName = "Groza crash check ";
        private static readonly Vector3 DrySite = new Vector3(-24500f, 0f, -17400f);

        public static object Run(Dictionary<string, object> args)
        {
            string step = (string)args["step"];
            string profile = args.TryGetValue("profile", out object which) ? (string)which : "heavy";
            Aircraft a = args.TryGetValue("idUnit", out object unit) ? unit as Aircraft : null;
            return Execute(step, profile, a);
        }

        public static string Peer(string step, string profile)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_PEER_PORT")))
                throw new InvalidOperationException("A loopback sandbox is required");
            Aircraft a = Object.FindObjectsOfType<Aircraft>().FirstOrDefault(p => p.NetworkUniqueName == PeerName + profile);
            if (step == "spawn" && a == null)
            {
                if (NuclearOption.Networking.NetworkManagerNuclearOption.i.Server.Active != true)
                    throw new InvalidOperationException("Only the host creates the test aircraft");
                var def = MegaplaneTemplate.Definition;
                if (def == null) return "{}";
                a = NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null, def.unitPrefab, def.aircraftParameters.loadouts[0], 1f, default,
                    new GlobalPosition(DrySite.x, 750f, DrySite.z), Quaternion.Euler(0f, 142f, 0f), Vector3.forward * 85f,
                    null, FactionRegistry.GetSortedHQs()[0], PeerName + profile, .5f, .5f);
            }
            return JsonConvert.SerializeObject(Execute(step, profile, a, true));
        }

        private static object Execute(string step, string profile, Aircraft a, bool all = false)
        {
            if (step.StartsWith("crew",StringComparison.Ordinal)) return Crew(step, profile, a);
            if (step == "spawn") return new { present = a != null, id = a != null ? a.NetId : 0 };
            if (step == "predamage")
            {
                if (a == null || !a.IsServer) throw new InvalidOperationException("A live host aircraft is required");
                a.SetGear(profile == "lesser" || profile == "touchdown");
                if (profile == "touchdown" && a.GetComponent<GentleApproach>() == null)
                    a.gameObject.AddComponent<GentleApproach>().Aircraft = a;
                if (profile != "damaged" && profile != "historical") return new { removed = false };
                foreach (string owner in new[] { "wing2_L", "pod4_R" })
                {
                    var box = a.GetComponentsInChildren<MegaplaneHitbox>().First(h => h.Owner != null && h.Owner.name == owner);
                    box.TakeDamage(0f, 0f, 1f, 0f, 120f, PersistentID.None);
                }
                // Leave an attached panel at reduced native health. Its damaged skin must
                // remain visible on wreckage reconstructed by a player arriving afterward.
                var witness = a.GetComponentsInChildren<MegaplaneHitbox>().First(h => h.Owner != null && h.Owner.name == "wingtip_R");
                witness.TakeDamage(0f, 0f, 1f, 0f, 72f, PersistentID.None);
                return new { ok = true };
            }
            if (step == "gearReady")
            {
                if (a == null || !a.IsServer) return false;
                bool down = profile == "lesser" || profile == "touchdown";
                if (a.NetworkgearDeployed != down) a.SetGear(down);
                var actuator = a.GetComponent<MegaplaneGear>();
                return down ? actuator.Leg > .98f && actuator.Door > .98f : actuator.Leg < .02f && actuator.Door < .02f;
            }
            if (step == "release") return Release(a, profile);
            if (step == "stumpPhoto" || step == "tipPhoto")
            {
                bool stump = step == "stumpPhoto";
                var face = MegaplaneSectionProbe.FindFace(stump ? "Wing_Outer_L__Fore" : "Wing_Tip_L__Aft", a, stump);
                if (face == null || Camera.main == null) throw new InvalidOperationException("Airborne fracture face absent");
                var frame = face.transform;
                Vector3 look = face.bounds.center;
                float radius = Mathf.Max(5.5f, face.bounds.extents.magnitude);
                Vector3 outward = (stump ? -1f : 1f)*a.transform.right;
                Vector3 eye = look + (outward*1.95f + a.transform.up*.77f - a.transform.forward*.60f)*radius;
                var rig = Camera.main.GetComponent<Photo>() ?? Camera.main.gameObject.AddComponent<Photo>();
                rig.Skin = frame; rig.LocalEye = frame.InverseTransformPoint(eye); rig.LocalLook = frame.InverseTransformPoint(look); rig.Fov = 48f;
                return new { face = face.name, vertices = face.GetComponent<MeshFilter>().sharedMesh.vertexCount, attached = frame.IsChildOf(a.transform) };
            }
            var fields = Object.FindObjectsOfType<MegaplaneWreck>();
            var record = Object.FindObjectsOfType<FlightRecord>().Where(w => w.Profile == profile)
                .OrderByDescending(w => w.StartTime).FirstOrDefault();
            uint id = a != null ? a.NetId : record != null ? record.FlightId : 0;
            var wrecks = all ? fields : fields.Where(w => w.Impact.Spawn == id).ToArray();
            if (step == "created") return wrecks.Length != 0;
            if (step == "contact") return record != null && (record.Contact || record.CrossedSea);
            if (step == "settled") return wrecks.Any(w => w.Settled);
            if (step == "empty") return wrecks.Length == 0;
            if (step == "crease")
            {
                var wreck = wrecks.OrderByDescending(w => w.Age).FirstOrDefault();
                var crush = wreck == null ? null : wreck.GetComponent<MegaplaneWreckCrush>();
                if (crush == null || crush.HotRenderer == null) return new { framed = false };
                var skin = crush.HotRenderer.transform;
                Vector3 look = crush.WorldHotspot;
                Vector3 outward = -crush.WorldContactNormal.normalized;
                Vector3 travel = Vector3.ProjectOnPlane(wreck.Impact.Velocity, Vector3.up).normalized;
                Vector3 eye = look + outward*11f + Vector3.Cross(Vector3.up,travel)*11f - travel*6f + Vector3.up*5f;
                eye.y = Mathf.Max(eye.y, Surface(eye).y + 1.6f);
                var rig = Camera.main.GetComponent<Photo>() ?? Camera.main.gameObject.AddComponent<Photo>();
                rig.Skin = skin; rig.LocalEye = skin.InverseTransformPoint(eye); rig.LocalLook = skin.InverseTransformPoint(look); rig.Fov = 61f;
                return new { framed = true, skin = skin.name };
            }
            if (step == "overview" || step == "close")
            {
                var w = wrecks.OrderByDescending(field => field.Age).First();
                SetPhoto(w, step == "close");
                return new { ok = true };
            }
            if (step != "inspect") throw new ArgumentException("Unknown test step " + step);
            return new
            {
                wrecks = wrecks.Length, nativeDebris = record == null ? 0 : record.Detached.Count(t => t != null && t.gameObject.activeInHierarchy),
                crushMatchesFlight = wrecks.Length != 0 && ValidCrease(wrecks[0], profile),
                sceneFields = fields.Length, aircraftLive = a != null && !a.disabled,
                actualContact = record != null && record.Contact, seaCrossing = record != null && record.CrossedSea,
                impactSpeed = record == null ? 0f : record.Speed, closing = record == null ? 0f : record.Closing,
                contactCollider = record == null ? "" : record.ColliderName,
                witnessPoint = V(record == null ? Vector3.zero : record.Point),
                first = wrecks.Length == 0 ? null : Describe(wrecks[0]),
                fields = wrecks.OrderBy(w => w.Impact.Spawn).Select(Describe).ToArray(),
            };
        }

        private static bool countingKills;

        private static void CountKill(Unit __instance)
        {
            foreach (var witness in Object.FindObjectsOfType<CrewWitness>())
                if (witness.Source == __instance) witness.Kills++;
        }

        private static object Crew(string step, string profile, Aircraft aircraft)
        {
            var witness = Object.FindObjectsOfType<CrewWitness>().FirstOrDefault(w => w.Profile == profile);
            if (step == "crewArm")
            {
                if (aircraft == null || !aircraft.IsServer || aircraft.pilots.Length == 0 || aircraft.Player == null)
                    throw new InvalidOperationException("This case requires the actual player to be seated");
                if (!countingKills)
                {
                    var patch = new HarmonyLib.Harmony("BoscaliSummer.Dev.GrozaCrewWitness");
                    patch.Patch(HarmonyLib.AccessTools.Method(typeof(Unit), nameof(Unit.ReportKilled)),
                        prefix: new HarmonyLib.HarmonyMethod(typeof(MegaplaneCrashProbe),nameof(CountKill)));
                    countingKills = true;
                }
                var holder = new GameObject("Groza crew witness");
                holder.transform.SetParent(Datum.origin,false);
                witness = holder.AddComponent<CrewWitness>();
                witness.Profile = profile; witness.Source = aircraft; witness.Pilot = aircraft.pilots[0];
                witness.Person = aircraft.Player; witness.Unit = aircraft.persistentID; witness.Flight = aircraft.NetId;
                witness.Sample();
                var before = witness.Describe();
                if (profile == "ejected") aircraft.StartEjectionSequence();
                return before;
            }
            if (witness == null) throw new InvalidOperationException("No crew witness for " + profile);
            witness.Sample();
            if (step == "crewReady") return profile != "ejected" || witness.Rescues().Length == 1;
            if (step == "crewRelease")
            {
                var rb = witness.Source.rb;
                Vector3 heading = Vector3.ProjectOnPlane(rb.transform.forward,Vector3.up).normalized;
                bool mild = profile == "survivor" || profile == "noRespawn";
                rb.velocity = heading*(mild ? 52f : 124f) + Vector3.down*(mild ? 21f : 87f);
                witness.ReleasedRescue = witness.Rescues().FirstOrDefault()?.NetId ?? 0;
            }
            if (step == "crewWreck") return Object.FindObjectsOfType<MegaplaneWreck>().Any(w => w.Impact.Spawn == witness.Flight);
            return witness.Describe();
        }

        private sealed class CrewWitness : MonoBehaviour
        {
            private static readonly System.Reflection.FieldInfo Health = HarmonyLib.AccessTools.Field(typeof(Pilot),"hitPoints");
            internal string Profile;
            internal Aircraft Source;
            internal NuclearOption.Networking.Player Person;
            internal PersistentID Unit;
            internal Pilot Pilot;
            internal uint Flight, ReleasedRescue;
            internal int Kills;
            private bool dead, ejected;
            private float health;
            private void Update() => Sample();
            internal void Sample()
            {
                if (Pilot == null) return;
                dead = Pilot.dead; ejected = Pilot.ejected; health = (float)Health.GetValue(Pilot);
            }
            internal PilotDismounted[] Rescues() => Object.FindObjectsOfType<PilotDismounted>()
                .Where(p => p.NetworkparentUnit == Unit && p.NetworkpilotNumber == 0).ToArray();
            internal object Describe()
            {
                var camera = SceneSingleton<CameraStateManager>.i;
                var rescues = Rescues();
                var survivor = rescues.FirstOrDefault();
                var fields = Object.FindObjectsOfType<MegaplaneWreck>().Where(w => w.Impact.Spawn == Flight).ToArray();
                var wreck = fields.FirstOrDefault();
                var hud = SceneSingleton<CombatHUD>.i;
                return new { key = Flight, sourcePresent = Source != null, sourceLive = Source != null && !Source.disabled,
                    seated = Person != null && Person.Aircraft != null && Person.Aircraft.NetId == Flight,
                    hudSeated = hud != null && hud.aircraft != null && hud.aircraft.NetId == Flight,
                    dead, ejected, health, kills = Kills, rescueCount = rescues.Length,
                    rescueId = survivor != null ? survivor.NetId : 0, rescueAtRelease = ReleasedRescue,
                    sameRescue = survivor != null && survivor.NetId == ReleasedRescue, playerOwnsRescue = survivor != null &&
                        survivor.Networkplayer == Person && Person != null && Person.PilotDismounted == survivor,
                    camera = camera != null && camera.mainCamera != null && camera.mainCamera.isActiveAndEnabled,
                    pivot = camera != null && camera.cameraPivot != null && camera.cameraPivot.gameObject.activeInHierarchy,
                    attachedView = Source != null && camera != null && (camera.transform.IsChildOf(Source.transform) ||
                        camera.cameraPivot != null && camera.cameraPivot.IsChildOf(Source.transform)),
                    view = camera?.currentState?.GetType().Name, map = DynamicMap.mapMaximized,
                    respawns = MissionHelper.CanRespawn, game = GameManager.gameResolution.ToString(),
                    fields = fields.Length, closing = wreck == null ? 0f : -Vector3.Dot(wreck.Impact.Velocity,wreck.Impact.Normal) };
            }
        }
        private static object Release(Aircraft a, string profile)
        {
            if (a == null || !a.IsServer) throw new InvalidOperationException("A live host aircraft is required");
            bool soft = profile == "touchdown", mild = profile == "lesser", water = profile == "water";
            if (!mild && !water && !soft && profile != "shore" && profile != "heavy" && profile != "damaged" && profile != "historical")
                throw new ArgumentException("Unknown flight case " + profile);
            Vector3 site = water ? SeaSite() : DrySite;
            if (mild) site += new Vector3(920f, 0f, 750f);
            if (mild) site = LevelSite(site);
            if (profile == "shore") site = ShoreSite();
            var holder = new GameObject("Groza flight contact witness");
            holder.transform.SetParent(Datum.origin, false);
            var record = holder.AddComponent<FlightRecord>();
            record.Profile = profile; record.FlightId = a.NetId; record.StartTime = Time.time;
            record.Body = a.rb;
            record.Water = water;
            record.Detached = a.damageables.Select(s => s.Damageable).OfType<AeroPart>()
                .Where(p => p != null && p.IsDetached() && !p.transform.IsChildOf(a.transform)).Select(p => p.transform).ToArray();
            foreach (var rb in a.GetComponentsInChildren<Rigidbody>().Distinct())
                rb.gameObject.AddComponent<ContactRelay>().Record = record;

            if (soft)
            {
                // Roll onto the ground from the original spawn. Moving a loaded aircraft between distant
                // positions would register acceleration in the native pilot/G monitor before any wheel contact.
                var approach = a.GetComponent<GentleApproach>();
                if (approach == null) throw new InvalidOperationException("Gear preparation is required");
                approach.Record = record;
                record.Begin();
                return new { id = a.NetId, disabled = a.disabled, gearLeg = a.GetComponent<MegaplaneGear>().Leg,
                    gearDoor = a.GetComponent<MegaplaneGear>().Door, speed = a.rb.velocity.magnitude,
                    start = V(a.rb.position.ToGlobalPosition().AsVector3()), dry = true };
            }
            a.SetGear(mild);
            float azimuth = 142f;
            Quaternion rot = Quaternion.Euler(soft ? 0f : mild ? 3f : 10f, azimuth, soft ? 0f : mild ? -2f : -7f);
            a.transform.SetPositionAndRotation(new GlobalPosition(site.x, 600f, site.z).ToLocalPosition(), rot);
            a.rb.position = a.transform.position;
            a.rb.rotation = rot;
            Physics.SyncTransforms();
            float nearest = float.MaxValue;
            foreach (var box in a.GetComponentsInChildren<MegaplaneHitbox>())
            {
                Collider c = box.GetComponent<Collider>();
                if (c == null || !c.enabled || c.isTrigger) continue;
                Bounds bounds = c.bounds;
                float ground = water ? Datum.LocalSeaY : Surface(bounds.center).y;
                nearest = Mathf.Min(nearest, bounds.min.y - ground);
            }
            if (nearest == float.MaxValue) throw new InvalidOperationException("No active contact geometry");
            a.rb.position += Vector3.up * ((soft ? .8f : mild ? 7f : 17f) - nearest);
            a.transform.position = a.rb.position;
            Vector3 heading = Quaternion.Euler(0f, azimuth, 0f) * Vector3.forward;
            a.rb.velocity = heading * (soft ? 11f : mild ? 61f : 125f) + Vector3.down * (soft ? 1.3f : mild ? 35f : 64f);
            a.rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            if (water)
            {
                record.WaterSkin = a.GetComponentsInChildren<MegaplaneHitbox>().Select(hit => hit.GetComponent<MeshCollider>())
                    .Where(c => c != null && c.sharedMesh != null && c.enabled).Select(c => c.sharedMesh.vertices
                    .Select(v => c.transform.TransformPoint(v)).OrderBy(v => v.y).First())
                    .Select(v => a.transform.InverseTransformPoint(v)).ToArray();
            }
            record.Begin();
            return new { id = a.NetId, disabled = a.disabled, gearLeg = a.GetComponent<MegaplaneGear>().Leg, gearDoor = a.GetComponent<MegaplaneGear>().Door,
                initialVelocity = V(a.rb.velocity), speed = a.rb.velocity.magnitude,
                dry = !water, start = V(a.rb.position.ToGlobalPosition().AsVector3()), mask = MegaplaneCrash.Mask(a).ToString("X16"),
                startsWithoutOuter = !HasArt(a, "Wing_Outer_L"), startsWithoutPod = !HasArt(a, "Pod_R4") };
        }

        private static bool HasArt(Aircraft a, string name) => a.GetComponentsInChildren<Transform>(true).Any(t => t.name == name);

        private static Vector3 SeaSite()
        {
            foreach (var p in new[] { new Vector3(-34000f,0f,-42000f), new Vector3(41000f,0f,-32000f),
                new Vector3(-38000f,0f,21000f), new Vector3(25000f,0f,31000f) })
                if (Height(p.x, p.z) < .1f) return p;
            throw new InvalidOperationException("No offshore test site on this map");
        }

        private static Vector3 ShoreSite()
        {
            Vector3 wet = SeaSite(), dry = DrySite;
            for (int i = 0; i < 18; i++)
            {
                Vector3 mid = (dry+wet)*.5f;
                if (Height(mid.x,mid.z)>1.4f) dry=mid; else wet=mid;
            }
            return dry + (DrySite-wet).normalized*28f;
        }

        private static Vector3 LevelSite(Vector3 center)
        {
            float best = float.MaxValue;
            Vector3 site = center;
            for (int ix = -4; ix <= 4; ix++)
            for (int iz = -4; iz <= 4; iz++)
            {
                float x = center.x + ix * 340f, z = center.z + iz * 340f;
                float[] sample = { Height(x, z), Height(x-140f,z), Height(x+140f,z), Height(x,z-100f), Height(x,z+100f) };
                if (sample.Min() <= 2f) continue;
                float rise = sample.Max() - sample.Min();
                if (rise < best) { best = rise; site = new Vector3(x,0f,z); }
            }
            return site;
        }

        private static float Height(float x, float z) => Surface(new GlobalPosition(x,0f,z).ToLocalPosition()).y - Datum.LocalSeaY;

        private static Vector3 Surface(Vector3 at)
        {
            Vector3 top = new Vector3(at.x, Datum.LocalSeaY + 4000f, at.z);
            return Physics.Raycast(top, Vector3.down, out RaycastHit hit, 8000f, PhysicsLayers.StaticsMask,
                    QueryTriggerInteraction.Ignore) ? hit.point : new Vector3(at.x, Datum.LocalSeaY, at.z);
        }

        private static bool ValidCrease(MegaplaneWreck wreck, string profile)
        {
            var crush = wreck.GetComponent<MegaplaneWreckCrush>();
            if (profile == "water") return crush == null || crush.Meshes == 0;
            if (profile != "heavy" && profile != "damaged" && profile != "historical") return true;
            return crush != null && crush.Meshes > 0 && crush.MovedVertices > 0 &&
                crush.MaxDisplacement > .02f && crush.MaxDisplacement <= 1.86f;
        }

        private static object Describe(MegaplaneWreck wreck)
        {
            var pieces = wreck.Pieces.Where(p => p != null).ToArray();
            var geometry = pieces.SelectMany(p => p.GetComponentsInChildren<MeshRenderer>(true)).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            string[] names = geometry.Select(r => r.name).ToArray();
            var impact = wreck.Impact;
            var crush = wreck.GetComponent<MegaplaneWreckCrush>();
            int crushedMeshes = crush == null ? 0 : crush.Meshes;
            int creasedVertices = crush == null ? 0 : crush.MovedVertices;
            float maximumCrease = crush == null ? 0f : crush.MaxDisplacement;
            Mesh damagedSkin = crush != null && crush.HotRenderer != null ? crush.HotRenderer.GetComponent<MeshFilter>().sharedMesh : null;
            bool privateMesh = damagedSkin == null || damagedSkin.name.StartsWith("GrozaCrush_",StringComparison.Ordinal) &&
                !MegaplaneTemplate.Definition.unitPrefab.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh == damagedSkin);
            var leg = geometry.FirstOrDefault(m => m.name == "Gear_Nose_L");
            var door = geometry.FirstOrDefault(m => m.name == "GearDoor_N_L");
            float legAngle = leg == null ? -1f : Quaternion.Angle(Quaternion.identity, leg.transform.localRotation);
            float doorAngle = door == null ? -1f : Quaternion.Angle(Quaternion.identity, door.transform.localRotation);
            bool expectedOuter = (impact.Surviving & (1UL << Array.IndexOf(MegaplaneCrash.GroupNames,"Wing_Outer_L"))) != 0;
            bool expectedPod = (impact.Surviving & (1UL << Array.IndexOf(MegaplaneCrash.GroupNames,"Pod_R4"))) != 0;
            var contact = new GlobalPosition(impact.Contact).ToLocalPosition();
            Vector3 surface = impact.Water ? new Vector3(contact.x,Datum.LocalSeaY,contact.z) : Surface(contact);
            return new
            {
                key = impact.Spawn, unit = impact.Unit, gearLeg = (int)impact.GearLeg, gearDoor = (int)impact.GearDoor,
                crushedMeshes, creasedVertices, maximumCrease, privateMesh,
                damageSkins = names.Where(n => n.StartsWith("GrozaSkinDamage_",StringComparison.Ordinal)).OrderBy(n => n).ToArray(),
                gearLegAngle = legAngle, gearDoorAngle = doorAngle, gearLegMatches = Mathf.Abs(legAngle - 90f*(1f-Mathf.SmoothStep(0f,1f,impact.GearLeg/255f))) < 2f,
                gearDoorMatches = Mathf.Abs(doorAngle - 90f*Mathf.SmoothStep(0f,1f,impact.GearDoor/255f)) < 2f, mask = impact.Surviving.ToString("X16"), water = impact.Water,
                surfaceError = Mathf.Abs(surface.y-contact.y), artMatchesMask = names.Contains("Wing_Outer_L") == expectedOuter && names.Contains("Pod_R4") == expectedPod,
                point = V(impact.Contact), velocity = V(impact.Velocity), normal = V(impact.Normal), age = wreck.Age,
                settled = wreck.Settled, count = pieces.Length, drawMeshes = geometry.Length,
                marks = wreck.GetComponentsInChildren<MegaplaneWreckMarks>(true).Length,
                physicsBodies = wreck.GetComponentsInChildren<Rigidbody>(true).Length,
                outer = names.Contains("Wing_Outer_L"), pod = names.Contains("Pod_R4"),
                oppositeWing = names.Contains("Wing_Outer_R"), center = names.Contains("Fuselage_Center"),
                width = pieces.Length < 2 ? 0f : pieces.Max(p => pieces.Max(q => Vector3.Distance(p.position,q.position))),
                pieces = pieces.OrderBy(p=>p.name).Select(p=>new { name = p.name, pos = V(p.position.ToGlobalPosition().AsVector3()) }).ToArray(),
            };
        }

        private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };

        private static void SetPhoto(MegaplaneWreck wreck, bool close)
        {
            var pieces = wreck.Pieces.Where(p=>p != null).ToArray();
            var meshes = pieces.SelectMany(p => p.GetComponentsInChildren<MeshRenderer>()).Where(r=>r.enabled).ToArray();
            if (meshes.Length == 0 || Camera.main == null) throw new InvalidOperationException("No visible field");
            Bounds field = meshes[0].bounds;
            foreach (var mesh in meshes) field.Encapsulate(mesh.bounds);
            Vector3 along = Vector3.ProjectOnPlane(wreck.Impact.Velocity,Vector3.up).normalized;
            if (along.sqrMagnitude < .1f) along = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, along);
            Vector3 look = field.center;
            float radius = Mathf.Max(60f,field.extents.magnitude);
            Vector3 eye = look + radius * (-along*1.1f + right*1.1f + Vector3.up*1.22f);
            if (close)
            {
                // A cut face carries the actual shell profile. Favor a face visible from above the ground.
                var faces = meshes.Where(m => m.name.StartsWith("Fuselage_",StringComparison.Ordinal) && m.name.Contains("__")).ToArray();
                Vector3 Outward(MeshRenderer m) => m.transform.forward * (m.name.EndsWith("__Fore",StringComparison.Ordinal) ? 1f : -1f);
                var face = faces.OrderByDescending(m => m.bounds.extents.magnitude + 18f*Outward(m).y).FirstOrDefault();
                if (face != null)
                {
                    look = face.bounds.center;
                    radius = Mathf.Clamp(face.bounds.extents.magnitude, 10f, 38f);
                    eye = look + radius*(Outward(face)*2.25f + face.transform.right*.38f + Vector3.up*.8f);
                }
                else
                {
                    var body = meshes.Where(m=>m.name.StartsWith("Fuselage_",StringComparison.Ordinal))
                        .OrderByDescending(m=>m.bounds.size.sqrMagnitude).FirstOrDefault();
                    if (body != null) { look = body.bounds.center; radius = Mathf.Max(32f,body.bounds.extents.magnitude); }
                    eye = look + radius*(-along*1.45f + right*1.35f + Vector3.up*.94f);
                }
            }
            eye.y = Mathf.Max(eye.y, Surface(eye).y + 15f);
            var camera = Camera.main;
            var rig = camera.GetComponent<Photo>() ?? camera.gameObject.AddComponent<Photo>();
            rig.Skin = null; rig.Target = wreck; rig.Eye = eye.ToGlobalPosition(); rig.Look = look.ToGlobalPosition(); rig.Fov = close ? 52f : 60f;
        }

        [DefaultExecutionOrder(30500)]
        private sealed class Photo : MonoBehaviour
        {
            internal Transform Skin;
            internal Vector3 LocalEye, LocalLook;
            internal MegaplaneWreck Target;
            internal GlobalPosition Eye, Look;
            internal float Fov;
            private void LateUpdate()
            {
                if (Skin == null && Target == null) { Destroy(this); return; }
                Vector3 eye = Skin != null ? Skin.TransformPoint(LocalEye) : Eye.ToLocalPosition();
                Vector3 look = Skin != null ? Skin.TransformPoint(LocalLook) : Look.ToLocalPosition();
                transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
                GetComponent<Camera>().fieldOfView = Fov;
            }
        }

        [DefaultExecutionOrder(25000)]
        private sealed class GentleApproach : MonoBehaviour
        {
            internal Aircraft Aircraft;
            internal FlightRecord Record;
            private void FixedUpdate()
            {
                if (Aircraft == null || Aircraft.rb == null || Aircraft.rb.isKinematic || (Record != null && Record.Contact))
                { enabled = false; return; }
                var rb = Aircraft.rb;
                Vector3 flat = Vector3.ProjectOnPlane(rb.velocity, Vector3.up);
                float sink = 0f;
                if (Record != null)
                {
                    float clearance = float.MaxValue;
                    foreach (var box in Aircraft.GetComponentsInChildren<MegaplaneHitbox>())
                    {
                        var c = box.GetComponent<Collider>();
                        if (c == null || !c.enabled || c.isTrigger) continue;
                        clearance = Mathf.Min(clearance, c.bounds.min.y-Surface(c.bounds.center).y);
                    }
                    sink = Mathf.Clamp(clearance*.38f, .85f, 9f);
                }
                float speed = Mathf.MoveTowards(flat.magnitude, 11f, 4f*Time.fixedDeltaTime);
                rb.velocity = flat.normalized*speed + Vector3.up*Mathf.MoveTowards(rb.velocity.y,-sink,4.8f*Time.fixedDeltaTime);
            }
        }

        private sealed class FlightRecord : MonoBehaviour
        {
            internal string Profile, ColliderName = "";
            internal Vector3[] WaterSkin = Array.Empty<Vector3>();
            internal Transform[] Detached = Array.Empty<Transform>();
            internal Rigidbody Body;
            internal uint FlightId;
            internal float StartTime;
            internal bool Contact, CrossedSea, Water, Started;
            internal float Speed, Closing;
            internal Vector3 Point, PriorVelocity;
            internal void Begin() { Started = true; PriorVelocity = Body.velocity; }
            private void FixedUpdate()
            {
                if (!Started || Body == null) return;
                PriorVelocity = Body.velocity;
                if (Water && WaterSkin.Any(p => (Body.position + Body.rotation*p).y <= Datum.LocalSeaY)) CrossedSea = true;
            }
        }

        private sealed class ContactRelay : MonoBehaviour
        {
            internal FlightRecord Record;
            private void OnCollisionEnter(Collision c)
            {
                if (Record == null || !Record.Started || Record.Contact || c.contactCount == 0 || c.collider.GetComponentInParent<Unit>() != null) return;
                var contact = c.GetContact(0);
                Record.Contact = true;
                Record.Speed = c.relativeVelocity.magnitude;
                Record.Closing = Mathf.Abs(Vector3.Dot(c.relativeVelocity, contact.normal));
                Record.Point = contact.point.ToGlobalPosition().AsVector3();
                Record.ColliderName = c.collider.name;
            }
            private void OnTriggerEnter(Collider c)
            {
                if (Record != null && Record.Started && Record.Water && c.sharedMaterial == GameAssets.i.WaterMaterial) Record.CrossedSea = true;
            }
        }
    }
}
#endif
