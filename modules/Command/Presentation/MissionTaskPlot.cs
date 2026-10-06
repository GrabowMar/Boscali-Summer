using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// A passive local chart of the host-permitted task location. Native terrain and theater
    /// extents share one flat projection; the ownship line is a bearing, never a planned route.
    /// </summary>
    internal sealed class MissionTaskPlot : AvPart
    {
        private readonly AvFrame frame;
        private readonly RectTransform viewport;
        private readonly Image terrain;
        private readonly Image wash;
        private readonly AvVector vectors;
        private readonly TMP_Text title, north, caption, notice, bearingLabel, ownshipLabel, contactLabel;
        private readonly Image ownshipPlate, contactPlate;
        private SecondaryObjectiveView entry;
        private bool fresh;
        private float height = 260f, width, placedHeight;
        private Sprite chart;
        private float worldX, worldZ, selfX = float.NaN, selfZ = float.NaN, heading;
        private bool offlineChart;
        private float cropMinX, cropMinZ, cropWidth, cropHeight;

        internal bool ChartAvailable { get; private set; }
        internal bool HasPlottedContact { get; private set; }
        internal bool HasBearing { get; private set; }

        internal MissionTaskPlot(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Mission tactical plot");
            frame = AvFrame.Add(Rect, "Plot frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            title = Text(Rect, "Plot heading", AvTextRole.Micro, "TACTICAL PLOT");
            north = Text(Rect, "North", AvTextRole.Head, "N ↑", TextAlignmentOptions.TopRight);
            caption = Text(Rect, "Permitted position status", AvTextRole.Micro, "NO TASK SELECTED");
            viewport = AvLay.Child(Rect, "Native chart viewport");
            viewport.gameObject.AddComponent<RectMask2D>();
            terrain = Image(viewport, "Native mission terrain");
            wash = Image(viewport, "Chart wash");
            AvLay.Fill(wash.rectTransform);
            vectors = AvVector.Create(viewport, "Passive task chart geometry", 700);
            ownshipPlate = Image(viewport, "Ownship label plate");
            ownshipLabel = Text(ownshipPlate.rectTransform, "Ownship", AvTextRole.Micro, "OWNSHIP");
            contactPlate = Image(viewport, "Contact label plate");
            contactLabel = Text(contactPlate.rectTransform, "Task contact", AvTextRole.Micro, "TASK");
            bearingLabel = Text(viewport, "Bearing label", AvTextRole.Micro, "BEARING", TextAlignmentOptions.Center);
            notice = Text(viewport, "Unavailable chart notice", AvTextRole.ProseSmall, "", TextAlignmentOptions.Center);
            AvText.Fit(notice, true);
            Restyle();
        }

        internal float Height
        {
            get => height;
            set
            {
                float next = Mathf.Clamp(value, 140f, 420f);
                if (Mathf.Approximately(next, height)) return;
                height = next;
                Changed();
            }
        }

        internal void Set(SecondaryObjectiveView objective, bool hostFresh)
        {
            entry = objective;
            fresh = hostFresh;
            if (!offlineChart) ReadNativeChart();
            Redraw();
        }

        /// <summary>
        /// Offline capture seam: native chart inputs only. Production contact permissions,
        /// finite-coordinate checks and host freshness still decide what the plot can show.
        /// </summary>
        internal void SetOfflineChart(Sprite sprite, float worldSizeX, float worldSizeZ,
            float ownshipX, float ownshipZ, float headingDegrees)
        {
            offlineChart = true;
            chart = sprite;
            worldX = worldSizeX;
            worldZ = worldSizeZ;
            selfX = ownshipX;
            selfZ = ownshipZ;
            heading = float.IsFinite(headingDegrees) ? headingDegrees : 0f;
            Redraw();
        }

        private void ReadNativeChart()
        {
            chart = null;
            worldX = worldZ = 0f;
            selfX = selfZ = float.NaN;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            Image source = map?.mapImage?.GetComponent<Image>();
            if (source != null && source.sprite != null && ModuleServices.TryGet(out ComMapOverlay overlay))
            {
                var grid = overlay.Grid;
                var settings = NetworkSceneSingleton<LevelInfo>.i?.LoadedMapSettings;
                // ComMapOverlay starts with a default grid before the mission frame arrives.
                // An unverified default span must not place contacts over a real terrain sprite.
                if (grid != null && settings != null && ValidSpan(settings.MapSize.x) && ValidSpan(settings.MapSize.y) &&
                    Mathf.Abs(grid.WorldSizeX-settings.MapSize.x) < 1f && Mathf.Abs(grid.WorldSizeY-settings.MapSize.y) < 1f)
                {
                    chart = source.sprite;
                    worldX = grid.WorldSizeX;
                    worldZ = grid.WorldSizeY;
                }
            }
            if (GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null && !aircraft.disabled && !aircraft.HasEjected())
            {
                Vector3 global = aircraft.transform.position.ToGlobalPosition().AsVector3();
                if (float.IsFinite(global.x) && float.IsFinite(global.z)) { selfX = global.x; selfZ = global.z; }
                heading = aircraft.transform.eulerAngles.y;
            }
        }

        public override float Measure(float availableWidth) => height;

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            width = slot.W;
            placedHeight = slot.H;
            AvLay.Place(title.rectTransform, 12f, 7f, Mathf.Max(1f, width-86f), 18f);
            AvLay.Place(north.rectTransform, width-64f, 7f, 52f, 18f);
            AvLay.Place(viewport, 10f, 34f, Mathf.Max(1f, width-20f), Mathf.Max(1f, slot.H-66f));
            AvLay.Place(caption.rectTransform, 12f, slot.H-24f, Mathf.Max(1f, width-24f), 18f);
            Redraw();
        }

        public override void Restyle()
        {
            frame.Paint(AvStyleHost.FuiColor("surface", AvTheme.Surface), AvStyleHost.FuiColor("hairline", AvTheme.Hairline));
            title.color = MissionTaskGraphics.Key;
            north.color = MissionTaskGraphics.Ink;
            caption.color = MissionTaskGraphics.Dim;
            notice.color = MissionTaskGraphics.Dim;
            ownshipLabel.color = MissionTaskGraphics.Ink;
            contactLabel.color = MissionTaskGraphics.Amber;
            bearingLabel.color = MissionTaskGraphics.Dim;
            ownshipPlate.color = contactPlate.color = MissionTaskGraphics.Ground.WithAlpha(.9f);
            terrain.color = Color.white.WithAlpha(.88f);
            wash.color = MissionTaskGraphics.Ground.WithAlpha(.18f);
            Redraw();
        }

        private void Redraw()
        {
            if (width <= 20f || placedHeight <= 66f) return;
            float w = width-20f, h = placedHeight-66f;
            vectors.Buffer.Clear();
            HasPlottedContact = HasBearing = false;
            ownshipPlate.enabled = contactPlate.enabled = false;
            ownshipLabel.gameObject.SetActive(false);
            contactLabel.gameObject.SetActive(false);
            bearingLabel.gameObject.SetActive(false);
            terrain.sprite = chart;
            bool available = chart != null && ValidSpan(worldX) && ValidSpan(worldZ);
            ChartAvailable = available;
            terrain.enabled = available;
            wash.enabled = available;
            north.gameObject.SetActive(available);
            if (!available)
            {
                notice.text = "NATIVE CHART UNAVAILABLE\nTask position is not plotted.";
                notice.gameObject.SetActive(true);
                AvLay.Place(notice.rectTransform, 12f, 4f, w-24f, h-8f);
                caption.text = !fresh ? "HOST REPORT STALE / POSITION WITHHELD" : "CHART FRAME UNAVAILABLE";
                vectors.Commit();
                return;
            }

            ObjectiveContact quality = entry?.Tasking?.Contact ?? ObjectiveContact.Known;
            bool known = fresh && entry != null && entry.IsActive && entry.HasMarker &&
                quality != ObjectiveContact.Lost && quality != ObjectiveContact.Unavailable &&
                InTheater(entry.X, entry.Z);
            bool lastKnown = known && quality == ObjectiveContact.LastKnown;
            bool ownship = InTheater(selfX, selfZ);
            FitCrop(w, h, known, ownship);
            PlaceTerrain(w, h);
            DrawGrid(w, h);
            Vector2 self = Vector2.zero, contact = Vector2.zero;
            bool selfVisible = ownship && Project(selfX, selfZ, w, h, out self);
            bool plotted = known && Project(entry.X, entry.Z, w, h, out contact);
            HasPlottedContact = plotted;
            Rect targetBox = default;
            if (plotted)
            {
                float radius = float.IsFinite(entry.Radius) && entry.Radius > 0f ? entry.Radius*w/cropWidth : 0f;
                // Clip the actual operating area through the viewport; never clamp its physical radius.
                if (radius > 1f && radius < Mathf.Max(w, h)*2f)
                {
                    if (lastKnown) AvStrokes.DashedRing(vectors.Buffer, contact.x, contact.y, radius, 24, .45f, .8f,
                        MissionTaskGraphics.RgbaOf(MissionTaskGraphics.Amber.WithAlpha(.5f)));
                    else AvStrokes.Ring(vectors.Buffer, contact.x, contact.y, radius, 64, .7f,
                        MissionTaskGraphics.RgbaOf(MissionTaskGraphics.Key.WithAlpha(.38f)));
                }
                if (selfVisible)
                {
                    AvStrokes.Line(vectors.Buffer, self.x, self.y, contact.x, contact.y, 3.8f,
                        MissionTaskGraphics.RgbaOf(MissionTaskGraphics.Ground));
                    AvStrokes.DashedLine(vectors.Buffer, self.x, self.y, contact.x, contact.y, 5f, 5f, 1f,
                        MissionTaskGraphics.RgbaOf((lastKnown ? MissionTaskGraphics.Dim : MissionTaskGraphics.Key).WithAlpha(.75f)));
                    HasBearing = true;
                }
                // Separate the two identity plates away from their common bearing.
                bool right = !selfVisible || contact.x >= self.x;
                bool above = selfVisible && contact.y >= self.y;
                targetBox = LabelBox(right ? contact.x+22f : contact.x-148f,
                    h-contact.y+(above ? -34f : 14f), 126f, w, h);
                PlacePlate(contactPlate, contactLabel, lastKnown ? "LAST KNOWN / #"+entry.Id : "TASK / #"+entry.Id, targetBox);
                LabelLeader(contact, targetBox, h, 18f, MissionTaskGraphics.Amber);
                ObjectiveAsset asset = entry.Tasking?.Asset ?? ObjectiveAsset.Unknown;
                ObjectiveFamily family = entry.Tasking?.Family ?? ObjectiveFamily.Unknown;
                ObjectiveAllegiance allegiance = entry.Tasking?.Allegiance ?? ObjectiveAllegiance.Unknown;
                MissionTaskGraphics.DrawContact(vectors, contact.x, contact.y, 28f, asset, family, true, lastKnown,
                    allegiance == ObjectiveAllegiance.Hostile,
                    allegiance == ObjectiveAllegiance.Neutral || allegiance == ObjectiveAllegiance.Unknown);
            }

            if (selfVisible)
            {
                bool right = !plotted || self.x > contact.x;
                bool above = plotted && self.y > contact.y;
                Rect shipBox = LabelBox(right ? self.x+16f : self.x-94f,
                    h-self.y+(above ? -34f : 14f), 78f, w, h);
                if (plotted)
                {
                    Rect contactGlyph = new Rect(contact.x-18f, h-contact.y-16f, 36f, 34f);
                    if (shipBox.Overlaps(targetBox) || shipBox.Overlaps(contactGlyph))
                        shipBox = SeparatedOwnshipBox(self, shipBox, targetBox, contactGlyph, w, h);
                }
                PlacePlate(ownshipPlate, ownshipLabel, "OWNSHIP", shipBox);
                LabelLeader(self, shipBox, h, 11f, MissionTaskGraphics.Ink);
                MissionTaskGraphics.DrawAircraft(vectors, self.x, self.y, 17f, 90f-heading, MissionTaskGraphics.Ink);
                if (HasBearing && Vector2.Distance(self, contact) >= 100f)
                {
                    Rect label = LabelBox((self.x+contact.x)*.5f-33f, h-(self.y+contact.y)*.5f+6f, 66f, w, h);
                    if (!label.Overlaps(shipBox) && !label.Overlaps(targetBox))
                    {
                        bearingLabel.gameObject.SetActive(true);
                        AvLay.Place(bearingLabel.rectTransform, label.x, label.y, label.width, 18f);
                    }
                }
            }

            notice.gameObject.SetActive(!plotted);
            if (!plotted)
            {
                notice.text = !fresh ? "HOST REPORT STALE\nTask position is withheld." : entry == null ? "SELECT A TASK" :
                    entry.IsOffered ? "LOCATION AVAILABLE AFTER ACCEPTANCE" : !entry.IsActive ? "TASK CLOSED / NO ACTIVE FIX" : "CONTACT LOST / NO CURRENT FIX";
                AvLay.Place(notice.rectTransform, 12f, Mathf.Max(0f, h*.5f-22f), w-24f, 44f);
            }
            caption.text = PlotCaption(plotted, ownship, lastKnown);
            caption.color = !fresh || lastKnown ? MissionTaskGraphics.Amber : MissionTaskGraphics.Dim;
            vectors.Commit();
        }

        private string PlotCaption(bool plotted, bool ownship, bool lastKnown)
        {
            if (!fresh) return "HOST REPORT STALE / POSITION WITHHELD";
            if (!plotted) return entry != null && entry.IsOffered ? "OFFER / NO PERMITTED FIX" : "NO PERMITTED TASK FIX";
            string quality = lastKnown ? "LAST KNOWN" : entry.Tasking?.Contact == ObjectiveContact.Fixed ? "FIXED POSITION" : "KNOWN POSITION";
            float age = entry.Tasking?.ContactAgeSeconds ?? float.NaN;
            if (float.IsFinite(age) && age >= 0f && entry.Tasking?.Contact != ObjectiveContact.Fixed)
                quality += " / AT REPORT " + Math.Ceiling(age).ToString("0") + "s";
            if (ownship)
            {
                float distance = Vector2.Distance(new Vector2(selfX, selfZ), new Vector2(entry.X, entry.Z));
                return (distance/1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km / " +
                    (HasBearing ? "BEARING ONLY / " : "OWNSHIP OUTSIDE PLOT / ") + quality;
            }
            return quality + " / OWNSHIP UNAVAILABLE";
        }

        private void FitCrop(float w, float h, bool contact, bool ownship)
        {
            float cx = contact ? entry.X : ownship ? selfX : 0f;
            float cz = contact ? entry.Z : ownship ? selfZ : 0f;
            float neededX = contact ? Mathf.Max(8000f, float.IsFinite(entry.Radius) ? entry.Radius*3f : 0f) : worldX;
            float neededZ = contact ? Mathf.Max(8000f, float.IsFinite(entry.Radius) ? entry.Radius*3f : 0f) : worldZ;
            if (contact && ownship)
            {
                cx = (entry.X+selfX)*.5f;
                cz = (entry.Z+selfZ)*.5f;
                neededX = Mathf.Max(neededX, Mathf.Abs(entry.X-selfX)*1.6f);
                neededZ = Mathf.Max(neededZ, Mathf.Abs(entry.Z-selfZ)*1.6f);
            }
            float aspect = w/h;
            cropWidth = Mathf.Max(neededX, neededZ*aspect);
            cropHeight = cropWidth/aspect;
            if (contact)
            {
                // Keep a local, metric-correct crop on small maps. Exceeding one dimension
                // does not turn the chart into a thin whole-theater strip.
                cropWidth = Mathf.Min(cropWidth, Mathf.Min(worldX, worldZ*aspect));
                cropHeight = cropWidth/aspect;
                if (ownship && (Mathf.Abs(entry.X-selfX)*1.15f > cropWidth || Mathf.Abs(entry.Z-selfZ)*1.15f > cropHeight))
                {
                    // When both points cannot fit, retain the task fix and report ownship
                    // outside this plot rather than misclassifying the contact as lost.
                    cx = entry.X;
                    cz = entry.Z;
                }
            }
            else if (cropWidth > worldX || cropHeight > worldZ)
            {
                cropWidth = Mathf.Max(worldX, worldZ*aspect);
                cropHeight = cropWidth/aspect;
                cx = cz = 0f;
            }
            cropMinX = Mathf.Clamp(cx-cropWidth*.5f, -worldX*.5f, Mathf.Max(-worldX*.5f, worldX*.5f-cropWidth));
            cropMinZ = Mathf.Clamp(cz-cropHeight*.5f, -worldZ*.5f, Mathf.Max(-worldZ*.5f, worldZ*.5f-cropHeight));
            if (cropWidth > worldX) cropMinX = -cropWidth*.5f;
            if (cropHeight > worldZ) cropMinZ = -cropHeight*.5f;
        }

        private void PlaceTerrain(float w, float h)
        {
            float left = (-worldX*.5f-cropMinX)*w/cropWidth;
            float top = h-(worldZ*.5f-cropMinZ)*h/cropHeight;
            AvLay.Place(terrain.rectTransform, left, top, worldX*w/cropWidth, worldZ*h/cropHeight);
        }

        private void DrawGrid(float w, float h)
        {
            Rgba color = MissionTaskGraphics.RgbaOf(MissionTaskGraphics.Key.WithAlpha(.18f));
            // World-aligned kilometre multiples, coarsened to at most eight lines per axis.
            float step = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(cropWidth, cropHeight)/5f)));
            if (Mathf.Max(cropWidth, cropHeight)/step > 8f) step *= 2f;
            if (Mathf.Max(cropWidth, cropHeight)/step > 8f) step *= 2.5f;
            for (float x = Mathf.Ceil(cropMinX/step)*step; x < cropMinX+cropWidth; x += step)
            {
                float p = (x-cropMinX)*w/cropWidth;
                AvStrokes.Line(vectors.Buffer, p, 0f, p, h, .55f, color);
            }
            for (float z = Mathf.Ceil(cropMinZ/step)*step; z < cropMinZ+cropHeight; z += step)
            {
                float p = (z-cropMinZ)*h/cropHeight;
                AvStrokes.Line(vectors.Buffer, 0f, p, w, p, .55f, color);
            }
        }

        private bool Project(float x, float z, float w, float h, out Vector2 position)
        {
            position = new Vector2((x-cropMinX)*w/cropWidth, (z-cropMinZ)*h/cropHeight);
            return position.x >= 0f && position.y >= 0f && position.x <= w && position.y <= h;
        }

        private bool InTheater(float x, float z) => float.IsFinite(x) && float.IsFinite(z) &&
            Mathf.Abs(x) <= worldX*.5f && Mathf.Abs(z) <= worldZ*.5f;

        private static bool ValidSpan(float n) => float.IsFinite(n) && n > 1000f && n <= 10000000f;

        private static Image Image(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text Text(RectTransform parent, string name, AvTextRole role, string text,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            TMP_Text label = AvText.Make(parent, name, role, text, align);
            label.raycastTarget = false;
            AvText.Fit(label, false);
            return label;
        }

        private static Rect LabelBox(float x, float y, float width, float w, float h)
        {
            float actualWidth = Mathf.Min(width, w-4f);
            return new Rect(Mathf.Clamp(x, 2f, Mathf.Max(2f, w-actualWidth-2f)),
                Mathf.Clamp(y, 2f, Mathf.Max(2f, h-22f)), actualWidth, 20f);
        }

        private static Rect SeparatedOwnshipBox(Vector2 self, Rect current, Rect target, Rect targetGlyph, float w, float h)
        {
            Rect[] choices =
            {
                LabelBox(self.x-94f, h-self.y-34f, 78f, w, h),
                LabelBox(self.x+16f, h-self.y-34f, 78f, w, h),
                LabelBox(self.x-94f, h-self.y+14f, 78f, w, h),
                LabelBox(self.x+16f, h-self.y+14f, 78f, w, h)
            };
            Rect best = current;
            float score = CollisionArea(best, target)*4f + CollisionArea(best, targetGlyph)*8f;
            foreach (Rect choice in choices)
            {
                float next = CollisionArea(choice, target)*4f + CollisionArea(choice, targetGlyph)*8f;
                if (next >= score) continue;
                best = choice;
                score = next;
            }
            return best;
        }

        private static float CollisionArea(Rect a, Rect b) =>
            Mathf.Max(0f, Mathf.Min(a.xMax,b.xMax)-Mathf.Max(a.xMin,b.xMin)) *
            Mathf.Max(0f, Mathf.Min(a.yMax,b.yMax)-Mathf.Max(a.yMin,b.yMin));

        private void LabelLeader(Vector2 contact, Rect label, float h, float inset, Color color)
        {
            var end = new Vector2(Mathf.Clamp(contact.x,label.xMin+4f,label.xMax-4f),
                h-Mathf.Clamp(h-contact.y,label.yMin+2f,label.yMax-2f));
            Vector2 delta = end-contact;
            if (delta.magnitude <= inset) return;
            Vector2 start = contact+delta.normalized*inset;
            AvStrokes.Line(vectors.Buffer, start.x, start.y, end.x, end.y, .7f,
                MissionTaskGraphics.RgbaOf(color.WithAlpha(.45f)));
        }

        private static void PlacePlate(Image plate, TMP_Text label, string text, Rect box)
        {
            label.text = text;
            AvLay.Place(plate.rectTransform, box.x, box.y, box.width, box.height);
            AvLay.Place(label.rectTransform, 4f, 1f, box.width-8f, 18f);
            plate.enabled = true;
            label.gameObject.SetActive(true);
        }
    }
}
