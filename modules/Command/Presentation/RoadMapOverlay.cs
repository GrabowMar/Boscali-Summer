using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using RoadPathfinding;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>The game's road network (`LevelInfo.roadNetwork`) drawn on the MAP relief as projected
    /// vector lines: amber over a dark halo. Relief only, like the hover ranges; one bounded UI mesh that
    /// rebuilds only when the relief view changes, on every terrain render, or it slides behind the ground.</summary>
    internal sealed class RoadMapOverlay : MonoBehaviour, ISceneService
    {
        private CommandSettings settings;
        private DynamicMap dynamicMap;
        private GameObject root;
        private RectTransform rootRect;
        private bool initialized;

        public bool Available => initialized && dynamicMap != null;

        public bool Visible => settings != null && settings.MapRoads.Value;

        public void SetVisible(bool visible)
        {
            if (settings != null) settings.MapRoads.Value = visible;
        }

        public void Configure(CommandSettings config) => settings = config;

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null;
            rootRect = null;
            dynamicMap = null;
            initialized = false;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (settings == null || !settings.Enabled.Value)
            {
                SetShown(false);
                return;
            }

            if (!initialized && DynamicMap.mapMaximized) TryInitialize();
            if (!initialized || dynamicMap == null || dynamicMap.mapImage == null)
            {
                SetShown(false);
                return;
            }

            bool relief = ModuleServices.TryGet(out IMapProjection projection) && projection.IsActive;
            bool shown = settings.MapRoads.Value && DynamicMap.mapMaximized && relief;
            SetShown(shown);
            if (shown && rootRect.parent != dynamicMap.mapImage.transform) MapUiAccess.AttachInk(rootRect, dynamicMap);
        }

        private void TryInitialize()
        {
            dynamicMap = SceneSingleton<DynamicMap>.i;
            if (dynamicMap == null || dynamicMap.mapImage == null) return;
            root = new GameObject("BoscaliRoads", typeof(RectTransform), typeof(RoadGraphic));
            rootRect = (RectTransform)root.transform;
            root.GetComponent<RoadGraphic>().raycastTarget = false;
            MapUiAccess.AttachInk(rootRect, dynamicMap);
            root.SetActive(false);
            initialized = true;
        }

        private void SetShown(bool visible)
        {
            if (root != null && root.activeSelf != visible) root.SetActive(visible);
        }

        [RequireComponent(typeof(CanvasRenderer))]
        [DefaultExecutionOrder(101)] // After the relief commits its pose.
        private sealed class RoadGraphic : MaskableGraphic
        {
            // A UI mesh holds 65k vertices, 4 per point per pass. Thinning to 2 px steps keeps a whole
            // theater (about 9.7k points on Heartland) under it; past the budget the remaining roads drop.
            private const int VertexBudget = 60000;
            private const float MinimumStepPixels = 2f;
            private static readonly Color32 Halo = new Color32(12, 16, 20, 150);
            private static readonly Color32 Ink = new Color32(235, 205, 120, 230);

            private Vector2[] points = new Vector2[256];
            private int count, revision = -1;
            private float lastScale;

            protected override void OnEnable()
            {
                base.OnEnable();
                revision = -1;
            }

            private void LateUpdate()
            {
                if (!ModuleServices.TryGet(out IMapProjection projection) || !projection.IsActive) return;
                float scale = transform.lossyScale.x;
                if (projection.Revision == revision && scale == lastScale) return;
                revision = projection.Revision; lastScale = scale;
                SetVerticesDirty();
            }

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                RoadNetwork network = NetworkSceneSingleton<LevelInfo>.i?.roadNetwork;
                if (network?.roads == null || !ModuleServices.TryGet(out IMapProjection projection) ||
                    !projection.IsActive) return;
                float scale = Mathf.Max(.001f, Mathf.Abs(transform.lossyScale.x));
                const float minimumStepSq = MinimumStepPixels * MinimumStepPixels;
                foreach (Road road in network.roads)
                {
                    if (road?.points == null || road.points.Count < 2) continue;
                    count = 0;
                    int last = road.points.Count - 1;
                    for (int i = 0; i <= last; i++)
                    {
                        GlobalPosition at = road.points[i];
                        if (!projection.TryProject(at.x, at.z, out float x, out float y))
                        {
                            if (!Flush(vh, scale)) return;
                            continue;
                        }
                        Vector2 pixel = new Vector2(x, y) * scale;
                        if (count > 0 && i < last && (pixel - points[count - 1]).sqrMagnitude < minimumStepSq) continue;
                        if (count == points.Length) Array.Resize(ref points, count * 2);
                        points[count++] = pixel;
                    }
                    if (!Flush(vh, scale)) return;
                }
            }

            /// <summary>Draws the pending run, halo then ink; false once the vertex budget is spent.</summary>
            private bool Flush(VertexHelper vh, float scale)
            {
                int length = count;
                count = 0;
                if (length < 2) return true;
                if (vh.currentVertCount + length * 8 > VertexBudget) return false;
                MapStroke.Ribbon(vh, points, 0, length, 1f / scale, 1.7f, 1f, Halo);
                MapStroke.Ribbon(vh, points, 0, length, 1f / scale, .75f, .8f, Ink);
                return true;
            }
        }
    }
}
