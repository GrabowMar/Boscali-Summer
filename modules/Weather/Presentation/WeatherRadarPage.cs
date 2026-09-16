using System.Text;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Weather.Presentation
{
    /// <summary>
    /// The WEA panel's radar page: a north-up plan scope over the deterministic storm field,
    /// with reflectivity, wind and warning-ring products. Every echo is a model cell — the
    /// page says so on its face and never dresses as a game radar return.
    ///
    /// Pooled and allocation-free by construction: one build, then only moves, scales,
    /// colours and active flags on the tick.
    /// </summary>
    internal sealed class WeatherRadarPage
    {
        private const int ModeReflectivity = 0;
        private const int ModeWind = 1;
        private const int ModeWarning = 2;

        private static readonly string[] ModeNames = { "REFLECTIVITY", "WIND", "WARNING" };

        /// <summary>The scope is a square whose side is the page width it is handed.</summary>
        private const float ScopeWidthRatio = 1f;

        // The same rungs the WEA page cycles and the default config value sits on.
        private static readonly float[] RangeMetres = { 20000f, 40000f, 80000f, 160000f };
        private const int DefaultRangeIndex = 1;

        private static readonly float[] RangeRingFractions = { 1f / 3f, 2f / 3f, 1f };

        /// <summary>Mirrors the radii <see cref="StormCell.WarningAt"/> tests.</summary>
        private static readonly float[] WarningRingFractions = { 0.6f, 1.6f, 4f };

        /// <summary>The one palette this file names: the reflectivity product's four bands.</summary>
        private static readonly Color ReflectivityLight = new Color(0.30f, 0.95f, 0.40f);
        private static readonly Color ReflectivityModerate = new Color(0.96f, 0.86f, 0.22f);
        private static readonly Color ReflectivityHeavy = new Color(0.95f, 0.34f, 0.20f);
        private static readonly Color ReflectivityExtreme = new Color(0.88f, 0.35f, 0.96f);

        private const float ReflectivityModerateBand = 0.45f;
        private const float ReflectivityHeavyBand = 0.70f;
        private const float ReflectivityExtremeBand = 0.88f;

        private const int RingSpriteSize = 128;
        private const float RingSpriteThickness = 1.5f;
        private const int DotSpriteSize = 64;

        private const float HeaderHeight = 18f;
        private const float ControlHeight = 24f;
        private const float ControlGap = 6f;
        private const float ModeButtonShare = 0.55f;
        private const float ScopeGap = 6f;
        private const float RowHeight = 16f;
        private const float RowPitch = 18f;
        private const float RailWidth = 3f;
        private const float RowInset = 10f;

        private const float MinBlipPixels = 5f;
        private const float MinRingPixels = 4f;
        private const float BlipMinAlpha = 0.35f;
        private const float RingMinAlpha = 0.30f;

        /// <summary>Intensity x radius, metres, that reads as a full-brightness wind echo.</summary>
        private const float WindEmphasisFull = 6000f;
        private const float WindSpeedFullMps = 30f;
        private const float WindArmMinFraction = 0.06f;
        private const float WindArmMaxFraction = 0.26f;
        private const float CrosshairThickness = 1f;
        private const float PlayerDotFraction = 0.04f;

        private readonly WeatherSettings settings;
        private readonly StringBuilder text = new StringBuilder(160);

        private readonly Image[] blips = new Image[StormField.MaxCells];
        private readonly Image[] rings = new Image[StormField.MaxCells * WarningRingFractions.Length];
        private readonly Image[] rangeRings = new Image[RangeRingFractions.Length];
        private readonly Image[] rails = new Image[StormField.MaxCells];
        private readonly TMP_Text[] rows = new TMP_Text[StormField.MaxCells];

        private RectTransform scope;
        private RectTransform windCross;
        private Image windArmX;
        private Image windArmZ;
        private AvButton modeButton;
        private AvButton rangeButton;
        private Sprite ringSprite;
        private Sprite dotSprite;

        private float scopeSide;
        private int mode = ModeReflectivity;
        private int rangeIndex = DefaultRangeIndex;
        private float scopeRangeMetres;

        private WeatherSnapshot lastSnapshot;
        private float lastPlayerX;
        private float lastPlayerZ;
        private bool hasReading;

        /// <summary>
        /// The constructor keeps the page's configuration; the area arguments are passed
        /// again to <see cref="Build"/>, which is what draws, so they are not stored here.
        /// </summary>
        public WeatherRadarPage(RectTransform parent, float x, float y, float width, WeatherSettings settings)
        {
            this.settings = settings;
            rangeIndex = InitialRangeIndex(settings);
            scopeRangeMetres = RangeMetres[rangeIndex];
        }

        public int ModeIndex => mode;

        public string ModeLabel => ModeNames[Mathf.Clamp(mode, 0, ModeNames.Length - 1)];

        // ---- Build -----------------------------------------------------------------------

        /// <summary>Draws the whole page inside <paramref name="width"/> and returns the bottom y.</summary>
        public float Build(RectTransform parent, float x, float y, float width)
        {
            if (parent == null) return y;

            float side = Mathf.Max(1f, width * ScopeWidthRatio);
            scopeSide = side;

            AvStyled.Label(parent, new Rect(x, y, width, 14f), "RADAR SCOPE", "section-title");
            y -= 16f;
            AvStyled.Label(parent, new Rect(x, y, width, 12f),
                "MODEL ECHOES — DERIVED, NOT A RADAR RETURN", "section-title-note");
            y -= HeaderHeight;

            float modeWidth = (width - ControlGap) * ModeButtonShare;
            modeButton = AvStyled.Button(parent, new Rect(x, y, modeWidth, ControlHeight), ModeLabel, "btn", CycleMode)
                .WithTooltip("Cycle the scope product: reflectivity, wind, or warning rings. Every echo is model output.");
            rangeButton = AvStyled.Button(parent,
                new Rect(x + modeWidth + ControlGap, y, width - modeWidth - ControlGap, ControlHeight),
                StormReadout.NauticalMiles(scopeRangeMetres), "btn", CycleRange)
                .WithTooltip("Cycle the scope range. Echoes beyond the rim are pinned to it.");
            y -= ControlHeight + ScopeGap;

            BuildScope(parent, x, y, side);
            y -= side + ScopeGap;

            AvStyled.Label(parent, new Rect(x, y, width * 0.5f, 14f), "ECHOES", "section-title");
            AvStyled.Label(parent, new Rect(x + width * 0.5f, y, width * 0.5f, 14f), "TIER AT YOUR POSITION",
                "section-title-note", align: TextAlignmentOptions.MidlineRight);
            y -= HeaderHeight;

            for (int i = 0; i < StormField.MaxCells; i++)
            {
                float rowY = y - i * RowPitch;
                rails[i] = AvStyled.Rail(parent, new Rect(x, rowY, RailWidth, RowHeight), "locked");
                rows[i] = AvStyled.Label(parent, new Rect(x + RowInset, rowY, width - RowInset, RowHeight),
                    "", "row-sub", align: TextAlignmentOptions.MidlineLeft);
                rails[i].gameObject.SetActive(false);
                rows[i].gameObject.SetActive(false);
            }
            y -= StormField.MaxCells * RowPitch;

            hasReading = false;
            return y;
        }

        private void BuildScope(RectTransform parent, float x, float y, float side)
        {
            ringSprite = CreateRingSprite();
            dotSprite = CreateDotSprite();

            // A masked, centre-pivoted square: children are placed in centre-relative pixels,
            // +x east and +y north, so the mapping below needs no per-child conversion.
            var scopeObject = new GameObject("WeatherScope", typeof(RectTransform), typeof(RectMask2D));
            scope = (RectTransform)scopeObject.transform;
            scope.SetParent(parent, false);
            scope.anchorMin = scope.anchorMax = new Vector2(0f, 1f);
            scope.pivot = new Vector2(0.5f, 0.5f);
            scope.anchoredPosition = new Vector2(x + side * 0.5f, y - side * 0.5f);
            scope.sizeDelta = new Vector2(side, side);

            Image ground = AvKit.Panel(scope, new Rect(0f, 0f, side, side), AvTheme.Ground);
            AvKit.Stretch(ground.rectTransform);
            // The frame lives on the page, not in the mask, so its edge is never half-clipped.
            AvKit.Outline(parent, new Rect(x, y, side, side), AvTheme.Hairline);
            AvKit.CornerTicks(parent, new Rect(x, y, side, side), AvTheme.Hairline);

            for (int i = 0; i < rangeRings.Length; i++)
            {
                float diameter = side * RangeRingFractions[i];
                rangeRings[i] = Echo(scope, ringSprite, AvTheme.Hairline);
                PlaceAt(rangeRings[i].rectTransform, Vector2.zero, diameter, diameter);
            }

            for (int i = 0; i < rings.Length; i++)
            {
                rings[i] = Echo(scope, ringSprite, AvTheme.RailInert);
                PlaceAt(rings[i].rectTransform, Vector2.zero, MinRingPixels, MinRingPixels);
                rings[i].gameObject.SetActive(false);
            }

            for (int i = 0; i < blips.Length; i++)
            {
                blips[i] = Echo(scope, dotSprite, AvTheme.RailInert);
                PlaceAt(blips[i].rectTransform, Vector2.zero, MinBlipPixels, MinBlipPixels);
                blips[i].gameObject.SetActive(false);
            }

            Image player = Echo(scope, dotSprite, AvTheme.Accent);
            PlaceAt(player.rectTransform, Vector2.zero, side * PlayerDotFraction, side * PlayerDotFraction);

            var crossObject = new GameObject("WindCross", typeof(RectTransform));
            windCross = (RectTransform)crossObject.transform;
            windCross.SetParent(scope, false);
            PlaceAt(windCross, Vector2.zero, 1f, 1f);
            windArmX = AvKit.Panel(windCross, new Rect(0f, 0f, 1f, CrosshairThickness), AvTheme.RailInfo);
            windArmZ = AvKit.Panel(windCross, new Rect(0f, 0f, CrosshairThickness, 1f), AvTheme.RailInfo);
            PlaceAt(windArmX.rectTransform, Vector2.zero, 1f, CrosshairThickness);
            PlaceAt(windArmZ.rectTransform, Vector2.zero, CrosshairThickness, 1f);
            windCross.gameObject.SetActive(false);
        }

        // ---- Refresh ---------------------------------------------------------------------

        public void Refresh(WeatherSnapshot snapshot, WeatherManager manager, float playerX, float playerZ)
        {
            lastSnapshot = snapshot;
            lastPlayerX = playerX;
            lastPlayerZ = playerZ;
            hasReading = true;
            Apply();
        }

        private void Apply()
        {
            if (scope == null || !hasReading) return;

            WeatherSnapshot snapshot = lastSnapshot;
            float playerX = lastPlayerX;
            float playerZ = lastPlayerZ;
            float halfSide = scopeSide * 0.5f;
            float pixelsPerMetre = scopeRangeMetres > 0f ? halfSide / scopeRangeMetres : 0f;
            int cellCount = snapshot.Cells == null ? 0 : Mathf.Clamp(snapshot.CellCount, 0, StormField.MaxCells);

            bool wind = mode == ModeWind;
            if (!snapshot.Available)
            {
                for (int i = 0; i < StormField.MaxCells; i++) HideCell(i);
                windCross.gameObject.SetActive(false);
                ShowNote("NO READOUT — NO WEATHER SCHEDULE ON THIS MISSION");
                return;
            }

            windCross.gameObject.SetActive(wind);
            if (wind)
            {
                float arm = Mathf.Lerp(WindArmMinFraction, WindArmMaxFraction,
                    Mathf.Clamp01(snapshot.LocalWindSpeed / WindSpeedFullMps)) * scopeSide;
                PlaceAt(windArmX.rectTransform, Vector2.zero, arm * 2f, CrosshairThickness);
                PlaceAt(windArmZ.rectTransform, Vector2.zero, CrosshairThickness, arm * 2f);
                windCross.localRotation = Quaternion.Euler(0f, 0f, 90f - snapshot.LocalWindHeading);
            }

            if (cellCount == 0)
            {
                for (int i = 0; i < StormField.MaxCells; i++) HideCell(i);
                ShowNote("NO ECHOES — THE SCHEDULE RAISES NO CELLS IN THIS FRONT");
                return;
            }

            bool ringsMode = mode == ModeWarning;
            for (int i = 0; i < cellCount; i++)
            {
                StormCell cell = snapshot.Cells[i];
                StormWarning tier = cell.WarningAt(playerX, playerZ);
                Vector2 point = ScopePoint(cell.X, cell.Z, playerX, playerZ, scopeRangeMetres, halfSide);

                BindBlip(i, cell, point, pixelsPerMetre, !ringsMode);
                BindRings(i, cell, tier, point, pixelsPerMetre, ringsMode);
                BindRow(i, cell, playerX, playerZ, tier);
            }
            for (int i = cellCount; i < StormField.MaxCells; i++) HideCell(i);
        }

        /// <summary>
        /// World metres to scope pixels, centre-relative, north up: world +Z is scope +y and
        /// world +X is scope +x. A point beyond the rim is pinned to it, so a cell out of
        /// range still reads as a bearing rather than vanishing.
        /// </summary>
        private static Vector2 ScopePoint(
            float worldX, float worldZ, float playerX, float playerZ,
            float rangeMetres, float halfSide)
        {
            float dx = worldX - playerX;
            float dz = worldZ - playerZ;
            float scale = rangeMetres > 0f ? halfSide / rangeMetres : 0f;
            var point = new Vector2(dx * scale, dz * scale);
            float distance = point.magnitude;
            if (distance > halfSide && distance > 0f) point *= halfSide / distance;
            return point;
        }

        private void BindBlip(int index, StormCell cell, Vector2 point, float pixelsPerMetre, bool visible)
        {
            Image blip = blips[index];
            float diameter = Mathf.Max(MinBlipPixels, cell.Radius * pixelsPerMetre * 2f);
            PlaceAt(blip.rectTransform, point, diameter, diameter);

            Color tint;
            if (mode == ModeWind)
            {
                float emphasis = Mathf.Clamp01(cell.Intensity * cell.Radius / WindEmphasisFull);
                tint = AvTheme.RailInfo;
                tint.a = Mathf.Lerp(BlipMinAlpha, 1f, emphasis);
            }
            else
            {
                tint = ReflectivityTint(cell.Intensity);
                tint.a = Mathf.Lerp(BlipMinAlpha, 1f, Mathf.Clamp01(cell.Intensity));
            }
            blip.color = tint;
            if (blip.gameObject.activeSelf != visible) blip.gameObject.SetActive(visible);
        }

        private void BindRings(int index, StormCell cell, StormWarning tier, Vector2 point, float pixelsPerMetre, bool visible)
        {
            Color tint = RailColor(tier);
            tint.a = Mathf.Lerp(RingMinAlpha, 1f, Mathf.Clamp01(cell.Intensity));

            int first = index * WarningRingFractions.Length;
            for (int i = 0; i < WarningRingFractions.Length; i++)
            {
                Image ring = rings[first + i];
                float diameter = Mathf.Max(MinRingPixels, WarningRingFractions[i] * cell.Radius * pixelsPerMetre * 2f);
                PlaceAt(ring.rectTransform, point, diameter, diameter);
                ring.color = tint;
                if (ring.gameObject.activeSelf != visible) ring.gameObject.SetActive(visible);
            }
        }

        private void BindRow(int index, StormCell cell, float playerX, float playerZ, StormWarning tier)
        {
            text.Length = 0;
            text.Append(StormReadout.Kind(cell.Kind));
            text.Append("  ");
            text.Append(StormReadout.NauticalMiles(cell.DistanceTo(playerX, playerZ)));
            text.Append("  ");
            text.Append(StormReadout.BearingTo(playerX, playerZ, cell.X, cell.Z));
            text.Append("  ");
            text.Append(WeatherReadout.Percent01(cell.Intensity));
            text.Append("  ");
            text.Append(StormReadout.Warning(tier));
            rows[index].SetText(text);

            rails[index].color = RailColor(tier);
            if (!rows[index].gameObject.activeSelf) rows[index].gameObject.SetActive(true);
            if (!rails[index].gameObject.activeSelf) rails[index].gameObject.SetActive(true);
        }

        private void ShowNote(string message)
        {
            text.Length = 0;
            text.Append(message);
            rows[0].SetText(text);
            rails[0].color = AvTheme.RailInert;
            rows[0].gameObject.SetActive(true);
            rails[0].gameObject.SetActive(true);
        }

        private void HideCell(int index)
        {
            if (blips[index].gameObject.activeSelf) blips[index].gameObject.SetActive(false);

            int first = index * WarningRingFractions.Length;
            for (int i = 0; i < WarningRingFractions.Length; i++)
            {
                if (rings[first + i].gameObject.activeSelf) rings[first + i].gameObject.SetActive(false);
            }
            if (rows[index].gameObject.activeSelf) rows[index].gameObject.SetActive(false);
            if (rails[index].gameObject.activeSelf) rails[index].gameObject.SetActive(false);
        }

        // ---- Controls --------------------------------------------------------------------

        private void CycleMode()
        {
            mode = (mode + 1) % ModeNames.Length;
            if (modeButton != null) modeButton.SetText(ModeLabel);
            Apply();
        }

        private void CycleRange()
        {
            rangeIndex = (rangeIndex + 1) % RangeMetres.Length;
            scopeRangeMetres = RangeMetres[rangeIndex];
            if (settings != null) settings.RadarRangeKm.Value = (int)(scopeRangeMetres / 1000f);
            if (rangeButton != null) rangeButton.SetText(StormReadout.NauticalMiles(scopeRangeMetres));
            Apply();
        }

        /// <summary>The nearest rung to whatever the config holds, so a hand-edited value still lands.</summary>
        private static int InitialRangeIndex(WeatherSettings settings)
        {
            if (settings == null) return DefaultRangeIndex;

            float metres = settings.RadarRangeKm.Value * 1000f;
            int best = DefaultRangeIndex;
            float bestDelta = float.MaxValue;
            for (int i = 0; i < RangeMetres.Length; i++)
            {
                float delta = Mathf.Abs(RangeMetres[i] - metres);
                if (delta >= bestDelta) continue;
                bestDelta = delta;
                best = i;
            }
            return best;
        }

        // ---- Presentation ----------------------------------------------------------------

        private static Color ReflectivityTint(float intensity)
        {
            if (intensity >= ReflectivityExtremeBand) return ReflectivityExtreme;
            if (intensity >= ReflectivityHeavyBand) return ReflectivityHeavy;
            if (intensity >= ReflectivityModerateBand) return ReflectivityModerate;
            return ReflectivityLight;
        }

        /// <summary>The tier's rail colour, resolved from the same class the readout names.</summary>
        private static Color RailColor(StormWarning warning)
        {
            AvStyle style = AvStyleHost.Style(StormReadout.RailClass(warning));
            return AvStyleHost.Resolve(style.Background, AvTheme.RailInert);
        }

        private static Image Echo(Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject("Echo", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            image.color = color;
            return image;
        }

        private static void PlaceAt(RectTransform rect, Vector2 point, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = point;
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>A one-pixel circle outline with a transparent centre, built once.</summary>
        private static Sprite CreateRingSprite()
        {
            var texture = new Texture2D(RingSpriteSize, RingSpriteSize, TextureFormat.RGBA32, mipChain: false)
            {
                name = "BoscaliWeather.Ring",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float centre = RingSpriteSize * 0.5f;
            float radius = centre - RingSpriteThickness - 1f;
            for (int y = 0; y < RingSpriteSize; y++)
            {
                for (int x = 0; x < RingSpriteSize; x++)
                {
                    float dx = x + 0.5f - centre;
                    float dy = y + 0.5f - centre;
                    float band = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - radius);
                    Color pixel = Color.white;
                    pixel.a = Mathf.Clamp01(RingSpriteThickness * 0.5f + 0.5f - band);
                    texture.SetPixel(x, y, pixel);
                }
            }
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, RingSpriteSize, RingSpriteSize),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
            sprite.name = "BoscaliWeather.Ring";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>A soft dot for blips and the player marker, built once.</summary>
        private static Sprite CreateDotSprite()
        {
            var texture = new Texture2D(DotSpriteSize, DotSpriteSize, TextureFormat.RGBA32, mipChain: false)
            {
                name = "BoscaliWeather.Dot",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float centre = DotSpriteSize * 0.5f;
            float radius = centre - 1f;
            for (int y = 0; y < DotSpriteSize; y++)
            {
                for (int x = 0; x < DotSpriteSize; x++)
                {
                    float dx = x + 0.5f - centre;
                    float dy = y + 0.5f - centre;
                    float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / radius);
                    Color pixel = Color.white;
                    pixel.a = falloff * falloff;
                    texture.SetPixel(x, y, pixel);
                }
            }
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, DotSpriteSize, DotSpriteSize),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
            sprite.name = "BoscaliWeather.Dot";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // ---- Teardown --------------------------------------------------------------------

        /// <summary>
        /// Called before the page's GameObjects are destroyed. Safe to call twice; the
        /// generated sprites and their textures are the only things this page owns.
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < blips.Length; i++) blips[i] = null;
            for (int i = 0; i < rings.Length; i++) rings[i] = null;
            for (int i = 0; i < rangeRings.Length; i++) rangeRings[i] = null;
            for (int i = 0; i < rails.Length; i++) rails[i] = null;
            for (int i = 0; i < rows.Length; i++) rows[i] = null;

            scope = null;
            windCross = null;
            windArmX = null;
            windArmZ = null;
            modeButton = null;
            rangeButton = null;
            hasReading = false;
            lastSnapshot = WeatherSnapshot.Unavailable;

            if (ringSprite != null)
            {
                UnityEngine.Object.Destroy(ringSprite.texture);
                UnityEngine.Object.Destroy(ringSprite);
                ringSprite = null;
            }
            if (dotSprite != null)
            {
                UnityEngine.Object.Destroy(dotSprite.texture);
                UnityEngine.Object.Destroy(dotSprite);
                dotSprite = null;
            }
        }
    }
}
