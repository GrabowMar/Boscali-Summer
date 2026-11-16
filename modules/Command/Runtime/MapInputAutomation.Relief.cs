#if !PUBLIC_RELEASE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    public static partial class MapInputAutomation
    {
        private const BindingFlags ReliefPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        private static float airportX, airportZ;
        private static object ReliefField(string name) => typeof(MfdTerrainRelief).GetField(name, ReliefPrivate).GetValue(null);
        private static float FixFloat(object fix, string name) => (float)fix.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(fix);
        private static bool FixAirborne(object fix) => (bool)fix.GetType().GetField("Airborne", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(fix);

        /// <summary>Sandbox-only comparison of displayed ground, contact anchors and actual batched miniature feet.</summary>
        public static Dictionary<string, object> Grounding(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("MapInputAutomation", "Grounding", "requires a hosted single-player nomodkit scenario");
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || !MfdTerrainRelief.IsDrawing) return Failure("MapInputAutomation", "Grounding", "relief is not drawing");
            IDictionary fixes = (IDictionary)ReliefField("iconFixes");
            if (Text(args, "action") == "focus")
            {
                int most = -1;
                foreach (DictionaryEntry entry in fixes)
                {
                    if (!(entry.Key is AirbaseMapIcon)) continue;
                    float x = FixFloat(entry.Value, "X"), z = FixFloat(entry.Value, "Z");
                    int nearby = 0;
                    foreach (DictionaryEntry contact in fixes)
                        if (contact.Key is UnitMapIcon && !FixAirborne(contact.Value) &&
                            new Vector2(FixFloat(contact.Value, "X") - x, FixFloat(contact.Value, "Z") - z).sqrMagnitude < 3000f * 3000f) nearby++;
                    if (nearby <= most) continue;
                    most = nearby; airportX = x; airportZ = z;
                }
                if (most < 0) return Failure("MapInputAutomation", "Grounding", "no airbase fix on map");
                ReliefNavigator.Focus(new GlobalPosition(airportX, 0f, airportZ), true);
                return new Dictionary<string, object> { ["ok"] = true, ["nearby"] = most, ["x"] = airportX, ["z"] = airportZ };
            }
            if (Text(args, "action") == "zoom")
                MfdTerrainRelief.Rig.ZoomAt(Number(args, "zoom", 4f), .5f, .5f, 0f);

            var root = (GameObject)ReliefField("sceneRoot");
            var settings = (MapSettings)ReliefField("source");
            var eye = (Camera)ReliefField("camera");
            MeshCollider[] colliders = root.GetComponentsInChildren<MeshCollider>();
            Physics.SyncTransforms();
            float spanX = (float)typeof(MfdTerrainRelief).GetProperty("ModelSpanX", ReliefPrivate).GetValue(null);
            float spanZ = (float)typeof(MfdTerrainRelief).GetProperty("ModelSpanZ", ReliefPrivate).GetValue(null);
            float toModel = (float)ReliefField("VerticalScale") * (float)ReliefField("ModelWidth") / 81920f;
            float sea = (float)ReliefField("seaLevel");
            MethodInfo sample = typeof(MfdTerrainRelief).GetMethod("GroundHeight", ReliefPrivate);
            float Ground(float x, float z) => (float)sample.Invoke(null, new object[] { x, z });
            float Hit(float x, float z)
            {
                var ray = new Ray(root.transform.position + new Vector3(x / settings.MapSize.x * spanX, 1000f, z / settings.MapSize.y * spanZ), Vector3.down);
                float best = float.NegativeInfinity;
                foreach (MeshCollider collider in colliders)
                    if (collider.Raycast(ray, out RaycastHit hit, 2000f)) best = Mathf.Max(best, hit.point.y - root.transform.position.y);
                return best;
            }
            Vector2 ScreenAt(float x, float z, float y)
            {
                Vector3 at = eye.WorldToViewportPoint(root.transform.position + new Vector3(x / settings.MapSize.x * spanX, y, z / settings.MapSize.y * spanZ));
                RectTransform viewport = map.mapBackground.rectTransform;
                Rect bounds = viewport.rect;
                Vector3 screen = viewport.TransformPoint(new Vector3(bounds.xMin + at.x * bounds.width, bounds.yMin + at.y * bounds.height));
                return new Vector2(screen.x, screen.y);
            }

            // Find a real saddle in the displayed bake; compare both triangle interiors against the collider, not another interpolation formula.
            float[] heights = (float[])ReliefField("heights");
            int side = (int)ReliefField("Samples"), saddleX = -1, saddleZ = -1;
            float twist = 0f;
            for (int z = 1; z < side - 2; z++)
            for (int x = 1; x < side - 2; x++)
            {
                int at = z * side + x;
                float a = heights[at], b = heights[at + 1], c = heights[at + side], d = heights[at + side + 1];
                if (Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d)) < 20f) continue;
                float bend = Mathf.Abs(a + d - b - c);
                if (bend <= twist) continue;
                twist = bend; saddleX = x; saddleZ = z;
            }
            int samples = 0;
            float sampleError = 0f;
            if (saddleX >= 0)
                foreach (Vector2 fraction in new[] { new Vector2(.2f, .2f), new Vector2(.8f, .8f), new Vector2(.2f, .7f), new Vector2(.7f, .2f) })
                {
                    float x = ((saddleX + fraction.x) / (side - 1) - .5f) * settings.MapSize.x;
                    float z = ((saddleZ + fraction.y) / (side - 1) - .5f) * settings.MapSize.y;
                    float hit = Hit(x, z);
                    if (!float.IsFinite(hit)) continue;
                    samples++; sampleError = Mathf.Max(sampleError, Mathf.Abs(Ground(x, z) - hit));
                }

            Mesh minis = (Mesh)typeof(MapMinis).GetField("mesh", ReliefPrivate).GetValue(null);
            Vector3[] vertices = minis != null ? minis.vertices : Array.Empty<Vector3>();
            int groundCount = 0, feet = 0, parked = 0, airborne = 0;
            float groundError = 0f, airError = 0f, footGap = 0f, footMinGap = float.PositiveInfinity;
            float clearance = (float)ReliefField("SurfaceClearance");
            foreach (DictionaryEntry entry in fixes)
            {
                if (!(entry.Key is UnitMapIcon icon) || icon.unit == null || icon.iconImage == null || !icon.iconImage.isActiveAndEnabled) continue;
                float x = FixFloat(entry.Value, "X"), z = FixFloat(entry.Value, "Z");
                float hit = Hit(x, z);
                if (!float.IsFinite(hit)) continue;
                if (FixAirborne(entry.Value))
                {
                    float lift = Mathf.Clamp((FixFloat(entry.Value, "Altitude") - sea) * toModel - Ground(x, z), 0f, (float)ReliefField("MaximumAirLift"));
                    if (lift < 1f || !(icon.unit is Aircraft)) continue;
                    airborne++;
                    Vector2 want = ScreenAt(x, z, Ground(x, z) + clearance + lift);
                    Vector3 actual = icon.iconImage.transform.position;
                    airError = Mathf.Max(airError, Vector2.Distance(want, new Vector2(actual.x, actual.y)));
                    continue;
                }
                if (new Vector2(x - airportX, z - airportZ).sqrMagnitude > 3000f * 3000f) continue;
                groundCount++;
                if (icon.unit is Aircraft) parked++;
                Vector2 expected = ScreenAt(x, z, hit + clearance);
                Vector3 rendered = icon.iconImage.transform.position;
                groundError = Mathf.Max(groundError, Vector2.Distance(expected, new Vector2(rendered.x, rendered.y)));
                float mx = x / settings.MapSize.x * spanX, mz = z / settings.MapSize.y * spanZ, low = float.PositiveInfinity;
                // Every footprint is a horizontal quad centered on the contact; choose its lowest matching plane directly from the uploaded mesh.
                for (int i = 0; i + 3 < vertices.Length; i++)
                {
                    Vector3 a = vertices[i], b = vertices[i + 1], c = vertices[i + 2], d = vertices[i + 3];
                    if (Mathf.Abs(a.y - b.y) > .0001f || Mathf.Abs(a.y - c.y) > .0001f || Mathf.Abs(a.y - d.y) > .0001f) continue;
                    Vector3 centre = (a + b + c + d) * .25f;
                    if (Mathf.Abs(centre.x - mx) > .002f || Mathf.Abs(centre.z - mz) > .002f || Vector3.Cross(b - a, c - a).sqrMagnitude < .000001f) continue;
                    low = Mathf.Min(low, centre.y);
                }
                if (!float.IsFinite(low)) continue;
                feet++; footGap = Mathf.Max(footGap, low - hit); footMinGap = Mathf.Min(footMinGap, low - hit);
            }
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["relief"] = true, ["samples"] = samples, ["saddleMeters"] = twist, ["sampleErrorModel"] = sampleError,
                ["ground"] = groundCount, ["parked"] = parked, ["feet"] = feet, ["maxFootGapModel"] = footGap,
                ["minFootGapModel"] = float.IsFinite(footMinGap) ? footMinGap : -999f, ["groundErrorPx"] = groundError,
                ["airborne"] = airborne, ["airErrorPx"] = airError, ["zoom"] = MfdTerrainRelief.Zoom,
                ["yaw"] = MfdTerrainRelief.Yaw, ["pitch"] = MfdTerrainRelief.Pitch, ["x"] = airportX, ["z"] = airportZ
            };
        }
    }
}
#endif
