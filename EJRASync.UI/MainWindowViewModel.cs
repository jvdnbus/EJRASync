using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EJRASync.Lib;
using EJRASync.Lib.Services;
using EJRASync.Lib.Utils;
using EJRASync.UI.Models;
using EJRASync.UI.Services;
using EJRASync.UI.ViewModels;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace EJRASync.UI {
	public partial class MainWindowViewModel : ObservableObject {
		private readonly IS3Service _s3Service;
		private readonly IHashStoreService _hashStoreService;
		private readonly IFileService _fileService;
		private readonly IContentStatusService _contentStatusService;
		private readonly ICompressionService _compressionService;
		private readonly IEjraAuthApiService _authApi;
		private readonly IEjraAuthService _authService;

		[ObservableProperty]
		private ObservableCollection<PendingChange> _pendingChanges = new();

		[ObservableProperty]
		private NavigationContext _navigationContext = new();

		[ObservableProperty]
		private string _statusMessage = "Ready";

		[ObservableProperty]
		private double _progressValue = 0;

		[ObservableProperty]
		private bool _isProgressIndeterminate = false;

		[ObservableProperty]
		private bool _isScanning = false;

		[ObservableProperty]
		private bool _isApplying = false;

		[ObservableProperty]
		private bool _isCancelling = false;

		[ObservableProperty]
		private bool _hasWriteAccess = false;

		[ObservableProperty]
		private bool _isLoggedIn = false;

		[ObservableProperty]
		private string? _userDisplayName;

		[ObservableProperty]
		private string? _userProfileImageUrl;

		[ObservableProperty]
		private string? _userProviderName;

		[ObservableProperty]
		private bool _isUpdateAvailable = false;

		[ObservableProperty]
		private bool _isDownloadingUpdate = false;

		private OAuthToken? _currentOAuthToken;
		private Views.PendingChangesDialog? _pendingChangesDialog;
		private CancellationTokenSource? _operationCancellationTokenSource;
		private AutoUpdater? _autoUpdater;
		private GitHubRelease? _availableUpdate;

		public LocalFileListViewModel LocalFiles { get; }
		public RemoteFileListViewModel RemoteFiles { get; }

		public MainWindowViewModel(
			IS3Service s3Service,
			IHashStoreService hashStoreService,
			IFileService fileService,
			IContentStatusService contentStatusService,
			ICompressionService compressionService,
			IEjraAuthApiService authApi,
			IEjraAuthService authService) {
			_s3Service = s3Service;
			_hashStoreService = hashStoreService;
			_fileService = fileService;
			_contentStatusService = contentStatusService;
			_compressionService = compressionService;
			_authApi = authApi;
			_authService = authService;

			LocalFiles = new LocalFileListViewModel(_fileService, this);
			RemoteFiles = new RemoteFileListViewModel(_s3Service, _hashStoreService, _contentStatusService, this);

			// Subscribe to NavigationContext changes for command updates
			NavigationContext.PropertyChanged += (s, e) => {
				if (e.PropertyName == nameof(NavigationContext.SelectedBucket)) {
					RebuildHashStoreCommand.NotifyCanExecuteChanged();
				}
			};

			// Set up default Assetto Corsa path
			var steamPath = EJRASync.Lib.SteamHelper.FindSteam();
			var acPath = EJRASync.Lib.SteamHelper.FindAssettoCorsa(steamPath);
			NavigationContext.LocalBasePath = acPath;
			NavigationContext.LocalCurrentPath = PathUtils.NormalizePath(Path.Combine(acPath, "content"));

			// Initialize auto updater
			_autoUpdater = new AutoUpdater(@$"{AppContext.BaseDirectory}\{Constants.GuiExecutableName}", LogMessage, UpdateProgress);
		}

		public async Task InitializeAsync() {
			await _contentStatusService.InitializeAsync();
			await LocalFiles.LoadFilesAsync(NavigationContext.LocalCurrentPath);

			// Try auto-login with saved token
			await TryAutoLoginAsync();

			// Check for updates on startup
			_ = Task.Run(CheckForUpdatesAsync);
		}

		private async Task TryAutoLoginAsync() {
			try {
				var savedToken = await _authService.LoadSavedTokenAsync();
				if (savedToken != null && !savedToken.IsExpired()) {
					await LoginWithTokenAsync(savedToken, "Auto-login successful!");
					// Don't call CheckWriteAccessAsync after successful auto-login
					return;
				} else {
					// No valid saved token, check for basic read access
					await CheckWriteAccessAsync();
				}
			} catch (Exception ex) {
				// If auto-login fails, just check for basic access
				await CheckWriteAccessAsync();
			}
		}

		private async Task CheckWriteAccessAsync() {
			try {
				var tokens = await _authApi.GetTokensAsync();
				HasWriteAccess = tokens?.UserWrite != null;
			} catch {
				HasWriteAccess = false;
			}
		}

		[RelayCommand(CanExecute = nameof(CanScanChanges))]
		private async Task ScanChangesAsync() {
			IsScanning = true;
			StatusMessage = "Scanning for changes...";
			ProgressValue = 0;

			_operationCancellationTokenSource = new CancellationTokenSource();
			CancelOperationCommand.NotifyCanExecuteChanged();
			ScanChangesCommand.NotifyCanExecuteChanged();

			try {
				PendingChanges.Clear();
				OnPropertyChanged(nameof(ViewChangesButtonText));
				ViewChangesCommand.NotifyCanExecuteChanged();
				DiscardChangesCommand.NotifyCanExecuteChanged();
				ApplyChangesCommand.NotifyCanExecuteChanged();

				// Update dialog title if it's open
				UpdatePendingChangesDialogTitle();

				var buckets = new[] { EJRASync.Lib.Constants.CarsBucketName, EJRASync.Lib.Constants.TracksBucketName, EJRASync.Lib.Constants.FontsBucketName, EJRASync.Lib.Constants.AppsBucketName };
				var localFolders = new[] { "content/cars", "content/tracks", "content/fonts", "apps" };

				for (int i = 0; i < buckets.Length; i++) {
					_operationCancellationTokenSource.Token.ThrowIfCancellationRequested();

					var bucket = buckets[i];
					var localFolder = localFolders[i];
					var localPath = Path.Combine(NavigationContext.LocalBasePath, localFolder);

					if (Directory.Exists(localPath)) {
						await ScanBucketChangesAsync(bucket, localPath, _operationCancellationTokenSource.Token);
					}

					if (!IsCancelling) {
						ProgressValue = (i + 1) * 100.0 / buckets.Length;
					}
				}

				if (!IsCancelling) {
					StatusMessage = $"Found {PendingChanges.Count} changes";

					// Update UI after scanning is complete
					OnPropertyChanged(nameof(ViewChangesButtonText));
					ViewChangesCommand.NotifyCanExecuteChanged();
					DiscardChangesCommand.NotifyCanExecuteChanged();
					ApplyChangesCommand.NotifyCanExecuteChanged();

					// Update remote file list preview
					RemoteFiles.UpdatePendingChangesPreview(PendingChanges);
				}
			} catch (OperationCanceledException) {
				StatusMessage = "Scan cancelled";
			} catch (Exception ex) {
				if (!IsCancelling) {
					StatusMessage = $"Error scanning: {ex.Message}";
				}
			} finally {
				IsScanning = false;
				IsCancelling = false;
				IsProgressIndeterminate = false;
				ProgressValue = 0;
				_operationCancellationTokenSource?.Dispose();
				_operationCancellationTokenSource = null;
				CancelOperationCommand.NotifyCanExecuteChanged();
				ScanChangesCommand.NotifyCanExecuteChanged();
			}
		}

		private async Task ScanBucketChangesAsync(string bucketName, string localPath, CancellationToken cancellationToken = default) {
			// Use indeterminate progress for unpredictable bucket scanning work
			IsProgressIndeterminate = true;
			StatusMessage = $"Scanning {bucketName}...";

			try {
				cancellationToken.ThrowIfCancellationRequested();

				// First, get the list of existing remote directories (top-level folders in the bucket)
				var remoteItems = await _s3Service.ListObjectsAsync(bucketName, cancellationToken: cancellationToken);
				var remoteDirs = remoteItems
					.Where(item => item.IsDirectory && !item.Key.StartsWith(HashStoreService.HASH_STORE_DIR))
					.Select(dir => dir.Name)
					.ToList();

				// Collect all files from all remote directories and their corresponding local files
				var allRemoteFiles = new List<RemoteFileItem>();
				var allLocalFiles = new List<LocalFileItem>();

				foreach (var remoteDir in remoteDirs) {
					cancellationToken.ThrowIfCancellationRequested();

					var localDirPath = Path.Combine(localPath, remoteDir);

					// Update status to show current directory being scanned
					if (!IsCancelling) {
						StatusMessage = $"Scanning {bucketName}/{remoteDir}...";
					}

					// Only scan if the corresponding local directory exists
					if (Directory.Exists(localDirPath)) {
						// Get all files in this remote directory recursively (no delimiter to get nested files)
						var remoteDirFiles = await _s3Service.ListObjectsAsync(bucketName, remoteDir, "", cancellationToken);
						allRemoteFiles
							.AddRange(remoteDirFiles.Where(f => !f.IsDirectory && !f.Key.StartsWith(HashStoreService.HASH_STORE_DIR))
							.Select(RemoteFileItem.FromLib));

						// Get all local files in this directory recursively
						var localDirFiles = await _fileService.GetLocalFilesAsync(localDirPath, true);
						allLocalFiles
							.AddRange(localDirFiles.Where(f => !f.IsDirectory)
							.Select(LocalFileItem.FromLib));
					}
				}

				cancellationToken.ThrowIfCancellationRequested();

				// Update status for file comparison phase
				if (!IsCancelling) {
					StatusMessage = $"Comparing {allLocalFiles.Count} files in {bucketName}...";
				}

				// Now batch process all collected files in a non-recursive manner
				await ProcessFileComparisonBatch(bucketName, localPath, allLocalFiles, allRemoteFiles, cancellationToken);
			} finally {
				// Reset indeterminate progress (parent method will handle final cleanup)
				IsProgressIndeterminate = false;
			}
		}

		private async Task ProcessFileComparisonBatch(string bucketName, string basePath, List<LocalFileItem> localFiles, List<RemoteFileItem> remoteFiles, CancellationToken cancellationToken = default) {
			// Create a dictionary for faster remote file lookup by key
			var remoteFilesByKey = remoteFiles.ToDictionary(r => r.Key, r => r);

			// Process each local file against the remote files
			foreach (var localFile in localFiles) {
				cancellationToken.ThrowIfCancellationRequested();

				var relativePath = Path.GetRelativePath(basePath, localFile.FullPath).Replace(Path.DirectorySeparatorChar, '/');

				if (remoteFilesByKey.TryGetValue(relativePath, out var remoteFile)) {
					// File exists - check if changed
					var localHash = await _fileService.CalculateFileHashAsync(localFile.FullPath);
					var remoteOriginalHash = remoteFile.OriginalHash;

					if (remoteOriginalHash == null || localHash != remoteOriginalHash) {
						AddUploadChange(bucketName, basePath, localFile, relativePath);
					}
				} else {
					// File doesn't exist remotely - needs upload
					AddUploadChange(bucketName, basePath, localFile, relativePath);
				}
			}
		}

		private void AddUploadChange(string bucketName, string basePath, LocalFileItem localFile, string remoteKey) {
			var shouldCompress = _compressionService.ShouldCompress(localFile.Name, localFile.SizeBytes);
			var changeType = shouldCompress ? ChangeType.CompressAndUpload : ChangeType.RawUpload;
			var description = Path.GetRelativePath(basePath, localFile.FullPath).Replace(Path.DirectorySeparatorChar, '/');

			PendingChanges.Add(new PendingChange {
				Type = changeType,
				Description = description,
				LocalPath = localFile.FullPath,
				RemoteKey = remoteKey,
				BucketName = bucketName,
				FileSizeBytes = localFile.SizeBytes
			});

			// Update UI commands whenever changes are added
			OnPropertyChanged(nameof(ViewChangesButtonText));
			ViewChangesCommand.NotifyCanExecuteChanged();
			DiscardChangesCommand.NotifyCanExecuteChanged();
			ApplyChangesCommand.NotifyCanExecuteChanged();
		}

		private bool CanScanChanges() => !IsScanning && !IsApplying && !IsCancelling;

		[RelayCommand(CanExecute = nameof(CanViewChanges))]
		private void ViewChanges() {
			// If dialog is already open, just bring it to front
			if (_pendingChangesDialog != null) {
				_pendingChangesDialog.Activate();
				_pendingChangesDialog.Focus();
				return;
			}

			// Create new dialog instance
			_pendingChangesDialog = new Views.PendingChangesDialog(PendingChanges);
			_pendingChangesDialog.Owner = Application.Current.MainWindow;

			// Handle when dialog is closed to clear the reference
			_pendingChangesDialog.Closed += (s, e) => _pendingChangesDialog = null;

			_pendingChangesDialog.Show();
		}

		private bool CanViewChanges() => PendingChanges.Count > 0;

		[RelayCommand(CanExecute = nameof(CanDiscardChanges))]
		private void DiscardChanges() {
			PendingChanges.Clear();
			OnPropertyChanged(nameof(ViewChangesButtonText));
			ViewChangesCommand.NotifyCanExecuteChanged();
			DiscardChangesCommand.NotifyCanExecuteChanged();
			ApplyChangesCommand.NotifyCanExecuteChanged();

			// Update dialog title if it's open
			UpdatePendingChangesDialogTitle();

			// Clear remote file list preview
			RemoteFiles.UpdatePendingChangesPreview(PendingChanges);

			StatusMessage = "Changes discarded";
		}

		private bool CanDiscardChanges() => PendingChanges.Count > 0 && !IsApplying && HasWriteAccess;

		[RelayCommand(CanExecute = nameof(CanApplyChanges))]
		private async Task ApplyChangesAsync() {
			IsApplying = true;
			StatusMessage = "Applying changes...";
			ProgressValue = 0;

			_operationCancellationTokenSource = new CancellationTokenSource();
			CancelOperationCommand.NotifyCanExecuteChanged();
			DiscardChangesCommand.NotifyCanExecuteChanged();

			try {
				var totalChanges = PendingChanges.Count;
				var processedChanges = 0;

				var parallelOptions = new ParallelOptions {
					MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
					CancellationToken = _operationCancellationTokenSource.Token
				};

				await Parallel.ForEachAsync(PendingChanges, parallelOptions, async (change, ct) => {
					try {
						ct.ThrowIfCancellationRequested();
						await ProcessChangeAsync(change, ct);
					} catch (OperationCanceledException) {
						// Cancel
					} catch (Exception ex) {
						Console.WriteLine($"Error processing {change.Description}: {ex.Message}");
					} finally {
						Interlocked.Increment(ref processedChanges);
						if (!IsCancelling) {
							ProgressValue = processedChanges * 100.0 / totalChanges;
							StatusMessage = $"({processedChanges}/{totalChanges}) {change.Description}...";
						}
					}
				});

				// Check for dirty hash stores and add UpdateHashStore pending changes
				var buckets = new[] { EJRASync.Lib.Constants.CarsBucketName, EJRASync.Lib.Constants.TracksBucketName,
									  EJRASync.Lib.Constants.FontsBucketName, EJRASync.Lib.Constants.AppsBucketName };

				foreach (var bucket in buckets) {
					if (_hashStoreService.IsDirty(bucket)) {
						PendingChanges.Add(new PendingChange {
							Type = ChangeType.UpdateHashStore,
							BucketName = bucket,
							Description = $"Update hash store for {bucket}"
						});
					}
				}

				// If we added hash store updates, apply them immediately
				if (PendingChanges.Any(c => c.Type == ChangeType.UpdateHashStore)) {
					var hashStoreChanges = PendingChanges.Where(c => c.Type == ChangeType.UpdateHashStore).ToList();
					foreach (var change in hashStoreChanges) {
						try {
							await ProcessUpdateHashStoreAsync(change);
							PendingChanges.Remove(change);
						} catch (Exception ex) {
							Console.WriteLine($"Error updating hash store for {change.BucketName}: {ex.Message}");
						}
					}
				}

				PendingChanges.Clear();
				OnPropertyChanged(nameof(ViewChangesButtonText));
				ViewChangesCommand.NotifyCanExecuteChanged();
				DiscardChangesCommand.NotifyCanExecuteChanged();
				ApplyChangesCommand.NotifyCanExecuteChanged();

				// Update dialog title if it's open
				UpdatePendingChangesDialogTitle();

				// Clear remote file list preview
				RemoteFiles.UpdatePendingChangesPreview(PendingChanges);

				// Refresh the current remote directory to show updated state
				if (!IsCancelling && !string.IsNullOrEmpty(NavigationContext.SelectedBucket)) {
					_ = Task.Run(async () => {
						try {
							await RemoteFiles.LoadFilesAsync(NavigationContext.SelectedBucket, NavigationContext.RemoteCurrentPath ?? "");
						} catch (Exception ex) {
							Console.WriteLine($"Error refreshing remote directory: {ex.Message}");
						}
					});
				}

				if (!IsCancelling) {
					StatusMessage = $"Successfully applied {processedChanges} changes";
				}
			} catch (OperationCanceledException) {
				StatusMessage = "Operation cancelled";
			} catch (Exception ex) {
				if (!IsCancelling) {
					StatusMessage = $"Error applying changes: {ex.Message}";
				}
			} finally {
				IsApplying = false;
				IsCancelling = false;
				IsProgressIndeterminate = false;
				ProgressValue = 0;
				_operationCancellationTokenSource?.Dispose();
				_operationCancellationTokenSource = null;
				CancelOperationCommand.NotifyCanExecuteChanged();
				DiscardChangesCommand.NotifyCanExecuteChanged();
			}
		}

		private async Task ProcessChangeAsync(PendingChange change, CancellationToken cancellationToken = default) {
			switch (change.Type) {
				case ChangeType.CompressAndUpload:
					await ProcessCompressAndUploadAsync(change, cancellationToken);
					break;
				case ChangeType.RawUpload:
					await ProcessRawUploadAsync(change, cancellationToken);
					break;
				case ChangeType.UpdateYaml:
					await ProcessYamlUpdateAsync(change, cancellationToken);
					break;
				case ChangeType.DeleteRemote:
					await ProcessDeleteRemoteAsync(change, cancellationToken);
					break;
				case ChangeType.UpdateHashStore:
					await ProcessUpdateHashStoreAsync(change, cancellationToken);
					break;
			}
		}

		private async Task ProcessCompressAndUploadAsync(PendingChange change, CancellationToken cancellationToken = default) {
			if (string.IsNullOrEmpty(change.LocalPath))
				return;

			var originalHash = await _fileService.CalculateFileHashAsync(change.LocalPath);
			cancellationToken.ThrowIfCancellationRequested();
			var tempCompressedFile = await _compressionService.CompressFileAsync(change.LocalPath);

			try {
				cancellationToken.ThrowIfCancellationRequested();

				var metadata = new Dictionary<string, string>
				{
					{ "original-hash", originalHash }
				};

				await _s3Service.UploadFileAsync(change.BucketName, change.RemoteKey, tempCompressedFile, metadata);
			} finally {
				// Clean up temporary compressed file
				if (File.Exists(tempCompressedFile)) {
					File.Delete(tempCompressedFile);
				}
			}
		}

		private async Task ProcessRawUploadAsync(PendingChange change, CancellationToken cancellationToken = default) {
			if (string.IsNullOrEmpty(change.LocalPath))
				return;

			await _s3Service.UploadFileAsync(change.BucketName, change.RemoteKey, change.LocalPath, change.Metadata);
		}

		private async Task ProcessYamlUpdateAsync(PendingChange change, CancellationToken cancellationToken = default) {
			var yamlData = await _contentStatusService.GenerateYamlAsync(change.BucketName);
			cancellationToken.ThrowIfCancellationRequested();
			await _s3Service.UploadDataAsync(change.BucketName, change.RemoteKey, yamlData);
		}

		private async Task ProcessDeleteRemoteAsync(PendingChange change, CancellationToken cancellationToken = default) {
			cancellationToken.ThrowIfCancellationRequested();

			// Check if we're deleting a folder (ends with / or is a directory prefix)
			// For folders, we need to use recursive deletion to remove all objects under that prefix
			if (change.RemoteKey.EndsWith("/")) {
				// It's a folder - use recursive deletion
				await _s3Service.DeleteObjectsRecursiveAsync(change.BucketName, change.RemoteKey);
			} else {
				// Check if this key represents a directory by looking for objects with this prefix
				var objectsWithPrefix = await _s3Service.ListObjectsAsync(change.BucketName, change.RemoteKey + "/");
				// Filter out .zstd files from the check
				objectsWithPrefix = objectsWithPrefix
					.Where(obj => !obj.Key.StartsWith(HashStoreService.HASH_STORE_DIR))
					.ToList();

				if (objectsWithPrefix.Any()) {
					// It's a folder that doesn't end with / but has child objects - use recursive deletion
					await _s3Service.DeleteObjectsRecursiveAsync(change.BucketName, change.RemoteKey + "/");
				}

				// Also delete the individual object (could be both a file and a prefix)
				await _s3Service.DeleteObjectAsync(change.BucketName, change.RemoteKey);
			}
		}

		private bool CanApplyChanges() => PendingChanges.Count > 0 && !IsApplying && HasWriteAccess;

		[RelayCommand(CanExecute = nameof(CanCancelOperation))]
		private void CancelOperation() {
			if (_operationCancellationTokenSource != null && !IsCancelling) {
				IsCancelling = true;
				IsProgressIndeterminate = true;
				StatusMessage = "Cancelling...";
				_operationCancellationTokenSource.Cancel();
			}
		}

		private bool CanCancelOperation() => (IsApplying || IsScanning) && !IsCancelling;

		[RelayCommand]
		private async Task LoginAsync() {
			try {
				// Check if we have a valid token saved
				var savedToken = await _authService.LoadSavedTokenAsync();
				if (savedToken != null && !savedToken.IsExpired()) {
					await LoginWithTokenAsync(savedToken, "Auto-login successful using saved credentials!");
					return;
				}

				var dialog = new Views.OAuthDialog(_authService);

				// Show dialog and start authentication
				var dialogTask = dialog.StartAuthenticationAsync();
				var result = dialog.ShowDialog();

				if (result == true && dialog.OAuthToken != null) {
					// Save the new token for future auto-login
					await _authService.SaveTokenAsync(dialog.OAuthToken);
					await LoginWithTokenAsync(dialog.OAuthToken, "Login successful! Checking permissions...");
				} else {
					StatusMessage = "Login cancelled or failed.";
				}
			} catch (Exception ex) {
				StatusMessage = $"Login failed: {ex.Message}";
				Sentry.SentrySdk.CaptureException(ex);
			}
		}

		private async Task LoginWithTokenAsync(OAuthToken token, string initialStatusMessage) {
			try {
				// Get write R2 tokens using the OAuth token (this can be done on background thread)
				var tokens = await _authApi.GetTokensAsync(token);

				// Update UI properties
				await Application.Current.Dispatcher.InvokeAsync(() => {
					StatusMessage = initialStatusMessage;

					// Store the OAuth token and update user info
					_currentOAuthToken = token;
					UserDisplayName = token.GetDisplayName();
					UserProfileImageUrl = token.GetProfileImageUrl();
					UserProviderName = token.GetProviderName();
					IsLoggedIn = true;

					if (tokens?.UserWrite != null) {
						HasWriteAccess = true;
						StatusMessage = "Login successful! (Admin)";

						// Update S3Service with write credentials
						_s3Service.UpdateCredentials(
							tokens.UserWrite.Aws.AccessKeyId,
							tokens.UserWrite.Aws.SecretAccessKey,
							tokens.UserWrite.S3Url
						);
					} else {
						StatusMessage = "Login successful!";
					}
				});
			} catch (Exception ex) {
				await Application.Current.Dispatcher.InvokeAsync(() => {
					StatusMessage = $"Auto-login failed: {ex.Message}";
				});
				throw; // Re-throw so TryAutoLoginAsync can handle it
			}
		}

		private async Task ProcessUpdateHashStoreAsync(PendingChange change, CancellationToken cancellationToken = default) {
			cancellationToken.ThrowIfCancellationRequested();

			// Save the hash store for this bucket
			await _hashStoreService.SaveToRemoteAsync(change.BucketName);
		}

		[RelayCommand]
		private async Task Logout() {
			// Clear the saved token
			await _authService.ClearSavedTokenAsync();

			_currentOAuthToken = null;
			UserDisplayName = null;
			UserProfileImageUrl = null;
			UserProviderName = null;
			IsLoggedIn = false;
			HasWriteAccess = false;
			StatusMessage = "Logged out successfully.";

			// Revert S3Service to read-only credentials
			try {
				var tokens = await _authApi.GetTokensAsync();
				if (tokens?.UserRead != null) {
					_s3Service.UpdateCredentials(
						tokens.UserRead.Aws.AccessKeyId,
						tokens.UserRead.Aws.SecretAccessKey,
						tokens.UserRead.S3Url
					);
				}
			} catch {
				// If we can't get read tokens, that's ok - just continue
			}
		}

		public string ViewChangesButtonText => $"View {PendingChanges.Count} changes";

		public void AddPendingChange(PendingChange change) {
			PendingChanges.Add(change);
			OnPropertyChanged(nameof(ViewChangesButtonText));
			ViewChangesCommand.NotifyCanExecuteChanged();
			DiscardChangesCommand.NotifyCanExecuteChanged();
			ApplyChangesCommand.NotifyCanExecuteChanged();

			// Update dialog title if it's open
			UpdatePendingChangesDialogTitle();

			// Update remote file list preview - this will immediately update the current view
			RemoteFiles.UpdatePendingChangesPreview(PendingChanges);
		}

		partial void OnPendingChangesChanged(ObservableCollection<PendingChange> value) {
			OnPropertyChanged(nameof(ViewChangesButtonText));
		}

		partial void OnHasWriteAccessChanged(bool value) {
			ViewChangesCommand.NotifyCanExecuteChanged();
			DiscardChangesCommand.NotifyCanExecuteChanged();
			ApplyChangesCommand.NotifyCanExecuteChanged();
			RebuildHashStoreCommand.NotifyCanExecuteChanged();
		}

		partial void OnIsApplyingChanged(bool value) {
			RebuildHashStoreCommand.NotifyCanExecuteChanged();
		}

		private void UpdatePendingChangesDialogTitle() {
			if (_pendingChangesDialog != null) {
				_pendingChangesDialog.Title = $"Pending Changes ({PendingChanges.Count} items)";
			}
		}

		[RelayCommand(CanExecute = nameof(CanRebuildHashStore))]
		private async Task RebuildHashStore() {
			if (string.IsNullOrEmpty(NavigationContext.SelectedBucket)) {
				StatusMessage = "No bucket selected for hash store rebuild";
				return;
			}

			try {
				IsApplying = true;
				StatusMessage = $"Rebuilding hash store for {NavigationContext.SelectedBucket}...";

				await _hashStoreService.RebuildHashStoreAsync(NavigationContext.SelectedBucket);

				StatusMessage = $"Hash store rebuilt successfully for {NavigationContext.SelectedBucket}";

				// Refresh the current view to show updated compression status
				if (!string.IsNullOrEmpty(NavigationContext.SelectedBucket)) {
					_ = Task.Run(async () => {
						try {
							await RemoteFiles.LoadFilesAsync(NavigationContext.SelectedBucket, NavigationContext.RemoteCurrentPath ?? "");
						} catch (Exception ex) {
							Console.WriteLine($"Error refreshing remote directory after hash store rebuild: {ex.Message}");
						}
					});
				}
			} catch (Exception ex) {
				StatusMessage = $"Error rebuilding hash store: {ex.Message}";
			} finally {
				IsApplying = false;
			}
		}

		private bool CanRebuildHashStore() {
			return HasWriteAccess && !IsApplying && !string.IsNullOrEmpty(NavigationContext.SelectedBucket);
		}

		public async Task HandleRemoteFileDownload(string bucketName, string remoteKey, string fileName) {
			try {
				// Show save dialog
				var saveFileDialog = new Microsoft.Win32.SaveFileDialog {
					FileName = fileName,
					Title = "Save file as..."
				};

				if (saveFileDialog.ShowDialog() == true) {
					StatusMessage = $"Downloading {fileName}...";

					// Download the file directly to the selected location
					using (var fileStream = new FileStream(saveFileDialog.FileName, FileMode.Create, FileAccess.Write)) {
						await _s3Service.DownloadObjectAsync(bucketName, remoteKey, fileStream);
					}

					StatusMessage = $"Downloaded {fileName} successfully";
				}
			} catch (Exception ex) {
				StatusMessage = $"Error downloading {fileName}: {ex.Message}";
			}
		}

		public async Task HandleRemoteFileView(string bucketName, string remoteKey, string fileName) {
			try {
				StatusMessage = $"Downloading {fileName} for viewing...";

				// Download to temp file
				var tempFilePath = await _s3Service.DownloadObjectAsync(bucketName, remoteKey);

				// Track temp file for cleanup
				AddTempFileForCleanup(tempFilePath);

				StatusMessage = $"Opening {fileName}...";

				// Open with default application
				var processInfo = new System.Diagnostics.ProcessStartInfo {
					FileName = tempFilePath,
					UseShellExecute = true
				};

				System.Diagnostics.Process.Start(processInfo);
				StatusMessage = "File opened successfully";
			} catch (Exception ex) {
				StatusMessage = $"Error opening {fileName}: {ex.Message}";
			}
		}

		private static readonly List<string> _tempFilesForCleanup = new();

		private void AddTempFileForCleanup(string filePath) {
			lock (_tempFilesForCleanup) {
				_tempFilesForCleanup.Add(filePath);
			}
		}

		public static void CleanupTempFiles() {
			lock (_tempFilesForCleanup) {
				foreach (var tempFile in _tempFilesForCleanup) {
					try {
						if (File.Exists(tempFile)) {
							File.Delete(tempFile);
						}
					} catch {
						// Ignore cleanup errors
					}
				}
				_tempFilesForCleanup.Clear();
			}
		}

		private async Task CheckForUpdatesAsync() {
			try {
				if (_autoUpdater != null) {
					_availableUpdate = await _autoUpdater.CheckForUpdate();
					await Application.Current.Dispatcher.InvokeAsync(() => {
						IsUpdateAvailable = _availableUpdate != null;
						UpdateAvailableCommand.NotifyCanExecuteChanged();
					});
				}
			} catch {
				// Silently fail - updates are not critical
			}
		}

		[RelayCommand(CanExecute = nameof(CanUpdateAvailable))]
		private async Task UpdateAvailable() {
			if (_availableUpdate == null || _autoUpdater == null)
				return;

			try {
				IsDownloadingUpdate = true;
				UpdateAvailableCommand.NotifyCanExecuteChanged();

				var success = await _autoUpdater.DownloadAndInstallUpdate(_availableUpdate);
				if (!success) {
					StatusMessage = "Update failed. Please try again later.";
				}
			} catch (Exception ex) {
				StatusMessage = $"Update failed: {ex.Message}";
			} finally {
				IsDownloadingUpdate = false;
				UpdateAvailableCommand.NotifyCanExecuteChanged();
			}
		}

		private bool CanUpdateAvailable() => IsUpdateAvailable && !IsDownloadingUpdate;

		private void LogMessage(string message) {
			Application.Current.Dispatcher.InvokeAsync(() => {
				StatusMessage = message;
			});
		}

		private void UpdateProgress(int progress) {
			Application.Current.Dispatcher.InvokeAsync(() => {
				ProgressValue = progress;
			});
		}
	}
}
