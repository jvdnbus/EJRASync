using CommunityToolkit.Mvvm.Input;
using EJRASync.Lib.Services;
using EJRASync.Lib.Utils;
using EJRASync.UI.Models;
using EJRASync.UI.Services;
using EJRASync.UI.Utils;
using log4net;
using System.IO;

namespace EJRASync.UI.ViewModels {
	public partial class RemoteFileListViewModel : BaseFileListViewModel<RemoteFileItem> {
		private static readonly ILog _logger = Lib.LoggingHelper.GetLogger(typeof(RemoteFileListViewModel));
		private readonly IS3Service _s3Service;
		private readonly IHashStoreService _hashStoreService;
		private readonly IContentStatusService _contentStatusService;
		private readonly MainWindowViewModel _mainViewModel;

		private const string BucketString = "Bucket";
		private const string ParentDirectoryName = "..";
		private const string FolderDisplaySize = "Folder";
		private const string BucketListDisplaySize = "Bucket List";

		public RemoteFileListViewModel(
			IS3Service s3Service,
			IHashStoreService hashStoreService,
			IContentStatusService contentStatusService,
			MainWindowViewModel mainViewModel) : base() {
			_s3Service = s3Service;
			_hashStoreService = hashStoreService;
			_contentStatusService = contentStatusService;
			_mainViewModel = mainViewModel;

			// Subscribe to content status changes to update file colors
			_contentStatusService.ContentStatusChanged += OnContentStatusChanged;
		}

		protected override string GetFileName(RemoteFileItem file) {
			return file.Name;
		}

		public async Task LoadBucketsAsync() {
			await this.InvokeUIAsync(() => {
				IsLoading = true;
				CurrentPath = "/";
				_allFiles.Clear();
			});

			try {
				var buckets = _mainViewModel.NavigationContext.AvailableBuckets;

				await this.InvokeUIAsync(() => {
					foreach (var bucket in buckets) {
						_allFiles.Add(new RemoteFileItem {
							Name = bucket,
							Key = bucket,
							DisplaySize = BucketString,
							IsDirectory = true,
							LastModified = DateTime.MinValue
						});
					}

					FilterFiles(); // Apply current filter
					_mainViewModel.NavigationContext.SelectedBucket = null;
					_mainViewModel.NavigationContext.RemoteCurrentPath = "";

					// Update pending changes preview for bucket list
					UpdatePendingChangesPreview(_mainViewModel.PendingChanges);
				});
			} finally {
				await this.InvokeUIAsync(() => {
					IsLoading = false;
				});
			}
		}

		public async Task LoadFilesAsync(string bucketName, string prefix = "") {
			await this.InvokeUIAsync(() => {
				IsLoading = true;
				CurrentPath = "/" + PathUtils.NormalizePath(string.IsNullOrEmpty(prefix) ? bucketName : $"{bucketName}/{prefix}");
			});

			try {
				// Initialize hash store for this bucket
				await _hashStoreService.InitializeBucketAsync(bucketName);

				prefix = PathUtils.NormalizePath(prefix);
				var files = await _s3Service.ListObjectsAsync(bucketName, prefix);

				await this.InvokeUIAsync(() => {
					_allFiles.Clear();

					// Add parent navigation
					if (!string.IsNullOrEmpty(prefix)) {
						// Navigate back within bucket
						var parentPrefix = GetParentPrefix(prefix);
						_allFiles.Add(new RemoteFileItem {
							Name = ParentDirectoryName,
							Key = ParentDirectoryName,
							DisplaySize = FolderDisplaySize,
							IsDirectory = true,
							LastModified = DateTime.MinValue
						});
					} else if (!string.IsNullOrEmpty(bucketName)) {
						// Navigate back to bucket list
						_allFiles.Add(new RemoteFileItem {
							Name = ParentDirectoryName,
							Key = ParentDirectoryName,
							DisplaySize = BucketListDisplaySize,
							IsDirectory = true,
							LastModified = DateTime.MinValue
						});
					}

					// Add files and populate activity status
					foreach (var libFile in files) {
						var file = RemoteFileItem.FromLib(libFile);
						// Only apply coloring to root-level directories in ejra-cars and ejra-tracks buckets, excluding .zstd
						if (file.IsDirectory &&
								(bucketName == EJRASync.Lib.Constants.CarsBucketName
									|| bucketName == EJRASync.Lib.Constants.TracksBucketName)
								&& string.IsNullOrEmpty(prefix) && file.Name != HashStoreService.HASH_STORE_DIR) {
							file.IsActive = _contentStatusService.IsContentActive(bucketName, file.Name);
						}

						_allFiles.Add(file);
					}

					FilterFiles(); // Apply current filter
					_mainViewModel.NavigationContext.SelectedBucket = bucketName;
					_mainViewModel.NavigationContext.RemoteCurrentPath = prefix;

					// Update pending changes preview for new directory
					UpdatePendingChangesPreview(_mainViewModel.PendingChanges);
				});
			} catch (Exception ex) {
				_logger.Error($"Error loading files from {bucketName}/{prefix}: {ex.Message}", ex);
			} finally {
				await this.InvokeUIAsync(() => {
					IsLoading = false;
				});
			}
		}

		[RelayCommand]
		private async Task NavigateToAsync(RemoteFileItem? file) {
			if (file == null || !file.IsDirectory)
				return;

			// Prevent navigation into .zstd directory (but allow viewing it)
			// if (file.Key == HashStoreService.HASH_STORE_DIR) {
			// 	return;
			// }

			_ = Task.Run(async () => {
				try {
					await this.InvokeUIAsync(() => {
						_mainViewModel.StatusMessage = "Loading...";
					});

					if (file.Key == ParentDirectoryName) {
						// Handle back navigation
						if (string.IsNullOrEmpty(_mainViewModel.NavigationContext.SelectedBucket)) {
							// Already at root
							return;
						} else if (string.IsNullOrEmpty(_mainViewModel.NavigationContext.RemoteCurrentPath)) {
							// Go back to bucket list
							await LoadBucketsAsync();
						} else {
							// Go back to parent directory within bucket
							var parentPrefix = GetParentPrefix(_mainViewModel.NavigationContext.RemoteCurrentPath);
							await LoadFilesAsync(_mainViewModel.NavigationContext.SelectedBucket, parentPrefix);
						}
					} else if (_mainViewModel.NavigationContext.AvailableBuckets.Contains(file.Name)) {
						// Navigate into bucket (file.Name is the bucket name when at root level)
						await LoadFilesAsync(file.Name);
					} else {
						// Navigate into directory within bucket
						var newPrefix = string.IsNullOrEmpty(_mainViewModel.NavigationContext.RemoteCurrentPath)
							? file.Key
							: $"{_mainViewModel.NavigationContext.RemoteCurrentPath}/{file.Name}/";

						await LoadFilesAsync(_mainViewModel.NavigationContext.SelectedBucket!, newPrefix);
					}

					await this.InvokeUIAsync(() => {
						_mainViewModel.StatusMessage = "Ready";
					});
				} catch (Exception ex) {
					await this.InvokeUIAsync(() => {
						_mainViewModel.StatusMessage = $"Navigation failed: {ex.Message}";
					});
				}
			});
		}

		[RelayCommand(CanExecute = nameof(CanNavigateUp))]
		private async Task NavigateUpAsync() {
			if (string.IsNullOrEmpty(_mainViewModel.NavigationContext.SelectedBucket)) {
				// Already at root showing buckets
				return;
			} else if (string.IsNullOrEmpty(_mainViewModel.NavigationContext.RemoteCurrentPath)) {
				// Go back to bucket list
				await LoadBucketsAsync();
			} else {
				// Go back to parent directory within bucket
				var parentPrefix = GetParentPrefix(_mainViewModel.NavigationContext.RemoteCurrentPath);
				await LoadFilesAsync(_mainViewModel.NavigationContext.SelectedBucket!, parentPrefix);
			}
		}

		private bool CanNavigateUp() {
			// Can navigate up if:
			// 1. We're inside a bucket (SelectedBucket is not null), OR
			// 2. We're in a subdirectory within a bucket (RemoteCurrentPath is not empty)
			return !string.IsNullOrEmpty(_mainViewModel.NavigationContext.SelectedBucket) ||
				   !string.IsNullOrEmpty(_mainViewModel.NavigationContext.RemoteCurrentPath);
		}

		[RelayCommand]
		public async Task RefreshAsync() {
			if (string.IsNullOrEmpty(_mainViewModel.NavigationContext.SelectedBucket)) {
				// Refresh bucket list
				await LoadBucketsAsync();
			} else {
				// Refresh current directory
				await LoadFilesAsync(_mainViewModel.NavigationContext.SelectedBucket,
					_mainViewModel.NavigationContext.RemoteCurrentPath ?? "");
			}
		}

		[RelayCommand]
		private async Task TagAsActiveAsync(RemoteFileItem? file) {
			var filesToProcess = SelectedFiles.Count > 0
				? SelectedFiles.Where(f => f.IsDirectory && !f.Key.StartsWith(HashStoreService.HASH_STORE_DIR)).ToList()
				: (file != null && file.IsDirectory && !file.Key.StartsWith(HashStoreService.HASH_STORE_DIR)
					? new List<RemoteFileItem> { file }
					: new List<RemoteFileItem>());

			if (!filesToProcess.Any() || string.IsNullOrEmpty(_mainViewModel.NavigationContext.SelectedBucket))
				return;

			var bucketName = _mainViewModel.NavigationContext.SelectedBucket;
			if (bucketName != Lib.Constants.CarsBucketName && bucketName != Lib.Constants.TracksBucketName)
				return;

			foreach (var item in filesToProcess) {
				var isCurrentlyActive = item.IsActive ?? false;
				await _contentStatusService.SetContentActiveAsync(bucketName, item.Name, !isCurrentlyActive);
			}

			// Add pending change to update YAML
			var yamlFileName = bucketName == Lib.Constants.CarsBucketName ? Lib.Constants.CarsYamlFile : Lib.Constants.TracksYamlFile;

			var change = new PendingChange {
				Type = ChangeType.UpdateYaml,
				Description = $"Update {yamlFileName}",
				RemoteKey = yamlFileName,
				BucketName = bucketName
			};

			_mainViewModel.AddPendingChange(change);
		}

		private string GetParentPrefix(string prefix) {
			if (string.IsNullOrEmpty(prefix))
				return "";

			// Remove trailing slash if present
			var cleanPrefix = prefix.TrimEnd('/');

			// If after removing trailing slash we have an empty string, we're at bucket root
			if (string.IsNullOrEmpty(cleanPrefix))
				return "";

			var lastSlash = cleanPrefix.LastIndexOf('/');
			return lastSlash > 0 ? cleanPrefix.Substring(0, lastSlash + 1) : "";
		}

		private void OnContentStatusChanged(object? sender, ContentStatusChangedEventArgs e) {
			// Update the IsActive property for the affected file
			var file = Files.FirstOrDefault(f => f.Name == e.ContentName);
			if (file != null) {
				file.IsActive = e.IsActive;
			}
		}

		public void UpdatePendingChangesPreview(IEnumerable<PendingChange> pendingChanges) {
			_ = Task.Run(async () => {
				await this.InvokeUIAsync(() => {
					var currentBucket = _mainViewModel.NavigationContext.SelectedBucket;
					var currentPath = _mainViewModel.NavigationContext.RemoteCurrentPath ?? "";

					if (string.IsNullOrEmpty(currentBucket)) return;

					// Clear existing pending change status and remove preview-only items
					var itemsToRemove = new List<RemoteFileItem>();
					foreach (var file in _allFiles) {
						if (file.IsPendingChange) {
							if (file.IsPreviewOnly) {
								// Remove preview-only items (like new folder previews)
								itemsToRemove.Add(file);
							} else {
								// Just clear the pending status for existing items
								file.IsPendingChange = false;
								file.Status = "";
							}
						}
					}

					// Remove preview-only items from both collections
					foreach (var item in itemsToRemove) {
						_allFiles.Remove(item);
						_filteredFiles.Remove(item);
					}

					// Find pending changes that affect current directory
					var relevantChanges = pendingChanges.Where(change =>
						change.BucketName == currentBucket &&
						IsChangeRelevantToCurrentDirectory(change, currentPath)).ToList();

					// Group changes by the name they will show in current directory to avoid duplicates
					var changesByFileName = relevantChanges.GroupBy(change => GetFileNameFromChange(change, currentPath));

					foreach (var changeGroup in changesByFileName) {
						var fileName = changeGroup.Key;
						var existingFile = _allFiles.FirstOrDefault(f => f.Name == fileName);
						var firstChange = changeGroup.First();

						if (existingFile != null) {
							// Update existing file/directory - these get "Pending" status
							existingFile.IsPendingChange = true;
							existingFile.Status = GetStatusFromChangeType(firstChange.Type);
						} else if (firstChange.Type == ChangeType.CompressAndUpload || firstChange.Type == ChangeType.RawUpload) {
							// Determine if this should be a directory or file based on the change
							var changeKey = PathUtils.NormalizePath(firstChange.RemoteKey);
							var isDirectory = string.IsNullOrEmpty(currentPath) && changeKey.Contains('/');

							// Add new file/folder preview for uploads - these get "To be added" status
							var newFile = new RemoteFileItem {
								Name = fileName,
								Key = fileName,
								DisplaySize = isDirectory ? FolderDisplaySize : (firstChange.FileSizeBytes?.ToString() ?? "Unknown"),
								LastModified = DateTime.Now,
								IsDirectory = isDirectory,
								IsPendingChange = true,
								IsPreviewOnly = true,
								Status = "(To be added)"
							};
							_allFiles.Add(newFile);
							if (string.IsNullOrWhiteSpace(SearchText) || newFile.Name.ToLowerInvariant().Contains(SearchText.ToLowerInvariant())) {
								_filteredFiles.Add(newFile);
							}
						}
					}
				});
			});
		}

		private bool IsChangeRelevantToCurrentDirectory(PendingChange change, string currentPath) {
			var changeKey = PathUtils.NormalizePath(change.RemoteKey);

			// If we're at the bucket root (empty currentPath)
			if (string.IsNullOrEmpty(currentPath)) {
				// Always show changes - either files at root or the top-level folders of nested files
				return true;
			}

			// For subdirectories, check if the change is directly in the current path
			var changeDirectory = PathUtils.NormalizePath(Path.GetDirectoryName(changeKey) ?? "");
			return changeDirectory == currentPath;
		}

		private string GetFileNameFromChange(PendingChange change, string currentPath) {
			var changeKey = PathUtils.NormalizePath(change.RemoteKey);

			if (string.IsNullOrEmpty(currentPath)) {
				// At bucket root - if the key has a path, return the first folder name
				if (changeKey.Contains('/')) {
					return changeKey.Split('/')[0];
				}
				return changeKey;
			}

			// In subdirectory - return just the filename
			return Path.GetFileName(changeKey);
		}

		private string GetStatusFromChangeType(ChangeType changeType) {
			return changeType switch {
				ChangeType.DeleteRemote => "(To be removed)",
				_ => "(Pending)"
			};
		}

		public bool CanTagAsActive(RemoteFileItem? file) {
			if (file == null || !file.IsDirectory)
				return false;

			var bucketName = _mainViewModel.NavigationContext.SelectedBucket;
			return bucketName == EJRASync.Lib.Constants.CarsBucketName || bucketName == EJRASync.Lib.Constants.TracksBucketName;
		}

		public string GetTagActiveText(RemoteFileItem? file) {
			if (file?.IsActive == true)
				return "Tag as inactive";
			else
				return "Tag as active";
		}

		[RelayCommand]
		private void DeleteRemoteFile(RemoteFileItem? file) {
			var filesToProcess = SelectedFiles.Count > 0
				? SelectedFiles.Where(f => f.Name != ParentDirectoryName && !f.Key.StartsWith(HashStoreService.HASH_STORE_DIR)).ToList()
				: (file != null && file.Name != ParentDirectoryName && !file.Key.StartsWith(HashStoreService.HASH_STORE_DIR)
					? new List<RemoteFileItem> { file }
					: new List<RemoteFileItem>());

			if (!filesToProcess.Any() || string.IsNullOrEmpty(_mainViewModel.NavigationContext.SelectedBucket))
				return;

			var bucketName = _mainViewModel.NavigationContext.SelectedBucket;

			foreach (var item in filesToProcess) {
				var remoteKey = PathUtils.NormalizePath(item.Key);

				var change = new PendingChange {
					Type = ChangeType.DeleteRemote,
					Description = $"Delete {item.Name}",
					RemoteKey = remoteKey,
					BucketName = bucketName
				};

				_mainViewModel.AddPendingChange(change);
			}
		}

		[RelayCommand]
		private async Task DownloadRemoteFile(RemoteFileItem? file) {
			if (file == null || file.IsDirectory || string.IsNullOrEmpty(_mainViewModel.NavigationContext.SelectedBucket))
				return;

			var bucketName = _mainViewModel.NavigationContext.SelectedBucket;

			// The file.Key already contains the full S3 path, so we don't need to prepend the current path
			var remoteKey = file.Key;

			// Let the main window handle the actual download with file dialog
			await _mainViewModel.HandleRemoteFileDownload(bucketName, remoteKey, file.Name);
		}

		[RelayCommand]
		private async Task ViewRemoteFile(RemoteFileItem? file) {
			if (file == null || file.IsDirectory || string.IsNullOrEmpty(_mainViewModel.NavigationContext.SelectedBucket))
				return;

			var bucketName = _mainViewModel.NavigationContext.SelectedBucket;

			// The file.Key already contains the full S3 path, so we don't need to prepend the current path
			var remoteKey = file.Key;

			// Let the main window handle the download and view
			await _mainViewModel.HandleRemoteFileView(bucketName, remoteKey, file.Name);
		}

	}
}
