using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>The four watch desks share one atlas. Each aircraft selects its own instrument pages.</summary>
    internal sealed class MegaplaneCrewDisplays : MonoBehaviour
    {
        private static readonly int BaseTransform = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTransform = Shader.PropertyToID("_MainTex_ST");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int MainColor = Shader.PropertyToID("_Color");
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        [SerializeField] private Aircraft carrier;
        [SerializeField] private AeroPart bridge;
        [SerializeField] private MeshRenderer[] faces;
        private MegaplaneReactor plant;
        private MegaplaneBay bay;
        private MegaplaneRailgun mount;
        private MaterialPropertyBlock block;
        private readonly int[] shown = { -2, -2, -2, -2 };
        private float nextSample;

        internal static void Install(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            if (GameManager.IsHeadless) return;
            string[] names = { "Nav", "Comms", "Engineering", "Cargo" };
            var screens = new MeshRenderer[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                if (!nodes.TryGetValue("FlightDeck_StationScreen_" + names[i], out Transform desk) ||
                    !desk.TryGetComponent(out screens[i])) return;
                screens[i].shadowCastingMode = ShadowCastingMode.Off;
                screens[i].receiveShadows = false;
            }
            var display = screens[0].gameObject.AddComponent<MegaplaneCrewDisplays>();
            display.faces = screens;
            display.bridge = screens[0].GetComponentInParent<AeroPart>(true);
            display.carrier = aircraft;
        }

        private void Start()
        {
            block = new MaterialPropertyBlock();
            plant = carrier.GetComponent<MegaplaneReactor>();
            bay = carrier.GetComponent<MegaplaneBay>();
            mount = carrier.GetComponent<MegaplaneRailgun>();
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < nextSample) return;
            nextSample = Time.unscaledTime + .2f;
            bool online = carrier != null && !carrier.disabled && bridge != null && !bridge.IsDetached();
            int sector = !online ? -1 : Mathf.FloorToInt((carrier.transform.eulerAngles.y + 22.5f) / 45f) % 8;
            Page(0, sector);
            Page(1, online ? 0 : -1); // Printed-style interphone directory.
            Page(2, online && plant != null ? MegaplaneSystemsDisplay.PlantPage(plant.State) : -1);
            Page(3, online ? CargoPage() : -1);
        }

        private int CargoPage()
        {
            if (bay == null) return mount == null ? -1 : mount.Door > .01f || mount.Lower > .001f ? 7 : 6;
            int front = bay.State[0], aft = bay.State[1];
            if (front == 4 || aft == 4) return 5;
            if (front == 3 || aft == 3) return 4;
            if (front == 1 || aft == 1) return 2;
            if (bay.Door > .01f) return 1;
            return front == 2 || aft == 2 ? 3 : 0;
        }

        private void Page(int desk, int row)
        {
            var screen = faces[desk];
            if (screen == null || shown[desk] == row) return;
            shown[desk] = row;
            screen.GetPropertyBlock(block);
            var offset = new Vector4(1f, 1f, 0f, -Mathf.Max(row, 0) / 8f);
            block.SetVector(BaseTransform, offset);
            block.SetVector(MainTransform, offset);
            Color light = row < 0 ? Color.black : Color.white;
            block.SetColor(BaseColor, light);
            block.SetColor(MainColor, light);
            block.SetColor(Emission, light);
            screen.SetPropertyBlock(block);
        }

#if !PUBLIC_RELEASE
        internal object Inspect() => new { stations = faces.Length, heading = ReadPage(0), circuit = ReadPage(1),
            engineering = ReadPage(2), cargo = ReadPage(3) };

        private int ReadPage(int index)
        {
            if (block == null || faces[index] == null) return -1;
            faces[index].GetPropertyBlock(block);
            return block.GetColor(BaseColor).r < .5f ? -1 : Mathf.RoundToInt(-8f * block.GetVector(BaseTransform).w);
        }
#endif
    }
}
