using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BoscaliSummer.Runtime;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static class MapUiAccess
    {
        private static readonly FieldInfo Speed = AccessTools.Field(typeof(VirtualMFD), "speed");
        private static readonly FieldInfo Airbase = AccessTools.Field(typeof(GameplayUI), "selectAirbasePanel");
        private static readonly FieldInfo Spectator = AccessTools.Field(typeof(GameplayUI), "spectatorPanel");
        private static readonly FieldInfo Message = AccessTools.Field(typeof(MessageUI), "messageText");
        private static readonly FieldInfo KillFeed = AccessTools.Field(typeof(MessageUI), "killFeedText");
        public static bool MfdAvailable => GameAccess.MfdAvailable;
        public static bool MfdFooterAvailable => Speed != null && Airbase != null && Spectator != null;
        public static bool MfdLogAvailable => Message != null && KillFeed != null;
        public static List<Button> GetLeftButtons(VirtualMFD mfd) => GameAccess.GetLeftMfdButtons(mfd);
        public static List<Button> GetRightButtons(VirtualMFD mfd) => GameAccess.GetRightMfdButtons(mfd);
        public static List<MFDScreen> GetLeftScreens(VirtualMFD mfd) => GameAccess.GetLeftMfdScreens(mfd);
        public static List<MFDScreen> GetRightScreens(VirtualMFD mfd) => GameAccess.GetRightMfdScreens(mfd);
        public static RectTransform GetMfdTopInstruments(VirtualMFD mfd) =>
            (Speed?.GetValue(mfd) as TMP_Text)?.transform.parent as RectTransform;
        public static GameObject GetSelectAirbasePanel(GameplayUI ui) => ui == null ? null : Airbase?.GetValue(ui) as GameObject;
        public static GameObject GetSpectatorPanel(GameplayUI ui) => ui == null ? null : Spectator?.GetValue(ui) as GameObject;
        public static TextMeshProUGUI GetMessageText(MessageUI ui) => ui == null ? null : Message?.GetValue(ui) as TextMeshProUGUI;
        public static TextMeshProUGUI GetKillFeedText(MessageUI ui) => ui == null ? null : KillFeed?.GetValue(ui) as TextMeshProUGUI;
    }
}
