using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Ets2ProfileManager
{
    public sealed partial class SaveEditorWindow : Window
    {
        private readonly PlayerProfile _profile;
        private SaveGame? _selected;
        private SaveGameData? _data;

        internal SaveEditorWindow(PlayerProfile profile)
        {
            _profile = profile;
            InitializeComponent();
            AppWindow.Resize(new SizeInt32(460, 760));
            lblProfile.Text = profile.Username;
            lblDetails.Text = $"{profile.EtsAts} — Folder ID: {profile.DirectoryShort}";
            var saves = SaveGame.GetSaveGames(profile.Directory);
            cmbSaves.ItemsSource = saves;
            if (saves.Count > 0)
            {
                cmbSaves.SelectedIndex = 0;
            }
            else
            {
                lblStatus.Text = $"No saves found for '{profile.Username}'. Load the game once with g_save_format \"2\" in config.cfg.";
            }
        }

        private void Saves_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (cmbSaves.SelectedItem is not SaveGame save)
            {
                _selected = null;
                _data = null;
                return;
            }
            _selected = save;
            try
            {
                _data = SaveGameData.Load(save.Path);
                txtMoney.Text = _data.Money.ToString();
                txtXP.Text = _data.XP.ToString();
                chkAdr1.IsChecked = (_data.Adr & 1) != 0;
                chkAdr2.IsChecked = (_data.Adr & 2) != 0;
                chkAdr3.IsChecked = (_data.Adr & 4) != 0;
                chkAdr4.IsChecked = (_data.Adr & 8) != 0;
                chkAdr5.IsChecked = (_data.Adr & 16) != 0;
                chkAdr6.IsChecked = (_data.Adr & 32) != 0;
                sldLongDist.Value = _data.LongDist;
                sldHeavyCargo.Value = _data.HeavyCargo;
                sldFragileCargo.Value = _data.FragileCargo;
                sldUrgentCargo.Value = _data.UrgentCargo;
                sldEcoDriving.Value = _data.EcoDriving;
                lblStatus.Text = $"Loaded '{save.DisplayName}'.";
            }
            catch (Exception ex)
            {
                _data = null;
                lblStatus.Text = $"Failed to load save data: {ex.Message}";
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || _data == null)
            {
                return;
            }
            if (GameRunning())
            {
                await Dialogs.MessageAsync(this, "Game is running", "Exit the game before modifying save statistics.");
                return;
            }
            if (!long.TryParse(txtMoney.Text, out long money))
            {
                await Dialogs.MessageAsync(this, "Invalid input", "Enter a valid numeric value for Money.");
                return;
            }
            if (!int.TryParse(txtXP.Text, out int xp) || xp < 0)
            {
                await Dialogs.MessageAsync(this, "Invalid input", "Enter a valid non-negative value for XP.");
                return;
            }
            try
            {
                string? backup = SafetyBackup.CreateBackup(_profile, "saveedit");
                if (backup == null)
                {
                    await Dialogs.MessageAsync(this, "Backup failed", "Safety backup failed. Save aborted — nothing was changed.");
                    lblStatus.Text = "Save aborted (no backup possible).";
                    return;
                }
                _data.Money = money;
                _data.XP = xp;
                int adr = 0;
                if (chkAdr1.IsChecked == true) adr |= 1;
                if (chkAdr2.IsChecked == true) adr |= 2;
                if (chkAdr3.IsChecked == true) adr |= 4;
                if (chkAdr4.IsChecked == true) adr |= 8;
                if (chkAdr5.IsChecked == true) adr |= 16;
                if (chkAdr6.IsChecked == true) adr |= 32;
                _data.Adr = adr;
                _data.LongDist = (int)sldLongDist.Value;
                _data.HeavyCargo = (int)sldHeavyCargo.Value;
                _data.FragileCargo = (int)sldFragileCargo.Value;
                _data.UrgentCargo = (int)sldUrgentCargo.Value;
                _data.EcoDriving = (int)sldEcoDriving.Value;
                SaveGameData.Save(_selected.Path, _data);
                lblStatus.Text = $"Saved '{_selected.DisplayName}'. Backup: {backup}.";
                await Dialogs.MessageAsync(this, "Success", $"Stats updated for '{_selected.DisplayName}'.");
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Failed to save: {ex.Message}";
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private static bool GameRunning() => GameGuard.IsRunning();
    }
}
