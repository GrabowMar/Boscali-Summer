using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal sealed class MapUiManager : MonoBehaviour, ISceneService
    {
        private float nextRefresh;
        private int screenCount = -1;
        private void LateUpdate()
        {
            if (!DynamicMap.mapMaximized) return;
            MfdNewsTicker.Tick();
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.1f;
            var map = SceneSingleton<DynamicMap>.i;
            var canvas = map == null ? null : map.maximizedMapCanvas;
            var mfd = MapMfdLookup.Resolve(canvas);
            if (mfd == null || canvas == null) return;
            int count = Count(MapUiAccess.GetLeftScreens(mfd)) + Count(MapUiAccess.GetRightScreens(mfd));
            var size = MfdLayout.CanvasSize(canvas);
            if (count != screenCount || size != MfdRailPatch.AppliedCanvasSize)
            {
                MfdRailPatch.OnStructureChanged(map);
                screenCount = count;
            }
            MfdRailPatch.Reconcile();
            VanillaMfdRebuild.Tick();
            MfdLogPanel.Tick();
            MfdMapFooter.Tick();
        }
        private static int Count(System.Collections.Generic.List<MFDScreen> screens)
        {
            int count = 0;
            if (screens != null) foreach (var screen in screens) if (screen != null) count++;
            return count;
        }
        public void ResetForScene()
        {
            MapMfdLookup.Reset();
            MfdRailPatch.Reset();
            MfdNewsTicker.Reset();
            nextRefresh = 0f;
            screenCount = -1;
        }
        private void OnDestroy() => ResetForScene();
    }
}
