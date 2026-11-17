using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using APOD.Core;

namespace APOD.UI
{
    public partial class SettingsDialog : Window
    {
        private readonly AppSettings _settings;

        // Constructor sin parámetros requerido por el diseñador de Avalonia.
        public SettingsDialog() : this(new AppSettings())
        {
        }

        public SettingsDialog(AppSettings settings)
        {
            _settings = settings;
            AvaloniaXamlLoader.Load(this);
            DownloadDirBox = this.FindControl<TextBox>("DownloadDirBox");
            ErrorText = this.FindControl<TextBlock>("ErrorText");
            DownloadDirBox.Text = settings.DownloadDir;

            // Los ajustes pueden venir de un JSON editado a mano: se avisa nada más abrir el panel.
            string error = settings.Validate();
            if (error != null)
            {
                ShowError(error);
            }

            // Se suscribe después de cargar el valor inicial para no ocultar el aviso de carga.
            DownloadDirBox.TextChanged += DownloadDirBox_TextChanged;
        }

        // Valida mientras se escribe: el aviso solo se ve mientras el valor del campo es erróneo.
        private void DownloadDirBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string error = AppSettings.ValidateDownloadDir(DownloadDirBox.Text?.Trim());
            if (error != null)
                ShowError(error);
            else
                HideError();
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            string current = string.IsNullOrWhiteSpace(DownloadDirBox.Text) ? ApodService.DefaultDownloadDir : DownloadDirBox.Text.Trim();
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Carpeta de descarga",
                AllowMultiple = false,
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(current)
            });

            if (folders.Count > 0)
            {
                string path = folders[0].TryGetLocalPath();
                if (path != null)
                {
                    DownloadDirBox.Text = path;
                }
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            DownloadDirBox.Text = "";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string dir = DownloadDirBox.Text?.Trim() ?? "";
            string error = AppSettings.ValidateDownloadDir(dir);
            if (error != null)
            {
                ShowError(error);
                return;
            }

            _settings.DownloadDir = dir;
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                ShowError("No se pudieron guardar los ajustes: " + ex.Message);
                return;
            }
            Close(true);
        }

        // Abre el JSON con la aplicación predeterminada del sistema. Se cierra el panel para que
        // un "Guardar" posterior no sobrescriba lo editado fuera.
        private async void OpenJsonButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(AppSettings.FilePath))
                {
                    _settings.Save();
                }
                if (!await Launcher.LaunchFileInfoAsync(new FileInfo(AppSettings.FilePath)))
                {
                    ShowError("No se pudo abrir " + AppSettings.FilePath);
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowError("No se pudo abrir el archivo de ajustes: " + ex.Message);
                return;
            }
            Close(false);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.IsVisible = true;
        }

        private void HideError()
        {
            ErrorText.IsVisible = false;
        }
    }
}
