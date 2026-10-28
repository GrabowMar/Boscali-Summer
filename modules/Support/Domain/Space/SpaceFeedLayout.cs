using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Space
{

    /// <summary>
    /// The ORBIT page's fixed arithmetic, for the compact page under the C2 chrome and for the full-screen station. Every row has a
    /// constant height after construction; only the sensor picture takes what is left. Nothing scrolls and a refresh never moves a
    /// row, because no height here depends on live text. Coordinates are board units, origin top-left of the area handed to
    /// <see cref="Compute"/> (the page below the chrome, or the station below its chrome).
    /// </summary>
    internal sealed class SpaceFeedLayout
    {
        public const int Tiles6 = SpaceFeedRules.ContactsPerPage;
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
