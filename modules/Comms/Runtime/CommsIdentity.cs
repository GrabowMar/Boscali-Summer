using System;
using System.Reflection;
using BoscaliSummer.Core;
using BoscaliSummer.Framework.Contracts;
using NuclearOption.Networking;

namespace BoscaliSummer.Features.Comms.Runtime
{
    /// <summary>
    /// Who a player is, for COMMS: a stable id, a display name and a side. The host resolves
    /// all three from its own <see cref="Player"/> object, never from what a peer claims.
    ///
    /// <para>The game's player-name API has moved between releases (a <c>PlayerName</c>
    /// member, then <c>GetNameOrCensored()</c>, then <c>GetDisplayName(PlayerNameContext)</c>),
    /// so it is found by reflection once and cached. A build that has none of them still
    /// works: posts are signed "PILOT" instead of failing.</para>
    /// </summary>
    internal static class CommsIdentity
    {
        private static bool resolved;
        private static MethodInfo displayName;
        private static object displayContext;
        private static MethodInfo censoredName;
        private static PropertyInfo nameProperty;
        private static FieldInfo nameField;

        public static ulong Id(Player player) => PlayerIdentity.Of(player);

        /// <summary>The side as the faction-name hash the rest of Boscali uses; 0 while unassigned.</summary>
        public static int Faction(Player player)
        {
            string name = player != null && player.HQ != null && player.HQ.faction != null
                ? player.HQ.faction.factionName
                : null;
            return string.IsNullOrEmpty(name) ? 0 : unchecked((int)Deterministic.HashString(name));
        }

        public static string Name(Player player)
        {
            if (player == null) return "";
            Resolve();
            try
            {
                if (displayName != null)
                    return displayName.Invoke(player, new[] { displayContext }) as string ?? "";
                if (censoredName != null) return censoredName.Invoke(player, null) as string ?? "";
                if (nameProperty != null) return nameProperty.GetValue(player, null) as string ?? "";
                if (nameField != null) return nameField.GetValue(player) as string ?? "";
            }
            catch (Exception)
            {
                // A name is decoration; a failure here must never cost the post itself.
            }
            return "";
        }

        public static bool TryLocal(out Player player) =>
            GameManager.GetLocalPlayer<Player>(out player) && player != null;

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            Type type = typeof(Player);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                foreach (MethodInfo method in type.GetMethods(flags))
                {
                    if (method.Name != "GetDisplayName" || method.ReturnType != typeof(string)) continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum) continue;
                    displayName = method;
                    displayContext = EnumValue(parameters[0].ParameterType, "Other");
                    break;
                }
                if (displayName == null)
                {
                    censoredName = type.GetMethod("GetNameOrCensored", flags, null, Type.EmptyTypes, null);
                    if (censoredName != null && censoredName.ReturnType != typeof(string)) censoredName = null;
                }
                if (displayName == null && censoredName == null)
                {
                    nameProperty = type.GetProperty("PlayerName", flags);
                    if (nameProperty != null && nameProperty.PropertyType != typeof(string)) nameProperty = null;
                    if (nameProperty == null)
                    {
                        nameField = type.GetField("PlayerName", flags) ?? type.GetField("playerName", flags);
                        if (nameField != null && nameField.FieldType != typeof(string)) nameField = null;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("[COMMS] Player name lookup unavailable: " + e.Message);
            }
        }

        private static object EnumValue(Type enumType, string preferred)
        {
            try
            {
                return Enum.IsDefined(enumType, preferred)
                    ? Enum.Parse(enumType, preferred)
                    : Enum.GetValues(enumType).GetValue(0);
            }
            catch (Exception)
            {
                return Activator.CreateInstance(enumType);
            }
        }
    }
}
