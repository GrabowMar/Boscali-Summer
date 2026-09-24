using System.Collections.Generic;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// The ordered instrument strip below the maximised map.
    ///
    /// <para>Vanilla draws the clock/speed/altitude/attitude group above the map and the
    /// airport, spectator controls, or unit telemetry on a separate lower canvas. The layout
    /// reserves a bottom strip for those controls; this class temporarily hosts the native
    /// objects side by side on wide screens and in two rows where width is scarce.</para>
    ///
    /// <para>The native objects remain authoritative. Their scripts keep updating their text,
    /// active state and button actions; only their parent and RectTransform presentation are
    /// snapshotted. Minimise restores every value and sibling index exactly.</para>
    /// </summary>
    internal static class MfdMapFooter
    {
        private const string FooterName = "NOAvionics.MapFooter";
        private const string ChromeName = "Chrome";
        private const string InstrumentsSlotName = "InstrumentsSlot";
        private const string ContextSlotName = "ContextSlot";
        private const float FooterInset = 6f;
        private const float RowGap = 4f;

        private sealed class RectSnapshot
        {
            public RectTransform Target;
            public Transform Parent;
            public int SiblingIndex;
            public Vector2 AnchorMin;
            public Vector2 AnchorMax;
            public Vector2 Pivot;
            public Vector2 SizeDelta;
            public Vector2 AnchoredPosition;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;

            private readonly List<ImageState> suppressedImages = new List<ImageState>();
            private CanvasGroup addedCanvasGroup;
            private CanvasGroup originalCanvasGroup;
            private float originalAlpha;
            private bool originalBlocksRaycasts;
            private bool imagesSuppressed;

            private struct ImageState
            {
                public Image Image;
                public bool WasEnabled;
            }

            public static RectSnapshot Capture(RectTransform target)
            {
                if (target == null) return null;
                return new RectSnapshot
                {
                    Target = target,
                    Parent = target.parent,
                    SiblingIndex = target.GetSiblingIndex(),
                    AnchorMin = target.anchorMin,
                    AnchorMax = target.anchorMax,
                    Pivot = target.pivot,
                    SizeDelta = target.sizeDelta,
                    AnchoredPosition = target.anchoredPosition,
                    LocalPosition = target.localPosition,
                    LocalRotation = target.localRotation,
                    LocalScale = target.localScale,
                };
            }

            public CanvasGroup EnsureCanvasGroup()
            {
                if (Target == null) return null;
                if (addedCanvasGroup != null) return addedCanvasGroup;
                if (originalCanvasGroup != null) return originalCanvasGroup;
                var cg = Target.GetComponent<CanvasGroup>();
                if (cg == null)
                {
                    cg = Target.gameObject.AddComponent<CanvasGroup>();
                    addedCanvasGroup = cg;
                }
                else
                {
                    originalCanvasGroup = cg;
                    originalAlpha = cg.alpha;
                    originalBlocksRaycasts = cg.blocksRaycasts;
                }
                return cg;
            }

            public void SuppressBackgroundImages()
            {
                if (Target == null || imagesSuppressed) return;
                imagesSuppressed = true;
                Image[] images = Target.GetComponentsInChildren<Image>(includeInactive: true);
                for (int i = 0; i < images.Length; i++)
                {
                    Image img = images[i];
                    if (img == null || !img.enabled) continue;

                    // Preserve interactive button visuals (e.g. Select Aircraft button)
                    if (img.GetComponent<Button>() != null || img.GetComponentInParent<Button>() != null)
                        continue;

                    bool alreadyTracked = false;
                    for (int j = 0; j < suppressedImages.Count; j++)
                    {
                        if (suppressedImages[j].Image == img)
                        {
                            alreadyTracked = true;
                            break;
                        }
                    }

                    if (!alreadyTracked)
                    {
                        suppressedImages.Add(new ImageState { Image = img, WasEnabled = img.enabled });
                    }
                    img.enabled = false;
                }
            }

            public void Restore()
            {
                if (originalCanvasGroup != null)
                {
                    originalCanvasGroup.alpha = originalAlpha;
                    originalCanvasGroup.blocksRaycasts = originalBlocksRaycasts;
                    originalCanvasGroup = null;
                }
                if (addedCanvasGroup != null)
                {
                    addedCanvasGroup.alpha = 1f;
                    addedCanvasGroup.blocksRaycasts = true;
                    Object.Destroy(addedCanvasGroup);
                    addedCanvasGroup = null;
                }

                for (int i = 0; i < suppressedImages.Count; i++)
                {
                    if (suppressedImages[i].Image != null)
                        suppressedImages[i].Image.enabled = suppressedImages[i].WasEnabled;
                }
                suppressedImages.Clear();

                if (Target == null || Parent == null) return;

                Target.SetParent(Parent, worldPositionStays: false);
                Target.SetSiblingIndex(Mathf.Clamp(SiblingIndex, 0, Parent.childCount - 1));
                Target.anchorMin = AnchorMin;
                Target.anchorMax = AnchorMax;
                Target.pivot = Pivot;
                Target.sizeDelta = SizeDelta;
                Target.anchoredPosition = AnchoredPosition;
                Target.localPosition = LocalPosition;
                Target.localRotation = LocalRotation;
                Target.localScale = LocalScale;
            }
        }

        private static RectTransform footer;
        private static RectTransform chrome;
        private static RectTransform instrumentsSlot;
        private static RectTransform contextSlot;
        private static Vector2 chromeSize;

        private static RectSnapshot instruments;
        private static RectSnapshot airbase;
        private static RectSnapshot spectator;
        private static RectSnapshot unitDebug;
        private static float nextUnitProbe;

        public static void Ensure(Canvas canvas, MfdLayout.Columns columns, VirtualMFD mfd)
        {
            if (canvas == null || mfd == null || !MapUiAccess.MfdFooterAvailable) return;

            EnsureRoot(canvas);
            if (footer == null) return;

            Rect footerArea = ResolveArea(columns);
            if (footerArea.width <= 1f || footerArea.height <= 1f) return;

            PlaceFooter(footerArea);
            BuildChrome(footerArea.size);
            PlaceSlots(footerArea.size);
            AdoptNativeSurfaces(mfd);

            // This panel contains live native buttons, so it belongs above the passive deck,
            // map tray and map viewport. Its rectangle is entirely inside BottomReserve and
            // cannot intercept map gestures.
            footer.SetAsLastSibling();
        }

        public static void Tick()
        {
            if (footer == null) return;

            // Late adoption if UnitDebug was instantiated or activated after initial layout
            if ((unitDebug == null || unitDebug.Target == null) && Time.unscaledTime >= nextUnitProbe)
            {
                nextUnitProbe = Time.unscaledTime + 1f;
                UnitDebug ud = Object.FindObjectOfType<UnitDebug>(true);
                if (ud != null && ud.transform is RectTransform rt && contextSlot != null)
                {
                    Adopt(ref unitDebug, rt, contextSlot,
                        new Vector2(contextSlot.rect.width, Mathf.Min(48f, contextSlot.rect.height)));
                    unitDebug?.SuppressBackgroundImages();
                }
            }

            bool airbaseActive = airbase != null && airbase.Target != null && airbase.Target.gameObject.activeInHierarchy;
            bool spectatorActive = spectator != null && spectator.Target != null && spectator.Target.gameObject.activeInHierarchy;

            if (unitDebug != null && unitDebug.Target != null)
            {
                CanvasGroup cg = unitDebug.EnsureCanvasGroup();
                if (cg != null)
                {
                    // Prioritize airbase and spectator contextual menus over ambient unit telemetry
                    bool shouldShow = !airbaseActive && !spectatorActive;
                    float targetAlpha = shouldShow ? 1f : 0f;
                    if (!Mathf.Approximately(cg.alpha, targetAlpha))
                    {
                        cg.alpha = targetAlpha;
                        cg.blocksRaycasts = shouldShow;
                    }
                }

                unitDebug.SuppressBackgroundImages();
            }

            if (airbaseActive)
            {
                airbase.SuppressBackgroundImages();
            }

            if (spectatorActive)
            {
                spectator.SuppressBackgroundImages();
            }
        }

        public static void Restore()
        {
            // Restore children before destroying their temporary slots.
            unitDebug?.Restore();
            spectator?.Restore();
            airbase?.Restore();
            instruments?.Restore();

            unitDebug = null;
            spectator = null;
            airbase = null;
            instruments = null;

            if (footer != null) Object.Destroy(footer.gameObject);
            footer = null;
            chrome = null;
            instrumentsSlot = null;
            contextSlot = null;
            chromeSize = Vector2.zero;
            nextUnitProbe = 0f;
        }

        public static void Reset() => Restore();

        private static void EnsureRoot(Canvas canvas)
        {
            if (footer != null && footer.parent != canvas.transform)
            {
                Restore();
            }

            if (footer == null)
            {
                Transform existing = canvas.transform.Find(FooterName);
                footer = existing as RectTransform;
            }

            if (footer == null)
            {
                var go = new GameObject(FooterName, typeof(RectTransform), typeof(Image));
                footer = go.GetComponent<RectTransform>();
                footer.SetParent(canvas.transform, worldPositionStays: false);
            }

            Image background = footer.GetComponent<Image>();
            if (background == null) background = footer.gameObject.AddComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = false;

            chrome = FindOrCreateLayer(footer, ChromeName);
            instrumentsSlot = FindOrCreateLayer(footer, InstrumentsSlotName);
            contextSlot = FindOrCreateLayer(footer, ContextSlotName);
        }

        private static Rect ResolveArea(MfdLayout.Columns columns)
        {
            float top = columns.Map.y - columns.Map.height - MfdLayout.Gutter;
            float bottom = -columns.Canvas.y * 0.5f + MfdLayout.Margin;
            return new Rect(columns.Map.x, top, columns.Map.width, Mathf.Max(0f, top - bottom));
        }

        private static void PlaceFooter(Rect area)
        {
            footer.anchorMin = footer.anchorMax = new Vector2(0.5f, 0.5f);
            footer.pivot = new Vector2(0f, 1f);
            footer.sizeDelta = area.size;
            footer.anchoredPosition = area.position;
            footer.localScale = Vector3.one;
        }

        private static void BuildChrome(Vector2 size)
        {
            if (Approximately(chromeSize, size) && chrome.childCount > 0) return;

            for (int i = chrome.childCount - 1; i >= 0; i--)
                Object.Destroy(chrome.GetChild(i).gameObject);

            chromeSize = size;
            var area = new Rect(0f, 0f, size.x, size.y);
            AvKit.Outline(chrome, area, AvTheme.Hairline);
            AvKit.Rule(chrome, new Rect(1f, 0f, size.x - 2f, 2f), AvTheme.Accent.WithAlpha(0.30f));
            // A small segmented uplink trace ties the footer to the wire and index rail.
            if (size.x >= 300f)
                for (int i = 0; i < 7; i++)
                    AvKit.Rule(chrome, new Rect(size.x - 59f + i * 7f, -1f, 4f, 2f),
                        i >= 5 ? AvTheme.Accent.WithAlpha(.8f) : AvTheme.Frame.WithAlpha(.75f));

            if (size.y < 90f)
            {
                float dividerX = FooterInset + (size.x - FooterInset * 2f - RowGap) * 0.50f + RowGap * 0.5f;
                AvKit.Rule(chrome, new Rect(dividerX, -FooterInset, 1f, size.y - FooterInset * 2f),
                    AvTheme.Frame.WithAlpha(0.55f));
            }
            else
            {
                float innerHeight = Mathf.Max(0f, size.y - FooterInset * 2f);
                float contextHeight = Mathf.Min(48f, innerHeight * 0.52f);
                float dividerY = -(FooterInset + Mathf.Max(0f, innerHeight - contextHeight) + RowGap * 0.5f);
                AvKit.Rule(chrome,
                    new Rect(FooterInset, dividerY, Mathf.Max(0f, size.x - FooterInset * 2f), 1f),
                    AvTheme.Frame.WithAlpha(0.55f));
            }
        }

        private static void PlaceSlots(Vector2 size)
        {
            float innerWidth = Mathf.Max(0f, size.x - FooterInset * 2f);
            float innerHeight = Mathf.Max(0f, size.y - FooterInset * 2f);
            if (size.y < 90f)
            {
                float instrumentsWidth = (innerWidth - RowGap) * 0.50f;
                AvKit.Place(instrumentsSlot,
                    new Rect(FooterInset, -FooterInset, instrumentsWidth, innerHeight));
                AvKit.Place(contextSlot,
                    new Rect(FooterInset + instrumentsWidth + RowGap, -FooterInset,
                        innerWidth - instrumentsWidth - RowGap, innerHeight));
            }
            else
            {
                float contextHeight = Mathf.Min(48f, innerHeight * 0.52f);
                float instrumentsHeight = Mathf.Max(0f, innerHeight - contextHeight - RowGap);
                AvKit.Place(instrumentsSlot,
                    new Rect(FooterInset, -FooterInset, innerWidth, instrumentsHeight));
                AvKit.Place(contextSlot,
                    new Rect(FooterInset, -(FooterInset + instrumentsHeight + RowGap), innerWidth, contextHeight));
            }

            EnsureMask(instrumentsSlot);
            EnsureMask(contextSlot);
        }

        private static void EnsureMask(RectTransform slot)
        {
            if (slot == null) return;
            if (slot.GetComponent<RectMask2D>() == null)
                slot.gameObject.AddComponent<RectMask2D>();
        }

        private static void AdoptNativeSurfaces(VirtualMFD mfd)
        {
            RectTransform top = MapUiAccess.GetMfdTopInstruments(mfd);
            GameplayUI gameplay = Object.FindObjectOfType<GameplayUI>(true) ?? Object.FindObjectOfType<GameplayUI>();
            RectTransform airbasePanel = MapUiAccess.GetSelectAirbasePanel(gameplay)?.transform as RectTransform;
            RectTransform spectatorPanel = MapUiAccess.GetSpectatorPanel(gameplay)?.transform as RectTransform;
            UnitDebug unitDebugComponent = Object.FindObjectOfType<UnitDebug>(true);
            RectTransform unitDebugPanel = unitDebugComponent != null ? unitDebugComponent.transform as RectTransform : null;

            Adopt(ref instruments, top, instrumentsSlot,
                new Vector2(1000f, Mathf.Min(60f, instrumentsSlot.rect.height)));
            Adopt(ref airbase, airbasePanel, contextSlot, null);
            Adopt(ref spectator, spectatorPanel, contextSlot, null);
            Adopt(ref unitDebug, unitDebugPanel, contextSlot, new Vector2(contextSlot != null ? contextSlot.rect.width : 900f,
                contextSlot != null ? Mathf.Min(48f, contextSlot.rect.height) : 48f));

            airbase?.SuppressBackgroundImages();
            spectator?.SuppressBackgroundImages();
            unitDebug?.SuppressBackgroundImages();
        }

        private static void Adopt(
            ref RectSnapshot snapshot,
            RectTransform target,
            RectTransform slot,
            Vector2? hostedSize)
        {
            if (target == null || slot == null) return;

            if (snapshot == null || snapshot.Target != target)
                snapshot = RectSnapshot.Capture(target);

            if (target.parent != slot) target.SetParent(slot, worldPositionStays: false);
            target.anchorMin = target.anchorMax = target.pivot = new Vector2(0.5f, 0.5f);
            target.anchoredPosition = Vector2.zero;
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;

            float maxWidth = Mathf.Max(0f, slot.rect.width);
            if (hostedSize.HasValue)
            {
                Vector2 requested = hostedSize.Value;
                target.sizeDelta = new Vector2(
                    Mathf.Min(requested.x, maxWidth),
                    requested.y);
            }
            else
            {
                target.sizeDelta = new Vector2(
                    Mathf.Min(target.sizeDelta.x, maxWidth),
                    target.sizeDelta.y);
            }
        }

        private static RectTransform FindOrCreateLayer(RectTransform parent, string name)
        {
            RectTransform layer = parent.Find(name) as RectTransform;
            if (layer == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                layer = go.GetComponent<RectTransform>();
                layer.SetParent(parent, worldPositionStays: false);
            }

            AvKit.Stretch(layer);
            return layer;
        }

        private static bool Approximately(Vector2 a, Vector2 b) =>
            Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y);
    }
}
