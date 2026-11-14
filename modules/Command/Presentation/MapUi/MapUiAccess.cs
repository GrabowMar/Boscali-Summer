using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BoscaliSummer.Core.Game;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal static class MapUiAccess
    {
        private static readonly FieldInfo Speed = AccessTools.Field(typeof(VirtualMFD), "speed");
        private static readonly FieldInfo Airbase = AccessTools.Field(typeof(GameplayUI), "selectAirbasePanel");
        private static readonly FieldInfo Spectator = AccessTools.Field(typeof(GameplayUI), "spectatorPanel");
        public static bool MfdAvailable => GameAccess.MfdAvailable;
        public static bool MfdFooterAvailable => Speed != null && Airbase != null && Spectator != null;
        public static List<Button> GetLeftButtons(VirtualMFD mfd) => GameAccess.GetLeftMfdButtons(mfd);
        public static List<Button> GetRightButtons(VirtualMFD mfd) => GameAccess.GetRightMfdButtons(mfd);
        public static List<MFDScreen> GetLeftScreens(VirtualMFD mfd) => GameAccess.GetLeftMfdScreens(mfd);
        public static List<MFDScreen> GetRightScreens(VirtualMFD mfd) => GameAccess.GetRightMfdScreens(mfd);
        public static RectTransform GetMfdTopInstruments(VirtualMFD mfd) =>
            (Speed?.GetValue(mfd) as TMP_Text)?.transform.parent as RectTransform;
        /// <summary>Own side: the live position. Others: only what the local faction knows.</summary>
        public static bool TryPosition(Unit unit, FactionHQ hq, out Vector3 position, FactionHQ owner = null)
        {
            GlobalPosition? at = hq == null || (owner ?? unit.NetworkHQ) == hq ? unit.GlobalPosition() : hq.GetKnownPosition(unit);
            position = at.HasValue ? at.Value.AsVector3() : default;
            return at.HasValue && float.IsFinite(position.x) && float.IsFinite(position.z);
        }

        /// <summary>Stretches a vector-ink overlay over the map image: above the territory tint, below the native contacts.</summary>
        public static void AttachInk(RectTransform rect, DynamicMap map)
        {
            Transform image = map.mapImage.transform;
            rect.SetParent(image, false);
            AvLay.Fill(rect);
            rect.localPosition = Vector3.zero;
            rect.SetSiblingIndex(image.childCount > 1 ? 1 : 0);
        }
        public static GameObject GetSelectAirbasePanel(GameplayUI ui) => ui == null ? null : Airbase?.GetValue(ui) as GameObject;
        public static GameObject GetSpectatorPanel(GameplayUI ui) => ui == null ? null : Spectator?.GetValue(ui) as GameObject;

        private static readonly FieldInfo Message = AccessTools.Field(typeof(MessageUI), "messageText");
        private static readonly FieldInfo KillFeed = AccessTools.Field(typeof(MessageUI), "killFeedText");
        private static TextMeshProUGUI feedMessage, feedKill;
        private static bool feedHidden, messageWas, killWas;

        /// <summary>The game's kill and message feed paints in the top-left, over the news ticker and the panel column; its lines are on
        /// EVN LOG, so while the map is open its two texts are off. The game keeps writing them; only the two renderers are disabled.</summary>
        public static void HideNativeFeed()
        {
            MessageUI ui = SceneSingleton<MessageUI>.i;
            TextMeshProUGUI message = ui == null ? null : Message?.GetValue(ui) as TextMeshProUGUI;
            TextMeshProUGUI kill = ui == null ? null : KillFeed?.GetValue(ui) as TextMeshProUGUI;
            if (feedHidden && (message != feedMessage || kill != feedKill)) RestoreNativeFeed();
            if (!feedHidden)
            {
                feedMessage = message; feedKill = kill;
                messageWas = message != null && message.enabled;
                killWas = kill != null && kill.enabled;
                feedHidden = true;
            }
            if (message != null) message.enabled = false;
            if (kill != null) kill.enabled = false;
        }

        /// <summary>Map closed or scene reset: the feed's texts back as they were.</summary>
        public static void RestoreNativeFeed()
        {
            if (!feedHidden) return;
            if (feedMessage != null) feedMessage.enabled = messageWas;
            if (feedKill != null) feedKill.enabled = killWas;
            feedMessage = feedKill = null;
            feedHidden = false;
        }
    }
}
