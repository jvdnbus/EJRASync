namespace EJRASync.UI.Models {
	public class RemoteFileItem {
		public string Name { get; set; } = string.Empty;
		public string Key { get; set; } = string.Empty;
		public string DisplaySize { get; set; } = string.Empty;
		public DateTime LastModified { get; set; }
		public long SizeBytes { get; set; }
		public bool IsDirectory { get; set; }
		public bool IsCompressed { get; set; }
		public bool? IsActive { get; set; }
		public string ETag { get; set; } = string.Empty;
		public string? OriginalHash { get; set; }
	}
}