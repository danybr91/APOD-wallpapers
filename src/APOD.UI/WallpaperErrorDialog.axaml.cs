using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace APOD.UI
{
    public partial class WallpaperErrorDialog : Window
    {
        private readonly string _imagePath;

        // Constructor sin parámetros requerido por el diseñador de Avalonia.
        public WallpaperErrorDialog() : this("", "")
        {
        }

        public WallpaperErrorDialog(string imagePath, string reason)
        {
            _imagePath = imagePath;
            AvaloniaXamlLoader.Load(this);
            this.FindControl<TextBlock>("ReasonText").Text = reason;
            this.FindControl<SelectableTextBlock>("PathText").Text = imagePath;
        }

        private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folder = Path.GetDirectoryName(_imagePath);
                if (string.IsNullOrEmpty(folder) || !await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder)))
                {
                    this.FindControl<TextBlock>("ReasonText").Text = "No se pudo abrir la carpeta.";
                }
            }
            catch (Exception ex)
            {
                this.FindControl<TextBlock>("ReasonText").Text = "No se pudo abrir la carpeta: " + ex.Message;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
