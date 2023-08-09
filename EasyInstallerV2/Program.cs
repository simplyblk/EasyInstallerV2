using System.IO.Compression;
using System.Text.Json;
using FNBuilds.API;

namespace EasyInstallerV2
{
	class Program
	{
		private static API api = new();
		private const int chunkSize = 536870912 / 8;
		static string FormatBytesWithSuffix(long bytes)
		{
			string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
			int i;
			double dblSByte = bytes;
			for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
			{
				dblSByte = bytes / 1024.0;
			}

			return string.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
		}
		private static async Task Download(ManifestFile manifest, string version, string resultPath)
		{
			if (manifest.Chunks != null)
			{
				long totalBytes = manifest.Size;
				long completedBytes = 0;
				int progressLength = 0;

				Directory.CreateDirectory(resultPath);

				SemaphoreSlim semaphore = new(Environment.ProcessorCount * 2);

				await Task.WhenAll(manifest.Chunks.Select(async chunkedFile =>
				{
					await semaphore.WaitAsync();

					if (chunkedFile.File != null && chunkedFile.ChunksIds != null)
					{
						try
						{
							string outputFilePath = Path.Combine(resultPath, chunkedFile.File);
							FileInfo fileInfo = new(outputFilePath);

							if (File.Exists(outputFilePath) && fileInfo.Length == chunkedFile.FileSize)
							{
								completedBytes += chunkedFile.FileSize;
								semaphore.Release();
								return;
							}

							string? path = Path.GetDirectoryName(outputFilePath);
							if (path != null)
							{
								Directory.CreateDirectory(path);
							}

							using (FileStream outputStream = File.OpenWrite(outputFilePath))
							{
								foreach (int chunkId in chunkedFile.ChunksIds)
								{
								retry:

									try
									{
										byte[] chunkData = api.DownloadChunk(version, chunkId);

										byte[] chunkDecompData = new byte[chunkSize + 1];
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
									catch
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
					}
				}));

				Console.WriteLine("\n\nFinished Downloading.\nPress any key to exit!");
				Thread.Sleep(100);
				Console.ReadKey();
			}
		}
		static void Main(string[] args)
		{
			List<string> versions = api.versions;

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
			string? targetVersionStr = Console.ReadLine();
			Console.Write("Please enter a game folder location: ");
			string? targetPath = Console.ReadLine();

			if (!string.IsNullOrEmpty(targetVersionStr) && !string.IsNullOrEmpty(targetPath) && int.TryParse(targetVersionStr, out int targetVersionIndex) && targetVersionIndex >= 0 && targetVersionIndex < versions.Count)
			{
				string targetVersion = versions[targetVersionIndex].Split("-")[1];
				ManifestFile manifest = api.GetManifest(targetVersion);
				
				Console.Write("\n");
				Download(manifest, targetVersion, targetPath).Wait();
			}
			else
			{
				Main(args);
				return;
			}
		}
	}
}