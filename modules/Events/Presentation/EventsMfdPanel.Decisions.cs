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
        /// <summary>Four bounded response routes; quotes are read-only and the host validates every order.</summary>
        private sealed class DecisionBoard
        {
            public const float Height = 210f;
            private const float RowPitch = 43f;
            private readonly RectTransform root;
            private readonly TMP_Text heading;
            private readonly TMP_Text subheading;
            private readonly AvButton[] actions = new AvButton[4];
            private readonly Image[] rails = new Image[4];
            private readonly TMP_Text[] details = new TMP_Text[4];
            private readonly TMP_Text[] reasons = new TMP_Text[4];
            public bool Visible => root.gameObject.activeSelf;

            public DecisionBoard(RectTransform parent, float x, float y, float width)
            {
                var obj = new GameObject("DecisionBoard", typeof(RectTransform));
                root = (RectTransform)obj.transform;
                root.SetParent(parent, false);
                AvKit.Place(root, new Rect(x, y, width, Height));
                AvStyled.Box(root, new Rect(0f, 0f, width, Height), "card");
                AvKit.Panel(root, new Rect(0f, 0f, 3f, Height), AvTheme.RailInfo).raycastTarget = false;
                heading = AvStyled.Label(root, new Rect(12f, -9f, width * 0.5f, 15f),
                    "RESPONSE DESK", "section-title");
                subheading = AvStyled.Label(root, new Rect(width * 0.5f, -9f, width * 0.5f - 12f, 15f),
                    "CHOOSE ONE · HOST VERIFIED", "section-title-note",
                    align: TextAlignmentOptions.MidlineRight);
                AvKit.Rule(root, new Rect(11f, -30f, width - 22f, 1f), AvTheme.Hairline);

                for (int i = 0; i < actions.Length; i++)
                {
                    float top = -34f - i * RowPitch;
                    AvKit.Panel(root, new Rect(10f, top, width - 20f, 40f), AvTheme.SurfaceInert)
                        .raycastTarget = false;
                    rails[i] = AvKit.Panel(root, new Rect(10f, top, 3f, 40f), AvTheme.RailInert);
                    rails[i].raycastTarget = false;
                    actions[i] = AvStyled.Button(root, new Rect(19f, top - 5f, 174f, 30f),
                        "", "btn", null, AvButtonStyle.Default);
                    details[i] = AvStyled.Label(root,
                        new Rect(201f, top - 4f, width - 215f, 14f), "", "row-value");
                    reasons[i] = AvStyled.Label(root,
                        new Rect(201f, top - 21f, width - 215f, 14f), "", "row-sub");
                }
                SetStandby();
            }

            public void Place(float x, float y, float width) =>
                AvKit.Place(root, new Rect(x, y, width, Height));

            public void SetStandby()
            {
                root.gameObject.SetActive(false);
                heading.text = "RESPONSE DESK";
                subheading.text = "AWAITING DISPATCH";
                string[] labels = { "ALLOCATION", "TREASURY", "CONTRACT", "PILOT CHANNEL" };
                string[] notes = {
                    "PERSONAL ALLOCATION", "FACTION FUNDS", "COMPLETED SECONDARY", "RECON QUALIFICATION"
                };
                for (int i = 0; i < actions.Length; i++)
                {
                    actions[i].SetText(labels[i]);
                    actions[i].SetEnabled(false);
                    actions[i].SetAction(null);
                    rails[i].color = AvTheme.RailInert;
                    details[i].text = "STANDBY";
                    details[i].color = AvTheme.Dim;
                    reasons[i].text = notes[i];
                    reasons[i].color = AvTheme.Dim;
                }
            }

            public void Refresh(EventsManager events, bool aimedAtLocal)
            {
                root.gameObject.SetActive(true);
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

                EventResponseKind[] kinds = { allocation, EventResponseKind.Treasury,
                    EventResponseKind.Contract, EventResponseKind.Perk };
                for (int i = 0; i < kinds.Length; i++)
                {
                    EventResponseKind kind = kinds[i];
                    EventDecisionQuote quote = kind == EventResponseKind.None
                        ? default : events.Quote(kind);
                    bool chosen = priced && kind != EventResponseKind.None &&
                        (selected == kind || faction == kind);
                    bool enabled = priced && selected == EventResponseKind.None && quote.Available;
                    string name = i == 0 && kind == EventResponseKind.None
                        ? "ALLOCATION" : quote.Label;
                    actions[i].SetText(name);
                    actions[i].SetEnabled(enabled);
                    EventResponseKind route = kind;
                    actions[i].SetAction(enabled ? (Action)(() => events.RequestResponse(route)) : null);
                    string payment = kind == EventResponseKind.None ? "—" :
                        quote.Cost > 0 ? quote.Cost + " " + quote.Unit : quote.Unit;
                    string impact = kind == EventResponseKind.None ? "" :
                        " → x" + quote.EffectiveMultiplier.ToString("0.00");
                    details[i].text = chosen ? "ACTIVE" : priced ? payment + impact : "UNAVAILABLE";
                    details[i].color = chosen ? AvTheme.RailReady : enabled ? AvTheme.TextPrimary : AvTheme.Dim;
                    reasons[i].text = chosen ? (faction == kind ? "FACTION-WIDE · UNTIL EVENT ENDS" :
                        "PILOT · UNTIL EVENT ENDS") :
                        !priced ? "NO PRICE EFFECT ON YOUR SIDE" :
                        quote.Reason == "HOST CHECKS FACTION CONTRACT" ? quote.Reason :
                        quote.Available ? quote.Shared ? "FACTION-WIDE DIRECTIVE" : "PERSONAL DIRECTIVE" :
                        quote.Reason;
                    reasons[i].color = enabled || chosen ? AvTheme.Dim : AvTheme.RailCaution;
                    rails[i].color = chosen ? AvTheme.RailReady : enabled ? AvTheme.RailInfo : AvTheme.RailInert;
                    actions[i].WithTooltip(chosen ? "This response is in force for the rest of the event." :
                        !priced ? "This dispatch has no price effect for your side." :
                        quote.Available ? quote.Label + " costs " + payment +
                            " and changes this side's support price to x" +
                            quote.EffectiveMultiplier.ToString("0.00") + "." : quote.Reason);
                }
            }
        }
    }
}
