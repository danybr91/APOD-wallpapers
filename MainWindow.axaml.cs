using System;
using System.IO;
using System.Net.Http;
using System.Threading;
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
        private DateTime _currentDate = DateTime.Today;
        private bool _isSettingDate;
        private CancellationTokenSource _loadCts;
        
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
            DatePicker = this.FindControl<DatePicker>("DatePicker");
            PrevDayButton = this.FindControl<Button>("PrevDayButton");
            NextDayButton = this.FindControl<Button>("NextDayButton");

            DatePicker.MinYear = new DateTimeOffset(Program.APOD_MIN_DATE);
            DatePicker.MaxYear = new DateTimeOffset(DateTime.Today);
            _isSettingDate = true;
            DatePicker.SelectedDate = new DateTimeOffset(DateTime.Today);
            _isSettingDate = false;
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
        
        private void DownloadTodayImage()
        {
            LoadDate(DateTime.Today);
        }

        private void UpdateControls(bool loading)
        {
            LoadingOverlay.IsVisible = loading;
            DownloadButton.IsEnabled = !loading;
            SetWallpaperButton.IsEnabled = !loading;
            DatePicker.IsEnabled = !loading;
            PrevDayButton.IsEnabled = !loading && _currentDate > Program.APOD_MIN_DATE;
            NextDayButton.IsEnabled = !loading && _currentDate < DateTime.Today;
        }

        private async void LoadDate(DateTime date)
        {
            if (date < Program.APOD_MIN_DATE || date > DateTime.Today)
            {
                WriteError("Fecha fuera del rango de APOD (del 16/06/1995 a hoy).");
                return;
            }

            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            CancellationToken token = _loadCts.Token;

            _currentDate = date;
            _isSettingDate = true;
            DatePicker.SelectedDate = new DateTimeOffset(date);
            _isSettingDate = false;

            UpdateControls(true);
            WriteInfo($"Descargando la imagen del {date:dd/MM/yyyy}...");
            try
            {
                using (var client = new HttpClient())
                {
                    string doc_url = Program.GetAPODPageURL(date);
                    if (Program.IsValidURL(doc_url))
                    {
                        HtmlDocument page = await Program.GetHTMLDocument(client, doc_url, token);
                        token.ThrowIfCancellationRequested();

                        string image_url = Program.APOD_URL_BASE + Program.GetImageURLFromAPOD(page);
                        string preview_url = Program.APOD_URL_BASE + Program.GetImagePreviewURLFromAPOD(page);
                        if (Program.IsValidURL(image_url) && Program.IsValidURL(preview_url))
                        {
                            TitleText.Text = "";
                            InfoText.Text = "";
                            SetDescriptionFromHtml(TitleText, Program.GetImageTitleFromAPOD(page));
                            SetDescriptionFromHtml(InfoText, Program.GetImageDescriptionFromAPOD(page));

                            image = await Program.DownloadImageParallel(client, preview_url, token);
                            token.ThrowIfCancellationRequested();
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
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                WriteError(e.Message);
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    UpdateControls(false);
                }
            }
        }

        private void DatePicker_SelectedDateChanged(object sender, DatePickerSelectedValueChangedEventArgs e)
        {
            if (_isSettingDate) return;
            if (e.NewDate.HasValue)
            {
                LoadDate(e.NewDate.Value.Date);
            }
        }

        private void PrevDayButton_Click(object sender, RoutedEventArgs e)
        {
            LoadDate(_currentDate.AddDays(-1));
        }

        private void NextDayButton_Click(object sender, RoutedEventArgs e)
        {
            LoadDate(_currentDate.AddDays(1));
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

                UpdateControls(true);
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
                    WriteError("Error al guardar la imagen: " + e.Message);
                }
                finally
                {
                    UpdateControls(false);
                }

                return true;
            }
            return false;
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveImageAsync();
        }

        private async void SetWallpaperButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(file_name))
            {
                await SaveImageAsync();
                if (!File.Exists(file_name))
                {
                    WriteError("La imagen no está guardada en el disco.");
                    return;
                }
            }
            Program.SetWallpaper(file_name);
            WriteInfo($"Fondo de pantalla establecido");
        }
    }
}