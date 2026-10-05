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
    /// The SPACE feed board's fixed arithmetic. Every row has a constant height after construction; only the image takes what is
    /// left. Nothing scrolls and a refresh never moves a row, because no height here depends on live text.
    /// </summary>
    internal sealed class SpaceFeedLayout
    {
        public const float Gap = 5f, ThreatH = 24f, ToolbarH = 28f, StatusH = 20f, TileH = 36f, ActionsH = 28f,
            HeaderH = 18f, CardH = 40f, WordsH = 22f, ColumnWidth = 458f, MaxCards = 6f, ThreatFullH = 36f;
        public const int Tiles6 = SpaceFeedRules.ContactsPerPage;

        public bool Full { get; private set; }
        public FeedBox Threat, Toolbar, Image, Status, Tiles, Actions, TaskedHeader, Words;
        public FeedBox[] Cards = new FeedBox[0];
        public int CardCount => Cards.Length;
        /// <summary>The lowest edge of any box.</summary>
        public float Bottom { get; private set; }

        /// <param name="full">The full-screen board: the image on the left, the control column fixed at 464 wide on the right.</param>
        public static SpaceFeedLayout Compute(float width, float height, bool full)
        {
            var l = new SpaceFeedLayout { Full = full };
            if (!full) l.Compact(width, height); else l.FullScreen(width, height);
            float bottom = Math.Max(Math.Max(l.Image.Bottom, l.Words.Bottom), l.Threat.Bottom);
            for (int i = 0; i < l.Cards.Length; i++) bottom = Math.Max(bottom, l.Cards[i].Bottom);
            l.Bottom = bottom;
            return l;
        }

        private void Compact(float w, float h)
        {
            int cards = h < 600f ? 1 : h < 700f ? 2 : 3;
            float fixedHeight = ThreatH + ToolbarH + StatusH + TileH + ActionsH + HeaderH + cards * CardH + WordsH + (7 + cards) * Gap;
            float image = Math.Max(60f, h - fixedHeight);
            float y = 0f;
            Threat = new FeedBox(0f, y, w, ThreatH); y += ThreatH + Gap;
            Toolbar = new FeedBox(0f, y, w, ToolbarH); y += ToolbarH + Gap;
            Image = new FeedBox(0f, y, w, image); y += image + Gap;
            Status = new FeedBox(0f, y, w, StatusH); y += StatusH + Gap;
            Tiles = new FeedBox(0f, y, w, TileH); y += TileH + Gap;
            Actions = new FeedBox(0f, y, w, ActionsH); y += ActionsH + Gap;
            Stack(0f, w, ref y, cards);
        }

        private void FullScreen(float w, float h)
        {
            float column = Math.Min(ColumnWidth, w * 0.4f);
            float x = w - column;
            Threat = new FeedBox(0f, 0f, w, ThreatFullH); // the full-screen strip is taller and its words larger: it must read at a glance
            float top = ThreatFullH + Gap;
            Image = new FeedBox(0f, top, Math.Max(60f, x - Gap), Math.Max(60f, h - top));
            float y = top;
            Toolbar = new FeedBox(x, y, column, ToolbarH); y += ToolbarH + Gap;
            Status = new FeedBox(x, y, column, StatusH); y += StatusH + Gap;
            Tiles = new FeedBox(x, y, column, TileH); y += TileH + Gap;
            Actions = new FeedBox(x, y, column, ActionsH); y += ActionsH + Gap;
            float rest = h - y - HeaderH - Gap - WordsH - Gap;
            int cards = (int)Math.Max(1f, Math.Min(MaxCards, (float)Math.Floor((rest + Gap) / (CardH + Gap))));
            Stack(x, column, ref y, cards);
        }

        private void Stack(float x, float w, ref float y, int cards)
        {
            TaskedHeader = new FeedBox(x, y, w, HeaderH); y += HeaderH + Gap;
            Cards = new FeedBox[cards];
            for (int i = 0; i < cards; i++) { Cards[i] = new FeedBox(x, y, w, CardH); y += CardH + Gap; }
            Words = new FeedBox(x, y, w, WordsH);
        }

        /// <summary>The six tile boxes of a page inside <see cref="Tiles"/>, with a 4 unit gap.</summary>
        public FeedBox Tile(int index)
        {
            const float g = 4f;
            float w = (Tiles.W - g * (Tiles6 - 1)) / Tiles6;
            return new FeedBox(Tiles.X + index * (w + g), Tiles.Y, w, Tiles.H);
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
