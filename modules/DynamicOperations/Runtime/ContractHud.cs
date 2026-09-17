using System.Collections.Generic;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>
    /// The cockpit half of the contract HUD, drawn entirely by the mod in vanilla's own
    /// likeness: one marker per accepted contract, projected through the game camera, with
    /// the vanilla pointer/dot/ring sprites, font, colours and motion read read-only from the
    /// game. A target in front of the aircraft gets a marker on its position and a label
    /// beside it; a target off screen is clamped to the frame edge and keeps its bearing, so a
    /// contract is never invisible just because the nose is pointed elsewhere. Labels glide
    /// and nudge apart, so three contracts in the same direction stay readable. Nothing is fed
    /// into, borrowed from, or written to the vanilla objective marker or overlay objects.
    /// </summary>
    internal sealed class ContractHud : MonoBehaviour, ISceneService
    {
        private const int MaxMarkers = OperationBoard.MaximumCards;
        private const float ContentSeconds = 0.5f;
        private const float ServerRefreshSeconds = 2f;
        private const float EdgeMarginX = 170f;
        private const float EdgeMarginY = 130f;

        private OperationsManager manager;
        private GameObject root;
        private RectTransform canvasRect;
        private ContractMarker[] markers;
        private readonly ContractCard[] cards = new ContractCard[MaxMarkers];
        private int cardCount;
        private float nextContent, nextServer, textSize;
        private bool styleWarned;

        internal void Configure(OperationsManager owner)
        {
            manager = owner;
            for (int i = 0; i < cards.Length; i++) cards[i] = default;
        }

        public void ResetForScene()
        {
            VanillaHudStyle.Invalidate();
            if (root != null) Destroy(root);
            root = null; canvasRect = null; markers = null; cardCount = 0;
            nextContent = 0f; nextServer = 0f; textSize = 0f;
            for (int i = 0; i < cards.Length; i++) cards[i] = default;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (manager == null) { Hide(); return; }
            if (DynamicMap.mapMaximized || !GameManager.GetLocalAircraft(out Aircraft aircraft) ||
                aircraft == null || aircraft.disabled || aircraft.HasEjected())
            { Hide(); return; }

            if (Time.unscaledTime >= nextContent)
            {
                nextContent = Time.unscaledTime + ContentSeconds;
                Pull();
            }
            if (cardCount == 0) { Hide(); return; }
            if (Time.unscaledTime >= nextServer)
            {
                nextServer = Time.unscaledTime + ServerRefreshSeconds;
                manager.Refresh();
            }

            Camera camera = SceneSingleton<CameraStateManager>.i != null
                ? SceneSingleton<CameraStateManager>.i.mainCamera
                : Camera.main;
            if (camera == null) { Hide(); return; }
            if (root == null) Build();
            if (root == null) return;

            Vector3 self = aircraft.transform.position.ToGlobalPosition().AsVector3();
            Render(camera, self);
        }

        private void Pull()
        {
            IReadOnlyList<SecondaryObjectiveView> views = manager.Objectives;
            cardCount = 0;
            if (views != null)
                for (int i = 0; i < views.Count && cardCount < MaxMarkers; i++)
                    if (ContractCard.TryRead(views[i], out ContractCard card)) cards[cardCount++] = card;
        }

        private void Render(Camera camera, Vector3 self)
        {
            float canvasHeight = canvasRect.rect.height;
            float halfWidth = canvasRect.rect.width * 0.5f - EdgeMarginX;
            float halfHeight = canvasHeight * 0.5f - EdgeMarginY;
            if (halfWidth < 1f || halfHeight < 1f) { Hide(); return; }

            float size = VanillaHudStyle.OverlayTextSize;
            if (OperationMarkerCopy.Finite(size) && size >= 1f && !Mathf.Approximately(size, textSize))
            {
                textSize = size;
                for (int i = 0; i < markers.Length; i++) markers[i].SetTextSize(size);
            }
            bool metric = VanillaHudStyle.Metric;
            VanillaHudStyle.Palette colours = VanillaHudStyle.Colours;

            int visible = 0;
            for (int i = 0; i < markers.Length; i++)
            {
                ContractMarker marker = markers[i];
                if (i >= cardCount || !cards[i].HasMarker)
                {
                    marker.SetVisible(false);
                    continue;
                }

                ContractCard card = cards[i];
                GlobalPosition target = new GlobalPosition(card.X, 0f, card.Z);
                Vector3 world = target.ToLocalPosition();
                if (!Project(camera, world, halfWidth, halfHeight, out float x, out float y, out float bearing, out bool clamped))
                {
                    marker.SetVisible(false);
                    continue;
                }

                // Vanilla measures everything from the aircraft, not the camera; in a chase or
                // external view the two differ by the camera offset. Both ends stay global so
                // the floating origin cancels, while the projection above needs the local point.
                Vector3 toTarget = target.AsVector3() - self;
                marker.SetVisible(true);
                marker.Place(x, y);
                marker.SetIcons(camera.transform.forward, toTarget, bearing);
                marker.SetRing(card.Radius, toTarget.magnitude, canvasHeight, camera, clamped);
                float distance = ContractMarkerMath.Distance(self.x, self.z, card.X, card.Z);
                marker.SetLabel(card, distance, metric, x, halfWidth, clamped, ToneColour(colours, card.Tone));
                visible++;
            }

            if (visible == 0) return;
            ResolveLabels();
        }

        /// <summary>
        /// Vanilla's label anti-overlap: every label's offset decays once a frame, a pair whose
        /// anchors sit closer than the separation pushes both labels apart along their delta,
        /// and the labels then glide a fifth of the way toward their anchor plus offset. The
        /// anchors are tested (not the drifting points), which is what vanilla does and what
        /// keeps the two labels from pumping in and out of range. Icons stay glued to their
        /// projected points — only the labels move.
        /// </summary>
        private void ResolveLabels()
        {
            for (int i = 0; i < markers.Length; i++)
                if (markers[i].Visible) markers[i].DecayNudge();
            for (int i = 0; i < markers.Length; i++)
            {
                if (!markers[i].Visible) continue;
                for (int j = i + 1; j < markers.Length; j++)
                {
                    if (!markers[j].Visible) continue;
                    Vector2 delta = markers[j].LabelAnchor - markers[i].LabelAnchor;
                    if (delta.sqrMagnitude >= ContractMarkerLook.LabelSeparation * ContractMarkerLook.LabelSeparation) continue;
                    markers[j].Nudge(delta.x, delta.y, Time.deltaTime);
                    markers[i].Nudge(-delta.x, -delta.y, Time.deltaTime);
                }
            }
            for (int i = 0; i < markers.Length; i++)
                if (markers[i].Visible) markers[i].GlideLabel();
        }

        /// <summary>Caution is vanilla's warning colour, everything located is vanilla's own green.</summary>
        private static Color ToneColour(VanillaHudStyle.Palette colours, MarkerTone tone) =>
            tone == MarkerTone.Caution ? colours.Warning : colours.AllClear;

        /// <summary>
        /// Where the marker should sit on screen, and the bearing it points along. In front of
        /// the camera the target's own point is used; behind it the camera-space direction is
        /// taken as-is (Unity already reports it in camera axes, so it needs no mirroring) and
        /// the point is kept inside the frame. The bearing is measured before the clamp, exactly
        /// like vanilla, so a marker pinned to the edge still points at its target.
        /// </summary>
        private bool Project(Camera camera, Vector3 world, float halfWidth, float halfHeight,
            out float x, out float y, out float bearing, out bool clamped)
        {
            x = 0f; y = 0f; bearing = 0f; clamped = false;
            Vector3 view = camera.WorldToScreenPoint(world);
            bool inFront = view.z > 0f;
            Vector2 local = default;
            bool projected = false;
            if (inFront)
                projected = RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, view, null, out local);
            if (projected)
            {
                bearing = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
                clamped = ContractMarkerMath.TryClampToBox(local.x, local.y, halfWidth, halfHeight,
                    out float cx, out float cy);
                x = cx;
                y = cy;
                return true;
            }

            Vector3 local3 = camera.transform.InverseTransformPoint(world);
            float angle = Mathf.Atan2(local3.y, local3.x);
            bearing = angle * Mathf.Rad2Deg;
            if (!ContractMarkerMath.TryEdgePoint(Mathf.Cos(angle), Mathf.Sin(angle), halfWidth, halfHeight,
                    out float ex, out float ey))
                return false;
            x = ex;
            y = ey;
            clamped = true;
            return true;
        }

        private void Hide()
        {
            if (markers == null) return;
            for (int i = 0; i < markers.Length; i++) markers[i].SetVisible(false);
        }

        private void Build()
        {
            if (!VanillaHudStyle.TryCockpit(out VanillaHudStyle.CockpitStyle style))
            {
                if (!styleWarned)
                {
                    styleWarned = true;
                    Plugin.Logger?.LogWarning("[Operations] Vanilla cockpit marker style unavailable; contract markers stay hidden.");
                }
                return;
            }

            root = new GameObject("Boscali Contract Markers", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1;
            canvas.pixelPerfect = false;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            canvasRect = (RectTransform)root.transform;
            markers = new ContractMarker[MaxMarkers];
            for (int i = 0; i < markers.Length; i++)
            {
                markers[i] = new ContractMarker(canvasRect, "Contract " + i, style);
                markers[i].SetVisible(false);
            }
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        }
    }
}
