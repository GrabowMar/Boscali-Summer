using System;
using TMPro;

namespace NOAvionics
{
    /// <summary>
    /// A numeric TMP label formatted with <see cref="AvNumFormat"/> instead of string
    /// interpolation: every <see cref="Set"/> writes into a fixed scratch buffer and only
    /// touches the TMP mesh when the shown characters actually changed.
    /// </summary>
    public sealed class AvNumText
    {
        private readonly char[] scratch = new char[32];

        /// <summary>What the label currently shows, kept only to detect a no-op write.</summary>
        private readonly char[] shown = new char[32];
        private int shownLength = -1;

        public AvNumText(TMP_Text text)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Text.enableAutoSizing = false;
            Text.isTextObjectScaleStatic = true;
            Text.raycastTarget = false;
        }

        public TMP_Text Text { get; }

        /// <summary>
        /// Formats <paramref name="prefix"/>, then the value, then <paramref name="suffix"/>
        /// into the scratch buffer and shows it. Never reads <c>Text.text</c>.
        /// </summary>
        public void Set(double value, int decimals, string prefix = null, string suffix = null, bool plus = false)
        {
            int at = 0;
            at = AvNumFormat.Append(scratch, at, prefix);
            at = AvNumFormat.Write(scratch, at, value, decimals, plus);
            at = AvNumFormat.Append(scratch, at, suffix);
            SetChars(scratch, at);
        }

        /// <summary>
        /// Shows the first <paramref name="length"/> characters of <paramref name="src"/>.
        /// A no-op when they are identical to what is already shown.
        /// </summary>
        public void SetChars(char[] src, int length)
        {
            if (src == null || length < 0) length = 0;

            if (length == shownLength && SameAsShown(src, length))
                return;

            int copy = Math.Min(length, shown.Length);
            for (int i = 0; i < copy; i++) shown[i] = src[i];
            shownLength = length;

            Text.SetCharArray(src, 0, length);
        }

        /// <summary>Rare, non-numeric path (e.g. a static label). Compares, then sets.</summary>
        public void SetString(string s)
        {
            s = s ?? "";

            if (s.Length == shownLength && SameAsShown(s))
                return;

            int copy = Math.Min(s.Length, shown.Length);
            for (int i = 0; i < copy; i++) shown[i] = s[i];
            shownLength = s.Length;

            Text.SetCharArray(s.ToCharArray(), 0, s.Length);
        }

        /// <summary>Shows an empty string; a no-op once it already is one.</summary>
        public void Clear()
        {
            if (shownLength == 0) return;
            shownLength = 0;
            Text.SetCharArray(shown, 0, 0);
        }

        private bool SameAsShown(char[] src, int length)
        {
            if (length > shown.Length) return false;
            for (int i = 0; i < length; i++)
                if (src[i] != shown[i]) return false;
            return true;
        }

        private bool SameAsShown(string s)
        {
            if (s.Length > shown.Length) return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] != shown[i]) return false;
            return true;
        }
    }
}
