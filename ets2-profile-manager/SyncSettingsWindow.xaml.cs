using System.Diagnostics;
using System.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Ets2ProfileManager
{
    public sealed partial class SyncSettingsWindow : Window
    {
        private readonly List<PlayerProfile> _profiles;
        private readonly List<CheckBox> _targetBoxes = new();
        private readonly List<CheckBox> _groupBoxes = new();

        internal SyncSettingsWindow(List<PlayerProfile> profiles, PlayerProfile? preselect)
        {
            _profiles = profiles;
            InitializeComponent();
            AppWindow.Resize(new SizeInt32(760, 920));
            cmbSource.ItemsSource = profiles;
            cmbSource.SelectedItem = preselect != null && profiles.Contains(preselect)
                ? preselect
                : profiles.FirstOrDefault();
            foreach (SettingGroup g in SettingsSync.Groups)
            {
                var cb = new CheckBox
                {
                    Tag = g.Id,
                    Content = $"{g.Title}  [{g.File}] — {g.Description}" +
                              (g.Warning != null ? $"  ⚠ {g.Warning}" : string.Empty),
                    IsChecked = g.Id is "ffb" or "steer" or "deadzone" or "shifting" or "pedals" or "steercam"
                };
                _groupBoxes.Add(cb);
                pnlGroups.Children.Add(cb);
            }
        }

        private PlayerProfile? Source => cmbSource.SelectedItem as PlayerProfile;

        private void Source_Changed(object sender, SelectionChangedEventArgs e)
        {
            pnlTargets.Children.Clear();
            _targetBoxes.Clear();
            if (Source == null)
            {
                return;
            }
            lblSourceInfo.Text = $"{Source.EtsAts} — Folder ID: {Source.DirectoryShort}" + (Source.IsCloud ? " — Cloud profile" : string.Empty);
            ControlsDoc srcCtl = SettingsSync.LoadControls(Source.Directory);
            CfgDoc srcLocal = SettingsSync.LoadCfg(Source.Directory, SettingsSync.LocalCfgFile);
            CfgDoc srcCfg = SettingsSync.LoadCfg(Source.Directory, SettingsSync.ConfigFile);
            lblSourceFiles.Text = "Source files: " +
                $"controls.sii {(srcCtl.Valid ? $"✓ ({srcCtl.Entries.Count} entries)" : "✗ MISSING — wheel/binding groups will skip")}, " +
                $"config_local.cfg {(srcLocal.Loaded ? $"✓ ({srcLocal.ByKey.Count} keys)" : "✗ MISSING")}, " +
                $"config.cfg {(srcCfg.Loaded ? $"✓ ({srcCfg.ByKey.Count} keys)" : "✗ MISSING")}";
            foreach (PlayerProfile p in _profiles.Where(p =>
                p.EtsAts == Source.EtsAts &&
                !string.Equals(p.Directory, Source.Directory, StringComparison.OrdinalIgnoreCase)))
            {
                var cb = new CheckBox { Tag = p, Content = $"{p.Username}  [{p.DirectoryShort}]" + (p.IsCloud ? "  (Cloud — Steam may overwrite on next sync)" : string.Empty) };
                _targetBoxes.Add(cb);
                pnlTargets.Children.Add(cb);
            }
            if (_targetBoxes.Count == 0)
            {
                pnlTargets.Children.Add(new TextBlock { Text = "No other profiles for this game.", Opacity = 0.7 });
            }
            RefreshPreview();
        }

        private List<PlayerProfile> SelectedTargets() =>
            _targetBoxes.Where(c => c.IsChecked == true).Select(c => (PlayerProfile)c.Tag!).ToList();

        private List<string> SelectedGroups() =>
            _groupBoxes.Where(c => c.IsChecked == true).Select(c => (string)c.Tag!).ToList();

        private void Preview_Click(object sender, RoutedEventArgs e) => RefreshPreview();

        private void RefreshPreview()
        {
            var sb = new StringBuilder();
            if (Source == null)
            {
                txtPreview.Text = string.Empty;
                return;
            }
            var targets = SelectedTargets();
            var groups = SelectedGroups();
            if (targets.Count == 0 || groups.Count == 0)
            {
                txtPreview.Text = "Select at least one target profile and one setting group.";
                return;
            }
            ControlsDoc srcCtl = SettingsSync.LoadControls(Source.Directory);
            CfgDoc srcLocal = SettingsSync.LoadCfg(Source.Directory, SettingsSync.LocalCfgFile);
            CfgDoc srcCfg = SettingsSync.LoadCfg(Source.Directory, SettingsSync.ConfigFile);
            if (!srcCtl.Valid)
            {
                sb.AppendLine($"WARNING: source has no valid {SettingsSync.ControlsFile}; those groups will be skipped.");
            }
            int total = 0;
            const int cap = 300;
            foreach (PlayerProfile t in targets)
            {
                ControlsDoc tgtCtl = SettingsSync.LoadControls(t.Directory);
                CfgDoc tgtLocal = SettingsSync.LoadCfg(t.Directory, SettingsSync.LocalCfgFile);
                CfgDoc tgtCfg = SettingsSync.LoadCfg(t.Directory, SettingsSync.ConfigFile);
                var diffs = SettingsSync.ComputeDiff(srcCtl, srcLocal, srcCfg, tgtCtl, tgtLocal, tgtCfg, groups);
                sb.AppendLine($"=== {t.Username} [{t.DirectoryShort}]{(t.IsCloud ? " (Cloud)" : string.Empty)} — {diffs.Count} change(s) ===");
                if (!tgtCtl.Valid && groups.Any(id => SettingsSync.Groups.Any(g => g.Id == id && g.File == SettingsSync.ControlsFile)))
                {
                    sb.AppendLine($"NOTE: target has no valid {SettingsSync.ControlsFile} — wheel/binding groups will skip.");
                }
                if (!tgtLocal.Loaded && groups.Any(id => SettingsSync.Groups.Any(g => g.Id == id && g.File == SettingsSync.LocalCfgFile)))
                {
                    sb.AppendLine($"NOTE: target has no {SettingsSync.LocalCfgFile} — shifting/pedals/wheel-range groups will skip.");
                }
                if (!tgtCfg.Loaded && groups.Any(id => SettingsSync.Groups.Any(g => g.Id == id && g.File == SettingsSync.ConfigFile)))
                {
                    sb.AppendLine($"NOTE: target has no {SettingsSync.ConfigFile} — aids/gameplay/units/camera/HUD groups will skip.");
                }
                foreach (SettingDiff d in diffs)
                {
                    if (total >= cap)
                    {
                        break;
                    }
                    sb.AppendLine($"[{d.File}] {d.Group}: {d.Key}: {Trunc(d.OldValue)} → {Trunc(d.NewValue)}");
                    total++;
                }
                if (diffs.Count == 0)
                {
                    sb.AppendLine("(already in sync)");
                }
            }
            if (total >= cap)
            {
                sb.AppendLine("… preview truncated, Apply will still process everything.");
            }
            txtPreview.Text = sb.ToString();
            lblStatus.Text = $"{targets.Count} target(s), {groups.Count} group(s) selected.";
        }

        private static string Trunc(string s) => s.Length > 60 ? s[..60] + "…" : s;

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (Source == null)
            {
                return;
            }
            var targets = SelectedTargets();
            var groups = SelectedGroups();
            if (targets.Count == 0 || groups.Count == 0)
            {
                await Dialogs.MessageAsync(this, "Nothing selected", "Select at least one target profile and one setting group.");
                return;
            }
            if (GameRunning())
            {
                await Dialogs.MessageAsync(this, "Game is running", "Exit the game before syncing settings.");
                lblStatus.Text = "Sync canceled, game is running.";
                return;
            }
            string groupNames = string.Join(", ", SettingsSync.Groups.Where(g => groups.Contains(g.Id)).Select(g => g.Title));
            string targetNames = string.Join("\n", targets.Select(t => $"• {t.Username} [{t.DirectoryShort}] ({t.EtsAts})"));
            string cloudNote = targets.Any(t => t.IsCloud)
                ? "\n\nWARNING: Cloud targets can be overwritten by Steam on next launch. Exit the game, sync, then launch the game once and check the settings stuck."
                : string.Empty;
            if (!await Dialogs.ConfirmAsync(this, "Confirm sync",
                $"Copy from '{Source.Username}' [{Source.DirectoryShort}] to:\n{targetNames}\n\nGroups: {groupNames}\n\nEach target is backed up first.{cloudNote} Continue?"))
            {
                lblStatus.Text = "Sync canceled.";
                return;
            }
            var sb = new StringBuilder();
            int okCount = 0;
            foreach (PlayerProfile t in targets)
            {
                string? backup = SafetyBackup.CreateBackup(t, "sync");
                if (backup == null)
                {
                    sb.AppendLine($"{t.Username}: BACKUP FAILED — skipped (refusing to write without backup).");
                    continue;
                }
                ApplyResult r = SettingsSync.Apply(Source.Directory, t.Directory, groups, Source.EtsAts, t.EtsAts);
                if (r.Ok)
                {
                    okCount++;
                    sb.AppendLine($"{t.Username}: {r.Applied} value(s) synced. Backup: {backup}");
                }
                else
                {
                    sb.AppendLine($"{t.Username}: ERRORS: {string.Join("; ", r.Errors)} Applied before error: {r.Applied}. Backup: {backup}");
                }
                foreach (string s in r.Skipped)
                {
                    sb.AppendLine($"  skipped: {s}");
                }
            }
            lblStatus.Text = sb.ToString();
            txtPreview.Text = sb.ToString();
            await Dialogs.MessageAsync(this, okCount == targets.Count ? "Sync complete" : "Sync finished with issues", sb.ToString());
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private static bool GameRunning() => GameGuard.IsRunning();
    }
}
