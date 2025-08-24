using System.Text.Json.Serialization;

namespace EJRASync.Lib.Models {
	public class ArchiveMetadataRequest {
		[JsonPropertyName("metadata")]
		public List<ArchiveMetadataItem> Metadata { get; set; } = new();
	}

	public class ArchiveMetadataItem {
		[JsonPropertyName("key")]
		public string Key { get; set; } = string.Empty;

		[JsonPropertyName("md5")]
		public string Md5 { get; set; } = string.Empty;

		[JsonPropertyName("fileSize")]
		public long FileSize { get; set; }

		[JsonPropertyName("displayName")]
		public string DisplayName { get; set; } = string.Empty;

		[JsonPropertyName("uploadDate")]
		public string UploadDate { get; set; } = string.Empty;
	}

	public class ArchiveMetadataResponse {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("stored")]
		public int Stored { get; set; }
	}

	public class ApiErrorResponse {
		[JsonPropertyName("error")]
		public string Error { get; set; } = string.Empty;
	}
}