using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// The TGT quick slots as a native-wheel page. Read-only over the live selector: the
    /// page announces and applies presets, it never mutates the library. Empty slots are
    /// absent, so the wheel only shows what the player actually assigned.
    /// </summary>
    internal sealed class TargetPresetRadialPage : IRadialMenuPage
    {
        public string Title => "TARGET FILTERS";

        public int EntryCount
        {
            get
            {
                if (!TargetPresetRuntime.WheelEnabled) return 0;
                TargetListSelector selector = SceneSingleton<TargetListSelector>.i;
                if (!TargetPresetRuntime.Ready(selector)) return 0;

                int count = 0;
                for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
                    if (Assigned(slot)) count++;
                return count;
            }
        }

        public string EntryLabel(int index)
        {
            int slot = SlotAt(index);
            string name = slot < 0 ? "" : TargetPresetRuntime.QuickSlotName(slot);
            if (name.Length == 0) return "";
            TargetListSelector selector = SceneSingleton<TargetListSelector>.i;
            return TargetPresetRuntime.ActiveName(selector) == name ? name + " [ON]" : name;
        }

        public bool EntryAllowed(int index) =>
            SlotAt(index) >= 0 && TargetPresetRuntime.Ready(SceneSingleton<TargetListSelector>.i);

        public void InvokeEntry(int index)
        {
            int slot = SlotAt(index);
            if (slot < 0) return;
            string name = TargetPresetRuntime.QuickSlotName(slot);
            TargetPresetRuntime.TryApplyByName(SceneSingleton<TargetListSelector>.i, name);
        }

        private static bool Assigned(int slot)
        {
            string name = TargetPresetRuntime.QuickSlotName(slot);
            return name.Length > 0 && TargetPresetRuntime.CatalogContains(name);
        }

        private static int SlotAt(int index)
        {
            if (index < 0) return -1;
            int seen = 0;
            for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
            {
                if (!Assigned(slot)) continue;
                if (seen == index) return slot;
                seen++;
            }
            return -1;
        }
    }
}
