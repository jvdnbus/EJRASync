using EJRASync.Lib.Models;
using EJRASync.Lib.Utils;
using log4net;
using System.Security.Cryptography;

namespace EJRASync.Lib.Services {
	public class FileService : IFileService {
		private static readonly ILog _logger = LoggingHelper.GetLogger(typeof(FileService));
		public async Task<List<LocalFile>> GetLocalFilesAsync(string directoryPath, bool recursive = false) {
			var items = new List<LocalFile>();

			if (!Directory.Exists(directoryPath))
				return items;

			await Task.Run(() => {
				try {
					if (recursive) {
						// Use EnumerateFileSystemEntries for recursive enumeration
						var searchOption = SearchOption.AllDirectories;
						var entries = Directory.EnumerateFileSystemEntries(directoryPath, "*", searchOption);

						foreach (var entry in entries) {
							try {
								if (Directory.Exists(entry)) {
									var dirInfo = new DirectoryInfo(entry);
									items.Add(new LocalFile {
										Name = GetRelativePath(directoryPath, entry),
										FullPath = entry,
										DisplaySize = "Folder",
										LastModified = dirInfo.LastWriteTime,
										IsDirectory = true
									});
								} else if (File.Exists(entry)) {
									var fileInfo = new FileInfo(entry);
									items.Add(new LocalFile {
										Name = GetRelativePath(directoryPath, entry),
										FullPath = entry,
										DisplaySize = FileSizeFormatter.FormatFileSize(fileInfo.Length),
										LastModified = fileInfo.LastWriteTime,
										SizeBytes = fileInfo.Length,
										IsDirectory = false
									});
								}
							} catch (UnauthorizedAccessException) {
								// Skip entries we can't access
							} catch (Exception ex) {
								_logger.Error($"Error processing entry {entry}: {ex.Message}");
							}
						}
					} else {
						// Non-recursive: Only current directory
						// Add directories first
						var directories = Directory.EnumerateDirectories(directoryPath);
						foreach (var dir in directories) {
							try {
								var dirInfo = new DirectoryInfo(dir);
								items.Add(new LocalFile {
									Name = dirInfo.Name,
									FullPath = dir,
									DisplaySize = "Folder",
									LastModified = dirInfo.LastWriteTime,
									IsDirectory = true
								});
							} catch (UnauthorizedAccessException) {
								// Skip directories we can't access
							} catch (Exception ex) {
								_logger.Error($"Error processing directory {dir}: {ex.Message}");
							}
						}

						// Add files
						var files = Directory.EnumerateFiles(directoryPath);
						foreach (var file in files) {
							try {
								var fileInfo = new FileInfo(file);
								items.Add(new LocalFile {
									Name = fileInfo.Name,
									FullPath = file,
									DisplaySize = FileSizeFormatter.FormatFileSize(fileInfo.Length),
									LastModified = fileInfo.LastWriteTime,
									SizeBytes = fileInfo.Length,
									IsDirectory = false
								});
							} catch (UnauthorizedAccessException) {
								// Skip files we can't access
							} catch (Exception ex) {
								_logger.Error($"Error processing file {file}: {ex.Message}");
							}
						}
					}
				} catch (UnauthorizedAccessException) {
					// Handle access denied to the root directory
				} catch (Exception ex) {
					_logger.Error($"Error listing files in {directoryPath}: {ex.Message}");
				}
			});

			return items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name).ToList();
		}

		private string GetRelativePath(string basePath, string fullPath) {
			// Get relative path for recursive listings
			var baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
			var fullUri = new Uri(fullPath);
			return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
		}

		public async Task<string> CalculateFileHashAsync(string filePath) {
			using var md5 = MD5.Create();
			using var stream = File.OpenRead(filePath);
			var hashBytes = await md5.ComputeHashAsync(stream);
			return Convert.ToHexString(hashBytes).ToLowerInvariant();
		}


		public bool IsValidDirectory(string path) {
			return Directory.Exists(path);
		}

		public string GetParentDirectory(string path) {
			var parent = Directory.GetParent(path);
			return parent?.FullName ?? path;
		}

		public async Task<LocalFile?> GetFileInfoAsync(string filePath) {
			try {
				if (Directory.Exists(filePath)) {
					var dirInfo = new DirectoryInfo(filePath);
					return new LocalFile {
						Name = dirInfo.Name,
						FullPath = filePath,
						DisplaySize = "Folder",
						LastModified = dirInfo.LastWriteTime,
						IsDirectory = true
					};
				} else if (File.Exists(filePath)) {
					var fileInfo = new FileInfo(filePath);
					var hash = await CalculateFileHashAsync(filePath);

					return new LocalFile {
						Name = fileInfo.Name,
						FullPath = filePath,
						DisplaySize = FileSizeFormatter.FormatFileSize(fileInfo.Length),
						LastModified = fileInfo.LastWriteTime,
						SizeBytes = fileInfo.Length,
						IsDirectory = false,
						FileHash = hash
					};
				}
			} catch (Exception ex) {
				_logger.Error($"Error getting file info for {filePath}: {ex.Message}");
			}

			return null;
		}
	}
}