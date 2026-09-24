using BoscaliSummer.Features.Hud.Configuration;
using BoscaliSummer.Features.Hud.Domain;
using BoscaliSummer.Features.Hud.Runtime;
using BoscaliSummer.Framework.Contracts;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace BoscaliSummer.Features.Hud.Presentation
{
    /// <summary>Fixed instrument slots. Only explicit settings change geometry, never telemetry.</summary>
    internal sealed class ThirdPersonTargetBoard
    {
        private readonly HudSurface surface;
        private readonly RectTransform panel, instrumentBacking, systems, tracks, video, mark;
        private readonly HudPanel housing;
        private readonly TMP_Text condition, damageCount, failureCount, inbound, outbound, standby, markText;
        private readonly TrackRow[] shotRows = new TrackRow[6];
        private readonly RawImage feed;
        private readonly Image footer;
        private HudBounds bounds;
        public HudBounds Bounds => surface.Root.activeSelf ? bounds : default;
        public ThirdPersonTargetBoard(Transform parent)
        {
            surface = new HudSurface("Boscali / tactical dock", parent);
            panel = HudSurface.Rect("Dock", surface.Transform);
            instrumentBacking = HudSurface.Rect("Instrument backing", panel);
            housing = instrumentBacking.gameObject.AddComponent<HudPanel>(); housing.raycastTarget = false;
            systems = HudSurface.Rect("Systems strip", panel);
            condition = HudSurface.Text("Condition", systems, 14);
            damageCount = HudSurface.Text("Parts", systems, 14);
            failureCount = HudSurface.Text("Failures", systems, 14);
            tracks = HudSurface.Rect("Missile tracks", panel);
            inbound = HudSurface.Text("Inbound heading", tracks, 14);
            outbound = HudSurface.Text("Outbound heading", tracks, 14);
            for (int i = 0; i < 6; i++) shotRows[i] = new TrackRow(tracks, i);
            video = HudSurface.Rect("Camera slot", panel);
            feed = new GameObject("Native video", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            feed.transform.SetParent(video, false); feed.raycastTarget = false;
            standby = HudSurface.Text("Camera standby", video, 16); standby.alignment = TextAlignmentOptions.Center;
            mark = HudSurface.Rect("Observation", panel);
            footer = HudSurface.Line("Observation backing", mark, AvTheme.Ground.WithAlpha(.64f));
            HudSurface.Place(footer.rectTransform, -12, 0, 320, 22);
            markText = HudSurface.Text("Mark", mark, 14);
        }
        public void Present(HudSettings settings, SystemsReading status, MissileTelemetry missiles, Texture texture, string mode, string observation)
        {
            surface.Resize(HudLayout.Scale(settings.BoardScaleStep.Value));
            float backing = settings.BoardContrast.Value == 0 ? 0 : settings.BoardContrast.Value == 2 ? .9f : .28f;
            footer.color = new Color(.015f, .025f, .035f, backing);
            Rect safe = surface.Safe;
            const float width = 320;
            const float inner = width - 24;
            bool showSystems = settings.AirframeEnabled.Value, showTracks = settings.ThirdPersonShotsEnabled.Value;
            bool showVideo = settings.ThirdPersonCameraEnabled.Value, showMark = settings.MarkEnabled.Value;
            // Reserve maximum bounds for placement/collision; only occupied rows get a backing.
            // The camera and the dock anchor do not move when threats enter or leave.
            float height = (showSystems ? 44 : 0) + (showTracks ? 110 : 0) + (showVideo ? 180 : 0) + (showMark ? 22 : 0);
            int trackRows = Mathf.Max(missiles.Inbound, missiles.Outbound);
            bool detailedSystems = !status.Valid || status.Affected || !status.FaultsAvailable;
            float systemsHeight = showSystems ? (detailedSystems ? 44 : 24) : 0;
            float tracksHeight = showTracks ? 20 + 30 * trackRows : 0;
            float instrumentBase = showVideo ? 180 : 0;
            if ((!showSystems && !showTracks && !showVideo && !showMark) || safe.width < width + 32 || safe.height < height + 32) { Hide(); return; }
            int corner = settings.BoardCorner.Value;
            bool left = corner == 1 || corner == 3;
            HudBounds place = TargetBoardLayout.Place(new HudBounds { X = safe.x, Y = safe.y, Width = safe.width, Height = safe.height },
                width, height, corner, settings.BoardInsetX.Value, settings.BoardInsetY.Value, 160 / surface.Scale);
            if (!place.Visible) { Hide(); return; }
            float x = place.X, y = place.Y;
            HudSurface.Place(panel, x, y, width, height); housing.Style(settings.BoardContrast.Value, left, false);
            HudSurface.Place(instrumentBacking, 0, instrumentBase, width, systemsHeight + tracksHeight);
            instrumentBacking.gameObject.SetActive(showSystems || showTracks);
            bounds = new HudBounds { X = x * surface.Scale, Y = y * surface.Scale, Width = width * surface.Scale, Height = height * surface.Scale };
            systems.gameObject.SetActive(showSystems); tracks.gameObject.SetActive(showTracks);
            video.gameObject.SetActive(showVideo); mark.gameObject.SetActive(showMark && !string.IsNullOrEmpty(observation));
            HudSurface.Place(systems, 12, instrumentBase + tracksHeight, inner, systemsHeight);
            HudSurface.Place(tracks, 12, instrumentBase, inner, tracksHeight);
            HudSurface.Place(video, 0, 0, width, 180);
            HudSurface.Place(mark, 12, instrumentBase + systemsHeight + tracksHeight, inner, 22);
            Color health = status.Detached > 0 || status.Failures > 0 ? HudSurface.Tone(HudTone.Warning)
                : status.Damaged > 0 ? HudSurface.Tone(HudTone.Caution) : HudSurface.Ink;
            HudSurface.Place(condition.rectTransform, 0, detailedSystems ? 22 : 2, inner, 20);
            HudSurface.Place(damageCount.rectTransform, 0, 2, 164, 20);
            HudSurface.Place(failureCount.rectTransform, 174, 2, 122, 20);
            HudSurface.Write(condition, !status.Valid ? "SYSTEMS  --" : status.Affected ? "SYSTEMS  CHECK" : status.FaultsAvailable ? "SYSTEMS  OK" : "AIRFRAME  OK / FAULTS --", health);
            HudSurface.Write(damageCount, !status.Valid ? "PARTS  --" : "DMG " + status.Damaged.ToString("00") + "   LOST " + status.Detached.ToString("00"), health);
            HudSurface.Write(failureCount, !status.FaultsAvailable ? "FAULTS  --" : "FAULT " + status.Failures.ToString("00"), health);
            damageCount.gameObject.SetActive(detailedSystems); failureCount.gameObject.SetActive(detailedSystems);
            HudSurface.Place(inbound.rectTransform, 0, tracksHeight - 20, 142, 20);
            HudSurface.Place(outbound.rectTransform, 154, tracksHeight - 20, 142, 20);
            HudSurface.Write(inbound, "IN " + (missiles.Inbound == 0 ? "--" : missiles.Inbound.ToString("00")), missiles.Inbound > 0 ? HudSurface.Tone(HudTone.Warning) : HudSurface.Ink);
            HudSurface.Write(outbound, "OUT " + (missiles.Outbound == 0 ? "--" : missiles.Outbound.ToString("00")), HudSurface.Ink);
            int inc = 0, outgoing = 0;
            for (int i = 0; i < 6; i++)
            { shotRows[i].Clear(); HudSurface.Place(shotRows[i].Rect, i < 3 ? 0 : 154, (trackRows - 1 - i % 3) * 30, 142, 30); }
            foreach (MissileTelemetry.ShotEntry entry in missiles.Shown)
            {
                int slot = entry.Outbound ? 3 + outgoing++ : inc++;
                shotRows[slot].Present(entry);
            }
            // Borrow the native texture. Preserve its aspect and clear immediately when unavailable.
            feed.texture = showVideo ? texture : null;
            feed.enabled = feed.texture != null;
            float pictureWidth = width, pictureHeight = 180;
            if (texture != null && texture.width > 0 && texture.height > 0)
            {
                float fit = Mathf.Min(width / texture.width, 180f / texture.height);
                pictureWidth = texture.width * fit; pictureHeight = texture.height * fit;
            }
            // Full-width video slot, no surrounding border, backing or padded camera frame.
            HudSurface.Place(feed.rectTransform, (width - pictureWidth) / 2, (180 - pictureHeight) / 2, pictureWidth, pictureHeight);
            HudSurface.Place(standby.rectTransform, 12, 54, inner, 68);
            standby.gameObject.SetActive(texture == null);
            HudSurface.Write(standby, "CAMERA STANDBY", HudSurface.Ink.WithAlpha(.75f));
            HudSurface.Place(markText.rectTransform, 0, 0, inner, 22);
            HudSurface.Write(markText, observation ?? "", HudSurface.Ink);
            surface.Group.alpha = HudLayout.Opacity(settings.BoardOpacityStep.Value); surface.Show(true);
        }
        // Fixed 3+3 lanes. The line shows remaining first-observed range, not a probability or timer.
        private sealed class TrackRow
        {
            public readonly RectTransform Rect;
            private readonly TMP_Text seeker, range, eta;
            private readonly Image line, origin, tip;
            public TrackRow(Transform parent, int index)
            {
                Rect = HudSurface.Rect("Track " + index, parent);
                seeker = HudSurface.Text("Seeker", Rect, 14);
                range = HudSurface.Text("Range", Rect, 14);
                eta = HudSurface.Text("Closure ETA", Rect, 14); eta.alignment = TextAlignmentOptions.MidlineRight;
                line = HudSurface.Line("Approach line", Rect, HudSurface.Ink);
                origin = HudSurface.Line("Endpoint", Rect, HudSurface.Ink);
                tip = HudSurface.Line("Missile", Rect, HudSurface.Ink); tip.rectTransform.localEulerAngles = new Vector3(0,0,45);
                HudSurface.Place(seeker.rectTransform,0,12,42,18);
                HudSurface.Place(range.rectTransform,42,12,58,18);
                HudSurface.Place(eta.rectTransform,100,12,42,18);
                HudSurface.Place(line.rectTransform,3,6,136,1);
            }
            public void Clear() => Rect.gameObject.SetActive(false);
            public void Present(MissileTelemetry.ShotEntry entry)
            {
                Rect.gameObject.SetActive(true);
                Color ink = entry.Outbound ? HudSurface.Ink : HudSurface.Tone(HudTone.Caution);
                HudSurface.Place(seeker.rectTransform,0,12,42,18);
                HudSurface.Write(seeker,entry.Seeker,ink);
                HudSurface.Write(range,entry.RangeText,ink);
                HudSurface.Write(eta,ShotCopy.EtaText(entry.Track.Eta),ink);
                line.gameObject.SetActive(true); origin.gameObject.SetActive(true); tip.gameObject.SetActive(true);
                line.color = ink.WithAlpha(.4f); origin.color = tip.color = ink;
                float remaining = Mathf.Clamp01(entry.Track.Fraction);
                HudSurface.Place(origin.rectTransform,entry.Outbound ? 137 : 1,4,4,5);
                HudSurface.Place(tip.rectTransform,3 + 132 * (entry.Outbound ? 1 - remaining : remaining),4,4,4);
            }
        }
        public void Hide() { feed.texture = null; surface.Show(false); bounds = default; }
        public void Destroy() { Hide(); surface.Destroy(); }
    }
}
