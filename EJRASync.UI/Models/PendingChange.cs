namespace EJRASync.UI.Models {
	public enum ChangeType {
		CompressAndUpload,
		RawUpload,
		DeleteRemote,
		UpdateYaml,
		UpdateHashStore
	}

	public class PendingChange {
		public string Id { get; set; } = Guid.NewGuid().ToString();
		public ChangeType Type { get; set; }
		public string Description { get; set; } = string.Empty;
		public string? LocalPath { get; set; }
		public string RemoteKey { get; set; } = string.Empty;
		public string BucketName { get; set; } = string.Empty;
		public long? FileSizeBytes { get; set; }
		public Dictionary<string, string> Metadata { get; set; } = new();
	}
}