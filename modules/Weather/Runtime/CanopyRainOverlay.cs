using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// The fallback canopy rain: one screen-space overlay canvas with exactly two animated
    /// <see cref="RawImage"/> layers — fast streaks and a slow, independent droplet drift — so
    /// it sits above the 3D world and below the vanilla cockpit HUD.
    ///
    /// <para>This is what draws when there is no cockpit glass to lock to: an observer, a
    /// parachute, a cockpit layout without a resolvable Canopy. <see cref="GlassRain"/> is the
    /// real effect, and its owner tears this down the moment that layer takes over, so the two can
    /// never compete for the gameplay canvas's sorting order.</para>
    ///
    /// Both sheets are generated once, in memory, into one reusable <see cref="Color32"/> buffer:
    /// no shader, no bundle, no file. The canvas, the two textures and the two images are the
    /// whole footprint, and per frame only <c>uvRect</c>, colour alpha and the canvas switch are
    /// touched — never a rebuild, never an allocation.
    /// </summary>
    internal sealed class CanopyRainOverlay
    {
        private const int TextureSize = 512;

        /// <summary>Tile counts must stay integral or the uvRect seam would cut the pattern.</summary>
        private const int StreakTiles = 3;
        private const int DropletTiles = 2;

        private const int StreakCount = 46;
        private const int DropletCount = 54;
        private const int StreakSeed = 0x5261696E;
        private const int DropletSeed = 0x44726F70;

        private const float MinIntensity = 0.02f;
        private const float StreakOpacity = 0.85f;
        private const float DropletOpacity = 0.45f;

        /// <summary>
        /// Scroll in uv units a second at full relative speed. A full uv unit is one screen of
        /// the streak layer, so 1.8 reads as rain streaming past, not as a static sheet.
        /// </summary>
        private const float StreakUvPerSecond = 1.8f;
        private const float DropletUvPerSecond = 0.09f;
        private const float DropletCrossDrift = 0.35f;
        private const float RelativeSpeedReference = 320f;
        private const float MinScrollFactor = 0.2f;
        private const float MaxAdvanceStep = 0.1f;

        /// <summary>Above this it is ice, not rain: the overlay fades out over the taper.</summary>
        private const float IceCeilingMetres = 12000f;
        private const float IceTaperMetres = 1800f;

        /// <summary>Used only when GameplayUI has not resolved yet; the vanilla HUD layer is 1.</summary>
        private const int FallbackSortingOrder = 0;

        private GameObject canvasObject;
        private Canvas canvas;
        private RawImage streakImage;
        private RawImage dropletImage;
        private Texture2D streakTexture;
        private Texture2D dropletTexture;
        private Color32[] buffer;

        private float streakOffsetY;
        private float dropletOffsetX;
        private float dropletOffsetY;
        private float streakScroll;
        private float dropletScroll;

        public bool Active => canvas != null && canvas.enabled;

        public bool Created => canvasObject != null;

        public bool TryCreate()
        {
            if (canvasObject != null) return true;

            var host = new GameObject("BoscaliCanopyRain", typeof(RectTransform), typeof(Canvas));
            canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = HudSortingOrder();
            canvasObject = host;

            BuildTextures();
            streakImage = BuildLayer("Streaks", streakTexture, StreakTiles);
            dropletImage = BuildLayer("Droplets", dropletTexture, DropletTiles);
            canvas.enabled = false;
            return true;
        }

        /// <summary>
        /// The layer levels for this tick: intensity and density drive the alpha, the cockpit
        /// view and a live canopy gate it, and altitude fades it out. Below the floor the whole
        /// canvas is switched off rather than drawn transparent.
        /// </summary>
        public void Apply(float intensity, float density, bool cockpit, bool canopyGone,
                          float altitude, float relativeSpeed)
        {
            if (canvas == null) return;
            float fade = cockpit && !canopyGone ? AltitudeFade(altitude) : 0f;
            float alpha = Mathf.Clamp01(intensity) * Mathf.Clamp01(density) * fade;
            bool on = alpha >= MinIntensity;
            canvas.enabled = on;
            if (!on) return;

            float speed = MinScrollFactor + Mathf.Clamp01(relativeSpeed / RelativeSpeedReference);
            streakScroll = StreakUvPerSecond * speed;
            dropletScroll = DropletUvPerSecond * speed;
            streakImage.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha * StreakOpacity));
            dropletImage.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha * DropletOpacity));
        }

        /// <summary>Advance the two uvRect offsets. Called every frame so the scroll stays smooth.</summary>
        public void Advance(float unscaledDeltaTime)
        {
            if (canvas == null || !canvas.enabled) return;
            float dt = Mathf.Min(unscaledDeltaTime, MaxAdvanceStep);
            streakOffsetY = Mathf.Repeat(streakOffsetY + streakScroll * dt, 1f);
            dropletOffsetY = Mathf.Repeat(dropletOffsetY + dropletScroll * dt, 1f);
            dropletOffsetX = Mathf.Repeat(dropletOffsetX + dropletScroll * DropletCrossDrift * dt, 1f);
            streakImage.uvRect = new Rect(0f, streakOffsetY, StreakTiles, StreakTiles);
            dropletImage.uvRect = new Rect(dropletOffsetX, dropletOffsetY, DropletTiles, DropletTiles);
        }

        public void Teardown()
        {
            if (canvasObject != null) Object.Destroy(canvasObject);
            canvasObject = null;
            canvas = null;
            streakImage = null;
            dropletImage = null;
            if (streakTexture != null) Object.Destroy(streakTexture);
            if (dropletTexture != null) Object.Destroy(dropletTexture);
            streakTexture = null;
            dropletTexture = null;
            buffer = null;
            streakOffsetY = 0f;
            dropletOffsetX = 0f;
            dropletOffsetY = 0f;
            streakScroll = 0f;
            dropletScroll = 0f;
        }

        // ---- Build -----------------------------------------------------------------------------

        private RawImage BuildLayer(string name, Texture2D texture, int tiles)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            RawImage image = host.GetComponent<RawImage>();
            var rect = (RectTransform)host.transform;
            rect.SetParent(canvasObject.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            image.texture = texture;
            image.uvRect = new Rect(0f, 0f, tiles, tiles);
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = false;
            return image;
        }

        private void BuildTextures()
        {
            buffer = new Color32[TextureSize * TextureSize];
            System.Array.Clear(buffer, 0, buffer.Length);
            var random = new System.Random(StreakSeed);
            for (int i = 0; i < StreakCount; i++)
                PaintStreak(random.Next(TextureSize), 1 + random.Next(2),
                    24 + random.Next(96), 0.16f + (float)random.NextDouble() * 0.4f,
                    random.Next(TextureSize));
            streakTexture = Upload("BoscaliRainStreaks");

            System.Array.Clear(buffer, 0, buffer.Length);
            random = new System.Random(DropletSeed);
            for (int i = 0; i < DropletCount; i++)
                PaintDroplet(random.Next(TextureSize), random.Next(TextureSize),
                    2.5f + (float)random.NextDouble() * 5f, 0.35f + (float)random.NextDouble() * 0.5f);
            dropletTexture = Upload("BoscaliRainDroplets");
        }

        private Texture2D Upload(string name)
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels32(buffer);
            texture.Apply(false);
            return texture;
        }

        /// <summary>A soft vertical smear; both ends fade, and it wraps across the sheet seam.</summary>
        private void PaintStreak(int x, int width, int length, float peak, int startY)
        {
            for (int row = 0; row < length; row++)
            {
                float vertical = Mathf.Sin((row + 0.5f) / length * Mathf.PI);
                int y = Wrap(startY + row);
                for (int column = 0; column < width; column++)
                {
                    float across = width == 1
                        ? 1f
                        : 1f - Mathf.Abs(column - (width - 1) * 0.5f) / ((width - 1) * 0.5f + 1f);
                    Blend(Wrap(x + column), y, peak * vertical * across);
                }
            }
        }

        /// <summary>A soft blob with a brighter rim, drawn wrapped so the tile is seamless.</summary>
        private void PaintDroplet(int x, int y, float radius, float opacity)
        {
            int reach = Mathf.CeilToInt(radius) + 1;
            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    float distance = Mathf.Sqrt((float)(dx * dx + dy * dy));
                    if (distance > radius + 1f) continue;
                    float edge = distance / radius;
                    float body = Mathf.Clamp01(1f - edge) * 0.55f;
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(edge - 0.82f) * 7f) * 0.8f;
                    Blend(Wrap(x + dx), Wrap(y + dy), (body + rim) * opacity);
                }
            }
        }

        private void Blend(int x, int y, float alpha)
        {
            int index = y * TextureSize + x;
            byte value = (byte)(Mathf.Clamp01(alpha) * 255f);
            if (value <= buffer[index].a) return;
            buffer[index] = new Color32(255, 255, 255, value);
        }

        private static int Wrap(int value)
        {
            value %= TextureSize;
            return value < 0 ? value + TextureSize : value;
        }

        private static float AltitudeFade(float altitude) =>
            Mathf.Clamp01((IceCeilingMetres - altitude) / IceTaperMetres);

        /// <summary>
        /// One below the vanilla cockpit HUD layer. GameplayUI is the public gameplay canvas and
        /// the repo's verified HUD order is 1 (the maximised map is 2), so this resolves to 0:
        /// above the 3D world, under every native surface. If it has not resolved yet the
        /// fallback is 1, which is still inside the gameplay layer.
        /// </summary>
        private static int HudSortingOrder()
        {
            GameplayUI ui = SceneSingleton<GameplayUI>.i;
            Canvas hud = ui != null ? ui.gameplayCanvas : null;
            // Two below the vanilla HUD, not one: the weather HUD banner claims hud - 1, and rain
            // is a background effect. The banner is information and must never lose to it.
            return hud != null ? Mathf.Max(0, hud.sortingOrder - 2) : FallbackSortingOrder;
        }
    }
}
