using System.Collections.Generic;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>
    /// The map half of the contract HUD: contract markers drawn by the mod onto the maximized
    /// tactical map, parented to the map image so they pan and zoom with it. Plates keep a
    /// constant screen size against the map's zoom, the area ring scales with the map, and the
    /// whole layer hides when the map's own objective layer is switched off. Vanilla's marker
    /// and overlay objects are not involved and are not modified.
    /// </summary>
    internal sealed class ContractMapHud : MonoBehaviour, ISceneService
    {
        private const int MaxPlates = OperationBoard.MaximumCards;
        private const float ContentSeconds = 0.5f;
        private const float ServerRefreshSeconds = 2f;

        private OperationsManager manager;
        private GameObject root;
        private RectTransform rootRect;
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
            root = null; rootRect = null; plates = null; cardCount = 0;
            nextContent = 0f; nextServer = 0f;
            for (int i = 0; i < cards.Length; i++) cards[i] = default;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (manager == null || map == null || map.mapImage == null || !DynamicMap.mapMaximized)
            { Hide(); return; }
            if (SceneSingleton<MapOptions>.i != null && !SceneSingleton<MapOptions>.i.showObjectives)
            { Hide(); return; }

            if (Time.unscaledTime >= nextContent)
            {
                nextContent = Time.unscaledTime + ContentSeconds;
                Pull();
            }
            if (Time.unscaledTime >= nextServer)
            {
                nextServer = Time.unscaledTime + ServerRefreshSeconds;
                manager.Refresh();
            }

            if (root == null) Build();
            if (rootRect.parent != map.mapImage.transform)
            {
                rootRect.SetParent(map.mapImage.transform, false);
                rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
                rootRect.pivot = new Vector2(0.5f, 0.5f);
                rootRect.anchoredPosition = Vector2.zero;
                rootRect.SetAsLastSibling();
            }
            if (cardCount == 0) { Hide(); return; }

            Render(map);
        }

        private void Pull()
        {
            IReadOnlyList<SecondaryObjectiveView> views = manager.Objectives;
            cardCount = 0;
            if (views != null)
                for (int i = 0; i < views.Count && cardCount < MaxPlates; i++)
                    if (ContractCard.TryRead(views[i], out ContractCard card)) cards[cardCount++] = card;
        }

        private void Render(DynamicMap map)
        {
            float factor = map.mapDisplayFactor;
            float zoom = map.mapImage.transform.localScale.x;
            float inverse = OperationMarkerCopy.Finite(zoom) && zoom > 0f ? 1f / zoom : 1f;
            float selfX = float.NaN, selfZ = float.NaN;
            if (GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null)
            {
                Vector3 self = aircraft.transform.position.ToGlobalPosition().AsVector3();
                selfX = self.x;
                selfZ = self.z;
            }

            for (int i = 0; i < plates.Length; i++)
            {
                ContractPlate plate = plates[i];
                if (i >= cardCount || !cards[i].HasMarker || !OperationMarkerCopy.Finite(factor) || factor <= 0f)
                {
                    plate.SetVisible(false);
                    continue;
                }
                ContractCard card = cards[i];
                plate.SetVisible(true);
                plate.SetScreenScale(inverse);
                plate.SetPosition(card.X * factor, card.Z * factor);
                plate.SetBearing(0f);
                plate.SetPointerActive(false);
                plate.Refresh(card, ContractMarkerMath.Distance(selfX, selfZ, card.X, card.Z), card.Tone);
                float ring = OperationMarkerCopy.Finite(card.Radius) && card.Radius > 0f ? card.Radius * factor : 0f;
                plate.SetRing(ring > 3f, ring);
            }
        }

        private void Hide()
        {
            if (root == null) return;
            for (int i = 0; i < plates.Length; i++) plates[i].SetVisible(false);
        }

        private void Build()
        {
            root = new GameObject("Boscali Contract Map Layer", typeof(RectTransform));
            rootRect = (RectTransform)root.transform;
            rootRect.SetParent(transform, false);
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            plates = new ContractPlate[MaxPlates];
            for (int i = 0; i < plates.Length; i++)
            {
                plates[i] = new ContractPlate(rootRect, "Contract " + i);
                plates[i].SetVisible(false);
            }
        }
    }
}
