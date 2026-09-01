using System;
using System.Collections.Generic;
using System.IO;

namespace BoscaliSummer.Core
{
    internal sealed class RadioTrack
    {
        public string Title { get; }
        public string Path { get; }
        public string Extension { get; }

        public RadioTrack(string title, string path, string extension)
        {
            Title = title;
            Path = path;
            Extension = extension;
        }
    }

    internal sealed class RadioChannel
    {
        public string Name { get; }
        public RadioTrack[] Tracks { get; }

        public RadioChannel(string name, RadioTrack[] tracks)
        {
            Name = name;
            Tracks = tracks;
        }
    }

    /// <summary>
    /// Builds a bounded, deterministic catalogue from the local radio directory. Only the
    /// root and its immediate child directories are stations; links and deeper trees are
    /// ignored so the scanner cannot escape the user-visible import boundary.
    /// </summary>
    internal sealed class RadioLibrary
    {
        public const int MaximumChannels = 32;
        public const int MaximumTracks = 512;
        public const long MaximumTrackBytes = 512L * 1024L * 1024L;

        public RadioChannel[] Channels { get; }
        public int TrackCount { get; }

        private RadioLibrary(RadioChannel[] channels, int trackCount)
        {
            Channels = channels;
            TrackCount = trackCount;
        }

        public static RadioLibrary Scan(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("A radio library root is required.", nameof(root));

            string canonicalRoot = Path.GetFullPath(root);
            Directory.CreateDirectory(canonicalRoot);

            var channels = new List<RadioChannel>();
            int totalTracks = 0;

            AddChannel(channels, "LOCAL", canonicalRoot, canonicalRoot, ref totalTracks);

            var directories = new List<DirectoryInfo>();
            foreach (DirectoryInfo directory in new DirectoryInfo(canonicalRoot).EnumerateDirectories())
                if ((directory.Attributes & FileAttributes.ReparsePoint) == 0)
                    directories.Add(directory);
            directories.Sort((left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));

            for (int i = 0; i < directories.Count && channels.Count < MaximumChannels; i++)
            {
                if (totalTracks >= MaximumTracks) break;
                AddChannel(
                    channels, CleanLabel(directories[i].Name), directories[i].FullName,
                    canonicalRoot, ref totalTracks);
            }

            return new RadioLibrary(channels.ToArray(), totalTracks);
        }

        public static bool IsSupportedExtension(string extension) =>
            string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase);

        private static void AddChannel(
            List<RadioChannel> channels,
            string name,
            string directory,
            string root,
            ref int totalTracks)
        {
            var paths = new List<string>();
            foreach (string candidate in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if (totalTracks + paths.Count >= MaximumTracks) break;
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(candidate);
                    var info = new FileInfo(fullPath);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        info.Length <= 0 || info.Length > MaximumTrackBytes ||
                        !IsContained(root, fullPath) ||
                        !IsSupportedExtension(info.Extension))
                        continue;
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                paths.Add(fullPath);
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            if (paths.Count == 0) return;

            var tracks = new RadioTrack[paths.Count];
            for (int i = 0; i < paths.Count; i++)
            {
                string extension = Path.GetExtension(paths[i]).ToLowerInvariant();
                tracks[i] = new RadioTrack(
                    CleanLabel(Path.GetFileNameWithoutExtension(paths[i])), paths[i], extension);
            }
            channels.Add(new RadioChannel(name, tracks));
            totalTracks += tracks.Length;
        }

        private static bool IsContained(string root, string path)
        {
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string CleanLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "UNTITLED";
            string clean = value.Replace('_', ' ').Trim();
            return clean.Length <= 48 ? clean : clean.Substring(0, 48);
        }
    }
}
