using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Framework.Features
{
    /// <summary>
    /// The usual shape of a module's <see cref="IHostSettingsView"/>: rows bound straight to
    /// the module's own config entries, so the config file stays the single owner of every
    /// value and a change made here is visible to the rest of the mod immediately.
    ///
    /// <para>Declared by the owning feature, one line per setting. Ranges come from the
    /// entry's own <c>AcceptableValueRange</c>; an entry without one still reads but does not
    /// step, rather than inventing a bound.</para>
    /// </summary>
    internal sealed class HostSettingsTable : IHostSettingsView
    {
        private readonly List<HostSettingView> rows = new List<HostSettingView>();
        private readonly List<Action> refreshers = new List<Action>();
        private readonly Dictionary<int, Action> toggles = new Dictionary<int, Action>();
        private readonly Dictionary<int, Action<int>> steps = new Dictionary<int, Action<int>>();

        public HostSettingsTable(string section) => Section = section ?? "";

        public string Section { get; }
        public IReadOnlyList<HostSettingView> Rows => rows;

        public HostSettingsTable Toggle(
            int id, ConfigEntry<bool> entry, string label, string help, Func<string> unavailable = null)
        {
            HostSettingView row = Add(new HostSettingView(id, HostSettingKind.Toggle, label, help));
            toggles.Add(id, () =>
            {
                if (row.Interactive) entry.Value = !entry.Value;
            });
            refreshers.Add(() =>
            {
                row.Value = entry.Value;
                row.ValueText = entry.Value ? "ON" : "OFF";
                Availability(row, unavailable);
                row.CanDecrease = row.CanIncrease = row.Interactive;
            });
            return this;
        }

        public HostSettingsTable Number(
            int id, ConfigEntry<float> entry, string label, string help, float step,
            Func<float, string> format = null, Func<string> unavailable = null)
        {
            HostSettingView row = Add(new HostSettingView(id, HostSettingKind.Stepper, label, help));
            Range(entry, out float min, out float max);
            steps.Add(id, direction =>
            {
                if (row.Interactive) entry.Value = HostSettingMath.Step(entry.Value, direction, min, max, step);
            });
            refreshers.Add(() =>
            {
                row.ValueText = format != null ? format(entry.Value) : Default(entry.Value);
                row.CanDecrease = entry.Value > min;
                row.CanIncrease = entry.Value < max;
                Availability(row, unavailable);
            });
            return this;
        }

        public HostSettingsTable Number(
            int id, ConfigEntry<int> entry, string label, string help, int step,
            Func<int, string> format = null, Func<string> unavailable = null)
        {
            HostSettingView row = Add(new HostSettingView(id, HostSettingKind.Stepper, label, help));
            Range(entry, out int min, out int max);
            steps.Add(id, direction =>
            {
                if (!row.Interactive) return;
                float next = HostSettingMath.Step(entry.Value, direction, min, max, step);
                entry.Value = (int)Math.Round(next, MidpointRounding.AwayFromZero);
            });
            refreshers.Add(() =>
            {
                row.ValueText = format != null ? format(entry.Value)
                    : entry.Value.ToString(CultureInfo.InvariantCulture);
                row.CanDecrease = entry.Value > min;
                row.CanIncrease = entry.Value < max;
                Availability(row, unavailable);
            });
            return this;
        }

        /// <summary>A fixed set of choices (an enum) stepped with the same -/+ control.</summary>
        public HostSettingsTable Choice<T>(
            int id, ConfigEntry<T> entry, string label, string help, Func<string> unavailable = null)
            where T : struct, Enum
        {
            HostSettingView row = Add(new HostSettingView(id, HostSettingKind.Stepper, label, help));
            T[] values = (T[])Enum.GetValues(typeof(T));
            string[] names = Enum.GetNames(typeof(T));
            steps.Add(id, direction =>
            {
                if (!row.Interactive || values.Length == 0) return;
                int index = IndexOf(values, entry.Value);
                entry.Value = values[(index + direction + values.Length) % values.Length];
            });
            refreshers.Add(() =>
            {
                int index = IndexOf(values, entry.Value);
                if (index < 0) index = 0;
                row.ValueText = names.Length > index ? names[index].ToUpperInvariant() : "";
                Availability(row, unavailable);
                row.CanDecrease = row.CanIncrease = row.Interactive && values.Length > 1;
            });
            return this;
        }

        public void Refresh()
        {
            for (int i = 0; i < refreshers.Count; i++) refreshers[i]();
        }

        public void Toggle(int id)
        {
            if (toggles.TryGetValue(id, out Action toggle)) toggle();
        }

        public void Step(int id, int direction)
        {
            if (steps.TryGetValue(id, out Action<int> step)) step(direction > 0 ? 1 : -1);
        }

        private HostSettingView Add(HostSettingView row)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Id == row.Id)
                    throw new InvalidOperationException("Duplicate host setting id " + row.Id + " in " + Section + ".");
            rows.Add(row);
            return row;
        }

        private static void Availability(HostSettingView row, Func<string> unavailable)
        {
            string reason = unavailable != null ? unavailable() : null;
            row.Interactive = string.IsNullOrEmpty(reason);
            row.Reason = row.Interactive ? null : reason;
        }

        private static int IndexOf<T>(T[] values, T value) where T : struct, Enum
        {
            for (int i = 0; i < values.Length; i++)
                if (EqualityComparer<T>.Default.Equals(values[i], value)) return i;
            return -1;
        }

        private static string Default(float value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);

        private static void Range(ConfigEntry<float> entry, out float min, out float max)
        {
            if (entry.Description?.AcceptableValues is AcceptableValueRange<float> range)
            {
                min = range.MinValue;
                max = range.MaxValue;
                return;
            }
            min = entry.Value;
            max = entry.Value;
        }

        private static void Range(ConfigEntry<int> entry, out int min, out int max)
        {
            if (entry.Description?.AcceptableValues is AcceptableValueRange<int> range)
            {
                min = range.MinValue;
                max = range.MaxValue;
                return;
            }
            min = entry.Value;
            max = entry.Value;
        }
    }
}
