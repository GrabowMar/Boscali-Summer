using System;
using TMPro;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>Text input: navigation stripped, Rewired keyboard held while focused (flight keys never fire).</summary>
    public sealed class AvField : AvPart
    {
        private readonly AvFrame frame;
        private readonly TMP_InputField input;
        private readonly TMP_Text text, hint;
        private bool focused, invalid, guarded;

        public AvField(RectTransform parent, string placeholder, int maxChars, Action<string> submit)
        {
            Rect = AvLay.Child(parent, "Field");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(4f)); AvLay.Fill(frame.rectTransform);
            frame.raycastTarget = true;
            RectTransform area = AvLay.Child(Rect, "TextArea"); AvLay.Fill(area, 0f);
            area.offsetMin = new Vector2(10f, 4f); area.offsetMax = new Vector2(-10f, -4f);
            area.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            text = AvText.Make(area, "Text", AvTextRole.Prose); AvLay.Fill(text.rectTransform);
            hint = AvText.Make(area, "Hint", AvTextRole.Prose, placeholder ?? ""); AvLay.Fill(hint.rectTransform);
            input = Rect.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area; input.textComponent = text; input.placeholder = hint;
            input.targetGraphic = frame; input.characterLimit = Mathf.Clamp(maxChars, 1, 256);
            input.lineType = TMP_InputField.LineType.SingleLine;
            AvInput.StripNavigation(input);
            input.onSelect.AddListener(_ => { focused = true; if (!guarded) { guarded = true; AvInput.KeyboardGuard.Acquire(); } Restyle(); });
            input.onDeselect.AddListener(_ => { focused = false; Release(); Restyle(); });
            input.onSubmit.AddListener(v => { submit?.Invoke(v); Release(); AvInput.Deselect(input.gameObject); });
            Rect.gameObject.AddComponent<ReleaseOnDisable>().Owner = this;
            Restyle();
        }

        public string Text { get => input.text; set => input.text = value ?? ""; }
        public bool Invalid { get => invalid; set { invalid = value; Restyle(); } }

        private void Release() { if (guarded) { guarded = false; AvInput.KeyboardGuard.Release(); } }

        public override float Measure(float width) => AvGridTokens.Row;

        public override void Restyle()
        {
            AvStyle f = AvStyleHost.FuiStyle("field", invalid ? "invalid" : focused ? "focus" : null);
            AvStyle b = AvStyleHost.FuiStyle("field");
            frame.Paint(AvStyleHost.Resolve(f.Background.HasValue ? f.Background : b.Background, AvTheme.Surface),
                        AvStyleHost.Resolve(f.Border, AvTheme.Hairline));
            text.color = AvStyleHost.Resolve(b.Color, AvTheme.TextPrimary);
            hint.color = AvStyleHost.FuiInk("field-hint", AvTheme.Disabled);
        }

        private sealed class ReleaseOnDisable : MonoBehaviour
        {
            public AvField Owner;
            private void OnDisable() => Owner?.Release();
        }
    }
}
