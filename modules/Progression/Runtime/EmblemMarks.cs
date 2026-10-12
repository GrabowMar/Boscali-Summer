using System;
using System.Collections.Generic;
using System.Globalization;

namespace BoscaliSummer.Modules.Progression.Runtime
{
    /// <summary>
    /// The insignia's centre marks, drawn as paths and read back as signed distances for <see cref="EmblemRaster"/>.
    /// A mark is a list of pieces separated by ';', laid down in order on a -100..100 sheet, y up:
    /// '+' adds ink, '-' cuts it away (detail lines, eyes), '=n,scale,x,y' lays mark n in at a size and place.
    /// After the sign: '|' draws the piece on both sides of the centre line (a half outline must then overshoot
    /// the line to x = -6), '!' flips it, '@deg' turns it anticlockwise, '~w' makes it a line of half-width w
    /// instead of a filled outline. Path letters: M L H V C Q Z as in SVG (absolute only), O x,y,r a circle,
    /// E x,y,rx,ry an ellipse, P a closed curve through the points that follow, R n,r0,r1,half,phase n rays.
    /// Filled pieces that should join must overlap: two that only touch leave a hairline between them.
    /// Order is the stored encoding, as in <see cref="EmblemDesign.ChargeNames"/>: append only.
    /// </summary>
    internal static class EmblemMarks
    {
        private const float Far = 1e6f;

        /// <summary>Beyond this from a piece's bounds only "outside" matters: wider than any keyline.</summary>
        private const float Reach = 0.14f;

        private static readonly string[] Source =
        {
            // 0 STAR
            "+ M0,98 L22.9,31.6 L93.2,30.3 L37.1,-12.1 L57.6,-79.3 L0,-39 L-57.6,-79.3 L-37.1,-12.1 L-93.2,30.3 L-22.9,31.6 Z;" +
            "-~1.7 M0,0 L0,82 M0,0 L78,25 M0,0 L48,-66 M0,0 L-48,-66 M0,0 L-78,25",
            // 1 BOLT
            "+ M34,98 L-58,4 L-12,4 L-42,-98 L62,24 L14,24 Z",
            // 2 WINGS
            "+| M10,46 L98,68 L93,38 L80,33 L84,12 L66,8 L69,-10 L50,-14 L52,-32 L30,-36 L10,-26 Z;" +
            "-|~1.8 M24,27 L82,41 M24,9 L67,17 M24,-9 L52,-4;" +
            "+ M-21,58 L21,58 L21,-10 Q21,-40 0,-58 Q-21,-40 -21,-10 Z;" +
            "- M0,34 L5.6,17 L23,17 L9,6 L14,-11 L0,0 L-14,-11 L-9,6 L-23,17 L-5.6,17 Z",
            // 3 CROSS
            "+ M0,100 L18,80 L18,18 L80,18 L100,0 L80,-18 L18,-18 L18,-80 L0,-100 L-18,-80 L-18,-18 L-80,-18 L-100,0 L-80,18 L-18,18 L-18,80 Z;" +
            "-~1.7 M0,84 L0,30 M0,-84 L0,-30 M84,0 L30,0 M-84,0 L-30,0; - M0,12 L12,0 L0,-12 L-12,0 Z",
            // 4 CHEVRONS
            "+ M0,98 L88,40 L88,14 L0,72 L-88,14 L-88,40 Z M0,42 L88,-16 L88,-42 L0,16 L-88,-42 L-88,-16 Z M0,-14 L88,-72 L88,-98 L0,-40 L-88,-98 L-88,-72 Z",
            // 5 DART
            "+| M-6,100 L0,100 L7,70 L12,30 L20,10 L95,-50 L95,-66 L24,-62 L22,-74 L42,-86 L42,-96 L8,-90 L0,-96 L-6,-96 Z;" +
            "- M0,78 C7,68 7,52 5,42 L-5,42 C-7,52 -7,68 0,78 Z;" +
            "-|~1.5 M25,-3 L90,-55 M12,-64 L12,-90 M12,27 L19,8",
            // 6 SWORD
            "+ M0,100 L20,70 L15,-30 L-15,-30 L-20,70 Z; -~2 M0,70 L0,-20;" +
            "+ M-60,-24 Q-64,-44 -48,-48 L48,-48 Q64,-44 60,-24 Q44,-34 16,-32 L-16,-32 Q-44,-34 -60,-24 Z;" +
            "+ M-9,-46 L9,-46 L8,-82 L-8,-82 Z; -~1.5 M-9,-56 L9,-53 M-9,-66 L9,-63 M-9,-76 L9,-73; + M0,-76 L13,-88 L0,-100 L-13,-88 Z",
            // 7 SPADE
            "+ M0,98 C30,60 78,36 78,-4 C78,-36 44,-50 16,-30 L28,-96 L-28,-96 L-16,-30 C-44,-50 -78,-36 -78,-4 C-78,36 -30,60 0,98 Z;" +
            "-|~2 M0,64 C18,42 52,24 52,-4 C52,-20 36,-28 22,-18",
            // 8 SKULL
            "+| M-6,98 L0,98 C38,98 68,78 70,42 C71,24 66,12 62,2 L66,-14 L56,-30 L48,-34 L46,-52 L40,-78 L22,-96 L-6,-98 Z;" +
            "-| M8,8 L54,28 C60,14 54,-4 42,-10 C28,-14 12,-6 8,8 Z;" +
            "-| M-6,-14 L0,-14 C8,-24 12,-34 9,-42 L-6,-38 Z;" +
            "-|~1.6 M-6,-62 L38,-60; -|~1.5 M0,-51 L0,-74 M10,-51 L10,-74 M20,-51 L20,-73 M30,-52 L30,-71; -|~1.4 M-6,-50 L36,-50 M-6,-75 L34,-73;" +
            "-|~1.5 M50,-34 L40,-46; -~1.4 M20,96 L26,80 L18,70 L24,58",
            // 9 CRESCENT
            "+ O-8,0,90; - O24,10,74",
            // 10 SUN
            "+ O0,0,40; + R8,47,98,13,0; + R8,47,74,9,22.5; -~2 O0,0,28",
            // 11 TRIDENT
            "+ M0,100 L18,62 L8,62 L8,-98 L-8,-98 L-8,62 L-18,62 Z; +|~7.5 M0,10 C36,10 50,30 50,62;" +
            "+| M50,98 L68,58 L32,58 Z; + M-22,20 L22,20 L22,2 L-22,2 Z; -~1.5 M-8,-40 L8,-40 M-8,-52 L8,-52 M-8,-64 L8,-64",
            // 12 CROWN
            "+ M-70,-42 L-86,50 L-42,6 L0,74 L42,6 L86,50 L70,-42 Z; + M-72,-50 L72,-50 L72,-72 L-72,-72 Z;" +
            "- M0,-54 L8,-61 L0,-68 L-8,-61 Z; -| M38,-54 L46,-61 L38,-68 L30,-61 Z;" +
            "-~1.7 M-58,-32 L-68,22 L-42,-10 L0,50 L42,-10 L68,22 L58,-32; + O0,80,7; +| O86,56,7",
            // 13 ARROW
            "+ M-7,60 L7,60 L7,-76 L-7,-76 Z; + M0,100 L42,30 L0,48 L-42,30 Z;" +
            "+| M4,-34 L38,-56 L38,-98 L4,-76 Z; -|~1.7 M10,-50 L38,-68 M10,-63 L38,-81",
            // 14 RETICLE
            "+~6.5 O0,0,62; +~6 M0,98 L0,40 M0,-98 L0,-40 M98,0 L40,0 M-98,0 L-40,0; + O0,0,9;" +
            "+~3.5 M52,52 L64,64 M-52,52 L-64,64 M52,-52 L64,-64 M-52,-52 L-64,-64",
            // 15 ANCHOR
            "+~8 M0,62 L0,-84; +~6.5 M-38,44 L38,44; +~6 O0,80,14; +~8.5 M-62,-20 C-58,-70 -20,-86 0,-86 C20,-86 58,-70 62,-20;" +
            "+| M62,12 L90,-32 L40,-28 Z; + M-11,-78 L11,-78 L0,-100 Z",
            // 16 EAGLE
            "+| M-6,40 L0,40 L10,30 L24,44 C40,70 64,92 98,98 L90,76 L96,66 L82,54 L90,42 L74,32 L82,20 L64,12 L70,0 L52,-6 L56,-18 L36,-18 L24,-10 L18,-36 L34,-50 L40,-70 L28,-62 L22,-78 L14,-60 L12,-70 L26,-98 L8,-88 L0,-98 L-6,-98 Z;" +
            "-|~1.6 M32,42 C50,62 70,78 86,82 M32,29 C46,44 62,52 78,56 M32,15 C42,26 56,32 68,33 M30,1 C38,9 48,11 58,10;" +
            "+ M-14,30 C-22,54 -10,80 14,78 C30,74 32,56 20,46 L14,26 Z; + M-14,70 L-46,56 L-32,51 L-38,40 L-13,47 Z; - M-4,68 L10,70 L3,61 Z;" +
            "-~1.5 M-9,12 L0,3 L9,12 M-9,-4 L0,-13 L9,-4 M-9,-20 L0,-29 L9,-20 M-9,-36 L0,-45 L9,-36",
            // 17 WOLF
            "+| M-6,44 L0,44 L18,54 L40,98 L58,56 L70,40 L64,30 L86,16 L72,6 L92,-14 L68,-20 L78,-42 L52,-40 L44,-62 L28,-66 L22,-90 L0,-98 L-6,-98 Z;" +
            "-| M38,82 L48,56 L28,58 Z; -| M12,14 L46,30 L40,10 L24,4 Z;" +
            "- M-14,-34 L14,-34 L8,-48 L0,-54 L-8,-48 Z; -| M-6,-64 L26,-58 L20,-72 L14,-66 L8,-78 L-6,-78 Z;" +
            "-|~1.5 M8,-2 L10,-28 M56,22 L70,12 M58,-4 L74,-16",
            // 18 BULL
            "+| M-6,34 L0,34 C10,40 22,40 30,36 C46,44 60,50 68,66 C72,78 72,90 68,100 C90,84 96,60 86,40 C78,24 62,16 48,16 " +
            "L60,6 L84,2 L62,-14 L44,-10 C44,-30 38,-50 30,-62 C36,-70 36,-84 26,-90 L0,-94 L-6,-94 Z;" +
            "-| M12,6 L36,16 L32,-2 L18,-6 Z; -| E14,-76,7,5; -|~1.5 M6,26 C10,10 8,-10 4,-30 M58,48 L50,38 M70,62 L78,54",
            // 19 AXES
            "+@-35~5.5 M0,-96 L0,96; +@-35 M2,84 C24,90 44,84 58,96 C70,70 70,40 58,14 C44,26 24,22 2,26 Z; +@-35 M-2,68 L-28,56 L-2,44 Z;" +
            "+!@-35~5.5 M0,-96 L0,96; +!@-35 M2,84 C24,90 44,84 58,96 C70,70 70,40 58,14 C44,26 24,22 2,26 Z; +!@-35 M-2,68 L-28,56 L-2,44 Z;" +
            "-@-35~1.7 M50,84 C60,66 60,44 50,26; -!@-35~1.7 M50,84 C60,66 60,44 50,26",
            // 20 JOLLY ROGER
            "+~6.5 M-76,-82 L76,-22; + O79,-11,9 O86,-28,9 O-79,-93,9 O-86,-76,9;" +
            "+!~6.5 M-76,-82 L76,-22; +! O79,-11,9 O86,-28,9 O-79,-93,9 O-86,-76,9; =8,0.62,0,38",
            // 21 SHARK
            "+ M-98,10 C-70,30 -40,36 -14,36 L10,80 C10,60 16,44 26,34 C50,30 66,22 76,14 L98,60 C88,34 86,14 88,0 L96,-36 C80,-22 70,-12 62,-8 " +
            "C44,-22 28,-28 10,-30 L-10,-66 L-34,-28 L-56,-26 C-70,-24 -78,-22 -82,-18 L-54,-4 L-94,0 Z;" +
            "+ M-88,1 L-84,-8 L-80,0 Z M-78,0 L-74,-8 L-70,-1 Z M-68,-1 L-64,-8 L-60,-2 Z;" +
            "- O-66,16,4.5; -~1.5 M-38,20 C-42,10 -42,0 -38,-10 M-30,20 C-34,10 -34,0 -30,-10 M-22,20 C-26,10 -26,0 -22,-10 M-50,-12 C-20,-18 30,-14 70,4",
            // 22 BOMB
            "+ M0,-98 C26,-80 38,-50 38,-16 C38,14 28,40 14,58 L-14,58 C-28,40 -38,14 -38,-16 C-38,-50 -26,-80 0,-98 Z;" +
            "+ M-10,54 L10,54 L10,80 L-10,80 Z; +| M6,60 L40,74 L40,98 L6,86 Z; + M-3,60 L3,60 L3,98 L-3,98 Z;" +
            "-~1.8 M-38,-2 L38,-2 M-33,26 L33,26 M-17,-72 L17,-72; -~1.6 M-25,-52 C-29,-34 -29,-18 -27,-8",
            // 23 COOL S
            "+~6.5 M-36,58 L-36,20 M0,58 L0,20 M36,58 L36,20 M-36,-20 L-36,-58 M0,-20 L0,-58 M36,-20 L36,-58 " +
            "M-36,58 L0,94 L36,58 M-36,-58 L0,-94 L36,-58 M-36,20 L0,-20 M0,20 L36,-20 M36,20 L18,0 M-36,-20 L-18,0",
            // 24 LOSS
            "+~6.5 M-50,20 L-50,80 M34,20 L34,80 M66,20 L66,62 M-66,-80 L-66,-20 M-34,-80 L-34,-20 M30,-80 L30,-20 M50,-74 L84,-74;" +
            "+~1.6 M0,-96 L0,96 M-96,0 L96,0",
            // 25 SHADES
            "+ M-98,42 L98,42 L98,30 L-98,30 Z; +| M12,34 L92,34 L92,2 L84,2 L84,-12 L74,-12 L74,-26 L30,-26 L30,-12 L20,-12 L20,2 L12,2 Z;" +
            "-| M24,22 L38,22 L38,15 L24,15 Z M38,15 L52,15 L52,8 L38,8 Z",
            // 26 DUCK
            "+ M-30,8 C-52,-6 -62,-34 -50,-60 C-38,-86 0,-92 32,-90 C68,-86 92,-58 96,-6 C82,-18 66,-18 50,-8 C32,4 10,10 -6,14 Z; + O-24,40,36;" +
            "+ M-52,52 C-74,54 -92,46 -98,36 C-86,28 -70,26 -52,28 Z; -~1.5 M-96,37 L-58,39; - O-34,52,6;" +
            "-~2.2 M2,-26 C26,-16 54,-16 72,-20 C60,-52 22,-58 2,-26 Z",
            // 27 MUSHROOM
            "+ M-80,22 C-96,40 -84,66 -62,66 C-56,90 -20,100 0,90 C20,100 56,90 62,66 C84,66 96,40 80,22 C60,10 30,8 22,8 L-22,8 C-30,8 -60,10 -80,22 Z;" +
            "+ M-22,12 C-16,-10 -14,-30 -18,-50 C-22,-66 -40,-80 -64,-88 L-64,-98 L64,-98 L64,-88 C40,-80 22,-66 18,-50 C14,-30 16,-10 22,12 Z;" +
            "+ E0,-12,44,9; -~1.8 M-70,30 C-40,40 40,40 70,30; -~1.6 M-7,-2 C-4,-30 -6,-50 -14,-68 M8,-2 C6,-30 8,-50 14,-68",
            // 28 STONKS
            "+~8 M-92,-40 L-34,14 L2,-20 L66,48; + M98,80 L48,66 L84,30 Z;" +
            "+ M-90,-98 L-72,-98 L-72,-76 L-90,-76 Z M-58,-98 L-40,-98 L-40,-62 L-58,-62 Z M-26,-98 L-8,-98 L-8,-70 L-26,-70 Z " +
            "M6,-98 L24,-98 L24,-48 L6,-48 Z M38,-98 L56,-98 L56,-26 L38,-26 Z M70,-98 L88,-98 L88,-4 L70,-4 Z",
            // 29 PINUP
            "*0",
            // 30 BOMBSHELL
            "*1",
        };

        private sealed class Piece
        {
            public bool Add = true, Mirror, Flip, Closed = true;
            public float Half, Cos = 1f, Sin, MinX = Far, MinY = Far, MaxX = -Far, MaxY = -Far;
            public float[] Edges;       // x0, y0, x1, y1 per edge
            public int Include = -1;
            public float Scale, ShiftX, ShiftY;
        }

        private static readonly Piece[][] Parsed = new Piece[Source.Length][];

        public static int Count => Source.Length;

        // ---- Traced nose art ----------------------------------------------------------------------
        // A mark written "*n" is picture n of emblem-art.bin (tools/Build-EmblemArt.py): three planes of
        // signed distance, so a traced drawing takes any tincture and stays sharp like the drawn marks.

        private const int ArtCells = 256, ArtPlane = ArtCells * ArtCells, ArtHeader = 7;
        private const float ArtUnit = 1f / 2048f;       // one byte step: a sixteenth of a cell, a cell being 2 / 256
        private static byte[] art;
        private static int artCount = -1;

        /// <summary>True for a mark with tone planes: its ink carries a shade and line work on top.</summary>
        public static bool Toned(int mark) => Source[mark][0] == '*';

        /// <summary>Signed distances to a toned mark's shade and line work, in its -1..1 box.</summary>
        public static void Tones(int mark, float x, float y, out float shade, out float line)
        {
            int picture = Source[mark][1] - '0';
            shade = Plane(picture, 1, x, y);
            line = Plane(picture, 2, x, y);
        }

        private static float Plane(int picture, int plane, float x, float y)
        {
            if (artCount < 0) LoadArt();
            if (picture >= artCount) return Far;
            float u = (x * 0.5f + 0.5f) * ArtCells - 0.5f, v = (y * 0.5f + 0.5f) * ArtCells - 0.5f;
            if (u < 0f || v < 0f || u > ArtCells - 1 || v > ArtCells - 1) return Reach;
            int x0 = (int)u, y0 = (int)v, x1 = Math.Min(x0 + 1, ArtCells - 1), y1 = Math.Min(y0 + 1, ArtCells - 1);
            float fx = u - x0, fy = v - y0;
            int at = ArtHeader + (picture * 3 + plane) * ArtPlane;
            float low = art[at + y0 * ArtCells + x0] * (1f - fx) + art[at + y0 * ArtCells + x1] * fx;
            float high = art[at + y1 * ArtCells + x0] * (1f - fx) + art[at + y1 * ArtCells + x1] * fx;
            return (128f - (low * (1f - fy) + high * fy)) * ArtUnit;
        }

        /// <summary>Reads the planes embedded next to this type, whichever assembly that is. Missing art draws nothing.</summary>
        private static void LoadArt()
        {
            byte[] loaded = null;
            int count = 0;
            try
            {
                System.Reflection.Assembly home = typeof(EmblemMarks).Assembly;
                foreach (string name in home.GetManifestResourceNames())
                {
                    if (!name.EndsWith("emblem-art.bin", StringComparison.Ordinal)) continue;
                    using (System.IO.Stream packed = home.GetManifestResourceStream(name))
                    using (var plain = new System.IO.Compression.DeflateStream(packed, System.IO.Compression.CompressionMode.Decompress))
                    using (var buffer = new System.IO.MemoryStream())
                    {
                        plain.CopyTo(buffer);
                        loaded = buffer.ToArray();
                    }
                    break;
                }
                if (loaded != null && loaded.Length > ArtHeader && loaded[0] == 'E' && loaded[1] == 'A' && loaded[2] == 'R' && loaded[3] == 'T' &&
                    (loaded[5] | loaded[6] << 8) == ArtCells && loaded.Length >= ArtHeader + loaded[4] * 3 * ArtPlane)
                    count = loaded[4];
            }
            catch (Exception) { count = 0; }
            art = loaded;
            artCount = count;
        }

        /// <summary>Signed distance to mark <paramref name="mark"/> in its own -1..1 box; negative inside the ink.</summary>
        public static float Distance(int mark, float x, float y)
        {
            if (Source[mark][0] == '*') return Plane(Source[mark][1] - '0', 0, x, y);
            // Parsing twice on two threads gives two equal tables; whichever lands is kept.
            Piece[] pieces = Parsed[mark] ?? (Parsed[mark] = Parse(Source[mark]));
            float d = Far;
            for (int i = 0; i < pieces.Length; i++)
            {
                Piece piece = pieces[i];
                if (piece.Include >= 0)
                {
                    d = Math.Min(d, Distance(piece.Include, (x - piece.ShiftX) / piece.Scale, (y - piece.ShiftY) / piece.Scale) * piece.Scale);
                    continue;
                }
                float px = piece.Flip ? -x : x;
                if (piece.Mirror) px = Math.Abs(px);
                float rx = px * piece.Cos + y * piece.Sin, ry = -px * piece.Sin + y * piece.Cos;
                float bx = Math.Max(Math.Max(piece.MinX - rx, rx - piece.MaxX), 0f), by = Math.Max(Math.Max(piece.MinY - ry, ry - piece.MaxY), 0f);
                float box = (float)Math.Sqrt(bx * bx + by * by);
                if (box > Reach)
                {
                    if (piece.Add) d = Math.Min(d, box);
                    continue;
                }
                float s = Shape(piece, rx, ry);
                d = piece.Add ? Math.Min(d, s) : Math.Max(d, -s);
            }
            return d;
        }

        private static float Shape(Piece piece, float x, float y)
        {
            float[] e = piece.Edges;
            float best = float.MaxValue;
            bool inside = false;
            for (int i = 0; i < e.Length; i += 4)
            {
                float ax = e[i], ay = e[i + 1], ex = e[i + 2] - ax, ey = e[i + 3] - ay, wx = x - ax, wy = y - ay;
                float t = (wx * ex + wy * ey) / (ex * ex + ey * ey);
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                float dx = wx - ex * t, dy = wy - ey * t, square = dx * dx + dy * dy;
                if (square < best) best = square;
                if ((ay > y) != (e[i + 3] > y) && wx < ex * wy / ey) inside = !inside;
            }
            float distance = (float)Math.Sqrt(best);
            if (!piece.Closed) return distance - piece.Half;
            return inside ? -distance : distance;
        }

        // ---- Reading the paths ------------------------------------------------------------------

        private static Piece[] Parse(string source)
        {
            var pieces = new List<Piece>();
            foreach (string raw in source.Split(';'))
            {
                string text = raw.Trim();
                if (text.Length == 0) continue;
                var piece = new Piece();
                int at = 1;
                if (text[0] == '=')
                {
                    piece.Include = (int)Number(text, ref at);
                    piece.Scale = Number(text, ref at);
                    piece.ShiftX = Number(text, ref at) / 100f;
                    piece.ShiftY = Number(text, ref at) / 100f;
                    pieces.Add(piece);
                    continue;
                }
                piece.Add = text[0] == '+';
                while (at < text.Length && text[at] != ' ')
                {
                    char flag = text[at++];
                    if (flag == '|') piece.Mirror = true;
                    else if (flag == '!') piece.Flip = true;
                    else if (flag == '~') { piece.Closed = false; piece.Half = Number(text, ref at) / 100f; }
                    else if (flag == '@')
                    {
                        double turn = Number(text, ref at) * Math.PI / 180.0;
                        piece.Cos = (float)Math.Cos(turn); piece.Sin = (float)Math.Sin(turn);
                    }
                }
                piece.Edges = Outline(text, at, piece.Closed);
                for (int i = 0; i < piece.Edges.Length; i += 2)
                {
                    piece.MinX = Math.Min(piece.MinX, piece.Edges[i] - piece.Half); piece.MaxX = Math.Max(piece.MaxX, piece.Edges[i] + piece.Half);
                    piece.MinY = Math.Min(piece.MinY, piece.Edges[i + 1] - piece.Half); piece.MaxY = Math.Max(piece.MaxY, piece.Edges[i + 1] + piece.Half);
                }
                pieces.Add(piece);
            }
            return pieces.ToArray();
        }

        private static float[] Outline(string text, int at, bool closed)
        {
            var edges = new List<float>();
            float x = 0f, y = 0f, startX = 0f, startY = 0f;
            bool open = false;
            void Line(float toX, float toY)
            {
                edges.Add(x / 100f); edges.Add(y / 100f); edges.Add(toX / 100f); edges.Add(toY / 100f);
                x = toX; y = toY;
            }
            void Close()
            {
                if (open && (x != startX || y != startY)) Line(startX, startY);
                open = false;
            }
            void Loop(float cx, float cy, float rx, float ry)
            {
                if (closed) Close();
                const int steps = 40;
                x = cx + rx; y = cy;
                for (int s = 1; s <= steps; s++)
                {
                    double a = s * 2.0 * Math.PI / steps;
                    Line(cx + rx * (float)Math.Cos(a), cy + ry * (float)Math.Sin(a));
                }
                open = false;
            }

            char command = ' ';
            while (true)
            {
                while (at < text.Length && (text[at] == ' ' || text[at] == ',')) at++;
                if (at >= text.Length) break;
                if (char.IsLetter(text[at])) command = text[at++];
                switch (command)
                {
                    case 'M':
                        if (closed) Close();
                        x = startX = Number(text, ref at); y = startY = Number(text, ref at);
                        open = true; command = 'L';
                        break;
                    case 'L': { float toX = Number(text, ref at); Line(toX, Number(text, ref at)); break; }
                    case 'H': Line(Number(text, ref at), y); break;
                    case 'V': Line(x, Number(text, ref at)); break;
                    case 'Z': if (open && (x != startX || y != startY)) Line(startX, startY); command = ' '; break;
                    case 'C':
                    case 'Q':
                    {
                        float x0 = x, y0 = y, x1 = Number(text, ref at), y1 = Number(text, ref at);
                        float x2 = Number(text, ref at), y2 = Number(text, ref at), x3 = x2, y3 = y2;
                        if (command == 'C') { x3 = Number(text, ref at); y3 = Number(text, ref at); }
                        else { x2 = x1 + (x3 - x1) / 3f; y2 = y1 + (y3 - y1) / 3f; x1 = x0 + (x1 - x0) * 2f / 3f; y1 = y0 + (y1 - y0) * 2f / 3f; }
                        const int steps = 12;
                        for (int s = 1; s <= steps; s++)
                        {
                            float t = s / (float)steps, u = 1f - t;
                            Line(u * u * u * x0 + 3f * u * u * t * x1 + 3f * u * t * t * x2 + t * t * t * x3,
                                u * u * u * y0 + 3f * u * u * t * y1 + 3f * u * t * t * y2 + t * t * t * y3);
                        }
                        break;
                    }
                    case 'O': { float cx = Number(text, ref at), cy = Number(text, ref at), r = Number(text, ref at); Loop(cx, cy, r, r); break; }
                    case 'E':
                    {
                        float cx = Number(text, ref at), cy = Number(text, ref at), rx = Number(text, ref at);
                        Loop(cx, cy, rx, Number(text, ref at));
                        break;
                    }
                    case 'R':
                    {
                        if (closed) Close();
                        int rays = (int)Number(text, ref at);
                        float inner = Number(text, ref at), outer = Number(text, ref at);
                        double half = Number(text, ref at) * Math.PI / 180.0, phase = Number(text, ref at) * Math.PI / 180.0;
                        for (int k = 0; k < rays; k++)
                        {
                            double a = phase + k * 2.0 * Math.PI / rays;
                            x = inner * (float)Math.Cos(a - half); y = inner * (float)Math.Sin(a - half);
                            float fromX = x, fromY = y;
                            Line(outer * (float)Math.Cos(a), outer * (float)Math.Sin(a));
                            Line(inner * (float)Math.Cos(a + half), inner * (float)Math.Sin(a + half));
                            Line(fromX, fromY);
                        }
                        break;
                    }
                    case 'P':
                    {
                        if (closed) Close();
                        var control = new List<float>();
                        while (true)
                        {
                            while (at < text.Length && (text[at] == ' ' || text[at] == ',')) at++;
                            if (at >= text.Length || char.IsLetter(text[at])) break;
                            control.Add(Number(text, ref at));
                        }
                        int count = control.Count / 2;
                        const int steps = 6;
                        x = control[0]; y = control[1];
                        for (int i = 0; i < count; i++)
                        {
                            int before = (i + count - 1) % count, next = (i + 1) % count, after = (i + 2) % count;
                            for (int s = 1; s <= steps; s++)
                            {
                                float t = s / (float)steps;
                                Line(Spline(control[before * 2], control[i * 2], control[next * 2], control[after * 2], t),
                                    Spline(control[before * 2 + 1], control[i * 2 + 1], control[next * 2 + 1], control[after * 2 + 1], t));
                            }
                        }
                        break;
                    }
                    default: throw new FormatException("emblem mark: unexpected '" + text[at] + "' in " + text);
                }
            }
            if (closed) Close();
            return edges.ToArray();
        }

        private static float Spline(float p0, float p1, float p2, float p3, float t) =>
            0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (3f * p1 - p0 - 3f * p2 + p3) * t * t * t);

        private static float Number(string text, ref int at)
        {
            while (at < text.Length && (text[at] == ' ' || text[at] == ',')) at++;
            int from = at;
            while (at < text.Length && (char.IsDigit(text[at]) || text[at] == '.' || (text[at] == '-' && at == from))) at++;
            return float.Parse(text.Substring(from, at - from), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
