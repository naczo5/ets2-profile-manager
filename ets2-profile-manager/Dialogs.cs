using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Ets2ProfileManager
{
    /// <summary>WinUI replacements for the MahApps dialog helpers.</summary>
    internal static class Dialogs
    {
        private static XamlRoot Root(Window owner)
        {
            if (owner.Content is FrameworkElement root && root.XamlRoot != null)
            {
                return root.XamlRoot;
            }
            throw new InvalidOperationException("Window is not active yet.");
        }

        public static async Task MessageAsync(Window owner, string title, string message)
        {
            var dlg = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = Root(owner)
            };
            await dlg.ShowAsync();
        }

        public static async Task<bool> ConfirmAsync(Window owner, string title, string message)
        {
            var dlg = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = "Yes",
                CloseButtonText = "No",
                XamlRoot = Root(owner)
            };
            return await dlg.ShowAsync() == ContentDialogResult.Primary;
        }

        public static async Task<string?> InputAsync(Window owner, string title, string label)
        {
            var box = new TextBox { PlaceholderText = label, MinWidth = 300 };
            var dlg = new ContentDialog
            {
                Title = title,
                Content = box,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                XamlRoot = Root(owner)
            };
            return await dlg.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
        }

        private static nint Handle(Window owner) => WindowNative.GetWindowHandle(owner);

        public static async Task<string?> PickSaveZipAsync(Window owner, string fileName)
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = fileName,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeChoices.Add("Zip files", new List<string> { ".zip" });
            InitializeWithWindow.Initialize(picker, Handle(owner));
            try
            {
                var file = await picker.PickSaveFileAsync();
                return file?.Path;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<string?> PickBackupZipAsync(Window owner)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".zip");
            InitializeWithWindow.Initialize(picker, Handle(owner));
            try
            {
                var file = await picker.PickSingleFileAsync();
                return file?.Path;
            }
            catch
            {
                return null;
            }
        }

        public static void OpenInExplorer(string directory)
        {
            System.Diagnostics.Process.Start("explorer.exe", $"\"{directory}\"");
        }
    }
}
