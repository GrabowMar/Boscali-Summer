using UnityEngine;

namespace BoscaliSummer.Runtime
{
    /// <summary>
    /// The one world span every theater-map overlay draws against: the extent the vanilla
    /// map image represents. Resolving this in more than one place is how a frontline
    /// overlay and its trench traces end up in different reference frames.
    /// </summary>
    internal static class TheaterFrame
    {
        private static readonly Vector2 Default = new Vector2(163840f, 81920f);
        private static Vector2 cached = Default;
        private static bool resolved;

        public static void Invalidate() => resolved = false;

        public static Vector2 Resolve(DynamicMap map = null)
        {
            if (resolved) return cached;
            resolved = true;
            cached = Default;

            try
            {
                MapSettings mapSettings = Object.FindObjectOfType<MapSettings>();
                if (mapSettings != null && mapSettings.MapSize.x > 1000f && mapSettings.MapSize.y > 1000f)
                {
                    cached = mapSettings.MapSize;
                    return cached;
                }
            }
            catch (System.Exception) { }

            try
            {
                DynamicMap dynamicMap = map != null ? map : SceneSingleton<DynamicMap>.i;
                if (dynamicMap != null && dynamicMap.mapImage != null)
                {
                    RectTransform rect = dynamicMap.mapImage.GetComponent<RectTransform>();
                    if (rect != null && rect.sizeDelta.x > 100f && rect.sizeDelta.y > 100f)
                    {
                        // DynamicMap sets sizeDelta = MapSize / 81920f * 900f.
                        float szX = (rect.sizeDelta.x / 900f) * 81920f;
                        float szY = (rect.sizeDelta.y / 900f) * 81920f;
                        if (szX > 1000f && szY > 1000f)
                        {
                            cached = new Vector2(szX, szY);
                            return cached;
                        }
                    }
                }
            }
            catch (System.Exception) { }

            try
            {
                LevelInfo levelInfo = NetworkSceneSingleton<LevelInfo>.i;
                if (levelInfo != null && levelInfo.mapSize > 1000f)
                    cached = new Vector2(levelInfo.mapSize * 2f, levelInfo.mapSize);
            }
            catch (System.Exception) { }

            return cached;
        }
    }
}
