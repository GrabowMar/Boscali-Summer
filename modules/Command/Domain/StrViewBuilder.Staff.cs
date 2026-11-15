using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    internal static partial class StrViewBuilder
    {
        private partial struct Worker
        {
            // ---- the INFO tab: chain of command ---------------------------------------------------------------------------------

            private static readonly string[] TierWords = { "THEATER COMMANDER", "COMPONENT COMMANDER", "BASE COMMANDER" };

            private void Staff()
            {
                StrStaffView s = m.Staff;
                StrStaffMode mode = !f.StaffPresent ? StrStaffMode.Off : f.PostsAvailable ? StrStaffMode.Roster : StrStaffMode.NoBoard;
                int cnt = mode == StrStaffMode.Roster && f.Officers != null ? Math.Min(f.OfficerCount, f.Officers.Length) : 0;
                int own = Side(true, cnt, s.Own), enemy = Side(false, cnt, s.Enemy);
                Put(ref s.OwnCount, own);
                Put(ref s.EnemyCount, enemy);
                Put(ref s.Mode, mode);

                string head = "", why = "", @do = "", ownNote = "", enemyNote = "";
                if (mode == StrStaffMode.Off)
                {
                    head = "NO CHAIN OF COMMAND";
                    why = "THE HIGH COMMAND MODULE IS OFF FOR YOU, SO THERE IS NO STAFF BOARD TO READ";
                    @do = "SWITCH HIGH COMMAND ON IN F1, THEN RESTART THE MISSION";
                }
                else if (mode == StrStaffMode.NoBoard)
                {
                    head = "NO STAFF BOARD YET";
                    why = string.IsNullOrEmpty(f.StaffStatus) ? "THE HOST HAS SENT NO BOARD, OR THE LAST ONE IS OVER 20 S OLD" : f.StaffStatus;
                    @do = "WAIT: THE NEXT BOARD ARRIVES BY ITSELF";
                }
                else
                {
                    if (own == 0) ownNote = "THE BOARD LISTS NO POSTS FOR YOUR FACTION";
                    enemyNote = enemy == 0
                        ? "NO ENEMY POST CONFIRMED YET" + StrWords.Dot + "UNCONFIRMED POSTS STAY SEALED"
                        : "ONLY POSTS YOUR SIDE HAS CONFIRMED ARE LISTED" + StrWords.Dot + "THE REST STAY SEALED";
                }
                Put(ref s.Head, head);
                Put(ref s.Why, why);
                Put(ref s.Do, @do);
                Put(ref s.OwnNote, ownNote);
                Put(ref s.EnemyNote, enemyNote);
            }

            // The side's officers in tree order, theater first: tier, then slot. Bounded by the host's eight posts per faction.
            private int Side(bool own, int cnt, StrOfficerRow[] rows)
            {
                Span<int> best = stackalloc int[StrViewModel.MaxOfficers];
                int kept = 0;
                for (int i = 0; i < cnt; i++)
                {
                    if (f.Officers[i].Own != own) continue;
                    int at = kept < best.Length ? kept : best.Length;
                    while (at > 0 && OfficerBefore(in f.Officers[i], in f.Officers[best[at - 1]])) at--;
                    if (at >= best.Length) continue;
                    int end = Math.Min(kept, best.Length - 1);
                    for (int k = end; k > at; k--) best[k] = best[k - 1];
                    best[at] = i;
                    if (kept < best.Length) kept++;
                }
                for (int k = 0; k < kept; k++) Officer(Get(rows, k), in f.Officers[best[k]]);
                return kept;
            }

            // The office in one short line for a chart card: the theater, a component by its name ("AIR COMPONENT"), a base by where it stands.
            private static string Office(int tier, string role, string site)
            {
                if (tier == 0) return "THEATER";
                if (tier == 2) return site == StrWords.Dash ? "BASE" : site;
                string name = role.Replace(" CMDR", "").Replace(" COMMANDER", "");
                return name.Length == 0 ? "COMPONENT" : name;
            }

            private static int Tier(in StrOfficerIn o) => Math.Max(0, Math.Min(2, o.Tier));

            private static bool OfficerBefore(in StrOfficerIn a, in StrOfficerIn b) => Tier(in a) != Tier(in b) ? Tier(in a) < Tier(in b) : a.Id < b.Id;

            private void Officer(StrOfficerRow r, in StrOfficerIn o)
            {
                int tier = Tier(in o);
                bool hidden = !o.Own && !o.Confirmed;
                string site = hidden || string.IsNullOrWhiteSpace(o.Site) ? StrWords.Dash : o.Site.Replace('_', ' ').ToUpperInvariant();
                string seen = hidden ? "NO INTEL" : o.Own ? "OWN STAFF" : SeenWords(o.IntelAge);
                string status = hidden ? "SEALED" : o.Own ? (o.Alert ? "UNDER FIRE" : site != StrWords.Dash ? "AT POST" : StrWords.Dash) : seen;

                Put(ref r.Id, o.Id);
                Put(ref r.ParentId, o.ParentId);
                Put(ref r.Tier, tier);
                Put(ref r.Own, o.Own);
                Put(ref r.Sealed, hidden);
                Put(ref r.Rank, hidden ? "" : o.Rank ?? "");
                Put(ref r.Name, hidden ? "SEALED POST" : string.IsNullOrWhiteSpace(o.Name) ? StrWords.Dash : o.Name);
                Put(ref r.Role, hidden || string.IsNullOrWhiteSpace(o.Role) ? TierWords[tier] : o.Role);
                Put(ref r.Site, site);
                Put(ref r.Office, Office(tier, hidden || string.IsNullOrWhiteSpace(o.Role) ? "" : o.Role, hidden ? StrWords.Dash : site));
                Put(ref r.StatusText, status);
                Put(ref r.SeenText, seen);
                Put(ref r.Ink, hidden ? StrInkRole.Muted : o.Own ? (o.Alert ? StrInkRole.Danger : StrInkRole.Own) : StrInkRole.Hostile);
                Put(ref r.Portrait, hidden ? null : o.Portrait);

                int traits = hidden ? 0 : Traits(r, o.Trait);
                Put(ref r.TraitCount, traits);

                StrItemId locate = StrItemId.None;
                string why = "";
                if (!o.Own)
                {
                    bool placed = !hidden && float.IsFinite(o.X) && float.IsFinite(o.Z);
                    if (!placed) why = hidden ? "SEALED: NO PLACE IS KNOWN" : "NO KNOWN PLACE ON THE MAP FOR THIS OFFICER";
                    else
                    {
                        for (int i = 0; i < m.PostCount; i++) if (m.Posts[i].PostId == o.Id) { locate = m.Posts[i].Id; break; }
                        if (locate.IsNone) why = "NOT ON THE STRIKE LIST: ONLY THE " + StrViewModel.MaxPosts + " RICHEST POSTS ARE LISTED";
                    }
                }
                Put(ref r.Locate, locate);
                Put(ref r.LocateWhy, why);
            }

            // The bonus line is "ENTRY · ENTRY · ENTRY"; "NO STAFF BONUS" and an empty line are no traits.
            private int Traits(StrOfficerRow r, string bonus)
            {
                int used = 0, start = 0;
                if (!string.IsNullOrWhiteSpace(bonus) && bonus != "NO STAFF BONUS")
                    while (used < r.Traits.Length && start <= bonus.Length)
                    {
                        int end = bonus.IndexOf(StrWords.Dot, start, StringComparison.Ordinal);
                        Put(ref r.Traits[used++], end < 0 ? bonus.Substring(start) : bonus.Substring(start, end - start));
                        if (end < 0) break;
                        start = end + StrWords.Dot.Length;
                    }
                for (int i = used; i < r.Traits.Length; i++) Put(ref r.Traits[i], "");
                return used;
            }
        }
    }
}
