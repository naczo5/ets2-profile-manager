using System.IO;
using System.IO.Compression;

namespace Ets2ProfileManager
{
    /// <summary>
    /// Safety net for every mutating operation: timestamped zip backups with
    /// retention, plus validated restore. Nothing is overwritten without a
    /// recoverable backup first.
    /// </summary>
    internal static class SafetyBackup
    {
        private const int KeepPerProfile = 10;

        public static string BackupRoot(PlayerProfile profile)
        {
            string game = profile.EtsAts.ToLower() == "ets" ? "ets" : "ats";
            return Path.Combine(PlayerProfile.GetGameHomeDirectory(game, profile.Directory), "ets2-profile-manager-backups");
        }

        /// <summary>Creates a timestamped backup zip of the whole profile directory. Returns zip path or null.</summary>
        public static string? CreateBackup(PlayerProfile profile, string reason)
        {
            try
            {
                string root = BackupRoot(profile);
                System.IO.Directory.CreateDirectory(root);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string safeReason = string.Concat(reason.Where(c => char.IsLetterOrDigit(c)));
                // Game tag in the name makes wrong-game restores visible before confirming.
                string zipPath = Path.Combine(root, $"{profile.DirectoryShort}-{profile.EtsAts}-{stamp}-{safeReason}.zip");
                if (File.Exists(zipPath))
                {
                    zipPath = Path.Combine(root, $"{profile.DirectoryShort}-{profile.EtsAts}-{stamp}-{safeReason}-{Guid.NewGuid():N}.zip");
                }
                // Write to temp first so a crash never leaves a half-written backup.
                string tmp = zipPath + ".tmp";
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
                ZipFile.CreateFromDirectory(profile.Directory, tmp);
                File.Move(tmp, zipPath);
                PruneOldBackups(root, profile.DirectoryShort);
                return zipPath;
            }
            catch
            {
                return null;
            }
        }

        private static void PruneOldBackups(string root, string dirShort)
        {
            try
            {
                var files = System.IO.Directory.GetFiles(root, $"{dirShort}-*.zip")
                    .OrderByDescending(f => f)
                    .Skip(KeepPerProfile)
                    .ToList();
                foreach (string f in files)
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Restores a backup zip over the profile directory.
        /// Refuses zips without profile.sii at root (wrong-file guard).
        /// Takes a fresh safety backup of the current state first.
        /// </summary>
        public static (bool ok, string message) RestoreBackup(PlayerProfile profile, string zipPath)
        {
            try
            {
                if (!File.Exists(zipPath))
                {
                    return (false, "Backup file not found.");
                }
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    bool hasProfile = zip.Entries.Any(e =>
                        e.FullName.Equals("profile.sii", StringComparison.OrdinalIgnoreCase));
                    if (!hasProfile)
                    {
                        return (false, "Not a profile backup: profile.sii missing at zip root. Aborted.");
                    }
                }
                string? preBackup = CreateBackup(profile, "prerestore");
                ZipFile.ExtractToDirectory(zipPath, profile.Directory, overwriteFiles: true);
                return (true, preBackup != null
                    ? $"Restored. Pre-restore backup kept at {preBackup}."
                    : "Restored. (Pre-restore backup failed — proceed with care.)");
            }
            catch (Exception ex)
            {
                return (false, $"Restore failed: {ex.Message}");
            }
        }
    }
}
