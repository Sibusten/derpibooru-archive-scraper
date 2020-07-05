using HtmlAgilityPack;
using Newtonsoft.Json;
using Npgsql;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace DerpibooruArchiveScraper {
    public class Program {
        private const string _connectionString = "Username=postgres;Password=password;Host=localhost;Port=5432;Database=derpibooru;";

        private const string _archiveBaseUrl = "https://theponyarchive.com/archive/derpibooru/";

        private const string _downloadFolder = "Downloads";
        private const string _jsonFolder = "Json";

        private const string _getImages = @"
select
    public.images.id as id,
    public.images.image_format as image_format

from public.images

inner join public.image_taggings on public.images.id = public.image_taggings.image_id
inner join public.tags on public.image_taggings.tag_id = public.tags.id

where public.tags.name = @tag;";

        private const string _getImageTags = @"
select
	public.tags.name as name

from public.tags

inner join public.image_taggings on public.tags.id = public.image_taggings.tag_id
inner join public.images on public.image_taggings.image_id = public.images.id

where public.images.id = @imageId;";

        private class ImageInfo {
            public int Id { get; set; }
            public string Ext { get; set; }

            public string GetFilename() {
                return $"{Id}.{Ext}";
            }
        }

        private static string GetFolderForImage(int imageId) {
            // Get the next multiple of 1000 for the ID
            int nextMultiple = ((imageId / 1000) + 1) * 1000;

            return $"{_archiveBaseUrl}{nextMultiple}/";
        }

        private static string GetValidFilename(string filename) {
            char[] invalidFileNameChars = Path.GetInvalidFileNameChars();

            // Builds a string out of valid chars and an _ for invalid ones
            return new string(filename.Select(ch => invalidFileNameChars.Contains(ch) ? '_' : ch).ToArray());
        }

        private static async Task Main(string[] args) {
            // Prompt for the search tag
            Console.Write("Enter search tag: ");
            string searchTag = Console.ReadLine();

            // Create download directories
            DirectoryInfo cwd = new DirectoryInfo(Directory.GetCurrentDirectory());
            DirectoryInfo downloadDir = cwd.CreateSubdirectory(_downloadFolder);
            DirectoryInfo tagDir = downloadDir.CreateSubdirectory(GetValidFilename(searchTag));
            DirectoryInfo jsonDir = tagDir.CreateSubdirectory(_jsonFolder);

            // Connect to the local derpibooru database
            Console.WriteLine("Connecting...");
            using (NpgsqlConnection connection = new NpgsqlConnection(_connectionString)) {
                await connection.OpenAsync();

                // Load the list of image IDs to scrape
                List<ImageInfo> imageInfos = new List<ImageInfo>();
                using (NpgsqlCommand getImagesCommand = new NpgsqlCommand(_getImages, connection)) {
                    getImagesCommand.Parameters.AddWithValue("tag", searchTag);

                    using (NpgsqlDataReader reader = await getImagesCommand.ExecuteReaderAsync()) {
                        while (await reader.ReadAsync()) {
                            imageInfos.Add(new ImageInfo
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                Ext = reader.GetString(reader.GetOrdinal("image_format")),
                            });
                        }
                    }
                }

                // Test if any images were found
                Console.WriteLine($"Found {imageInfos.Count} images");
                if (imageInfos.Count < 0) {
                    return;
                }

                // Download images from the archive
                List<int> missingImages = new List<int>();
                foreach (ImageInfo imageInfo in imageInfos) {
                    string imagePath = $"{tagDir.FullName}/{imageInfo.GetFilename()}";

                    // Skip existing images
                    if (File.Exists(imagePath)) {
                        Console.WriteLine($"Skipping existing image: {imageInfo.Id}");
                        continue;
                    }

                    string baseUrl = GetFolderForImage(imageInfo.Id);

                    // Get remote image name
                    HtmlWeb web = new HtmlWeb();
                    HtmlDocument doc = web.Load(baseUrl);
                    string imageName = null;
                    foreach (HtmlNode node in doc.DocumentNode.SelectNodes("//tr/*[2]/a")) {
                        if (node.InnerText.StartsWith(imageInfo.Id.ToString())) {
                            imageName = node.Attributes["href"].Value;
                            break;
                        }
                    }

                    // Check if the image could be found
                    if (imageName == null) {
                        Console.WriteLine($"Could not find image {imageInfo.Id} on archive!");
                        missingImages.Add(imageInfo.Id);
                        continue;
                    }

                    string imageUrl = $"{baseUrl}{imageName}";

                    // Download the image
                    using (WebClient wc = new WebClient()) {
                        await wc.DownloadFileTaskAsync(new Uri(imageUrl), imagePath);

                        Console.WriteLine($"Downloaded: {imageInfo.Id}");
                    }

                    // Collect the image tags
                    List<string> imageTags = new List<string>();
                    using (NpgsqlCommand getTagsCommand = new NpgsqlCommand(_getImageTags, connection)) {
                        getTagsCommand.Parameters.AddWithValue("imageId", imageInfo.Id);

                        using (NpgsqlDataReader reader = await getTagsCommand.ExecuteReaderAsync()) {
                            while (await reader.ReadAsync()) {
                                imageTags.Add(reader.GetString(0));
                            }
                        }
                    }

                    // Create image tag JSON
                    var tagJsonObj = new
                    {
                        tags = imageTags
                    };
                    string tagJson = JsonConvert.SerializeObject(tagJsonObj);

                    // Get JSON file path
                    string jsonPath = $"{jsonDir.FullName}/{imageInfo.Id}.json";

                    // Write JSON
                    using (StreamWriter jsonWriter = new StreamWriter(jsonPath)) {
                        jsonWriter.WriteLine(tagJson);
                    }
                }

                if (missingImages.Count > 0) {
                    string missingImagesString = string.Join(", ", missingImages.Select(id => id.ToString()));

                    Console.WriteLine($"Could not find {missingImages.Count} images!");
                    Console.WriteLine(missingImagesString);

                    // Write missing files
                    string missingPath = $"{tagDir.FullName}/missing.txt";
                    using (StreamWriter missingWriter = new StreamWriter(missingPath)) {
                        missingWriter.WriteLine(missingImagesString);
                    }
                }
            }
        }
    }
}
