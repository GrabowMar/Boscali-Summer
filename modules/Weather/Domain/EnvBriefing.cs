using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>What the ENV screen says about the sky at the aircraft (spec 8.4), as text and flags the panel only has to lay out.</summary>
    internal readonly struct EnvBriefing
    {
        public readonly string SkyName, Metar, Story, Role, NextMarker;
        public readonly int SkyNumber;
        /// <summary>Distance in kilometres across the wavy line to the next front ahead, or -1 when none is.</summary>
        public readonly float NextKm;
        /// <summary>The tape does not move: the screen shows HELD instead of any forecast.</summary>
        public readonly bool Held;
        /// <summary>The aircraft is slow enough (under about 40 m/s over the ground) for a forecast at its current position to mean something.</summary>
        public readonly bool AtPosition;

        public EnvBriefing(string skyName, int skyNumber, string metar, string story, string role, string nextMarker, float nextKm, bool held, bool atPosition)
        {
            SkyName = skyName; SkyNumber = skyNumber; Metar = metar; Story = story; Role = role; NextMarker = nextMarker; NextKm = nextKm; Held = held; AtPosition = atPosition;
        }
    }

    /// <summary>
    /// The ENV briefing from the tape (spec 8.4): the sky name and number under the aircraft, the METAR line of the local layers, the story and the role of the segment it is in,
    /// and the next front ahead as a distance and a bearing along the wind (<c>COLD FRONT 62 KM W</c>: fronts come from where the wind comes from). Pure over the tape, the frame and
    /// the sampled point, so the words are tested without the game.
    /// </summary>
    internal static class EnvBriefings
    {
        public const float AtPositionMaxGroundSpeed = 40f;
        public const string NoFront = "NO FRONT AHEAD";

        public static EnvBriefing Build(WeatherTape tape, TapeFrame frame, in WeatherPoint at, float x, float z, float groundSpeed, bool moving)
        {
            float s = frame.S(x, z);
            int sky = tape.SkyIndexAt(s);
            string name = sky >= 0 && sky < SkyLibrary.Count ? SkyLibrary.Get(sky).Name : "-";
            TapeSegment seg = SegmentAt(tape, s);
            string story = seg.Story >= 0 ? WeatherStories.Name(seg.Story) : "Opening";
            return new EnvBriefing(name, sky + 1, WeatherMetar.Line(at), story, seg.Label ?? "", NextMarker(tape, frame, x, z, s, out float nextKm), nextKm,
                !moving, groundSpeed < AtPositionMaxGroundSpeed);
        }

        /// <summary>The segment the tape position is in (the first before 0, the last past the end).</summary>
        public static TapeSegment SegmentAt(WeatherTape tape, float s)
        {
            int found = 0;
            for (int i = 0; i < tape.Segments.Length; i++)
                if (s >= tape.Segments[i].S0) found = i;
            return tape.Segments[found];
        }

        /// <summary>"COLD FRONT 62 KM W": the nearest marker that has not reached the position yet, its distance across the wavy line and the compass word of the direction it comes from.</summary>
        public static string NextMarker(WeatherTape tape, TapeFrame frame, float x, float z, float s, out float km)
        {
            km = -1f;
            float slope = frame.MeanderSlope(frame.Across(x, z));
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < tape.Markers.Count; i++)
            {
                float d = tape.Markers.Distance(i, s, slope);
                if (d < 0f && -d < bestDistance) { best = i; bestDistance = -d; }
            }
            if (best < 0) return NoFront;
            km = bestDistance * 0.001f;
            float bearing = WeatherMath.VectorToHeading(frame.UX, frame.UZ);
            return MetOfficeText.FrontWord(MetOfficeTapeFill.KindOf(tape.Markers.Items[best].Kind)) + " " + MetOfficeText.Where(bestDistance * 0.001f, bearing);
        }
    }

    /// <summary>
    /// The ENV forecast from the tape (spec 8.4): the weather at one position at <c>t + offset</c> for each default offset, each read from the same tape with D at that time. A held
    /// tape returns the same sky for every offset, which is why the screen says HELD instead of drawing it.
    /// </summary>
    internal static class EnvForecast
    {
        public static void Fill(ForecastStep[] into, WeatherField scratch, WeatherKey key, float now, float halfX, float halfZ, float hourOfDay,
            int[] offsetsMinutes, float x, float z, bool moving, float? forcedRain)
        {
            for (int i = 0; i < into.Length && i < offsetsMinutes.Length; i++)
            {
                scratch.Build(key, now + offsetsMinutes[i] * 60f, halfX, halfZ, hourOfDay, 1f, moving);
                WeatherPoint p = scratch.Sample(x, z);
                into[i] = new ForecastStep(offsetsMinutes[i], p.SkyCover, p.NativeDeck,
                    forcedRain ?? Math.Max(0f, Math.Min(1f, p.RainRate / 20f)), SkyMath.ClassOf(scratch.RowAt(x, z)));
            }
        }
    }
}
