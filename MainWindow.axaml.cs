using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using HtmlAgilityPack;

namespace APOD_wallpapers
{
    public partial class MainWindow : Window
    {
        private string file_name;
        private Bitmap image;
        private string imageName;

        private bool saved;
        
        public MainWindow()
        {
            InitializeComponent();
            // Descarga la imagen inicial al abrir la ventana
            DownloadTodayImage();
        }
        
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            // Añade esta línea para inicializar el control StatusText
            StatusText = this.FindControl<TextBlock>("StatusText");
            PreviewImage = this.FindControl<Image>("PreviewImage");
            LoadingOverlay = this.FindControl<Border>("LoadingOverlay");
            TitleText = this.FindControl<TextBlock>("TitleText");
            InfoText = this.FindControl<TextBlock>("InfoText");
            DownloadButton = this.FindControl<Button>("DownloadButton");
            SetWallpaperButton = this.FindControl<Button>("SetWallpaperButton");
        }
        
        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }

        private void WriteInfo(string info)
        {
            SetStatus(info);
        }

        private void WriteError(string error)
        {
            SetStatus($"ERROR: {error}");
        }

        private StringBuilder GetContentFromHtml(HtmlNode htmlNode)
        {
            String separator = " ";
            var accumulatedText = new StringBuilder();

            foreach (var node in htmlNode.ChildNodes)
            {
                if (node.Name == "#text")
                {
                    accumulatedText.Append(node.InnerText.Replace("\n", separator));
                }
                else if (node.Name == "b")
                {
                    accumulatedText.Append(node.InnerText).Replace("\n", separator);
                }
                else if (node.Name == "i")
                {
                    accumulatedText.Append(node.InnerText).Replace("\n", separator);
                }
                else if (node.Name == "a")
                {
                    //string href = node.GetAttributeValue("href", "#");
                    accumulatedText.Append($"{node.InnerText.Replace("\n", separator)}");
                }
            }

            return accumulatedText;
        }
        
        private async void DownloadTodayImage()
        {
            LoadingOverlay.IsVisible = true;
            DownloadButton.IsEnabled = false;
            SetWallpaperButton.IsEnabled = false;
            WriteInfo("Descargando la imagen del día...");
            try
            {
                using (var client = new HttpClient())
                {
                    string doc_url = Program.APOD_URL_BASE + Program.APOD_MAIN_PAGE;
                    if (Program.IsValidURL(doc_url))
                    {
                        HtmlDocument page = await Program.GetHTMLDocument(client, doc_url);
                        string image_url = Program.APOD_URL_BASE + Program.GetImageURLFromAPOD(page);
                        if (Program.IsValidURL(image_url))
                        {
                            // Coger la descripcion
                            TitleText.Text = "";
                            InfoText.Text = "";
                            TitleText.Text = GetContentFromHtml(Program.GetImageTitleFromAPOD(page)).ToString();
                            InfoText.Text = GetContentFromHtml(Program.GetImageDescriptionFromAPOD(page)).ToString();
                            
                            image = await Program.DownloadImage(client, image_url);
                            file_name = Program.GetImagefileNameFromURL(image_url);
                            if (image != null)
                            {
                                PreviewImage.Source = image;
                                WriteInfo("Listo");
                            }
                        }
                    }
                }
                saved = false;
            }
            catch (Exception e)
            {
                WriteError(e.Message);
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
                DownloadButton.IsEnabled = true;
                SetWallpaperButton.IsEnabled = true;
            }
        }
        
        private async Task<bool> SaveImageAsync()
        {
            if (image == null) return false;

            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Save Image",
                InitialFileName =  file_name,
                Directory = Program.DEFAULT_DOWNLOAD_DIR,
                Filters = { new FileDialogFilter { Name = "Image Files", Extensions = { "jpg", "jpeg", "png" } } }
            };

            file_name = await dialog.ShowAsync(this);
            if (file_name != null)
            {
                image.Save(file_name);
                saved = true;
                WriteInfo($"Imagen guardada en {file_name}");
                return true;
            }
            saved = false;
            return false;
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveImageAsync();
        }

        private async void SetWallpaperButton_Click(object sender, RoutedEventArgs e)
        {
            if (!saved)
            {
                if (!await SaveImageAsync()) return;
            }
            Program.SetWallpaper(file_name);
            WriteInfo($"Fondo de pantalla establecido");
        }
    }
}