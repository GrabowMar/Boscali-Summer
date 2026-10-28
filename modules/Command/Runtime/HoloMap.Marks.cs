using System.Collections.Generic;
using System.Diagnostics;
using BoscaliSummer.Modules.Command.Domain;
using HarmonyLib;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// What is drawn over the hologram, with the vector kit the module already uses (<see cref="AvStrokes"/> into one <see cref="AvVector"/> layer) and the same camera as the render (<see cref="HoloView.Project"/>), in
    /// the frame's own units from its lower-left corner; nothing is drawn outside the frame. Both pictures carry: the player's aircraft on a stem from the ground to its altitude, a north tick on the frame's edge where
    /// north leaves it (with an N just inside), the units the game's own map shows (nearest first, up to 48 on the MINI and 96 on the BIG one; aircraft on stems at their altitude, ground and sea units on the surface, in
    /// the colours the game gave their icons), the selected target in brackets, and the airbases in view on short leaders with their names (all of them, up to six, on the BIG one; the nearest one on the MINI). The BIG one
    /// adds the target's name and range. The standing MINI also draws the camera's view wedge (two dim lines and an arc on the sea-level plane, the camera's horizontal field of view) and carries the game's own grid reference
    /// (read from the corner map's own text, written only when it changes) dim at its lower right, next to the scale bar that the furniture draws. Units and airbases are scanned four times a second out to the ground the frame
    /// can see (<c>reach</c>) into small lists of global positions (timed); each picture only subtracts the aircraft. Buildings and dismounted pilots are left out: the game's map is full of them and the cap would be
    /// spent on one airbase. Labels are 13 pooled text objects, written only when their words change. Nothing here is interactive.
    /// </summary>
    internal sealed partial class HoloMap
    {
        private const int WedgeSegments = 12;
        private const float WedgeShare = .55f;

        private static readonly AccessTools.FieldRef<MapIcon, Vector3> IconPosition =
            AccessTools.Field(typeof(MapIcon), "globalPosition") == null ? null
                : AccessTools.FieldRefAccess<MapIcon, Vector3>("globalPosition");

        // The corner map's own grid reference ("Gh98"): the game writes it into this Text every frame while it shows grid labels.
        private static readonly AccessTools.FieldRef<GridLabels, Text> GridAircraft =
            AccessTools.Field(typeof(GridLabels), "gridAircraft") == null ? null
                : AccessTools.FieldRefAccess<GridLabels, Text>("gridAircraft");

        private readonly HoloMark[] units = new HoloMark[HoloMarks.MaximumUnits];
        private readonly HoloMark[] bases = new HoloMark[HoloMarks.MaximumAirbases];
        private readonly Airbase[] basePool = new Airbase[64];
        private readonly Dictionary<Airbase, string> baseNames = new Dictionary<Airbase, string>(64);
        private readonly float[] wedgeEast = new float[WedgeSegments + 1], wedgeNorth = new float[WedgeSegments + 1], wedgeX = new float[WedgeSegments + 1], wedgeY = new float[WedgeSegments + 1];
        private int unitCount, baseCount, scanRange = -1, scanCap = -1;
        private float scanReach = -1f;
        private int unitsDrawn, basesDrawn, labelsUsed;
        private string flagText = "", targetText = "", gridRef = "";
        private int flagRange, targetTenths = -1;
        private bool flagNorth, flagMini;
        private float wedgeDegrees;
        private Unit targetNamed;

        /// <summary>One picture's marks, then the furniture and the labels that go with them.</summary>
        private void DrawMarks(int range)
        {
            bool mini = shape == HoloShape.Mini;
            int unitCap = HoloPresentation.UnitCap(shape);
            if (Time.unscaledTime >= nextScan || scanRange != range || scanCap != unitCap || scanReach != reach) Scan(range, unitCap);
            DrawFurniture(Flag(range, mini));

            AvQuadBuffer b = marks.Buffer;
            b.Clear();
            float u = unit, w = pixel, sea = field.SeaLevel;
            var ink = new Rgba(inkColour.r, inkColour.g, inkColour.b, .9f);
            var dim = new Rgba(inkColour.r, inkColour.g, inkColour.b, .5f);
            var bright = new Rgba(Mathf.Lerp(inkColour.r, 1f, .65f), Mathf.Lerp(inkColour.g, 1f, .65f), Mathf.Lerp(inkColour.b, 1f, .65f), 1f);

            DrawNorth(b, w, u, ink);
            if (mini) DrawWedge(b, w, dim);
            else wedgeDegrees = 0f;

            // The units the game's map shows: ground and sea on the surface, aircraft on stems; the ones that fall outside the frame are not drawn.
            unitsDrawn = 0;
            float limit = reach * reach, edge = 3f * u;
            for (int i = 0; i < unitCount; i++)
            {
                HoloMark m = units[i];
                float e = m.East - planeX, n = m.North - planeZ;
                if (e * e + n * n > limit) continue;
                float ground = Ground(m.East, m.North);
                var c = Unpack(m.Colour, .95f);
                if (m.Kind == HoloKind.Air)
                {
                    float top = Mathf.Max(ground, HoloView.MarkHeight(m.Height - sea, radius));
                    // A high contact can project above the tile while its ground fix is
                    // nearby. Keep its symbol in view instead of silently dropping it.
                    if ((!At(e, top, n, out float tx, out float ty) || !Inside(tx, ty, 8f * u)) &&
                        (!At(e, ground, n, out tx, out ty) || !Inside(tx, ty, 8f * u))) continue;
                    if (At(e, ground, n, out float gx, out float gy)) AvStrokes.Line(b, gx, gy, tx, ty, w, new Rgba(c.R, c.G, c.B, .4f));
                    AvStrokes.Diamond(b, tx, ty, 5f * u, w * 1.8f, c);
                    AvStrokes.Line(b, tx - 7f * u, ty, tx + 7f * u, ty, w * 1.3f, c);
                }
                else
                {
                    if (!At(e, ground, n, out float sx, out float sy) || !Inside(sx, sy, edge)) continue;
                    AvStrokes.Fill(b, sx - 1.5f * u, sy - 1.5f * u, 3f * u, 3f * u, c);
                }
                unitsDrawn++;
            }

            DrawPlayer(b, ink, bright, w, u, sea);
            DrawTarget(b, bright, w, u, sea, !mini);
            DrawBases(b, ink, w, u);
            marks.Commit();

            // The game's own grid reference, dim at the MINI's lower right (the BIG one leaves it out).
            if (mini)
            {
                gridRef = ReadGridRef();
                Label(LabelGrid, gridRef, frameW - 10f * u, 14f * u, inkColour.WithAlpha(.7f));
            }
            else
            {
                gridRef = "";
                Label(LabelGrid, "", 0f, 0f, inkColour);
            }

            labelsUsed = 0;
            for (int i = 0; i < LabelCount; i++)
                if (labels[i].text.Length > 0) labelsUsed++;
        }

        /// <summary>
        /// The north tick: where the way north (as the picture shows it from the aircraft's spot) leaves the frame, a short line pointing in with an N just inside it. The frame is a rectangle, so the tick walks round
        /// its edge as the picture turns.
        /// </summary>
        private void DrawNorth(AvQuadBuffer b, float w, float u, Rgba ink)
        {
            if (At(0f, 0f, 0f, out float ax, out float ay) && At(0f, 0f, 1000f, out float px, out float py))
            {
                float dx = px - ax, dy = py - ay, length = Mathf.Sqrt(dx * dx + dy * dy);
                if (length > 1e-3f)
                {
                    dx /= length;
                    dy /= length;
                    HoloView.EdgePoint(ax, ay, dx, dy, 0f, 0f, frameW, frameH, out float ex, out float ey);
                    AvStrokes.Line(b, ex, ey, ex - dx * 9f * u, ey - dy * 9f * u, w, ink);
                    float lx = Mathf.Clamp(ex - dx * 17f * u, 8f * u, frameW - 8f * u), ly = Mathf.Clamp(ey - dy * 17f * u, 8f * u, frameH - 8f * u);
                    Label(LabelNorth, "N", lx, ly, inkColour);
                    return;
                }
            }
            Label(LabelNorth, "", 0f, 0f, inkColour);
        }

        /// <summary>The shelf's right end: the MINI's range alone, or the BIG one's range and which way is up; rebuilt only when it changes.</summary>
        private string Flag(int range, bool mini)
        {
            if (flagText.Length == 0 || flagRange != range || flagNorth != NorthUp || flagMini != mini)
            {
                flagRange = range;
                flagNorth = NorthUp;
                flagMini = mini;
                flagText = mini ? HoloMarks.MiniFlag(range) : HoloMarks.ShelfFlag(range, flagNorth);
            }
            return flagText;
        }

        /// <summary>
        /// The corner map's own grid reference, read from the Text the game writes it into (the game's <c>GridLabels.gridAircraft</c>; the game itself switches it off with the grid labels and leaves it
        /// empty outside the map). A string the game already holds: no allocation, and the label only changes when the words do.
        /// </summary>
        private static string ReadGridRef()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            GridLabels grid = map != null ? map.gridLabels : null;
            Text text = grid != null && GridAircraft != null ? GridAircraft(grid) : null;
            return text != null && text.enabled ? text.text ?? "" : "";
        }

        /// <summary>
        /// The MINI's view wedge, like the white one on the game's own corner map: two dim lines from the aircraft's spot on the sea-level plane and a short arc closing them, spanning the camera's
        /// horizontal field of view along the way the camera looks (the nose, in a forward cockpit view; the game's wedge turns with the camera the same way).
        /// </summary>
        private void DrawWedge(AvQuadBuffer b, float w, Rgba dim)
        {
            wedgeDegrees = 0f;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera lens = cameras != null ? cameras.mainCamera : null;
            if (lens == null) return;
            float fov = HoloView.HorizontalFov(lens.fieldOfView, lens.aspect);
            if (fov <= 0f) return;
            HoloView.Wedge(lens.transform.eulerAngles.y, fov, radius * WedgeShare, WedgeSegments, wedgeEast, wedgeNorth);
            int count = 0;
            for (int i = 0; i <= WedgeSegments; i++)
                if (At(wedgeEast[i], 0f, wedgeNorth[i], out float x, out float y))
                {
                    wedgeX[count] = x;
                    wedgeY[count] = y;
                    count++;
                }
            if (count != WedgeSegments + 1 || !At(0f, 0f, 0f, out float cx, out float cy)) return;
            wedgeDegrees = Mathf.Min(HoloView.WedgeMaxDegrees, Mathf.Max(HoloView.WedgeMinDegrees, fov));
            AvStrokes.Line(b, cx, cy, wedgeX[0], wedgeY[0], w, dim);
            AvStrokes.Line(b, cx, cy, wedgeX[WedgeSegments], wedgeY[WedgeSegments], w, dim);
            AvStrokes.Polyline(b, wedgeX, wedgeY, count, false, w, dim);
        }

        /// <summary>The player's aircraft: a cross where the ground is under it, the stem up to its altitude and a chevron there pointing the way it flies on the picture.</summary>
        private void DrawPlayer(AvQuadBuffer b, Rgba ink, Rgba bright, float w, float u, float sea)
        {
            float ground = Ground(planeX, planeZ);
            float top = Mathf.Max(ground, HoloView.MarkHeight(planeY - sea, radius));
            if (!At(0f, top, 0f, out float tx, out float ty)) return;
            if (At(0f, ground, 0f, out float gx, out float gy))
            {
                AvStrokes.Line(b, gx, gy, tx, ty, w, new Rgba(ink.R, ink.G, ink.B, .6f));
                AvStrokes.Cross(b, gx, gy, 2.5f * u, w, ink);
            }
            float yaw = plane != null ? plane.transform.eulerAngles.y * Mathf.Deg2Rad : 0f;
            float angle = 90f;
            if (At(Mathf.Sin(yaw) * 1500f, top, Mathf.Cos(yaw) * 1500f, out float hx, out float hy))
                angle = Mathf.Atan2(hy - ty, hx - tx) * Mathf.Rad2Deg;
            AvStrokes.Chevron(b, tx, ty, 13f * u, angle, w * 1.6f, bright);
            AvStrokes.Fill(b, tx - 1.25f * u, ty - 1.25f * u, 2.5f * u, 2.5f * u, bright);
        }

        /// <summary>The first selected target, in brackets when it is inside the frame; the BIG picture adds its name and range.</summary>
        private void DrawTarget(AvQuadBuffer b, Rgba bright, float w, float u, float sea, bool named)
        {
            Unit target = FirstTarget();
            if (target != null)
            {
                GlobalPosition at = target.GlobalPosition();
                float e = at.x - planeX, n = at.z - planeZ, flat = Mathf.Sqrt(e * e + n * n);
                if (flat <= reach)
                {
                    float ground = Ground(at.x, at.z);
                    float top = target is Aircraft ? Mathf.Max(ground, HoloView.MarkHeight(at.y - sea, radius)) : ground;
                    if (At(e, top, n, out float x, out float y) && Inside(x, y, 8f * u))
                    {
                        AvStrokes.Bracket(b, x - 8f * u, y - 8f * u, 16f * u, 16f * u, 5f * u, w * 1.4f, bright);
                        if (!named)
                        {
                            Label(LabelTarget, "", 0f, 0f, inkColour);
                            return;
                        }
                        float dy = at.y - planeY;
                        int tenths = (int)(Mathf.Sqrt(flat * flat + dy * dy) / 100f);
                        if (!ReferenceEquals(target, targetNamed) || tenths != targetTenths)
                        {
                            targetNamed = target;
                            targetTenths = tenths;
                            targetText = HoloMarks.TargetLabel(target.unitName, tenths * 100f);
                        }
                        Label(LabelTarget, targetText, x + 12f * u, y + 10f * u, new Color(bright.R, bright.G, bright.B, 1f));
                        KeepInside(LabelTarget, x + 12f * u, y + 10f * u, u);
                        return;
                    }
                }
            }
            Label(LabelTarget, "", 0f, 0f, inkColour);
        }

        /// <summary>The airbases in the scan that are in the frame (the BIG picture names up to six, the MINI the nearest one in view), each a diamond on a short leader with its name kept inside the frame.</summary>
        private void DrawBases(AvQuadBuffer b, Rgba ink, float w, float u)
        {
            basesDrawn = 0;
            float limit = reach * reach;
            int cap = HoloPresentation.AirbaseCap(shape);
            for (int i = 0; i < HoloMarks.MaximumAirbases; i++)
            {
                bool shown = false;
                if (i < baseCount && basesDrawn < cap)
                {
                    HoloMark m = bases[i];
                    float e = m.East - planeX, n = m.North - planeZ;
                    // One field can be several airbases of one name (its runways and pads): the nearest carries the name.
                    string name = NameOf(basePool[m.Id]);
                    bool named = false;
                    for (int k = 0; k < i && !named; k++) named = labels[LabelBase + k].text == name;
                    if (!named && e * e + n * n <= limit && At(e, Ground(m.East, m.North), n, out float x, out float y) && Inside(x, y, 6f * u))
                    {
                        AvStrokes.Diamond(b, x, y, 3.5f * u, w, ink);
                        AvStrokes.Line(b, x + 2.5f * u, y + 2.5f * u, x + 9f * u, y + 8f * u, w, ink);
                        int index = LabelBase + i;
                        Label(index, name, x + 11f * u, y + 8f * u, inkColour);
                        KeepInside(index, x + 11f * u, y + 8f * u, u);
                        basesDrawn++;
                        shown = true;
                    }
                }
                if (!shown) Label(LabelBase + i, "", 0f, 0f, inkColour);
            }
        }

        /// <summary>Moves a label that was placed at (x, y) so its text stays inside the frame (a label hangs to the right of its mark: near the right edge it is pulled back, near the top edge pushed down).</summary>
        private void KeepInside(int index, float x, float y, float u)
        {
            float room = frameW - 4f * u - AvText.Width(labels[index]);
            if (x > room) x = room;
            if (y > frameH - 8f * u) y = frameH - 8f * u;
            Place(index, x, y);
        }

        // ------------------------------------------------------------------ the scans

        /// <summary>The nearest units the game's map shows (48 for the MINI, 96 for the BIG) and the nearest six airbases, out to the ground the frame can see, as global positions; four times a second, timed.</summary>
        private void Scan(int range, int unitCap)
        {
            long started = Stopwatch.GetTimestamp();
            nextScan = Time.unscaledTime + ScanInterval;
            scanRange = range;
            scanCap = unitCap;
            scanReach = reach;
            unitCount = 0;
            float limit = reach * reach;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && map.mapDisplayFactor > 0f && IconPosition != null)
            {
                float factor = map.mapDisplayFactor;
                List<MapIcon> icons = map.mapIcons;
                for (int i = 0; i < icons.Count; i++)
                {
                    UnitMapIcon icon = icons[i] as UnitMapIcon;
                    if (icon == null) continue;
                    Unit unit = icon.unit;
                    if (unit == null || unit == plane || unit is Building || unit is PilotDismounted) continue;
                    Image image = icon.iconImage;
                    if (image == null) continue;
                    // The game's map positions are global metres times its display factor, x east and y north: what the faction knows, not the true position.
                    Vector3 local = image.transform.localPosition;
                    float x = local.x / factor, z = local.y / factor, dx = x - planeX, dz = z - planeZ, flat = dx * dx + dz * dz;
                    if (flat > limit || !image.isActiveAndEnabled) continue;
                    bool air = unit is Aircraft || unit is Missile;
                    Color32 colour = image.color;
                    HoloMarks.Offer(units, ref unitCount, new HoloMark
                    {
                        East = x, North = z, Height = air ? IconPosition(icon).y / factor : 0f, Range = Mathf.Sqrt(flat),
                        Colour = (uint)(colour.r << 16 | colour.g << 8 | colour.b), Kind = air ? HoloKind.Air : HoloKind.Surface,
                    }, unitCap, prioritizeAir: true);
                }
            }
            ScanBases(limit, HoloMarks.MaximumAirbases);
            scanMs = Milliseconds(started);
            lastScanMs = scanMs;
            if (scanMs > worstScanMs) worstScanMs = scanMs;
            scans++;
        }

        private void ScanBases(float limit, int cap)
        {
            baseCount = 0;
            if (FactionRegistry.airbaseLookup == null) return;
            int pool = 0;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airbase == null || airbase.AttachedAirbase || airbase.UnitDestroyed() || pool >= basePool.Length) continue;
                GlobalPosition at = airbase.center.GlobalPosition();
                float dx = at.x - planeX, dz = at.z - planeZ, flat = dx * dx + dz * dz;
                if (flat > limit) continue;
                basePool[pool] = airbase;
                HoloMarks.Offer(bases, ref baseCount, new HoloMark { East = at.x, North = at.z, Range = Mathf.Sqrt(flat), Id = pool }, cap);
                pool++;
            }
        }

        private string NameOf(Airbase airbase)
        {
            if (airbase == null) return "";
            if (!baseNames.TryGetValue(airbase, out string name))
            {
                name = HoloMarks.AirbaseLabel(airbase.SavedAirbase != null ? airbase.SavedAirbase.DisplayName : airbase.name);
                baseNames[airbase] = name;
            }
            return name;
        }

        /// <summary>The first of the aircraft's selected targets, the one the HUD reads its range from.</summary>
        private Unit FirstTarget()
        {
            if (plane == null || plane.weaponManager == null) return null;
            List<Unit> targets = plane.weaponManager.GetTargetList();
            return targets != null && targets.Count > 0 && targets[0] != null && !targets[0].disabled ? targets[0] : null;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Metres of ground above the sea at a global position (water is the sea).</summary>
        private float Ground(float globalX, float globalZ) =>
            (field.IsLand(globalX, globalZ) ? field.Sample(globalX, globalZ) : field.SeaLevel) - field.SeaLevel;

        /// <summary>A scene point (metres from the aircraft, height above the sea) on the picture, in the root's units from the frame's bottom-left corner.</summary>
        private bool At(float east, float height, float north, out float x, out float y)
        {
            x = y = 0f;
            if (!HoloView.Project(camera, east, height, north, out float nx, out float ny)) return false;
            x = (nx * .5f + .5f) * frameW;
            y = (ny * .5f + .5f) * frameH;
            return true;
        }

        /// <summary>A point of the frame is at least <paramref name="margin"/> units in from every edge.</summary>
        private bool Inside(float x, float y, float margin) => x >= margin && x <= frameW - margin && y >= margin && y <= frameH - margin;

        private static Rgba Unpack(uint packed, float alpha) =>
            new Rgba(((packed >> 16) & 255u) / 255f, ((packed >> 8) & 255u) / 255f, (packed & 255u) / 255f, alpha);

        private void Label(int index, string text, float x, float y, Color colour)
        {
            TMPro.TextMeshProUGUI label = labels[index];
            AvText.Set(label, text);
            if (label.color != colour) label.color = colour;
            Place(index, x, y);
        }

        private void ResetMarks()
        {
            unitCount = baseCount = unitsDrawn = basesDrawn = labelsUsed = 0;
            scanRange = scanCap = -1;
            baseNames.Clear();
            for (int i = 0; i < basePool.Length; i++) basePool[i] = null;
            flagText = targetText = gridRef = "";
            flagRange = 0;
            scanReach = -1f;
            flagMini = false;
            targetTenths = -1;
            targetNamed = null;
            wedgeDegrees = 0f;
        }
    }
}
