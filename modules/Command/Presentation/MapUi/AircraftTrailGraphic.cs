using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>One input-transparent vector layer for observed aircraft history.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class AircraftTrailGraphic : MaskableGraphic
    {
        private const int MaximumTracks = 48;
        private const int Samples = 12;
        private const float SampleInterval = .45f;
        private const float TrailSeconds = 5.4f;

        private sealed class Track
        {
            internal readonly Vector3[] Points = new Vector3[Samples];
            internal readonly float[] Times = new float[Samples];
            internal Color32 Ink;
            internal int Count, Next;
            internal bool Seen;
            internal float LastSample;
        }

        private readonly Dictionary<UnitMapIcon, Track> tracks = new Dictionary<UnitMapIcon, Track>(MaximumTracks);
        private readonly List<UnitMapIcon> stale = new List<UnitMapIcon>(MaximumTracks);
        private Camera reliefCamera;
        private Vector3 sceneOrigin;
        private float now;
        private float nextRefresh;
        private int lastViewRevision = -1;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            color = Color.white;
        }

        internal void BeginSample(float time)
        {
            foreach (Track track in tracks.Values) track.Seen = false;
            now = time;
        }

        internal void Record(UnitMapIcon icon, Vector3 modelPoint, Color32 ink)
        {
            if (icon == null) return;
            if (!tracks.TryGetValue(icon, out Track track))
            {
                if (tracks.Count >= MaximumTracks) return;
                track = new Track();
                tracks.Add(icon, track);
            }
            track.Seen = true;
            track.Ink = ink;
            if (track.Count > 0 && now - track.LastSample < SampleInterval) return;
            int previous = (track.Next + Samples - 1) % Samples;
            if (track.Count > 0 && (modelPoint - track.Points[previous]).sqrMagnitude < .04f)
            {
                track.Times[previous] = now;
                track.LastSample = now;
                return;
            }
            track.Points[track.Next] = modelPoint;
            track.Times[track.Next] = now;
            track.Next = (track.Next + 1) % Samples;
            track.Count = Mathf.Min(track.Count + 1, Samples);
            track.LastSample = now;
        }

        internal void EndSample(Camera mapCamera, Vector3 origin, int viewRevision, float time)
        {
            reliefCamera = mapCamera;
            sceneOrigin = origin;
            now = time;
            stale.Clear();
            foreach (KeyValuePair<UnitMapIcon, Track> entry in tracks)
                if (entry.Key == null || !entry.Value.Seen) stale.Add(entry.Key);
            foreach (UnitMapIcon icon in stale) tracks.Remove(icon);
            if (tracks.Count > 0 || stale.Count > 0) SetVerticesDirty();
            lastViewRevision = viewRevision;
            nextRefresh = time + SampleInterval;
        }

        internal void Refresh(Camera mapCamera, Vector3 origin, int viewRevision, float time)
        {
            if (tracks.Count == 0 ||
                (viewRevision == lastViewRevision && time < nextRefresh)) return;
            reliefCamera = mapCamera;
            sceneOrigin = origin;
            now = time;
            nextRefresh = time + SampleInterval;
            lastViewRevision = viewRevision;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (reliefCamera == null || !isActiveAndEnabled) return;
            Rect rect = rectTransform.rect;
            if (rect.width < 4f || rect.height < 4f) return;

            foreach (Track track in tracks.Values)
            {
                if (track.Count < 2) continue;
                int oldest = (track.Next + Samples - track.Count) % Samples;
                for (int n = 1; n < track.Count; n++)
                {
                    int ai = (oldest + n - 1) % Samples;
                    int bi = (oldest + n) % Samples;
                    float age = now - track.Times[bi];
                    if (age > TrailSeconds) continue;
                    Vector3 a = reliefCamera.WorldToViewportPoint(sceneOrigin + track.Points[ai]);
                    Vector3 b = reliefCamera.WorldToViewportPoint(sceneOrigin + track.Points[bi]);
                    if (a.z <= 0f || b.z <= 0f) continue;
                    Vector2 from = new Vector2(rect.xMin + a.x * rect.width, rect.yMin + a.y * rect.height);
                    Vector2 to = new Vector2(rect.xMin + b.x * rect.width, rect.yMin + b.y * rect.height);
                    float fade = Mathf.Clamp01(1f - age / TrailSeconds);
                    Color32 halo = track.Ink;
                    halo.a = (byte)(28f * fade * track.Ink.a / 255f);
                    Color32 core = track.Ink;
                    core.a = (byte)(150f * fade * track.Ink.a / 255f);
                    AddStroke(mesh, from, to, 5f, halo);
                    AddStroke(mesh, from, to, 1.5f, core);
                }
            }
        }

        private static void AddStroke(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color32 ink)
        {
            Vector2 delta = b - a;
            if (delta.sqrMagnitude < .01f) return;
            Vector2 side = new Vector2(-delta.y, delta.x).normalized * width * .5f;
            AddQuad(mesh, a - side, b + side, a + side, b - side, ink);
        }

        private static void AddQuad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            Color32 ink)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(a, ink, Vector2.zero);
            mesh.AddVert(c, ink, Vector2.up);
            mesh.AddVert(b, ink, Vector2.one);
            mesh.AddVert(d, ink, Vector2.right);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
