using NOAvionics;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// Tracks Boscali's armed map right-click.
    ///
    /// <para>The shared picker arbitrates ownership with Wing Command. The local completed
    /// frame additionally keeps ownership through the consuming frame, regardless of plugin
    /// update order, while the press coordinates distinguish a click from a map pan.</para>
    /// </summary>
    internal sealed class SupportMapGesture
    {
        private int? completedFrame;

        // Screen position of the pending right-button press, in pixels. NaN means no press is
        // being tracked, so a stray button-up cannot be read as a click.
        private float pressX = float.NaN;
        private float pressY = float.NaN;

        /// <summary>Whether a Boscali support action is currently armed for a map click.</summary>
        public bool Armed => MapPicker.IsOwner(MapPicker.Support);

        /// <summary>Status-strip prompt to show while armed.</summary>
        public string Prompt => Armed ? MapPicker.Prompt : null;

        public bool TryArm(string prompt)
        {
            if (!MapPicker.TryArm(MapPicker.Support, MapPicker.GestureRight, prompt)) return false;
            completedFrame = null;
            ClearPress();
            return true;
        }

        public void Complete(int frame) => completedFrame = frame;

        /// <summary>Remember where the right button went down, to measure travel on release.</summary>
        public void NotePointerDown(float screenX, float screenY)
        {
            pressX = screenX;
            pressY = screenY;
        }

        /// <summary>
        /// Whether the release at <paramref name="screenX"/>/<paramref name="screenY"/> is
        /// close enough to the tracked press to count as a click rather than a map drag.
        /// False when no press was tracked.
        /// </summary>
        public bool ReleasedAsClick(float screenX, float screenY, float slopPixels)
        {
            if (float.IsNaN(pressX) || float.IsNaN(pressY)) return false;
            float dx = screenX - pressX;
            float dy = screenY - pressY;
            return dx * dx + dy * dy <= slopPixels * slopPixels;
        }

        public void Advance(int frame)
        {
            // Hold the armed state for the entire consuming frame, then clear.
            if (completedFrame.HasValue && completedFrame.Value != frame) Reset();
        }

        public void Reset()
        {
            completedFrame = null;
            ClearPress();
            MapPicker.Disarm(MapPicker.Support);
        }

        private void ClearPress()
        {
            pressX = float.NaN;
            pressY = float.NaN;
        }
    }
}
