using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EJRASync.Lib.Utils;
using EJRASync.UI.Models;
using EJRASync.UI.Utils;
using log4net;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace EJRASync.UI.ViewModels {
	public partial class LocalFileListViewModel : ObservableObject {
		private static readonly ILog _logger = Lib.LoggingHelper.GetLogger(typeof(LocalFileListViewModel));
		private readonly Lib.Services.IFileService _fileService;
		private readonly MainWindowViewModel _mainViewModel;

		[ObservableProperty]
		private ObservableCollection<LocalFileItem> _files = new();

		[ObservableProperty]
		private string _currentPath = string.Empty;

		[ObservableProperty]
		private LocalFileItem? _selectedFile;

		[ObservableProperty]
		private ObservableCollection<LocalFileItem> _selectedFiles = new();

		[ObservableProperty]
		private bool _isLoading = false;

		public LocalFileListViewModel(Lib.Services.IFileService fileService, MainWindowViewModel mainViewModel) {
			_fileService = fileService;
			_mainViewModel = mainViewModel;
		}

		public async Task LoadFilesAsync(string path) {
			if (!_fileService.IsValidDirectory(path))
				return;

			try {
				await this.InvokeUIAsync(() => {
					_mainViewModel.StatusMessage = "Loading local files...";
					IsLoading = true;
					CurrentPath = PathUtils.NormalizePath(path);
					_mainViewModel.NavigationContext.LocalCurrentPath = PathUtils.NormalizePath(path);
				});

				var files = await _fileService.GetLocalFilesAsync(path);

				await this.InvokeUIAsync(() => {
					Files.Clear();

					// Add parent directory navigation if not at root
					var basePath = _mainViewModel.NavigationContext.LocalBasePath;
					if (!string.Equals(path, basePath, StringComparison.OrdinalIgnoreCase)) {
						Files.Add(new LocalFileItem {
							Name = "..",
							FullPath = _fileService.GetParentDirectory(path),
							DisplaySize = "Folder",
							IsDirectory = true,
							LastModified = DateTime.MinValue
						});
					}

					foreach (var libFile in files) {
						Files.Add(LocalFileItem.FromLib(libFile));
					}

					_mainViewModel.StatusMessage = "Ready";
				});
			} catch (Exception ex) {
				await this.InvokeUIAsync(() => {
					_mainViewModel.StatusMessage = $"Error loading files: {ex.Message}";
				});
			} finally {
				await this.InvokeUIAsync(() => {
					IsLoading = false;
				});
			}
		}

		[RelayCommand]
		private async Task NavigateToAsync(LocalFileItem? file) {
			if (file == null || !file.IsDirectory)
				return;

			await LoadFilesAsync(file.FullPath);
			_mainViewModel.NavigationContext.LocalCurrentPath = PathUtils.NormalizePath(file.FullPath);
		}

		[RelayCommand(CanExecute = nameof(CanNavigateUp))]
		private async Task NavigateUpAsync() {
			var currentPath = _mainViewModel.NavigationContext.LocalCurrentPath;
			var parentPath = _fileService.GetParentDirectory(currentPath);

			if (!string.IsNullOrEmpty(parentPath) && Directory.Exists(parentPath)) {
				await LoadFilesAsync(parentPath);
			}
		}

		private bool CanNavigateUp() {
			var currentPath = _mainViewModel.NavigationContext.LocalCurrentPath;
			var basePath = _mainViewModel.NavigationContext.LocalBasePath;
			return !string.Equals(currentPath, basePath, StringComparison.OrdinalIgnoreCase);
		}

		[RelayCommand]
		public async Task RefreshAsync() {
			await LoadFilesAsync(_mainViewModel.NavigationContext.LocalCurrentPath);
		}

		[RelayCommand]
		private async void CompressAndUpload(LocalFileItem? file) {
			var filesToProcess = SelectedFiles.Count > 0 ? SelectedFiles.ToList() : (file != null ? new List<LocalFileItem> { file } : new List<LocalFileItem>());

			if (!filesToProcess.Any())
				return;

			foreach (var item in filesToProcess) {
				if (item.IsDirectory) {
					await ProcessDirectoryForCompressAndUpload(item.FullPath);
				} else {
					ProcessFileForCompressAndUpload(item);
				}
			}
		}

		public async Task ProcessDirectoryForCompressAndUpload(string directoryPath) {
			var bucketName = DetermineBucketName(directoryPath);
			if (string.IsNullOrEmpty(bucketName))
				return;

			try {
				// Collect all files from directory and subdirectories
				var allFiles = await CollectAllFilesFromDirectory(directoryPath);

				if (!allFiles.Any()) {
					_mainViewModel.StatusMessage = "No files found in directory";
					return;
				}

				var directoryName = Path.GetFileName(directoryPath);
				_mainViewModel.StatusMessage = $"Adding {allFiles.Count} files from {directoryName} for compress & upload...";

				// Process all files in one batch
				foreach (var file in allFiles) {
					ProcessFileForCompressAndUpload(file);
				}

				_mainViewModel.StatusMessage = $"Added {allFiles.Count} files from {directoryName} for compress & upload";
			} catch (Exception ex) {
				_mainViewModel.StatusMessage = $"Error processing directory {directoryPath}: {ex.Message}";
			}
		}

		public void ProcessFileForCompressAndUpload(LocalFileItem file) {
			var bucketName = DetermineBucketName(file.FullPath);
			if (string.IsNullOrEmpty(bucketName))
				return;

			var relativePath = GetRelativeRemotePath(file.FullPath, bucketName);
			var fullRelativePath = Path.GetRelativePath(_mainViewModel.NavigationContext.LocalBasePath, file.FullPath);
			var change = new PendingChange {
				Type = ChangeType.CompressAndUpload,
				Description = fullRelativePath,
				LocalPath = file.FullPath,
				RemoteKey = relativePath,
				BucketName = bucketName,
				FileSizeBytes = file.SizeBytes
			};

			_mainViewModel.AddPendingChange(change);
		}

		[RelayCommand]
		private async void RawUpload(LocalFileItem? file) {
			var filesToProcess = SelectedFiles.Count > 0 ? SelectedFiles.ToList() : (file != null ? new List<LocalFileItem> { file } : new List<LocalFileItem>());

			if (!filesToProcess.Any())
				return;

			foreach (var item in filesToProcess) {
				if (item.IsDirectory) {
					await ProcessDirectoryForRawUpload(item.FullPath);
				} else {
					ProcessFileForRawUpload(item);
				}
			}
		}

		public async Task ProcessDirectoryForRawUpload(string directoryPath) {
			var bucketName = DetermineBucketName(directoryPath);
			if (string.IsNullOrEmpty(bucketName))
				return;

			try {
				// Collect all files from directory and subdirectories
				var allFiles = await CollectAllFilesFromDirectory(directoryPath);

				if (!allFiles.Any()) {
					_mainViewModel.StatusMessage = "No files found in directory";
					return;
				}

				var directoryName = Path.GetFileName(directoryPath);
				_mainViewModel.StatusMessage = $"Adding {allFiles.Count} files from {directoryName} for raw upload...";

				// Process all files in one batch
				foreach (var file in allFiles) {
					ProcessFileForRawUpload(file);
				}

				_mainViewModel.StatusMessage = $"Added {allFiles.Count} files from {directoryName} for raw upload";
			} catch (Exception ex) {
				_mainViewModel.StatusMessage = $"Error processing directory {directoryPath}: {ex.Message}";
			}
		}

		public void ProcessFileForRawUpload(LocalFileItem file) {
			var bucketName = DetermineBucketName(file.FullPath);
			if (string.IsNullOrEmpty(bucketName))
				return;

			var relativePath = GetRelativeRemotePath(file.FullPath, bucketName);
			var fullRelativePath = Path.GetRelativePath(_mainViewModel.NavigationContext.LocalBasePath, file.FullPath);
			var change = new PendingChange {
				Type = ChangeType.RawUpload,
				Description = fullRelativePath,
				LocalPath = file.FullPath,
				RemoteKey = relativePath,
				BucketName = bucketName,
				FileSizeBytes = file.SizeBytes
			};

			_mainViewModel.AddPendingChange(change);
		}

		[RelayCommand]
		private void ViewInExplorer(LocalFileItem? file) {
			var filesToProcess = SelectedFiles.Count > 0 ? SelectedFiles.ToList() : (file != null ? new List<LocalFileItem> { file } : new List<LocalFileItem>());

			if (!filesToProcess.Any())
				return;

			foreach (var item in filesToProcess) {
				try {
					var windowsPath = item.FullPath.Replace('/', '\\');
					if (item.IsDirectory) {
						Process.Start("explorer.exe", windowsPath);
					} else {
						Process.Start("explorer.exe", $"/select,\"{windowsPath}\"");
					}
				} catch (Exception ex) {
					_logger.Error($"Error opening explorer: {ex.Message}", ex);
				}
			}
		}

		private string DetermineBucketName(string filePath) {
			var basePath = _mainViewModel.NavigationContext.LocalBasePath;
			var relativePath = Path.GetRelativePath(basePath, filePath);

			// Normalize path separators to handle both forward and back slashes
			var normalizedRelativePath = relativePath.Replace('\\', '/');

			if (normalizedRelativePath.StartsWith("content/cars", StringComparison.OrdinalIgnoreCase))
				return Lib.Constants.CarsBucketName;
			if (normalizedRelativePath.StartsWith("content/tracks", StringComparison.OrdinalIgnoreCase))
				return Lib.Constants.TracksBucketName;
			if (normalizedRelativePath.StartsWith("content/fonts", StringComparison.OrdinalIgnoreCase))
				return Lib.Constants.FontsBucketName;
			if (normalizedRelativePath.StartsWith("content/gui", StringComparison.OrdinalIgnoreCase))
				return Lib.Constants.GuiBucketName;
			if (normalizedRelativePath.StartsWith("apps", StringComparison.OrdinalIgnoreCase))
				return Lib.Constants.AppsBucketName;

			return null;
		}

		private string GetRelativeRemotePath(string filePath, string bucketName) {
			var basePath = _mainViewModel.NavigationContext.LocalBasePath;
			var relativePath = Path.GetRelativePath(basePath, filePath);

			// Remove the content prefix for specific buckets
			if (relativePath.StartsWith("apps" + Path.DirectorySeparatorChar)) {
				var parts = relativePath.Split(Path.DirectorySeparatorChar);
				if (parts.Length > 1) {
					// Skip "apps"
					relativePath = string.Join(Path.DirectorySeparatorChar.ToString(), parts.Skip(1));
				}
			} else if (relativePath.StartsWith("content" + Path.DirectorySeparatorChar)) {
				var parts = relativePath.Split(Path.DirectorySeparatorChar);
				if (parts.Length > 2) {
					// Skip "content" and bucket folder name
					relativePath = string.Join(Path.DirectorySeparatorChar.ToString(), parts.Skip(2));
				}
			}

			return relativePath.Replace(Path.DirectorySeparatorChar, '/');
		}

		private async Task<List<LocalFileItem>> CollectAllFilesFromDirectory(string directoryPath) {
			var allFiles = new List<LocalFileItem>();
			var directoriesToProcess = new Queue<string>();
			directoriesToProcess.Enqueue(directoryPath);

			while (directoriesToProcess.Count > 0) {
				var currentDirectory = directoriesToProcess.Dequeue();

				try {
					var items = await _fileService.GetLocalFilesAsync(currentDirectory);

					// Add all files to the collection
					var files = items.Where(f => !f.IsDirectory).Select(LocalFileItem.FromLib).ToList();
					allFiles.AddRange(files);

					// Queue up subdirectories for processing
					var subdirectories = items.Where(f => f.IsDirectory && f.Name != "..").ToList();
					foreach (var subdir in subdirectories) {
						directoriesToProcess.Enqueue(subdir.FullPath);
					}
				} catch (Exception ex) {
					// Log error but continue with other directories
					_logger.Error($"Error processing directory {currentDirectory}: {ex.Message}", ex);
				}
			}

			return allFiles;
		}
	}
}
