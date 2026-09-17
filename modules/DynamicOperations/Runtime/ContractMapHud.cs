using System.Collections.Generic;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>
    /// The map half of the contract HUD: accepted contracts drawn as vanilla-style objective
    /// markers on the maximized tactical map, parented to the map image so they pan and zoom
    /// with it. Every visual fact is read from the game's own marker prefab, so the tags look
    /// like the objectives beside them. Vanilla's marker and overlay objects are not involved
    /// and are not modified.
    /// </summary>
    internal sealed class ContractMapHud : MonoBehaviour, ISceneService
    {
        private const int MaxTags = OperationBoard.MaximumCards;
        private const float ContentSeconds = 0.5f;
        private const float ServerRefreshSeconds = 2f;
        private const float MinimumRingPixels = 24f;
        private const float RingAlpha = 0.35f;

        private OperationsManager manager;
        private GameObject root;
        private RectTransform rootRect;
        private ContractMapTag[] tags;
        private VanillaHudStyle.MapStyle style;
        private Sprite ringSprite;
        private bool warned;
        private readonly ContractCard[] cards = new ContractCard[MaxTags];
        private readonly Vector2[] positions = new Vector2[MaxTags];
        private readonly bool[] shown = new bool[MaxTags];
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
            root = null; rootRect = null; tags = null; ringSprite = null; style = default;
            cardCount = 0; warned = false;
            nextContent = 0f; nextServer = 0f;
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
            if (Time.unscaledTime >= nextServer)
            {
                nextServer = Time.unscaledTime + ServerRefreshSeconds;
                manager.Refresh();
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
            if (views != null)
                for (int i = 0; i < views.Count && cardCount < MaxTags; i++)
                    if (ContractCard.TryRead(views[i], out ContractCard card)) cards[cardCount++] = card;
        }

        private void Render(DynamicMap map)
        {
            float factor = map.mapDisplayFactor;
            float zoom = map.mapImage.transform.localScale.x;
            if (!OperationMarkerCopy.Finite(factor) || factor <= 0f ||
                !OperationMarkerCopy.Finite(zoom) || zoom <= 0f)
            {
                Hide();
                return;
            }

            for (int i = 0; i < tags.Length; i++)
            {
                shown[i] = false;
                if (i >= cardCount || !cards[i].HasMarker)
                {
                    tags[i].SetVisible(false);
                    continue;
                }
                shown[i] = true;
                positions[i] = new Vector2(cards[i].X * factor, cards[i].Z * factor);
            }

            float inverse = 1f / zoom;
            VanillaHudStyle.Palette colours = VanillaHudStyle.Colours;
            for (int i = 0; i < tags.Length; i++)
            {
                if (!shown[i]) continue;
                ContractCard card = cards[i];

                bool caution = card.Tone == MarkerTone.Caution;
                ContractMapTag tag = tags[i];
                tag.SetVisible(true);
                tag.Place(positions[i].x, positions[i].y, inverse, zoom);
                tag.Apply(card, style, caution ? colours.Warning : style.LabelColour);

                Color colour = caution ? colours.Warning : colours.AllClear;
                float diameter = 2f * card.Radius * factor;
                tag.SetRing(card.Radius > 0f && ringSprite != null && diameter * zoom >= MinimumRingPixels,
                    diameter, new Color(colour.r, colour.g, colour.b, RingAlpha));
            }
        }

        private void Hide()
        {
            if (tags == null) return;
            for (int i = 0; i < tags.Length; i++) tags[i].SetVisible(false);
        }

        /// <summary>
        /// Build the tag pool from the game's own marker style. Fails closed: when vanilla's
        /// map style cannot be read, nothing is built and the layer stays hidden.
        /// </summary>
        private bool Build()
        {
            if (!VanillaHudStyle.TryMap(out VanillaHudStyle.MapStyle mapStyle))
            {
                if (!warned)
                {
                    warned = true;
                    Plugin.Logger?.LogWarning("[Operations] Vanilla map marker style unavailable; contract map markers stay hidden this scene.");
                }
                return false;
            }

            Sprite sprite = mapStyle.Ring;
            if (sprite == null && VanillaHudStyle.TryCockpit(out VanillaHudStyle.CockpitStyle cockpit)) sprite = cockpit.Ring;
            style = mapStyle;
            ringSprite = sprite;

            root = new GameObject("Boscali Contract Map Layer", typeof(RectTransform));
            rootRect = (RectTransform)root.transform;
            rootRect.SetParent(transform, false);
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            tags = new ContractMapTag[MaxTags];
            for (int i = 0; i < tags.Length; i++)
                tags[i] = new ContractMapTag(rootRect, "Contract " + i, style, ringSprite);
            return true;
        }
    }
}
