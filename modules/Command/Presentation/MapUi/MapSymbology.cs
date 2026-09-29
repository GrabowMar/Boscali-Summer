using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Contact symbology for the maximised tactical map.
    ///
    /// <para>The game draws every ground vehicle with one blob, every aircraft and ship as a bare
    /// silhouette, and at map scale they all read as tiny tinted specks. This layer keeps the
    /// game's icon (its selection, tint, tooltip, click and heading behaviour stay authoritative)
    /// and adds two things on top of the same pipeline: a framed dark plate behind each contact
    /// whose outline shape marks allegiance (see <see cref="MapSymbolAtlas"/>), and a distinct
    /// glyph for ground platforms that the game gives no distinguishing sprite (armour, light
    /// vehicles, supply trucks, artillery, anti-air guns, SAMs, radars). Aircraft and ships keep the
    /// game's per-type silhouette, which already differs by class and rotates with heading.</para>
    ///
    /// <para>Bounded and cached: one small state per icon (hard ceiling <see cref="MaximumSymbols"/>),
    /// one shared plate layer, no per-frame allocation, no scene scans. Plates live in that layer
    /// beneath every icon, so a plate never covers a neighbour's glyph. The layer is swept once a
    /// second for destroyed icons.</para>
    /// </summary>
    internal static class MapSymbology
    {
        internal const int MaximumSymbols = 2048;
        /// <summary>Plate width over glyph width; glyphs are drawn to fit inside the hostile diamond.</summary>
        internal const float PlateRatio = 1.5f;
        /// <summary>Plate width in reference pixels at the game's MEDIUM (80%) symbol size.</summary>
        internal const float MediumPlate = 24f;
        private const float SelectedGrowth = 1.2f;
        private const float SweepInterval = 1f;

        private sealed class Symbol
        {
            internal Image Plate;
            internal RectTransform PlateRect;
            internal Sprite Original;
            internal Sprite GlyphSprite;
            internal float Relative = 1f;
            internal MapSide Side = (MapSide)(-1);
            internal Vector2 PlateSize;
        }

        private static readonly Symbol NotApplicable = new Symbol();
        private static readonly Dictionary<UnitMapIcon, Symbol> symbols = new Dictionary<UnitMapIcon, Symbol>();
        private static readonly List<UnitMapIcon> stale = new List<UnitMapIcon>(64);
        private static RectTransform layer;
        private static float layerScale = 1f;
        private static float nextSweep;
        private static FactionHQ spectatorBase;

        internal static bool Enabled => DynamicMap.mapMaximized &&
            Plugin.Settings?.Command?.MapSymbology?.Value == true;

        /// <summary>Postfix work for <c>UnitMapIcon.UpdateIcon</c>: keeps sprite, frame and tint current.
        /// On the flat map it also places the plate; the relief places it while it projects.</summary>
        internal static void Apply(UnitMapIcon icon, bool relief)
        {
            if (icon == null) return;
            if (!Enabled) { Release(icon); return; }
            Symbol symbol = Prepare(icon);
            if (symbol == NotApplicable) return;
            if (relief) return;
            Fit(icon, SceneSingleton<DynamicMap>.i != null &&
                SceneSingleton<DynamicMap>.i.selectedIcons.Contains(icon));
            Sync(icon);
        }

        /// <summary>Sizes the game's icon to the symbol grid. False when the icon keeps the game's sizing.</summary>
        internal static bool Fit(UnitMapIcon icon, bool selected)
        {
            if (!Enabled || icon == null || icon.iconImage == null) return false;
            if (!symbols.TryGetValue(icon, out Symbol symbol)) symbol = Prepare(icon);
            if (symbol == NotApplicable || symbol.Plate == null) return false;
            Transform transform = icon.iconImage.transform;
            var rect = transform as RectTransform;
            if (rect == null) return false;
            float pixels = Mathf.Max(rect.rect.width, rect.rect.height);
            float lossy = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
            if (pixels < .01f || lossy < .0001f) return true;
            float glyph = MediumPlate * SizeFactor() * symbol.Relative / PlateRatio *
                (selected ? SelectedGrowth : 1f) * layerScale;
            transform.localScale *= glyph / (pixels * lossy);
            return true;
        }

        /// <summary>Moves the plate to wherever the icon finally landed this frame.</summary>
        internal static void Sync(UnitMapIcon icon)
        {
            if (icon == null || !symbols.TryGetValue(icon, out Symbol symbol) || symbol == NotApplicable ||
                symbol.Plate == null) return;
            Image image = icon.iconImage;
            bool show = Enabled && image != null && image.enabled && icon.gameObject.activeInHierarchy;
            if (symbol.Plate.enabled != show) symbol.Plate.enabled = show;
            if (!show) return;
            Transform transform = image.transform;
            var glyphRect = transform as RectTransform;
            RectTransform plate = symbol.PlateRect;
            plate.position = transform.position;
            plate.rotation = Quaternion.identity;
            float ratio = Mathf.Abs(transform.lossyScale.x) / Mathf.Max(.0001f, Mathf.Abs(layer.lossyScale.x));
            plate.localScale = new Vector3(ratio, ratio, 1f);
            if (glyphRect != null)
            {
                float side = Mathf.Max(glyphRect.rect.width, glyphRect.rect.height) * PlateRatio;
                if (!Mathf.Approximately(symbol.PlateSize.x, side))
                {
                    symbol.PlateSize = new Vector2(side, side);
                    plate.sizeDelta = symbol.PlateSize;
                }
            }
            symbol.Plate.color = image.color;
        }

        /// <summary>Puts the game's own icon back and hides the plate.</summary>
        internal static void Release(UnitMapIcon icon)
        {
            if (icon == null || !symbols.TryGetValue(icon, out Symbol symbol) || symbol == NotApplicable) return;
            if (symbol.Plate != null && symbol.Plate.enabled) symbol.Plate.enabled = false;
            Image image = icon.iconImage;
            if (image != null && symbol.GlyphSprite != null && image.sprite == symbol.GlyphSprite &&
                symbol.Original != null) image.sprite = symbol.Original;
        }

        // ------------------------------------------------------------ resolve

        private static Symbol Prepare(UnitMapIcon icon)
        {
            if (Time.unscaledTime >= nextSweep) Sweep();
            if (!symbols.TryGetValue(icon, out Symbol symbol))
            {
                Unit unit = icon.unit;
                if (unit == null || icon.iconImage == null) return NotApplicable;
                if (symbols.Count >= MaximumSymbols) return NotApplicable;
                symbol = Classify(icon, unit);
                symbols.Add(icon, symbol);
            }
            if (symbol == NotApplicable) return symbol;
            Unit contact = icon.unit;
            if (contact == null || icon.iconImage == null || !EnsurePlate(symbol)) return symbol;
            MapSide side = SideOf(contact);
            if (side != symbol.Side)
            {
                symbol.Side = side;
                symbol.Plate.sprite = MapSymbolAtlas.Plate(side);
            }
            Image image = icon.iconImage;
            if (symbol.GlyphSprite != null && image.sprite != symbol.GlyphSprite &&
                (image.sprite == symbol.Original || image.sprite == null)) image.sprite = symbol.GlyphSprite;
            return symbol;
        }

        private static Symbol Classify(UnitMapIcon icon, Unit unit)
        {
            var symbol = new Symbol { Original = unit.definition != null ? unit.definition.mapIcon : null };
            float scale = unit.definition != null ? unit.definition.mapIconSize : 1f;
            symbol.Relative = Mathf.Clamp(scale <= 0f ? 1f : scale, .85f, 1.35f);
            if (unit is Ship) { symbol.GlyphSprite = MapSymbolAtlas.Glyph(MapGlyph.Hull); return symbol; }
            if (unit is Aircraft)
            {
                // Jets keep the game's per-type silhouette; rotor sprites are all thin lines at this size.
                if (symbol.Original != null && symbol.Original.name.IndexOf("helo", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    symbol.GlyphSprite = MapSymbolAtlas.Glyph(MapGlyph.Helicopter);
                return symbol;
            }
            if (!(unit is GroundVehicle)) return NotApplicable;
            var vehicle = unit.definition as VehicleDefinition;
            symbol.GlyphSprite = MapSymbolAtlas.Glyph(GroundGlyph(vehicle != null ? vehicle.vehicleType : VehicleType.LCV));
            return symbol;
        }

        internal static MapGlyph GroundGlyph(VehicleType type)
        {
            switch (type)
            {
                case VehicleType.MBT:
                case VehicleType.AFV: return MapGlyph.Armor;
                case VehicleType.TRUCK:
                case VehicleType.UGV: return MapGlyph.Supply;
                case VehicleType.ART: return MapGlyph.Artillery;
                case VehicleType.AAA: return MapGlyph.AntiAir;
                case VehicleType.IR_SAM:
                case VehicleType.R_SAM: return MapGlyph.Sam;
                case VehicleType.RDR: return MapGlyph.Radar;
                default: return MapGlyph.Light;
            }
        }

        private static MapSide SideOf(Unit unit)
        {
            switch (DynamicMap.GetFactionMode(unit.NetworkHQ, true))
            {
                case FactionMode.Friendly: return MapSide.Friendly;
                case FactionMode.Enemy: return MapSide.Hostile;
                case FactionMode.Spectator:
                    // A spectator sees both factions in their own colours: the first one met gets the
                    // friendly frame, every other the hostile frame, so shape still separates them.
                    if (spectatorBase == null) spectatorBase = unit.NetworkHQ;
                    return unit.NetworkHQ == spectatorBase ? MapSide.Friendly : MapSide.Hostile;
                default: return MapSide.Neutral;
            }
        }

        private static float SizeFactor()
        {
            MapOptions options = SceneSingleton<MapOptions>.i;
            float size = options != null ? options.iconSize : .8f;
            if (float.IsNaN(size) || float.IsInfinity(size) || size <= 0f) return 1f;
            return Mathf.Clamp(size / .8f, .75f, 1.3f);
        }

        // -------------------------------------------------------------- plates

        private static bool EnsurePlate(Symbol symbol)
        {
            if (symbol.Plate != null) return true;
            if (!EnsureLayer()) return false;
            var go = new GameObject("NOAvionics.MapPlate", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(layer, false);
            symbol.Plate = go.GetComponent<Image>();
            symbol.Plate.raycastTarget = false;
            symbol.Plate.enabled = false;
            symbol.PlateRect = symbol.Plate.rectTransform;
            symbol.PlateSize = Vector2.zero;
            symbol.Side = (MapSide)(-1);
            return true;
        }

        private static bool EnsureLayer()
        {
            if (layer != null) return true;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.iconLayer == null) return false;
            var go = new GameObject("NOAvionics.MapSymbols", typeof(RectTransform));
            go.transform.SetParent(map.iconLayer.transform, false);
            go.transform.SetAsFirstSibling();
            layer = go.GetComponent<RectTransform>();
            layer.anchorMin = layer.anchorMax = new Vector2(.5f, .5f);
            layer.sizeDelta = Vector2.zero;
            layer.localScale = Vector3.one;
            Canvas canvas = layer.GetComponentInParent<Canvas>();
            Canvas root = canvas != null ? canvas.rootCanvas : null;
            layerScale = root != null ? Mathf.Max(.01f, Mathf.Abs(root.transform.lossyScale.x)) : 1f;
            return true;
        }

        /// <summary>Drops state and plates whose icon the game has destroyed.</summary>
        private static void Sweep()
        {
            nextSweep = Time.unscaledTime + SweepInterval;
            stale.Clear();
            foreach (KeyValuePair<UnitMapIcon, Symbol> entry in symbols)
                if (entry.Key == null || entry.Key.iconImage == null) stale.Add(entry.Key);
            for (int i = 0; i < stale.Count; i++)
            {
                symbols.TryGetValue(stale[i], out Symbol symbol);
                if (symbol != null && symbol.Plate != null) Object.Destroy(symbol.Plate.gameObject);
                symbols.Remove(stale[i]);
            }
            stale.Clear();
        }
    }
}
