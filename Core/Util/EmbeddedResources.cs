using System.IO;
using System.Reflection;

namespace BoscaliSummer.Core.Util
{
    internal static class EmbeddedResources
    {
        /// <summary>The whole embedded resource, or null when it is missing, empty, over <paramref name="maxBytes"/> or truncated.</summary>
        internal static byte[] ReadAll(Assembly assembly, string name, int maxBytes)
        {
            using (Stream stream = assembly.GetManifestResourceStream(name)) return ReadAll(stream, maxBytes);
        }

        /// <summary>Same bounds for any stream (a file or a resource); the caller still owns and disposes the stream.</summary>
        internal static byte[] ReadAll(Stream stream, int maxBytes)
        {
            if (stream == null || stream.Length <= 0 || stream.Length > maxBytes) return null;
            var data = new byte[(int)stream.Length];
            int read = 0;
            while (read < data.Length)
            {
                int count = stream.Read(data, read, data.Length - read);
                if (count <= 0) return null;
                read += count;
            }
            return data;
        }
    }
}
