using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal sealed class MapUiManager : MonoBehaviour, ISceneService
    {
        private float nextRefresh;
        private int screenCount = -1;
        private Vector2 canvasSize;
        private void LateUpdate()
        {
            if (!DynamicMap.mapMaximized || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.1f;
            var map = SceneSingleton<DynamicMap>.i;
            var canvas = map == null ? null : map.maximizedMapCanvas;
            var mfd = canvas == null ? null : canvas.GetComponentInChildren<VirtualMFD>(true);
            if (mfd == null) return;
            int count = Count(MapUiAccess.GetLeftScreens(mfd)) + Count(MapUiAccess.GetRightScreens(mfd));
            var size = ((RectTransform)canvas.transform).rect.size;
            if (count != screenCount || size != canvasSize)
            {
                MfdRailPatch.Refresh(map);
                screenCount = count;
                canvasSize = size;
            }
            MfdRailPatch.Reconcile();
            VanillaMfdRebuild.Tick();
            MfdLogPanel.Tick();
        }
        private static int Count(System.Collections.Generic.List<MFDScreen> screens)
        {
            int count = 0;
            if (screens != null) foreach (var screen in screens) if (screen != null) count++;
            return count;
        }
        public void ResetForScene()
        {
            MfdRailPatch.Reset();
            nextRefresh = 0f;
            screenCount = -1;
        }
        private void OnDestroy() => ResetForScene();
    }
}
