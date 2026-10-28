using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    internal enum MetFrontKind : byte { Warm, Cold, Occluded, Squall }

    /// <summary>One cloud layer for the sky-overhead tiles. <see cref="Present"/> false draws an empty tile.</summary>
    internal struct MetLayer
    {
        public bool Present;
        public string Label;
        public float BaseMetres, Cover;
    }

    /// <summary>A frontal line as a polyline in kilometres (map frame: x east, z north, origin = theater centre); the console reads its nearest point to the camera.</summary>
    internal sealed class MetFront
    {
        public const int MaxPoints = 32;
        public MetFrontKind Kind;
        public int Count;
        public readonly float[] X = new float[MaxPoints], Z = new float[MaxPoints];
    }

    /// <summary>A coloured span of the strip along the wind, in km from the camera (negative = downwind, already passed). Code is the class code, Name the segment's role.</summary>
    internal struct MetBlock { public float FromKm, ToKm; public WeatherRegimeType Class; public string Code, Name; }

    /// <summary>A front's tick on the strip, in km from the camera.</summary>
    internal struct MetMark { public float AtKm; public MetFrontKind Kind; public string Label; }

    /// <summary>
    /// Everything the weather console shows, as plain values, refilled in place at 4 Hz (no allocation per refresh). The window and the rules read nothing else.
    /// Distances are kilometres in the map frame, headings compass degrees.
    /// </summary>
    internal sealed class MetOfficeModel
    {
        public const int MaxFronts = 4, MaxBlocks = 12, MaxMarks = 6, LayerCount = 3;

        // Seat and readiness.
        public bool Host, Captured, HasCamera;

        // Sky overhead.
        public WeatherRegimeType Class;
        public float Cover;
        /// <summary>1-47: the library sky over the camera.</summary>
        public int SkyNumber;
        public string SkyName = "", Metar = "";
        /// <summary>High, mid, low.</summary>
        public readonly MetLayer[] Layers = new MetLayer[LayerCount];
        /// <summary>0-46 when the host set a sky, -1 while the mission's weather runs.</summary>
        public int PinnedSky = -1;
        /// <summary>True while something AUTO clears is set besides a sky: a set-piece, a story, an anchor or a rain override (a mission script can set them).</summary>
        public bool Forced;

        // Wind and fronts.
        public float WindMps;
        public int WindFromDeg;
        public int FrontCount;
        public readonly MetFront[] Fronts = NewFronts();
        public float CamX, CamZ;

        // The strip along the wind: the weather pattern's name, the role of the segment overhead, the segments and the front ticks within reach.
        public string StoryName = "", SkyRole = "";
        public int BlockCount, MarkCount;
        public readonly MetBlock[] Blocks = new MetBlock[MaxBlocks];
        public readonly MetMark[] Marks = new MetMark[MaxMarks];

        public int LayoutSalt;
        public float MissionSeconds;

        private static MetFront[] NewFronts()
        {
            var fronts = new MetFront[MaxFronts];
            for (int i = 0; i < fronts.Length; i++) fronts[i] = new MetFront();
            return fronts;
        }

        /// <summary>Back to the empty state, keeping every array, so a refill never allocates.</summary>
        public void Reset()
        {
            Host = Captured = HasCamera = false;
            Class = WeatherRegimeType.Clear; Cover = 0f; SkyNumber = 0; SkyName = Metar = ""; PinnedSky = -1; Forced = false;
            Array.Clear(Layers, 0, Layers.Length);
            WindMps = 0f; WindFromDeg = 0; FrontCount = 0; CamX = CamZ = 0f;
            StoryName = SkyRole = ""; BlockCount = MarkCount = 0;
            LayoutSalt = 0; MissionSeconds = 0f;
        }

        private static float Fin(float v) => Scalar.IsFinite(v) ? v : 0f;
        private static float Unit(float v) => Scalar.IsFinite(v) ? Scalar.Clamp01(v) : 0f;

        /// <summary>Makes every number a text or the strip draws finite: NaN and infinity become 0, covers are clamped to 0-1. The adapter calls it at the end of Fill.</summary>
        public void Sanitize()
        {
            Cover = Unit(Cover); WindMps = Fin(WindMps); MissionSeconds = Fin(MissionSeconds); CamX = Fin(CamX); CamZ = Fin(CamZ);
            for (int i = 0; i < Layers.Length; i++) { Layers[i].Cover = Unit(Layers[i].Cover); Layers[i].BaseMetres = Fin(Layers[i].BaseMetres); }
            for (int i = 0; i < Blocks.Length; i++) { Blocks[i].FromKm = Fin(Blocks[i].FromKm); Blocks[i].ToKm = Fin(Blocks[i].ToKm); }
            for (int i = 0; i < Marks.Length; i++) Marks[i].AtKm = Fin(Marks[i].AtKm);
            for (int i = 0; i < Fronts.Length; i++)
                for (int p = 0; p < MetFront.MaxPoints; p++) { Fronts[i].X[p] = Fin(Fronts[i].X[p]); Fronts[i].Z[p] = Fin(Fronts[i].Z[p]); }
        }
    }

    /// <summary>The one read seam of the window.</summary>
    internal interface IWeatherChartSource
    {
        /// <summary>Overwrites <paramref name="into"/> with the world as it is now. Never throws; an unready world fills Host/Captured false.</summary>
        void Fill(MetOfficeModel into);
    }

    /// <summary>The one write seam of the window. Every method is one host request; a client or an unready world refuses it (false) and changes nothing.</summary>
    internal interface IMetOfficeCommands
    {
        /// <summary>0-46 sets that library sky for the whole theater; -1 (AUTO) hands the weather back to the mission and clears every override.</summary>
        bool PinSky(int sky);
        bool Reroll();
    }
}
