using BoscaliSummer.Features.Support.Domain.SpecOps;
using BoscaliSummer.Features.Support.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPEC OPS on the maximised map: each deployed team as a labelled marker on its objective
    /// (callsign, state and mission in words), and each held post's reach as a ring in the post's
    /// colour, so an ARMed SPOT, SUPPRESS or safehouse FORTIFY has a visible target area. Four
    /// pooled marks, map-local, client-only; nothing here feeds gameplay.
    /// </summary>
    internal sealed class SpecOpsMapLayer
    {
        private sealed class TeamMark
        {
            public GameObject Root;
            public Image Icon;
            public TextMeshProUGUI Label;
            public Image Reach;
            public string LastLabel;
        }

        private readonly TeamMark[] marks = new TeamMark[SpecOpsDetachment.TeamCount];

        public SpecOpsMapLayer(Transform parent, TMP_FontAsset font)
        {
            SupportTacticalIcons.EnsureInitialized();
            for (int i = 0; i < marks.Length; i++) marks[i] = Build(parent, font);
        }

        public void Update(SupportManager support, float mapFactor, float invZoom)
        {
            SpecOpsDetachment detachment = support != null ? support.LocalDetachment : null;
            bool live = detachment != null && detachment.Enabled && support.SpecOpsEnabled;
            float time = Time.unscaledTime;
            for (int i = 0; i < marks.Length; i++)
            {
                TeamMark mark = marks[i];
                FieldTeam team = live ? detachment.Team(i) : default;
                bool show = live && team.Deployed;
                if (mark.Root.activeSelf != show) mark.Root.SetActive(show);
                bool reach = show && team.State == TeamState.Holding;
                if (mark.Reach.gameObject.activeSelf != reach) mark.Reach.gameObject.SetActive(reach);
                if (!show) continue;

                var position = new Vector3(team.X * mapFactor, team.Z * mapFactor, 0f);
                mark.Root.transform.localPosition = position;
                mark.Root.transform.localScale = Vector3.one * invZoom;
                Color colour = team.State == TeamState.Holding ? FieldTones.Post(team.Mission)
                    : FieldTones.StatePulsed(team.State, time);
                mark.Icon.color = colour;
                string label = "<b>" + FieldWords.Callsign(i) + "</b>\n" + (team.State == TeamState.Holding
                    ? FieldWords.Post(team.Mission)
                    : FieldWords.State(team.State) + " · " + FieldWords.Mission(team.Mission));
                if (mark.LastLabel != label) mark.Label.text = mark.LastLabel = label;
                mark.Label.color = colour;

                if (!reach) continue;
                mark.Reach.transform.localPosition = position;
                mark.Reach.rectTransform.sizeDelta = Vector2.one * FieldCatalog.PostReach(team.Mission) * 2f * mapFactor;
                mark.Reach.color = colour.WithAlpha(0.22f);
            }
        }

        private static TeamMark Build(Transform parent, TMP_FontAsset font)
        {
            var mark = new TeamMark
            {
                Reach = Make(parent, "SpecOpsReach", SupportTacticalIcons.DottedRingSprite),
                Root = new GameObject("SpecOpsTeam", typeof(RectTransform))
            };
            mark.Root.transform.SetParent(parent, false);
            mark.Icon = Make(mark.Root.transform, "Icon", null);
            mark.Icon.rectTransform.sizeDelta = new Vector2(10f, 10f);
            mark.Icon.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
            mark.Icon.gameObject.SetActive(true);

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(mark.Root.transform, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.sizeDelta = new Vector2(150f, 26f);
            textRect.pivot = new Vector2(0.5f, 0f);
            textRect.anchoredPosition = new Vector2(0f, 8f);
            mark.Label = textObject.GetComponent<TextMeshProUGUI>();
            if (font != null) mark.Label.font = font;
            mark.Label.fontSize = 8.5f;
            mark.Label.alignment = TextAlignmentOptions.Bottom;
            mark.Label.raycastTarget = false;
            mark.Root.SetActive(false);
            return mark;
        }

        private static Image Make(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            go.SetActive(false);
            return image;
        }
    }
}
