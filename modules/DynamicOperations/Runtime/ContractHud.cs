using System.Collections.Generic;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>
    /// The cockpit half of the contract HUD, drawn entirely by the mod: one marker per
    /// accepted contract, projected through the game camera. A target in front of the
    /// aircraft gets a pointer on its position, a two-line plate above it and, when the
    /// contract owns an area, a ring of dots sized by the same projection the vanilla area
    /// ring uses. A target off screen is clamped to the frame edge and keeps its distance, so
    /// a contract is never invisible just because the nose is pointed elsewhere. Nothing is
    /// fed into, or read from, the vanilla objective marker or overlay objects.
    /// </summary>
    internal sealed class ContractHud : MonoBehaviour, ISceneService
    {
        private const int MaxPlates = OperationBoard.MaximumCards;
        private const float ContentSeconds = 0.5f;
        private const float ServerRefreshSeconds = 2f;
        private const float EdgeMarginX = 170f;
        private const float EdgeMarginY = 130f;
        private const float MinRingPixels = 16f;
        private const float MaxRingPixels = 2600f;

        private OperationsManager manager;
        private GameObject root;
        private RectTransform canvasRect;
        private ContractPlate[] plates;
        private readonly ContractCard[] cards = new ContractCard[MaxPlates];
        private int cardCount;
        private float nextContent, nextServer;

        internal void Configure(OperationsManager owner)
        {
            manager = owner;
            for (int i = 0; i < cards.Length; i++) cards[i] = default;
        }

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null; canvasRect = null; plates = null; cardCount = 0;
            nextContent = 0f; nextServer = 0f;
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

            Vector3 self = aircraft.transform.position.ToGlobalPosition().AsVector3();
            Render(camera, self);
        }

        private void Pull()
        {
            IReadOnlyList<SecondaryObjectiveView> views = manager.Objectives;
            cardCount = 0;
            if (views != null)
                for (int i = 0; i < views.Count && cardCount < MaxPlates; i++)
                    if (ContractCard.TryRead(views[i], out ContractCard card)) cards[cardCount++] = card;
        }

        private void Render(Camera camera, Vector3 self)
        {
            float halfWidth = canvasRect.rect.width * 0.5f - EdgeMarginX;
            float halfHeight = canvasRect.rect.height * 0.5f - EdgeMarginY;
            if (halfWidth < 1f || halfHeight < 1f) { Hide(); return; }

            for (int i = 0; i < plates.Length; i++)
            {
                ContractPlate plate = plates[i];
                if (i >= cardCount || !cards[i].HasMarker)
                {
                    plate.SetVisible(false);
                    continue;
                }

                ContractCard card = cards[i];
                Vector3 world = new GlobalPosition(card.X, 0f, card.Z).ToLocalPosition();
                float distance = ContractMarkerMath.Distance(self.x, self.z, card.X, card.Z);
                float range3d = Vector3.Distance(camera.transform.position, world);
                if (!Project(camera, world, halfWidth, halfHeight, out float x, out float y, out float bearing, out bool clamped))
                {
                    plate.SetVisible(false);
                    continue;
                }

                plate.SetVisible(true);
                plate.SetPosition(x, y);
                plate.SetScreenScale(1f);
                plate.SetBearing(bearing);
                plate.SetPointerActive(true);
                plate.Refresh(card, distance, card.Tone);

                float ring = !clamped
                    ? card.Radius * ContractMarkerMath.PixelsPerMetre(canvasRect.rect.height, camera.fieldOfView, range3d)
                    : 0f;
                plate.SetRing(ring >= MinRingPixels && ring <= MaxRingPixels, ring);
            }
        }

        /// <summary>
        /// Where the marker should sit on screen. In front of the camera the target's own
        /// point is used; behind it the bearing is mirrored so the marker still points the
        /// right way. Either way the point is kept inside the frame.
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
            if (!projected)
            {
                Vector3 local3 = camera.transform.InverseTransformPoint(world);
                float angle = Mathf.Atan2(local3.y, local3.x);
                float dx = Mathf.Cos(angle);
                float dy = Mathf.Sin(angle);
                if (!inFront) { dx = -dx; dy = -dy; }
                if (!ContractMarkerMath.TryEdgePoint(dx, dy, halfWidth, halfHeight, out float ex, out float ey))
                    return false;
                x = ex;
                y = ey;
                clamped = true;
            }
            else
            {
                clamped = ContractMarkerMath.TryClampToBox(local.x, local.y, halfWidth, halfHeight,
                    out float cx, out float cy);
                x = cx;
                y = cy;
            }

            bearing = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            return true;
        }

        private void Hide()
        {
            if (root == null) return;
            for (int i = 0; i < plates.Length; i++) plates[i].SetVisible(false);
        }

        private void Build()
        {
            root = new GameObject("Boscali Contract Markers", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4;
            canvas.pixelPerfect = true;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            canvasRect = (RectTransform)root.transform;
            plates = new ContractPlate[MaxPlates];
            for (int i = 0; i < plates.Length; i++)
            {
                plates[i] = new ContractPlate(canvasRect, "Contract " + i);
                plates[i].SetVisible(false);
            }
        }
    }
}
