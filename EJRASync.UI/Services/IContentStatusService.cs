namespace EJRASync.UI.Services {
	public interface IContentStatusService {
		Task InitializeAsync();
		bool? IsContentActive(string bucketName, string contentName);
		Task SetContentActiveAsync(string bucketName, string contentName, bool isActive);
		Task<byte[]> GenerateYamlAsync(string bucketName);
		List<string> GetActiveContent(string bucketName);
		event EventHandler<ContentStatusChangedEventArgs>? ContentStatusChanged;
	}

	public class ContentStatusChangedEventArgs : EventArgs {
		public string BucketName { get; set; } = string.Empty;
		public string ContentName { get; set; } = string.Empty;
		public bool IsActive { get; set; }
	}
}