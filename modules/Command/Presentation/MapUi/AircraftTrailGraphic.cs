using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// One input-transparent vector layer for observed aircraft history. The newest stroke is glued to
    /// the icon's own transform every frame, so the trail never lags or detaches from the plane; the
    /// older samples are projected once per view change and reused.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class AircraftTrailGraphic : MaskableGraphic
    {
        private const int MaximumTracks = 48;
        private const int Samples = 12;
        private const float SampleInterval = .45f;
        private const float TrailSeconds = 5.4f;
        // Model units per second; the relief is 900 units per 81.92 km, so this is about 3.6 km/s.
        private const float MaximumModelSpeed = 40f;
        // The head redraws only when an icon moved this far (squared pixels).
        private const float HeadMoveSqr = .36f;

        private sealed class Track
        {
            internal readonly Vector3[] Points = new Vector3[Samples];
            internal readonly float[] Times = new float[Samples];
            internal readonly Vector2[] Screen = new Vector2[Samples];
            internal readonly int[] Projected = new int[Samples];
            internal readonly bool[] Visible = new bool[Samples];
            internal Color32 Ink;
            internal int Count, Next;
            internal bool Seen, HasHead;
            internal float LastSample;
            internal Vector2 Head;
        }

        private readonly Dictionary<UnitMapIcon, Track> tracks = new Dictionary<UnitMapIcon, Track>(MaximumTracks);
        private readonly List<UnitMapIcon> stale = new List<UnitMapIcon>(MaximumTracks);
        private Camera reliefCamera;
        private Vector3 sceneOrigin;
        private float now;
        private float nextRefresh;
        private int lastViewRevision = -1;
        // Bumped whenever cached screen points stop matching the view; 0 marks "never projected".
        private int projection = 1;
        private int projectedView = -1;
        private Rect projectedRect;
        private Vector3 projectedOrigin;

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
            // A spawn or respawn moves the icon across the map in one step: that is not a flown path.
            if (track.Count > 0 && (modelPoint - track.Points[previous]).magnitude >
                MaximumModelSpeed * (now - track.LastSample)) track.Count = 0;
            if (track.Count > 0 && (modelPoint - track.Points[previous]).sqrMagnitude < .04f)
            {
                track.Times[previous] = now;
                track.LastSample = now;
                return;
            }
            track.Points[track.Next] = modelPoint;
            track.Times[track.Next] = now;
            track.Projected[track.Next] = 0;
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

        /// <summary>Every frame: pin each trail's head to where its icon is drawn right now.</summary>
        internal void Follow()
        {
            bool moved = false;
            foreach (KeyValuePair<UnitMapIcon, Track> entry in tracks)
            {
                Track track = entry.Value;
                Image image = entry.Key != null ? entry.Key.iconImage : null;
                if (image == null || !image.isActiveAndEnabled)
                {
                    moved |= track.HasHead;
                    track.HasHead = false;
                    continue;
                }
                Vector2 head = rectTransform.InverseTransformPoint(image.transform.position);
                if (track.HasHead && (head - track.Head).sqrMagnitude < HeadMoveSqr) continue;
                track.Head = head;
                track.HasHead = true;
                moved = true;
            }
            if (moved) SetVerticesDirty();
        }

        private bool Project(Track track, int index, Rect rect, out Vector2 point)
        {
            if (track.Projected[index] != projection)
            {
                Vector3 view = reliefCamera.WorldToViewportPoint(sceneOrigin + track.Points[index]);
                track.Visible[index] = view.z > 0f;
                track.Screen[index] = new Vector2(rect.xMin + view.x * rect.width, rect.yMin + view.y * rect.height);
                track.Projected[index] = projection;
            }
            point = track.Screen[index];
            return track.Visible[index];
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (reliefCamera == null || !isActiveAndEnabled) return;
            Rect rect = rectTransform.rect;
            if (rect.width < 4f || rect.height < 4f) return;
            if (projectedView != lastViewRevision || projectedRect != rect || projectedOrigin != sceneOrigin)
            {
                projection = projection == int.MaxValue ? 1 : projection + 1;
                projectedView = lastViewRevision;
                projectedRect = rect;
                projectedOrigin = sceneOrigin;
            }
            float maximumHead = Mathf.Max(rect.width, rect.height) * .25f;

            foreach (Track track in tracks.Values)
            {
                if (track.Count < 1) continue;
                int oldest = (track.Next + Samples - track.Count) % Samples;
                Color32 core = track.Ink;
                for (int n = 1; n < track.Count; n++)
                {
                    int ai = (oldest + n - 1) % Samples;
                    int bi = (oldest + n) % Samples;
                    float age = now - track.Times[bi];
                    if (age > TrailSeconds) continue;
                    if (!Project(track, ai, rect, out Vector2 from) || !Project(track, bi, rect, out Vector2 to)) continue;
                    core.a = (byte)(165f * Mathf.Clamp01(1f - age / TrailSeconds) * track.Ink.a / 255f);
                    // One stroke: the wide feather is the halo, and it keeps the layer in budget.
                    MapStroke.Segment(mesh, from, to, 1.4f, 2.4f, core);
                }
                // The live stroke from the last sample to the icon itself; a respawn jump is not drawn.
                int newest = (track.Next + Samples - 1) % Samples;
                if (!track.HasHead || !Project(track, newest, rect, out Vector2 tail) ||
                    (track.Head - tail).sqrMagnitude > maximumHead * maximumHead) continue;
                core.a = (byte)(165f * track.Ink.a / 255f);
                MapStroke.Segment(mesh, tail, track.Head, 1.4f, 2.4f, core);
            }
        }
    }
}
