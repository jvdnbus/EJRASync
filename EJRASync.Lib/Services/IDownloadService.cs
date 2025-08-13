using EJRASync.Lib.Models;

namespace EJRASync.Lib.Services {
	public interface IDownloadService {
		Task<List<RemoteFile>> GetFilesToDownloadAsync(string bucketName, string remotePrefix, string localBasePath);
		Task DownloadFilesAsync(List<RemoteFile> filesToDownload, string bucketName, string localBasePath, IProgress<DownloadProgress>? progress = null);
		Task<bool> ValidateFileAsync(string localFilePath, string expectedHash);
	}

	public class DownloadProgress {
		public int TotalFiles { get; set; }
		public int CompletedFiles { get; set; }
		public long TotalBytes { get; set; }
		public long CompletedBytes { get; set; }
		public string CurrentFileName { get; set; } = string.Empty;
		public long CurrentFileBytes { get; set; }
		public long CurrentFileTotal { get; set; }
		public bool IsDecompressing { get; set; }
		public List<FileProgress> ActiveDownloads { get; set; } = new();
	}

	public class FileProgress {
		public string FileName { get; set; } = string.Empty;
		public long CompletedBytes { get; set; }
		public long TotalBytes { get; set; }
		public bool IsDecompressing { get; set; }
	}
}