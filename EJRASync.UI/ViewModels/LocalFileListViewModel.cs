using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EJRASync.UI.Models;
using EJRASync.UI.Services;
using EJRASync.UI.Utils;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace EJRASync.UI.ViewModels {
	public partial class LocalFileListViewModel : ObservableObject {
		private readonly IFileService _fileService;
		private readonly MainWindowViewModel _mainViewModel;

		[ObservableProperty]
		private ObservableCollection<LocalFileItem> _files = new();

		[ObservableProperty]
		private string _currentPath = string.Empty;

		[ObservableProperty]
		private LocalFileItem? _selectedFile;

		[ObservableProperty]
		private bool _isLoading = false;

		public LocalFileListViewModel(IFileService fileService, MainWindowViewModel mainViewModel) {
			_fileService = fileService;
			_mainViewModel = mainViewModel;
		}

		public async Task LoadFilesAsync(string path) {
			if (!_fileService.IsValidDirectory(path))
				return;

			_ = Task.Run(async () => {
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

						foreach (var file in files) {
							Files.Add(file);
						}
					});

					await this.InvokeUIAsync(() => {
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
			});
		}

		[RelayCommand]
		private async Task NavigateToAsync(LocalFileItem? file) {
			if (file == null || !file.IsDirectory)
				return;

			await LoadFilesAsync(file.FullPath);
			_mainViewModel.NavigationContext.LocalCurrentPath = PathUtils.NormalizePath(file.FullPath);
		}

		[RelayCommand]
		private void CompressAndUpload(LocalFileItem? file) {
			if (file == null || file.IsDirectory)
				return;

			var bucketName = DetermineBucketName(file.FullPath);
			if (string.IsNullOrEmpty(bucketName))
				return;

			var relativePath = GetRelativeRemotePath(file.FullPath, bucketName);
			var change = new PendingChange {
				Type = ChangeType.CompressAndUpload,
				Description = $"Compress and upload {file.Name}",
				LocalPath = file.FullPath,
				RemoteKey = relativePath,
				BucketName = bucketName,
				FileSizeBytes = file.SizeBytes
			};

			_mainViewModel.AddPendingChange(change);
		}

		[RelayCommand]
		private void RawUpload(LocalFileItem? file) {
			if (file == null || file.IsDirectory)
				return;

			var bucketName = DetermineBucketName(file.FullPath);
			if (string.IsNullOrEmpty(bucketName))
				return;

			var relativePath = GetRelativeRemotePath(file.FullPath, bucketName);
			var change = new PendingChange {
				Type = ChangeType.RawUpload,
				Description = $"Upload {file.Name}",
				LocalPath = file.FullPath,
				RemoteKey = relativePath,
				BucketName = bucketName,
				FileSizeBytes = file.SizeBytes
			};

			_mainViewModel.AddPendingChange(change);
		}

		[RelayCommand]
		private void ViewInExplorer(LocalFileItem? file) {
			if (file == null)
				return;

			try {
				if (file.IsDirectory) {
					Process.Start("explorer.exe", file.FullPath);
				} else {
					Process.Start("explorer.exe", $"/select,\"{file.FullPath}\"");
				}
			} catch (Exception ex) {
				Console.WriteLine($"Error opening explorer: {ex.Message}");
			}
		}

		private string DetermineBucketName(string filePath) {
			var basePath = _mainViewModel.NavigationContext.LocalBasePath;
			var relativePath = Path.GetRelativePath(basePath, filePath);

			if (relativePath.StartsWith("content" + Path.DirectorySeparatorChar + "cars", StringComparison.OrdinalIgnoreCase))
				return EJRASync.Lib.Constants.CarsBucketName;
			if (relativePath.StartsWith("content" + Path.DirectorySeparatorChar + "tracks", StringComparison.OrdinalIgnoreCase))
				return EJRASync.Lib.Constants.TracksBucketName;
			if (relativePath.StartsWith("content" + Path.DirectorySeparatorChar + "fonts", StringComparison.OrdinalIgnoreCase))
				return EJRASync.Lib.Constants.FontsBucketName;
			if (relativePath.StartsWith("apps", StringComparison.OrdinalIgnoreCase))
				return EJRASync.Lib.Constants.AppsBucketName;

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
	}
}
