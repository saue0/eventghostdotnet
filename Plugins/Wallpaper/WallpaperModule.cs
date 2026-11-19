using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using EventGhostDotNet.PluginContracts;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSelectionMode = System.Windows.Controls.SelectionMode;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfWindow = System.Windows.Window;

namespace EventGhostDotNet.Plugins.Wallpaper;

public sealed class WallpaperModule : IPlugin
{
    private const string ApplyBingActionId = "WallpaperApplyBing";
    private const string ApplyLocalImagesActionId = "WallpaperApplyLocalImages";
    private const int MaximumImageCount = 8;
    private const long MaximumImageBytes = 50 * 1024 * 1024;
    private static readonly Uri BingHost = new("https://www.bing.com/");
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(90)
    };
    private readonly SemaphoreSlim _executionLock = new(1, 1);

    private sealed record ActionSettings(int StartIndex, int ImageCount)
    {
        public bool IncludeSystemInfo { get; init; } = true;
    }

    private sealed record LocalImagesSettings(string[] Paths)
    {
        public bool IncludeSystemInfo { get; init; } = true;
    }
    private sealed record BingImage(string Url);
    private sealed record InfoRow(string Label, string Value);

    public string Id => "Wallpaper";
    public string Name => "Wallpaper";
    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [
        new(ApplyBingActionId, "Fond d'écran — Bing du jour"),
        new(ApplyLocalImagesActionId, "Fond d'écran — images locales")
    ];

    public PluginActionData? ConfigureAction(
        string actionId, PluginActionData? existing, IPluginHost host)
    {
        if (actionId == ApplyLocalImagesActionId)
            return ConfigureLocalImagesAction(existing, host);
        if (actionId != ApplyBingActionId)
            throw new InvalidOperationException($"Action Wallpaper inconnue : {actionId}");

        ActionSettings settings;
        try
        {
            settings = existing is null
                ? new ActionSettings(0, 3)
                : ReadSettings(existing.Value);
        }
        catch (JsonException exception)
        {
            WpfMessageBox.Show(host.Owner, $"La configuration de l'action est invalide.\n{exception.Message}",
                Name, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return null;
        }

        var dialog = new WpfWindow
        {
            Title = existing is null ? "Configurer le fond d'écran Bing" : "Modifier le fond d'écran Bing",
            Owner = host.Owner,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            Padding = new System.Windows.Thickness(18)
        };
        var panel = new WpfStackPanel { Width = 480 };
        panel.Children.Add(new WpfTextBlock
        {
            Text = "Index de départ (0 = image du jour) :",
            Margin = new System.Windows.Thickness(0, 0, 0, 5)
        });
        var indexBox = new WpfTextBox
        {
            Text = settings.StartIndex.ToString(CultureInfo.InvariantCulture),
            Margin = new System.Windows.Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(indexBox);
        panel.Children.Add(new WpfTextBlock
        {
            Text = "Nombre d'images à télécharger (1 à 8) :",
            Margin = new System.Windows.Thickness(0, 0, 0, 5)
        });
        var countBox = new WpfTextBox
        {
            Text = settings.ImageCount.ToString(CultureInfo.InvariantCulture),
            Margin = new System.Windows.Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(countBox);
        var includeSystemInfo = new WpfCheckBox
        {
            Content = "Ajouter les informations système sur l'image",
            IsChecked = settings.IncludeSystemInfo,
            Margin = new System.Windows.Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(includeSystemInfo);
        panel.Children.Add(new WpfTextBlock
        {
            Text = @"Les images préparées seront enregistrées dans %TEMP%\EventGhostDotNet\Wallpaper.",
            Foreground = System.Windows.Media.Brushes.Gray,
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Margin = new System.Windows.Thickness(0, 0, 0, 12)
        });

        var buttons = new WpfStackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };
        buttons.Children.Add(new WpfButton { Content = "Annuler", IsCancel = true });
        var save = new WpfButton
        {
            Content = existing is null ? "Ajouter" : "Enregistrer",
            IsDefault = true
        };
        save.Click += (_, _) =>
        {
            if (!int.TryParse(indexBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var startIndex) ||
                !int.TryParse(countBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var imageCount) ||
                startIndex is < 0 or > 7 || imageCount is < 1 or > MaximumImageCount ||
                (long)startIndex + imageCount > MaximumImageCount)
            {
                WpfMessageBox.Show(dialog,
                    "L'index doit être compris entre 0 et 7 et le nombre d'images entre 1 et 8. " +
                    "La plage demandée ne peut pas dépasser l'index 7.",
                    "Valeurs invalides", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            settings = new ActionSettings(startIndex, imageCount)
            {
                IncludeSystemInfo = includeSystemInfo.IsChecked == true
            };
            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        return dialog.ShowDialog() == true
            ? new PluginActionData(actionId, JsonSerializer.Serialize(settings), string.Empty)
            : null;
    }

    private static PluginActionData? ConfigureLocalImagesAction(
        PluginActionData? existing, IPluginHost host)
    {
        LocalImagesSettings settings;
        try
        {
            settings = existing is null
                ? new LocalImagesSettings([])
                : ReadLocalSettings(existing.Value);
        }
        catch (JsonException exception)
        {
            WpfMessageBox.Show(host.Owner, $"La configuration de l'action est invalide.\n{exception.Message}",
                "Wallpaper", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return null;
        }

        var dialog = new WpfWindow
        {
            Title = existing is null ? "Configurer les images locales" : "Modifier les images locales",
            Owner = host.Owner,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            Width = 700,
            Height = 460,
            MinWidth = 560,
            MinHeight = 360,
            Padding = new System.Windows.Thickness(18)
        };
        var panel = new WpfStackPanel();
        panel.Children.Add(new WpfTextBlock
        {
            Text = "Choisissez une ou plusieurs images. Elles seront traitées et appliquées dans l'ordre de la liste.",
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Margin = new System.Windows.Thickness(0, 0, 0, 10)
        });

        var includeSystemInfo = new WpfCheckBox
        {
            Content = "Ajouter les informations système sur l'image",
            IsChecked = settings.IncludeSystemInfo,
            Margin = new System.Windows.Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(includeSystemInfo);

        var fileList = new WpfListBox
        {
            MinHeight = 240,
            SelectionMode = WpfSelectionMode.Extended,
            Margin = new System.Windows.Thickness(0, 0, 0, 10)
        };
        foreach (var path in settings.Paths)
            fileList.Items.Add(path);
        panel.Children.Add(fileList);

        var fileButtons = new WpfStackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Margin = new System.Windows.Thickness(0, 0, 0, 12)
        };
        var addFiles = new WpfButton { Content = "Ajouter des images...", Margin = new System.Windows.Thickness(0, 0, 8, 0) };
        addFiles.Click += (_, _) =>
        {
            var picker = new WpfOpenFileDialog
            {
                Title = "Sélectionner les images locales",
                Filter = "Fichiers image|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|Tous les fichiers|*.*",
                Multiselect = true,
                CheckFileExists = true
            };
            var initialDirectory = fileList.Items.Count > 0
                ? Path.GetDirectoryName((string)fileList.Items[0])
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (!string.IsNullOrWhiteSpace(initialDirectory))
                picker.InitialDirectory = initialDirectory;
            if (picker.ShowDialog(dialog) != true)
                return;

            foreach (var path in picker.FileNames)
            {
                if (!fileList.Items.Cast<string>().Contains(path, StringComparer.OrdinalIgnoreCase))
                    fileList.Items.Add(path);
            }
        };
        fileButtons.Children.Add(addFiles);
        var removeFiles = new WpfButton { Content = "Retirer la sélection" };
        removeFiles.Click += (_, _) =>
        {
            foreach (var item in fileList.SelectedItems.Cast<string>().ToArray())
                fileList.Items.Remove(item);
        };
        fileButtons.Children.Add(removeFiles);
        panel.Children.Add(fileButtons);

        var buttons = new WpfStackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };
        buttons.Children.Add(new WpfButton { Content = "Annuler", IsCancel = true });
        var save = new WpfButton
        {
            Content = existing is null ? "Ajouter" : "Enregistrer",
            IsDefault = true
        };
        save.Click += (_, _) =>
        {
            if (fileList.Items.Count == 0)
            {
                WpfMessageBox.Show(dialog, "Sélectionnez au moins une image.",
                    "Images requises", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        if (dialog.ShowDialog() != true)
            return null;
        var selectedPaths = fileList.Items.Cast<string>().ToArray();
        return new PluginActionData(
            ApplyLocalImagesActionId,
            JsonSerializer.Serialize(new LocalImagesSettings(selectedPaths)
            {
                IncludeSystemInfo = includeSystemInfo.IsChecked == true
            }),
            string.Empty);
    }

    public string DescribeAction(PluginActionData action)
    {
        if (action.ActionId == ApplyLocalImagesActionId)
        {
            try
            {
                var settings = ReadLocalSettings(action.Value);
                var overlay = settings.IncludeSystemInfo ? "avec informations système" : "sans ajout d'informations";
                return $"Images locales — {settings.Paths.Length} fichier(s), {overlay}";
            }
            catch (JsonException)
            {
                return "Images locales — configuration invalide";
            }
        }
        if (action.ActionId != ApplyBingActionId)
            throw new InvalidOperationException($"Action Wallpaper inconnue : {action.ActionId}");
        try
        {
            var settings = ReadSettings(action.Value);
            var overlay = settings.IncludeSystemInfo ? "avec informations système" : "sans ajout d'informations";
            return $"Bing — télécharger {settings.ImageCount} image(s) à partir de l'index {settings.StartIndex}, {overlay}";
        }
        catch (JsonException)
        {
            return "Bing — configuration invalide";
        }
    }

    public async Task ExecuteActionAsync(
        PluginActionData action, IPluginHost host, CancellationToken cancellationToken)
    {
        await _executionLock.WaitAsync(cancellationToken);
        try
        {
            if (action.ActionId == ApplyLocalImagesActionId)
            {
                await ExecuteLocalImagesAsync(action, host, cancellationToken);
                return;
            }
            if (action.ActionId != ApplyBingActionId)
                throw new InvalidOperationException($"Action Wallpaper inconnue : {action.ActionId}");

            await ExecuteBingImagesAsync(action, host, cancellationToken);
        }
        finally
        {
            _executionLock.Release();
        }
    }

    private static async Task ExecuteBingImagesAsync(
        PluginActionData action, IPluginHost host, CancellationToken cancellationToken)
    {
        var settings = ReadSettings(action.Value);
        var directory = Path.Combine(Path.GetTempPath(), "EventGhostDotNet", "Wallpaper");
        Directory.CreateDirectory(directory);

        var images = await GetBingImagesAsync(settings, cancellationToken);
        if (images.Count == 0)
            throw new InvalidOperationException("Bing n'a retourné aucune image pour cette plage d'index.");

        var generatedImages = new List<string>(images.Count);
        for (var index = 0; index < images.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = images[index];
            var downloadedPath = await DownloadImageAsync(
                image, directory, index, settings.IncludeSystemInfo, cancellationToken);
            var generatedPath = settings.IncludeSystemInfo
                ? await Task.Run(
                    () => CreateInfoImage(downloadedPath, directory, $"Image_{index + 1}.jpg"),
                    cancellationToken)
                : downloadedPath;
            generatedImages.Add(generatedPath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var monitorCount = SetWallpapers(generatedImages, cancellationToken);
        CleanupWallpaperFiles(directory, generatedImages);
        host.Result = true;
        host.Log($"Fond d'écran Bing appliqué sur {monitorCount} écran(s); " +
                 $"{generatedImages.Count} image(s) enregistrée(s) dans « {directory} ».");
    }

    private static async Task ExecuteLocalImagesAsync(
        PluginActionData action, IPluginHost host, CancellationToken cancellationToken)
    {
        var settings = ReadLocalSettings(action.Value);
        var directory = Path.Combine(Path.GetTempPath(), "EventGhostDotNet", "Wallpaper");
        Directory.CreateDirectory(directory);

        var generatedImages = new List<string>(settings.Paths.Length);
        for (var index = 0; index < settings.Paths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.Paths[index]));
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Image locale introuvable : {sourcePath}", sourcePath);

            var generatedPath = settings.IncludeSystemInfo
                ? await CreateLocalInfoImageAsync(sourcePath, directory, index, cancellationToken)
                : await Task.Run(
                    () => CopyLocalImageToTemp(sourcePath, directory, index),
                    cancellationToken);
            generatedImages.Add(generatedPath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var monitorCount = SetWallpapers(generatedImages, cancellationToken);
        CleanupWallpaperFiles(directory, generatedImages);
        host.Result = true;
        host.Log($"Fond d'écran local appliqué sur {monitorCount} écran(s); " +
                 $"{generatedImages.Count} image(s) préparée(s) depuis la liste configurée " +
                 $"{(settings.IncludeSystemInfo ? "avec" : "sans")} ajout d'informations système.");
    }

    private static async Task<string> CreateLocalInfoImageAsync(
        string sourcePath, string directory, int index, CancellationToken cancellationToken)
    {
        var outputName = $"Image_{index + 1}.jpg";
        var outputPath = Path.Combine(directory, outputName);
        var processingPath = sourcePath;
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath),
                StringComparison.OrdinalIgnoreCase))
        {
            processingPath = Path.Combine(directory, $"Image_{index + 1}_Source{Path.GetExtension(sourcePath)}");
            File.Copy(sourcePath, processingPath, overwrite: true);
        }

        return await Task.Run(
            () => CreateInfoImage(processingPath, directory, outputName), cancellationToken);
    }

    public Task ResetAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string CopyLocalImageToTemp(
        string sourcePath, string outputDirectory, int index)
    {
        var outputPath = Path.Combine(
            outputDirectory, $"Image_{index + 1}{Path.GetExtension(sourcePath)}");
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath),
                StringComparison.OrdinalIgnoreCase))
            return outputPath;
        File.Copy(sourcePath, outputPath, overwrite: true);
        return outputPath;
    }

    private static void CleanupWallpaperFiles(string directory, IReadOnlyCollection<string> retainedImages)
    {
        var retainedPaths = retainedImages
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var fileName = Path.GetFileName(path);
            if (!(fileName.StartsWith("Image_", StringComparison.OrdinalIgnoreCase) ||
                  fileName.StartsWith("Bing_", StringComparison.OrdinalIgnoreCase) ||
                  fileName.StartsWith("Local_", StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!retainedPaths.Contains(Path.GetFullPath(path)))
                File.Delete(path);
        }
    }

    private static LocalImagesSettings ReadLocalSettings(string value)
    {
        using var document = JsonDocument.Parse(value);
        LocalImagesSettings settings;
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var legacyPaths = document.RootElement.Deserialize<string?[]>();
            if (legacyPaths is null || legacyPaths.Any(string.IsNullOrWhiteSpace))
                throw new JsonException("Sélectionnez au moins une image et vérifiez les chemins.");
            settings = new LocalImagesSettings(legacyPaths.Select(path => path!).ToArray());
        }
        else
        {
            settings = document.RootElement.Deserialize<LocalImagesSettings>()
                ?? throw new JsonException("La configuration des images locales est invalide.");
        }

        if (settings.Paths is null || settings.Paths.Length == 0 ||
            settings.Paths.Any(string.IsNullOrWhiteSpace))
            throw new JsonException("Sélectionnez au moins une image et vérifiez les chemins.");
        return settings with
        {
            Paths = settings.Paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static ActionSettings ReadSettings(string value)
    {
        var settings = JsonSerializer.Deserialize<ActionSettings>(value)
            ?? throw new JsonException("La configuration Bing est vide.");
        if (settings.StartIndex is < 0 or > 7 || settings.ImageCount is < 1 or > MaximumImageCount ||
            (long)settings.StartIndex + settings.ImageCount > MaximumImageCount)
            throw new JsonException("L'index ou le nombre d'images est hors limites.");
        return settings;
    }

    private static async Task<IReadOnlyList<BingImage>> GetBingImagesAsync(
        ActionSettings settings, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            $"HPImageArchive.aspx?format=js&idx={settings.StartIndex}&n={settings.ImageCount}&mkt=fr-FR",
            UriKind.Relative);
        using var response = await HttpClient.GetAsync(
            new Uri(BingHost, uri), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("images", out var imageArray) ||
            imageArray.ValueKind != JsonValueKind.Array)
            throw new JsonException("La réponse Bing ne contient pas la liste des images.");

        var images = new List<BingImage>();
        foreach (var item in imageArray.EnumerateArray())
        {
            if (images.Count == settings.ImageCount)
                break;
            var relativeUrl = item.GetProperty("url").GetString();
            if (string.IsNullOrWhiteSpace(relativeUrl))
                throw new JsonException("Une image Bing ne contient pas son URL.");

            var imageUri = Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri
                : new Uri(BingHost, relativeUrl);
            if (imageUri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(imageUri.Host, BingHost.Host, StringComparison.OrdinalIgnoreCase))
                throw new JsonException("Bing a retourné une URL d'image non sécurisée ou inattendue.");

            images.Add(new BingImage(imageUri.AbsoluteUri));
        }
        return images;
    }

    private static async Task<string> DownloadImageAsync(
        BingImage image, string directory, int imageIndex, bool includeSystemInfo,
        CancellationToken cancellationToken)
    {
        var imageName = includeSystemInfo
            ? $"Image_{imageIndex + 1}_Source.jpg"
            : $"Image_{imageIndex + 1}.jpg";
        var destination = Path.Combine(directory, imageName);
        var temporaryPath = destination + ".download.tmp";
        try
        {
            using var response = await HttpClient.GetAsync(
                image.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumImageBytes)
                throw new InvalidOperationException("L'image Bing dépasse la taille maximale de 50 Mo.");

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[81920];
            long totalBytes = 0;
            await using (var output = new FileStream(
                temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                while (true)
                {
                    var bytesRead = await input.ReadAsync(buffer, cancellationToken);
                    if (bytesRead == 0)
                        break;
                    totalBytes += bytesRead;
                    if (totalBytes > MaximumImageBytes)
                        throw new InvalidOperationException("L'image Bing dépasse la taille maximale de 50 Mo.");
                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }
                if (totalBytes == 0)
                    throw new InvalidOperationException("Bing a retourné une image vide.");
                await output.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, destination, overwrite: true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string CreateInfoImage(string imagePath, string outputDirectory, string outputFileName)
    {
        using var source = Image.FromFile(imagePath);
        using var bitmap = new Bitmap(source);
        using var graphics = Graphics.FromImage(bitmap);
        using var titleFont = new Font("Arial", 28, FontStyle.Bold);
        using var normalFont = new Font("Arial", 16, FontStyle.Bold);
        using var blackBrush = new SolidBrush(Color.Black);
        using var yellowBrush = new SolidBrush(Color.Yellow);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rows = CreateInfoRows();
        var maxLabelWidth = 0f;
        var maxValueWidth = 0f;
        for (var index = 0; index < rows.Count; index++)
        {
            var font = index == 0 ? titleFont : normalFont;
            maxLabelWidth = Math.Max(maxLabelWidth, graphics.MeasureString(rows[index].Label, font).Width);
            maxValueWidth = Math.Max(maxValueWidth, graphics.MeasureString(rows[index].Value, font).Width);
        }

        var valueOffset = (int)Math.Ceiling(maxLabelWidth) + 40;
        var blockWidth = valueOffset + maxValueWidth;
        var startX = Math.Max(20, bitmap.Width - blockWidth - 65);
        for (var index = 0; index < rows.Count; index++)
        {
            var font = index == 0 ? titleFont : normalFont;
            var y = index == 0 ? 20 : 55 + ((index - 1) * 24);
            DrawShadowText(graphics, rows[index].Label, font, yellowBrush, blackBrush, startX, y);
            DrawShadowText(graphics, rows[index].Value, font, yellowBrush, blackBrush, startX + valueOffset, y);
        }

        var outputPath = Path.Combine(outputDirectory, outputFileName);
        var jpegCodec = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(codec => codec.MimeType == "image/jpeg")
            ?? throw new InvalidOperationException("L'encodeur JPEG de Windows est indisponible.");
        using var encoderParameters = new EncoderParameters(1);
        using var qualityParameter = new EncoderParameter(Encoder.Quality, 95L);
        encoderParameters.Param[0] = qualityParameter;
        bitmap.Save(outputPath, jpegCodec, encoderParameters);
        return outputPath;
    }

    private static List<InfoRow> CreateInfoRows()
    {
        var computerName = Dns.GetHostName();
        var operatingSystem = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "ProductName",
            null) as string ?? Environment.OSVersion.VersionString;
        var bootTime = DateTime.Now.Subtract(TimeSpan.FromMilliseconds(Environment.TickCount64));
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var drive = new DriveInfo(systemDrive);
        var freeSpace = (drive.AvailableFreeSpace / 1024d / 1024d / 1024d)
            .ToString("F2", CultureInfo.InvariantCulture);

        return
        [
            new InfoRow("Nom de l'ordinateur:", computerName),
            new InfoRow("Système d'exploitation:", operatingSystem),
            new InfoRow("Heure de démarrage:", bootTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            new InfoRow("Espace libre:", $"{systemDrive} {freeSpace} GB NTFS"),
            new InfoRow("Domaine de la machine:", Environment.UserDomainName),
            new InfoRow("Utilisateur connecté:", Environment.UserName)
        ];
    }

    private static void DrawShadowText(
        Graphics graphics, string text, Font font, Brush foreground, Brush shadow, float x, float y)
    {
        graphics.DrawString(text, font, shadow, x - 1, y - 1);
        graphics.DrawString(text, font, shadow, x + 1, y - 1);
        graphics.DrawString(text, font, shadow, x - 1, y + 1);
        graphics.DrawString(text, font, shadow, x + 1, y + 1);
        graphics.DrawString(text, font, foreground, x, y);
    }

    private static int SetWallpapers(
        IReadOnlyList<string> wallpaperPaths, CancellationToken cancellationToken)
    {
        if (wallpaperPaths.Count == 0)
            throw new ArgumentException("Aucune image de fond d'écran fournie.", nameof(wallpaperPaths));

        var componentType = Type.GetTypeFromCLSID(
            new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), throwOnError: true)
            ?? throw new InvalidOperationException("Le composant DesktopWallpaper de Windows est indisponible.");
        var component = Activator.CreateInstance(componentType)
            ?? throw new InvalidOperationException("Impossible de créer le composant DesktopWallpaper.");
        try
        {
            var desktopWallpaper = (IDesktopWallpaperNative)component;
            var monitorPaths = GetMonitorDevicePaths(desktopWallpaper);
            if (monitorPaths.Length == 0)
                throw new InvalidOperationException("Aucun écran n'a été détecté.");

            for (var index = 0; index < monitorPaths.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CheckHResult(
                    desktopWallpaper.SetWallpaper(
                        monitorPaths[index], wallpaperPaths[index % wallpaperPaths.Count]),
                    "SetWallpaper");
            }
            return monitorPaths.Length;
        }
        finally
        {
            if (Marshal.IsComObject(component))
                Marshal.ReleaseComObject(component);
        }
    }

    private static string[] GetMonitorDevicePaths(IDesktopWallpaperNative wallpaper)
    {
        CheckHResult(wallpaper.GetMonitorDevicePathCount(out var count), "GetMonitorDevicePathCount");
        var paths = new string[checked((int)count)];
        for (uint index = 0; index < count; index++)
        {
            var pathPointer = IntPtr.Zero;
            CheckHResult(
                wallpaper.GetMonitorDevicePathAt(index, out pathPointer), "GetMonitorDevicePathAt");
            try
            {
                paths[index] = Marshal.PtrToStringUni(pathPointer)
                    ?? throw new InvalidOperationException("Windows a retourné un identifiant d'écran vide.");
            }
            finally
            {
                if (pathPointer != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pathPointer);
            }
        }
        return paths;
    }

    private static void CheckHResult(int result, string operation)
    {
        if (result < 0)
            throw new InvalidOperationException(
                $"{operation} a échoué (HRESULT 0x{unchecked((uint)result):X8}).");
    }

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaperNative
    {
        [PreserveSig]
        int SetWallpaper(
            [MarshalAs(UnmanagedType.LPWStr)] string monitorId,
            [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        [PreserveSig]
        int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out IntPtr wallpaper);

        [PreserveSig]
        int GetMonitorDevicePathAt(uint monitorIndex, out IntPtr monitorId);

        [PreserveSig]
        int GetMonitorDevicePathCount(out uint count);
    }
}
