using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The weather console's view of the tape (the pure half of <c>MetOfficeAdapter.Fill</c>): front markers as polylines along the meander, and the strip along the wind
    /// from the segments and markers within <see cref="ReachKm"/> of the camera.
    /// </summary>
    internal static class MetOfficeTapeFill
    {
        /// <summary>The strip shows this far downwind and upwind of the camera.</summary>
        public const float ReachKm = 150f;
        private const float FrontReachMetres = 260000f;

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

        /// <summary>Fills the model's fronts with the markers nearest the camera along the tape (cap <see cref="MetOfficeModel.MaxFronts"/>).</summary>
        public static void Fronts(MetOfficeModel m, WeatherTape tape, TapeFrame frame, float camXMetres, float camZMetres)
        {
            m.FrontCount = 0;
            float sCam = frame.S(camXMetres, camZMetres);
            var used = new bool[tape.Markers.Count];
            while (m.FrontCount < MetOfficeModel.MaxFronts)
            {
                int best = -1;
                float bestGap = FrontReachMetres;
                for (int i = 0; i < used.Length; i++)
                {
                    float gap = Math.Abs(tape.Markers.Items[i].S - sCam);
                    if (!used[i] && gap < bestGap) { best = i; bestGap = gap; }
                }
                if (best < 0) break;
                used[best] = true;
                Line(tape.Markers.Items[best], frame, m.Fronts[m.FrontCount++]);
            }
        }

        /// <summary>The strip: segment blocks and front ticks within <see cref="ReachKm"/> of the camera (upwind positive), the pattern's name and the role overhead.</summary>
        public static void Strip(MetOfficeModel m, WeatherTape tape, float sCam)
        {
            m.BlockCount = m.MarkCount = 0;
            int under = 0;
            for (int i = 0; i < tape.Segments.Length; i++)
            {
                TapeSegment seg = tape.Segments[i];
                if (sCam >= seg.S0 && sCam < seg.S1) under = i;
                float from = (seg.S0 - sCam) * 0.001f, to = (seg.S1 - sCam) * 0.001f;
                if (to < -ReachKm || from > ReachKm || m.BlockCount >= MetOfficeModel.MaxBlocks) continue;
                m.Blocks[m.BlockCount++] = new MetBlock
                {
                    FromKm = Math.Max(from, -ReachKm), ToKm = Math.Min(to, ReachKm), Class = seg.Class, Code = RegimeSnapshot.FromType(seg.Class).Code, Name = seg.Label,
                };
            }
            for (int i = 0; i < tape.Markers.Count && m.MarkCount < MetOfficeModel.MaxMarks; i++)
            {
                TapeMarker k = tape.Markers.Items[i];
                float at = (k.S - sCam) * 0.001f;
                if (at < -ReachKm || at > ReachKm) continue;
                m.Marks[m.MarkCount++] = new MetMark { AtKm = at, Kind = KindOf(k.Kind), Label = Short(k.Kind) };
            }
            TapeSegment now = tape.Segments[Math.Min(under, tape.Segments.Length - 1)];
            m.StoryName = now.Story >= 0 ? WeatherStories.Name(now.Story) : "Opening";
            m.SkyRole = now.Label;
        }

        private static string Short(MarkerKind kind) =>
            kind switch { MarkerKind.Warm => "WARM", MarkerKind.Squall => "SQUALL", MarkerKind.Occluded => "OCCL", _ => "COLD" };
    }
}
