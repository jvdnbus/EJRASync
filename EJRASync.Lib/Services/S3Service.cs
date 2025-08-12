using Amazon.S3;
using Amazon.S3.Model;
using EJRASync.Lib.Models;
using System.Collections.Concurrent;
using System.Net;

namespace EJRASync.Lib.Services {
	public class S3Service : IS3Service {
		private IAmazonS3 _s3Client;
		private readonly IHashStoreService _hashStoreService;
		private readonly ConcurrentQueue<UploadRetryItem> _retryQueue = new();
		private readonly Timer _retryTimer;
		private readonly object _retryLock = new();

		public S3Service(IAmazonS3 s3Client, IHashStoreService hashStoreService) {
			_s3Client = s3Client;
			_hashStoreService = hashStoreService;
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

		public async Task<List<RemoteFile>> ListObjectsAsync(string bucketName, string prefix = "", string delimiter = "/", CancellationToken cancellationToken = default) {
			var items = new List<RemoteFile>();

			var request = new ListObjectsV2Request {
				BucketName = bucketName,
				Prefix = prefix,
				Delimiter = delimiter,
				MaxKeys = 1000
			};

			var paginator = _s3Client.Paginators.ListObjectsV2(request);

			await foreach (var response in paginator.Responses.WithCancellation(cancellationToken)) {
				// Add directories
				if (response.CommonPrefixes != null) {
					foreach (var commonPrefix in response.CommonPrefixes) {
						var name = commonPrefix.TrimEnd('/');
						if (name.Contains('/'))
							name = name.Substring(name.LastIndexOf('/') + 1);

						items.Add(new RemoteFile {
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

						// Check if compressed by looking in hash store
						var originalHash = _hashStoreService.GetOriginalHash(bucketName, obj.Key);
						var isCompressed = originalHash != null;

						items.Add(new RemoteFile {
							Name = name,
							Key = obj.Key,
							DisplaySize = FormatFileSize(obj.Size ?? 0),
							LastModified = obj.LastModified ?? DateTime.MinValue,
							SizeBytes = obj.Size ?? 0,
							IsDirectory = false,
							IsCompressed = isCompressed,
							ETag = obj.ETag ?? "",
							OriginalHash = originalHash
						});
					}
				}
			}

			return items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name).ToList();
		}

		public async Task<List<string>> ListBucketsAsync() {
			var response = await _s3Client.ListBucketsAsync();
			return response.Buckets.Select(b => b.BucketName).ToList();
		}

		public async Task<RemoteFile?> GetObjectMetadataAsync(string bucketName, string key) {
			try {
				var request = new GetObjectMetadataRequest {
					BucketName = bucketName,
					Key = key
				};

				var response = await _s3Client.GetObjectMetadataAsync(request);

				var name = key.Contains('/') ? key.Substring(key.LastIndexOf('/') + 1) : key;
				var originalHash = _hashStoreService.GetOriginalHash(bucketName, key);

				return new RemoteFile {
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

		public async Task<string?> GetObjectOriginalHashFromMetadataAsync(string bucketName, string key) {
			try {
				var request = new GetObjectMetadataRequest {
					BucketName = bucketName,
					Key = key
				};

				var response = await _s3Client.GetObjectMetadataAsync(request);

				// Check for original hash in metadata (for rebuild purposes, bypass hash store)
				return response.Metadata.Keys.Contains("x-amz-meta-original-hash")
					? response.Metadata["x-amz-meta-original-hash"]
					: null;
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

				// If this is a compressed file with original hash, add it to hash store
				if (metadata.TryGetValue("original-hash", out var originalHash)) {
					_hashStoreService.SetOriginalHash(bucketName, key, originalHash);
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

				// If this is a compressed file with original hash, add it to hash store
				if (metadata.TryGetValue("original-hash", out var originalHash)) {
					_hashStoreService.SetOriginalHash(bucketName, key, originalHash);
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

			// Remove from hash store if it exists
			_hashStoreService.RemoveHash(bucketName, key);
		}

		public async Task DeleteObjectsRecursiveAsync(string bucketName, string keyPrefix) {
			var objectsToDelete = new List<string>();

			var request = new ListObjectsV2Request {
				BucketName = bucketName,
				Prefix = keyPrefix
			};

			var paginator = _s3Client.Paginators.ListObjectsV2(request);

			// List all objects with the prefix recursively (no delimiter to get all nested objects)
			await foreach (var response in paginator.Responses) {
				// Add all object keys to our deletion list
				if (response.S3Objects != null) {
					foreach (var obj in response.S3Objects) {
						objectsToDelete.Add(obj.Key);
					}
				}
			}

			// If no objects to delete, return early
			if (!objectsToDelete.Any()) {
				return;
			}

			// Process deletions in batches of 1000 (AWS limit)
			const int batchSize = 1000;
			for (int i = 0; i < objectsToDelete.Count; i += batchSize) {
				var batch = objectsToDelete.Skip(i).Take(batchSize).ToList();

				var deleteRequest = new DeleteObjectsRequest {
					BucketName = bucketName,
					Objects = batch.Select(key => new KeyVersion { Key = key }).ToList()
				};

				await _s3Client.DeleteObjectsAsync(deleteRequest);

				// Remove from hash store
				foreach (var key in batch) {
					_hashStoreService.RemoveHash(bucketName, key);
				}
			}
		}

		public async Task<string> DownloadObjectAsync(string bucketName, string key, FileStream? fileStream = null, IProgress<long>? progress = null) {
			var request = new GetObjectRequest {
				BucketName = bucketName,
				Key = key
			};

			using var response = await _s3Client.GetObjectAsync(request);

			if (fileStream != null) {
				using var responseStream = response.ResponseStream;
				await CopyStreamWithProgressAsync(responseStream, fileStream, response.ContentLength, progress);
				await fileStream.FlushAsync();
				return fileStream.Name;
			} else {
				var tempFilePath = Path.GetTempFileName();
				using (var tempFileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write)) {
					using var responseStream = response.ResponseStream;
					await CopyStreamWithProgressAsync(responseStream, tempFileStream, response.ContentLength, progress);
					await tempFileStream.FlushAsync();
				}
				return tempFilePath;
			}
		}

		private async Task CopyStreamWithProgressAsync(Stream source, Stream destination, long totalBytes, IProgress<long>? progress) {
			if (progress == null) {
				await source.CopyToAsync(destination);
				return;
			}

			const int bufferSize = 8192;
			var buffer = new byte[bufferSize];
			long totalBytesRead = 0;

			int bytesRead;
			while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0) {
				await destination.WriteAsync(buffer, 0, bytesRead);
				totalBytesRead += bytesRead;
				progress.Report(totalBytesRead);
			}
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