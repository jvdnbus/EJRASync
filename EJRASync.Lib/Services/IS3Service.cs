using EJRASync.Lib.Models;

namespace EJRASync.Lib.Services {
	public interface IS3Service : IDisposable {
		Task<List<RemoteFile>> ListObjectsAsync(string bucketName, string prefix = "", string delimiter = "/", CancellationToken cancellationToken = default);
		Task<List<string>> ListBucketsAsync();
		Task<RemoteFile?> GetObjectMetadataAsync(string bucketName, string key);
		Task<string?> GetObjectOriginalHashFromMetadataAsync(string bucketName, string key);
		Task UploadFileAsync(string bucketName, string key, string localFilePath, Dictionary<string, string>? metadata = null, IProgress<long>? progress = null);
		Task UploadDataAsync(string bucketName, string key, byte[] data, Dictionary<string, string>? metadata = null);
		Task DeleteObjectAsync(string bucketName, string key);
		Task DeleteObjectsRecursiveAsync(string bucketName, string keyPrefix);
		Task<string> DownloadObjectAsync(string bucketName, string key, FileStream? fileStream = null, IProgress<long>? progress = null);
		void UpdateCredentials(string accessKeyId, string secretAccessKey, string serviceUrl);
		int GetRetryQueueCount();
	}
}