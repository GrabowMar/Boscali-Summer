using BoscaliSummer.Features.Support.Domain.Cyber;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// CYBER › THREATS — the adversary campaign and the incident board. The campaign card says
    /// how hot the adversary is and when it moves next; each incident row says what it is,
    /// where, who, how it ended, and offers the verb that answers it (TRACE an intrusion or a
    /// heard operation, BURN THROUGH a raid, ISOLATE the site an intrusion sits on).
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const float CampaignHeight = 136f;
        private static readonly string[] DefenceNotes =
        {
            "PROBE · a SIGINT post covering the target blocks it; otherwise your emitters are exposed for 90 s.",
            "INTRUSION · isolate or bait the site it sits on; held for 40 s it is contained. Patch what it took.",
            "TRACE · needs a working SIGINT post; bait doubles the speed. A foothold cuts operation cost 25%.",
            "RAID · links and radar cover inside halve; a jammer in reach can burn through.",
            "C2 BREACH · a compromised Cyber Command locks OPERATIONS and stops feeding bandwidth. Field sites left alone reimage themselves after two minutes; Cyber Command never does.",
            "FOOTHOLD · while one is open your operations cost 25% less and need no jammer of your own in reach."
        };

        private readonly OpsRow[] incidentRows = new OpsRow[CyberNetwork.IncidentSlots];
        private Image campaignRail, heatFill;
        private TMP_Text campaignPhase, campaignClock, campaignNote, footholdNote, incidentNote;

        private void ResetThreatPage()
        {
            for (int i = 0; i < incidentRows.Length; i++) incidentRows[i] = null;
            campaignRail = heatFill = null;
            campaignPhase = campaignClock = campaignNote = footholdNote = incidentNote = null;
        }

        private void BuildThreatPage(RectTransform root, Rect body)
        {
            float height = HeaderHeight + CampaignHeight + SectionGap +
                           HeaderHeight + incidentRows.Length * RowHeight + SectionGap +
                           HeaderHeight + DefenceNotes.Length * 56f + SectionGap;
            RectTransform parent = BeginSub(root, body, height, out float x, out float y, out float width);

            Header(parent, x, ref y, width, "ADVERSARY CAMPAIGN", "ACTIVITY · HEAT · NEXT ATTACK");
            var card = new Rect(x, y, width, CampaignHeight);
            campaignRail = AvKit.TacticalCard(parent, card, AvTheme.RailInert).Rail;
            campaignPhase = AvKit.Label(parent, "", new Rect(x + 12f, y - 8f, width * 0.6f, 26f), AvTheme.Dim, 20f,
                FontStyles.Bold);
            campaignClock = AvKit.Label(parent, "", new Rect(x + width - 172f, y - 10f, 160f, 22f), AvTheme.RailCaution,
                AvTokens.FontTitle, FontStyles.Bold, TextAlignmentOptions.Right);
            AvKit.Label(parent, "HEAT", new Rect(x + 12f, y - 40f, 40f, 12f), AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold);
            heatFill = AvKit.ProgressBar(parent, new Rect(x + 52f, y - 42f, width - 64f, 8f), 0f, AvTheme.RailCaution);
            float track = width - 64f;
            AvKit.Rule(parent, new Rect(x + 52f + track * CyberNetwork.ActiveHeat / CyberNetwork.HeatMaximum, y - 38f, 1f, 16f),
                AvTheme.TextPrimary.WithAlpha(0.6f));
            AvKit.Rule(parent, new Rect(x + 52f + track * CyberNetwork.OffensiveHeat / CyberNetwork.HeatMaximum, y - 38f, 1f, 16f),
                AvTheme.RailDanger.WithAlpha(0.8f));
            campaignNote = Wrapped(AvStyled.Label(parent, new Rect(x + 12f, y - 60f, width - 24f, 30f), "", "row-sub"));
            footholdNote = Wrapped(AvStyled.Label(parent, new Rect(x + 12f, y - 98f, width - 24f, 30f), "", "row-sub"));
            y -= CampaignHeight + SectionGap;

            incidentNote = Header(parent, x, ref y, width, "INCIDENT BOARD", "");
            for (int i = 0; i < incidentRows.Length; i++)
            {
                int index = i;
                incidentRows[i] = Row(parent, x, y, width, false, "---", "NO INCIDENT", "", "TRACE",
                    () => OnIncidentPrimary(index), "ISOLATE", () => OnIncidentIsolate(index));
                y -= RowHeight;
            }
            y -= SectionGap;

            Header(parent, x, ref y, width, "DEFENCE GUIDE", "READINESS AND COUNTERMEASURES");
            for (int i = 0; i < DefenceNotes.Length; i++)
            {
                TMP_Text note = Wrapped(AvStyled.Label(parent, new Rect(x, y - i * 56f, width, 52f), DefenceNotes[i], "row-sub"));
                note.enableWordWrapping = true;
                note.maxVisibleLines = 4;
                note.color = AvTheme.Dim;
            }
        }

        private void OnIncidentPrimary(int index)
        {
            CyberNetwork network = support.LocalCyber;
            if (network == null || !network.IncidentActive(index)) return;
            CyberVerb verb = network.Incident(index).Kind == IncidentKind.Raid ? CyberVerb.BurnThrough : CyberVerb.Trace;
            support.RequestCyberVerb(verb, index);
            CyberLog(CyberWords.Verb(verb) + " · " + CyberWords.Incident(network.Incident(index).Kind));
            nextRefresh = 0f;
        }

        private void OnIncidentIsolate(int index)
        {
            CyberNetwork network = support.LocalCyber;
            if (network == null || !network.IncidentActive(index)) return;
            int slot = network.Incident(index).Site;
            if (!network.Exists(slot)) return;
            support.RequestCyberVerb(CyberVerb.Isolate, slot);
            nextRefresh = 0f;
        }

        private void RefreshThreatPage(CyberNetwork network, double now)
        {
            if (campaignPhase == null) return;
            bool built = network != null && network.HasCommand;
            if (!built)
            {
                campaignRail.color = AvTheme.RailInert;
                campaignPhase.text = "NO NETWORK";
                campaignPhase.color = AvTheme.Dim;
                campaignClock.text = "";
                heatFill.fillAmount = 0f;
                campaignNote.text = "The adversary moves once Cyber Command is on station.";
                footholdNote.text = "";
            }
            else
            {
                CampaignPhase phase = network.Phase;
                Color colour = phase == CampaignPhase.Offensive ? AvTheme.RailDanger
                    : phase == CampaignPhase.Active ? AvTheme.RailCaution : AvTheme.RailInfo;
                campaignRail.color = colour;
                campaignPhase.text = CyberWords.Phase(phase);
                campaignPhase.color = colour;
                campaignClock.text = network.NextIncident > now ? "NEXT ~" + Clock((float)(network.NextIncident - now)) : "";
                heatFill.fillAmount = network.Heat / CyberNetwork.HeatMaximum;
                heatFill.color = colour;
                campaignNote.text = network.ExposedUntil > now
                    ? "EMITTERS EXPOSED TO " + support.CyberOriginName(network.ExposedOrigin) + " FOR " +
                      CyberWords.Seconds(network.ExposedUntil - now) + " · EMCON OR MOVE"
                    : "HEAT " + Mathf.RoundToInt(network.Heat) + " · YOUR OPERATIONS RAISE IT, EVERY INCIDENT YOU WIN LOWERS IT";
                footholdNote.text = Footholds(network, now);
            }

            int active = network != null ? network.ActiveIncidents(IncidentKind.None) : 0;
            incidentNote.text = active + "/" + incidentRows.Length + " ACTIVE · " +
                                (network != null ? network.Defended + "W / " + network.Breached + "L" : "NO NETWORK");
            for (int i = 0; i < incidentRows.Length; i++) RefreshIncidentRow(network, i, now);
        }

        private string Footholds(CyberNetwork network, double now)
        {
            string text = null;
            for (int i = 0; i < CyberNetwork.MaximumOrigins; i++)
            {
                if (!network.FootholdOn(i, now)) continue;
                text = (text == null ? "FOOTHOLD · " : text + " · ") + support.CyberOriginName(i) + " " +
                       Clock(network.FootholdRemaining(i, now));
            }
            return text ?? "NO FOOTHOLD · TRACE AN ATTACKER TO GET ONE";
        }

        private void RefreshIncidentRow(CyberNetwork network, int index, double now)
        {
            OpsRow row = incidentRows[index];
            CyberIncident incident = network != null ? network.Incident(index) : default;
            if (incident.Kind == IncidentKind.None)
            {
                row.Code.text = "---";
                row.Name.text = "NO INCIDENT";
                row.Detail.text = "";
                row.Value.text = "";
                row.Primary.gameObject.SetActive(false);
                row.Secondary.gameObject.SetActive(false);
                Paint(row, Tone.Locked, "QUIET");
                return;
            }

            bool active = incident.Outcome == IncidentOutcome.Active;
            string origin = support.CyberOriginName(incident.Origin);
            string where = network.Exists(incident.Site) ? CyberWords.Callsign(network, incident.Site) : "SECTOR";
            row.Code.text = CyberWords.IncidentCode(incident.Kind);
            row.Name.text = CyberWords.Incident(incident.Kind) + " · " + origin;
            row.Value.text = active ? CyberWords.Seconds(incident.Ends - now) : "";

            Tone tone;
            string status;
            if (!active)
            {
                tone = CyberWords.Won(incident.Outcome) ? Tone.Ready : Tone.Danger;
                status = CyberWords.Outcome(incident.Outcome) + " · " + where;
            }
            else if (incident.Tracing)
            {
                tone = Tone.Pending;
                status = "TRACING " + Mathf.RoundToInt(incident.Trace * 100f) + "% · " + where;
            }
            else
            {
                tone = incident.Kind == IncidentKind.Intrusion ? Tone.Danger : Tone.Armed;
                status = (incident.Held ? "HELD IN BAIT · " : "ACTIVE · ") + where;
            }
            row.Detail.text = Detail(incident, now);

            CyberVerb verb = incident.Kind == IncidentKind.Raid ? CyberVerb.BurnThrough : CyberVerb.Trace;
            bool answerable = active && (incident.Kind == IncidentKind.Raid || CyberNetwork.Traceable(incident.Kind));
            row.Primary.gameObject.SetActive(answerable);
            CyberDenial denial = answerable ? network.Check(verb, index, now) : CyberDenial.NoTarget;
            row.Primary.SetText(verb == CyberVerb.Trace ? "TRACE" : "BURN");
            row.Primary.SetEnabled(answerable && denial == CyberDenial.None && !support.CommandPending);
            row.Primary.WithTooltip(CyberWords.Verb(verb) + " — " + CyberWords.VerbHelp(verb) + " " +
                                    CyberWords.Denial(denial) + ".");

            bool isolatable = active && incident.Kind == IncidentKind.Intrusion && network.Exists(incident.Site);
            row.Secondary.gameObject.SetActive(isolatable);
            if (isolatable)
            {
                CyberDenial isolate = network.Check(CyberVerb.Isolate, incident.Site, now);
                row.Secondary.SetEnabled(isolate == CyberDenial.None && !support.CommandPending);
                row.Secondary.SetText(network.Site(incident.Site).Isolated ? "REJOIN" : "ISOLATE");
                row.Secondary.WithTooltip(CyberWords.Verb(CyberVerb.Isolate) + " " + where + " — " +
                                          CyberWords.VerbHelp(CyberVerb.Isolate) + " " + CyberWords.Denial(isolate) + ".");
            }
            Paint(row, tone, status);
        }

        private static string Detail(in CyberIncident incident, double now)
        {
            string age = "OPENED " + Clock((float)(now - incident.Started)) + " AGO";
            switch (incident.Kind)
            {
                case IncidentKind.Probe: return age + " · MAPPING YOUR EMITTERS; A SIGINT EAR ON IT BLOCKS IT.";
                case IncidentKind.Intrusion: return age + " · WALKS A LINK TOWARD CYBER COMMAND EVERY FEW SECONDS.";
                case IncidentKind.Raid: return age + " · SECTOR BARRAGE: LINKS AND RADAR COVER HALVED INSIDE.";
                default: return age + " · AN ENEMY OPERATION YOUR SIGINT HEARD; TRACE IT WHILE IT LASTS.";
            }
        }
    }
}
