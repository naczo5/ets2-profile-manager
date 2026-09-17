using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ets2ProfileManager
{
    public sealed partial class MainWindow : Window
    {
        private List<PlayerProfile> _profiles = new();

        private bool _activatedOnce;

        public MainWindow()
        {
            InitializeComponent();
            LoadProfiles(showDialog: false);
            Activated += (_, _) =>
            {
                // XamlRoot only exists after activation: defer the empty-state dialog here.
                if (_activatedOnce)
                {
                    return;
                }
                _activatedOnce = true;
                if (_profiles.Count == 0)
                {
                    _ = Dialogs.MessageAsync(this, "No profiles found",
                        "No profiles found. You should deactivate Steam Cloud synchronization in the game's profile settings.");
                }
            };
        }

        private async void LoadProfiles(bool showDialog = true)
        {
            try
            {
                _profiles = new List<PlayerProfile>();
                _profiles.AddRange(PlayerProfile.GetEtsProfiles(game: "ets"));
                _profiles.AddRange(PlayerProfile.GetEtsProfiles(game: "ats"));
            }
            catch (Exception ex)
            {
                statusBarText.Text = $"Error loading profiles: {ex.Message}";
                return;
            }
            lvProfiles.ItemsSource = _profiles;
            statusBarText.Text = $"{_profiles.Count} profiles found. Right-click a row for actions.";
            if (_profiles.Count == 0 && showDialog)
            {
                await Dialogs.MessageAsync(this, "No profiles found",
                    "No profiles found. You should deactivate Steam Cloud synchronization in the game's profile settings.");
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadProfiles();

        private static PlayerProfile? ItemOf(object sender) =>
            (sender as MenuFlyoutItem)?.DataContext as PlayerProfile;

        private static bool GameRunning() => GameGuard.IsRunning();

        /// <summary>Cloud profiles support sync-target, backup and explore only.</summary>
        private async Task<bool> RejectCloudAsync(PlayerProfile profile, string action)
        {
            if (!profile.IsCloud)
            {
                return false;
            }
            await Dialogs.MessageAsync(this, "Cloud profile",
                $"{action} is not supported for Steam Cloud profiles (Steam owns those files and would fight the change). " +
                "You can sync settings TO a Cloud profile, back it up, or open it in Explorer.");
            statusBarText.Text = $"{action} not supported for Cloud profiles.";
            return true;
        }

        private async void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not PlayerProfile toCopy)
            {
                return;
            }
            if (await RejectCloudAsync(toCopy, "Copy"))
            {
                return;
            }
            if (GameRunning())
            {
                await Dialogs.MessageAsync(this, "Game is running", "You should end the game before copying a profile.");
                statusBarText.Text = "Copy canceled, game is running.";
                return;
            }
            string? name = await Dialogs.InputAsync(this, "Copy profile", "New user name (must be unique):");
            if (string.IsNullOrWhiteSpace(name) || name == toCopy.Username)
            {
                statusBarText.Text = "Copying canceled.";
                return;
            }
            if (!StringExtensions.IsEncodableUsername(name, out string reason))
            {
                await Dialogs.MessageAsync(this, "Invalid name", reason);
                statusBarText.Text = "Copy canceled, invalid user name.";
                return;
            }
            if (_profiles.Any(p => p.Username.Equals(name, StringComparison.OrdinalIgnoreCase) && p.EtsAts == toCopy.EtsAts))
            {
                await Dialogs.MessageAsync(this, "Not unique", $"The new user name must be unique. {name} is already used.");
                statusBarText.Text = "Copy canceled, non unique user name.";
                return;
            }
            if (!PlayerProfile.CopyProfile(toCopy, name))
            {
                await Dialogs.MessageAsync(this, "Copy failed", "The destination may already exist or files are locked.");
                statusBarText.Text = "Copy failed.";
                return;
            }
            LoadProfiles();
            statusBarText.Text = $"Profile {toCopy.DirectoryShort} copied to {name.ScsUsernameToDirectory()}.";
        }

        private async void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not PlayerProfile toRename)
            {
                return;
            }
            if (await RejectCloudAsync(toRename, "Rename"))
            {
                return;
            }
            if (GameRunning())
            {
                await Dialogs.MessageAsync(this, "Game is running", "You should end the game before renaming a profile.");
                statusBarText.Text = "Rename canceled, game is running.";
                return;
            }
            string? name = await Dialogs.InputAsync(this, "Rename profile", $"New name for '{toRename.Username}':");
            if (string.IsNullOrWhiteSpace(name) || name == toRename.Username)
            {
                statusBarText.Text = "Renaming canceled.";
                return;
            }
            if (!StringExtensions.IsEncodableUsername(name, out string reason))
            {
                await Dialogs.MessageAsync(this, "Invalid name", reason);
                statusBarText.Text = "Rename canceled, invalid user name.";
                return;
            }
            if (_profiles.Any(p => p.Username.Equals(name, StringComparison.OrdinalIgnoreCase) && p.EtsAts == toRename.EtsAts))
            {
                await Dialogs.MessageAsync(this, "Not unique", $"The new user name must be unique. {name} is already used.");
                statusBarText.Text = "Rename canceled, non unique user name.";
                return;
            }
            string? backup = SafetyBackup.CreateBackup(toRename, "rename");
            if (backup == null)
            {
                await Dialogs.MessageAsync(this, "Backup failed", "Safety backup failed. Rename aborted — nothing was changed.");
                statusBarText.Text = "Rename aborted (no backup possible).";
                return;
            }
            if (!PlayerProfile.RenameProfile(toRename, name))
            {
                await Dialogs.MessageAsync(this, "Rename failed", "The destination may already exist or files are locked.");
                statusBarText.Text = "Rename failed.";
                return;
            }
            LoadProfiles();
            statusBarText.Text = $"Profile renamed to {name}. Backup: {backup}.";
        }

        private async void EditSave_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not PlayerProfile profile)
            {
                return;
            }
            if (profile.IsCloud)
            {
                await Dialogs.MessageAsync(this, "Cloud profile — saves live on Steam",
                    "Money/XP editing needs local save files, but Steam Cloud profiles keep saves on Valve's servers " +
                    "— this folder has no save/ directory, so there is nothing to edit.\n\nWorkaround:\n" +
                    "1. In-game profile manager → Edit profile → uncheck Steam Cloud.\n" +
                    "2. The profile appears here as a local profile — edit it freely.\n" +
                    "3. Re-enable Cloud afterwards if you want; Steam will upload the edited saves on next launch.");
                statusBarText.Text = "Save editing needs a local profile (see steps).";
                return;
            }
            // Browsing saves decrypts game.sii/info.sii in place, so the
            // backup must happen before the editor even opens.
            string? backup = SafetyBackup.CreateBackup(profile, "savebrowse");
            if (backup == null)
            {
                await Dialogs.MessageAsync(this, "Backup failed", "Safety backup failed. Editor not opened — nothing was changed.");
                statusBarText.Text = "Edit save aborted (no backup possible).";
                return;
            }
            var editor = new SaveEditorWindow(profile);
            editor.Activate();
        }

        private void Sync_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not PlayerProfile profile)
            {
                return;
            }
            var window = new SyncSettingsWindow(_profiles, profile);
            window.Activate();
        }

        private async void Backup_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not PlayerProfile profile)
            {
                return;
            }
            if (GameRunning())
            {
                await Dialogs.MessageAsync(this, "Game is running", "You should end the game before backing up a profile.");
                statusBarText.Text = "Backup canceled, game is running.";
                return;
            }
            string? path = await Dialogs.PickSaveZipAsync(this,
                $"{profile.DirectoryShort}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip");
            if (string.IsNullOrEmpty(path))
            {
                statusBarText.Text = "Backup canceled.";
                return;
            }
            try
            {
                PlayerProfile.BackupProfile(profile, path);
                statusBarText.Text = $"Profile {profile.DirectoryShort} saved to {path}.";
            }
            catch (Exception ex)
            {
                statusBarText.Text = $"Backup failed: {ex.Message}";
            }
        }

        private async void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not PlayerProfile profile)
            {
                return;
            }
            if (await RejectCloudAsync(profile, "Restore"))
            {
                return;
            }
            if (GameRunning())
            {
                await Dialogs.MessageAsync(this, "Game is running", "You should end the game before restoring a backup.");
                statusBarText.Text = "Restore canceled, game is running.";
                return;
            }
            string? zip = await Dialogs.PickBackupZipAsync(this);
            if (string.IsNullOrEmpty(zip))
            {
                statusBarText.Text = "Restore canceled.";
                return;
            }
            if (!await Dialogs.ConfirmAsync(this, "Confirm restore",
                $"Overwrite profile '{profile.Username}' [{profile.DirectoryShort}] with\n{zip}?\n\nCurrent state is backed up first."))
            {
                statusBarText.Text = "Restore canceled.";
                return;
            }
            var (ok, message) = SafetyBackup.RestoreBackup(profile, zip);
            if (!ok)
            {
                await Dialogs.MessageAsync(this, "Restore failed", message);
            }
            LoadProfiles();
            statusBarText.Text = message;
        }

        private async void Decrypt_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not PlayerProfile profile)
            {
                return;
            }
            if (await RejectCloudAsync(profile, "Decrypt"))
            {
                return;
            }
            if (GameRunning())
            {
                await Dialogs.MessageAsync(this, "Game is running", "You should end the game before decrypting.");
                statusBarText.Text = "Decrypt canceled, game is running.";
                return;
            }
            string? backup = SafetyBackup.CreateBackup(profile, "decrypt");
            if (backup == null)
            {
                await Dialogs.MessageAsync(this, "Backup failed", "Safety backup failed. Decrypt aborted — nothing was changed.");
                statusBarText.Text = "Decrypt aborted (no backup possible).";
                return;
            }
            bool ok = await Task.Run(() => PlayerProfile.DecryptFile(profile.Directory, "profile.sii"));
            LoadProfiles();
            statusBarText.Text = ok
                ? $"Profile {profile.DirectoryShort} decrypted. Backup: {backup}."
                : $"Decrypt reported failure for {profile.DirectoryShort}. Backup: {backup}.";
        }

        private void Explorer_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not PlayerProfile profile)
            {
                return;
            }
            Dialogs.OpenInExplorer(profile.Directory);
            statusBarText.Text = $"Opened Explorer in {profile.DirectoryShort}.";
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not PlayerProfile toDelete)
            {
                return;
            }
            if (await RejectCloudAsync(toDelete, "Delete"))
            {
                return;
            }
            if (GameRunning())
            {
                await Dialogs.MessageAsync(this, "Game is running", "You should end the game before deleting a profile.");
                statusBarText.Text = "Delete canceled, game is running.";
                return;
            }
            if (!await Dialogs.ConfirmAsync(this, "Are you sure?",
                $"Delete the profile of user {toDelete.Username}? A safety backup is kept first."))
            {
                statusBarText.Text = "Deleting a profile canceled.";
                return;
            }
            string? backup = SafetyBackup.CreateBackup(toDelete, "delete");
            if (backup == null &&
                !await Dialogs.ConfirmAsync(this, "Backup failed", "Safety backup failed. Delete anyway without a way back?"))
            {
                statusBarText.Text = "Delete canceled (no backup possible).";
                return;
            }
            if (!PlayerProfile.DeleteProfile(toDelete))
            {
                await Dialogs.MessageAsync(this, "Delete failed",
                    "Deleting the profile failed. Maybe files are open in another program.");
                return;
            }
            LoadProfiles();
            statusBarText.Text = backup != null
                ? $"Profile {toDelete.DirectoryShort} deleted. Backup: {backup}."
                : $"Profile {toDelete.DirectoryShort} deleted (no backup).";
        }
    }
}
