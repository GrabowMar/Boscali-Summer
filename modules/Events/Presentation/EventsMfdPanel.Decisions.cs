using System;
using BoscaliSummer.Features.Events.Domain;
using BoscaliSummer.Features.Events.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Events.Presentation
{
    internal sealed partial class EventsMfdPanel
    {
        /// <summary>
        /// The response desk: four bounded routes (allocation / treasury / contract / pilot channel).
        /// Quotes are read-only; the host validates every order. The whole block collapses to nothing
        /// while no dispatch prices this player's side, the way <c>AvAlert</c> collapses when hidden.
        /// </summary>
        private sealed class DecisionBoardPart : AvPart
        {
            private const int Count = 4;
            private static readonly string[] StandbyLabel = { "ALLOCATION", "TREASURY", "CONTRACT", "PILOT CHANNEL" };
            private static readonly string[] StandbyNote =
                { "PERSONAL ALLOCATION", "FACTION FUNDS", "COMPLETED SECONDARY", "RECON QUALIFICATION" };

            private readonly AvFrame frame;
            private readonly TMP_Text heading, subheading;
            private readonly Image hairline;
            private readonly AvRow[] rows;
            private readonly EventResponseKind[] routes = new EventResponseKind[Count];
            private readonly Action<EventResponseKind> request;

            public DecisionBoardPart(RectTransform parent, Action<EventResponseKind> onRequest)
            {
                request = onRequest;
                Rect = AvLay.Child(parent, "DecisionBoard");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
                AvLay.Fill(frame.rectTransform);
                heading = AvText.Make(Rect, "Heading", AvTextRole.Head, "RESPONSE DESK");
                subheading = AvText.Make(Rect, "Subheading", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
                hairline = AvLay.Solid(Rect, "Rule", Color.clear);
                rows = new AvRow[Count];
                for (int i = 0; i < Count; i++)
                {
                    int index = i;
                    rows[i] = new AvRow(Rect, () =>
                    {
                        if (routes[index] != EventResponseKind.None) request?.Invoke(routes[index]);
                    });
                }
                SetStandby();
            }

            public bool Visible => Rect.gameObject.activeSelf;

            public override float Measure(float width) => !Visible ? 0f : ComputeHeight(width);

            private float ComputeHeight(float width)
            {
                float rowW = width - 24f, h = 32f;
                for (int i = 0; i < rows.Length; i++) h += rows[i].Measure(rowW) + 4f;
                return h + 8f;
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                if (!Visible) return;
                AvLay.Place(heading.rectTransform, 12f, 8f, s.W * 0.5f - 12f, 16f);
                AvLay.Place(subheading.rectTransform, s.W * 0.5f, 8f, s.W * 0.5f - 12f, 16f);
                AvLay.Place(hairline.rectTransform, 12f, 28f, s.W - 24f, 1f);
                float y = 32f, rowW = s.W - 24f;
                for (int i = 0; i < rows.Length; i++)
                {
                    float h = rows[i].Measure(rowW);
                    rows[i].Place(new AvSlot(12f, y, rowW, h));
                    y += h + 4f;
                }
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                heading.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                subheading.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                hairline.color = AvStyleHost.FuiColor("hairline", AvTheme.Hairline);
                foreach (AvRow row in rows) row.Restyle();
            }

            public void SetStandby()
            {
                Rect.gameObject.SetActive(false);
                heading.text = "RESPONSE DESK";
                subheading.text = "AWAITING DISPATCH";
                for (int i = 0; i < Count; i++)
                {
                    routes[i] = EventResponseKind.None;
                    rows[i].Set(StandbyLabel[i], StandbyNote[i], "STANDBY", AvState.Inert);
                    rows[i].Interactable = false;
                }
            }

            public void Refresh(EventsManager events, bool aimedAtLocal)
            {
                Rect.gameObject.SetActive(true);
                EventResponseKind selected = events.LocalResponse;
                EventResponseKind faction = events.LocalFactionResponse;
                EventResponseKind allocation = EventSelector.ResponseKind(events.LocalBaseMultiplier);
                bool priced = aimedAtLocal && allocation != EventResponseKind.None;

                heading.text = selected == EventResponseKind.None ? "RESPONSE DESK" : "DIRECTIVE IN FORCE";
                subheading.text = selected == EventResponseKind.None
                    ? priced ? "CHOOSE ONE RESPONSE" : "NO PRICE DIRECTIVE"
                    : faction != EventResponseKind.None && faction != selected
                        ? "PILOT + FACTION DIRECTIVES"
                        : EventsManager.ResponseLabel(selected) + " · COMMITTED";

                EventResponseKind[] kinds =
                    { allocation, EventResponseKind.Treasury, EventResponseKind.Contract, EventResponseKind.Perk };
                for (int i = 0; i < kinds.Length; i++)
                {
                    EventResponseKind kind = kinds[i];
                    EventDecisionQuote quote = kind == EventResponseKind.None ? default : events.Quote(kind);
                    bool chosen = priced && kind != EventResponseKind.None && (selected == kind || faction == kind);
                    bool enabled = priced && selected == EventResponseKind.None && quote.Available;
                    string name = i == 0 && kind == EventResponseKind.None ? "ALLOCATION" : quote.Label;

                    routes[i] = enabled ? kind : EventResponseKind.None;
                    rows[i].Interactable = enabled;

                    string payment = kind == EventResponseKind.None ? "—"
                        : quote.Cost > 0 ? AvNum.Fixed(quote.Cost, 0) + " " + quote.Unit : quote.Unit;
                    string impact = kind == EventResponseKind.None ? ""
                        : " → x" + AvNum.Fixed(quote.EffectiveMultiplier, 2);
                    string detail = chosen ? "ACTIVE" : priced ? payment + impact : "UNAVAILABLE";
                    string reason = chosen
                        ? faction == kind ? "FACTION-WIDE · UNTIL EVENT ENDS" : "PILOT · UNTIL EVENT ENDS"
                        : !priced ? "NO PRICE EFFECT ON YOUR SIDE"
                        : quote.Reason == "HOST CHECKS FACTION CONTRACT" ? quote.Reason
                        : quote.Available ? quote.Shared ? "FACTION-WIDE DIRECTIVE" : "PERSONAL DIRECTIVE"
                        : quote.Reason;

                    AvState state = chosen ? AvState.Ready : enabled ? AvState.Info : AvState.Inert;
                    rows[i].Set(name, reason, detail, state);
                    rows[i].Help = chosen ? "This response is in force for the rest of the event." :
                        !priced ? "This dispatch has no price effect for your side." :
                        quote.Available ? quote.Label + " costs " + payment +
                            " and changes this side's support price to x" +
                            AvNum.Fixed(quote.EffectiveMultiplier, 2) + "." : quote.Reason;
                }
            }
        }
    }
}
