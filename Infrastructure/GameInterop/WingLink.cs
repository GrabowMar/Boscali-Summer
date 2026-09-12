using System;
using System.Reflection;
using BepInEx.Bootstrap;
using NOAvionics;

namespace BoscaliSummer.Runtime
{
    /// <summary>
    /// Optional, read-only Wing Command link. The shared BCL-only presence board and map
    /// picker are canonical; bounded reflection keeps coexistence intact with Wing Command
    /// releases that expose only their public compatibility façade.
    /// </summary>
    internal static class WingLink
    {
        private const string WingCommandGuid = "com.marci.wingcommand";
        private const string MembershipType = "WingCommand.Interop.WingMembership, WingCommand";
        private const string MapModeType = "WingCommand.Interop.WingMapMode, WingCommand";

        private static bool membershipResolved;
        private static MethodInfo contains;
        private static PropertyInfo count;
        private static bool mapModeResolved;
        private static PropertyInfo gestureArmed;

        public static bool Available =>
            !string.IsNullOrEmpty(PresenceBoard.GetString(PresenceBoard.WingGuid)) ||
            Chainloader.PluginInfos.ContainsKey(WingCommandGuid);

        public static bool IsWingMember(int persistentIdHash)
        {
            int[] ids = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
            if (ids.Length > 0) return PresenceBoard.Contains(ids, persistentIdHash);

            MethodInfo method = ResolveMembership();
            if (method == null) return false;
            try { return method.Invoke(null, new object[] { persistentIdHash }) is bool hit && hit; }
            catch (Exception error) { FailMembership(error); return false; }
        }

        public static int WingCount
        {
            get
            {
                int[] ids = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
                if (ids.Length > 0) return ids.Length;
                if (ResolveMembership() == null || count == null) return 0;
                try { return count.GetValue(null) is int value ? value : 0; }
                catch (Exception error) { FailMembership(error); return 0; }
            }
        }

        public static bool WingMapGestureArmed
        {
            get
            {
                if (MapPicker.IsOwner(MapPicker.WingPoint)) return true;
                PropertyInfo property = ResolveMapMode();
                if (property == null) return false;
                try { return property.GetValue(null) is bool value && value; }
                catch (Exception error) { FailMapMode(error); return false; }
            }
        }

        private static MethodInfo ResolveMembership()
        {
            if (membershipResolved) return contains;
            if (!Chainloader.PluginInfos.ContainsKey(WingCommandGuid)) return null;
            membershipResolved = true;
            try
            {
                Type type = Type.GetType(MembershipType, throwOnError: false);
                contains = type?.GetMethod("Contains", BindingFlags.Public | BindingFlags.Static,
                                           null, new[] { typeof(int) }, null);
                count = type?.GetProperty("Count", BindingFlags.Public | BindingFlags.Static);
            }
            catch (Exception error) { FailMembership(error); }
            return contains;
        }

        private static PropertyInfo ResolveMapMode()
        {
            if (mapModeResolved) return gestureArmed;
            if (!Chainloader.PluginInfos.ContainsKey(WingCommandGuid)) return null;
            mapModeResolved = true;
            try
            {
                Type type = Type.GetType(MapModeType, throwOnError: false);
                gestureArmed = type?.GetProperty("GestureArmed",
                    BindingFlags.Public | BindingFlags.Static);
            }
            catch (Exception error) { FailMapMode(error); }
            return gestureArmed;
        }

        private static void FailMembership(Exception error)
        {
            contains = null;
            count = null;
            Plugin.Logger?.LogWarning(
                "WingLink membership fallback failed; continuing without wing awareness. " +
                error.Message);
        }

        private static void FailMapMode(Exception error)
        {
            gestureArmed = null;
            Plugin.Logger?.LogWarning(
                "WingLink map fallback failed; shared picker remains active. " + error.Message);
        }
    }
}
