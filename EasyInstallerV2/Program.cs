using System.Text.Json;
using System.IO.Compression;
using System.Net;
using System.Net.Http;

namespace EasyInstallerV2
{
    class Program
    {
        const string BASE_URL = "https://cdn.novafn.dev";
        const int CHUNK_SIZE = 67108864;

        public static HttpClient httpClient = new HttpClient();

        class ChunkedFile
        {
            public List<int> ChunksIds = new();
            public String File = String.Empty;
            public long FileSize = 0;
        }

        class ManifestFile
        {
            public String Name = String.Empty;
            public List<ChunkedFile> Chunks = new();
            public long Size = 0;
        }

        static string FormatBytesWithSuffix(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }

            return String.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
        }

        static async Task Download(ManifestFile manifest, string version, string resultPath)
        {
            long totalBytes = manifest.Size;
            long completedBytes = 0;
            int progressLength = 0;

            if (!Directory.Exists(resultPath))
                Directory.CreateDirectory(resultPath);

            SemaphoreSlim semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

            await Task.WhenAll(manifest.Chunks.Select(async chunkedFile =>
            {
                await semaphore.WaitAsync();

                try
                {
                    WebClient webClient = new WebClient();

                    string outputFilePath = Path.Combine(resultPath, chunkedFile.File);
                    var fileInfo = new FileInfo(outputFilePath);

                    if (File.Exists(outputFilePath) && fileInfo.Length == chunkedFile.FileSize)
                    {
                        completedBytes += chunkedFile.FileSize;
                        semaphore.Release();
                        return;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath));

                    using (FileStream outputStream = File.OpenWrite(outputFilePath))
                    {
                        foreach (int chunkId in chunkedFile.ChunksIds)
                        {
                            retry:

                            try
                            {
                                string chunkUrl = BASE_URL + $"/{version}/" + chunkId + ".chunk";
                                var chunkData = await webClient.DownloadDataTaskAsync(chunkUrl);

                                byte[] chunkDecompData = new byte[CHUNK_SIZE + 1];
                                int bytesRead;
                                long chunkCompletedBytes = 0;

                                MemoryStream memoryStream = new MemoryStream(chunkData);
                                GZipStream decompressionStream = new GZipStream(memoryStream, CompressionMode.Decompress);

                                while ((bytesRead = await decompressionStream.ReadAsync(chunkDecompData, 0, chunkDecompData.Length)) > 0)
                                {
                                    await outputStream.WriteAsync(chunkDecompData, 0, bytesRead);
                                    Interlocked.Add(ref completedBytes, bytesRead);
                                    Interlocked.Add(ref chunkCompletedBytes, bytesRead);

                                    double progress = (double)completedBytes / totalBytes * 100;
                                    string progressMessage = $"\rDownloaded: {FormatBytesWithSuffix(completedBytes)} / {FormatBytesWithSuffix(totalBytes)} ({progress:F2}%)";

                                    int padding = progressLength - progressMessage.Length;
                                    if (padding > 0)
                                        progressMessage += new string(' ', padding);

                                    Console.Write(progressMessage);
                                    progressLength = progressMessage.Length;
                                }

                                memoryStream.Close();
                                decompressionStream.Close();
                            }
                            catch (Exception ex)
                            {
                                goto retry;
                            }
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }));

            Console.WriteLine("\n\nFinished Downloading.\nPress any key to exit!");
            Thread.Sleep(100);
            Console.ReadKey();
        }

        static async Task<List<string>> GetVersionsAsync()
        {
            var versionsResponse = await httpClient.GetStringAsync(BASE_URL + "/versions.json");

            if (string.IsNullOrEmpty(versionsResponse))
            {
                throw new Exception("failed to get versions");
            }

            var versions = JsonConvert.DeserializeObject<List<string>>(versionsResponse);

            if (versions == null)
            {
                throw new Exception("failed to parse versions");
            }

            return versions;
        }

        static async Task<ManifestFile> GetManifestAsync(string version)
        {
            var manifestResponse = await httpClient.GetStringAsync(BASE_URL + $"/{version}/{version}.manifest");

            if (string.IsNullOrEmpty(manifestResponse))
            {
                throw new Exception("failed to get manifest");
            }

            var manifest = JsonConvert.DeserializeObject<ManifestFile>(manifestResponse);

            if (manifest == null)
            {
                throw new Exception("failed to parse manifest");
            }

            return manifest;
        }

        static async Task Main(string[] args)
        {
            var versions = await GetVersionsAsync();

            Console.Clear();

            Console.Title = "EasyInstaller V2 made by Ender & blk";
            Console.Write("\n\nEasyInstaller V2 made by Ender & blk\n\n");
            Console.WriteLine("\nAvailable manifests:");

            for (int i = 0; i < versions.Count; i++)
            {
                Console.WriteLine($" * [{i}] {versions[i]}");
            }

            Console.WriteLine($"\nTotal: {versions.Count}");

            Console.Write("Please enter the number before the Build Version to select it: ");
            var targetVersionStr = Console.ReadLine();

            if (!int.TryParse(targetVersionStr, out int targetVersionIndex))
            {
                await Main(args);
                return;
            }

            if (versions.ElementAtOrDefault(targetVersionIndex) == null)
            {
                await Main(args);
                return;
            }

            var targetVersion = versions[targetVersionIndex].Split("-")[1];
            var manifest = await GetManifestAsync(targetVersion);

            Console.Write("Please enter a game folder location: ");
            var targetPath = Console.ReadLine();
            Console.Write("\n");

            if (string.IsNullOrEmpty(targetPath))
            {
                await Main(args);
                return;
            }

            Download(manifest, targetVersion, targetPath).GetAwaiter().GetResult();
        }
    }
}
