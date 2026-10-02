using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>uGUI port of UI Toolkit Extensions' StepProgressBar state and width model.
    /// Source and BSD notice: ../ThirdParty/UIToolkitExtensions/NOTICE.txt.</summary>
    public sealed class AvStepProgress
    {
        public readonly RectTransform Rect;
        private readonly Image background, fill;
        private int currentSteps, maxSteps = 1;
        public float NormalizedProgress => (float)currentSteps / maxSteps;

        public AvStepProgress(RectTransform parent, string name)
        {
            Rect = AvLay.Child(parent, name);
            background = AvLay.Solid(Rect, "Track", AvTheme.Hairline);
            AvLay.Fill(background.rectTransform);
            fill = AvLay.Solid(Rect, "Fill", AvTheme.RailInfo);
            AvSurfaceGradient.Apply(fill, Color.white, Color.white * 0.72f);
            SetProgress(0, 1);
        }

        public void SetProgress(int steps, int maximum)
        {
            maxSteps = Mathf.Max(1, maximum);
            currentSteps = Mathf.Clamp(steps, 0, maxSteps);
            RectTransform bar = fill.rectTransform;
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(NormalizedProgress, 1f);
            bar.offsetMin = bar.offsetMax = Vector2.zero;
        }

        public void Paint(Color ink, Color track)
        {
            fill.color = ink;
            background.color = track;
        }
    }
}
