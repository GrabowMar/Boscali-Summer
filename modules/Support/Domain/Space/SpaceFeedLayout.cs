using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>A rectangle in board units, origin top-left, y down.</summary>
    internal readonly struct FeedBox
    {
        public readonly float X, Y, W, H;
        public FeedBox(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
        public float Right => X + W;
        public float Bottom => Y + H;
    }

    /// <summary>
    /// The ORBIT page's fixed arithmetic, for the compact page under the C2 chrome and for the full-screen station. Every row has a
    /// constant height after construction; only the sensor picture takes what is left. Nothing scrolls and a refresh never moves a
    /// row, because no height here depends on live text. Coordinates are board units, origin top-left of the area handed to
    /// <see cref="Compute"/> (the page below the chrome, or the station below its chrome).
    /// </summary>
    internal sealed class SpaceFeedLayout
    {
        /// <summary>Height of a C2 box header (mirrors C2Box.HeaderH; Domain cannot reference Presentation).</summary>
        public const float BoxHeader = 20f;
        public const float StripH = 22f, WarnFullH = 36f, ToolbarH = 26f, RowH = 26f, RowPitch = 28f, ButtonsH = 30f, CardH = 38f,
            CardPitch = 40f, ArtH = 50f, CellsH = 34f, CompactCellsH = 30f, ColumnWidth = 458f, MinImageTall = 150f, MinImageShort = 100f,
            ConsoleLineH = 14f, ConsolePad = 4f, SensorChrome = 51f, TaskedFixed = 22f;
        public const int Tiles6 = SpaceFeedRules.ContactsPerPage;

        /// <summary>The full-screen station.</summary>
        public bool Full { get; private set; }
        /// <summary>The constellation box carries the cosmetic orbit art (tall pages and the station); short pages show the three cells only.</summary>
        public bool Art { get; private set; }
        public FeedBox Threat, Constellation, ConstArt, ConstCells, Sensor, Toolbar, Image, Track, Buttons, Tasked, Console;
        public int TrackRows, CardRows, ConsoleLines;
        /// <summary>The lowest edge of any box.</summary>
        public float Bottom { get; private set; }
        public int CardCount => CardRows;

        /// <param name="full">The station: the sensor frame on the left, the control column fixed at 458 wide on the right.</param>
        public static SpaceFeedLayout Compute(float width, float height, bool full)
        {
            var l = new SpaceFeedLayout { Full = full };
            if (!full) l.Compact(width, height); else l.Station(width, height);
            float bottom = Math.Max(Math.Max(l.Sensor.Bottom, l.Tasked.Bottom), Math.Max(l.Track.Bottom, l.Threat.Bottom));
            bottom = Math.Max(bottom, Math.Max(l.Constellation.Bottom, l.Buttons.Bottom));
            if (l.ConsoleLines > 0) bottom = Math.Max(bottom, l.Console.Bottom);
            l.Bottom = bottom;
            return l;
        }

        private void Compact(float w, float h)
        {
            Art = h >= 600f;
            float g = Art ? 6f : 4f;
            TrackRows = Art ? 5 : 4;
            float y = g;
            Threat = new FeedBox(0f, y, w, StripH); y += StripH + g;
            if (Art)
            {
                float ch = 21f + ArtH + CellsH + 1f;
                Constellation = new FeedBox(0f, y, w, ch);
                ConstArt = new FeedBox(1f, y + 21f, w - 2f, ArtH);
                ConstCells = new FeedBox(1f, y + 21f + ArtH, w - 2f, CellsH);
                y += ch + g;
            }
            else
            {
                Constellation = new FeedBox(0f, y, w, CompactCellsH);
                ConstArt = new FeedBox(0f, y, w, 0f);
                ConstCells = new FeedBox(1f, y, w - 2f, CompactCellsH);
                y += CompactCellsH + g;
            }
            float trackH = TaskedFixed + TrackRows * RowPitch;
            float below = SensorChrome + g + trackH + g + ButtonsH + g + g;
            float pool = h - y - below - TaskedFixed;
            float minImage = Art ? MinImageTall : MinImageShort;
            CardRows = Art ? Math.Max(1, Math.Min(4, (int)Math.Floor((pool - minImage) / CardPitch))) : 1;
            float image = Math.Max(60f, pool - CardRows * CardPitch);
            Sensor = new FeedBox(0f, y, w, SensorChrome + image);
            PlaceSensor();
            y += Sensor.H + g;
            Track = new FeedBox(0f, y, w, trackH); y += trackH + g;
            Buttons = new FeedBox(0f, y, w, ButtonsH); y += ButtonsH + g;
            Tasked = new FeedBox(0f, y, w, TaskedFixed + CardRows * CardPitch);
            Console = new FeedBox(0f, 0f, 0f, 0f);
            ConsoleLines = 0;
        }

        private void Station(float w, float h)
        {
            Art = true;
            const float g = 6f;
            Threat = new FeedBox(0f, 0f, w, WarnFullH);
            float y = WarnFullH + g;
            float column = Math.Min(ColumnWidth, w * 0.4f);
            float x = w - column;
            Sensor = new FeedBox(0f, y, Math.Max(120f, x - g), Math.Max(SensorChrome + 60f, h - y));
            PlaceSensor();
            float ch = 21f + ArtH + CellsH + 1f;
            Constellation = new FeedBox(x, y, column, ch);
            ConstArt = new FeedBox(x + 1f, y + 21f, column - 2f, ArtH);
            ConstCells = new FeedBox(x + 1f, y + 21f + ArtH, column - 2f, CellsH);
            y += ch + g;
            TrackRows = Tiles6;
            float trackH = TaskedFixed + TrackRows * RowPitch;
            Track = new FeedBox(x, y, column, trackH); y += trackH + g;
            Buttons = new FeedBox(x, y, column, ButtonsH); y += ButtonsH + g;
            CardRows = 3;
            Tasked = new FeedBox(x, y, column, TaskedFixed + CardRows * CardPitch); y += Tasked.H + g;
            float rest = h - y;
            ConsoleLines = (int)Math.Floor((rest - 2f * ConsolePad) / ConsoleLineH);
            if (ConsoleLines < 2) { ConsoleLines = 0; Console = new FeedBox(x, y, column, 0f); }
            else
            {
                ConsoleLines = Math.Min(ConsoleLines, 28);
                Console = new FeedBox(x, y, column, ConsoleLines * ConsoleLineH + 2f * ConsolePad);
            }
        }

        private void PlaceSensor()
        {
            Toolbar = new FeedBox(Sensor.X + 2f, Sensor.Y + 22f, Sensor.W - 4f, ToolbarH);
            Image = new FeedBox(Sensor.X + 1f, Sensor.Y + 50f, Sensor.W - 2f, Math.Max(0f, Sensor.H - SensorChrome));
        }

        /// <summary>One track-file row inside <see cref="Track"/>.</summary>
        public FeedBox TrackRow(int index) => new FeedBox(Track.X + 2f, Track.Y + 22f + index * RowPitch, Track.W - 4f, RowH);

        /// <summary>One TASKED row inside <see cref="Tasked"/>.</summary>
        public FeedBox CardRow(int index) => new FeedBox(Tasked.X + 2f, Tasked.Y + 22f + index * CardPitch, Tasked.W - 4f, CardH);

        /// <summary>One of the three constellation bird cells inside <see cref="ConstCells"/>.</summary>
        public FeedBox BirdCell(int index)
        {
            float w = ConstCells.W / 3f;
            return new FeedBox(ConstCells.X + index * w, ConstCells.Y, w, ConstCells.H);
        }

        /// <summary>The CONFIRM (0) and TRANSMIT (1) buttons inside <see cref="Buttons"/>.</summary>
        public FeedBox ActionButton(int index)
        {
            const float g = 6f;
            float w = (Buttons.W - 2f * 2f - g) / 2f;
            return new FeedBox(Buttons.X + 2f + index * (w + g), Buttons.Y, w, Buttons.H);
        }
    }

    internal enum FeedEntryKind : byte { Contact, Mark }

    /// <summary>One selectable target on the feed: a host-revealed contact, or a MARK whose reveal has lapsed (a fixed point).</summary>
    internal readonly struct FeedEntry
    {
        public readonly int Id;
        public readonly FeedEntryKind Kind;
        public readonly ProbableClass Class;
        public readonly byte Percent;
        public readonly bool Moving, Marked;
        public readonly float X, Z, Expires;
        public readonly BirdKind Source;

        public FeedEntry(int id, FeedEntryKind kind, ProbableClass cls, byte percent, bool moving, bool marked, float x, float z, float expires, BirdKind source)
        {
            Id = id; Kind = kind; Class = cls; Percent = percent; Moving = moving; Marked = marked; X = x; Z = z; Expires = expires; Source = source;
        }
    }

    internal static class SpaceFeedEntries
    {
        /// <summary>
        /// The selectable targets: live reveals first (in mirror order, MARKed ones flagged), then MARKs whose reveal lapsed as
        /// fixed points. Bounded to the wire's contact cap; expired reveals drop out.
        /// </summary>
        public static void Build(IReadOnlyList<FeedContact> contacts, IReadOnlyList<FeedMark> marks, float now, List<FeedEntry> into)
        {
            into.Clear();
            int markCount = marks?.Count ?? 0;
            for (int i = 0; contacts != null && i < contacts.Count && into.Count < SpaceWire.MaxContacts; i++)
            {
                FeedContact c = contacts[i];
                if (SpaceRules.MissionTime(now) && c.Expires <= now) continue;
                bool marked = false;
                for (int m = 0; m < markCount; m++) if (marks[m].Id == c.Id) { marked = true; break; }
                into.Add(new FeedEntry(c.Id, FeedEntryKind.Contact, c.Class, c.Percent, c.Moving, marked, c.X, c.Z, c.Expires, c.Source));
            }
            for (int m = 0; m < markCount && into.Count < SpaceWire.MaxContacts + SpaceWire.MaxMarks; m++)
            {
                FeedMark mark = marks[m];
                bool listed = false;
                for (int i = 0; i < into.Count; i++) if (into[i].Id == mark.Id) { listed = true; break; }
                if (listed) continue;
                into.Add(new FeedEntry(mark.Id, FeedEntryKind.Mark, ProbableClass.Unknown, 0, mark.Moving, true, mark.X, mark.Z, mark.Expires, mark.Source));
            }
        }

        public static string Title(in FeedEntry e)
        {
            if (e.Kind == FeedEntryKind.Mark) return "MARK";
            return SpaceFeedRules.ContactLabel(e.Class, 0);
        }

        public static string Sub(in FeedEntry e)
        {
            if (e.Marked) return "MARKED";
            string pct = e.Percent > 0 && e.Percent <= 100 ? e.Percent + "%" : "";
            string move = e.Moving ? "MOVING" : "";
            return pct.Length > 0 && move.Length > 0 ? pct + " " + move : pct.Length > 0 ? pct : move.Length > 0 ? move : "UNKNOWN";
        }

        /// <summary>Only a live, un-MARKed reveal can be confirmed.</summary>
        public static bool CanConfirm(in FeedEntry e) => e.Kind == FeedEntryKind.Contact && !e.Marked;

        /// <summary>
        /// The MARK ids a SEND posts: the selected MARK first, then the rest in order, at most six. Returns how many were written.
        /// </summary>
        public static int SendIds(IReadOnlyList<FeedMark> marks, int selectedId, int[] ids)
        {
            int n = 0, count = marks?.Count ?? 0;
            bool lead = false;
            for (int i = 0; i < count && n < ids.Length; i++)
                if (marks[i].Id == selectedId) { ids[n++] = selectedId; lead = true; break; }
            for (int i = 0; i < count && n < ids.Length; i++)
            {
                if (lead && marks[i].Id == selectedId) continue;
                ids[n++] = marks[i].Id;
            }
            return n;
        }
    }
}
