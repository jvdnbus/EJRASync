using EJRASync.UI.Utils;
using EJRASync.UI.Models;
using EJRASync.UI.Services;
using EJRASync.UI.Views;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EJRASync.UI {
	public partial class MainWindow : Views.DarkThemeWindow {
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
			Title = $"EJRA Sync Manager {EJRASync.Lib.Constants.Version}";
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
			
			// Pre-initialize context menus to avoid slow first-time opening
			InitializeContextMenus();
		}


		private async void OnLoaded(object sender, RoutedEventArgs e) {
			_ = Task.Run(async () => {
				try {
					await this.InvokeUIAsync(() => {
						_viewModel.StatusMessage = "Initializing...";
					});

					await _viewModel.InitializeAsync();
					// Load initial remote bucket list in background
					await _viewModel.RemoteFiles.LoadBucketsAsync();

					await this.InvokeUIAsync(() => {
						_viewModel.StatusMessage = "Ready";
					});
				} catch (Exception ex) {
					await this.InvokeUIAsync(() => {
						_viewModel.StatusMessage = $"Initialization failed: {ex.Message}";
					});
				}
			});
		}

		private void InitializeContextMenus() {
			try {
				// Force creation and initialization of context menus by accessing them
				if (LocalFilesDataGrid.ContextMenu != null) {
					LocalFilesDataGrid.ContextMenu.DataContext = DataContext;
					LocalFilesDataGrid.ContextMenu.UpdateLayout();
					// Force measure and arrange to initialize bindings
					LocalFilesDataGrid.ContextMenu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
					LocalFilesDataGrid.ContextMenu.Arrange(new Rect(LocalFilesDataGrid.ContextMenu.DesiredSize));
				}
				
				// Initialize the remote file context menu resource
				if (FindResource("RemoteFileContextMenu") is ContextMenu remoteContextMenu) {
					remoteContextMenu.DataContext = DataContext;
					remoteContextMenu.UpdateLayout();
					// Force measure and arrange to initialize bindings
					remoteContextMenu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
					remoteContextMenu.Arrange(new Rect(remoteContextMenu.DesiredSize));
				}
			}
			catch (Exception ex) {
				// Ignore any errors during pre-initialization
				System.Diagnostics.Debug.WriteLine($"Context menu pre-initialization error: {ex.Message}");
			}
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
				// Backup current selection in case we need to restore it
				_dragSelectionBackup.Clear();
				_dragSelectionBackup.AddRange(dataGrid.SelectedItems.Cast<object>());
				
				// Check if the click is on a selected item with multiple selection
				var hitTestResult = VisualTreeHelper.HitTest(dataGrid, _dragStartPoint);
				if (hitTestResult?.VisualHit != null) {
					var row = FindAncestor<DataGridRow>(hitTestResult.VisualHit);
					if (row != null && row.IsSelected && dataGrid.SelectedItems.Count > 1) {
						// Clicking on a selected item when multiple items are selected
						// Prevent the default selection behavior to preserve multi-selection
						e.Handled = true;
					}
				}
			}
		}

		private void DataGrid_MouseMove(object sender, MouseEventArgs e) {
			if (e.LeftButton == MouseButtonState.Pressed && !_isDragging) {
				Point mousePos = e.GetPosition(null);
				Vector diff = _dragStartPoint - mousePos;

				if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
					Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance) {
					
					var dataGrid = sender as DataGrid;
					if (dataGrid != null) {
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
					var filePaths = localFiles.Where(f => !f.IsDirectory && f.Name != "..").Select(f => f.FullPath).ToArray();
					
					if (filePaths.Any() || localFiles.Any(f => f.IsDirectory && f.Name != "..")) {
						dataObject.SetData(DataFormats.FileDrop, filePaths);
						dataObject.SetData("LocalFiles", localFiles);
						DragDrop.DoDragDrop(sourceDataGrid, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
					}
				} else if (sourceDataGrid == RemoteFilesDataGrid) {
					// Dragging from remote files
					var remoteFiles = RemoteFilesDataGrid.SelectedItems.Cast<RemoteFileItem>().Where(f => f.Name != "..").ToList();
					if (remoteFiles.Any()) {
						dataObject.SetData("RemoteFiles", remoteFiles);
						DragDrop.DoDragDrop(sourceDataGrid, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
					}
				}
			}
			finally {
				_isDragging = false;
			}
		}

		private void LocalFilesDataGrid_DragOver(object sender, DragEventArgs e) {
			// Allow dropping files from Windows Explorer or remote files for download
			if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("RemoteFiles")) {
				e.Effects = DragDropEffects.Copy;
			} else {
				e.Effects = DragDropEffects.None;
			}
			e.Handled = true;
		}

		private async void LocalFilesDataGrid_Drop(object sender, DragEventArgs e) {
			if (e.Data.GetDataPresent("RemoteFiles")) {
				// Internal drag from remote files - download to local
				var remoteFiles = e.Data.GetData("RemoteFiles") as List<RemoteFileItem>;
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
			// Allow dropping local files for upload
			if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("LocalFiles")) {
				e.Effects = DragDropEffects.Copy;
			} else {
				e.Effects = DragDropEffects.None;
			}
			e.Handled = true;
		}

		private async void RemoteFilesDataGrid_Drop(object sender, DragEventArgs e) {
			if (e.Data.GetDataPresent("LocalFiles")) {
				// Internal drag from local files
				var localFiles = e.Data.GetData("LocalFiles") as List<LocalFileItem>;
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
					_viewModel.StatusMessage = "Cannot download: no bucket selected";
					return;
				}

				var bucketName = _viewModel.NavigationContext.SelectedBucket;
				var currentRemotePath = _viewModel.NavigationContext.RemoteCurrentPath ?? "";
				var localCurrentPath = _viewModel.NavigationContext.LocalCurrentPath;

				if (string.IsNullOrEmpty(localCurrentPath) || !Directory.Exists(localCurrentPath)) {
					_viewModel.StatusMessage = "Cannot download: invalid local directory";
					return;
				}

				_viewModel.StatusMessage = "Downloading files...";
				var downloadedCount = 0;

				foreach (var file in remoteFiles.Where(f => !f.IsDirectory)) {
					try {
						var remoteKey = string.IsNullOrEmpty(currentRemotePath) 
							? file.Key 
							: $"{currentRemotePath}/{file.Key}";

						var localFilePath = Path.Combine(localCurrentPath, file.Name);
						
						// Download the file
						var fileData = await _s3Service.DownloadObjectAsync(bucketName, remoteKey);
						
						// Check if file needs decompression (has original-hash metadata)
						if (file.IsCompressed && !string.IsNullOrEmpty(file.OriginalHash)) {
							// Decompress the file
							var tempCompressedFile = Path.GetTempFileName();
							await File.WriteAllBytesAsync(tempCompressedFile, fileData);
							
							await _compressionService.DecompressFileAsync(tempCompressedFile, localFilePath);
							
							// Clean up temp file
							if (File.Exists(tempCompressedFile)) {
								File.Delete(tempCompressedFile);
							}
						} else {
							// Write raw file
							await File.WriteAllBytesAsync(localFilePath, fileData);
						}

						downloadedCount++;
					} catch (Exception ex) {
						System.Diagnostics.Debug.WriteLine($"Error downloading {file.Name}: {ex.Message}");
					}
				}

				_viewModel.StatusMessage = $"Downloaded {downloadedCount} files";
				
				// Refresh local file list to show new files
				await _viewModel.LocalFiles.LoadFilesAsync(localCurrentPath);
			} catch (Exception ex) {
				_viewModel.StatusMessage = $"Error downloading files: {ex.Message}";
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
				foreach (var file in localFiles.Where(f => f.Name != "..")) {
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

				_viewModel.StatusMessage = $"Added {localFiles.Count} items for upload";
			} catch (Exception ex) {
				_viewModel.StatusMessage = $"Error processing drag and drop: {ex.Message}";
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
							DisplaySize = "Folder"
						});
					}
				}

				if (localFiles.Any()) {
					await HandleInternalFileDrop(localFiles);
				}
			} catch (Exception ex) {
				_viewModel.StatusMessage = $"Error processing external file drop: {ex.Message}";
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

		// Helper method to find ancestor of specific type in visual tree
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
