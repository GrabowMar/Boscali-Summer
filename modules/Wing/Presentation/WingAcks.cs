using System;

using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Runtime;

namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The order results the HUD strip's ack chip and BEHAVIOUR › SORTIE show (spec 2026-10-04 §4.1 AckFeed). Only the
    /// player's own orders: plan steps and behaviour rules would flood the chip.</summary>
    internal static class WingAcks
    {
        public static readonly AckFeed Feed = new AckFeed();

        public static void Push(WingOrder o, OrderResult r)
        {
            if (o == null || o.Source != OrderSource.Player) return;
            string what = r.Accepted && !string.IsNullOrEmpty(r.Ack) ? r.Ack : KindWord(o.Kind);
            Feed.Push(Time.unscaledTime, Who(o.Scope), what, r.Accepted, r.Accepted ? null : r.Reason);
        }

        private static string Who(WingScope s)
        {
            int first = 0, count = s.Members?.Length ?? 0;
            WingService w = WingService.Instance;
            if (s.Kind == ScopeKind.Members && count > 0 && w != null)
                foreach (WingMember m in w.Members)
                    if ((object)m.Aircraft != null && Array.IndexOf(s.Members, m.Aircraft.persistentID.Id) >= 0)
                    {
                        first = first == 0 ? m.Number : Math.Min(first, m.Number);
                    }
            return AckWords.Who((byte)s.Kind, s.Element, first, count);
        }

        /// <summary>"FORM UP" from FormUp: the kind's name with spaces before inner capitals.</summary>
        private static string KindWord(OrderKind k)
        {
            string n = k.ToString();
            var sb = new System.Text.StringBuilder(n.Length + 4);
            for (int i = 0; i < n.Length; i++)
            {
                if (i > 0 && char.IsUpper(n[i])) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(n[i]));
            }
            return sb.ToString();
        }
    }
}
