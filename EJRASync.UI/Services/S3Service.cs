using Amazon.S3;
using Amazon.S3.Model;
using EJRASync.UI.Models;
using System.IO;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;

namespace EJRASync.UI.Services {
	public class S3Service : IS3Service {
		private IAmazonS3 _s3Client;
		private readonly ConcurrentQueue<UploadRetryItem> _retryQueue = new();
		private readonly Timer _retryTimer;
		private readonly object _retryLock = new();

		public S3Service(IAmazonS3 s3Client) {
			_s3Client = s3Client;
			_retryTimer = new Timer(ProcessRetryQueue, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
		}

		private class UploadRetryItem {
			public string BucketName { get; set; } = string.Empty;
			public string Key { get; set; } = string.Empty;
			public string? LocalFilePath { get; set; }
			public byte[]? Data { get; set; }
			public Dictionary<string, string>? Metadata { get; set; }
			public int RetryCount { get; set; } = 0;
			public DateTime NextRetryTime { get; set; }
			public Exception LastException { get; set; } = null!;
			public TaskCompletionSource<bool> TaskCompletion { get; set; } = new();
		}

		private async void ProcessRetryQueue(object? state) {
			if (!Monitor.TryEnter(_retryLock)) return;

			try {
				var itemsToRetry = new List<UploadRetryItem>();
				
				// Collect items ready for retry
				while (_retryQueue.TryPeek(out var item)) {
					if (DateTime.UtcNow >= item.NextRetryTime) {
						if (_retryQueue.TryDequeue(out item)) {
							itemsToRetry.Add(item);
						}
					} else {
						break; // Items are ordered by NextRetryTime
					}
				}

				// Process retry items
				foreach (var item in itemsToRetry) {
					try {
						if (item.LocalFilePath != null) {
							await UploadFileInternalAsync(item.BucketName, item.Key, item.LocalFilePath, item.Metadata);
						} else if (item.Data != null) {
							await UploadDataInternalAsync(item.BucketName, item.Key, item.Data, item.Metadata);
						}
						
						item.TaskCompletion.SetResult(true);
					} catch (Exception ex) {
						item.RetryCount++;
						item.LastException = ex;
						
						if (item.RetryCount >= 5) {
							// Max retries exceeded
							item.TaskCompletion.SetException(new Exception($"Upload failed after 5 retries: {ex.Message}", ex));
						} else {
							// Schedule next retry with exponential backoff
							var delay = TimeSpan.FromSeconds(Math.Pow(2, item.RetryCount) * 30);
							item.NextRetryTime = DateTime.UtcNow.Add(delay);
							_retryQueue.Enqueue(item);
						}
					}
				}
			} catch (Exception ex) {
				SentrySdk.CaptureException(ex);
			} finally {
				Monitor.Exit(_retryLock);
			}
		}

		private bool ShouldRetry(Exception ex) {
			return ex is AmazonS3Exception s3Ex && (
				s3Ex.StatusCode == HttpStatusCode.RequestTimeout ||
				s3Ex.StatusCode == HttpStatusCode.InternalServerError ||
				s3Ex.StatusCode == HttpStatusCode.BadGateway ||
				s3Ex.StatusCode == HttpStatusCode.ServiceUnavailable ||
				s3Ex.StatusCode == HttpStatusCode.GatewayTimeout
			) || ex is HttpRequestException || ex is TaskCanceledException || ex is TimeoutException;
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
			try {
				await UploadFileInternalAsync(bucketName, key, localFilePath, metadata, progress);
			} catch (Exception ex) {
				if (ShouldRetry(ex)) {
					var retryItem = new UploadRetryItem {
						BucketName = bucketName,
						Key = key,
						LocalFilePath = localFilePath,
						Metadata = metadata,
						NextRetryTime = DateTime.UtcNow.AddSeconds(30), // First retry after 30 seconds
						LastException = ex
					};
					
					_retryQueue.Enqueue(retryItem);
					
					// Return the task that will complete when the retry succeeds or fails
					await retryItem.TaskCompletion.Task;
				} else {
					throw;
				}
			}
		}

		private async Task UploadFileInternalAsync(string bucketName, string key, string localFilePath, Dictionary<string, string>? metadata = null, IProgress<long>? progress = null) {
			var request = new PutObjectRequest {
				BucketName = bucketName,
				Key = key,
				FilePath = localFilePath,
				ContentType = GetContentType(localFilePath),
				DisablePayloadSigning = true,
				DisableDefaultChecksumValidation = true
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
			try {
				await UploadDataInternalAsync(bucketName, key, data, metadata);
			} catch (Exception ex) {
				if (ShouldRetry(ex)) {
					var retryItem = new UploadRetryItem {
						BucketName = bucketName,
						Key = key,
						Data = data,
						Metadata = metadata,
						NextRetryTime = DateTime.UtcNow.AddSeconds(30), // First retry after 30 seconds
						LastException = ex
					};
					
					_retryQueue.Enqueue(retryItem);
					
					// Return the task that will complete when the retry succeeds or fails
					await retryItem.TaskCompletion.Task;
				} else {
					throw;
				}
			}
		}

		private async Task UploadDataInternalAsync(string bucketName, string key, byte[] data, Dictionary<string, string>? metadata = null) {
			using var stream = new MemoryStream(data);
			var request = new PutObjectRequest {
				BucketName = bucketName,
				Key = key,
				InputStream = stream,
				ContentType = GetContentType(key),
				DisablePayloadSigning = true,
				DisableDefaultChecksumValidation = true
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

		public void UpdateCredentials(string accessKeyId, string secretAccessKey, string serviceUrl) {
			lock (_retryLock) {
				// Dispose the old client
				_s3Client?.Dispose();
				
				// Create new client with updated credentials
				var config = new AmazonS3Config {
					ServiceURL = serviceUrl,
					ForcePathStyle = true,
				};
				_s3Client = new AmazonS3Client(accessKeyId, secretAccessKey, config);
			}
		}

		public int GetRetryQueueCount() => _retryQueue.Count;

		public void Dispose() {
			_retryTimer?.Dispose();
			_s3Client?.Dispose();
		}
	}
}