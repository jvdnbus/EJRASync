using EJRASync.UI.Models;
using System.IO;
using System.Security.Cryptography;

namespace EJRASync.UI.Services {
	public class FileService : IFileService {
		public async Task<List<LocalFileItem>> GetLocalFilesAsync(string directoryPath, bool recursive = false) {
			var items = new List<LocalFileItem>();

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
									items.Add(new LocalFileItem {
										Name = GetRelativePath(directoryPath, entry),
										FullPath = entry,
										DisplaySize = "Folder",
										LastModified = dirInfo.LastWriteTime,
										IsDirectory = true
									});
								} else if (File.Exists(entry)) {
									var fileInfo = new FileInfo(entry);
									items.Add(new LocalFileItem {
										Name = GetRelativePath(directoryPath, entry),
										FullPath = entry,
										DisplaySize = FormatFileSize(fileInfo.Length),
										LastModified = fileInfo.LastWriteTime,
										SizeBytes = fileInfo.Length,
										IsDirectory = false
									});
								}
							} catch (UnauthorizedAccessException) {
								// Skip entries we can't access
							} catch (Exception ex) {
								Console.WriteLine($"Error processing entry {entry}: {ex.Message}");
							}
						}
					} else {
						// Non-recursive: Only current directory
						// Add directories first
						var directories = Directory.EnumerateDirectories(directoryPath);
						foreach (var dir in directories) {
							try {
								var dirInfo = new DirectoryInfo(dir);
								items.Add(new LocalFileItem {
									Name = dirInfo.Name,
									FullPath = dir,
									DisplaySize = "Folder",
									LastModified = dirInfo.LastWriteTime,
									IsDirectory = true
								});
							} catch (UnauthorizedAccessException) {
								// Skip directories we can't access
							} catch (Exception ex) {
								Console.WriteLine($"Error processing directory {dir}: {ex.Message}");
							}
						}

						// Add files
						var files = Directory.EnumerateFiles(directoryPath);
						foreach (var file in files) {
							try {
								var fileInfo = new FileInfo(file);
								items.Add(new LocalFileItem {
									Name = fileInfo.Name,
									FullPath = file,
									DisplaySize = FormatFileSize(fileInfo.Length),
									LastModified = fileInfo.LastWriteTime,
									SizeBytes = fileInfo.Length,
									IsDirectory = false
								});
							} catch (UnauthorizedAccessException) {
								// Skip files we can't access
							} catch (Exception ex) {
								Console.WriteLine($"Error processing file {file}: {ex.Message}");
							}
						}
					}
				} catch (UnauthorizedAccessException) {
					// Handle access denied to the root directory
				} catch (Exception ex) {
					Console.WriteLine($"Error listing files in {directoryPath}: {ex.Message}");
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

		public string FormatFileSize(long bytes) {
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

		public bool IsValidDirectory(string path) {
			return Directory.Exists(path);
		}

		public string GetParentDirectory(string path) {
			var parent = Directory.GetParent(path);
			return parent?.FullName ?? path;
		}

		public async Task<LocalFileItem?> GetFileInfoAsync(string filePath) {
			try {
				if (Directory.Exists(filePath)) {
					var dirInfo = new DirectoryInfo(filePath);
					return new LocalFileItem {
						Name = dirInfo.Name,
						FullPath = filePath,
						DisplaySize = "Folder",
						LastModified = dirInfo.LastWriteTime,
						IsDirectory = true
					};
				} else if (File.Exists(filePath)) {
					var fileInfo = new FileInfo(filePath);
					var hash = await CalculateFileHashAsync(filePath);

					return new LocalFileItem {
						Name = fileInfo.Name,
						FullPath = filePath,
						DisplaySize = FormatFileSize(fileInfo.Length),
						LastModified = fileInfo.LastWriteTime,
						SizeBytes = fileInfo.Length,
						IsDirectory = false,
						FileHash = hash
					};
				}
			} catch (Exception ex) {
				Console.WriteLine($"Error getting file info for {filePath}: {ex.Message}");
			}

			return null;
		}
	}
}