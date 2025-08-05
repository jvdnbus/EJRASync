using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EJRASync.UI.Models;
using EJRASync.UI.Services;
using EJRASync.UI.Utils;
using EJRASync.UI.ViewModels;
using System.Collections.ObjectModel;
using System.IO;

namespace EJRASync.UI {
	public partial class MainWindowViewModel : ObservableObject {
		private readonly IS3Service _s3Service;
		private readonly IFileService _fileService;
		private readonly IContentStatusService _contentStatusService;
		private readonly ICompressionService _compressionService;

		[ObservableProperty]
		private ObservableCollection<PendingChange> _pendingChanges = new();

		[ObservableProperty]
		private NavigationContext _navigationContext = new();

		[ObservableProperty]
		private string _statusMessage = "Ready";

		[ObservableProperty]
		private double _progressValue = 0;

		[ObservableProperty]
		private bool _isScanning = false;

		[ObservableProperty]
		private bool _isApplying = false;

		public LocalFileListViewModel LocalFiles { get; }
		public RemoteFileListViewModel RemoteFiles { get; }

		public MainWindowViewModel(
			IS3Service s3Service,
			IFileService fileService,
			IContentStatusService contentStatusService,
			ICompressionService compressionService) {
			_s3Service = s3Service;
			_fileService = fileService;
			_contentStatusService = contentStatusService;
			_compressionService = compressionService;

			LocalFiles = new LocalFileListViewModel(_fileService, this);
			RemoteFiles = new RemoteFileListViewModel(_s3Service, _contentStatusService, this);

			// Set up default Assetto Corsa path
			var steamPath = EJRASync.Lib.SteamHelper.FindSteam();
			var acPath = EJRASync.Lib.SteamHelper.FindAssettoCorsa(steamPath);
			NavigationContext.LocalBasePath = acPath;
			NavigationContext.LocalCurrentPath = PathUtils.NormalizePath(Path.Combine(acPath, "content"));
		}

		public async Task InitializeAsync() {
			await _contentStatusService.InitializeAsync();
			await LocalFiles.LoadFilesAsync(NavigationContext.LocalCurrentPath);
		}

		[RelayCommand(CanExecute = nameof(CanScanChanges))]
		private async Task ScanChangesAsync() {
			IsScanning = true;
			StatusMessage = "Scanning for changes...";
			ProgressValue = 0;

			try {
				PendingChanges.Clear();

				var buckets = new[] { EJRASync.Lib.Constants.CarsBucketName, EJRASync.Lib.Constants.TracksBucketName, EJRASync.Lib.Constants.FontsBucketName, EJRASync.Lib.Constants.AppsBucketName };
				var localFolders = new[] { "content/cars", "content/tracks", "content/fonts", "apps" };

				for (int i = 0; i < buckets.Length; i++) {
					var bucket = buckets[i];
					var localFolder = localFolders[i];
					var localPath = Path.Combine(NavigationContext.LocalBasePath, localFolder);

					if (Directory.Exists(localPath)) {
						await ScanBucketChangesAsync(bucket, localPath);
					}

					ProgressValue = (i + 1) * 100.0 / buckets.Length;
				}

				StatusMessage = $"Found {PendingChanges.Count} changes";
			} catch (Exception ex) {
				StatusMessage = $"Error scanning: {ex.Message}";
			} finally {
				IsScanning = false;
				ProgressValue = 0;
			}
		}

		private async Task ScanBucketChangesAsync(string bucketName, string localPath) {
			var localFiles = await _fileService.GetLocalFilesAsync(localPath);
			var remoteFiles = await _s3Service.ListObjectsAsync(bucketName);

			foreach (var localFile in localFiles.Where(f => !f.IsDirectory)) {
				var relativePath = Path.GetRelativePath(localPath, localFile.FullPath).Replace(Path.DirectorySeparatorChar, '/');
				var remoteFile = remoteFiles.FirstOrDefault(r => r.Key == relativePath);

				if (remoteFile == null) {
					// File doesn't exist remotely - needs upload
					AddUploadChange(bucketName, localFile, relativePath);
				} else {
					// File exists - check if changed
					var localHash = await _fileService.CalculateFileHashAsync(localFile.FullPath);
					var remoteOriginalHash = remoteFile.OriginalHash;

					if (remoteOriginalHash == null || localHash != remoteOriginalHash) {
						AddUploadChange(bucketName, localFile, relativePath);
					}
				}
			}
		}

		private void AddUploadChange(string bucketName, LocalFileItem localFile, string remoteKey) {
			var shouldCompress = _compressionService.ShouldCompress(localFile.Name, localFile.SizeBytes);
			var changeType = shouldCompress ? ChangeType.CompressAndUpload : ChangeType.RawUpload;
			var description = shouldCompress
				? $"Compress and upload {localFile.Name}"
				: $"Upload {localFile.Name}";

			PendingChanges.Add(new PendingChange {
				Type = changeType,
				Description = description,
				LocalPath = localFile.FullPath,
				RemoteKey = remoteKey,
				BucketName = bucketName,
				FileSizeBytes = localFile.SizeBytes
			});
		}

		private bool CanScanChanges() => !IsScanning && !IsApplying;

		[RelayCommand(CanExecute = nameof(CanViewChanges))]
		private void ViewChanges() {
			var dialog = new EJRASync.UI.Views.PendingChangesDialog(PendingChanges);
			dialog.ShowDialog();
		}

		private bool CanViewChanges() => PendingChanges.Count > 0;

		[RelayCommand(CanExecute = nameof(CanDiscardChanges))]
		private void DiscardChanges() {
			PendingChanges.Clear();
			StatusMessage = "Changes discarded";
		}

		private bool CanDiscardChanges() => PendingChanges.Count > 0;

		[RelayCommand(CanExecute = nameof(CanApplyChanges))]
		private async Task ApplyChangesAsync() {
			IsApplying = true;
			StatusMessage = "Applying changes...";
			ProgressValue = 0;

			try {
				var totalChanges = PendingChanges.Count;
				var processedChanges = 0;

				var parallelOptions = new ParallelOptions {
					MaxDegreeOfParallelism = Environment.ProcessorCount
				};

				await Parallel.ForEachAsync(PendingChanges, parallelOptions, async (change, ct) => {
					try {
						await ProcessChangeAsync(change);
					} catch (Exception ex) {
						Console.WriteLine($"Error processing {change.Description}: {ex.Message}");
					} finally {
						Interlocked.Increment(ref processedChanges);
						ProgressValue = processedChanges * 100.0 / totalChanges;
						StatusMessage = $"Processing {processedChanges} of {totalChanges}...";
					}
				});

				PendingChanges.Clear();
				StatusMessage = $"Successfully applied {processedChanges} changes";
			} catch (Exception ex) {
				StatusMessage = $"Error applying changes: {ex.Message}";
			} finally {
				IsApplying = false;
				ProgressValue = 0;
			}
		}

		private async Task ProcessChangeAsync(PendingChange change) {
			switch (change.Type) {
				case ChangeType.CompressAndUpload:
					await ProcessCompressAndUploadAsync(change);
					break;
				case ChangeType.RawUpload:
					await ProcessRawUploadAsync(change);
					break;
				case ChangeType.UpdateYaml:
					await ProcessYamlUpdateAsync(change);
					break;
			}
		}

		private async Task ProcessCompressAndUploadAsync(PendingChange change) {
			if (string.IsNullOrEmpty(change.LocalPath))
				return;

			var originalHash = await _fileService.CalculateFileHashAsync(change.LocalPath);
			var compressedData = await _compressionService.CompressFileAsync(change.LocalPath);

			var metadata = new Dictionary<string, string>
			{
				{ "original-hash", originalHash }
			};

			await _s3Service.UploadDataAsync(change.BucketName, change.RemoteKey, compressedData, metadata);
		}

		private async Task ProcessRawUploadAsync(PendingChange change) {
			if (string.IsNullOrEmpty(change.LocalPath))
				return;

			await _s3Service.UploadFileAsync(change.BucketName, change.RemoteKey, change.LocalPath, change.Metadata);
		}

		private async Task ProcessYamlUpdateAsync(PendingChange change) {
			var yamlData = await _contentStatusService.GenerateYamlAsync(change.BucketName);
			await _s3Service.UploadDataAsync(change.BucketName, change.RemoteKey, yamlData);
		}

		private bool CanApplyChanges() => PendingChanges.Count > 0 && !IsApplying;

		public string ViewChangesButtonText => $"View {PendingChanges.Count} changes";

		public void AddPendingChange(PendingChange change) {
			PendingChanges.Add(change);
		}

		partial void OnPendingChangesChanged(ObservableCollection<PendingChange> value) {
			OnPropertyChanged(nameof(ViewChangesButtonText));
		}
	}
}
