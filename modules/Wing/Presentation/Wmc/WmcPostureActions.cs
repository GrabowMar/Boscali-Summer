using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The posture settings' orders (spec tactical v3 §4), shared by TACTICAL's posture strip and BEHAVIOUR's TUNING and FORM
    /// so the two pages cannot drift apart. Each gates on the host through <see cref="WmcUi.Order"/>.</summary>
    internal static class WmcPostureActions
    {
        public static readonly WingDoctrine[] Profiles = { WingDoctrine.Reserve, WingDoctrine.Escort, WingDoctrine.Sweep };
        public static readonly float[] FallBackRatios = { 0f, 1.5f, 2f, 3f };
        public static readonly string[] FallBackWords = { "NEVER", "1.5 : 1", "2 : 1", "3 : 1" };
        public static readonly string[] WinchesterWords = { "REJOIN", "RTB", "REFIT" };
        public static readonly string[] BingoWords = { "RTB", "REFIT" };
        public const string HostOnly = "The host sets these";

        public static string Cannot(WmcContext c) => c != null && c.Client ? HostOnly : "Wing Command is not ready";

        /// <summary>One doctrine setting at the scope's level (spec WMC rebuild R3): the rest of each aircraft's doctrine stays.</summary>
        public static void Axis(WmcContext c, DoctrineAxis axis, int value)
        {
            if (c == null) return;
            string word = WingDoctrine.ValueName(axis, (byte)value);
            if (word == null) return;
            WmcUi.Order(c, () => WingOrders.Run(new WingOrder { Kind = OrderKind.SetOverride, Number = (int)axis, Text = word, Scope = c.Scope }));
        }

        public static void Profile(WmcContext c, int i)
        {
            if (c == null || i < 0 || i >= Profiles.Length) return;
            WmcUi.Order(c, () => WingOrders.Run(new WingOrder { Kind = OrderKind.SetDoctrine, Text = Profiles[i].ToString(), Scope = c.Scope }));
        }

        public static void Shape(WmcContext c, string id)
        {
            if (c == null || string.IsNullOrEmpty(id)) return;
            WmcUi.Order(c, () => WingOrders.Run(new WingOrder { Kind = OrderKind.SetShape, Text = id, Scope = c.Scope }));
        }

        public static void Spacing(WmcContext c, int i) => WmcUi.Order(c, () => WingCommands.SetSpacing((SpacingPreset)i));

        public static void Stack(WmcContext c, int i) => WmcUi.Order(c, () =>
        {
            switch (i)
            {
                case 0: WingCommands.Stack(WingCommands.GoHighMetres, "Going high"); break;
                case 1: WingCommands.Stack(0f, "Level with you"); break;
                default: WingCommands.Stack(WingCommands.GoLowMetres, "Going low"); break;
            }
        });

        /// <summary>0 BUSTER (no afterburner), 1 GATE.</summary>
        public static void Power(WmcContext c, int i) => WmcUi.Order(c, () => WingCommands.Afterburner(i == 1));

        public static void Winchester(WmcContext c, int i) => WmcUi.Order(c, () => WingSettings.Instance.AfterWinchester.Value = (WinchesterAction)i);

        public static void Bingo(WmcContext c, int i) => WmcUi.Order(c, () => WingSettings.Instance.AfterBingo.Value = (BingoAction)i);

        public static void FallBack(WmcContext c, int i) => WmcUi.Order(c, () => WingSettings.Instance.FallBackRatio.Value = FallBackRatios[i]);

        public static int FallBackIndex(float ratio)
        {
            for (int i = 0; i < FallBackRatios.Length; i++)
                if (System.Math.Abs(ratio - FallBackRatios[i]) < 0.05f) return i;
            return -1;
        }
    }
}
