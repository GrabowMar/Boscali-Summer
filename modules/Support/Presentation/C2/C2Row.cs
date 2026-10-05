using System;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>
    /// One fixed-height capability / post row: index (+ optional pin button), name with a reason chip, optional mono sub-line,
    /// price, state word, one primary button and an optional extra button. Nothing wraps and the height never changes, so a
    /// refresh cannot move the page. Every string is fitted to its slot with an ellipsis.
    /// </summary>
    internal sealed class C2Row : AvPart
    {
        private const float PadX = 6f, RailW = 3f, PinW = 22f, IndexW = 22f, PrimaryW = 78f, ExtraW = 52f, StateW = 64f, PriceW = 52f, Gap = 4f, CtlH = 22f, ExtraH = 18f;
        private readonly float rowHeight;
        private readonly bool subLine;
        private readonly AvFrame frame, chipFrame;
        private readonly Image rail;
        private readonly TMP_Text indexText, nameText, chipText, subText, priceText, stateText;
        private readonly AvControl pin;
        private AvHelpTip helpTip;
        private Action pinAction, extraAction;
        private float width = AvTokens.PanelWidth;
        private AvState chipTone = AvState.Ready, stateTone = AvState.Inert;
        private bool armed, pinShown, extraShown;
        private string indexRaw = "", nameRaw = "", chipRaw = "", subRaw = "", priceRaw = "", stateRaw = "", helpRaw = "", chipOverflowTip;
        private string primaryLabelRaw;
        private AvButtonStyle primaryStyleRaw;
        private bool primaryEnabledRaw, hasSet;

        public C2Row(RectTransform parent, float height, bool subLine)
        {
            this.subLine = subLine;
            rowHeight = Mathf.Max(24f, height);
            Rect = AvLay.Child(parent, "C2Row");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            indexText = C2Kit.Mono(Rect, "Index", 10f, TextAlignmentOptions.MidlineLeft);
            nameText = C2Kit.Cond(Rect, "Name", AvTextRole.Label, 12f, TextAlignmentOptions.MidlineLeft);
            chipFrame = AvFrame.Add(Rect, "ChipFrame", default(AvChamfer));
            chipText = C2Kit.Mono(Rect, "Chip", 10f, TextAlignmentOptions.Center);
            subText = C2Kit.Mono(Rect, "Sub", 10f, TextAlignmentOptions.MidlineLeft);
            priceText = C2Kit.Mono(Rect, "Price", 12f, TextAlignmentOptions.MidlineRight, true);
            stateText = C2Kit.Mono(Rect, "State", 10f, TextAlignmentOptions.MidlineRight, true, 2f);
            pin = AvControl.Make(Rect, new AvControl.Spec("", () => pinAction?.Invoke(), AvButtonStyle.Quiet, AvIcon.Star));
            pin.Rect.gameObject.SetActive(false);
            Primary = AvControl.Make(Rect, new AvControl.Spec("", null, AvButtonStyle.Primary));
            Primary.SingleLine();
            Extra = AvControl.Make(Rect, new AvControl.Spec("", () => extraAction?.Invoke(), AvButtonStyle.Quiet));
            Extra.SingleLine();
            Extra.Rect.gameObject.SetActive(false);
            Restyle();
            Layout();
        }

        /// <summary>The one action of the row (AUTHORIZE / EXECUTE / DENIED / CLAIM). Subscribe to <c>Clicked</c>.</summary>
        public AvControl Primary { get; }

        /// <summary>A second, quiet action (UNLASE). Hidden until <see cref="SetExtra"/>.</summary>
        public AvControl Extra { get; }

        /// <summary>True while this row's call is armed: armed row style (the primary button keeps its own style, so a danger EXECUTE stays red).</summary>
        public bool Armed
        {
            get => armed;
            set { if (armed == value) return; armed = value; Restyle(); }
        }

        public void Set(string index, string name, string chip, AvState chipState, string sub, string price, string state, AvState stateState,
            string primaryLabel, AvButtonStyle primaryStyle, bool primaryEnabled)
        {
            // A refresh that changes nothing must not re-fit every string (the page refreshes several times a second).
            if (hasSet && indexRaw == (index ?? "") && nameRaw == (name ?? "") && chipRaw == (chip ?? "") && subRaw == (sub ?? "") &&
                priceRaw == (price ?? "") && stateRaw == (state ?? "") && chipTone == chipState && stateTone == stateState &&
                primaryLabelRaw == (primaryLabel ?? "") && primaryStyleRaw == primaryStyle && primaryEnabledRaw == primaryEnabled) return;
            hasSet = true;
            primaryLabelRaw = primaryLabel ?? ""; primaryStyleRaw = primaryStyle; primaryEnabledRaw = primaryEnabled;
            indexRaw = index ?? ""; nameRaw = name ?? ""; chipRaw = chip ?? ""; subRaw = sub ?? ""; priceRaw = price ?? ""; stateRaw = state ?? "";
            chipTone = chipState;
            bool toneChanged = stateTone != stateState;
            stateTone = stateState;
            Primary.Label = primaryLabel ?? "";
            Primary.SetStyle(primaryStyle);
            Primary.Interactable = primaryEnabled;
            if (toneChanged) Restyle();
            else RestyleTexts();
            Layout();
        }

        /// <summary>Show the extra button with this label (null or empty hides it).</summary>
        public void SetExtra(string label, Action onPress)
        {
            extraAction = onPress;
            extraShown = !string.IsNullOrEmpty(label);
            Extra.Label = label ?? "";
            Extra.Rect.gameObject.SetActive(extraShown);
            Layout();
        }

        /// <summary>Show the star pin button; <paramref name="pinnedNow"/> latches it.</summary>
        public void SetPin(bool pinnedNow, Action onPress)
        {
            if (pinShown == (onPress != null) && pin.Latched == pinnedNow && pinAction == onPress) return;
            pinAction = onPress;
            pinShown = onPress != null;
            pin.Rect.gameObject.SetActive(pinShown);
            pin.Latched = pinnedNow;
            Layout();
        }

        /// <summary>
        /// Hover help for the whole row and for its pin button; both show in the footer of the owning console. The row help is
        /// followed by price and reason when the reason chip had to be dropped for room.
        /// </summary>
        public void SetHelp(string rowHelp, string pinHelp)
        {
            string r = rowHelp ?? "";
            if (r != helpRaw) { helpRaw = r; ApplyTip(); }
            if (pin.Help != pinHelp) pin.Help = pinHelp;
        }

        public override float Measure(float w) => rowHeight;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            Layout();
        }

        private void Layout()
        {
            float w = width, h = rowHeight;
            AvLay.Place(frame.rectTransform, 0f, 0f, w, h);
            AvLay.Place(rail.rectTransform, 0f, 0f, RailW, h);

            // Right cluster, right to left. The extra button (UNLASE) is docked to the primary: stacked under it in a tall row (every
            // row keeps its price and state columns), or directly to its left in a short one.
            float x = w - PadX;
            x -= PrimaryW;
            bool stack = extraShown && h >= 44f;
            if (stack)
            {
                float top = (h - (CtlH + 3f + ExtraH)) * 0.5f;
                AvLay.Place(Primary.Rect, x, top, PrimaryW, CtlH);
                AvLay.Place(Extra.Rect, x, top + CtlH + 3f, PrimaryW, ExtraH);
            }
            else
            {
                AvLay.Place(Primary.Rect, x, (h - CtlH) * 0.5f, PrimaryW, CtlH);
                if (extraShown)
                {
                    x -= Gap + ExtraW;
                    AvLay.Place(Extra.Rect, x, (h - CtlH) * 0.5f, ExtraW, CtlH);
                }
            }
            x -= Gap;
            x -= StateW;
            OpsText.Set(stateText, C2Kit.FitTo(stateText, stateRaw, StateW));
            C2Kit.Place(stateText, x, 0f, StateW, h);
            x -= Gap + PriceW;
            OpsText.Set(priceText, C2Kit.FitTo(priceText, priceRaw, PriceW));
            C2Kit.Place(priceText, x, 0f, PriceW, h);
            float right = x - 6f;

            // Left cluster.
            float left = RailW + 5f;
            if (pinShown)
            {
                AvLay.Place(pin.Rect, left, (h - 20f) * 0.5f, PinW, 20f);
                left += PinW + 3f;
            }
            OpsText.Set(indexText, C2Kit.FitTo(indexText, indexRaw, IndexW + 6f));
            C2Kit.Place(indexText, left, 0f, IndexW + 6f, h);
            left += IndexW + 8f;

            float area = Mathf.Max(20f, right - left);
            // With a sub-line the name and the sub-line form one 32 px block centred in the row, so a tall row never airs them apart.
            float nameY = subLine ? Mathf.Floor((h - 32f) * 0.5f) : 0f, nameH = subLine ? 16f : h;
            string name = nameRaw;
            float nameW = C2Kit.Width(nameText, name);
            float chipW = chipRaw.Length > 0 ? C2Kit.Width(chipText, chipRaw) + 10f : 0f;
            bool chip = chipW > 0f && nameW + 6f + chipW <= area;
            if (!chip && nameW > area) name = C2Kit.FitTo(nameText, nameRaw, area);
            if (chipW > 0f && !chip) SetTip(priceRaw + " \u00B7 " + chipRaw); else SetTip(null);
            OpsText.Set(nameText, name);
            nameW = Mathf.Min(area, C2Kit.Width(nameText, name) + 2f);
            C2Kit.Place(nameText, left, nameY, chip ? nameW : area, nameH);
            chipFrame.gameObject.SetActive(chip);
            chipText.gameObject.SetActive(chip);
            if (chip)
            {
                OpsText.Set(chipText, chipRaw);
                float cy = nameY + (nameH - 14f) * 0.5f;
                AvLay.Place(chipFrame.rectTransform, left + nameW + 6f, cy, chipW, 14f);
                C2Kit.Place(chipText, left + nameW + 6f, cy, chipW, 14f);
            }
            subText.gameObject.SetActive(subLine && subRaw.Length > 0);
            if (subLine)
            {
                float subArea = Mathf.Max(20f, right - left);
                OpsText.Set(subText, C2Kit.FitTo(subText, subRaw, subArea));
                C2Kit.Place(subText, left, nameY + 18f, subArea, 14f);
            }
        }

        private void SetTip(string overflow)
        {
            if (overflow == chipOverflowTip) return;
            chipOverflowTip = overflow;
            ApplyTip();
        }

        private void ApplyTip()
        {
            string tip = helpRaw.Length > 0 && chipOverflowTip != null ? helpRaw + " · " + chipOverflowTip
                : helpRaw.Length > 0 ? helpRaw : chipOverflowTip;
            if (helpTip == null && tip == null) return;
            frame.raycastTarget = tip != null;
            helpTip = AvHelpTip.Attach(frame.gameObject, tip ?? "");
        }

        private void RestyleTexts()
        {
            indexText.color = OpsInk.Muted;
            nameText.color = Primary.Interactable || stateTone != AvState.Inert ? OpsInk.Ink : OpsInk.Muted;
            subText.color = OpsInk.Dim;
            priceText.color = OpsInk.Word(stateTone == AvState.Inert ? AvState.Info : stateTone);
            stateText.color = OpsInk.Word(stateTone);
            chipText.color = OpsInk.Word(chipTone);
            chipFrame.Paint(OpsInk.Inert, OpsInk.Rail(chipTone));
        }

        public override void Restyle()
        {
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(stateTone), armed ? "armed" : null);
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            rail.color = armed ? OpsInk.Select : OpsInk.Rail(stateTone);
            RestyleTexts();
            Primary.Restyle();
            Extra.Restyle();
            pin.Restyle();
        }
    }
}
