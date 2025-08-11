using EJRASync.UI.Models;
using System.IO;

namespace EJRASync.UI.Services {
	public interface IS3Service : IDisposable {
		Task<List<RemoteFileItem>> ListObjectsAsync(string bucketName, string prefix = "", string delimiter = "/", CancellationToken cancellationToken = default);
		Task<List<string>> ListBucketsAsync();
		Task<RemoteFileItem?> GetObjectMetadataAsync(string bucketName, string key);
		Task UploadFileAsync(string bucketName, string key, string localFilePath, Dictionary<string, string>? metadata = null, IProgress<long>? progress = null);
		Task UploadDataAsync(string bucketName, string key, byte[] data, Dictionary<string, string>? metadata = null);
		Task DeleteObjectAsync(string bucketName, string key);
		Task DeleteObjectsRecursiveAsync(string bucketName, string keyPrefix);
		Task<string> DownloadObjectAsync(string bucketName, string key, FileStream? fileStream = null, IProgress<long>? progress = null);
		void UpdateCredentials(string accessKeyId, string secretAccessKey, string serviceUrl);
		int GetRetryQueueCount();
	}
}