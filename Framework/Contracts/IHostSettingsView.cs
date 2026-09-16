using System.Collections.Generic;

namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>How a host setting row is operated: a latched ON/OFF, or a stepped value.</summary>
    internal enum HostSettingKind
    {
        Toggle,
        Stepper
    }

    /// <summary>
    /// One host-authoritative setting published by its owning module. The row carries only
    /// what the panel has to draw — the value stays in the module's own config entry, and
    /// the panel writes back through <see cref="IHostSettingsView"/>.
    /// </summary>
    internal sealed class HostSettingView
    {
        public HostSettingView(int id, HostSettingKind kind, string label, string help)
        {
            Id = id;
            Kind = kind;
            Label = label ?? "";
            Help = help ?? "";
        }

        public int Id { get; }
        public HostSettingKind Kind { get; }
        public string Label { get; }
        public string Help { get; }

        /// <summary>Current latched state for a Toggle row.</summary>
        public bool Value { get; set; }

        /// <summary>The value as printed for either kind, e.g. "ON", "1.35x", "60 s".</summary>
        public string ValueText { get; set; }

        public bool CanDecrease { get; set; }
        public bool CanIncrease { get; set; }

        /// <summary>False when the owning module cannot honour a change (missing dependency).</summary>
        public bool Interactive { get; set; } = true;

        /// <summary>Why <see cref="Interactive"/> is false; shown instead of the help text.</summary>
        public string Reason { get; set; }
    }

    /// <summary>
    /// One module's host-authoritative settings. Registered by the owning feature, consumed
    /// by Command's SET SERVER page; the panel never sees a module's settings object.
    /// </summary>
    internal interface IHostSettingsView
    {
        string Section { get; }
        IReadOnlyList<HostSettingView> Rows { get; }

        /// <summary>Re-read every row's live value. Values are mutated in place, never rebuilt.</summary>
        void Refresh();

        void Toggle(int id);

        /// <summary>Move a Stepper row one step; direction is -1 or 1.</summary>
        void Step(int id, int direction);
    }

    /// <summary>
    /// Pure stepping arithmetic for host setting rows. Speed is irrelevant here, so the
    /// only concerns are the range, a non-finite step result, and not wrapping past a bound.
    /// </summary>
    internal static class HostSettingMath
    {
        public static float Step(float value, int direction, float min, float max, float step)
        {
            if (step <= 0f || direction == 0) return Clamp(value, min, max);

            float next = value + (direction > 0 ? step : -step);
            if (float.IsNaN(next) || float.IsInfinity(next)) next = direction > 0 ? max : min;
            return Clamp(next, min, max);
        }

        public static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value)) return min;
            return value < min ? min : value > max ? max : value;
        }
    }
}
