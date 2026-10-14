using System.Collections.Generic;
using BoscaliSummer.Modules.DynamicOperations.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.DynamicOperations.Runtime
{
    /// <summary>Offered and accepted tasks on the native map, using host-permitted fixes
    /// and the shared relief projection. Passive annotations; native clicks remain authoritative.</summary>
    [DefaultExecutionOrder(101)] // After the map commits the displayed relief pose.
    internal sealed class ContractMapHud : MonoBehaviour, ISceneService
    {
        private const int MaxTags = OperationBoard.MaximumCards;
        private const float ContentSeconds = 0.5f;

        private OperationsManager manager;
        private GameObject root;
        private RectTransform rootRect;
        private ContractMapTag[] tags;
        private readonly ContractCard[] cards = new ContractCard[MaxTags];
        private readonly Vector2[] positions = new Vector2[MaxTags];
        private readonly bool[] shown = new bool[MaxTags];
        private int cardCount;
        private float nextContent;

        internal void Configure(OperationsManager owner)
        {
            manager = owner;
            for (int i = 0; i < cards.Length; i++) cards[i] = default;
        }

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null; rootRect = null; tags = null;
            cardCount = 0;
            nextContent = 0f;
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i] = default;
                positions[i] = Vector2.zero;
                shown[i] = false;
            }
            VanillaHudStyle.Invalidate();
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (manager == null || map == null || map.mapImage == null || !DynamicMap.mapMaximized)
            {
                Hide();
                return;
            }
            if (SceneSingleton<MapOptions>.i != null && !SceneSingleton<MapOptions>.i.showObjectives)
            {
                Hide();
                return;
            }

            if (Time.unscaledTime >= nextContent)
            {
                nextContent = Time.unscaledTime + ContentSeconds;
                Pull();
            }
            if (!manager.IsFresh)
            {
                cardCount = 0;
                Hide();
                return;
            }

            if (root == null && !Build())
            {
                Hide();
                return;
            }
            if (rootRect.parent != map.mapImage.transform)
            {
                rootRect.SetParent(map.mapImage.transform, false);
                rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
                rootRect.pivot = new Vector2(0.5f, 0.5f);
                rootRect.anchoredPosition = Vector2.zero;
                rootRect.SetAsLastSibling();
            }
            if (cardCount == 0)
            {
                Hide();
                return;
            }

            Render(map);
        }

        private void Pull()
        {
            IReadOnlyList<SecondaryObjectiveView> views = manager.Objectives;
            cardCount = 0;
            if (manager.IsFresh && views != null)
                for (int i = 0; i < views.Count && cardCount < MaxTags; i++)
                    if (ContractCard.TryRead(views[i], out ContractCard card, includeOffers: true)) cards[cardCount++] = card;
        }

        private void Render(DynamicMap map)
        {
            float factor = map.mapDisplayFactor;
            float zoom = map.mapImage.transform.localScale.x;
            if (!float.IsFinite(factor) || factor <= 0f ||
                !float.IsFinite(zoom) || zoom <= 0f)
            {
                Hide();
                return;
            }

            bool projected = ModuleServices.TryGet(out IMapProjection projection) && projection.IsActive;
            for (int i = 0; i < tags.Length; i++)
            {
                shown[i] = false;
                if (i >= cardCount || !cards[i].HasMarker)
                {
                    tags[i].SetVisible(false);
                    continue;
                }
                if (projected)
                {
                    shown[i] = projection.TryProject(cards[i].X, cards[i].Z, out float x, out float y);
                    positions[i] = new Vector2(x, y);
                    if (!shown[i]) tags[i].SetVisible(false);
                }
                else
                {
                    shown[i] = true;
                    positions[i] = new Vector2(cards[i].X * factor, cards[i].Z * factor);
                }
            }

            float screenScale = Mathf.Abs(rootRect.lossyScale.x);
            if (!float.IsFinite(screenScale) || screenScale < .001f) { Hide(); return; }
            float inverse = 1f / screenScale;
            for (int i = 0; i < tags.Length; i++)
            {
                if (!shown[i]) continue;
                ContractMapTag tag = tags[i];
                tag.SetVisible(true);
                tag.Place(positions[i].x, positions[i].y, inverse, screenScale);
                float lift = 0f;
                for (int j = 0; j < i; j++)
                    if (shown[j] && Mathf.Abs(positions[i].x-positions[j].x)*screenScale < 230f &&
                        Mathf.Abs(positions[i].y-positions[j].y)*screenScale < 60f)
                        lift += 58f;
                tag.Apply(cards[i], lift);
                tag.SetRing(cards[i], map, projected ? projection : null, positions[i], screenScale);
            }
        }

        private void Hide()
        {
            if (tags == null) return;
            for (int i = 0; i < tags.Length; i++) tags[i].SetVisible(false);
        }

        private bool Build()
        {
            root = new GameObject("Boscali Contract Map Layer", typeof(RectTransform));
            rootRect = (RectTransform)root.transform;
            rootRect.SetParent(transform, false);
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            tags = new ContractMapTag[MaxTags];
            for (int i = 0; i < tags.Length; i++)
                tags[i] = new ContractMapTag(rootRect, "Contract " + i);
            return true;
        }
    }
}
