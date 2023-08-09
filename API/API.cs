using System.Text.Json;

namespace FNBuilds.API
{
	public class API
	{
		private HttpClient client;
		public API(string server = "https://manifest.fnbuilds.services")
		{
			client = new()
			{
				BaseAddress = new(server)
			};
		}
		public List<string> versions
		{
			get
			{
				return Deserialize<List<string>>(client.GetStringAsync("/versions.json").Result);
			}
		}
		public ManifestFile GetManifest(string version)
		{
			string json = client.GetStringAsync($"/{version}/{version}.manifest").Result;
			return Deserialize<ManifestFile>(json);
		}
		public byte[] DownloadChunk(string version, int chunk)
		{
			return client.GetByteArrayAsync($"/{version}/{chunk}.chunk").Result;
		}
		private static T Deserialize<T>(string json)
		{
			T? item = JsonSerializer.Deserialize<T>(json);
			if (item != null)
			{
				return item;
			}
			throw new Exception("Json deserialization failed");
		}
	}
}