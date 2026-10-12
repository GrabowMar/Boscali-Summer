using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;
using HarmonyLib;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// What is drawn over the hologram, with the vector kit the module already uses (<see cref="AvStrokes"/> into one <see cref="AvVector"/> layer) and the same view and projection
    /// as the render (<see cref="HoloView.Project"/>): the player's aircraft on a stem from the ground to its altitude, two range rings on the sea-level plane (half and full radius, the
    /// range written on the outer), a north tick, up to six airbases named on short leaders, up to 96 of the units the game's own map shows (nearest first; aircraft on stems at their
    /// altitude, ground and sea units on the surface, in the colours the game gave their icons) and the selected target in brackets with its name and range. Units and airbases are
    /// scanned four times a second into small lists of global positions; each picture only subtracts the aircraft. Buildings and dismounted pilots are left out: the game's map is full of
    /// them and the 96 would be spent on one airbase. Labels are 11 pooled text objects, written only when their words change. Nothing here is interactive.
    /// </summary>
    internal sealed partial class HoloMap
    {
        private static readonly AccessTools.FieldRef<MapIcon, Vector3> IconPosition =
            AccessTools.Field(typeof(MapIcon), "globalPosition") == null ? null
                : AccessTools.FieldRefAccess<MapIcon, Vector3>("globalPosition");

        private readonly HoloMark[] units = new HoloMark[HoloMarks.MaximumUnits];
        private readonly HoloMark[] bases = new HoloMark[HoloMarks.MaximumAirbases];
        private readonly Airbase[] basePool = new Airbase[64];
        private readonly Dictionary<Airbase, string> baseNames = new Dictionary<Airbase, string>(64);
        private readonly float[] ringEast = new float[65], ringNorth = new float[65], ringX = new float[65], ringY = new float[65];
        private int unitCount, baseCount, scanRange = -1;
        private int unitsDrawn, basesDrawn, labelsUsed;
        private string flagText = "", ringText = "", targetText = "";
        private int flagRange, ringRange, targetTenths = -1;
        private bool flagNorth;
        private Unit targetNamed;

        /// <summary>One picture's marks, then the furniture and the labels that go with them.</summary>
        private void DrawMarks(int range)
        {
            if (Time.unscaledTime >= nextScan || scanRange != range) Scan(range);
            if (flagText.Length == 0 || flagRange != range || flagNorth != NorthUp)
            {
                flagRange = range;
                flagNorth = NorthUp;
                flagText = HoloMarks.ShelfFlag(range, flagNorth);
            }
            DrawFurniture(flagText);

            AvQuadBuffer b = marks.Buffer;
            b.Clear();
            float u = unit, w = pixel, sea = field.SeaLevel;
            var ink = new Rgba(inkColour.r, inkColour.g, inkColour.b, .9f);
            var dim = new Rgba(inkColour.r, inkColour.g, inkColour.b, .5f);
            var bright = new Rgba(Mathf.Lerp(inkColour.r, 1f, .65f), Mathf.Lerp(inkColour.g, 1f, .65f), Mathf.Lerp(inkColour.b, 1f, .65f), 1f);

            // Rings on the sea-level plane: half the radius dotted, the full radius solid with its range written beside it; and north.
            DrawRing(b, radius * .5f, true, dim);
            DrawRing(b, radius, false, new Rgba(inkColour.r, inkColour.g, inkColour.b, .7f));
            if (At(0f, 0f, radius * .93f, out float x0, out float y0) && At(0f, 0f, radius, out float x1, out float y1))
                AvStrokes.Line(b, x0, y0, x1, y1, w, ink);
            if (At(0f, 0f, radius * 1.09f, out float nx, out float ny)) Label(LabelNorth, "N", nx, ny, inkColour);
            else Label(LabelNorth, "", 0f, 0f, inkColour);
            if (ringRange != range || ringText.Length == 0)
            {
                ringRange = range;
                ringText = HoloMarks.Range(radius);
            }
            HoloView.FromTable(heading, radius * .7071f, -radius * .7071f, out float re, out float rn);
            if (At(re, 0f, rn, out float rx, out float ry)) Label(LabelRing, ringText, rx + 5f * u, ry, Dim(.8f));
            else Label(LabelRing, "", 0f, 0f, inkColour);

            // The units the game's map shows: ground and sea on the surface, aircraft on stems.
            unitsDrawn = 0;
            float limit = radius * radius;
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
                    if (!At(e, top, n, out float tx, out float ty)) continue;
                    if (At(e, ground, n, out float gx, out float gy)) AvStrokes.Line(b, gx, gy, tx, ty, w, new Rgba(c.R, c.G, c.B, .4f));
                    AvStrokes.Diamond(b, tx, ty, 3f * u, w * 1.3f, c);
                }
                else
                {
                    if (!At(e, ground, n, out float sx, out float sy)) continue;
                    AvStrokes.Fill(b, sx - 1.5f * u, sy - 1.5f * u, 3f * u, 3f * u, c);
                }
                unitsDrawn++;
            }

            DrawPlayer(b, ink, bright, w, u, sea);
            DrawTarget(b, bright, w, u, sea);
            DrawBases(b, ink, w, u);
            marks.Commit();

            labelsUsed = 0;
            for (int i = 0; i < LabelCount; i++)
                if (labels[i].text.Length > 0) labelsUsed++;
        }

        private void DrawRing(AvQuadBuffer b, float ringRadius, bool dotted, Rgba colour)
        {
            HoloView.Ring(ringRadius, 64, ringEast, ringNorth);
            int count = 0;
            for (int i = 0; i <= 64; i++)
                if (At(ringEast[i], 0f, ringNorth[i], out float x, out float y))
                {
                    ringX[count] = x;
                    ringY[count] = y;
                    count++;
                }
            float feather = pixel * .8f;
            for (int i = 0; i + 1 < count; i += dotted ? 2 : 1)
                AvStrokes.Line(b, ringX[i], ringY[i], ringX[i + 1], ringY[i + 1], pixel, colour, feather);
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

        /// <summary>The first selected target, in brackets, with its name and range.</summary>
        private void DrawTarget(AvQuadBuffer b, Rgba bright, float w, float u, float sea)
        {
            Unit target = FirstTarget();
            if (target != null)
            {
                GlobalPosition at = target.GlobalPosition();
                float e = at.x - planeX, n = at.z - planeZ, flat = Mathf.Sqrt(e * e + n * n);
                if (flat <= radius)
                {
                    float ground = Ground(at.x, at.z);
                    float top = target is Aircraft ? Mathf.Max(ground, HoloView.MarkHeight(at.y - sea, radius)) : ground;
                    if (At(e, top, n, out float x, out float y))
                    {
                        AvStrokes.Bracket(b, x - 8f * u, y - 8f * u, 16f * u, 16f * u, 5f * u, w * 1.4f, bright);
                        float dy = at.y - planeY;
                        int tenths = (int)(Mathf.Sqrt(flat * flat + dy * dy) / 100f);
                        if (!ReferenceEquals(target, targetNamed) || tenths != targetTenths)
                        {
                            targetNamed = target;
                            targetTenths = tenths;
                            targetText = HoloMarks.TargetLabel(target.unitName, tenths * 100f);
                        }
                        Label(LabelTarget, targetText, x + 12f * u, y + 10f * u, new Color(bright.R, bright.G, bright.B, 1f));
                        return;
                    }
                }
            }
            Label(LabelTarget, "", 0f, 0f, inkColour);
        }

        private void DrawBases(AvQuadBuffer b, Rgba ink, float w, float u)
        {
            basesDrawn = 0;
            float limit = radius * radius;
            for (int i = 0; i < HoloMarks.MaximumAirbases; i++)
            {
                bool shown = false;
                if (i < baseCount)
                {
                    HoloMark m = bases[i];
                    float e = m.East - planeX, n = m.North - planeZ;
                    if (e * e + n * n <= limit && At(e, Ground(m.East, m.North), n, out float x, out float y))
                    {
                        AvStrokes.Diamond(b, x, y, 3.5f * u, w, ink);
                        AvStrokes.Line(b, x + 2.5f * u, y + 2.5f * u, x + 9f * u, y + 8f * u, w, ink);
                        Label(LabelBase + i, NameOf(basePool[m.Id]), x + 11f * u, y + 8f * u, inkColour);
                        basesDrawn++;
                        shown = true;
                    }
                }
                if (!shown) Label(LabelBase + i, "", 0f, 0f, inkColour);
            }
        }

        // ------------------------------------------------------------------ the scans

        /// <summary>The 96 nearest units the game's map shows and the 6 nearest airbases, as global positions; four times a second.</summary>
        private void Scan(int range)
        {
            nextScan = Time.unscaledTime + ScanInterval;
            scanRange = range;
            unitCount = 0;
            float limit = radius * radius;
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
                    });
                }
            }
            ScanBases(limit);
        }

        private void ScanBases(float limit)
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
                HoloMarks.Offer(bases, ref baseCount, new HoloMark { East = at.x, North = at.z, Range = Mathf.Sqrt(flat), Id = pool });
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

        /// <summary>A scene point (metres from the aircraft, height above the sea) on the square picture, in the root's units from its bottom-left corner.</summary>
        private bool At(float east, float height, float north, out float x, out float y)
        {
            x = y = 0f;
            if (!HoloView.Project(heading, radius, east, height, north, out float nx, out float ny)) return false;
            x = (nx * .5f + .5f) * side;
            y = (ny * .5f + .5f) * side;
            return true;
        }

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
            scanRange = -1;
            baseNames.Clear();
            for (int i = 0; i < basePool.Length; i++) basePool[i] = null;
            flagText = ringText = targetText = "";
            flagRange = ringRange = 0;
            targetTenths = -1;
            targetNamed = null;
        }
    }
}
