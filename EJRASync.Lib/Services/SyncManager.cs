using EJRASync.Lib.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EJRASync.Lib.Services {
	public class SyncManager {
		private readonly IDownloadService _downloadService;
		private readonly IS3Service _s3Service;
		private readonly IHashStoreService _hashStoreService;
		private readonly IProgressService _progressService;

		private string _carsFolder = "cars";
		private string _tracksFolder = "tracks";
		private string _fontsFolder = "fonts";
		private string _appsFolder = "apps";

		public SyncManager(
			IDownloadService downloadService,
			IS3Service s3Service,
			IHashStoreService hashStoreService,
			IProgressService progressService,
			string? acPath = null) {

			_downloadService = downloadService;
			_s3Service = s3Service;
			_hashStoreService = hashStoreService;
			_progressService = progressService;

			var steamPath = SteamHelper.FindSteam();
			_progressService.ShowMessage($"Steam path: {steamPath}");

			if (acPath == null)
				acPath = SteamHelper.FindAssettoCorsa(steamPath);
			else
				_progressService.ShowMessage($"Override Assetto Corsa path: {acPath}");

			_progressService.ShowMessage($"Assetto Corsa path: {acPath}");

			this._carsFolder = Path.Combine(acPath, "content", this._carsFolder);
			_progressService.ShowMessage($"Cars folder: {this._carsFolder}");

			this._tracksFolder = Path.Combine(acPath, "content", this._tracksFolder);
			_progressService.ShowMessage($"Tracks folder: {this._tracksFolder}");

			this._fontsFolder = Path.Combine(acPath, "content", this._fontsFolder);
			_progressService.ShowMessage($"Fonts folder: {this._fontsFolder}");

			this._appsFolder = Path.Combine(acPath, this._appsFolder);
			_progressService.ShowMessage($"Apps folder: {this._appsFolder}");
		}

		public async Task SyncBucketAsync(string bucketName, string localPath, string yamlFile, bool forceInstall) {
			_progressService.ShowMessage($"Syncing {bucketName} to {localPath}...");

			// Ensure the local path exists
			Directory.CreateDirectory(localPath);

			// Get list of files to download based on YAML filter (if provided)
			var filesToDownload = await GetFilesToDownloadAsync(bucketName, localPath, yamlFile, forceInstall);

			if (!filesToDownload.Any()) {
				_progressService.ShowSuccess("All files are up to date!");
				return;
			}

			_progressService.ShowMessage($"Found {filesToDownload.Count} files to download");

			// Download files with progress tracking
			await _progressService.RunWithProgressAsync(async progress => {
				await _downloadService.DownloadFilesAsync(filesToDownload, bucketName, localPath, progress);
			});

			_progressService.ShowSuccess($"Synced {filesToDownload.Count} files to {localPath}");
		}

		private async Task<List<RemoteFile>> GetFilesToDownloadAsync(string bucketName, string localPath, string yamlFile, bool forceInstall) {
			// Get all remote files
			_progressService.ShowMessage($"Scanning remote files in {bucketName}...");
			var remoteFiles = await _s3Service.ListObjectsAsync(bucketName, "", "", CancellationToken.None);
			var filesToConsider = remoteFiles.Where(f => !f.IsDirectory && !f.Key.StartsWith(HashStoreService.HASH_STORE_DIR)).ToList();
			_progressService.ShowMessage($"Found {filesToConsider.Count} remote files to consider");

			// Apply YAML filtering if specified
			if (!string.IsNullOrEmpty(yamlFile)) {
				var yamlObject = filesToConsider.FirstOrDefault(f => f.Key == yamlFile);
				if (yamlObject != null) {
					var allowedPrefixes = await GetYamlAllowedPrefixesAsync(bucketName, yamlFile);
					if (allowedPrefixes.Any()) {
						filesToConsider = filesToConsider
							.Where(f => allowedPrefixes.Any(prefix => f.Key.StartsWith(prefix)))
							.ToList();
					}
				} else {
					_progressService.ShowMessage($"No index found ({yamlFile}), downloading everything!");
				}
			}

			var filesToDownload = new List<RemoteFile>();

			await _progressService.RunWithSimpleProgressAsync($"Checking {filesToConsider.Count} files for updates", filesToConsider.Count, async progress => {
				var checkedFiles = 0;
				foreach (var remoteFile in filesToConsider) {
					checkedFiles++;
					var fileName = Path.GetFileName(remoteFile.Key);
					progress.Report((checkedFiles, fileName));

					var localFilePath = Path.Combine(localPath, remoteFile.Key.Replace('/', Path.DirectorySeparatorChar));

					if (await ShouldDownloadFileAsync(remoteFile, localFilePath, forceInstall)) {
						filesToDownload.Add(remoteFile);
					}
				}
			});

			return filesToDownload;
		}

		private async Task<List<string>> GetYamlAllowedPrefixesAsync(string bucketName, string yamlFile) {
			try {
				_progressService.ShowMessage($"Downloading indexing file: {yamlFile}");
				var tempFilePath = await _s3Service.DownloadObjectAsync(bucketName, yamlFile);

				try {
					_progressService.ShowMessage("Parsing filter configuration...");
					var yamlContents = await File.ReadAllTextAsync(tempFilePath);

					var deserializer = new DeserializerBuilder()
						.WithNamingConvention(UnderscoredNamingConvention.Instance)
						.Build();

					var allowedPrefixes = deserializer.Deserialize<List<string>>(yamlContents);
					_progressService.ShowMessage($"Loaded {allowedPrefixes.Count} allowed prefixes from index");
					return allowedPrefixes;
				} finally {
					if (File.Exists(tempFilePath)) {
						File.Delete(tempFilePath);
					}
				}
			} catch (Exception ex) {
				_progressService.ShowError($"Error reading index file {yamlFile}: {ex.Message}");
				return new List<string>();
			}
		}

		private async Task<bool> ShouldDownloadFileAsync(RemoteFile remoteFile, string localPath, bool forceInstall) {
			if (forceInstall) {
				return true;
			}

			if (!File.Exists(localPath)) {
				return true;
			}

			// If remote file has original hash metadata (compressed files), validate against it
			if (!string.IsNullOrEmpty(remoteFile.OriginalHash)) {
				return !await _downloadService.ValidateFileAsync(localPath, remoteFile.OriginalHash);
			}

			// Fallback: compare using ETag if available
			if (!string.IsNullOrEmpty(remoteFile.ETag)) {
				try {
					var localChecksum = FileChecksum.Calculate(localPath);
					var remoteChecksum = remoteFile.ETag.Trim('"');
					return localChecksum != remoteChecksum;
				} catch {
					return true; // If we can't calculate checksum, download to be safe
				}
			}

			// Final fallback: compare file size and last modified date
			var localInfo = new FileInfo(localPath);
			return localInfo.Length != remoteFile.SizeBytes ||
				   localInfo.LastWriteTime < remoteFile.LastModified;
		}

		public async Task SyncAllAsync(bool forceInstall = false) {
			Dictionary<string, (string, string)> buckets = new Dictionary<string, (string, string)>() {
				[Constants.CarsBucketName] = (_carsFolder, Constants.CarsYamlFile),
				[Constants.TracksBucketName] = (_tracksFolder, Constants.TracksYamlFile),
				[Constants.FontsBucketName] = (_fontsFolder, ""),
				[Constants.AppsBucketName] = (_appsFolder, "")
			};

			foreach (var bucket in buckets) {
				var bucketName = bucket.Key;

				// Initialize bucket hash store
				await _hashStoreService.InitializeBucketAsync(bucketName);

				await SyncBucketAsync(bucketName, bucket.Value.Item1, bucket.Value.Item2, forceInstall);

				// Save any dirty hash stores
				if (_hashStoreService.IsDirty(bucketName)) {
					await _hashStoreService.SaveToRemoteAsync(bucketName);
				}
			}
		}
	}
}