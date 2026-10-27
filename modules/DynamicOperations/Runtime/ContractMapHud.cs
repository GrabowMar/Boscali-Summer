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
        private readonly float[] lifts = new float[MaxTags];
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

        // LateUpdate: the relief commits its pose in MapUiManager.LateUpdate (order 100);
        // projecting in Update reads last frame's pose and the tags trail a pan.
        private void LateUpdate()
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
                float lift = LabelLift(i, screenScale);
                tag.Apply(cards[i], lift);
                tag.SetRing(cards[i], map, projected ? projection : null, positions[i], screenScale);
            }
        }

        /// <summary>
        /// Lifts tag <paramref name="i"/>'s label clear of every label placed before it. The boxes are
        /// the labels' real screen rectangles (lift included), so a lifted label never lands on a third.
        /// </summary>
        private float LabelLift(int i, float screenScale)
        {
            float x = positions[i].x * screenScale, y = positions[i].y * screenScale;
            float height = LabelHeight(i), lift = 0f;
            for (int pass = 0; pass < MaxTags; pass++)
            {
                bool moved = false;
                for (int j = 0; j < i; j++)
                {
                    if (!shown[j]) continue;
                    float jx = positions[j].x * screenScale, jTop = positions[j].y * screenScale + LabelTop + lifts[j];
                    float top = y + LabelTop + lift;
                    bool overlapX = Mathf.Abs(x - jx) < LabelWidth;
                    bool overlapY = top > jTop - LabelHeight(j) - LabelGap && top - height < jTop + LabelGap;
                    if (!overlapX || !overlapY) continue;
                    lift = jTop + height + LabelGap - (y + LabelTop);
                    moved = true;
                }
                if (!moved) break;
            }
            lifts[i] = lift;
            return lift;
        }

        // Matches ContractMapTag: label top-left at (12, 11 + lift), 200 px wide, one line per row.
        private const float LabelTop = 11f, LabelWidth = 200f, LabelGap = 2f;
        private float LabelHeight(int i) => cards[i].IsOffered ? 14f : 28f;

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
