using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Physical bridge repeater. All indications read the same parts and actuators as the flying aircraft.</summary>
    internal sealed class MegaplaneSystemsDisplay : MonoBehaviour
    {
        private const int PodCount = 8, Gear = 8, Assist = 9, Plant = 10, Indicators = 11;
        private const float ScreenWidth = .63f, ScreenHeight = .215f;
        [SerializeField] private Aircraft carrier;
        [SerializeField] private AeroPart[] pods;
        private MeshRenderer face;
        private Mesh mesh;
        private Vector2[] uv;
        private int[] shown;
        private MegaplaneGear gear;
        private MegaplaneFlight flight;
        private MegaplaneReactor reactor;
        private float nextSample;

        internal static Renderer Install(Aircraft aircraft, Transform locator, Dictionary<string, Transform> nodes, int layer)
        {
            var go = new GameObject("GrozaSystemsDisplay") { layer = layer };
            go.transform.SetParent(locator, false);
            go.transform.localPosition = Vector3.back * .004f;
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MegaplaneArt.SystemsDisplay();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            var display = go.AddComponent<MegaplaneSystemsDisplay>();
            display.carrier = aircraft;
            display.pods = new AeroPart[PodCount];
            for (int i = 0; i < PodCount; i++)
                display.pods[i] = nodes["Pod_" + (i < 4 ? "L" : "R") + (i % 4 + 1)].GetComponentInParent<AeroPart>(true);
            return renderer;
        }

        private void Awake() => face = GetComponent<MeshRenderer>();

        private void LateUpdate()
        {
            // The native seat camera enables this face on entry and disables it on exit.
            // Only a cockpit that is actually visited needs a private, mutable mesh.
            if (!face.enabled) { nextSample = 0f; return; }
            if (Time.unscaledTime < nextSample || carrier == null) return;
            nextSample = Time.unscaledTime + .10f;
            if (mesh == null) Initialize();

            bool changed = false;
            for (int i = 0; i < pods.Length; i++)
            {
                float health = MegaplaneFlight.PodHealth(pods[i]);
                changed |= Indicate(i, health <= 0f ? 2 : health < .99f ? 1 : 0);
            }
            // A moving door or leg still reads TRANSIT, even when the command bit is already set.
            int gearState = carrier.NetworkgearDeployed ? (gear.Leg >= .999f ? 2 : 1) : (gear.Door <= .001f && gear.Leg <= .001f ? 0 : 1);
            changed |= Indicate(Gear, gearState);
            changed |= Indicate(Assist, flight.NoseAssist > .05f ? 1 : 0);
            byte band = reactor.State;
            changed |= Indicate(Plant, band >= MegaplaneReactor.Flash ? 3 : band >= 7 ? 2 : band >= 2 ? 1 : 0);
            if (changed) mesh.uv = uv;
        }

        private void Initialize()
        {
            gear = carrier.GetComponent<MegaplaneGear>();
            flight = carrier.GetComponent<MegaplaneFlight>();
            reactor = carrier.GetComponent<MegaplaneReactor>();
            shown = new int[Indicators];
            var points = new Vector3[(1 + Indicators) * 4];
            uv = new Vector2[points.Length];
            var triangles = new int[(1 + Indicators) * 6];
            for (int quad = 0; quad <= Indicators; quad++)
            {
                int vertex = 4 * quad, index = 6 * quad;
                triangles[index] = vertex; triangles[index + 1] = vertex + 1; triangles[index + 2] = vertex + 2;
                triangles[index + 3] = vertex; triangles[index + 4] = vertex + 2; triangles[index + 5] = vertex + 3;
                if (quad == 0) continue;
                int indicator = quad - 1;
                shown[indicator] = -1;
                if (indicator < PodCount) Place(points, vertex, 71 + 92 * (indicator % 4), 111 + 78 * (indicator / 4), 67, 43);
                else Place(points, vertex, 655, 69 + 71 * (indicator - Gear), 186, 50);
            }
            Place(points, 0, 0, 0, 864, 288);
            SampleTile(0, 0, 0, 864, 288);
            mesh = new Mesh { name = "Groza flight deck systems" };
            mesh.MarkDynamic();
            mesh.vertices = points;
            mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        // Front-facing quads in screen pixels. Only the UVs change as the indicators update.
        private static void Place(Vector3[] points, int v, int x, int y, int width, int height)
        {
            float left = (x / 864f - .5f) * ScreenWidth, right = left + width * ScreenWidth / 864f;
            float top = (.5f - y / 288f) * ScreenHeight, bottom = top - height * ScreenHeight / 288f;
            float depth = v == 0 ? 0f : -.0005f;
            points[v] = new Vector3(left, bottom, depth); points[v + 1] = new Vector3(left, top, depth);
            points[v + 2] = new Vector3(right, top, depth); points[v + 3] = new Vector3(right, bottom, depth);
        }

        private bool Indicate(int indicator, int state)
        {
            if (shown[indicator] == state) return false;
            shown[indicator] = state;
            int vertex = (indicator + 1) * 4;
            if (indicator < PodCount) SampleTile(vertex, 860, 314 + 61 * state, 67, 43);
            else SampleTile(vertex, 12 + 200 * state, 312 + 62 * (indicator - Gear), 186, 50);
            return true;
        }

        private void SampleTile(int vertex, int x, int y, int width, int height)
        {
            float left = (x + .5f) / 1024f, right = (x + width - .5f) / 1024f;
            float top = 1f - (y + .5f) / 512f, bottom = 1f - (y + height - .5f) / 512f;
            uv[vertex] = new Vector2(left, bottom); uv[vertex + 1] = new Vector2(left, top);
            uv[vertex + 2] = new Vector2(right, top); uv[vertex + 3] = new Vector2(right, bottom);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

#if !PUBLIC_RELEASE
        internal object Inspect()
        {
            if (shown == null) return new { ready = false, visible = face.enabled };
            return new { ready = true, visible = face.enabled, port1 = shown[0], starboard1 = shown[4],
                gear = shown[Gear], assist = shown[Assist], plant = shown[Plant], pods = new System.ArraySegment<int>(shown, 0, PodCount).ToArray() };
        }
#endif
    }
}
