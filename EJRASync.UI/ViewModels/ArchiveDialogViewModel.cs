using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EJRASync.Lib;
using EJRASync.Lib.Models;
using EJRASync.Lib.Services;
using EJRASync.Lib.Utils;
using EJRASync.UI.Models;
using EJRASync.UI.Utils;
using log4net;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace EJRASync.UI.ViewModels {
	public partial class ArchiveDialogViewModel : ObservableObject {
		private static readonly ILog _logger = LoggingHelper.GetLogger(typeof(ArchiveDialogViewModel));

		private readonly IS3Service _s3Service;
		private readonly IFileService _fileService;
		private readonly IEjraApiService _apiService;
		private readonly IDownloadService _downloadService;
		private readonly OAuthToken? _oauthToken;
		private CancellationTokenSource? _cancellationTokenSource;
		private readonly List<ArchiveMetadataItem> _completedArchiveMetadata = new();

		[ObservableProperty]
		private ObservableCollection<ArchiveItem> _cars = new();

		[ObservableProperty]
		private ObservableCollection<ArchiveItem> _tracks = new();

		[ObservableProperty]
		private bool _areAllCarsSelected = false;

		[ObservableProperty]
		private bool _areAllTracksSelected = false;

		[ObservableProperty]
		private int _selectedCount = 0;

		[ObservableProperty]
		private string _statusMessage = "Ready";

		[ObservableProperty]
		private double _progressValue = 0;

		[ObservableProperty]
		private bool _isProgressIndeterminate = false;

		[ObservableProperty]
		private bool _isProcessing = false;

		[ObservableProperty]
		private bool _canStart = true;

		public string StartButtonText => IsProcessing ? "Cancel" : "Publish";

		public ArchiveDialogViewModel(IS3Service s3Service, IFileService fileService, IEjraApiService apiService, IDownloadService downloadService, OAuthToken? oauthToken) {
			_s3Service = s3Service;
			_fileService = fileService;
			_apiService = apiService;
			_downloadService = downloadService;
			_oauthToken = oauthToken;

			// Subscribe to property changes to update UI
			Cars.CollectionChanged += (s, e) => UpdateSelectionCounts();
			Tracks.CollectionChanged += (s, e) => UpdateSelectionCounts();
		}

		public async Task InitializeAsync() {
			try {
				IsProgressIndeterminate = true;
				StatusMessage = "Loading available content...";

				await LoadCarsAsync();
				await LoadTracksAsync();

				StatusMessage = "Ready";
			} catch (Exception ex) {
				StatusMessage = $"Error loading content: {ex.Message}";
				_logger.Error($"Error initializing archive dialog: {ex.Message}", ex);
			} finally {
				IsProgressIndeterminate = false;
			}
		}

		private async Task LoadCarsAsync() {
			try {
				StatusMessage = "Loading cars...";
				var carObjects = await _s3Service.ListObjectsAsync(Constants.CarsBucketName, cancellationToken: CancellationToken.None);

				_logger.Info($"Found {carObjects.Count} car objects");

				var carDirectories = carObjects
					.Where(obj => obj.IsDirectory && !obj.Key.StartsWith(HashStoreService.HASH_STORE_DIR))
					.Select(obj => obj.Name)
					.OrderBy(name => name)
					.ToList();

				_logger.Info($"Found {carDirectories.Count} car directories");

				await this.InvokeUIAsync(() => {
					Cars.Clear();
					foreach (var carDir in carDirectories) {
						var carItem = new ArchiveItem(carDir, Constants.CarsBucketName);
						carItem.PropertyChanged += (s, e) => {
							if (e.PropertyName == nameof(ArchiveItem.IsSelected)) {
								UpdateSelectionCounts();
								UpdateSelectAllStates();
							}
						};
						Cars.Add(carItem);
					}
				});

				StatusMessage = $"Loaded {Cars.Count} cars";
			} catch (Exception ex) {
				StatusMessage = $"Error loading cars: {ex.Message}";
				_logger.Error($"Error loading cars: {ex.Message}", ex);
			}
		}

		private async Task LoadTracksAsync() {
			try {
				StatusMessage = "Loading tracks...";
				var trackObjects = await _s3Service.ListObjectsAsync(Constants.TracksBucketName, cancellationToken: CancellationToken.None);

				_logger.Info($"Found {trackObjects.Count} track objects");

				var trackDirectories = trackObjects
					.Where(obj => obj.IsDirectory && !obj.Key.StartsWith(HashStoreService.HASH_STORE_DIR))
					.Select(obj => obj.Name)
					.OrderBy(name => name)
					.ToList();

				_logger.Info($"Found {trackDirectories.Count} track directories");

				await this.InvokeUIAsync(() => {
					Tracks.Clear();
					foreach (var trackDir in trackDirectories) {
						var trackItem = new ArchiveItem(trackDir, Constants.TracksBucketName);
						trackItem.PropertyChanged += (s, e) => {
							if (e.PropertyName == nameof(ArchiveItem.IsSelected)) {
								UpdateSelectionCounts();
								UpdateSelectAllStates();
							}
						};
						Tracks.Add(trackItem);
					}
				});

				StatusMessage = $"Loaded {Tracks.Count} tracks";
			} catch (Exception ex) {
				StatusMessage = $"Error loading tracks: {ex.Message}";
				_logger.Error($"Error loading tracks: {ex.Message}", ex);
			}
		}

		[RelayCommand]
		private void ToggleAllCars() {
			var newState = !Cars.All(c => c.IsSelected);
			foreach (var car in Cars) {
				car.IsSelected = newState;
			}
			UpdateSelectAllStates();
		}

		[RelayCommand]
		private void ToggleAllTracks() {
			var newState = !Tracks.All(t => t.IsSelected);
			foreach (var track in Tracks) {
				track.IsSelected = newState;
			}
			UpdateSelectAllStates();
		}

		private void UpdateSelectionCounts() {
			SelectedCount = Cars.Count(c => c.IsSelected) + Tracks.Count(t => t.IsSelected);
			OnPropertyChanged(nameof(StartButtonText));
		}

		private void UpdateSelectAllStates() {
			AreAllCarsSelected = Cars.Any() && Cars.All(c => c.IsSelected);
			AreAllTracksSelected = Tracks.Any() && Tracks.All(t => t.IsSelected);
		}

		private async Task StartAsync() {
			var selectedItems = Cars.Where(c => c.IsSelected)
								   .Concat(Tracks.Where(t => t.IsSelected))
								   .ToList();

			if (selectedItems.Count == 0) {
				StatusMessage = "Please select at least one item to archive";
				return;
			}

			try {
				await this.InvokeUIAsync(() => {
					IsProcessing = true;
					IsProgressIndeterminate = false;
					ProgressValue = 0;
					OnPropertyChanged(nameof(StartButtonText));
				});

				_cancellationTokenSource = new CancellationTokenSource();
				_completedArchiveMetadata.Clear(); // Clear any previous metadata

				// First, check disk space
				await ValidateDiskSpaceAsync(selectedItems, _cancellationTokenSource.Token);

				// Process each selected item with proper progress tracking
				var totalItems = selectedItems.Count;
				var processedItems = 0;

				foreach (var item in selectedItems) {
					_cancellationTokenSource.Token.ThrowIfCancellationRequested();

					var itemBaseProgress = (double)processedItems / totalItems * 100;
					var itemProgressRange = 100.0 / totalItems;

					try {
						await ProcessArchiveItemAsync(item, _cancellationTokenSource.Token, itemBaseProgress, itemProgressRange);
						processedItems++;
					} catch (Exception ex) {
						_logger.Error($"Error processing item {item.Name}: {ex.Message}", ex);
						// Continue processing other items even if one fails
					}

					ProgressValue = (double)(processedItems) / totalItems * 100;
				}

				StatusMessage = processedItems == totalItems
					? $"Archive completed successfully! Processed {processedItems} items."
					: $"Archive completed with errors. Processed {processedItems}/{totalItems} items.";
				ProgressValue = 100;

				// Clear all selections after completion
				await this.InvokeUIAsync(() => {
					foreach (var car in Cars) {
						car.IsSelected = false;
					}
					foreach (var track in Tracks) {
						track.IsSelected = false;
					}
					UpdateSelectionCounts();
					UpdateSelectAllStates();
				});

			} catch (OperationCanceledException) {
				StatusMessage = "Archive operation cancelled";
				ProgressValue = 0;
			} catch (Exception ex) {
				StatusMessage = $"Error during archive: {ex.Message}";
				ProgressValue = 0;
				_logger.Error($"Error during archive operation: {ex.Message}", ex);

				MessageBox.Show(
					$"An error occurred during the archive process:\n\n{ex.Message}",
					"Archive Error",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			} finally {
				// Always try to submit metadata if we have any completed items
				if (_completedArchiveMetadata.Count > 0) {
					try {
						StatusMessage = "Submitting archive metadata...";
						await SubmitArchiveMetadataAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);
					} catch (Exception ex) {
						_logger.Error($"Error submitting metadata: {ex.Message}", ex);
						// Don't throw - just log the error
					}
				}

				IsProcessing = false;
				OnPropertyChanged(nameof(StartButtonText));
				_cancellationTokenSource?.Dispose();
				_cancellationTokenSource = null;
			}
		}

		private async Task ValidateDiskSpaceAsync(List<ArchiveItem> selectedItems, CancellationToken cancellationToken) {
			StatusMessage = "Validating disk space...";
			IsProgressIndeterminate = true;

			try {
				long totalEstimatedSize = 0;

				foreach (var item in selectedItems) {
					cancellationToken.ThrowIfCancellationRequested();

					// Get all files in this directory to estimate size
					var remoteFiles = await _s3Service.ListObjectsAsync(item.BucketName, item.Name, "", cancellationToken);
					var files = remoteFiles.Where(f => !f.IsDirectory).ToList();

					totalEstimatedSize += files.Sum(f => f.SizeBytes);
				}

				// Add some buffer for compression and temporary files (50% overhead)
				var requiredSpace = (long)(totalEstimatedSize * 1.5);

				var tempPath = Path.GetTempPath();
				var drive = new DriveInfo(Path.GetPathRoot(tempPath)!);
				var availableSpace = drive.AvailableFreeSpace;

				if (availableSpace < requiredSpace) {
					var requiredGB = Math.Round(requiredSpace / (1024.0 * 1024.0 * 1024.0), 2);
					var availableGB = Math.Round(availableSpace / (1024.0 * 1024.0 * 1024.0), 2);

					throw new InvalidOperationException(
						$"Insufficient disk space ({drive.Name}). Required: {requiredGB} GB, Available: {availableGB} GB");
				}

				StatusMessage = $"Disk space validated. Estimated download: {Math.Round(totalEstimatedSize / (1024.0 * 1024.0 * 1024.0), 2)} GB";
			} finally {
				IsProgressIndeterminate = false;
			}
		}

		private async Task ProcessArchiveItemAsync(ArchiveItem item, CancellationToken cancellationToken, double baseProgress, double progressRange) {
			var tempDir = Path.Combine(Path.GetTempPath(), $"ejra_archive_{Guid.NewGuid()}");
			string? zipPath = null;

			try {
				// Create temporary directory
				Directory.CreateDirectory(tempDir);

				// Step 1: Download all files for this item (40% of item progress)
				StatusMessage = $"Downloading {item.Name}...";
				var downloadBaseProgress = baseProgress;
				var downloadProgressRange = progressRange * 0.4;
				await DownloadItemFilesAsync(item, tempDir, cancellationToken, downloadBaseProgress, downloadProgressRange);

				cancellationToken.ThrowIfCancellationRequested();

				// Step 2: Create ZIP archive (20% of item progress)
				StatusMessage = $"Creating archive for {item.Name}...";
				var archiveBaseProgress = baseProgress + downloadProgressRange;
				var archiveProgressRange = progressRange * 0.2;
				zipPath = await CreateZipArchiveAsync(item, tempDir, cancellationToken, archiveBaseProgress, archiveProgressRange);

				cancellationToken.ThrowIfCancellationRequested();

				// Step 2.5: Extract display name from UI files
				StatusMessage = $"Reading metadata for {item.Name}...";
				var displayName = await ExtractDisplayNameAsync(item, tempDir);

				// Step 3: Upload to archive bucket (40% of item progress)
				StatusMessage = $"Uploading {item.Name} to mod archive...";
				var uploadBaseProgress = baseProgress + downloadProgressRange + archiveProgressRange;
				var uploadProgressRange = progressRange * 0.40;
				var (remoteKey, fileMD5, fileSize) = await UploadToModArchiveAsync(item, zipPath, cancellationToken, uploadBaseProgress, uploadProgressRange);

				// Store metadata for API submission
				var archiveMetadata = new ArchiveMetadataItem {
					Key = remoteKey,
					Md5 = fileMD5,
					FileSize = fileSize,
					DisplayName = displayName,
					UploadDate = DateTime.UtcNow.ToString("O") // ISO 8601 format
				};
				_completedArchiveMetadata.Add(archiveMetadata);

			} finally {
				// Cleanup
				try {
					if (Directory.Exists(tempDir)) {
						Directory.Delete(tempDir, true);
					}
					if (!string.IsNullOrEmpty(zipPath) && File.Exists(zipPath)) {
						File.Delete(zipPath);
					}
				} catch (Exception ex) {
					_logger.Warn($"Error cleaning up temporary files: {ex.Message}");
				}
			}
		}

		private async Task DownloadItemFilesAsync(ArchiveItem item, string tempDir, CancellationToken cancellationToken, double baseProgress, double progressRange) {
			// Get all files in this directory recursively
			var filesToDownload = await _downloadService.GetFilesToDownloadAsync(item.BucketName, item.Name, tempDir);

			if (!filesToDownload.Any()) return;

			// Create progress reporter that maps DownloadService progress to our progress range
			var progressReporter = new Progress<DownloadProgress>(downloadProgress => {
				if (downloadProgress.TotalFiles > 0) {
					var overallProgress = (double)downloadProgress.CompletedFiles / downloadProgress.TotalFiles;
					ProgressValue = baseProgress + (overallProgress * progressRange);
					StatusMessage = $"Downloading {item.Name}... ({downloadProgress.CompletedFiles}/{downloadProgress.TotalFiles})";
				}
			});

			// Use DownloadService to properly handle compressed files
			await _downloadService.DownloadFilesAsync(filesToDownload, item.BucketName, tempDir, progressReporter);
		}

		private async Task<string> CreateZipArchiveAsync(ArchiveItem item, string tempDir, CancellationToken cancellationToken, double baseProgress, double progressRange) {
			var timestamp = DateTime.UtcNow.ToString("ddMMyyyyHHmm");
			var zipFileName = $"{item.Name}-{timestamp}.zip";
			var zipPath = Path.Combine(Path.GetTempPath(), zipFileName);

			using var zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

			// Determine the content path within the archive
			string contentPath;
			if (item.BucketName == Constants.CarsBucketName) {
				contentPath = "content/cars/";
			} else if (item.BucketName == Constants.TracksBucketName) {
				contentPath = "content/tracks/";
			} else {
				contentPath = "content/";
			}

			// Add all files to the archive with correct relative paths
			var itemDir = Path.Combine(tempDir, item.Name);
			if (Directory.Exists(itemDir)) {
				await AddDirectoryToZipAsync(zipArchive, itemDir, $"{contentPath}{item.Name}/", cancellationToken, baseProgress, progressRange);
			}

			return zipPath;
		}

		private async Task AddDirectoryToZipAsync(ZipArchive zipArchive, string sourceDir, string entryPrefix, CancellationToken cancellationToken, double baseProgress, double progressRange) {
			var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
			var processedFiles = 0;
			var totalFiles = files.Length;

			foreach (var filePath in files) {
				cancellationToken.ThrowIfCancellationRequested();

				var relativePath = Path.GetRelativePath(sourceDir, filePath);
				var entryName = entryPrefix + relativePath.Replace(Path.DirectorySeparatorChar, '/');

				var entry = zipArchive.CreateEntry(entryName);
				using var entryStream = entry.Open();
				using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);

				await fileStream.CopyToAsync(entryStream, cancellationToken);

				processedFiles++;
				var fileProgress = (double)processedFiles / totalFiles;
				ProgressValue = baseProgress + (fileProgress * progressRange);
			}
		}

		private async Task<(string remoteKey, string fileMD5, long fileSize)> UploadToModArchiveAsync(ArchiveItem item, string zipPath, CancellationToken cancellationToken, double baseProgress, double progressRange) {
			var archiveBucket = "ejra-mod-archive";
			string keyPrefix;

			if (item.BucketName == Constants.CarsBucketName) {
				keyPrefix = "cars/";
			} else if (item.BucketName == Constants.TracksBucketName) {
				keyPrefix = "tracks/";
			} else {
				keyPrefix = "other/";
			}

			var fileName = Path.GetFileName(zipPath);
			var remoteKey = keyPrefix + fileName;

			// Calculate file MD5 and get file size
			var archiveFileInfo = new FileInfo(zipPath);
			var fileSize = archiveFileInfo.Length;
			string fileMD5;

			using (var md5 = MD5.Create())
			using (var stream = File.OpenRead(zipPath)) {
				var hashBytes = await md5.ComputeHashAsync(stream, cancellationToken);
				fileMD5 = Convert.ToHexString(hashBytes).ToLowerInvariant();
			}

			// Retry logic for network failures
			var maxRetries = 3;
			var baseDelay = TimeSpan.FromSeconds(2);

			for (int attempt = 1; attempt <= maxRetries; attempt++) {
				try {
					cancellationToken.ThrowIfCancellationRequested();

					if (attempt > 1) {
						StatusMessage = $"Retrying upload for {item.Name} (attempt {attempt}/{maxRetries})...";
						await Task.Delay(TimeSpan.FromSeconds(baseDelay.TotalSeconds * attempt), cancellationToken);
					}

					// Upload with progress reporting
					var fileInfo = new FileInfo(zipPath);
					var progressReporter = new Progress<long>(bytesUploaded => {
						if (fileInfo.Length > 0) {
							var uploadProgress = (double)bytesUploaded / fileInfo.Length;
							ProgressValue = baseProgress + (uploadProgress * progressRange);
							var percentage = uploadProgress * 100;
							StatusMessage = $"Uploading {item.Name}... {percentage:F1}% ({FileSizeFormatter.FormatFileSize(bytesUploaded)} / {FileSizeFormatter.FormatFileSize(fileInfo.Length)})";
						}
					});

					// Use direct S3 upload with automatic multipart handling for large files (>50MB)
					if (fileInfo.Length > 50 * 1024 * 1024) {
						await _s3Service.UploadLargeFileAsync(archiveBucket, remoteKey, zipPath, progressReporter, cancellationToken);
						_logger.Info($"Successfully uploaded large file {fileName} to {archiveBucket}/{remoteKey} using multipart upload");
					} else {
						await _s3Service.UploadFileAsync(archiveBucket, remoteKey, zipPath, null, progressReporter, cancellationToken);
						_logger.Info($"Successfully uploaded {fileName} to {archiveBucket}/{remoteKey} using standard upload");
					}
					return (remoteKey, fileMD5, fileSize);
				} catch (OperationCanceledException) {
					throw;
				} catch (Exception ex) when (attempt < maxRetries) {
					_logger.Warn($"Upload attempt {attempt} failed for {fileName}: {ex.Message}");

					// Check if it's a network-related error that might benefit from retry
					if (IsNetworkError(ex)) {
						continue;
					} else {
						throw;
					}
				} catch (Exception ex) {
					_logger.Error($"Final upload attempt failed for {fileName}: {ex.Message}", ex);
					throw;
				}
			}

			throw new InvalidOperationException("Upload failed after all retry attempts");
		}


		private static bool IsNetworkError(Exception ex) {
			// Check for common network-related exceptions that might be transient
			return ex.Message.Contains("connection was forcibly closed") ||
				   ex.Message.Contains("timeout") ||
				   ex.Message.Contains("network") ||
				   ex.Message.Contains("connection reset") ||
				   ex.Message.Contains("Unable to write data to the transport connection") ||
				   ex is System.Net.Sockets.SocketException ||
				   ex is System.Net.WebException ||
				   ex is HttpRequestException;
		}

		private async Task<string> ExtractDisplayNameAsync(ArchiveItem item, string tempDir) {
			try {
				string uiFilePath;

				if (item.BucketName == Constants.CarsBucketName) {
					// For cars: look for ui/ui_car.json
					uiFilePath = Path.Combine(tempDir, item.Name, "ui", "ui_car.json");
				} else if (item.BucketName == Constants.TracksBucketName) {
					// For tracks: look for ui/*ui_track.json (find the first match)
					var uiDir = Path.Combine(tempDir, item.Name, "ui");
					if (Directory.Exists(uiDir)) {
						var uiTrackFiles = Directory.GetFiles(uiDir, "*ui_track.json", SearchOption.AllDirectories);
						uiFilePath = uiTrackFiles.FirstOrDefault() ?? "";
					} else {
						uiFilePath = "";
					}
				} else {
					// Unknown bucket type, return the item name as fallback
					return item.Name;
				}

				if (string.IsNullOrEmpty(uiFilePath) || !File.Exists(uiFilePath)) {
					return item.Name; // Fallback to item name
				}

				var jsonContent = await File.ReadAllTextAsync(uiFilePath);
				using var jsonDoc = JsonDocument.Parse(jsonContent);

				// Try to find the "name" property
				if (jsonDoc.RootElement.TryGetProperty("name", out var nameElement)) {
					var displayName = nameElement.GetString();
					if (!string.IsNullOrEmpty(displayName)) {
						return displayName;
					}
				}

				return item.Name; // Fallback to item name if no "name" property found
			} catch (Exception ex) {
				_logger.Warn($"Error extracting display name for {item.Name}: {ex.Message}");
				return item.Name; // Fallback to item name on error
			}
		}

		private async Task SubmitArchiveMetadataAsync(CancellationToken cancellationToken) {
			try {
				// Get the API token - we need the user's access token for authentication
				if (string.IsNullOrEmpty(_oauthToken?.UserProfile?.Properties.AccessToken)) {
					_logger.Warn("No API token available for metadata submission");
					StatusMessage = "Warning: Could not submit metadata - not authenticated";
					return;
				}

				var response = await _apiService.SubmitArchiveMetadataAsync(
					_completedArchiveMetadata,
					_oauthToken,
					cancellationToken);

				if (response?.Success == true) {
					_logger.Info($"Successfully submitted metadata for {response.Stored} archives");
					StatusMessage = $"Metadata submitted successfully for {response.Stored} archives";
				} else {
					_logger.Warn("Failed to submit archive metadata - no response or unsuccessful");
					StatusMessage = "Warning: Archive metadata submission failed";
				}

			} catch (Exception ex) {
				_logger.Error($"Error submitting archive metadata: {ex.Message}", ex);
				StatusMessage = $"Warning: Archive metadata submission failed: {ex.Message}";
				// Don't throw - this shouldn't fail the entire operation
			}
		}

		[RelayCommand(CanExecute = nameof(CanStart))]
		private async Task Start() {
			if (IsProcessing) {
				_cancellationTokenSource?.Cancel();
				StatusMessage = "Cancelling...";
				return;
			}

			await StartAsync();
		}

		partial void OnIsProcessingChanged(bool value) {
			UpdateCanStart();
			OnPropertyChanged(nameof(StartButtonText));
			_logger.Debug($"OnIsProcessingChanged: IsProcessing={value}, SelectedCount={SelectedCount}, CanStart={CanStart}");
		}

		partial void OnSelectedCountChanged(int value) {
			UpdateCanStart();
			_logger.Debug($"OnSelectedCountChanged: IsProcessing={IsProcessing}, SelectedCount={value}, CanStart={CanStart}");
		}

		private void UpdateCanStart() {
			// Button should always be enabled when processing (for cancel)
			// Or when items are selected and not processing (for publish)
			CanStart = IsProcessing || SelectedCount > 0;
		}
	}
}