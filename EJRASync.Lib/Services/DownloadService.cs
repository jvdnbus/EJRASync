using EJRASync.Lib.Models;
using System.Collections.Concurrent;

namespace EJRASync.Lib.Services {
	public class DownloadService : IDownloadService {
		private readonly IS3Service _s3Service;
		private readonly IFileService _fileService;
		private readonly ICompressionService _compressionService;
		private readonly SemaphoreSlim _downloadSemaphore;
		private const int MaxConcurrentDownloads = 8;
		private const int MaxRetries = 3;

		public DownloadService(IS3Service s3Service, IFileService fileService, ICompressionService compressionService) {
			_s3Service = s3Service;
			_fileService = fileService;
			_compressionService = compressionService;
			_downloadSemaphore = new SemaphoreSlim(MaxConcurrentDownloads, MaxConcurrentDownloads);
		}

		public async Task<List<RemoteFile>> GetFilesToDownloadAsync(string bucketName, string remotePrefix, string localBasePath) {
			// Get all remote files using prefix (recursively)
			var remoteFiles = await _s3Service.ListObjectsAsync(bucketName, remotePrefix, "", CancellationToken.None);

			// Filter to only files (not directories) and exclude .zstd system files
			var filesToCheck = remoteFiles.Where(f => !f.IsDirectory && !f.Key.StartsWith(HashStoreService.HASH_STORE_DIR)).ToList();

			var filesToDownload = new List<RemoteFile>();

			foreach (var remoteFile in filesToCheck) {
				var localPath = Path.Combine(localBasePath, remoteFile.Key.Replace('/', Path.DirectorySeparatorChar));

				// Check if file needs updating
				if (await ShouldDownloadFileAsync(remoteFile, localPath)) {
					filesToDownload.Add(remoteFile);
				}
			}

			return filesToDownload;
		}

		private async Task<bool> ShouldDownloadFileAsync(RemoteFile remoteFile, string localPath) {
			if (!File.Exists(localPath)) {
				return true; // File doesn't exist locally
			}

			// If remote file has original hash metadata, validate against it
			if (!string.IsNullOrEmpty(remoteFile.OriginalHash)) {
				var localHash = await _fileService.CalculateFileHashAsync(localPath);
				return localHash != remoteFile.OriginalHash;
			}

			// Fallback: compare file size and last modified date
			var localInfo = new FileInfo(localPath);
			return localInfo.Length != remoteFile.SizeBytes ||
				   localInfo.LastWriteTime < remoteFile.LastModified;
		}

		public async Task DownloadFilesAsync(List<RemoteFile> filesToDownload, string bucketName, string localBasePath, IProgress<DownloadProgress>? progress = null) {
			var progressData = new DownloadProgress {
				TotalFiles = filesToDownload.Count,
				TotalBytes = filesToDownload.Sum(f => f.SizeBytes)
			};

			var activeDownloads = new ConcurrentDictionary<string, FileProgress>();
			var completedFiles = 0;
			long completedBytes = 0;

			var downloadTasks = filesToDownload.Select(async file => {
				await _downloadSemaphore.WaitAsync();
				try {
					var fileProgress = new FileProgress {
						FileName = file.Name,
						TotalBytes = file.SizeBytes
					};

					activeDownloads.TryAdd(file.Key, fileProgress);

					// Update progress with active downloads
					UpdateProgress();

					await DownloadFileWithRetryAsync(file, bucketName, localBasePath, fileProgress, UpdateProgress);

					// Mark as completed
					Interlocked.Increment(ref completedFiles);
					Interlocked.Add(ref completedBytes, file.SizeBytes);
					activeDownloads.TryRemove(file.Key, out _);

					UpdateProgress();
				} finally {
					_downloadSemaphore.Release();
				}
			});

			void UpdateProgress() {
				if (progress == null) return;

				progressData.CompletedFiles = completedFiles;
				progressData.CompletedBytes = completedBytes;
				progressData.ActiveDownloads = activeDownloads.Values.ToList();
				progress.Report(progressData);
			}

			await Task.WhenAll(downloadTasks);
		}

		private async Task DownloadFileWithRetryAsync(RemoteFile file, string bucketName, string localBasePath, FileProgress fileProgress, Action updateProgress) {
			var localPath = Path.Combine(localBasePath, file.Key.Replace('/', Path.DirectorySeparatorChar));
			var localDir = Path.GetDirectoryName(localPath);

			if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir)) {
				Directory.CreateDirectory(localDir);
			}

			Exception? lastException = null;

			for (int retry = 0; retry <= MaxRetries; retry++) {
				try {
					// Download progress callback
					var downloadProgress = new Progress<long>(bytesDownloaded => {
						fileProgress.CompletedBytes = bytesDownloaded;
						updateProgress();
					});

					// Download file to temporary location first
					var tempFilePath = await _s3Service.DownloadObjectAsync(bucketName, file.Key, null, downloadProgress);

					try {
						if (file.IsCompressed && !string.IsNullOrEmpty(file.OriginalHash)) {
							// File is compressed - decompress it
							fileProgress.IsDecompressing = true;
							updateProgress();

							await _compressionService.DecompressFileAsync(tempFilePath, localPath);

							// Validate decompressed file hash
							if (await ValidateFileAsync(localPath, file.OriginalHash)) {
								File.SetLastWriteTime(localPath, file.LastModified);
								return; // Success
							} else {
								throw new InvalidDataException($"Hash validation failed for decompressed file: {file.Name}");
							}
						} else {
							// File is not compressed or no hash available - move directly
							if (File.Exists(localPath)) {
								File.Delete(localPath);
							}
							File.Move(tempFilePath, localPath);
							File.SetLastWriteTime(localPath, file.LastModified);
							// Success!
							return;
						}
					} finally {
						// Clean up temp file
						if (File.Exists(tempFilePath)) {
							try { File.Delete(tempFilePath); } catch { }
						}
					}
				} catch (Exception ex) {
					lastException = ex;

					if (retry < MaxRetries) {
						// Exponential backoff: wait 2^retry seconds
						var delay = TimeSpan.FromSeconds(Math.Pow(2, retry));
						await Task.Delay(delay);
					}
				}
			}

			// If we get here, all retries failed
			throw new Exception($"Failed to download {file.Name} after {MaxRetries + 1} attempts", lastException);
		}

		public async Task<bool> ValidateFileAsync(string localFilePath, string expectedHash) {
			if (string.IsNullOrEmpty(expectedHash)) {
				return true; // No hash to validate against
			}

			try {
				var actualHash = await _fileService.CalculateFileHashAsync(localFilePath);
				return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
			} catch {
				return false;
			}
		}
	}
}