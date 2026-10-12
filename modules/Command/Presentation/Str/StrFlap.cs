using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// Split-flap digits for a countdown (lock, auto-pick, first review, wait): four framed digit cells around a colon. It shows what the model says
    /// and counts nothing itself; no digits at all stands for an unknown time. A part element, not a flow part.
    /// </summary>
    internal sealed class StrFlap
    {
        internal const float Width = 84f, Height = 22f;
        private const float Cell = 17f, Gap = 2f;

        private readonly RectTransform rect;
        private readonly AvFrame[] cells = new AvFrame[4];
        private readonly TMP_Text[] digits = new TMP_Text[4];
        private readonly TMP_Text colon;
        private string shown = "";

        internal StrFlap(RectTransform parent, string name)
        {
            rect = AvLay.Child(parent, name);
            for (int i = 0; i < 4; i++)
            {
                RectTransform box = AvLay.Child(rect, "Digit " + i);
                cells[i] = AvFrame.Add(box, "Frame", default(AvChamfer));
                AvLay.Fill(cells[i].rectTransform);
                digits[i] = AvText.Make(box, "Number", AvTextRole.DataStrong, "", TextAlignmentOptions.Center);
                AvLay.Fill(digits[i].rectTransform);
            }
            colon = AvText.Make(rect, "Colon", AvTextRole.DataStrong, ":", TextAlignmentOptions.Center);
            Restyle();
        }

        internal RectTransform Rect => rect;

        /// <summary>The four digits "mmss" (StrPanelText.Flap); "" shows nothing.</summary>
        internal void Set(string mmss)
        {
            mmss = mmss ?? "";
            if (mmss == shown) return;
            shown = mmss;
            for (int i = 0; i < 4; i++) digits[i].text = mmss.Length == 4 ? mmss[i].ToString() : "";
            colon.text = mmss.Length == 4 ? ":" : "";
        }

        internal void Place(float x, float y)
        {
            AvLay.Place(rect, x, y, Width, Height);
            float x0 = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (i == 2) x0 += 8f;
                AvLay.Place(cells[i].transform.parent as RectTransform, x0, 0f, Cell, Height);
                x0 += Cell + Gap;
            }
            AvLay.Place(colon.rectTransform, 2f * (Cell + Gap) - 1f, 0f, 10f, Height - 2f);
        }

        internal void SetActive(bool on)
        {
            if (rect.gameObject.activeSelf != on) rect.gameObject.SetActive(on);
        }

        internal void Restyle(StrInkRole role = StrInkRole.Effort)
        {
            Color ink = StrInk.Of(role);
            for (int i = 0; i < 4; i++)
            {
                cells[i].Paint(AvInk.Inert, ink.WithAlpha(0.55f));
                digits[i].color = AvInk.Ink;
            }
            colon.color = ink;
        }
    }
}
