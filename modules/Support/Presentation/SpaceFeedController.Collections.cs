using System;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.Fronts;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;

namespace BoscaliSummer.Modules.Support.Presentation
{
    internal sealed partial class SpaceFeedController
    {
        internal bool TryWords(float now, out string words, out AvState tone)
        {
            words = feedWords;
            tone = feedTone;
            return words.Length > 0 && now - feedWordsAt < WordsHold;
        }

        internal CollectionReportView CollectionReport(float now)
        {
            var result = new CollectionReportView { Title = "NO PRODUCT RECEIVED", Summary = "BOOK A PASS TO CUE THE SQUAD" };
            bool optical = TryCollectedLook(BirdKind.Optical, now, out GlobalPosition eo, out float eoAt);
            bool radar = TryCollectedLook(BirdKind.Radar, now, out GlobalPosition sar, out float sarAt);
            if (!optical && !radar)
            {
                var ops = manager.OpsMirror;
                if (ops.Known && ops.State.Active && ops.State.Faction == lastFaction)
                {
                    foreach (OrbitCollection collection in ops.State.Collections)
                    {
                        if (collection.Status != OrbitCollection.Queued) continue;
                        result.Title = "COLLECTION SCHEDULED";
                        result.Summary = "RETURN EXPECTED AFTER ACQUISITION";
                        break;
                    }
                }
                return result;
            }
            bool useRadar = radar && (!optical || sarAt >= eoAt);
            GlobalPosition aim = useRadar ? sar : eo;
            BirdKind kind = useRadar ? BirdKind.Radar : BirdKind.Optical;
            result.Title = useRadar ? "RADAR RETURN" : "OPTICAL RETURN";
            result.Grid = OpsKit.Grid(aim.x, aim.z);

            var space = manager.SpaceMirror.State;
            if (!manager.SpaceMirror.Known || !space.Feed)
            {
                result.Summary = "WAITING FOR DOWNLINK";
                return result;
            }
            float radius = useRadar ? settings.SarSceneRadius.Value : settings.OpticalSceneRadius.Value;
            FeedContact lead = default;
            for (int i = 0; i < space.Contacts.Count; i++)
            {
                FeedContact contact = space.Contacts[i];
                if (contact.Source != kind || !contact.Booked || contact.Class != ProbableClass.Hostile || contact.Expires <= now) continue;
                double dx = contact.X - aim.x, dz = contact.Z - aim.z;
                if (dx * dx + dz * dz > (double)radius * radius) continue;
                if (lead.Id == 0 || contact.Percent > lead.Percent || (contact.Percent == lead.Percent && contact.Expires > lead.Expires))
                    lead = contact;
            }
            if (lead.Id == 0)
            {
                result.Summary = "NO HOSTILE FIX AVAILABLE · REVIEW DOWNLINK";
                return result;
            }

            result.Title = "HOSTILE FIX · " + (useRadar ? "RADAR" : "OPTICAL");
            result.Grid = OpsKit.Grid(lead.X, lead.Z);
            result.ContactId = lead.Id;
            result.FreshUntil = lead.Expires;
            result.Summary = lead.Percent > 0 ? lead.Percent + "% CONFIDENCE" : "AWAITING HOST CONFIRMATION";
            foreach (FeedPost post in space.Posts)
            {
                if (post.Points == null || post.Expires <= now) continue;
                foreach (FeedPoint point in post.Points)
                {
                    if (point.Source != kind || Math.Abs(lead.X - point.X) > 25f || Math.Abs(lead.Z - point.Z) > 25f) continue;
                    result.Summary = "MIS JOB " + post.CallId + " ON THE BOARD";
                    result.SendRefusal = "ALREADY ON MIS";
                    return result;
                }
            }
            if (chainTrack != 0 || markRequest != 0 || sendRequest != 0) result.SendRefusal = "WAITING FOR HOST";
            else if (space.Family == SpaceFamilyState.Dark) result.SendRefusal = "DOWNLINK OFFLINE";
            else if (space.LiveMarks >= SpaceWire.MaxMarks && !MarkedId(lead.Id)) result.SendRefusal = "MARK CAPACITY FULL";
            result.CanSend = result.SendRefusal == null;
            return result;
        }

        internal bool SendOrbitalTarget(int contactId)
        {
            float now = SupportManager.MissionNow();
            CollectionReportView report = CollectionReport(now);
            if (contactId <= 0 || report.ContactId != contactId || !report.CanSend) return false;
            Touch();
            if (MarkedId(contactId)) return SendMarks(contactId, single: true);
            RequestMark(contactId);
            if (markRequest == 0) return false;
            chainTrack = contactId;
            chainAt = now;
            chainSingle = true;
            return true;
        }
    }
}
