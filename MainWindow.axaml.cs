using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HtmlAgilityPack;

namespace APOD_wallpapers
{
    public partial class MainWindow : Window
    {
        private string file_name;
        private Bitmap image;
        private string imageName;
        private string fullResUrl;
        
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

        private void SetDescriptionFromHtml(TextBlock textBlock, HtmlNode htmlNode)
        {
            var inlines = new InlineCollection();

            foreach (var node in htmlNode.ChildNodes)
            {
                if (node.Name == "#text")
                {
                    inlines.Add(new Run(node.InnerText.Replace("\n", " ")));
                }
                else if (node.Name == "b")
                {
                    inlines.Add(new Run { Text = node.InnerText.Replace("\n", " "), FontWeight = FontWeight.Bold });
                }
                else if (node.Name == "i")
                {
                    inlines.Add(new Run { Text = node.InnerText.Replace("\n", " "), FontStyle = FontStyle.Italic });
                }
                else if (node.Name == "a")
                {
                    Inline inline = CreateLinkInline(node);
                    if (inline != null) inlines.Add(inline);
                }
            }

            textBlock.Text = null;
            textBlock.Inlines = inlines;
        }

        private Inline CreateLinkInline(HtmlNode node)
        {
            string href = node.GetAttributeValue("href", "");
            string text = node.InnerText.Replace("\n", " ");
            if (string.IsNullOrEmpty(text)) return null;

            Uri uri = null;
            if (!Uri.TryCreate(href, UriKind.Absolute, out uri))
            {
                Uri.TryCreate(new Uri(Program.APOD_URL_BASE), href, out uri);
            }
            if (uri == null) return new Run(text);

            var link = new HyperlinkButton
            {
                Content = text,
                NavigateUri = uri,
                Classes = { "link" }
            };
            ToolTip.SetTip(link, uri);
            return new InlineUIContainer(link)
            {
                BaselineAlignment = BaselineAlignment.Bottom
            };
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
                        string preview_url = Program.APOD_URL_BASE + Program.GetImagePreviewURLFromAPOD(page);
                        if (Program.IsValidURL(image_url) && Program.IsValidURL(preview_url))
                        {
                            // Coger la descripcion
                            TitleText.Text = "";
                            InfoText.Text = "";
                            SetDescriptionFromHtml(TitleText, Program.GetImageTitleFromAPOD(page));
                            SetDescriptionFromHtml(InfoText, Program.GetImageDescriptionFromAPOD(page));
                            
                            image = await Program.DownloadImageParallel(client, preview_url);
                            fullResUrl = image_url;
                            file_name = Program.GetImagefileNameFromURL(image_url);
                            if (image != null)
                            {
                                PreviewImage.Source = image;
                                WriteInfo("Listo");
                            }
                        }
                    }
                }
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
                if (string.IsNullOrEmpty(fullResUrl))
                {
                    WriteError("No hay imagen de resolución completa para guardar.");
                    return false;
                }

                LoadingOverlay.IsVisible = true;
                DownloadButton.IsEnabled = false;
                SetWallpaperButton.IsEnabled = false;
                try
                {
                    WriteInfo("Descargando la imagen...");
                    using var client = new HttpClient();
                    byte[] bytes = await Program.DownloadImageParallelToBytes(client, fullResUrl);
                    File.WriteAllBytes(file_name, bytes);
                    WriteInfo($"Imagen guardada en {file_name}");
                }
                catch (Exception e)
                {
                    WriteError("Error al guardar la iamgen: " + e.Message);
                }
                finally
                {
                    LoadingOverlay.IsVisible = false;
                    DownloadButton.IsEnabled = true;
                    SetWallpaperButton.IsEnabled = true;
                }

                return true;
            }
            return false;
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveImageAsync();
        }

        private void SetWallpaperButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(file_name))
            {
                WriteError("La imagen no está guardada en el disco.");
                return;
            }
            Program.SetWallpaper(file_name);
            WriteInfo($"Fondo de pantalla establecido");
        }
    }
}