#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.SeaStructures.Domain;
using BoscaliSummer.Modules.SeaStructures.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.SeaStructures
{
    /// <summary>nomodkit single-player test hook (tests/ingame/sea-structures.json); inert outside a sim run.</summary>
    public static class SeaStructureAutomation
    {
        private const string Source = "SeaStructureAutomation";
        private static readonly FieldInfo TurretDisabled = typeof(Turret).GetField("disabled", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TurretParts = typeof(Turret).GetField("criticalParts", BindingFlags.Instance | BindingFlags.NonPublic);

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure(Source, "Step", "requires a hosted single-player nomodkit scenario");
            SeaStructureService service = SeaStructureService.Active;
            if (service == null) return Failure(Source, "Step", "Sea structures module not installed");
            string action = Text(args, "action") ?? "status";
            int index = (int)Number(args, "index", -1f); // default: the newest structure
            if (index < 0) index = SeaStructure.All.Count - 1;
            SeaStructure target = index >= 0 && index < SeaStructure.All.Count ? SeaStructure.All[index] : null;
            switch (action)
            {
                case "spawn":
                {
                    // "kind": rig | fort, in open water near map point "x","z", owned by "faction".
                    SeaKind kind = (Text(args, "kind") ?? "fort") == "rig" ? SeaKind.OilRig : SeaKind.SeaFort;
                    FactionHQ hq = FactionRegistry.HqFromName(Text(args, "faction") ?? "Primeva");
                    Vector3 near = new GlobalPosition(Number(args, "x", 30000f), 0f, Number(args, "z", -30000f)).ToLocalPosition();
                    var taken = new List<Vector3>();
                    foreach (SeaStructure s in SeaStructure.All) taken.Add(s.transform.position);
                    if (!SeaStructureService.FindSite(near, 0f, 12000f, taken, out Vector3 site)) return Failure(Source, action, "no open water near the point");
                    Ship ship = service.Spawn(kind, site.ToGlobalPosition(), Number(args, "heading", 0f), hq);
                    if (ship == null) return Failure(Source, action, "the structure was not spawned (no template or spawner)");
                    target = ship.GetComponent<SeaStructure>();
                    break;
                }
                case "hit":
                {
                    // The game's own TakeDamage on one section ("section": name) or on all of them, "impact" hit points each.
                    if (target == null) return Failure(Source, action, "no structure at that index");
                    string name = Text(args, "section");
                    float impact = Number(args, "impact", 30f);
                    foreach (KeyValuePair<string, UnitPart> part in target.Parts())
                        if (name == null || string.Equals(part.Key, name, StringComparison.OrdinalIgnoreCase))
                            part.Value.TakeDamage(0f, 0f, 1f, 0f, impact, default);
                    break;
                }
                case "look":
                {
                    // After a scenario "camera" op: sit "distance" m out on "bearing", "height" m up, looking at the structure "lift" m above the sea.
                    if (target == null || SceneSingleton<CameraStateManager>.i == null) return Failure(Source, action, "no structure or camera");
                    Vector3 aim = target.transform.TransformPoint(new Vector3(Number(args, "aimX", 0f), Number(args, "lift", 25f), Number(args, "aimZ", 0f)));
                    Vector3 eye = target.transform.position + Quaternion.Euler(0f, Number(args, "bearing", 30f), 0f) * Vector3.forward *
                                  Number(args, "distance", 220f) + Vector3.up * Number(args, "height", 60f);
                    SceneSingleton<CameraStateManager>.i.SetCameraPosition(eye.ToGlobalPosition(), Quaternion.LookRotation(aim - eye));
                    SceneSingleton<CameraStateManager>.i.cameraVelocity = Vector3.zero;
                    break;
                }
                case "detail":
                {
                    if (target == null) return Failure(Source, action, "no structure at that index");
                    Material material = null;
                    foreach (MeshRenderer renderer in target.GetComponentsInChildren<MeshRenderer>(true))
                        if (renderer.name == SeaTemplates.MeshPrefix + "0") { material = renderer.sharedMaterial; break; }
                    if (material == null || !material.HasProperty("_DetailAlbedoMapScale") || !material.HasProperty("_DetailNormalMapScale"))
                        return Failure(Source, action, "the structure has no native URP detail material");
                    // Keep the compiled detail keyword active: an A/B image proves the shipped shader executes its detail path.
                    bool enabled = Number(args, "enabled", 1f) != 0f;
                    material.SetFloat("_DetailAlbedoMapScale", enabled ? 0.30f : 0f);
                    material.SetFloat("_DetailNormalMapScale", enabled ? 0.45f : 0f);
                    break;
                }
            }
            return Status(action, target);
        }

        private static Dictionary<string, object> Status(string action, SeaStructure target)
        {
            var result = new Dictionary<string, object>
            {
                ["ok"] = true, ["step"] = action, ["count"] = SeaStructure.All.Count,
                ["rigTemplate"] = SeaTemplates.Of(SeaKind.OilRig) != null, ["fortTemplate"] = SeaTemplates.Of(SeaKind.SeaFort) != null,
                ["rigDonor"] = SeaTemplates.Donor(SeaKind.OilRig), ["fortDonor"] = SeaTemplates.Donor(SeaKind.SeaFort)
            };
            if (target == null || target.Ship == null) return result;
            int parts = 0, detached = 0, turrets = 0, stations = target.Ship.weaponStations.Count;
            float worst = 100f;
            foreach (KeyValuePair<string, UnitPart> part in target.Parts())
            {
                parts++;
                if (part.Value.IsDetached()) detached++;
                worst = Mathf.Min(worst, part.Value.hitPoints);
            }
            foreach (Turret _ in target.GetComponentsInChildren<Turret>()) turrets++;
            target.Wear(out int worn, out int pieces, out int gone);
            var registered = new HashSet<Weapon>();
            foreach (WeaponStation station in target.Ship.weaponStations)
                foreach (Weapon weapon in station.Weapons) if (weapon != null) registered.Add(weapon);
            var disabledWeapons = new HashSet<Weapon>();
            var supportLinked = new HashSet<Weapon>();
            foreach (Turret turret in target.GetComponentsInChildren<Turret>(true))
            {
                bool disabled = TurretDisabled?.GetValue(turret) is bool value && value;
                UnitPart[] dependencies = TurretParts?.GetValue(turret) as UnitPart[];
                foreach (WeaponStation station in turret.GetWeaponStations())
                    foreach (Weapon weapon in station.Weapons)
                    {
                        if (weapon == null) continue;
                        if (disabled) disabledWeapons.Add(weapon);
                        UnitPart owner = weapon.GetComponentInParent<UnitPart>();
                        if (owner != null && dependencies != null && Array.IndexOf(dependencies, owner) >= 0) supportLinked.Add(weapon);
                    }
            }
            var roster = new List<string>();
            int launchers = 0, fixedLaunchers = 0, readyWeapons = 0, flightDeckWeapons = 0, fixedReady = 0, fixedSupportLinks = 0;
            foreach (Weapon weapon in registered)
            {
                Vector3 at = target.transform.InverseTransformPoint(weapon.transform.position);
                if (weapon is MissileLauncher)
                {
                    launchers++;
                    if (weapon.GetComponentInParent<Turret>() == null)
                    {
                        fixedLaunchers++;
                        if (!disabledWeapons.Contains(weapon) && weapon.GetAmmoTotal() > 0) fixedReady++;
                        if (supportLinked.Contains(weapon)) fixedSupportLinks++;
                    }
                }
                if (weapon.attachedUnit == target.Ship && weapon.GetAmmoTotal() > 0 && !disabledWeapons.Contains(weapon)) readyWeapons++;
                if (target.Layout.Kind == SeaKind.SeaFort && at.x > -93f && at.x < -47f && at.z > -11f && at.z < 11f) flightDeckWeapons++;
                roster.Add((weapon.info != null ? weapon.info.weaponName : weapon.name) + " [" + weapon.GetType().Name + "] @" +
                           at.x.ToString("0.0", CultureInfo.InvariantCulture) + ", " + at.y.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
                           at.z.ToString("0.0", CultureInfo.InvariantCulture) + " ammo " + weapon.GetAmmoTotal());
            }
            result["nativeWeaponCount"] = registered.Count;
            result["nativeWeaponsReady"] = readyWeapons;
            result["nativeMissileLaunchers"] = launchers;
            result["fixedMissileLaunchers"] = fixedLaunchers;
            result["fixedWeaponsReady"] = fixedReady;
            result["fixedWeaponSupportLinks"] = fixedSupportLinks;
            result["weaponPlacements"] = string.Join(" | ", roster);
            result["weaponsInFlightDeck"] = flightDeckWeapons;
            Airbase airbase = target.GetComponent<Airbase>();
            result["inheritedAiRunways"] = airbase != null ? airbase.runways.Length : 0;
            if (target.Layout.Kind == SeaKind.SeaFort) CheckFlightDeck(target, result);
            CheckTextures(target, result);
            // What is drawn that is not ours: donor renderers left visible, with where they sit.
            var donor = new List<string>();
            foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || r.forceRenderingOff || !r.enabled || r.name.StartsWith("BS")) continue;
                MeshFilter filter = r.GetComponent<MeshFilter>();
                if (r is MeshRenderer && (filter == null || filter.sharedMesh == null)) continue;
                string path = r.name;
                for (Transform t = r.transform.parent; t != null && t != target.transform && path.Length < 110; t = t.parent) path = t.name + "/" + path;
                Vector3 at = target.transform.InverseTransformPoint(r.bounds.center);
                donor.Add(path + " @" + at.x.ToString("0") + "," + at.y.ToString("0") + "," + at.z.ToString("0") + " size " + r.bounds.size.magnitude.ToString("0"));
            }
            result["donorRenderers"] = string.Join(" | ", donor);
            var ships = new List<string>();
            foreach (ShipDefinition def in Encyclopedia.i.ships)
                if (def != null && def.unitPrefab != null)
                    ships.Add(def.jsonKey + ":" + def.unitPrefab.GetComponentsInChildren<Turret>(true).Length + "t/" +
                              def.unitPrefab.GetComponentsInChildren<ShipPart>(true).Length + "p/" + def.unitPrefab.GetComponentsInChildren<Hangar>(true).Length + "h");
            result["ships"] = string.Join(" ", ships);
            result["kind"] = target.Layout.Key;
            result["name"] = target.Ship.unitName;
            result["disabled"] = target.Ship.disabled;
            result["exploded"] = target.Exploded ? 1 : 0;
            result["gone"] = gone;
            result["sections"] = parts;
            result["detached"] = detached;
            result["worstHp"] = worst;
            result["turrets"] = turrets;
            result["stations"] = stations;
            result["wornSections"] = worn;
            result["fallenPieces"] = pieces;
            result["height"] = target.transform.position.y - Datum.LocalSeaY;
            result["tilt"] = Vector3.Angle(target.transform.up, Vector3.up);
            return result;
        }

        private static void CheckTextures(SeaStructure target, Dictionary<string, object> result)
        {
            // Read the material actually drawn by every authored section; creating a new material here could hide a binding bug.
            Material material = null;
            int sections = 0, tangentMeshes = 0;
            bool sameMaterial = true;
            foreach (MeshRenderer renderer in target.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.name.StartsWith(SeaTemplates.MeshPrefix) ||
                    !int.TryParse(renderer.name.Substring(SeaTemplates.MeshPrefix.Length), out _)) continue;
                sections++;
                if (material == null) material = renderer.sharedMaterial;
                sameMaterial &= material != null && renderer.sharedMaterial == material;
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                Vector4[] tangents = mesh.tangents;
                bool valid = tangents.Length == mesh.vertexCount && tangents.Length > 0;
                foreach (Vector4 tangent in tangents)
                {
                    float length = tangent.x * tangent.x + tangent.y * tangent.y + tangent.z * tangent.z;
                    valid &= length > 0.9f && length < 1.1f && Mathf.Abs(Mathf.Abs(tangent.w) - 1f) < 0.01f;
                }
                if (valid) tangentMeshes++;
            }
            bool mapsValid = material != null, detailMapsValid = material != null;
            Texture2D albedo = null;
            long memory = 0, sharedDetailMemory = 0;
            foreach (string map in new[] { "albedo", "normal", "surface", "detailAlbedo", "detailNormal" })
            {
                bool detail = map.StartsWith("detail");
                string property = map == "albedo" ? "_BaseMap" : map == "normal" ? "_BumpMap" : map == "surface" ? "_MetallicGlossMap" :
                    map == "detailAlbedo" ? "_DetailAlbedoMap" : "_DetailNormalMap";
                if (map == "albedo" && material != null && !material.HasProperty(property)) property = "_MainTex";
                Texture2D texture = material != null && material.HasProperty(property) ? material.GetTexture(property) as Texture2D : null;
                if (map == "albedo") albedo = texture;
                int size = detail ? 1024 : map == "albedo" ? 4096 : 2048;
                string expectedName = detail ? "Sea_detail_" + (map == "detailAlbedo" ? "albedo" : "normal") : target.Layout.Key + "_" + map;
                bool valid = texture != null && texture.width == size && texture.height == size &&
                             texture.name == expectedName && texture.isDataSRGB == (map == "albedo") &&
                             texture.mipmapCount > 1 && texture.filterMode == FilterMode.Trilinear && texture.anisoLevel >= 4 &&
                             texture.wrapMode == (detail ? TextureWrapMode.Repeat : TextureWrapMode.Clamp);
                if (detail) detailMapsValid &= valid;
                else mapsValid &= valid;
                result[map + "Width"] = texture != null ? texture.width : 0;
                result[map + "Height"] = texture != null ? texture.height : 0;
                result[map + "Format"] = texture != null ? texture.format.ToString() : "missing";
                result[map + "SRGB"] = texture != null && texture.isDataSRGB;
                result[map + "Mips"] = texture != null ? texture.mipmapCount : 0;
                if (texture != null)
                {
                    long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
                    if (detail)
                    {
                        sharedDetailMemory += bytes;
                        result[map + "InstanceId"] = texture.GetInstanceID();
                    }
                    else memory += bytes;
                }
            }
            result["modelVersion"] = target.Layout.Version;
            result["textureMemoryBytes"] = memory;
            result["textureShader"] = material != null ? material.shader.name : "missing";
            result["textureAtlasReady"] = target.Layout.Version == 2 && sections == target.Layout.Sections.Length && sameMaterial && mapsValid &&
                material.IsKeywordEnabled("_NORMALMAP") &&
                (material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") || material.IsKeywordEnabled("_METALLICGLOSSMAP")) ? 1 : 0;
            result["textureTangentMeshes"] = tangentMeshes;
            result["textureTangentsReady"] = tangentMeshes == target.Layout.Sections.Length ? 1 : 0;
            bool scaled = material != null && material.HasProperty("_DetailAlbedoMapScale");
            string keyword = scaled ? "_DETAIL_SCALED" : "_DETAIL_MULX2";
            Vector2 tiling = material != null ? material.GetTextureScale("_DetailAlbedoMap") : Vector2.zero;
            float albedoScale = scaled ? material.GetFloat("_DetailAlbedoMapScale") : 1f;
            float normalScale = material != null ? material.GetFloat("_DetailNormalMapScale") : 0f;
            bool maskBound = material != null && albedo != null && material.GetTexture("_DetailMask") == albedo;
            bool opaque = material != null && !material.IsKeywordEnabled("_ALPHATEST_ON") && !material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") &&
                (!material.HasProperty("_Surface") || material.GetFloat("_Surface") == 0f) &&
                (!material.HasProperty("_AlphaClip") || material.GetFloat("_AlphaClip") == 0f);
            result["detailSharedMemoryBytes"] = sharedDetailMemory;
            result["detailMaskUsesAlbedo"] = maskBound;
            result["detailUvScaleX"] = tiling.x;
            result["detailUvScaleY"] = tiling.y;
            result["detailAlbedoScale"] = albedoScale;
            result["detailNormalScale"] = normalScale;
            result["detailKeyword"] = material != null && material.IsKeywordEnabled(keyword) ? keyword : "missing";
            result["detailReady"] = (int)result["textureAtlasReady"] == 1 && detailMapsValid && maskBound && opaque &&
                tiling == new Vector2(32f, 32f) && Mathf.Abs(normalScale - 0.45f) < 0.001f &&
                (!scaled || Mathf.Abs(albedoScale - 0.30f) < 0.001f) && material.IsKeywordEnabled(keyword) &&
                (scaled || material.GetFloat("_UVSec") == 0f) ? 1 : 0;
        }

        private static void CheckFlightDeck(SeaStructure target, Dictionary<string, object> result)
        {
            UnitPart deck = null;
            foreach (KeyValuePair<string, UnitPart> part in target.Parts()) if (part.Key == "FLIGHT DECK") deck = part.Value;
            Physics.SyncTransforms();
            int supported = 0, obstacles = 0;
            // The same layer exclusion as native LandingGear.FixedUpdate: this proves real colliders, not render bounds.
            int mask = ~((int)PhysicsLayers.ExclusionZonesMask | (int)PhysicsLayers.IgnoreCollisionsMask);
            foreach (float x in new[] { -89f, -80f, -70f, -60f, -51f })
                foreach (float z in new[] { -8f, 0f, 8f })
                {
                    Vector3 from = target.transform.TransformPoint(new Vector3(x, 40f, z));
                    if (Physics.Raycast(from, -target.transform.up, out RaycastHit hit, 20f, mask, QueryTriggerInteraction.Ignore) &&
                        hit.collider.GetComponent<UnitPart>() == deck && deck != null &&
                        Mathf.Abs(target.transform.InverseTransformPoint(hit.point).y - 26.4f) < 0.08f &&
                        Vector3.Dot(hit.normal, target.transform.up) > 0.99f &&
                        hit.collider.sharedMaterial != null && hit.collider.sharedMaterial != GameAssets.i.terrainMaterial)
                        supported++;
                }
            // Check the compact VTOL deck above its painted surface, including donor weapon and magazine colliders.
            var clear = new Bounds(new Vector3(-70f, 32.5f, 0f), new Vector3(46f, 11.8f, 22f));
            foreach (Collider collider in target.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                Bounds world = collider.bounds;
                var local = new Bounds(target.transform.InverseTransformPoint(world.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                    local.Encapsulate(target.transform.InverseTransformPoint(new Vector3(
                        (i & 1) == 0 ? world.min.x : world.max.x,
                        (i & 2) == 0 ? world.min.y : world.max.y,
                        (i & 4) == 0 ? world.min.z : world.max.z)));
                if (local.Intersects(clear)) obstacles++;
            }
            result["flightDeckSamples"] = 15;
            result["flightDeckSupported"] = supported;
            result["flightDeckObstacles"] = obstacles;
            result["flightDeckClear"] = supported == 15 && obstacles == 0 ? 1 : 0;
        }
    }
}
#endif
