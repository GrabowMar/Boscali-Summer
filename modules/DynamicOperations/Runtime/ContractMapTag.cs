using BoscaliSummer.Modules.DynamicOperations.Domain;
using BoscaliSummer.Core.Contracts;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.DynamicOperations.Runtime
{
    /// <summary>Offers use open corners, accepted tasks a diamond. Both use host-permitted
    /// fixes and project their operating areas through the map's current ground projection.</summary>
    internal sealed class ContractMapTag
    {
        private readonly RectTransform root;
        private readonly AvVector symbol, area;
        private readonly TMP_Text label;
        private int labelId = -1, areaId = -1, areaRevision = -1;
        private string title, phase;
        private bool offered, lastKnown, returning;
        private Color labelTint;
        private float labelLift = -1f, areaRadius, areaZoom, areaFactor;
        private Vector2 areaCentre;
        private IMapProjection areaProjection;

        internal ContractMapTag(RectTransform parent, string name)
        {
            root = AvLay.Child(parent, name);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            root.sizeDelta = Vector2.zero;
            area = AvVector.Create(root, "Task operating area", 512);
            symbol = AvVector.Create(root, "Task fix", 32);
            label = AvText.Make(root, "Task identity", AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, .5f);
            label.rectTransform.pivot = new Vector2(0f, 1f);
            label.rectTransform.sizeDelta = new Vector2(200f, 28f);
            label.fontSize = 11f;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.richText = false;
            label.raycastTarget = false;
            SetVisible(false);
        }

        public void SetVisible(bool visible) => root.gameObject.SetActive(visible);

        public void Place(float x, float y, float inverseZoom)
        {
            root.anchoredPosition = new Vector2(x, y);
            root.localScale = new Vector3(inverseZoom, inverseZoom, 1f);
        }

        public void Apply(in ContractCard next, float lift)
        {
            Color tint = next.LastKnown || next.Tone == MarkerTone.Caution ? AvTheme.RailCaution :
                next.IsOffered ? AvTheme.Dim : AvTheme.RailReady;
            if (labelId == next.Id && title == next.Title && phase == next.PhaseTitle &&
                offered == next.IsOffered && lastKnown == next.LastKnown && returning == next.Returning && labelTint == tint && labelLift == lift) return;
            labelId = next.Id; title = next.Title; phase = next.PhaseTitle;
            offered = next.IsOffered; lastKnown = next.LastKnown; returning = next.Returning; labelTint = tint; labelLift = lift;
            areaId = -1;
            // Offers read quieter: dim tint, faded ink, number only. The bracket says "offer", and the title is on the STR
            // sortie tag and the MIS board; printing it here too doubled every offer on the map.
            label.color = next.IsOffered ? tint.WithAlpha(.7f) : tint;
            string text = next.IsOffered ? "#" + next.Id : "#" + next.Id + " " + next.Title + "\n" +
                (next.LastKnown ? "LAST KNOWN" : next.Returning ? "RTB" : next.PhaseTitle);
            if (label.text != text) label.text = text;
            label.rectTransform.anchoredPosition = new Vector2(12f, 11f + lift);
            var b = symbol.Buffer; b.Clear();
            var ink = new Rgba(tint.r, tint.g, tint.b, next.IsOffered ? .6f : .9f);
            if (next.IsOffered) AvStrokes.Bracket(b, -5f, -5f, 10f, 10f, 3f, 1f, ink);
            else
            {
                AvStrokes.Line(b, -6, 0, 0, 6, 1.2f, ink, 1f);
                AvStrokes.Line(b, 0, 6, 6, 0, 1.2f, ink, 1f);
                AvStrokes.Line(b, 6, 0, 0, -6, 1.2f, ink, 1f);
                AvStrokes.Line(b, 0, -6, -6, 0, 1.2f, ink, 1f);
            }
            AvStrokes.Line(b, 5, 5, 10, 11+lift, 1f, ink, 1f);
            AvStrokes.Line(b, 10, 11+lift, 30, 11+lift, 1f, ink, 1f);
            symbol.Commit();
        }

        public void SetRing(in ContractCard next, DynamicMap map, IMapProjection projection, Vector2 centre, float zoom)
        {
            int revision = projection?.Revision ?? 0;
            if (areaId == next.Id && areaRadius == next.Radius && areaZoom == zoom &&
                areaFactor == map.mapDisplayFactor && areaCentre == centre && areaProjection == projection && areaRevision == revision) return;
            areaId = next.Id; areaRadius = next.Radius; areaZoom = zoom; areaFactor = map.mapDisplayFactor;
            areaCentre = centre; areaProjection = projection; areaRevision = revision;
            var b = area.Buffer; b.Clear();
            if (!(next.Radius > 0f)) { area.Commit(); return; }
            Color tint = label.color;
            var ink = new Rgba(tint.r, tint.g, tint.b, next.IsOffered ? .3f : .5f);
            Vector2 previous = default;
            bool hadPrevious = false;
            // One ground circle at 128 stations; relief must never use a screen-space circle.
            for (int i = 0; i <= 128; i++)
            {
                float angle = i * (Mathf.PI * 2f / 128f);
                float x = next.X + Mathf.Cos(angle) * next.Radius, z = next.Z + Mathf.Sin(angle) * next.Radius;
                Vector2 point;
                bool valid = true;
                if (projection != null)
                {
                    valid = projection.TryProject(x, z, out float px, out float py);
                    point = new Vector2(px, py);
                }
                else point = new Vector2(x * map.mapDisplayFactor, z * map.mapDisplayFactor);
                point = (point-centre) * zoom;
                if (valid && hadPrevious && (!next.IsOffered || i % 4 < 3))
                    AvStrokes.Line(b, previous.x, previous.y, point.x, point.y, 1f, ink, 1f);
                previous = point; hadPrevious = valid;
            }
            area.Commit();
        }
    }
}
