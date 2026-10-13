using System.Reflection;
using HarmonyLib;

namespace BoscaliSummer.Core.Game
{
    /// <summary>The Harmony target every kill-message patch shares (Chatter's radio, the wing's kill credit and pilot-loss record).</summary>
    internal static class KillMessageRpc
    {
        /// <summary>The received-kill RPC body (<c>UserCode_RpcKillMessage_*</c>, its suffix is generated): runs for host execution and for received messages, with (killerID, killedID).</summary>
        internal static MethodBase Target() => AccessTools.FirstMethod(typeof(MessageManager),
            method => method.Name.StartsWith("UserCode_RpcKillMessage_"));
    }
}
