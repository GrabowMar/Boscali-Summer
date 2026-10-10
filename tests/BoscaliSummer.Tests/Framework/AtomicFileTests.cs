using System;
using System.IO;
using BoscaliSummer.Core.Storage;

namespace BoscaliSummer.Tests.Framework
{
    internal static class AtomicFileTests
    {
        internal static void Run()
        {
            string directory = Path.Combine(Path.GetTempPath(), "BoscaliAtomic-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "pilots.json");
            Directory.CreateDirectory(directory);
            try
            {
                TestAssert.That(AtomicFile.WriteAllText(path, "original", out _) && File.ReadAllText(path) == "original",
                    "atomic storage creates a complete file");
                TestAssert.That(AtomicFile.WriteAllText(path, "replacement", out _) && File.ReadAllText(path) == "replacement",
                    "atomic storage replaces a complete file");
                // Refuse both replacement and overwrite while retaining a readable original.
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    TestAssert.That(!AtomicFile.WriteAllText(path, "refused", out string error) && !string.IsNullOrEmpty(error),
                        "a locked save reports replacement failure");
                    TestAssert.That(File.ReadAllText(path) == "replacement", "failed replacement preserves saved data");
                }
                TestAssert.That(!File.Exists(path + ".tmp"), "failed replacement clears its temporary file");
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
