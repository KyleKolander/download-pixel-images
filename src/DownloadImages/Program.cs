using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Io;

namespace App
{
    [DebuggerDisplay("{ToString()}")]
    class Image
    {
        public string Version { get; set; }
        public string Download { get; set; }
        public override string ToString()
        {
            return $"{Version,-75}{Download}";
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            var pixelPath = @"D:\Pixel";
            if (args.Length > 0)
            {
                pixelPath = args[0];
            }

            WriteLine("");
            WriteLine($"OTA and Factory images will be downloaded to  ->  {pixelPath}");
            WriteLine("");

            const string otaImagesUrl = "https://developers.google.com/android/ota";
            const string factoryImagesUrl = "https://developers.google.com/android/images";
            const string otaImagesUrlCookie = "devsite_wall_acks=nexus-ota-tos";
            const string factoryImagesUrlCookie = "devsite_wall_acks=nexus-image-tos";
            const string headerText = "Version (\"redfin\" for Pixel 5)";

            var otaImages = await ExtractImages(otaImagesUrl, otaImagesUrlCookie);
            var factoryImages = await ExtractImages(factoryImagesUrl, factoryImagesUrlCookie);
            var selectedOtaImage = GetOtaImageUserSelection(otaImages, headerText);
            var selectedFactoryImage = factoryImages.First(x => x.Version == selectedOtaImage.Version);
            var dateString = GetImageVersionDate(selectedOtaImage);
            
            var downloadPath = Path.Combine(pixelPath, dateString);
            var otaDownloadPath = Path.Combine(downloadPath, "OTA.zip");
            var factoryDownloadPath = Path.Combine(downloadPath, "FACTORY.zip");
            if (!Directory.Exists(downloadPath))
            {
                Directory.CreateDirectory(downloadPath);
            }

            Console.Clear();
            WriteLine("");
            WriteLine($"SELECTED");
            WriteLine($"   DATE:            {dateString}");
            WriteLine($"   VERSION:         {selectedOtaImage.Version}");
            WriteLine($"   OTA:             {selectedOtaImage.Download}");
            WriteLine($"   FACTORY:         {selectedFactoryImage.Download}");
            WriteLine($"   DOWNLOAD PATH:   {downloadPath}");
            WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("");

            object lockObject = new object(); 
            long otaLastProgressUpdatePosition = -1;
            long factoryLastProgressUpdatePosition = -1;

            DownloadProgressChangedEventHandler otaProgressChanged = (object sender, DownloadProgressChangedEventArgs e) =>
            {
                double percent = ((double)e.BytesReceived / (double)e.TotalBytesToReceive) * 100;
                lock (lockObject)
                {
                    if (otaLastProgressUpdatePosition <= e.BytesReceived)
                    {
                        Console.SetCursorPosition(0, 8);
                        var isCompleted = e.TotalBytesToReceive != 0 && e.BytesReceived == e.TotalBytesToReceive;
                        if (isCompleted)
                        {
                            Console.Write($"OTA     = {otaDownloadPath,-80}");
                        }
                        else
                        {
                            Console.Write($"OTA     = {percent,7:##0.000} %     ({e.BytesReceived,13:0,000} of {e.TotalBytesToReceive,13:0,000})");
                        }
                        otaLastProgressUpdatePosition = e.BytesReceived;
                    }
                }
            };

            DownloadProgressChangedEventHandler factoryProgressChanged = (object sender, DownloadProgressChangedEventArgs e) =>
            {
                double percent = ((double)e.BytesReceived / (double)e.TotalBytesToReceive) * 100;
                lock (lockObject)
                {
                    if (factoryLastProgressUpdatePosition <= e.BytesReceived)
                    {
                        Console.SetCursorPosition(0, 9);
                        var isCompleted = e.TotalBytesToReceive != 0 && e.BytesReceived == e.TotalBytesToReceive;
                        if (isCompleted)
                        {
                            Console.Write($"FACTORY = {factoryDownloadPath,-80}");
                        }
                        else
                        {
                            Console.Write($"FACTORY = {percent,7:##0.000} %     ({e.BytesReceived,13:0,000} of {e.TotalBytesToReceive,13:0,000})");
                        }
                        factoryLastProgressUpdatePosition = e.BytesReceived;
                    }
                }
            };

            var otaTask = DownloadImage(selectedOtaImage, otaDownloadPath, otaProgressChanged);
            var factoryTask = DownloadImage(selectedFactoryImage, factoryDownloadPath, factoryProgressChanged);
            await Task.WhenAll(otaTask, factoryTask);

            WriteLine("");
            Console.SetCursorPosition(0, 11);
            WriteLine("DONE.");
        }

        static void Write(string text)
        {
            Console.Write(text);
            Debug.Write(text);
        }

        static void WriteLine(string text)
        {
            Console.WriteLine(text);
            Debug.WriteLine(text);
        }

        static async Task<List<Image>> ExtractImages(string imagesUrl, string cookie)
        {
            var url = new Url(imagesUrl);

            var config = Configuration.Default.WithDefaultLoader(new LoaderOptions { IsResourceLoadingEnabled = true }).WithCookies().WithJs();
            var context = BrowsingContext.New(config);

            // bypass the acknowledge button click
            context.SetCookie(url, cookie);

            var document = await context.OpenAsync(imagesUrl);

            var redfin = document.GetElementById("redfin").NextSibling.NextSibling.LastChild as IHtmlTableSectionElement;
            var redfinChildren = redfin.Children;

            var images = new List<Image>();

            for (var i = redfinChildren.Length - 1; i >= 0; i--)
            {
                var tr = redfinChildren[i] as IHtmlTableRowElement;
                images.Add(new Image
                {
                    Version = tr.Cells[0].TextContent,
                    Download = (tr.Cells.Where(x => x.TextContent == "Link").First().FirstElementChild as IHtmlAnchorElement)?.Href
                });
            }

            return images;
        }

        static Image GetOtaImageUserSelection(List<Image> images, string headerText)
        {
            var maxVersionLength = images.Max(x => x.Version.Length);

            var sb = new StringBuilder();
            sb.AppendLine($"         {headerText.PadRight(maxVersionLength)}");
            sb.AppendLine($"         {new string('=', maxVersionLength)}");

            for (var i = 0; i < images.Count; i++)
            {
                var image = images[i];
                sb.AppendLine($"   {(i + 1).ToString().PadLeft(2, ' ')}    {image.Version.PadRight(maxVersionLength)}");
            }
            sb.AppendLine();
            sb.Append("Please select the image version: ");

            Write(sb.ToString());

            var selectedImageNumber = Convert.ToInt32(Console.ReadLine());
            var selectedImage = images[selectedImageNumber - 1];

            return selectedImage;
        }

        static string GetImageVersionDate(Image image)
        {
            var version = image.Version;

            //    11.0.0 (RQ3A.210705.001, Jul 2021)

            Regex pattern = new Regex(@"^.+\(\w+\.(?<date>\d+)\.\d+(\.\w+)*,.*\)$");
            Match match = pattern.Match(version);
            string date = match.Groups["date"].Value;
            return $"20{date}";
        }

        static async Task DownloadImage(Image image, string downloadPath, DownloadProgressChangedEventHandler progressChanged)
        {
            using var client = new WebClient();
            client.DownloadProgressChanged += progressChanged;
            await client.DownloadFileTaskAsync(image.Download, downloadPath);
        }
    }
}
