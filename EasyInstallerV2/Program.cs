using Newtonsoft.Json;
using System.IO.Compression;
using System.Net;

namespace EasyInstallerV2
{
    class Program
    {
        public const string BASE_URL = "https://manifest.fnbuilds.services";
        private const int CHUNK_SIZE = 536870912 / 8;

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

            SemaphoreSlim semaphore = new SemaphoreSlim(12);

            await Task.WhenAll(manifest.Chunks.Select(async chunkedFile =>
            {
                await semaphore.WaitAsync();

                try
                {
                    WebClient httpClient = new WebClient();

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
                                var chunkData = await httpClient.DownloadDataTaskAsync(chunkUrl);

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

        static void Main(string[] args)
        {
            var httpClient = new WebClient();

            List<string> versions = JsonConvert.DeserializeObject<List<string>>(httpClient.DownloadString(BASE_URL + "/versions.json"));

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
            var targetVersionIndex = 0;

            try
            {
                targetVersionIndex = int.Parse(targetVersionStr);
            }
            catch (Exception ex)
            {
                Main(args);
                return;
            }

            if (!(targetVersionIndex >= 0 && targetVersionIndex < versions.Count))
            {
                Main(args);
                return;
            }

            var targetVersion = versions[targetVersionIndex].Split("-")[1];
            var manifest = JsonConvert.DeserializeObject<ManifestFile>(httpClient.DownloadString(BASE_URL + $"/{targetVersion}/{targetVersion}.manifest"));

            Console.Write("Please enter a game folder location: ");
            var targetPath = Console.ReadLine();
            Console.Write("\n");

            Download(manifest, targetVersion, targetPath).GetAwaiter().GetResult();
        }
    }
}
