using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Spooktober.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>A temporary pocket arena above the battlefield, with native damageable anchors and a way home.
    /// Uses the current physics scene so weapons, target locks and every connected aircraft part keep working.</summary>
    internal static class HellDimension
    {
        private static readonly List<Unit> anchors = new List<Unit>(), guards = new List<Unit>();
        private static readonly List<GameObject> beacons = new List<GameObject>();
        private static readonly Dictionary<Renderer, bool> concealed = new Dictionary<Renderer, bool>();
        private static readonly Dictionary<Renderer, bool> concealedClouds = new Dictionary<Renderer, bool>();
        private const BindingFlags CloudFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo CloudLayerField = typeof(LevelInfo).GetField("cloudLayer", CloudFields);
        private static readonly FieldInfo CloudRendererField = typeof(CloudLayer).GetField("cloudRenderer", CloudFields);
        private static readonly FieldInfo[] CloudParticleFields = {
            typeof(CloudLayer).GetField("cloudSystem", CloudFields),
            typeof(CloudLayer).GetField("distantCloudSystem", CloudFields),
            typeof(CloudLayer).GetField("flyThroughSystem", CloudFields)
        };
        private static readonly Dictionary<Unit, (bool Active, bool Enabled)> suspendedUnits = new Dictionary<Unit, (bool, bool)>();
        private static GameObject root, sky;
        private static Mesh groundMesh, skyMesh;
        private static Material groundMaterial, skyMaterial;
        private static HellGrowth.Handle growth;
        private static HellPortal exit;
        private static Aircraft traveller;
        private static GlobalPosition returnAt;
        private static Quaternion returnHeading;
        private static float nextFeed, nextWarning, previousExitHeight;
        private static bool haveExitHeight;
        private static int announcedBroken;

        internal static bool Inside { get; private set; }
        internal static bool Built => root != null;
        internal static bool ContainsCamera(Camera camera)
        {
            if (!Inside || root == null || camera == null) return false;
            Vector3 at = root.transform.InverseTransformPoint(camera.transform.position);
            float radius = HellRules.DimensionRadius + 500f;
            return at.y > -500f && at.x * at.x + at.z * at.z < radius * radius;
        }
        internal static bool Suspended => Built && !root.activeSelf;
        internal static int HiddenCloudRenderers => concealedClouds.Count;
        internal static int ActiveCollidersOutside
        {
            get
            {
                if (Inside) return 0;
                int count = Count(root) + Count(exit?.Root);
                foreach (Unit unit in anchors) if (unit != null) count += Count(unit.gameObject);
                foreach (Unit unit in guards) if (unit != null) count += Count(unit.gameObject);
                return count;

                int Count(GameObject target)
                {
                    if (target == null) return 0;
                    int active = 0;
                    foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
                        if (collider.enabled && collider.gameObject.activeInHierarchy) active++;
                    return active;
                }
            }
        }
        internal static int Broken { get; private set; }
        internal static int Remaining => Built ? HellRules.Anchors - Broken : 0;
        internal static IReadOnlyList<Unit> Anchors => anchors;
        internal static bool IsSurface(Collider collider) => root != null && collider != null && collider.transform.IsChildOf(root.transform);

        internal static bool Enter(HellPortal gate)
        {
            if (Inside || gate == null || !gate.Alive || !GameManager.GetLocalAircraft(out Aircraft host) || host == null || host.disabled)
                return false;
            traveller = host;
            returnAt = (gate.Root.transform.position + gate.Root.transform.forward * 650f + Vector3.up * 1000f).ToGlobalPosition();
            returnHeading = Quaternion.LookRotation(gate.Root.transform.forward, Vector3.up);
            try
            {
                if (!Built) Build(gate);
                SetSuspended(false); // restore the physical floor before relocating the pilot onto it
                sky.SetActive(true);
                SetVisible(true);
                Inside = true;
                HideNativeClouds();
                haveExitHeight = false;
                Move(host, root.transform.TransformPoint(new Vector3(0f, 650f, -3600f)), Quaternion.identity);
                FallenAce.Announce("HELL DIMENSION · BREAK THE THREE RIFT ANCHORS");
                FallenAce.Announce("DIVE INTO THE REAR RIFT TO RETREAT");
                SpookAudio.Play(SpookAudio.Gate, 1f);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[Spooktober] Hell dimension entry: " + e);
                Clear(true);
                FallenAce.Announce("THE RIFT REJECTED YOU");
                return false;
            }
        }

        internal static void Frame(float now)
        {
            if (!Built) return;
            Broken = 0;
            for (int i = 0; i < anchors.Count; i++)
            {
                bool alive = anchors[i] != null && !anchors[i].disabled;
                if (!alive) Broken++;
                if (i < beacons.Count && beacons[i] != null && beacons[i].activeSelf != alive) beacons[i].SetActive(alive);
            }
            if (Broken != announcedBroken)
            {
                announcedBroken = Broken;
                FallenAce.Announce("RIFT ANCHOR BROKEN · " + Remaining + " REMAIN");
            }
            if (HellRules.DimensionWon(Broken))
            {
                HellGate.Close(true); // returns the pilot before destroying the arena or HQ
                return;
            }
            if (!Inside) return;
            if (traveller == null || traveller.disabled || !GameManager.GetLocalAircraft(out Aircraft current) || current != traveller)
            {
                Leave(false); // a death is never a win; a new aircraft can enter again
                return;
            }
            if (now >= nextFeed)
            {
                nextFeed = now + 1f;
                HideNativeClouds(); // catch native emitters recreated or reactivated by the async weather loop
                GameManager.GetLocalHQ(out FactionHQ mine);
                foreach (Unit unit in anchors) Track(mine, unit);
                foreach (Unit unit in guards) Track(mine, unit);
                Track(HellSide.Side.HQ, traveller);
            }
            Vector3 local = root.transform.InverseTransformPoint(traveller.transform.position);
            if (local.y < -150f || new Vector2(local.x, local.z).magnitude > HellRules.DimensionRadius + 500f)
            {
                Leave(true);
                FallenAce.Announce("THE DIMENSION CASTS YOU OUT · THE INVASION CONTINUES");
                return;
            }
            if (now >= nextWarning)
            {
                nextWarning = now + 20f;
                FallenAce.Announce("HELL · " + Remaining + " RIFT ANCHORS REMAIN");
            }
            Vector3 rel = traveller.transform.position - exit.Root.transform.position;
            float height = rel.y;
            if (haveExitHeight && HellRules.EnteredRift(previousExitHeight, height, new Vector2(rel.x, rel.z).magnitude, exit.Radius))
            {
                Leave(true);
                FallenAce.Announce("BACK THROUGH THE RIFT · THE INVASION CONTINUES");
                return;
            }
            previousExitHeight = height;
            haveExitHeight = true;
        }

        private static void Build(HellPortal gate)
        {
            root = new GameObject("Hell Dimension");
            if (Datum.origin != null) root.transform.SetParent(Datum.origin, false);
            GlobalPosition site = gate.Root.transform.position.ToGlobalPosition();
            Vector3 position = new GlobalPosition(site.x, 6500f, site.z).ToLocalPosition();
            position.y = Datum.LocalSeaY + 6500f;
            root.transform.position = position;
            Broken = announcedBroken = 0;
            groundMaterial = HellGroundSurface.Material();
            if (groundMaterial == null) throw new InvalidOperationException("The sooted wasteland surface is unavailable");
            BuildGround();
            Physics.SyncTransforms();
            BuildSky();
            Vector3 exitAt = Surface(root.transform.TransformPoint(new Vector3(0f, 0f, -3600f))) + Vector3.up * 3f;
            exit = HellPortal.Spawn(exitAt, Quaternion.identity, 140f);
            exit.IsDimensionExit = true;
            exit.Shockwave();
            VehicleDefinition definition = AnchorDefinition();
            if (definition == null) throw new InvalidOperationException("No native damageable anchor prefab");
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || HellSide.Side.HQ == null) throw new InvalidOperationException("No server spawner or HELL HQ");
            for (int i = 0; i < HellRules.Anchors; i++)
            {
                float angle = (i * 120f + 30f) * Mathf.Deg2Rad;
                Vector3 at = Surface(root.transform.position + new Vector3(Mathf.Cos(angle) * 2200f, 0f, Mathf.Sin(angle) * 2200f)) + Vector3.up;
                Unit anchor = spawner.SpawnVehicle(definition.unitPrefab, at.ToGlobalPosition(), Quaternion.Euler(0f, i * 120f, 0f),
                    Vector3.zero, HellSide.Side.HQ, "HELL:RIFT-ANCHOR:" + (i + 1), 1f, true, null);
                if (anchor == null) throw new InvalidOperationException("Anchor " + i + " failed to spawn");
                anchors.Add(anchor);
                anchor.NetworkunitName = "RIFT ANCHOR " + (i + 1);
                if (anchor is GroundVehicle vehicle) vehicle.SetHoldPosition(true);
                HellArmy.Enlist(anchor);
                beacons.Add(Beacon(at, i));
                for (int j = 0; j < 2; j++)
                {
                    Vector3 guardAt = Surface(at + new Vector3(j == 0 ? -180f : 180f, 0f, 220f)) + Vector3.up;
                    Unit guard = spawner.SpawnVehicle(definition.unitPrefab, guardAt.ToGlobalPosition(), Quaternion.identity,
                        Vector3.zero, HellSide.Side.HQ, "HELL:ANCHOR-GUARD:" + i + ":" + j, 1f, true, null);
                    if (guard == null) continue;
                    guards.Add(guard);
                    if (guard is GroundVehicle held) held.SetHoldPosition(true);
                    HellArmy.Enlist(guard);
                }
            }
            Physics.SyncTransforms();
            growth = HellGrowth.Create(root.transform, 666, Vector3.zero, HellRules.DimensionRadius, 1f, 4.5f);
        }

        private static VehicleDefinition AnchorDefinition()
        {
            VehicleDefinition fallback = null;
            if (Encyclopedia.i?.vehicles == null) return null;
            foreach (VehicleDefinition definition in Encyclopedia.i.vehicles)
            {
                if (definition == null || definition.unitPrefab == null) continue;
                if (definition.jsonKey == "SPAAG1") return definition;
                if (definition.jsonKey == "MBT1") fallback = definition;
            }
            return fallback;
        }

        private static GameObject Beacon(Vector3 at, int index)
        {
            var beacon = new GameObject("Rift Anchor Beacon " + (index + 1));
            beacon.transform.SetParent(root.transform, true);
            beacon.transform.position = at;
            Mesh shard = HellMeshes.Mesh("hell_spire_b");
            GameObject spire = shard != null ? new GameObject("Anchor Crown", typeof(MeshFilter), typeof(MeshRenderer))
                : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            if (spire.TryGetComponent(out Collider collider)) Object.Destroy(collider); // native anchor remains the damage target
            spire.name = "Obsidian Spire";
            spire.transform.SetParent(beacon.transform, false);
            if (shard != null)
            {
                float scale = 280f / Mathf.Max(.1f, shard.bounds.size.y);
                spire.transform.localScale = Vector3.one * scale;
                spire.transform.localPosition = new Vector3(-shard.bounds.center.x * scale, -shard.bounds.min.y * scale, 45f - shard.bounds.center.z * scale);
                spire.GetComponent<MeshFilter>().sharedMesh = shard;
                spire.GetComponent<Renderer>().sharedMaterials = HellMaterials.Shared;
            }
            else
            {
                spire.transform.localPosition = new Vector3(0f, 140f, 35f);
                spire.transform.localScale = new Vector3(35f, 140f, 35f);
                spire.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            }
            ParticleSystem flame = RadGhostLook.NewEmbers("HellAnchorFlame", beacon.transform, 12f, 600, RadGhostLook.FireColour);
            var main = flame.main;
            main.startLifetime = 5f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(35f, 70f);
            var shape = flame.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 5f;
            shape.radius = 25f;
            flame.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            return beacon;
        }

        private static void BuildGround()
        {
            const int slices = 96, rings = 18;
            var vertices = new Vector3[1 + slices * rings];
            var uv = new Vector2[vertices.Length];
            uv[0] = HellGroundSurface.Uv(root.transform.position.ToGlobalPosition());
            var triangles = new List<int>(slices * rings * 6);
            for (int r = 1; r <= rings; r++)
                for (int s = 0; s < slices; s++)
                {
                    float angle = s * Mathf.PI * 2f / slices;
                    float radius = HellRules.DimensionRadius * r / rings;
                    int i = 1 + (r - 1) * slices + s;
                    float rim = Mathf.InverseLerp(.66f, 1f, r / (float)rings);
                    float x = Mathf.Cos(angle) * radius, z = Mathf.Sin(angle) * radius;
                    float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(x * .0004f + 8f, z * .0004f + 17f) * 2f - 1f);
                    float height = Mathf.Pow(ridge, 7f) * 170f + rim * rim * (500f + 900f * Mathf.PerlinNoise(s * .17f, 3.7f));
                    vertices[i] = new Vector3(x, height, z);
                    uv[i] = HellGroundSurface.Uv((root.transform.position + vertices[i]).ToGlobalPosition());
                    int next = 1 + (r - 1) * slices + (s + 1) % slices;
                    if (r == 1) { triangles.Add(0); triangles.Add(next); triangles.Add(i); }
                    else
                    {
                        int prev = i - slices, prevNext = next - slices;
                        triangles.Add(prev); triangles.Add(next); triangles.Add(i);
                        triangles.Add(prev); triangles.Add(prevNext); triangles.Add(next);
                    }
                }
            groundMesh = new Mesh { name = "Hell Pocket Basalt" };
            groundMesh.vertices = vertices;
            groundMesh.uv = uv;
            groundMesh.triangles = triangles.ToArray();
            groundMesh.RecalculateNormals();
            groundMesh.RecalculateBounds();
            var floor = new GameObject("Lava Crust", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            floor.layer = PhysicsLayers.Statics;
            floor.transform.SetParent(root.transform, false);
            floor.GetComponent<MeshFilter>().sharedMesh = groundMesh;
            floor.GetComponent<MeshCollider>().sharedMesh = groundMesh;
            floor.GetComponent<MeshCollider>().sharedMaterial = GameAssets.i?.terrainMaterial;
            floor.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
        }

        private static void BuildSky()
        {
            sky = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(sky.GetComponent<Collider>());
            sky.name = "Hell Dimension Firmament";
            sky.transform.SetParent(root.transform, false);
            sky.transform.localPosition = Vector3.up * 1000f;
            sky.transform.localScale = Vector3.one * 22000f;
            MeshFilter filter = sky.GetComponent<MeshFilter>();
            skyMesh = Object.Instantiate(filter.sharedMesh);
            skyMesh.name = "Hell Inward Firmament";
            int[] triangles = skyMesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                (triangles[i], triangles[i + 1]) = (triangles[i + 1], triangles[i]);
            skyMesh.triangles = triangles;
            skyMesh.RecalculateNormals();
            filter.sharedMesh = skyMesh;
            Shader shader = SpookAssets.HellSky ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? SpookAssets.Hellfire;
            skyMaterial = new Material(shader) { name = "Hell Blood Sky" };
            var color = new Color(.095f, .003f, .018f, 1f);
            if (skyMaterial.HasProperty("_BaseColor")) skyMaterial.SetColor("_BaseColor", color);
            if (skyMaterial.HasProperty("_Color")) skyMaterial.SetColor("_Color", color);
            if (skyMaterial.HasProperty("_Char")) { skyMaterial.SetColor("_Char", color); skyMaterial.SetFloat("_Intensity", 0f); }
            skyMaterial.renderQueue = 1990;
            var renderer = sky.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = skyMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void Track(FactionHQ hq, Unit unit)
        {
            if (hq == null || unit == null || unit.disabled) return;
            TrackingInfo track = hq.GetTrackingData(unit.persistentID);
            if (track == null) hq.RpcUpdateTrackingInfo(unit.persistentID);
            else track.UpdateInfo(unit.GlobalPosition());
        }

        private static Vector3 Surface(Vector3 at) =>
            Physics.Raycast(at + Vector3.up * 1500f, Vector3.down, out RaycastHit hit, 2500f, PhysicsLayers.StaticsMask)
                ? hit.point : at;

        private static void Move(Aircraft aircraft, Vector3 at, Quaternion heading)
        {
            FallenAce.Relocate(aircraft, at, heading * Quaternion.Inverse(aircraft.transform.rotation));
            Physics.SyncTransforms();
        }

        internal static void Leave(bool returnPilot)
        {
            if (returnPilot && Inside && traveller != null && !traveller.disabled)
                Move(traveller, returnAt.ToLocalPosition(), returnHeading);
            Inside = false;
            RestoreNativeClouds();
            traveller = null;
            SetSuspended(true);
            SetVisible(false);
            if (sky != null) sky.SetActive(false);
            haveExitHeight = false;
        }

        internal static void Clear(bool returnPilot)
        {
            Leave(returnPilot);
            SetVisible(true);
            foreach (List<Unit> units in new[] { anchors, guards })
            {
                foreach (Unit unit in units)
                    if (unit != null && unit.Identity != null && unit.Identity.NetId != 0)
                        try { GameAccess.NetworkManagerOrNull?.ServerObjectManager?.Destroy(unit.Identity, !unit.Identity.IsSceneObject); }
                        catch (Exception e) { Plugin.Logger.LogWarning("[Spooktober] anchor cleanup: " + e.Message); }
                units.Clear();
            }
            beacons.Clear();
            suspendedUnits.Clear();
            growth?.Dispose();
            growth = null;
            exit?.Close();
            exit = null;
            if (root != null) Object.Destroy(root);
            if (groundMesh != null) Object.Destroy(groundMesh);
            if (skyMesh != null) Object.Destroy(skyMesh);
            if (skyMaterial != null) Object.Destroy(skyMaterial);
            root = sky = null;
            nextFeed = nextWarning = 0f;
        }

        private static void SetSuspended(bool suspend)
        {
            if (!suspend)
            {
                if (root != null) root.SetActive(true);
                if (exit?.Root != null) exit.Root.SetActive(true);
            }
            if (suspend)
            {
                foreach (List<Unit> units in new[] { anchors, guards })
                    foreach (Unit unit in units)
                    {
                        if (unit == null || suspendedUnits.ContainsKey(unit)) continue;
                        suspendedUnits.Add(unit, (unit.gameObject.activeSelf, unit.enabled));
                        // Native ground jobs inspect enabled, not activeInHierarchy. Suppress them before removing the floor.
                        unit.enabled = false;
                        unit.gameObject.SetActive(false);
                    }
                if (root != null) root.SetActive(false);
                if (exit?.Root != null) exit.Root.SetActive(false);
            }
            else
            {
                foreach (KeyValuePair<Unit, (bool Active, bool Enabled)> pair in suspendedUnits)
                {
                    Unit unit = pair.Key;
                    if (unit == null) continue;
                    Unit.UnitState state = unit.unitState;
                    unit.gameObject.SetActive(pair.Value.Active);
                    unit.enabled = pair.Value.Enabled;
                    // Unit.OnEnable unconditionally marks a unit Active; retain its existing damage/abandonment state.
                    unit.NetworkunitState = state;
                }
                suspendedUnits.Clear();
            }
            Physics.SyncTransforms();
        }

        private static void HideNativeClouds()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            CloudLayer layer = level != null ? CloudLayerField?.GetValue(level) as CloudLayer : null;
            if (layer == null) return;
            // Only renderers owned by the native CloudLayer are touched. Weather simulation,
            // cloud altitude, density, materials and the mission's conditions keep running.
            foreach (Renderer renderer in layer.GetComponentsInChildren<Renderer>(true)) Hide(renderer);
            Hide(CloudRendererField?.GetValue(layer) as Renderer);
            foreach (FieldInfo field in CloudParticleFields)
                if (field?.GetValue(layer) is ParticleSystem particles)
                    foreach (Renderer renderer in particles.GetComponentsInChildren<Renderer>(true)) Hide(renderer);

            void Hide(Renderer renderer)
            {
                if (renderer == null) return;
                if (!concealedClouds.ContainsKey(renderer)) concealedClouds.Add(renderer, renderer.forceRenderingOff);
                // The native async loop toggles child GameObjects; forceRenderingOff survives those toggles.
                renderer.forceRenderingOff = true;
            }
        }

        private static void RestoreNativeClouds()
        {
            foreach (KeyValuePair<Renderer, bool> pair in concealedClouds)
                if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            concealedClouds.Clear();
        }

        private static void SetVisible(bool visible)
        {
            if (visible)
            {
                foreach (KeyValuePair<Renderer, bool> pair in concealed)
                    if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
                concealed.Clear();
                return;
            }
            if (root != null) Hide(root);
            foreach (Unit unit in anchors) if (unit != null) Hide(unit.gameObject);
            foreach (Unit unit in guards) if (unit != null) Hide(unit.gameObject);

            void Hide(GameObject target)
            {
                foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
                {
                    if (!concealed.ContainsKey(renderer)) concealed.Add(renderer, renderer.forceRenderingOff);
                    renderer.forceRenderingOff = true;
                }
            }
        }

        internal static void Reset()
        {
            Clear(false);
            Broken = announcedBroken = 0;
        }
    }

    internal sealed class HellWeatherExclusion : IWeatherCameraExclusion
    {
        public bool Contains(Camera camera) => HellDimension.ContainsCamera(camera);
    }
}
