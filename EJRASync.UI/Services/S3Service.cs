using Amazon.S3;
using Amazon.S3.Model;
using EJRASync.UI.Models;
using System.IO;

namespace EJRASync.UI.Services {
	public class S3Service : IS3Service {
		private readonly IAmazonS3 _s3Client;

		public S3Service(IAmazonS3 s3Client) {
			_s3Client = s3Client;
		}

		public async Task<List<RemoteFileItem>> ListObjectsAsync(string bucketName, string prefix = "") {
			var items = new List<RemoteFileItem>();
			string? continuationToken = null;

			do {
				var request = new ListObjectsV2Request {
					BucketName = bucketName,
					Prefix = prefix,
					Delimiter = "/",
					ContinuationToken = continuationToken
				};

				var response = await _s3Client.ListObjectsV2Async(request);

				// Add directories
				if (response.CommonPrefixes != null) {
					foreach (var commonPrefix in response.CommonPrefixes) {
						var name = commonPrefix.TrimEnd('/');
						if (name.Contains('/'))
							name = name.Substring(name.LastIndexOf('/') + 1);

						items.Add(new RemoteFileItem {
							Name = name,
							Key = commonPrefix,
							IsDirectory = true,
							DisplaySize = "Folder",
							LastModified = DateTime.MinValue
						});
					}
				}

				// Add files
				if (response.S3Objects != null) {
					foreach (var obj in response.S3Objects) {
						if (obj.Key.EndsWith("/")) continue; // Skip directory markers

						var name = obj.Key;
						if (name.Contains('/'))
							name = name.Substring(name.LastIndexOf('/') + 1);

						// Check if compressed by looking for metadata
						var metadata = await GetObjectMetadataAsync(bucketName, obj.Key);
						var isCompressed = metadata?.OriginalHash != null;

						items.Add(new RemoteFileItem {
							Name = name,
							Key = obj.Key,
							DisplaySize = FormatFileSize(obj.Size ?? 0),
							LastModified = obj.LastModified ?? DateTime.MinValue,
							SizeBytes = obj.Size ?? 0,
							IsDirectory = false,
							IsCompressed = isCompressed,
							ETag = obj.ETag ?? "",
							OriginalHash = metadata?.OriginalHash
						});
					}
				}

				continuationToken = response.NextContinuationToken;
			} while (continuationToken != null);

			return items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name).ToList();
		}

		public async Task<List<string>> ListBucketsAsync() {
			var response = await _s3Client.ListBucketsAsync();
			return response.Buckets.Select(b => b.BucketName).ToList();
		}

		public async Task<RemoteFileItem?> GetObjectMetadataAsync(string bucketName, string key) {
			try {
				var request = new GetObjectMetadataRequest {
					BucketName = bucketName,
					Key = key
				};

				var response = await _s3Client.GetObjectMetadataAsync(request);

				var name = key.Contains('/') ? key.Substring(key.LastIndexOf('/') + 1) : key;
				var originalHash = response.Metadata.Keys.Contains("x-amz-meta-original-hash")
					? response.Metadata["x-amz-meta-original-hash"]
					: null;

				return new RemoteFileItem {
					Name = name,
					Key = key,
					DisplaySize = FormatFileSize(response.ContentLength),
					LastModified = response.LastModified ?? DateTime.MinValue,
					SizeBytes = response.ContentLength,
					IsDirectory = false,
					IsCompressed = originalHash != null,
					ETag = response.ETag ?? "",
					OriginalHash = originalHash
				};
			} catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) {
				return null;
			}
		}

		public async Task UploadFileAsync(string bucketName, string key, string localFilePath, Dictionary<string, string>? metadata = null, IProgress<long>? progress = null) {
			var request = new PutObjectRequest {
				BucketName = bucketName,
				Key = key,
				FilePath = localFilePath,
				ContentType = GetContentType(localFilePath)
			};

			if (metadata != null) {
				foreach (var kvp in metadata) {
					request.Metadata.Add(kvp.Key, kvp.Value);
				}
			}

			if (progress != null) {
				request.StreamTransferProgress += (sender, args) => progress.Report(args.TransferredBytes);
			}

			await _s3Client.PutObjectAsync(request);
		}

		public async Task UploadDataAsync(string bucketName, string key, byte[] data, Dictionary<string, string>? metadata = null) {
			using var stream = new MemoryStream(data);
			var request = new PutObjectRequest {
				BucketName = bucketName,
				Key = key,
				InputStream = stream,
				ContentType = GetContentType(key)
			};

			if (metadata != null) {
				foreach (var kvp in metadata) {
					request.Metadata.Add(kvp.Key, kvp.Value);
				}
			}

			await _s3Client.PutObjectAsync(request);
		}

		public async Task DeleteObjectAsync(string bucketName, string key) {
			var request = new DeleteObjectRequest {
				BucketName = bucketName,
				Key = key
			};

			await _s3Client.DeleteObjectAsync(request);
		}

		public async Task<byte[]> DownloadObjectAsync(string bucketName, string key) {
			var request = new GetObjectRequest {
				BucketName = bucketName,
				Key = key
			};

			using var response = await _s3Client.GetObjectAsync(request);
			using var memoryStream = new MemoryStream();
			await response.ResponseStream.CopyToAsync(memoryStream);
			return memoryStream.ToArray();
		}

		private string FormatFileSize(long bytes) {
			if (bytes == 0) return "0 B";

			string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
			int suffixIndex = 0;
			double size = bytes;

			while (size >= 1024 && suffixIndex < suffixes.Length - 1) {
				size /= 1024;
				suffixIndex++;
			}

			return $"{size:F1} {suffixes[suffixIndex]}";
		}

		private string GetContentType(string fileName) {
			var extension = Path.GetExtension(fileName).ToLowerInvariant();
			return extension switch {
				".jpg" or ".jpeg" => "image/jpeg",
				".png" => "image/png",
				".gif" => "image/gif",
				".dds" => "image/vnd-ms.dds",
				".ini" => "text/plain",
				".txt" => "text/plain",
				".yaml" or ".yml" => "text/yaml",
				".json" => "application/json",
				_ => "application/octet-stream"
			};
		}
	}
}