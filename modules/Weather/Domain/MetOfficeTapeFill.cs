using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The Met Office window's view of the tape (the pure half of <c>MetOfficeAdapter.Fill</c>, replacing the legacy MetFrontLine): front markers as polylines along the
    /// meander with their kind, distance and ETA, and the STORY strip from the segments and markers around the camera. A held tape has no ETA (STATIC); the strip then
    /// counts minutes at the tape's own speed.
    /// </summary>
    internal static class MetOfficeTapeFill
    {
        private const float WindowMin = 93f, ReachMetres = 260000f;

        public static MetFrontKind KindOf(MarkerKind kind) => kind switch
        {
            MarkerKind.Warm => MetFrontKind.Warm,
            MarkerKind.Squall => MetFrontKind.Squall,
            MarkerKind.Occluded => MetFrontKind.Occluded,
            _ => MetFrontKind.Cold,
        };

        /// <summary>The line of one marker as a polyline in km (map frame: x east, z north, origin = theater centre): the points with s(x, z) = s_k, so every point has signed distance 0.</summary>
        public static void Line(TapeMarker marker, TapeFrame frame, MetFront into)
        {
            into.Kind = KindOf(marker.Kind);
            into.Strength = marker.Strength;
            into.DirX = -frame.UX; into.DirZ = -frame.UZ;      // the tape slides downwind, so the line travels with the wind
            into.Count = MetFront.MaxPoints;
            float step = 2f * MetChartMath.RangeKm / (MetFront.MaxPoints - 1);
            for (int i = 0; i < into.Count; i++)
            {
                float across = -MetChartMath.RangeKm + i * step;                       // km along w
                float along = marker.S - frame.Offset - frame.Meander(across * 1000f); // metres along u
                into.X[i] = (frame.UX * along - frame.UZ * across * 1000f) * 0.001f;
                into.Z[i] = (frame.UZ * along + frame.UX * across * 1000f) * 0.001f;
            }
        }

        /// <summary>Fills the model's fronts with the markers nearest the camera along the tape (cap <see cref="MetOfficeModel.MaxFronts"/>). ETA is minutes to the line at
        /// <paramref name="speed"/> when the tape <paramref name="moving"/>, NaN (STATIC) otherwise or once the line has passed.</summary>
        public static void Fronts(MetOfficeModel m, WeatherTape tape, TapeFrame frame, float camXMetres, float camZMetres, float speed, bool moving)
        {
            m.FrontCount = 0;
            float sCam = frame.S(camXMetres, camZMetres);
            var used = new bool[tape.Markers.Count];
            while (m.FrontCount < MetOfficeModel.MaxFronts)
            {
                int best = -1;
                float bestGap = ReachMetres;
                for (int i = 0; i < used.Length; i++)
                {
                    float gap = Math.Abs(tape.Markers.Items[i].S - sCam);
                    if (!used[i] && gap < bestGap) { best = i; bestGap = gap; }
                }
                if (best < 0) break;
                used[best] = true;
                MetFront f = m.Fronts[m.FrontCount++];
                Line(tape.Markers.Items[best], frame, f);
                float ahead = tape.Markers.Items[best].S - sCam;
                f.EtaMinutes = moving && speed > 0.5f && ahead > 0f ? ahead / speed / 60f : float.NaN;
            }
        }

        /// <summary>The STORY card: role blocks within 93 min of NOW, marker ticks, nine pips, the story name and the step label.</summary>
        public static void Story(MetOfficeModel m, WeatherTape tape, float sCam, float speed, bool moving)
        {
            float perMin = Math.Max(1f, speed) * 60f;           // metres per minute
            m.HasStory = true;
            m.BlockCount = m.MarkCount = 0;
            Array.Clear(m.Segments, 0, m.Segments.Length);
            int pips = 0, under = 0;
            for (int i = 0; i < tape.Segments.Length; i++)
            {
                TapeSegment seg = tape.Segments[i];
                if (sCam >= seg.S0 && sCam < seg.S1) under = i;
                float from = (seg.S0 - sCam) / perMin, to = (seg.S1 - sCam) / perMin;
                if (to < -WindowMin || from > WindowMin) continue;
                if (m.BlockCount < MetOfficeModel.MaxBlocks)
                    m.Blocks[m.BlockCount++] = new MetBlock { FromMin = Math.Max(from, -WindowMin), ToMin = Math.Min(to, WindowMin), Class = seg.Class, Label = RegimeSnapshot.FromType(seg.Class).Code };
                if (pips < m.Segments.Length) m.Segments[pips++] = new MetSegment { Present = true, Label = seg.Label, Class = seg.Class };
            }
            for (int i = 0; i < tape.Markers.Count && m.MarkCount < MetOfficeModel.MaxMarks; i++)
            {
                TapeMarker k = tape.Markers.Items[i];
                float at = (k.S - sCam) / perMin;
                if (at < -WindowMin || at > WindowMin) continue;
                m.Marks[m.MarkCount++] = new MetMark { AtMin = at, Kind = KindOf(k.Kind), Label = Short(k.Kind) };
            }
            TapeSegment now = tape.Segments[Math.Min(under, tape.Segments.Length - 1)];
            m.StoryIndex = now.Story;
            m.StoryName = now.Story >= 0 ? WeatherStories.Name(now.Story) : "Opening";
            m.SkyRole = now.Label;
            int next = Math.Min(under + 1, tape.Segments.Length - 1);
            float eta = (tape.Segments[next].S0 - sCam) / perMin;
            m.StripNote = next == under ? "END OF TAPE" : !moving ? "HELD" : "NEXT " + tape.Segments[next].Label + " IN " + (int)Math.Max(0f, eta) + "'";
        }

        private static string Short(MarkerKind kind) =>
            kind switch { MarkerKind.Warm => "WARM", MarkerKind.Squall => "SQUALL", MarkerKind.Occluded => "OCCL", _ => "COLD" };
    }
}
