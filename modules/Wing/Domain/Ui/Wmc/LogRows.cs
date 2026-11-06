using System.Text;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Enum names as words for a toast or an ack line.</summary>
    internal static class LogRows
    {
        /// <summary>"NoTarget" → "no target".</summary>
        internal static string Words(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (char.IsUpper(c) && i > 0) sb.Append(' ');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
