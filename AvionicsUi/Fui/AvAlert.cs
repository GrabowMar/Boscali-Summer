using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>Caution/danger card: 3 px rail, icon, title, wrapped body. Dissolves in; danger pulses.</summary>
    public sealed class AvAlert : AvPart
    {
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text icon, title, body;
        private readonly AvFx fx, railFx;
        private AvState state = AvState.Caution;

        public AvAlert(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Alert");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f)); AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            icon = AvIcons.Make(Rect, AvIcon.AlertTriangle, AvGridTokens.IconTool, Color.white);
            title = AvText.Make(Rect, "Title", AvTextRole.Head, "", TextAlignmentOptions.TopLeft, true);
            body = AvText.Make(Rect, "Body", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
            fx = AvFx.On(frame); railFx = AvFx.On(rail);
            Rect.gameObject.SetActive(false);
        }

        public void Show(AvIcon glyph, string t, string b, AvState st)
        {
            state = st == AvState.Danger ? AvState.Danger : AvState.Caution;
            AvIcons.Set(icon, glyph == AvIcon.None ? AvIcon.AlertTriangle : glyph, AvGridTokens.IconTool);
            title.text = AvStates.Glyph(state) + (t ?? "");
            body.text = b ?? "";
            bool was = Rect.gameObject.activeSelf;
            Rect.gameObject.SetActive(true);
            Changed();
            if (!was) fx.Play(AvFxKind.Dissolve, 0.8f, 6f);
            railFx.Set(state == AvState.Danger ? AvFxKind.Pulse : AvFxKind.None, 0.35f);
            Restyle();
        }

        public void Hide() => SetShown(false);

        public override float Measure(float width) =>
            !Rect.gameObject.activeSelf ? 0f : 10f + AvText.Height(title, width - 52f) + 4f + AvText.Height(body, width - 52f) + 10f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float th = AvText.Height(title, s.W - 52f);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(icon.rectTransform, 12f, 10f, 22f, 22f);
            AvLay.Place(title.rectTransform, 42f, 10f, s.W - 52f, th);
            AvLay.Place(body.rectTransform, 42f, 14f + th, s.W - 52f, AvText.Height(body, s.W - 52f));
        }

        public override void Restyle()
        {
            AvStyle a = AvStyleHost.FuiStyle("alert" + (state == AvState.Danger ? " danger" : ""));
            frame.Paint(AvStyleHost.Resolve(a.Background, AvTheme.SurfaceRaised), AvStyleHost.Resolve(a.Border, AvTheme.Warning));
            Color c = AvStyleHost.Resolve(a.Rail, AvTheme.Warning);
            rail.color = c; icon.color = c; title.color = c;
            body.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
        }
    }
}
