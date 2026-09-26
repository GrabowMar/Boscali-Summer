using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BoscaliSummer.Infrastructure.Storage
{
    /// <summary>
    /// Configuration options for the rolling backup and disaster recovery engine.
    /// </summary>
    public sealed class RollingBackupConfig
    {
        public string BaseDirectory { get; set; } = Path.Combine("BepInEx", "config", "BoscaliSummer", "saves");
        public string QuarantineDirectory => Path.Combine(BaseDirectory, "quarantine");
        public int MaxRollingBackups { get; set; } = 3;
        public int MaxQuarantineSnapshots { get; set; } = 10;
        public int MinimumValidFileSizeBytes { get; set; } = 64;
        public bool EnableCrashSnapshots { get; set; } = true;
        public bool StrictCrcVerification { get; set; } = true;
        public int FileLockRetryAttempts { get; set; } = 3;
        public int FileLockRetryBaseDelayMs { get; set; } = 50;
    }

    /// <summary>
    /// Audit result returned by load and rollback operations.
    /// </summary>
    public sealed class RecoveryResult
    {
        public bool Success { get; internal set; }
        public string CampaignId { get; internal set; }
        public string LoadedFilePath { get; internal set; }
        public string ActiveTier { get; internal set; } = "None";
        public bool WasRolledBack { get; internal set; }
        public List<string> QuarantinedFiles { get; } = new List<string>();
        public List<string> AuditTrail { get; } = new List<string>();
        public string FailureReason { get; internal set; }

        internal void Log(string message) => AuditTrail.Add($"[{DateTime.UtcNow:O}] {message}");
    }

    /// <summary>
    /// Production-grade multi-tier rolling backup, integrity validation, and disaster recovery manager.
    /// </summary>
    public sealed class RollingBackupManager
    {
        private readonly RollingBackupConfig config;
        private readonly Action<string, string> logDelegate; // (level, message)
        private readonly object ioLock = new object();

        public RollingBackupManager(RollingBackupConfig config = null, Action<string, string> logDelegate = null)
        {
            this.config = config ?? new RollingBackupConfig();
            this.logDelegate = logDelegate ?? DefaultLogger;
            EnsureDirectoriesExist();
        }

        private static void DefaultLogger(string level, string message)
        {
            Console.WriteLine($"[Persistence.DR] [{level}] {message}");
        }

        private void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(config.BaseDirectory)) Directory.CreateDirectory(config.BaseDirectory);
            if (!Directory.Exists(config.QuarantineDirectory)) Directory.CreateDirectory(config.QuarantineDirectory);
        }

        // =========================================================================
        // ATOMIC SAVE & REVERSE ROLLING ROTATION
        // =========================================================================

        /// <summary>
        /// Atomically saves the campaign payload with reverse-order rolling backup rotation.
        /// </summary>
        public void SaveAtomic(string campaignId, string jsonPayload)
        {
            if (string.IsNullOrWhiteSpace(campaignId)) throw new ArgumentNullException(nameof(campaignId));
            if (string.IsNullOrWhiteSpace(jsonPayload)) throw new ArgumentNullException(nameof(jsonPayload));

            lock (ioLock)
            {
                EnsureDirectoriesExist();

                string primaryPath = GetPrimaryPath(campaignId);
                string tempPath = primaryPath + ".tmp";
                byte[] bytes = Encoding.UTF8.GetBytes(jsonPayload);

                // 1. Write staged .tmp file with write-through flush
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(flushToDisk: true);
                }

                // 2. Pre-commit validation of the staged file
                var (isValid, reason, _) = ValidateFile(tempPath, campaignId);
                if (!isValid)
                {
                    TryDeleteFile(tempPath);
                    string err = $"Staged save payload failed validation for '{campaignId}': {reason}. Aborting commit to protect backup chain.";
                    logDelegate("ERROR", err);
                    throw new InvalidOperationException(err);
                }

                // 3. Reverse Rolling Backup Rotation (bakN-1 -> bakN ... json -> bak1)
                RotateBackups(campaignId);

                // 4. Move .tmp to primary .json
                ExecuteWithRetry(() =>
                {
                    StripReadOnly(primaryPath);
                    if (File.Exists(primaryPath)) File.Delete(primaryPath);
                    File.Move(tempPath, primaryPath);
                }, "CommitTempToPrimary");

                logDelegate("INFO", $"Successfully committed save for '{campaignId}' (Bytes: {bytes.Length}).");
            }
        }

        private void RotateBackups(string campaignId)
        {
            int max = config.MaxRollingBackups;
            if (max <= 0) return;

            // Step A: Prune the oldest tier (e.g. bak3)
            string oldestBak = GetBackupPath(campaignId, max);
            TryDeleteFile(oldestBak);

            // Step B: Shift intermediate backups downwards (bak2 -> bak3, bak1 -> bak2)
            for (int k = max - 1; k >= 1; k--)
            {
                string src = GetBackupPath(campaignId, k);
                string dst = GetBackupPath(campaignId, k + 1);
                if (File.Exists(src))
                {
                    ExecuteWithRetry(() =>
                    {
                        StripReadOnly(dst);
                        if (File.Exists(dst)) File.Delete(dst);
                        File.Move(src, dst);
                    }, $"RotateBak_{k}_to_{k + 1}");
                }
            }

            // Step C: Move existing primary .json to .bak1
            string primary = GetPrimaryPath(campaignId);
            string bak1 = GetBackupPath(campaignId, 1);
            if (File.Exists(primary))
            {
                ExecuteWithRetry(() =>
                {
                    StripReadOnly(bak1);
                    if (File.Exists(bak1)) File.Delete(bak1);
                    File.Move(primary, bak1);
                }, "RotatePrimaryToBak1");
            }
        }

        // =========================================================================
        // MULTI-TIER RECOVERY & AUTOMATIC CHAIN ROLLBACK
        // =========================================================================

        /// <summary>
        /// Attempts to load the primary save. If corrupt, automatically rolls back through
        /// the backup chain, quarantining corrupted files with diagnostic sidecars.
        /// </summary>
        public bool TryLoadWithChainRecovery(string campaignId, out string payload, out RecoveryResult result)
        {
            if (string.IsNullOrWhiteSpace(campaignId)) throw new ArgumentNullException(nameof(campaignId));

            lock (ioLock)
            {
                result = new RecoveryResult { CampaignId = campaignId };
                payload = null;

                // Build prioritized candidate list: Primary -> bak1 -> bak2 -> ... -> CrashSnapshots
                var candidates = BuildCandidateChain(campaignId);

                foreach (var candidate in candidates)
                {
                    result.Log($"Evaluating candidate: '{candidate.Path}' (Tier: {candidate.TierName})");

                    if (!File.Exists(candidate.Path))
                    {
                        result.Log($"Candidate does not exist: '{candidate.Path}'");
                        continue;
                    }

                    var (isValid, failureReason, autopsy) = ValidateFile(candidate.Path, campaignId);

                    if (isValid)
                    {
                        // Candidate is healthy!
                        try
                        {
                            payload = File.ReadAllText(candidate.Path, Encoding.UTF8);
                            result.Success = true;
                            result.LoadedFilePath = candidate.Path;
                            result.ActiveTier = candidate.TierName;

                            if (candidate.TierName != "Primary")
                            {
                                result.WasRolledBack = true;
                                logDelegate("WARNING", $"[Persistence.DR] Auto-healed campaign '{campaignId}' from {candidate.TierName} ('{candidate.Path}')!");

                                // Heal: Restore recovered backup as active primary save
                                TryHealPrimaryFromBackup(campaignId, candidate.Path);
                            }

                            result.Log($"Successfully loaded valid state from '{candidate.Path}' ({candidate.TierName}).");
                            return true;
                        }
                        catch (Exception ex)
                        {
                            result.Log($"Exception reading valid candidate '{candidate.Path}': {ex.Message}");
                        }
                    }

                    // Candidate is corrupt -> Quarantine and continue down the chain!
                    logDelegate("ERROR", $"Corruption in '{candidate.Path}' ({candidate.TierName}): {failureReason}");
                    string quarantinedPath = QuarantineCorruptedFile(campaignId, candidate, failureReason, autopsy);
                    result.QuarantinedFiles.Add(quarantinedPath);
                }

                // If all candidates in chain failed:
                result.Success = false;
                result.FailureReason = "All candidate files in the backup chain are corrupt or missing.";
                logDelegate("CRITICAL", $"[Persistence.DR] Disaster Recovery FAILED for '{campaignId}'. All backups exhausted!");
                return false;
            }
        }

        private void TryHealPrimaryFromBackup(string campaignId, string healthyBackupPath)
        {
            try
            {
                string primaryPath = GetPrimaryPath(campaignId);
                StripReadOnly(primaryPath);
                File.Copy(healthyBackupPath, primaryPath, overwrite: true);
                logDelegate("INFO", $"Restored healthy backup '{healthyBackupPath}' as active '{primaryPath}'.");
            }
            catch (Exception ex)
            {
                logDelegate("ERROR", $"Failed to restore healed backup to primary: {ex.Message}");
            }
        }

        private struct CandidateInfo
        {
            public string Path;
            public string TierName;
        }

        private List<CandidateInfo> BuildCandidateChain(string campaignId)
        {
            var chain = new List<CandidateInfo>();

            // 1. Primary
            chain.Add(new CandidateInfo { Path = GetPrimaryPath(campaignId), TierName = "Primary" });

            // 2. Rolling Backups (.bak1, .bak2, .bak3, ...)
            for (int i = 1; i <= config.MaxRollingBackups; i++)
            {
                chain.Add(new CandidateInfo { Path = GetBackupPath(campaignId, i), TierName = $"Backup{i}" });
            }

            // 3. Newest Crash Snapshot (if enabled)
            if (config.EnableCrashSnapshots)
            {
                string newestCrash = GetNewestCrashSnapshot(campaignId);
                if (!string.IsNullOrEmpty(newestCrash))
                {
                    chain.Add(new CandidateInfo { Path = newestCrash, TierName = "CrashSnapshot" });
                }
            }

            return chain;
        }

        // =========================================================================
        // 5-PHASE INTEGRITY VALIDATION
        // =========================================================================

        public sealed class ValidationAutopsy
        {
            public long FileSizeBytes;
            public bool HasLeadingNullBytes;
            public bool HasTrailingNullBytes;
            public int TrailingNullCount;
            public bool JsonSyntaxValid;
            public string SyntaxError;
            public bool EnvelopeValid;
            public string HeaderCrc32;
            public string ComputedCrc32;
        }

        public (bool IsValid, string Reason, ValidationAutopsy Autopsy) ValidateFile(string filePath, string expectedCampaignId)
        {
            var autopsy = new ValidationAutopsy();

            if (!File.Exists(filePath))
                return (false, "File does not exist.", autopsy);

            var fileInfo = new FileInfo(filePath);
            autopsy.FileSizeBytes = fileInfo.Length;

            // Phase 1: Physical Sanity
            if (fileInfo.Length < config.MinimumValidFileSizeBytes)
                return (false, $"File length ({fileInfo.Length} bytes) is below minimum threshold ({config.MinimumValidFileSizeBytes} bytes).", autopsy);

            byte[] rawBytes;
            try
            {
                rawBytes = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                return (false, $"Cannot read physical bytes: {ex.Message}", autopsy);
            }

            // Phase 2: Binary & Text Hygiene (Null-byte scan)
            int len = rawBytes.Length;
            if (len > 0 && rawBytes[0] == 0x00) autopsy.HasLeadingNullBytes = true;

            int nullCount = 0;
            for (int i = len - 1; i >= 0 && rawBytes[i] == 0x00; i--) nullCount++;
            if (nullCount > 0)
            {
                autopsy.HasTrailingNullBytes = true;
                autopsy.TrailingNullCount = nullCount;
                return (false, $"File contains {nullCount} trailing null bytes (SSD/OS write-cache flush truncation).", autopsy);
            }

            if (autopsy.HasLeadingNullBytes)
                return (false, "File contains leading null bytes (corrupted header).", autopsy);

            string text;
            try
            {
                text = Encoding.UTF8.GetString(rawBytes);
            }
            catch (Exception ex)
            {
                return (false, $"UTF-8 decoding failed: {ex.Message}", autopsy);
            }

            // Phase 3: Structural JSON Parsing
            if (!FastJsonSyntaxCheck(text, out string syntaxErr))
            {
                autopsy.JsonSyntaxValid = false;
                autopsy.SyntaxError = syntaxErr;
                return (false, $"JSON syntax violation: {syntaxErr}", autopsy);
            }
            autopsy.JsonSyntaxValid = true;

            // Phase 4: Envelope & Schema Sanity
            var (envValid, envErr, headerCrc) = ValidateEnvelope(text, expectedCampaignId);
            autopsy.EnvelopeValid = envValid;
            autopsy.HeaderCrc32 = headerCrc;

            if (!envValid)
                return (false, $"Envelope validation failed: {envErr}", autopsy);

            // Phase 5: Cryptographic/Polynomial Integrity (IEEE 802.3 CRC32)
            if (config.StrictCrcVerification && !string.IsNullOrEmpty(headerCrc))
            {
                // Calculate CRC over payload excluding the CRC header itself
                string canonicalPayload = ExtractCanonicalPayloadForCrc(text);
                uint calculated = Crc32Util.Compute(canonicalPayload);
                string computedHex = calculated.ToString("X8");
                autopsy.ComputedCrc32 = computedHex;

                if (!string.Equals(headerCrc, computedHex, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"CRC32 checksum mismatch (Header: 0x{headerCrc}, Computed: 0x{computedHex}). Bit rot or tampering detected.", autopsy);
                }
            }

            return (true, "File passed 5-phase validation.", autopsy);
        }

        private static bool FastJsonSyntaxCheck(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "Empty content"; return false; }

            json = json.Trim();
            if ((!json.StartsWith("{") || !json.EndsWith("}")) && (!json.StartsWith("[") || !json.EndsWith("]")))
            {
                error = "JSON must start with '{' or '[' and end with '}' or ']'";
                return false;
            }

            // Stack-based bracket balance and quote state scan
            int braceCount = 0;
            int bracketCount = 0;
            bool inString = false;
            bool isEscaped = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    if (isEscaped)
                    {
                        isEscaped = false;
                    }
                    else if (c == '\\')
                    {
                        isEscaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                }
                else if (c == '{') braceCount++;
                else if (c == '}')
                {
                    braceCount--;
                    if (braceCount < 0) { error = $"Unmatched closing brace '}}' at index {i}"; return false; }
                }
                else if (c == '[') bracketCount++;
                else if (c == ']')
                {
                    bracketCount--;
                    if (bracketCount < 0) { error = $"Unmatched closing bracket ']' at index {i}"; return false; }
                }
            }

            if (inString) { error = "Unterminated string literal"; return false; }
            if (braceCount != 0) { error = $"Unclosed braces (unbalanced count: {braceCount})"; return false; }
            if (bracketCount != 0) { error = $"Unclosed brackets (unbalanced count: {bracketCount})"; return false; }

            return true;
        }

        private static (bool Valid, string Error, string HeaderCrc) ValidateEnvelope(string json, string expectedCampaignId)
        {
            // Fast regex scan for envelope properties
            var versionMatch = Regex.Match(json, @"""formatVersion""\s*:\s*(\d+)");
            if (!versionMatch.Success) return (false, "Missing 'formatVersion' field.", null);

            var idMatch = Regex.Match(json, @"""campaignId""\s*:\s*""([^""]+)""");
            if (!idMatch.Success) return (false, "Missing 'campaignId' field.", null);

            string foundId = idMatch.Groups[1].Value;
            if (!string.Equals(foundId, expectedCampaignId, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Mismatched campaignId. Expected '{expectedCampaignId}', found '{foundId}'.", null);
            }

            if (!json.Contains("\"sections\""))
                return (false, "Missing mandatory 'sections' container.", null);

            var crcMatch = Regex.Match(json, @"""crc32""\s*:\s*""([A-Fa-f0-9]{8})""");
            string crc = crcMatch.Success ? crcMatch.Groups[1].Value : null;

            return (true, null, crc);
        }

        /// <summary>
        /// Removes the crc32 header line to reproduce the canonical content hashed during save.
        /// </summary>
        public static string ExtractCanonicalPayloadForCrc(string json)
        {
            return Regex.Replace(json, @"""crc32""\s*:\s*""[A-Fa-f0-9]{8}""\s*,?", string.Empty);
        }

        // =========================================================================
        // FORENSIC QUARANTINING & LRU CEILING
        // =========================================================================

        private string QuarantineCorruptedFile(string campaignId, CandidateInfo candidate, string reason, ValidationAutopsy autopsy)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            string baseQuarantineName = $"{campaignId}.{candidate.TierName}.corrupt_{timestamp}";
            string quarantinedPayloadPath = Path.Combine(config.QuarantineDirectory, baseQuarantineName + ".json");
            string quarantinedMetaPath = Path.Combine(config.QuarantineDirectory, baseQuarantineName + ".meta.json");

            try
            {
                // 1. Bit-for-bit physical copy (Preserves unparseable bytes & null chars)
                if (File.Exists(candidate.Path))
                {
                    File.Copy(candidate.Path, quarantinedPayloadPath, overwrite: true);
                }

                // 2. Structured diagnostic autopsy sidecar
                string metaJson = BuildForensicSidecarJson(campaignId, candidate, reason, autopsy);
                File.WriteAllText(quarantinedMetaPath, metaJson, Encoding.UTF8);

                logDelegate("INFO", $"Quarantined corrupted save to '{quarantinedPayloadPath}' and sidecar '{quarantinedMetaPath}'.");

                // 3. LRU Quota Enforcement
                EnforceQuarantineCeiling(campaignId);
            }
            catch (Exception ex)
            {
                logDelegate("ERROR", $"Failed to write quarantine evidence: {ex.Message}");
            }

            return quarantinedPayloadPath;
        }

        private static string BuildForensicSidecarJson(string campaignId, CandidateInfo candidate, string reason, ValidationAutopsy autopsy)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"quarantineTimestampUtc\": \"{DateTime.UtcNow:O}\",");
            sb.AppendLine($"  \"campaignId\": \"{EscapeJson(campaignId)}\",");
            sb.AppendLine($"  \"sourceFileName\": \"{EscapeJson(Path.GetFileName(candidate.Path))}\",");
            sb.AppendLine($"  \"sourceTier\": \"{candidate.TierName}\",");
            sb.AppendLine($"  \"fileSizeBytes\": {autopsy?.FileSizeBytes ?? 0},");
            sb.AppendLine($"  \"quarantineReason\": \"{EscapeJson(reason)}\",");
            sb.AppendLine("  \"validationAutopsy\": {");
            sb.AppendLine($"    \"hasLeadingNullBytes\": {(autopsy?.HasLeadingNullBytes == true ? "true" : "false")},");
            sb.AppendLine($"    \"hasTrailingNullBytes\": {(autopsy?.HasTrailingNullBytes == true ? "true" : "false")},");
            sb.AppendLine($"    \"trailingNullCount\": {autopsy?.TrailingNullCount ?? 0},");
            sb.AppendLine($"    \"jsonSyntaxValid\": {(autopsy?.JsonSyntaxValid == true ? "true" : "false")},");
            sb.AppendLine($"    \"syntaxError\": \"{EscapeJson(autopsy?.SyntaxError ?? "None")}\",");
            sb.AppendLine($"    \"envelopeValid\": {(autopsy?.EnvelopeValid == true ? "true" : "false")},");
            sb.AppendLine($"    \"headerCrc32\": \"{autopsy?.HeaderCrc32 ?? "None"}\",");
            sb.AppendLine($"    \"computedCrc32\": \"{autopsy?.ComputedCrc32 ?? "None"}\"");
            sb.AppendLine("  },");
            sb.AppendLine("  \"systemInfo\": {");
            sb.AppendLine($"    \"os\": \"{EscapeJson(Environment.OSVersion.ToString())}\",");
            sb.AppendLine($"    \"machineName\": \"{EscapeJson(Environment.MachineName)}\",");
            sb.AppendLine($"    \"clrVersion\": \"{Environment.Version}\"");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private void EnforceQuarantineCeiling(string campaignId)
        {
            try
            {
                var dir = new DirectoryInfo(config.QuarantineDirectory);
                var pattern = $"{campaignId}.*.corrupt_*.json";
                var files = dir.GetFiles(pattern)
                               .OrderBy(f => f.CreationTimeUtc)
                               .ToList();

                if (files.Count > config.MaxQuarantineSnapshots)
                {
                    int removeCount = files.Count - config.MaxQuarantineSnapshots;
                    for (int i = 0; i < removeCount; i++)
                    {
                        var file = files[i];
                        string metaFile = Path.ChangeExtension(file.FullName, ".meta.json");

                        TryDeleteFile(file.FullName);
                        TryDeleteFile(metaFile);
                        logDelegate("INFO", $"Pruned expired quarantine artifact '{file.Name}' to honor ceiling of {config.MaxQuarantineSnapshots}.");
                    }
                }
            }
            catch (Exception ex)
            {
                logDelegate("WARNING", $"Error enforcing quarantine ceiling: {ex.Message}");
            }
        }

        // =========================================================================
        // EMERGENCY CRASH SNAPSHOTS
        // =========================================================================

        /// <summary>
        /// Immediately writes a synchronous crash snapshot bypasses debouncing.
        /// Does NOT rotate or corrupt the healthy .bak chain.
        /// </summary>
        public string SaveEmergencyCrashSnapshot(string campaignId, string jsonPayload, Exception fatalException)
        {
            lock (ioLock)
            {
                EnsureDirectoriesExist();
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string crashPath = Path.Combine(config.BaseDirectory, $"{campaignId}.crash_{timestamp}.json");

                try
                {
                    // Inject crash context into the payload if valid JSON
                    string finalPayload = jsonPayload;
                    if (fatalException != null && finalPayload.TrimEnd().EndsWith("}"))
                    {
                        string crashBlock = $",\"_emergencyCrash\":{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"type\":\"{EscapeJson(fatalException.GetType().FullName)}\",\"message\":\"{EscapeJson(fatalException.Message)}\"}}}}";
                        int lastBrace = finalPayload.LastIndexOf('}');
                        finalPayload = finalPayload.Substring(0, lastBrace) + crashBlock;
                    }

                    byte[] bytes = Encoding.UTF8.GetBytes(finalPayload);
                    using (var fs = new FileStream(crashPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.None))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                        fs.Flush(flushToDisk: true);
                    }

                    logDelegate("CRITICAL", $"[Persistence.DR] Emergency crash snapshot saved to '{crashPath}'.");
                    return crashPath;
                }
                catch (Exception ex)
                {
                    logDelegate("CRITICAL", $"Failed to save emergency crash snapshot: {ex.Message}");
                    return null;
                }
            }
        }

        private string GetNewestCrashSnapshot(string campaignId)
        {
            try
            {
                var dir = new DirectoryInfo(config.BaseDirectory);
                var crashFiles = dir.GetFiles($"{campaignId}.crash_*.json")
                                    .OrderByDescending(f => f.CreationTimeUtc)
                                    .ToList();

                return crashFiles.Count > 0 ? crashFiles[0].FullName : null;
            }
            catch
            {
                return null;
            }
        }

        // =========================================================================
        // PATH RESOLUTION & UTILITIES
        // =========================================================================

        public string GetPrimaryPath(string campaignId) => Path.Combine(config.BaseDirectory, $"{campaignId}.json");

        public string GetBackupPath(string campaignId, int tier) => Path.Combine(config.BaseDirectory, $"{campaignId}.bak{tier}");

        private void ExecuteWithRetry(Action action, string operationName)
        {
            int attempts = 0;
            while (true)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException ex) when (attempts < config.FileLockRetryAttempts)
                {
                    attempts++;
                    int delay = config.FileLockRetryBaseDelayMs * attempts;
                    logDelegate("WARNING", $"IOException in '{operationName}' (Attempt {attempts}/{config.FileLockRetryAttempts}). Retrying in {delay}ms. Error: {ex.Message}");
                    Thread.Sleep(delay);
                }
            }
        }

        private static void StripReadOnly(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    var attrs = File.GetAttributes(path);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                    }
                }
                catch { }
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    StripReadOnly(path);
                    File.Delete(path);
                }
                catch { }
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }

    /// <summary>
    /// Fast zero-allocation IEEE 802.3 CRC32 calculation table.
    /// </summary>
    public static class Crc32Util
    {
        private const uint Polynomial = 0xEDB88320u;
        private static readonly uint[] Table = new uint[256];

        static Crc32Util()
        {
            for (uint i = 0; i < 256; i++)
            {
                uint entry = i;
                for (int j = 0; j < 8; j++)
                {
                    entry = (entry & 1) != 0 ? (entry >> 1) ^ Polynomial : entry >> 1;
                }
                Table[i] = entry;
            }
        }

        public static uint Compute(byte[] buffer, int offset, int length)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < offset + length; i++)
            {
                byte index = (byte)((crc & 0xFF) ^ buffer[i]);
                crc = (crc >> 8) ^ Table[index];
            }
            return ~crc;
        }

        public static uint Compute(string utf8String)
        {
            if (string.IsNullOrEmpty(utf8String)) return 0;
            byte[] bytes = Encoding.UTF8.GetBytes(utf8String);
            return Compute(bytes, 0, bytes.Length);
        }
    }
}
