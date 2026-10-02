using NOAvionics;
using System;
using BoscaliSummer.Modules.Events.Domain;
using BoscaliSummer.Modules.Events.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Events.Presentation
{
    internal sealed partial class EventsMfdPanel
    {
        /// <summary>
        /// The response desk: four bounded routes (allocation / treasury / contract / pilot channel) as a 2x2
        /// grid of compact choice cards. Each card carries the route name and scope beside the support price it
        /// would set, its cost and one CHOOSE action; the chosen route latches in the game's accent and reads
        /// IN FORCE. What the price means rides on the CHOOSE button's hover help.
        /// Quotes are read-only; the host validates every order. When the dispatch does not price this side
        /// the desk is one compact note instead of four locked cards.
        /// </summary>
        private sealed class ResponseDeskPart : AvPart
        {
            private const int Count = 4;
            private const float Gap = 8f;

            private readonly ChoiceCard[] cards = new ChoiceCard[Count];
            private readonly EventResponseKind[] routes = new EventResponseKind[Count];
            private readonly Action<EventResponseKind> request;
            private readonly AvFrame noteFrame;
            private readonly TMP_Text noteIcon, noteTitle, noteBody;
            private bool noteMode = true;

            public ResponseDeskPart(RectTransform parent, Action<EventResponseKind> onRequest)
            {
                request = onRequest;
                Rect = AvLay.Child(parent, "ResponseDesk");
                for (int i = 0; i < Count; i++)
                {
                    int index = i;
                    cards[i] = new ChoiceCard(Rect, () =>
                    {
                        if (routes[index] != EventResponseKind.None) request?.Invoke(routes[index]);
                    });
                }
                noteFrame = AvFrame.Add(Rect, "NoteFrame", AvChamfer.Diagonal(6f));
                noteIcon = AvIcons.Make(Rect, AvIcon.InfoCircle, AvGridTokens.IconTool, Color.white);
                noteTitle = AvText.Make(Rect, "NoteTitle", AvTextRole.Head, "", TextAlignmentOptions.TopLeft, true);
                noteBody = AvText.Make(Rect, "NoteBody", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
                SetNote("NO RESPONSE NEEDED", "");
                Restyle();
            }

            private void SetNote(string headline, string body)
            {
                bool wasNote = noteMode;
                noteMode = true;
                noteFrame.gameObject.SetActive(true);
                noteIcon.gameObject.SetActive(true);
                noteTitle.gameObject.SetActive(true);
                noteBody.gameObject.SetActive(true);
                foreach (ChoiceCard card in cards) card.Root.gameObject.SetActive(false);
                bool changed = !wasNote || noteTitle.text != headline || noteBody.text != body;
                noteTitle.text = headline;
                noteBody.text = body;
                if (changed) Changed();
            }

            private void ShowCards()
            {
                if (!noteMode) return;
                noteMode = false;
                noteFrame.gameObject.SetActive(false);
                noteIcon.gameObject.SetActive(false);
                noteTitle.gameObject.SetActive(false);
                noteBody.gameObject.SetActive(false);
                foreach (ChoiceCard card in cards) card.Root.gameObject.SetActive(true);
                Changed();
            }

            public override float Measure(float width)
            {
                if (noteMode)
                {
                    float tw = width - 24f - 32f;
                    return 12f + AvText.Height(noteTitle, tw) +
                        (noteBody.text.Length > 0 ? 4f + AvText.Height(noteBody, tw) : 0f) + 12f;
                }
                float cw = (width - Gap) * 0.5f;
                float total = 0f;
                for (int row = 0; row < Count / 2; row++)
                {
                    float h = Mathf.Max(cards[row * 2].Measure(cw), cards[row * 2 + 1].Measure(cw));
                    total += h + (row > 0 ? Gap : 0f);
                }
                return total;
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                if (noteMode)
                {
                    float tw = s.W - 24f - 32f;
                    AvLay.Fill(noteFrame.rectTransform);
                    AvLay.Place(noteIcon.rectTransform, 12f, 12f, 20f, 20f);
                    float th = AvText.Height(noteTitle, tw);
                    AvLay.Place(noteTitle.rectTransform, 44f, 12f, tw, th);
                    AvLay.Place(noteBody.rectTransform, 44f, 16f + th, tw,
                        noteBody.text.Length > 0 ? AvText.Height(noteBody, tw) : 0f);
                    return;
                }
                float cw = (s.W - Gap) * 0.5f, y = 0f;
                for (int row = 0; row < Count / 2; row++)
                {
                    float h = Mathf.Max(cards[row * 2].Measure(cw), cards[row * 2 + 1].Measure(cw));
                    cards[row * 2].Place(0f, y, cw, h);
                    cards[row * 2 + 1].Place(cw + Gap, y, cw, h);
                    y += h + Gap;
                }
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card inert");
                noteFrame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                noteIcon.color = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
                noteTitle.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                noteBody.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                foreach (ChoiceCard card in cards) card.Restyle();
            }

            /// <summary>No dispatch prices this side: the whole desk collapses (the owner hides the part).</summary>
            public void SetStandby()
            {
                for (int i = 0; i < Count; i++) routes[i] = EventResponseKind.None;
                SetNote("NO RESPONSE NEEDED", "");
            }

            /// <summary>Refresh from the live quotes; returns the section caption.</summary>
            public string Refresh(EventsManager events, bool aimedAtLocal)
            {
                EventResponseKind selected = events.LocalResponse;
                EventResponseKind faction = events.LocalFactionResponse;
                EventResponseKind allocation = EventSelector.ResponseKind(events.LocalBaseMultiplier);
                bool priced = aimedAtLocal && allocation != EventResponseKind.None;

                string caption = selected == EventResponseKind.None
                    ? priced ? "CHOOSE ONE" : "NO PRICE"
                    : faction != EventResponseKind.None && faction != selected
                        ? "PILOT + FACTION"
                        : EventsManager.ResponseLabel(selected) + " · COMMITTED";

                if (!priced)
                {
                    for (int i = 0; i < Count; i++) routes[i] = EventResponseKind.None;
                    SetNote("NO RESPONSE NEEDED", aimedAtLocal ? "FLAVOUR ONLY" : "OTHER SIDE");
                    return caption;
                }

                ShowCards();
                bool grew = false;
                EventResponseKind[] kinds =
                    { allocation, EventResponseKind.Treasury, EventResponseKind.Contract, EventResponseKind.Perk };
                for (int i = 0; i < kinds.Length; i++)
                {
                    EventResponseKind kind = kinds[i];
                    EventDecisionQuote quote = events.Quote(kind);
                    bool chosen = selected == kind || faction == kind;
                    bool enabled = selected == EventResponseKind.None && quote.Available;
                    routes[i] = enabled ? kind : EventResponseKind.None;

                    string payment = quote.Cost > 0 ? AvNum.Fixed(quote.Cost, 0) + " " + quote.Unit : quote.Unit;
                    string scope = quote.Shared ? "FACTION-WIDE" : "PERSONAL";
                    string note = chosen ? "IN FORCE" : quote.Reason ?? "";
                    string help = chosen ? "This response is in force for the rest of the event." :
                        quote.Available ? quote.Label + " costs " + payment +
                            " and changes this side's support price to x" +
                            AvNum.Fixed(quote.EffectiveMultiplier, 2) + "." : quote.Reason;

                    grew |= cards[i].Set(string.IsNullOrEmpty(quote.Label) ? "ALLOCATION" : quote.Label.ToUpperInvariant(),
                        scope, "x" + AvNum.Fixed(quote.EffectiveMultiplier, 2), quote.Cost > 0 ? "COST " + payment : payment, note,
                        chosen, enabled, help);
                }
                if (grew) Changed();
                return caption;
            }

            /// <summary>One route: name, scope, price it would set, cost, reason and the CHOOSE action.</summary>
            private sealed class ChoiceCard
            {
                private const float PadX = 10f, ButtonHeight = 28f;
                private readonly AvFrame frame;
                private readonly Image rail;
                private readonly TMP_Text name, scope, effect, cost, note;
                private readonly AvControl button;
                private bool chosen, enabled;

                public RectTransform Root { get; }

                public ChoiceCard(RectTransform parent, Action onChoose)
                {
                    Root = AvLay.Child(parent, "Choice");
                    frame = AvFrame.Add(Root, "Frame", AvChamfer.Diagonal(6f));
                    AvLay.Fill(frame.rectTransform);
                    rail = AvLay.Solid(Root, "Rail", Color.clear);
                    name = AvText.Make(Root, "Name", AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
                    scope = AvText.Make(Root, "Scope", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
                    effect = AvText.Make(Root, "Effect", AvTextRole.Display, "", TextAlignmentOptions.MidlineRight);
                    AvText.Fit(effect, false);
                    cost = AvText.Make(Root, "Cost", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineLeft, true);
                    note = AvText.Make(Root, "Note", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                    button = AvControl.Make(Root, new AvControl.Spec("CHOOSE", onChoose, AvButtonStyle.Primary));
                    Restyle();
                }

                /// <summary>True when text changed in a way that can change the card's height.</summary>
                public bool Set(string nameText, string scopeText, string effectText, string costText, string noteText,
                    bool isChosen, bool isEnabled, string help)
                {
                    bool grew = name.text != nameText || scope.text != scopeText || cost.text != costText || note.text != noteText;
                    name.text = nameText;
                    scope.text = scopeText;
                    effect.text = effectText;
                    cost.text = costText;
                    note.text = noteText;
                    chosen = isChosen;
                    enabled = isEnabled;
                    button.Label = isChosen ? "IN FORCE" : isEnabled ? "CHOOSE" : "LOCKED";
                    button.Interactable = isEnabled;
                    button.Latched = isChosen;
                    button.Help = help;
                    Restyle();
                    return grew;
                }

                // Name and scope stack on the left; the price it would set is the big number on the right.
                private float PriceWidth => Mathf.Ceil(AvText.Width(effect)) + 4f;

                public float Measure(float width)
                {
                    float w = width - 2f * PadX;
                    float nameW = Mathf.Max(30f, w - PriceWidth - 6f);
                    float head = Mathf.Max(32f, AvText.Height(name, nameW) + 1f + AvText.Height(scope, nameW));
                    float h = 8f + head + 2f + 16f;
                    if (note.text.Length > 0) h += 2f + AvText.Height(note, w);
                    return h + 6f + ButtonHeight + 8f;
                }

                public void Place(float x, float y, float width, float height)
                {
                    AvLay.Place(Root, x, y, width, height);
                    float w = width - 2f * PadX, cy = 8f;
                    AvLay.Place(rail.rectTransform, 0f, 0f, 2f, height);
                    float pw = PriceWidth, nameW = Mathf.Max(30f, w - pw - 6f);
                    float nh = AvText.Height(name, nameW), sh = AvText.Height(scope, nameW);
                    float head = Mathf.Max(32f, nh + 1f + sh);
                    AvLay.Place(name.rectTransform, PadX, cy, nameW, nh);
                    AvLay.Place(scope.rectTransform, PadX, cy + nh + 1f, nameW, sh);
                    AvLay.Place(effect.rectTransform, PadX + w - pw, cy, pw, 32f);
                    cy += head + 2f;
                    AvLay.Place(cost.rectTransform, PadX, cy, w, 16f);
                    cy += 16f;
                    float noteH = note.text.Length > 0 ? AvText.Height(note, w) : 0f;
                    AvLay.Place(note.rectTransform, PadX, cy + (noteH > 0f ? 2f : 0f), w, noteH);
                    AvLay.Place(button.Rect, PadX, height - 8f - ButtonHeight, w, ButtonHeight);
                }

                public void Restyle()
                {
                    AvStyle c = chosen ? AvStyleHost.FuiStyle("cell", "on")
                        : AvStyleHost.FuiStyle(enabled ? "card" : "card inert");
                    frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                    rail.color = chosen ? AvStyleHost.FuiColor("select", AvTheme.Accent)
                        : enabled ? AvStyleHost.FuiColor("info", AvTheme.RailInfo) : AvStyleHost.FuiColor("inert", AvTheme.RailInert);
                    name.color = enabled || chosen ? AvStyleHost.FuiColor("ink", AvTheme.TextPrimary) : AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                    scope.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                    effect.color = chosen ? AvStyleHost.FuiColor("select", AvTheme.Accent)
                        : enabled ? AvStyleHost.FuiColor("ready", AvTheme.RailInfo) : AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                    cost.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
                    note.color = chosen ? AvStyleHost.FuiColor("ink", AvTheme.TextPrimary) : AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
                    button.Restyle();
                }
            }
        }
    }
}
