using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EJRASync.UI.Models;
using EJRASync.UI.Services;
using EJRASync.UI.Utils;
using System.Collections.ObjectModel;
using System.Windows;

namespace EJRASync.UI.ViewModels {
	public partial class RemoteFileListViewModel : ObservableObject {
		private readonly IS3Service _s3Service;
		private readonly IContentStatusService _contentStatusService;
		private readonly MainWindowViewModel _mainViewModel;

		[ObservableProperty]
		private ObservableCollection<RemoteFileItem> _files = new();

		[ObservableProperty]
		private string _currentPath = string.Empty;

		[ObservableProperty]
		private RemoteFileItem? _selectedFile;

		[ObservableProperty]
		private bool _isLoading = false;

		public RemoteFileListViewModel(
			IS3Service s3Service,
			IContentStatusService contentStatusService,
			MainWindowViewModel mainViewModel) {
			_s3Service = s3Service;
			_contentStatusService = contentStatusService;
			_mainViewModel = mainViewModel;

			// Subscribe to content status changes to update file colors
			_contentStatusService.ContentStatusChanged += OnContentStatusChanged;
		}

		public async Task LoadBucketsAsync() {
			await this.InvokeUIAsync(() => {
				IsLoading = true;
				CurrentPath = "/";
				Files.Clear();
			});

			try {
				var buckets = _mainViewModel.NavigationContext.AvailableBuckets;

				await this.InvokeUIAsync(() => {
					foreach (var bucket in buckets) {
						Files.Add(new RemoteFileItem {
							Name = bucket,
							Key = bucket,
							DisplaySize = "Bucket",
							IsDirectory = true,
							LastModified = DateTime.MinValue
						});
					}

					_mainViewModel.NavigationContext.SelectedBucket = null;
					_mainViewModel.NavigationContext.RemoteCurrentPath = "";
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
				var files = await _s3Service.ListObjectsAsync(bucketName, prefix);

				await this.InvokeUIAsync(() => {
					Files.Clear();

					// Add parent navigation
					if (!string.IsNullOrEmpty(prefix)) {
						// Navigate back within bucket
						var parentPrefix = GetParentPrefix(prefix);
						Files.Add(new RemoteFileItem {
							Name = "..",
							Key = "..",
							DisplaySize = "Folder",
							IsDirectory = true,
							LastModified = DateTime.MinValue
						});
					} else if (!string.IsNullOrEmpty(bucketName)) {
						// Navigate back to bucket list
						Files.Add(new RemoteFileItem {
							Name = "..",
							Key = "..",
							DisplaySize = "Bucket List",
							IsDirectory = true,
							LastModified = DateTime.MinValue
						});
					}

					// Add files and populate activity status
					foreach (var file in files) {
						// Only apply coloring to root-level directories in ejra-cars and ejra-tracks buckets
						if (file.IsDirectory && (bucketName == EJRASync.Lib.Constants.CarsBucketName || bucketName == EJRASync.Lib.Constants.TracksBucketName) && string.IsNullOrEmpty(prefix)) {
							file.IsActive = _contentStatusService.IsContentActive(bucketName, file.Name);
						}

						Files.Add(file);
					}

					_mainViewModel.NavigationContext.SelectedBucket = bucketName;
					_mainViewModel.NavigationContext.RemoteCurrentPath = prefix;
				});
			} catch (Exception ex) {
				Console.WriteLine($"Error loading files from {bucketName}/{prefix}: {ex.Message}");
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

			_ = Task.Run(async () => {
				try {
					await this.InvokeUIAsync(() => {
						_mainViewModel.StatusMessage = "Loading...";
					});

					if (file.Key == "..") {
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
					} else if (_mainViewModel.NavigationContext.AvailableBuckets.Contains(file.Key)) {
						// Navigate into bucket
						await LoadFilesAsync(file.Key);
					} else {
						// Navigate into directory within bucket
						var newPrefix = string.IsNullOrEmpty(_mainViewModel.NavigationContext.RemoteCurrentPath)
							? file.Key
							: $"{_mainViewModel.NavigationContext.RemoteCurrentPath}/{file.Key}";

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

		[RelayCommand]
		private async Task TagAsActiveAsync(RemoteFileItem? file) {
			if (file == null || !file.IsDirectory || string.IsNullOrEmpty(_mainViewModel.NavigationContext.SelectedBucket))
				return;

			var bucketName = _mainViewModel.NavigationContext.SelectedBucket;
			if (bucketName != EJRASync.Lib.Constants.CarsBucketName && bucketName != EJRASync.Lib.Constants.TracksBucketName)
				return;

			var isCurrentlyActive = file.IsActive ?? false;
			await _contentStatusService.SetContentActiveAsync(bucketName, file.Name, !isCurrentlyActive);

			// Add pending change to update YAML
			var yamlFileName = bucketName == EJRASync.Lib.Constants.CarsBucketName ? EJRASync.Lib.Constants.CarsYamlFile : EJRASync.Lib.Constants.TracksYamlFile;

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
			return lastSlash > 0 ? cleanPrefix.Substring(0, lastSlash) : "";
		}

		private void OnContentStatusChanged(object? sender, ContentStatusChangedEventArgs e) {
			// Update the IsActive property for the affected file
			var file = Files.FirstOrDefault(f => f.Name == e.ContentName);
			if (file != null) {
				file.IsActive = e.IsActive;
			}
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
			if (file == null || string.IsNullOrEmpty(_mainViewModel.NavigationContext.SelectedBucket))
				return;

			var bucketName = _mainViewModel.NavigationContext.SelectedBucket;
			var remoteKey = string.IsNullOrEmpty(_mainViewModel.NavigationContext.RemoteCurrentPath)
				? file.Key
				: $"{_mainViewModel.NavigationContext.RemoteCurrentPath}/{file.Key}";

			var change = new PendingChange {
				Type = ChangeType.DeleteRemote,
				Description = $"Delete {file.Name}",
				RemoteKey = remoteKey,
				BucketName = bucketName
			};

			_mainViewModel.AddPendingChange(change);
		}

	}
}
