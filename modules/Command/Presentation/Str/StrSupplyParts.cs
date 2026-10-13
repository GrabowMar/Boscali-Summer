using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// The pilot's own funds and four shares of them to give to the faction pool. A press is the game's own donation; the host takes one a minute,
    /// which the gate says before it is pressed.
    /// </summary>
    internal sealed class StrDonateRow : StrPart
    {
        private static readonly int[] Shares = { 10, 25, 50, 100 };
        private static readonly string[] Words = { "10%", "25%", "50%", "ALL" };
        private const float Pad = 2f, ButtonH = 26f, ButtonW = 62f, LabelW = 84f;

        private readonly TMP_Text label, funds, caption;
        private readonly StrButton[] buttons = new StrButton[4];

        internal StrDonateRow(RectTransform parent, StrControls controls, IStrHub hub) : base(parent, "Donate", controls, hub)
        {
            label = Text(Rect, "Label", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            label.text = "YOUR FUNDS";
            funds = Text(Rect, "val.funds", AvTextRole.DataStrong, TextAlignmentOptions.MidlineLeft);
            caption = Text(Rect, "Caption", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true);
            for (int i = 0; i < buttons.Length; i++)
            {
                int share = Shares[i];
                buttons[i] = new StrButton(Rect, Words[i], AvButtonStyle.Default, () => Hub.Run(StrActionKind.Donate, StrItemId.None, share));
            }
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            StrSupplyView s = vm.Supply;
            AvText.Set(funds, s.FundsText);
            for (int i = 0; i < buttons.Length; i++)
            {
                int share = Shares[i];
                buttons[i].Bind(Controls, StrControlIds.SupplyGive(share), Words[i], () => StrGate.Check(Vm, StrActionKind.Donate, StrItemId.None, share),
                    "Give " + (share == 100 ? "all" : share + "%") + " of your funds to the faction pool. The staff pays its reinforcement waves from the pool.");
            }
            AvText.Set(caption, s.Note.Length > 0 ? s.Note : "GIVE A SHARE TO THE POOL, OR BUY A GROUP BELOW");
            Changed();
        }

        public override float Measure(float width) => ButtonH + 4f + Mathf.Max(15f, AvText.Height(caption, width - 2f * Pad)) + 2f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float x = s.W - Pad - buttons.Length * (ButtonW + 2f) + 2f;
            AvLay.Place(label, Pad, 0f, LabelW, ButtonH);
            AvLay.Place(funds, Pad + LabelW, 0f, Mathf.Max(30f, x - Pad - LabelW - 6f), ButtonH);
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Place(x, 0f, ButtonW, ButtonH);
                x += ButtonW + 2f;
            }
            AvLay.Place(caption, Pad, ButtonH + 4f, s.W - 2f * Pad, Mathf.Max(15f, AvText.Height(caption, s.W - 2f * Pad)));
        }

        public override void Restyle()
        {
            label.color = AvInk.Dim;
            funds.color = AvInk.Ink;
            caption.color = AvInk.Dim;
            foreach (StrButton b in buttons) b.Restyle();
        }
    }

    /// <summary>One group of the mission's own convoy list: name, what is in it, price, whether it can be bought now, and BUY (the game's own purchase).</summary>
    internal sealed class StrConvoyRow : StrPart
    {
        private const float Pad = 8f, ButtonW = 88f, CostW = 64f;

        private readonly int index;
        private readonly AvFrame frame;
        private readonly TMP_Text name, detail, cost, state;
        private readonly StrButton buy;
        private StrSupplyRow row;

        internal StrConvoyRow(RectTransform parent, StrControls controls, IStrHub hub, int ordinal) : base(parent, "Convoy " + (ordinal + 1), controls, hub)
        {
            index = ordinal;
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            name = Text(Rect, "Name", AvTextRole.Head, TextAlignmentOptions.MidlineLeft);
            detail = Text(Rect, "Detail", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true);
            cost = Text(Rect, "val.cost", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
            state = Text(Rect, "State", AvTextRole.Micro, TextAlignmentOptions.Center);
            buy = new StrButton(Rect, "BUY", AvButtonStyle.Primary, () => Hub.Run(StrActionKind.BuyConvoy, StrItemId.None, index));
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            row = index < vm.Supply.RowCount ? vm.Supply.Rows[index] : null;
            SetShown(row != null);
            if (row == null) return;
            AvText.Set(name, row.Name);
            AvText.Set(detail, row.Detail);
            AvText.Set(cost, row.CostText);
            AvText.Set(state, row.StateText);
            buy.Bind(Controls, StrControlIds.SupplyBuy(index + 1), "BUY", () => StrGate.Check(Vm, StrActionKind.BuyConvoy, StrItemId.None, index),
                "Buy this group with your own funds. A depot of your side delivers it and the AI takes it to the front.");
            Paint();
            Changed();
        }

        public override float Measure(float width) => Shown ? Lay(width, false) : 0f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (Shown) Lay(s.W, true);
        }

        private float Lay(float width, bool place)
        {
            float right = width - Pad - ButtonW, textW = Mathf.Max(40f, right - 8f - Pad);
            float detailH = detail.text.Length > 0 ? Mathf.Max(15f, AvText.Height(detail, textW)) : 0f;
            float h = Mathf.Max(Pad + 20f + 2f + detailH + Pad, Pad + 26f + 2f + 16f + Pad);
            if (!place) return h;
            AvLay.Place(name, Pad, Pad, Mathf.Max(30f, textW - CostW - 6f), 20f);
            AvLay.Place(cost, Pad + textW - CostW, Pad, CostW, 20f);
            AvLay.Place(detail, Pad, Pad + 22f, textW, detailH);
            buy.Place(right, Pad, ButtonW, 26f);
            AvLay.Place(state, right, Pad + 28f, ButtonW, 16f);
            return h;
        }

        private void Paint()
        {
            bool ready = row != null && row.Ready;
            frame.Paint(AvInk.Inert, ready ? AvInk.Effort.WithAlpha(0.45f) : AvInk.Hairline);
            name.color = AvInk.Ink;
            detail.color = AvInk.Dim;
            cost.color = AvInk.Ink;
            state.color = ready ? AvInk.State(AvState.Ready) : AvInk.Dim;
        }

        public override void Restyle()
        {
            Paint();
            buy.Restyle();
        }
    }
}
