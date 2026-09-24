using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>One input-transparent screen finish over the complete maximized MFD.</summary>
    internal static class MfdScreenFinish
    {
        private const string RootName = "NOAvionics.ScreenFinish";
        // The large OPS and STR workspaces use 30000 and 30001. The finish is the
        // physical display face, so it also belongs above those UI canvases.
        private const int DisplayOrder = 30002;
        private static RectTransform root;

        public static void Ensure(Canvas source)
        {
            if (source == null) return;
            Vector2 size = MfdLayout.CanvasSize(source);
            if (size.x <= 1f || size.y <= 1f) return;

            // The source map canvas can be clipped or reordered internally. A sibling
            // screen-space canvas spans the viewport just like MfdMapDeck's backdrop.
            Transform host = source.transform.parent;
            if (root != null && root.parent != host) Restore();
            bool created = false;
            if (root == null)
            {
                var go = new GameObject(RootName, typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(CanvasGroup));
                root = (RectTransform)go.transform;
                root.SetParent(host, false);
                go.layer = source.gameObject.layer;
                AvDisplayGlass.AttachFullDisplay(root);
                CanvasGroup group = go.GetComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
                created = true;
            }

            Canvas canvas = root.GetComponent<Canvas>();
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // A nested canvas can lose overrideSorting after its mode is updated.
            if (!canvas.overrideSorting) canvas.overrideSorting = true;
            if (canvas.sortingLayerID != source.sortingLayerID) canvas.sortingLayerID = source.sortingLayerID;
            int order = Mathf.Max(DisplayOrder, source.sortingOrder + 1);
            if (canvas.sortingOrder != order) canvas.sortingOrder = order;
            if (canvas.targetDisplay != source.targetDisplay) canvas.targetDisplay = source.targetDisplay;
            if (created)
            {
                CopyScaler(source.GetComponent<CanvasScaler>(), root.GetComponent<CanvasScaler>());
                AvKit.Stretch(root);
            }
        }

        private static void CopyScaler(CanvasScaler source, CanvasScaler destination)
        {
            if (source == null)
            {
                destination.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                destination.referenceResolution = new Vector2(1920f, 1080f);
                destination.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                destination.matchWidthOrHeight = 0.5f;
                return;
            }

            destination.uiScaleMode = source.uiScaleMode;
            destination.scaleFactor = source.scaleFactor;
            destination.referenceResolution = source.referenceResolution;
            destination.screenMatchMode = source.screenMatchMode;
            destination.matchWidthOrHeight = source.matchWidthOrHeight;
            destination.referencePixelsPerUnit = source.referencePixelsPerUnit;
        }

        public static void Restore()
        {
            if (root != null)
            {
                root.gameObject.SetActive(false);
                Object.Destroy(root.gameObject);
            }
            root = null;
        }
    }
}
