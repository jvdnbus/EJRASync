namespace EJRASync.UI.Models {
	public class LocalFileItem {
		public string Name { get; set; } = string.Empty;
		public string FullPath { get; set; } = string.Empty;
		public string DisplaySize { get; set; } = string.Empty;
		public DateTime LastModified { get; set; }
		public long SizeBytes { get; set; }
		public bool IsDirectory { get; set; }
		public string FileHash { get; set; } = string.Empty;
	}
}