using EJRASync.Lib.Services;
using EJRASync.UI.Models;
using EJRASync.UI.Utils;
using EJRASync.UI.Views;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EJRASync.UI {
	public partial class MainWindow : DarkThemeWindow {
		// Constants
		private const string ZeroByteFileSize = "0 B";
		private static readonly string[] FileSizeSuffixes = { "B", "KB", "MB", "GB", "TB" };

		// Asset Icon Paths
		private const string IconCheckSquare = "/Assets/Icons/check-square.png";
		private const string IconSubtractSquare = "/Assets/Icons/subtract-square.png";
		private const string IconDelete = "/Assets/Icons/delete.png";
		private const string IconDownload = "/Assets/Icons/download-file.png";
		private const string IconView = "/Assets/Icons/view.png";

		// Context Menu Text
		private const string MenuTagAsActive = "Tag as Active";
		private const string MenuTagAsInactive = "Tag as Inactive";
		private const string MenuDelete = "Delete";
		private const string MenuDownload = "Download";
		private const string MenuView = "View";

		// Drag/Drop Data Format Keys
		private const string DataKeyLocalFiles = "LocalFiles";
		private const string DataKeyRemoteFiles = "RemoteFiles";

		// Color Values
		private static readonly Color MenuBackgroundColor = Color.FromRgb(0x2D, 0x2D, 0x30);
		private static readonly Color MenuForegroundColor = Color.FromRgb(0xF0, 0xF0, 0xF0);
		private static readonly Color SeparatorBackgroundColor = Color.FromRgb(0x3E, 0x3E, 0x42);

		// Status Messages
		private const string StatusInitializing = "Initializing...";
		private const string StatusReady = "Ready";
		private const string StatusDownloadingFiles = "Downloading files...";
		private const string StatusDownloadingFileFormat = "Downloading {0} ({1}/{2})...";
		private const string StatusDownloadedFilesFormat = "Downloaded {0} files";
		private const string StatusUploadItemsFormat = "Added {0} items for upload";
		private const string StatusCannotDownloadNoBucket = "Cannot download: no bucket selected";
		private const string StatusCannotDownloadInvalidDir = "Cannot download: invalid local directory";
		private const string StatusInitializationFailedFormat = "Initialization failed: {0}";
		private const string StatusErrorDownloadingFormat = "Error downloading files: {0}";
		private const string StatusErrorProcessingDragDropFormat = "Error processing drag and drop: {0}";
		private const string StatusErrorExternalFileDropFormat = "Error processing external file drop: {0}";

		// File/Folder Names
		private const string ParentDirectoryName = "..";
		private const string FolderDisplaySize = "Folder";

		private readonly MainWindowViewModel _viewModel;
		private readonly IS3Service _s3Service;
		private readonly ICompressionService _compressionService;
		private Point _dragStartPoint;
		private bool _isDragging = false;
		private List<object> _dragSelectionBackup = new();

		public MainWindow(MainWindowViewModel viewModel, IS3Service s3Service, ICompressionService compressionService) {
			_viewModel = viewModel;
			_s3Service = s3Service;
			_compressionService = compressionService;
			InitializeComponent();
			DataContext = _viewModel;
			Title = $"EJRA Sync Manager {Lib.Constants.Version}";
			Loaded += OnLoaded;

			// Wire up selection change events
			LocalFilesDataGrid.SelectionChanged += LocalFilesDataGrid_SelectionChanged;
			RemoteFilesDataGrid.SelectionChanged += RemoteFilesDataGrid_SelectionChanged;

			// Wire up drag-and-drop events
			LocalFilesDataGrid.PreviewMouseLeftButtonDown += DataGrid_PreviewMouseLeftButtonDown;
			LocalFilesDataGrid.MouseMove += DataGrid_MouseMove;
			LocalFilesDataGrid.DragOver += LocalFilesDataGrid_DragOver;
			LocalFilesDataGrid.Drop += LocalFilesDataGrid_Drop;

			RemoteFilesDataGrid.PreviewMouseLeftButtonDown += DataGrid_PreviewMouseLeftButtonDown;
			RemoteFilesDataGrid.MouseMove += DataGrid_MouseMove;
			RemoteFilesDataGrid.DragOver += RemoteFilesDataGrid_DragOver;
			RemoteFilesDataGrid.Drop += RemoteFilesDataGrid_Drop;

			// Warm up files context menus
			this.Loaded += (s, e) => {
				Dispatcher.BeginInvoke(new Action(() => {
					if (LocalFilesDataGrid.ContextMenu != null) {
						LocalFilesDataGrid.ContextMenu.IsOpen = true;
						LocalFilesDataGrid.ContextMenu.IsOpen = false;
					}
					if (RemoteFilesDataGrid.ContextMenu != null) {
						RemoteFilesDataGrid.ContextMenu.IsOpen = true;
						RemoteFilesDataGrid.ContextMenu.IsOpen = false;
					}
				}), System.Windows.Threading.DispatcherPriority.Loaded);
			};
		}

		private void OnLoaded(object sender, RoutedEventArgs e) {
			_ = Task.Run(async () => {
				try {
					await this.InvokeUIAsync(() => {
						_viewModel.StatusMessage = StatusInitializing;
					});

					await _viewModel.InitializeAsync();
					// Load initial remote bucket list in background
					await _viewModel.RemoteFiles.LoadBucketsAsync();

					await this.InvokeUIAsync(() => {
						_viewModel.StatusMessage = StatusReady;
					});
				} catch (Exception ex) {
					await this.InvokeUIAsync(() => {
						_viewModel.StatusMessage = string.Format(StatusInitializationFailedFormat, ex.Message);
					});
				}
			});
		}

		private void LocalFilesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			_viewModel.LocalFiles.SelectedFiles.Clear();
			foreach (var item in LocalFilesDataGrid.SelectedItems.Cast<LocalFileItem>()) {
				_viewModel.LocalFiles.SelectedFiles.Add(item);
			}
		}

		private void RemoteFilesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			_viewModel.RemoteFiles.SelectedFiles.Clear();
			foreach (var item in RemoteFilesDataGrid.SelectedItems.Cast<RemoteFileItem>()) {
				_viewModel.RemoteFiles.SelectedFiles.Add(item);
			}
		}

		// Drag and drop implementation
		private void DataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
			_dragStartPoint = e.GetPosition(null);
			_isDragging = false;

			var dataGrid = sender as DataGrid;
			if (dataGrid != null) {
				// Check if the click is on a data row (not scrollbar, header, etc.)
				var hitTestResult = VisualTreeHelper.HitTest(dataGrid, e.GetPosition(dataGrid));
				if (hitTestResult?.VisualHit != null) {
					var row = FindAncestor<DataGridRow>(hitTestResult.VisualHit);
					if (row == null) {
						// Not clicking on a data row, don't prepare for drag
						return;
					}

					// Backup current selection in case we need to restore it
					_dragSelectionBackup.Clear();
					_dragSelectionBackup.AddRange(dataGrid.SelectedItems.Cast<object>());

					// Check if the click is on a selected item with multiple selection
					if (row.IsSelected && dataGrid.SelectedItems.Count > 1) {
						// Clicking on a selected item when multiple items are selected
						// Prevent the default selection behavior to preserve multi-selection
						e.Handled = true;
					}
				}
			}
		}

		private void DataGrid_MouseMove(object sender, MouseEventArgs e) {
			if (e.LeftButton == MouseButtonState.Pressed && !_isDragging && _dragSelectionBackup.Count > 0) {
				Point mousePos = e.GetPosition(null);
				Vector diff = _dragStartPoint - mousePos;

				if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
					Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance) {

					var dataGrid = sender as DataGrid;
					if (dataGrid != null) {
						// Double-check that we're still over a data row
						var hitTestResult = VisualTreeHelper.HitTest(dataGrid, e.GetPosition(dataGrid));
						if (hitTestResult?.VisualHit != null) {
							var row = FindAncestor<DataGridRow>(hitTestResult.VisualHit);
							if (row == null) {
								// No longer over a data row, cancel drag
								return;
							}
						}

						// If selection was reduced to 1 item but we had multiple items backed up,
						// restore the original selection for dragging
						if (_dragSelectionBackup.Count > 1 && dataGrid.SelectedItems.Count == 1) {
							dataGrid.SelectedItems.Clear();
							foreach (var item in _dragSelectionBackup) {
								dataGrid.SelectedItems.Add(item);
							}
						}

						if (dataGrid.SelectedItems.Count > 0) {
							_isDragging = true;
							StartDragOperation(dataGrid);
						}
					}
				}
			}
		}

		private void StartDragOperation(DataGrid sourceDataGrid) {
			try {
				var dataObject = new DataObject();

				if (sourceDataGrid == LocalFilesDataGrid) {
					// Dragging from local files
					var localFiles = LocalFilesDataGrid.SelectedItems.Cast<LocalFileItem>().ToList();
					var filePaths = localFiles.Where(f => !f.IsDirectory && f.Name != ParentDirectoryName).Select(f => f.FullPath).ToArray();

					if (filePaths.Any() || localFiles.Any(f => f.IsDirectory && f.Name != ParentDirectoryName)) {
						dataObject.SetData(DataFormats.FileDrop, filePaths);
						dataObject.SetData(DataKeyLocalFiles, localFiles);
						DragDrop.DoDragDrop(sourceDataGrid, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
					}
				} else if (sourceDataGrid == RemoteFilesDataGrid) {
					// Dragging from remote files
					var remoteFiles = RemoteFilesDataGrid.SelectedItems.Cast<RemoteFileItem>()
						.Where(f => f.Name != ParentDirectoryName && !f.Key.StartsWith(HashStoreService.HASH_STORE_DIR))
						.ToList();
					if (remoteFiles.Any()) {
						dataObject.SetData(DataKeyRemoteFiles, remoteFiles);
						DragDrop.DoDragDrop(sourceDataGrid, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
					}
				}
			} finally {
				_isDragging = false;
			}
		}

		private void LocalFilesDataGrid_DragOver(object sender, DragEventArgs e) {
			// Allow dropping files from Windows Explorer or remote files for download
			if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataKeyRemoteFiles)) {
				e.Effects = DragDropEffects.Copy;
			} else {
				e.Effects = DragDropEffects.None;
			}
			e.Handled = true;
		}

		private async void LocalFilesDataGrid_Drop(object sender, DragEventArgs e) {
			if (e.Data.GetDataPresent(DataKeyRemoteFiles)) {
				// Internal drag from remote files - download to local
				var remoteFiles = e.Data.GetData(DataKeyRemoteFiles) as List<RemoteFileItem>;
				if (remoteFiles?.Any() == true) {
					await HandleRemoteToLocalDrop(remoteFiles);
				}
			} else if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
				// External drag from Windows Explorer
				var files = (string[])e.Data.GetData(DataFormats.FileDrop);
				HandleExternalFileDrop(files);
			}
			e.Handled = true;
		}

		private void RemoteFilesDataGrid_DragOver(object sender, DragEventArgs e) {
			// Check if we're trying to drop into .zstd directory
			var currentRemotePath = _viewModel.NavigationContext.RemoteCurrentPath ?? "";
			if (currentRemotePath.StartsWith(HashStoreService.HASH_STORE_DIR)) {
				e.Effects = DragDropEffects.None;
				e.Handled = true;
				return;
			}

			// Allow dropping local files for upload
			if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataKeyLocalFiles)) {
				e.Effects = DragDropEffects.Copy;
			} else {
				e.Effects = DragDropEffects.None;
			}
			e.Handled = true;
		}

		private async void RemoteFilesDataGrid_Drop(object sender, DragEventArgs e) {
			// Check if we're trying to drop into .zstd directory
			var currentRemotePath = _viewModel.NavigationContext.RemoteCurrentPath ?? "";
			if (currentRemotePath.StartsWith(HashStoreService.HASH_STORE_DIR)) {
				_viewModel.StatusMessage = "Cannot upload files to .zstd directory - this is a system directory";
				e.Handled = true;
				return;
			}

			if (e.Data.GetDataPresent(DataKeyLocalFiles)) {
				// Internal drag from local files
				var localFiles = e.Data.GetData(DataKeyLocalFiles) as List<LocalFileItem>;
				if (localFiles?.Any() == true) {
					await HandleInternalFileDrop(localFiles);
				}
			} else if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
				// External drag from Windows Explorer
				var files = (string[])e.Data.GetData(DataFormats.FileDrop);
				await HandleExternalFileDropToRemote(files);
			}
			e.Handled = true;
		}

		private async Task HandleRemoteToLocalDrop(List<RemoteFileItem> remoteFiles) {
			try {
				if (string.IsNullOrEmpty(_viewModel.NavigationContext.SelectedBucket)) {
					_viewModel.StatusMessage = StatusCannotDownloadNoBucket;
					return;
				}

				var bucketName = _viewModel.NavigationContext.SelectedBucket;
				var currentRemotePath = _viewModel.NavigationContext.RemoteCurrentPath ?? "";
				var localCurrentPath = _viewModel.NavigationContext.LocalCurrentPath;

				if (string.IsNullOrEmpty(localCurrentPath) || !Directory.Exists(localCurrentPath)) {
					_viewModel.StatusMessage = StatusCannotDownloadInvalidDir;
					return;
				}

				_viewModel.StatusMessage = StatusDownloadingFiles;
				_viewModel.ProgressValue = 0;
				var downloadedCount = 0;
				var filesToDownload = remoteFiles.Where(f => !f.IsDirectory).ToList();
				var totalFiles = filesToDownload.Count;

				for (int i = 0; i < filesToDownload.Count; i++) {
					var file = filesToDownload[i];
					try {
						var remoteKey = string.IsNullOrEmpty(currentRemotePath)
							? file.Key
							: $"{currentRemotePath}/{file.Key}";

						var localFilePath = Path.Combine(localCurrentPath, file.Name);

						_viewModel.StatusMessage = string.Format(StatusDownloadingFileFormat, file.Name, i + 1, totalFiles);

						// Create progress reporter for this file
						var progress = new Progress<long>(bytesRead => {
							// Update progress based on file progress within overall download progress
							var fileProgressPercent = file.SizeBytes > 0 ? (double)bytesRead / file.SizeBytes * 100 : 100;
							var overallProgress = ((double)i / totalFiles * 100) + (fileProgressPercent / totalFiles);
							_viewModel.ProgressValue = Math.Min(overallProgress, 100);
						});

						// Check if file needs decompression (has original-hash metadata)
						if (file.IsCompressed && !string.IsNullOrEmpty(file.OriginalHash)) {
							// Download to temp file and decompress
							var tempCompressedFile = await _s3Service.DownloadObjectAsync(bucketName, remoteKey, null, progress);

							try {
								await _compressionService.DecompressFileAsync(tempCompressedFile, localFilePath);
							} finally {
								// Clean up temp file
								if (File.Exists(tempCompressedFile)) {
									File.Delete(tempCompressedFile);
								}
							}
						} else {
							// Download directly to destination
							using (var fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write)) {
								await _s3Service.DownloadObjectAsync(bucketName, remoteKey, fileStream, progress);
							}
						}

						downloadedCount++;
						_viewModel.ProgressValue = (double)(i + 1) / totalFiles * 100;
					} catch (Exception ex) {
						System.Diagnostics.Debug.WriteLine($"Error downloading {file.Name}: {ex.Message}");
					}
				}

				_viewModel.StatusMessage = string.Format(StatusDownloadedFilesFormat, downloadedCount);
				_viewModel.ProgressValue = 0;

				// Refresh local file list to show new files
				await _viewModel.LocalFiles.LoadFilesAsync(localCurrentPath);
			} catch (Exception ex) {
				_viewModel.StatusMessage = string.Format(StatusErrorDownloadingFormat, ex.Message);
				_viewModel.ProgressValue = 0;
			}
		}

		private async Task HandleInternalFileDrop(List<LocalFileItem> localFiles) {
			try {
				// Show upload confirmation dialog
				var dialog = new UploadConfirmationDialog(localFiles.Count);
				dialog.Owner = this;

				var result = dialog.ShowDialog();
				if (result != true) return;

				// Process files based on user choice
				foreach (var file in localFiles.Where(f => f.Name != ParentDirectoryName)) {
					if (dialog.Result == UploadConfirmationDialog.UploadMethod.Compress) {
						if (file.IsDirectory) {
							await _viewModel.LocalFiles.ProcessDirectoryForCompressAndUpload(file.FullPath);
						} else {
							_viewModel.LocalFiles.ProcessFileForCompressAndUpload(file);
						}
					} else if (dialog.Result == UploadConfirmationDialog.UploadMethod.Raw) {
						if (file.IsDirectory) {
							await _viewModel.LocalFiles.ProcessDirectoryForRawUpload(file.FullPath);
						} else {
							_viewModel.LocalFiles.ProcessFileForRawUpload(file);
						}
					}
				}

				_viewModel.StatusMessage = string.Format(StatusUploadItemsFormat, localFiles.Count);
			} catch (Exception ex) {
				_viewModel.StatusMessage = string.Format(StatusErrorProcessingDragDropFormat, ex.Message);
			}
		}

		private async Task HandleExternalFileDropToRemote(string[] files) {
			try {
				// Convert external files to LocalFileItem objects
				var localFiles = new List<LocalFileItem>();

				foreach (var filePath in files) {
					if (File.Exists(filePath)) {
						var fileInfo = new FileInfo(filePath);
						localFiles.Add(new LocalFileItem {
							Name = fileInfo.Name,
							FullPath = filePath,
							SizeBytes = fileInfo.Length,
							LastModified = fileInfo.LastWriteTime,
							IsDirectory = false,
							DisplaySize = FormatFileSize(fileInfo.Length)
						});
					} else if (Directory.Exists(filePath)) {
						var dirInfo = new DirectoryInfo(filePath);
						localFiles.Add(new LocalFileItem {
							Name = dirInfo.Name,
							FullPath = filePath,
							SizeBytes = 0,
							LastModified = dirInfo.LastWriteTime,
							IsDirectory = true,
							DisplaySize = FolderDisplaySize
						});
					}
				}

				if (localFiles.Any()) {
					await HandleInternalFileDrop(localFiles);
				}
			} catch (Exception ex) {
				_viewModel.StatusMessage = string.Format(StatusErrorExternalFileDropFormat, ex.Message);
			}
		}

		private void HandleExternalFileDrop(string[] files) {
			// For dropping files into the local file list (navigate to containing folder)
			if (files.Length > 0) {
				var firstFile = files[0];
				var directory = File.Exists(firstFile) ? Path.GetDirectoryName(firstFile) : firstFile;

				if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) {
					Task.Run(async () => {
						await _viewModel.LocalFiles.LoadFilesAsync(directory);
					});
				}
			}
		}

		private string FormatFileSize(long bytes) {
			if (bytes == 0) return ZeroByteFileSize;

			var suffixes = FileSizeSuffixes;
			int suffixIndex = 0;
			double size = bytes;

			while (size >= 1024 && suffixIndex < suffixes.Length - 1) {
				size /= 1024;
				suffixIndex++;
			}

			return $"{size:F1} {suffixes[suffixIndex]}";
		}

		// Helper method to find ancestor of specific type in visual tree
		private void RemoteFilesDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e) {
			var dataGrid = sender as DataGrid;
			var contextMenu = dataGrid?.ContextMenu;

			if (contextMenu == null) return;

			// Clear existing items
			contextMenu.Items.Clear();

			// Get the selected file from the PlacementTarget to avoid visual tree searches
			var selectedFile = _viewModel.RemoteFiles.SelectedFile;
			if (selectedFile == null) {
				e.Handled = true;
				return;
			}

			// For .zstd files, only allow Download and View (not other operations)
			bool isZstdFile = selectedFile.Key.StartsWith(HashStoreService.HASH_STORE_DIR);

			if (isZstdFile && selectedFile.IsDirectory) {
				// Prevent context menu for .zstd directory itself
				e.Handled = true;
				return;
			}

			// Set explicit DataContext to avoid inheritance issues
			contextMenu.DataContext = _viewModel;

			// Build context menu items based on the selected file
			if (selectedFile.IsDirectory && !isZstdFile) {
				// Tag as Active menu item
				if (!selectedFile.IsActive.HasValue || selectedFile.IsActive == false) {
					var tagActiveItem = new MenuItem {
						Header = MenuTagAsActive,
						Background = new SolidColorBrush(MenuBackgroundColor),
						Foreground = new SolidColorBrush(MenuForegroundColor),
						Command = _viewModel.RemoteFiles.TagAsActiveCommand,
						CommandParameter = selectedFile,
						IsEnabled = _viewModel.HasWriteAccess
					};
					tagActiveItem.Icon = new Image {
						Source = new BitmapImage(new Uri(IconCheckSquare, UriKind.Relative)),
						Width = 16,
						Height = 16
					};
					contextMenu.Items.Add(tagActiveItem);
				}

				// Tag as Inactive menu item
				if (selectedFile.IsActive == true) {
					var tagInactiveItem = new MenuItem {
						Header = MenuTagAsInactive,
						Background = new SolidColorBrush(MenuBackgroundColor),
						Foreground = new SolidColorBrush(MenuForegroundColor),
						Command = _viewModel.RemoteFiles.TagAsActiveCommand,
						CommandParameter = selectedFile,
						IsEnabled = _viewModel.HasWriteAccess
					};
					tagInactiveItem.Icon = new Image {
						Source = new BitmapImage(new Uri(IconSubtractSquare, UriKind.Relative)),
						Width = 16,
						Height = 16
					};
					contextMenu.Items.Add(tagInactiveItem);
				}

				if (contextMenu.Items.Count > 0) {
					contextMenu.Items.Add(new Separator { Background = new SolidColorBrush(SeparatorBackgroundColor) });
				}
			}

			// Download and View menu items (for files only, including .zstd files)
			if (!selectedFile.IsDirectory) {
				// Download menu item
				var downloadItem = new MenuItem {
					Header = MenuDownload,
					Background = new SolidColorBrush(MenuBackgroundColor),
					Foreground = new SolidColorBrush(MenuForegroundColor),
					Command = _viewModel.RemoteFiles.DownloadRemoteFileCommand,
					CommandParameter = selectedFile,
					IsEnabled = _viewModel.HasWriteAccess
				};
				downloadItem.Icon = new Image {
					Source = new BitmapImage(new Uri(IconDownload, UriKind.Relative)),
					Width = 16,
					Height = 16
				};
				contextMenu.Items.Add(downloadItem);

				// View menu item
				var viewItem = new MenuItem {
					Header = MenuView,
					Background = new SolidColorBrush(MenuBackgroundColor),
					Foreground = new SolidColorBrush(MenuForegroundColor),
					Command = _viewModel.RemoteFiles.ViewRemoteFileCommand,
					CommandParameter = selectedFile,
					IsEnabled = _viewModel.HasWriteAccess
				};
				viewItem.Icon = new Image {
					Source = new BitmapImage(new Uri(IconView, UriKind.Relative)),
					Width = 16,
					Height = 16
				};
				contextMenu.Items.Add(viewItem);

				if (contextMenu.Items.Count > 0) {
					contextMenu.Items.Add(new Separator { Background = new SolidColorBrush(SeparatorBackgroundColor) });
				}
			}

			// Delete menu item (for non-.zstd files only)
			if (!isZstdFile) {
				var deleteItem = new MenuItem {
					Header = MenuDelete,
					Background = new SolidColorBrush(MenuBackgroundColor),
					Foreground = new SolidColorBrush(MenuForegroundColor),
					Command = _viewModel.RemoteFiles.DeleteRemoteFileCommand,
					CommandParameter = selectedFile,
					IsEnabled = _viewModel.HasWriteAccess
				};
				deleteItem.Icon = new Image {
					Source = new BitmapImage(new Uri(IconDelete, UriKind.Relative)),
					Width = 16,
					Height = 16
				};
				contextMenu.Items.Add(deleteItem);
			}
		}

		private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject {
			do {
				if (current is T) {
					return (T)current;
				}
				current = VisualTreeHelper.GetParent(current);
			}
			while (current != null);
			return null;
		}
	}
}
