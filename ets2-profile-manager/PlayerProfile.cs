
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Ets2ProfileManager.Properties;

namespace Ets2ProfileManager
{
    public class PlayerProfile
    {
        private string? _username;
        private string? _directory;
        private string? _directoryshort;
        private bool _decrypted = false;
        private string? _etsats;
        private string? _lastaccess;

        public string Directory
        {
            get => _directory ?? "none";
            set => _directory = value;
        }

        public string DirectoryShort
        {
            get => _directoryshort ?? "none";
            set => _directoryshort = value;
        }
        public string Username
        {
            get => _username ?? "none";
            set => _username = value;
        }

        public bool Decrypted
        {
            get => _decrypted;
            set => _decrypted = value;
        }

        public string EtsAts
        {
            get => _etsats ?? "none";
            set => _etsats = value;
        }

        public string? LastAccess
        {
            get => _lastaccess ?? null;
            set => _lastaccess = value;
        }

        public bool IsCloud { get; set; }

        public string CloudTag => IsCloud ? "Cloud • " : string.Empty;

        public static List<PlayerProfile> GetEtsProfiles(string game="ets")
        {
            List<PlayerProfile> pf = new();
            foreach (string profiledirectory in GetProfileDirectories(game))
            {
                if (System.IO.Directory.Exists(profiledirectory))
                {
                    AddProfilesFromDirectory(pf, profiledirectory, game);
                }
            }
            return pf;
        }

        /// <summary>
        /// Candidate game home directories. MyDocuments is the default, but the
        /// game can run with -homedir on another drive/folder.
        /// Scans fixed drives for a second Documents-style home, no hardcoded paths.
        /// </summary>
        public static IEnumerable<string> GetGameHomeDirectories(string game = "ets")
        {
            string subfolder = game == "ets"
                ? @"Euro Truck Simulator 2"
                : @"American Truck Simulator";

            string myDocs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                subfolder);

            yield return myDocs;

            // Fallback: game home on another drive (e.g. custom -homedir).
            // Looks like <drive>:\<user>\Documents\<game> one level deep.
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }
                string root = drive.RootDirectory.FullName;
                if (root.StartsWith(Path.GetPathRoot(myDocs) ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string[] userDirs;
                try
                {
                    userDirs = System.IO.Directory.GetDirectories(root);
                }
                catch
                {
                    continue;
                }
                foreach (string userDir in userDirs)
                {
                    string candidate = Path.Combine(userDir, "Documents", subfolder);
                    if (string.Equals(candidate, myDocs, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (System.IO.Directory.Exists(Path.Combine(candidate, "profiles")))
                    {
                        yield return candidate;
                    }
                }
            }
        }

        public static IEnumerable<string> GetProfileDirectories(string game = "ets")
        {
            foreach (string home in GetGameHomeDirectories(game))
            {
                yield return Path.Combine(home, "profiles");
                // Steam Cloud profiles: same file formats, no profile.sii/save locally.
                yield return Path.Combine(home, "steam_profiles");
            }
        }

        public static string GetGameHomeDirectory(string game, string profileDirectory)
        {
            // profileDirectory is <home>\profiles\<hex> or <home>\steam_profiles\<hex>; walk back up.
            string? container = Path.GetDirectoryName(profileDirectory);
            string? home = container != null ? Path.GetDirectoryName(container) : null;
            if (home != null && System.IO.Directory.Exists(home))
            {
                return home;
            }
            // Fallback: first existing home.
            foreach (string candidate in GetGameHomeDirectories(game))
            {
                if (System.IO.Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                game == "ets" ? @"Euro Truck Simulator 2" : @"American Truck Simulator");
        }

        private static void AddProfilesFromDirectory(List<PlayerProfile> pf, string profiledirectory, string game)
        {
            bool isCloud = Path.GetFileName(profiledirectory.TrimEnd(Path.DirectorySeparatorChar))
                .Equals("steam_profiles", StringComparison.OrdinalIgnoreCase);
            string[] profilesubdirectories;
            try
            {
                profilesubdirectories = System.IO.Directory.GetDirectories(profiledirectory);
            }
            catch
            {
                return;
            }
            if (profilesubdirectories.Length > 0)
            {
                foreach (string subdirectory in profilesubdirectories)
                {
                    try
                    {
                        DirectoryInfo di = new(subdirectory);
                        string shortdir = di.Name;
                        // Local profiles carry profile.sii; cloud ones only settings (controls.sii).
                        bool hasIdentity = isCloud
                            ? File.Exists(Path.Combine(subdirectory, "controls.sii"))
                            : File.Exists(Path.Combine(subdirectory, "profile.sii"));
                        if (shortdir.Length > 0 && shortdir.Length % 2 == 0 && shortdir.IsHex() && hasIdentity)
                        {
                            if (pf.Any(p => string.Equals(p.Directory, subdirectory, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }
                            PlayerProfile p = new()
                            {
                                Directory = subdirectory,
                                DirectoryShort = shortdir,
                                Decrypted = isCloud || IsDecrypted(Path.Combine(subdirectory, "profile.sii")),
                                EtsAts = game.ToUpper(),
                                Username = subdirectory.DirectoryToScsUsername(),
                                LastAccess = di.LastWriteTime.ToString(),
                                IsCloud = isCloud,
                            };
                            pf.Add(p);
                        }
                    }
                    catch
                    {
                        // Locked/inaccessible single profile must not break the whole listing.
                    }
                }
            }
        }

        public static bool CopyProfile(PlayerProfile profile, string newusername)
        {
            string newDirectoryShort = newusername.ScsUsernameToDirectory();

            // Derive base from the profile itself so any game home works,
            // fall back to the known homes if that fails.
            string? profileDirectoryBase = Path.GetDirectoryName(profile.Directory);
            if (string.IsNullOrEmpty(profileDirectoryBase) || !System.IO.Directory.Exists(profileDirectoryBase))
            {
                string game = profile.EtsAts.ToLower() == "ets" ? "ets" : "ats";
                profileDirectoryBase = Path.Combine(GetGameHomeDirectory(game, profile.Directory), "profiles");
            }
            string newDirectoryFull = Path.Combine(profileDirectoryBase, newDirectoryShort);
            if (System.IO.Directory.Exists(newDirectoryFull))
            {
                return false;
            }

            try
            {
                // Copy profile directory to new directory
                CopyDirectory(profile.Directory, newDirectoryFull, true);
                string filename = Path.Combine(newDirectoryFull, "profile.sii");
                if (!IsDecrypted(filename))
                {
                    bool result = DecryptFile(newDirectoryFull, "profile.sii");
                    if (!result)
                    {
                        System.IO.Directory.Delete(newDirectoryFull, true);
                        return false;
                    }
                }

                File.WriteAllText(filename, ReplaceProfileName(File.ReadAllText(filename), profile.Username, newusername));

                // Remove stale profile backup from copied folder
                string bakFilename = Path.Combine(newDirectoryFull, "profile.bak.sii");
                if (File.Exists(bakFilename))
                {
                    try { File.Delete(bakFilename); } catch { }
                }

                return true;
            }
            catch
            {
                if (System.IO.Directory.Exists(newDirectoryFull))
                {
                    try { System.IO.Directory.Delete(newDirectoryFull, true); } catch { }
                }
                return false;
            }
        }

        // Rename a profile in place (directory move + profile_name update).
        public static bool RenameProfile(PlayerProfile profile, string newusername)
        {
            string newDirectoryShort = newusername.ScsUsernameToDirectory();
            string? profileDirectoryBase = Path.GetDirectoryName(profile.Directory);
            if (string.IsNullOrEmpty(profileDirectoryBase))
            {
                return false;
            }
            string newDirectoryFull = Path.Combine(profileDirectoryBase, newDirectoryShort);

            if (System.IO.Directory.Exists(newDirectoryFull))
            {
                return false;
            }

            try
            {
                System.IO.Directory.Move(profile.Directory, newDirectoryFull);

                try
                {
                    string filename = Path.Combine(newDirectoryFull, "profile.sii");
                    if (!IsDecrypted(filename))
                    {
                        bool result = DecryptFile(newDirectoryFull, "profile.sii");
                        if (!result)
                        {
                            throw new InvalidOperationException("Decrypt failed.");
                        }
                    }

                    File.WriteAllText(filename, ReplaceProfileName(File.ReadAllText(filename), profile.Username, newusername));

                    string bakFilename = Path.Combine(newDirectoryFull, "profile.bak.sii");
                    if (File.Exists(bakFilename))
                    {
                        try { File.Delete(bakFilename); } catch { }
                    }
                }
                catch
                {
                    // Best-effort revert: directory was already moved.
                    try { System.IO.Directory.Move(newDirectoryFull, profile.Directory); } catch { }
                    return false;
                }

                profile.Directory = newDirectoryFull;
                profile.DirectoryShort = newDirectoryShort;
                profile.Username = newusername;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ReplaceProfileName(string content, string oldUsername, string newUsername)
        {
            string pattern = @"profile_name:\s*(""[^""\r\n]*""|[^\r\n]+)";
            // MatchEvaluator: newUsername is free text, `$` would be regex substitution syntax otherwise.
            string replacement = $"profile_name: \"{newUsername.Replace("\"", "\\\"")}\"";
            if (Regex.IsMatch(content, pattern))
            {
                return Regex.Replace(content, pattern, _ => replacement);
            }
            return content.Replace(oldUsername, newUsername);
        }

        // Delete a profile directory
        public static bool DeleteProfile(PlayerProfile profile)
        {
            try
            {
                System.IO.Directory.Delete(profile.Directory, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Save a profile directory to a zip file
        public static void BackupProfile(PlayerProfile profile, string zipfilename)
        {
            string startPath = profile.Directory;
            string zipPath = zipfilename;
            ZipFile.CreateFromDirectory(startPath, zipPath);
        }

        // Check the first line of a file to detect if it is encrypted or not
        private static bool IsDecrypted(string filename)
        {
            try
            {
                using StreamReader sr = new(filename);
                string text = sr.ReadLine() ?? string.Empty;
                return text.Contains("SiiNunit");
            }
            catch
            {
                return false;
            }
        }

        // Use SII_Decrypt.exe for decrypting sii files.
        // The temp copy is hash-verified on every run so a planted file can
        // never be executed instead of the embedded resource.
        public static bool DecryptFile(string directory, string filename)
        {
            string tempExeName = Path.Combine(Path.GetTempPath(),
                "SII_Decrypt.exe");

            try
            {
                byte[] bytes = Resources.GetSII_Decrypt();
                string wantHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                string haveHash = File.Exists(tempExeName)
                    ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tempExeName)))
                    : string.Empty;
                if (!string.Equals(wantHash, haveHash, StringComparison.Ordinal))
                {
                    File.WriteAllBytes(tempExeName, bytes);
                }
            }
            catch
            {
                return false;
            }

            try
            {
                using Process process = new();
                process.StartInfo.FileName = tempExeName;
                process.StartInfo.Arguments = filename;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.WorkingDirectory = directory;
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(30_000))
                {
                    try { process.Kill(); } catch { }
                    return false;
                }

                return output.Contains("Result: File is a plain-text SII (1)") ||
                    output.Contains("Result: Success (0)");
            }
            catch
            {
                return false;
            }
        }

        // https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-copy-directories
        private static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            // Get information about the source directory
            DirectoryInfo dir = new DirectoryInfo(sourceDir);

            // Check if the source directory exists
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            // Cache directories before we start copying
            DirectoryInfo[] dirs = dir.GetDirectories();

            // Create the destination directory
            System.IO.Directory.CreateDirectory(destinationDir);

            // Get the files in the source directory and copy to the destination directory
            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath);
            }

            // If recursive and copying subdirectories, recursively call this method
            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }
    }

    public class SaveGame
    {
        public string Path { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string LastWriteTime { get; set; } = string.Empty;

        public static List<SaveGame> GetSaveGames(string profileDirectory)
        {
            List<SaveGame> saves = new();
            string savePath = System.IO.Path.Combine(profileDirectory, "save");
            if (System.IO.Directory.Exists(savePath))
            {
                foreach (string dir in System.IO.Directory.GetDirectories(savePath))
                {
                    var di = new DirectoryInfo(dir);
                    string folderName = di.Name;

                    if (File.Exists(System.IO.Path.Combine(dir, "game.sii")) && File.Exists(System.IO.Path.Combine(dir, "info.sii")))
                    {
                        saves.Add(new SaveGame
                        {
                            Path = dir,
                            FolderName = folderName,
                            DisplayName = ParseSaveName(dir, folderName),
                            LastWriteTime = di.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                    }
                }
            }
            return saves.OrderByDescending(s => s.LastWriteTime).ToList();
        }

        private static string ParseSaveName(string saveDir, string folderName)
        {
            string infoFile = System.IO.Path.Combine(saveDir, "info.sii");
            if (!File.Exists(infoFile)) return folderName;

            try
            {
                if (!IsFileDecrypted(infoFile))
                {
                    PlayerProfile.DecryptFile(saveDir, "info.sii");
                }

                string content = File.ReadAllText(infoFile);
                var match = Regex.Match(content, @"\bname:\s*""?([^""\r\n]+)""?");
                if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                {
                    string nameVal = match.Groups[1].Value.Trim();
                    if (!nameVal.StartsWith("@@"))
                    {
                        return nameVal;
                    }
                }
            }
            catch { }

            if (folderName.Equals("autosave", StringComparison.OrdinalIgnoreCase)) return "Auto Save";
            if (folderName.Equals("autosave_drive", StringComparison.OrdinalIgnoreCase)) return "Auto Save (Drive)";
            if (folderName.Equals("quicksave", StringComparison.OrdinalIgnoreCase)) return "Quick Save";

            return $"Save Slot {folderName}";
        }

        private static bool IsFileDecrypted(string filename)
        {
            using StreamReader sr = new(filename);
            string text = sr.ReadLine() ?? string.Empty;
            sr.Close();
            return text.Contains("SiiNunit");
        }
    }

    public class SaveGameData
    {
        public long Money { get; set; }
        public int XP { get; set; }
        public int Adr { get; set; }
        public int LongDist { get; set; }
        public int HeavyCargo { get; set; }
        public int FragileCargo { get; set; }
        public int UrgentCargo { get; set; }
        public int EcoDriving { get; set; }

        public static SaveGameData Load(string saveDir)
        {
            string gameFile = Path.Combine(saveDir, "game.sii");
            if (!IsFileDecrypted(gameFile))
            {
                PlayerProfile.DecryptFile(saveDir, "game.sii");
            }

            string content = File.ReadAllText(gameFile);
            SaveGameData data = new();

            var moneyMatch = Regex.Match(content, @"\bmoney_account:\s*(-?\d+)");
            if (moneyMatch.Success)
            {
                data.Money = long.Parse(moneyMatch.Groups[1].Value);
            }

            var xpMatch = Regex.Match(content, @"\bexperience_points:\s*(\d+)");
            if (xpMatch.Success)
            {
                data.XP = int.Parse(xpMatch.Groups[1].Value);
            }

            var economyMatch = Regex.Match(content, @"\beconomy\s*:\s*_nameless\.[^\n{]+\{([^}]+)\}");
            string economyBlock = economyMatch.Success ? economyMatch.Groups[1].Value : content;

            data.Adr = GetSkillValue(economyBlock, "adr");
            data.LongDist = GetSkillValue(economyBlock, "long_dist");
            data.HeavyCargo = GetSkillValue(economyBlock, "heavy");
            data.FragileCargo = GetSkillValue(economyBlock, "fragile");
            data.UrgentCargo = GetSkillValue(economyBlock, "urgent");
            data.EcoDriving = GetSkillValue(economyBlock, "mechanical");

            return data;
        }

        private static int GetSkillValue(string block, string key)
        {
            var match = Regex.Match(block, $@"\b{key}:\s*(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        public static void Save(string saveDir, SaveGameData data)
        {
            string gameFile = Path.Combine(saveDir, "game.sii");
            if (!IsFileDecrypted(gameFile))
            {
                PlayerProfile.DecryptFile(saveDir, "game.sii");
            }

            string content = File.ReadAllText(gameFile);

            content = Regex.Replace(content, @"\bmoney_account:\s*-?\d+", $"money_account: {data.Money}");
            content = Regex.Replace(content, @"\bexperience_points:\s*\d+", $"experience_points: {data.XP}");

            var economyMatch = Regex.Match(content, @"\beconomy\s*:\s*(_nameless\.[^\n{]+)\{([^}]+)\}");
            if (economyMatch.Success)
            {
                string economyHeader = economyMatch.Groups[1].Value;
                string economyBlock = economyMatch.Groups[2].Value;

                economyBlock = ReplaceSkillValue(economyBlock, "adr", data.Adr);
                economyBlock = ReplaceSkillValue(economyBlock, "long_dist", data.LongDist);
                economyBlock = ReplaceSkillValue(economyBlock, "heavy", data.HeavyCargo);
                economyBlock = ReplaceSkillValue(economyBlock, "fragile", data.FragileCargo);
                economyBlock = ReplaceSkillValue(economyBlock, "urgent", data.UrgentCargo);
                economyBlock = ReplaceSkillValue(economyBlock, "mechanical", data.EcoDriving);

                string oldEconomyBlock = economyMatch.Value;
                string newEconomyBlock = $"economy : {economyHeader}{{{economyBlock}}}";

                content = content.Replace(oldEconomyBlock, newEconomyBlock);
            }
            else
            {
                content = ReplaceSkillValue(content, "adr", data.Adr);
                content = ReplaceSkillValue(content, "long_dist", data.LongDist);
                content = ReplaceSkillValue(content, "heavy", data.HeavyCargo);
                content = ReplaceSkillValue(content, "fragile", data.FragileCargo);
                content = ReplaceSkillValue(content, "urgent", data.UrgentCargo);
                content = ReplaceSkillValue(content, "mechanical", data.EcoDriving);
            }

            File.WriteAllText(gameFile + ".ets2pm-tmp", content);

            // Re-validate from the temp file before replacing: values must read back.
            string verify = File.ReadAllText(gameFile + ".ets2pm-tmp");
            if (!Regex.IsMatch(verify, $@"\bmoney_account:\s*{data.Money}\b") ||
                !Regex.IsMatch(verify, $@"\bexperience_points:\s*{data.XP}\b"))
            {
                File.Delete(gameFile + ".ets2pm-tmp");
                throw new InvalidOperationException("Validation failed after write. Target untouched.");
            }
            File.Copy(gameFile + ".ets2pm-tmp", gameFile, overwrite: true);
            File.Delete(gameFile + ".ets2pm-tmp");
        }

        private static string ReplaceSkillValue(string block, string key, int value)
        {
            return Regex.Replace(block, $@"\b{key}:\s*\d+", $"{key}: {value}");
        }

        private static bool IsFileDecrypted(string filename)
        {
            try
            {
                using StreamReader sr = new(filename);
                string text = sr.ReadLine() ?? string.Empty;
                return text.Contains("SiiNunit");
            }
            catch
            {
                return false;
            }
        }
    }
}
