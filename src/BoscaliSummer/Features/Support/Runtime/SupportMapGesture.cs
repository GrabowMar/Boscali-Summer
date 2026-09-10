using NOAvionics;

namespace BoscaliSummer.Features.Support.Runtime
{
    internal sealed class SupportMapGesture
    {
        private int? completedFrame;

        public bool TryArm(string prompt)
        {
            if (!MapPicker.TryArm(MapPicker.Support, MapPicker.GestureRight, prompt)) return false;
            completedFrame = null;
            return true;
        }

        public void Complete(int frame) => completedFrame = frame;

        public void Advance(int frame)
        {
            // WC polls the same right-click in Update. Keep ownership for the entire
            // consuming frame, irrespective of the two plugins' execution order.
            if (completedFrame.HasValue && completedFrame.Value != frame) Reset();
        }

        public void Reset()
        {
            completedFrame = null;
            MapPicker.Disarm(MapPicker.Support);
        }
    }
}
