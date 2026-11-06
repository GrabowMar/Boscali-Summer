using BoscaliSummer.Core.Lifecycle;
using HarmonyLib;
using BoscaliSummer.Modules.Command.Configuration;
using NuclearOption.MissionEditorScripts;
using NuclearOption.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// MAP HOLD KEY (default V): a quick look at where you are. Held while flying it grows the game's own corner map (heading up, same zoom, same icons)
    /// to a see-through square in the middle of the screen; let go and it goes back to the corner. Nothing takes the mouse, nothing pauses, no MFD opens.
    /// Only the minimised map's own size, place and image alpha are touched, and they are put back exactly as the game's Minimize sets them.
    /// </summary>
    internal sealed class MapHoldKey : MonoBehaviour, ISceneService
    {
        private const float ScreenShare = .85f;   // of the screen's height
        private const float MapAlpha = .45f;       // the terrain image; icons stay solid

        private static readonly AccessTools.FieldRef<DynamicMap, RectTransform> MapRect = AccessTools.FieldRefAccess<DynamicMap, RectTransform>("mapRectTransform");
        private static readonly AccessTools.FieldRef<DynamicMap, RectTransform> BackRect = AccessTools.FieldRefAccess<DynamicMap, RectTransform>("backgroundRectTransform");

        private CommandSettings settings;
        private DynamicMap grown;
        private Image image;
        private float imageAlpha;

        public void Configure(CommandSettings config) => settings = config;

        private void LateUpdate()
        {
            if (settings == null) return;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            bool want = map != null && Wanted(map);
            if (grown != null && (!want || grown != map)) Restore();
            if (want && grown == null) Grow(map);
            if (grown != null) Place(grown);
        }

        private bool Wanted(DynamicMap map)
        {
            KeyCode key = settings.MapHoldKey.Value;
            if (key == KeyCode.None || DynamicMap.mapMaximized || !map.gameObject.activeInHierarchy || !Input.GetKey(key)) return false;
            if (InputFieldChecker.InsideInputField || GameplayUI.GameIsPaused || BoscaliSummer.Core.Ui.MapTool.Typing()) return false;
            return GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null && !aircraft.disabled;
        }

        private void Grow(DynamicMap map)
        {
            grown = map;
            image = map.mapImage != null ? map.mapImage.GetComponent<Image>() : null;
            if (image != null)
            {
                imageAlpha = image.color.a;
                Color c = image.color;
                c.a = imageAlpha * MapAlpha;
                image.color = c;
            }
        }

        /// <summary>Every frame while held: the square in the screen's middle (the canvas may rescale with the window).</summary>
        private static void Place(DynamicMap map)
        {
            Canvas canvas = map.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var screen = (RectTransform)canvas.rootCanvas.transform;
            float scale = Mathf.Max(1e-4f, MapRect(map).lossyScale.y);
            float side = screen.rect.height * screen.lossyScale.y * ScreenShare / scale;
            MapRect(map).sizeDelta = Vector2.one * side;
            BackRect(map).sizeDelta = MapRect(map).sizeDelta + new Vector2(20f, 20f);
            map.transform.position = screen.position;
        }

        /// <summary>Back to the corner exactly as the game's Minimize lays it out.</summary>
        private void Restore()
        {
            DynamicMap map = grown;
            grown = null;
            if (image != null)
            {
                Color c = image.color;
                c.a = imageAlpha;
                image.color = c;
            }
            image = null;
            if (map == null || DynamicMap.mapMaximized) return;   // Maximize has laid it out already
            map.transform.localPosition = Vector3.zero;
            MapRect(map).sizeDelta = Vector2.one * map.mapScaleMinimized;
            BackRect(map).sizeDelta = MapRect(map).sizeDelta + new Vector2(20f, 20f);
        }

        public void ResetForScene()
        {
            if (grown != null) Restore();
        }
    }
}
