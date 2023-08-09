namespace FNBuilds.API
{
	public class ChunkedFile
	{
		public List<int>? ChunksIds { get; set; }
		public string? File { get; set; }
		public long FileSize { get; set; }
	}
	public class ManifestFile
	{
		public string? Name { get; set; }
		public List<ChunkedFile>? Chunks { get; set; }
		public long Size { get; set; }
	}
}