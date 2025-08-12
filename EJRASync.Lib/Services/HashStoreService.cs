using System.Collections.Concurrent;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EJRASync.Lib.Services {
	public class HashStoreService : IHashStoreService {
		private readonly Func<IS3Service> _s3ServiceFactory;
		private readonly IProgressService _progressService;
		private readonly ConcurrentDictionary<string, Dictionary<string, string>> _bucketHashStores = new();
		private readonly ConcurrentDictionary<string, bool> _dirtyFlags = new();
		public const string HASH_STORE_DIR = ".zstd";
		public const string HASH_STORE_KEY = $"{HASH_STORE_DIR}/hash-store.yaml";

		public HashStoreService(Func<IS3Service> s3ServiceFactory, IProgressService progressService) {
			_s3ServiceFactory = s3ServiceFactory;
			_progressService = progressService;
		}

		public async Task InitializeBucketAsync(string bucketName) {
			if (_bucketHashStores.ContainsKey(bucketName)) {
				return; // Already initialized
			}

			_progressService.ShowMessage($"Initializing hash store for bucket: {bucketName}");

			var hashStore = new Dictionary<string, string>();

			try {
				// Try to download existing hash store
				var tempFilePath = await _s3ServiceFactory().DownloadObjectAsync(bucketName, HASH_STORE_KEY);

				try {
					var yamlContents = await File.ReadAllTextAsync(tempFilePath);
					if (!string.IsNullOrWhiteSpace(yamlContents)) {
						var deserializer = new DeserializerBuilder()
							.WithNamingConvention(UnderscoredNamingConvention.Instance)
							.Build();

						var deserializedStore = deserializer.Deserialize<Dictionary<string, string>>(yamlContents);
						if (deserializedStore != null) {
							hashStore = deserializedStore;
							_progressService.ShowMessage($"Loaded {hashStore.Count} hash entries for {bucketName}");
						}
					}
				} finally {
					if (File.Exists(tempFilePath)) {
						File.Delete(tempFilePath);
					}
				}
			} catch (Exception ex) {
				// Hash store doesn't exist yet, start with empty store
				_progressService.ShowMessage($"No existing hash store found for {bucketName}, creating new one");
			}

			_bucketHashStores[bucketName] = hashStore;
			_dirtyFlags[bucketName] = false;
		}

		public string? GetOriginalHash(string bucketName, string key) {
			if (_bucketHashStores.TryGetValue(bucketName, out var hashStore)) {
				return hashStore.TryGetValue(key, out var hash) ? hash : null;
			}
			return null;
		}

		public void SetOriginalHash(string bucketName, string key, string originalHash) {
			if (!_bucketHashStores.TryGetValue(bucketName, out var hashStore)) {
				hashStore = new Dictionary<string, string>();
				_bucketHashStores[bucketName] = hashStore;
			}

			hashStore[key] = originalHash;
			_dirtyFlags[bucketName] = true;
		}

		public void RemoveHash(string bucketName, string key) {
			if (_bucketHashStores.TryGetValue(bucketName, out var hashStore)) {
				if (hashStore.Remove(key)) {
					_dirtyFlags[bucketName] = true;
				}
			}
		}

		public bool IsDirty(string bucketName) {
			return _dirtyFlags.TryGetValue(bucketName, out var isDirty) && isDirty;
		}

		public async Task SaveToRemoteAsync(string bucketName) {
			if (!_bucketHashStores.TryGetValue(bucketName, out var hashStore)) {
				return;
			}

			_progressService.ShowMessage($"Saving hash store for bucket: {bucketName}");

			var serializer = new SerializerBuilder()
				.WithNamingConvention(UnderscoredNamingConvention.Instance)
				.Build();

			var yamlContent = serializer.Serialize(hashStore);
			var yamlBytes = System.Text.Encoding.UTF8.GetBytes(yamlContent);

			await _s3ServiceFactory().UploadDataAsync(bucketName, HASH_STORE_KEY, yamlBytes);

			_dirtyFlags[bucketName] = false;
			_progressService.ShowMessage($"Hash store saved with {hashStore.Count} entries");
		}

		public void MarkClean(string bucketName) {
			_dirtyFlags[bucketName] = false;
		}

		public async Task RebuildHashStoreAsync(string bucketName) {
			_progressService.ShowMessage($"Rebuilding hash store for bucket: {bucketName}");

			var s3Service = _s3ServiceFactory();
			var hashStore = new Dictionary<string, string>();
			int scannedFiles = 0;
			int foundHashes = 0;

			// Get all objects in the bucket (without delimiter to get all files recursively)
			var allObjects = await s3Service.ListObjectsAsync(bucketName, "", "");
			var files = allObjects.Where(obj => !obj.IsDirectory && !obj.Key.StartsWith(HASH_STORE_DIR)).ToList();

			_progressService.ShowMessage($"Scanning {files.Count} files for original hash metadata...");

			foreach (var file in files) {
				scannedFiles++;
				if (scannedFiles % 50 == 0) {
					_progressService.ShowMessage($"Scanned {scannedFiles}/{files.Count} files...");
				}

				try {
					// Get the object metadata directly from S3 to check for original-hash
					// We can't use the S3Service.GetObjectMetadataAsync as it would use this hash store service
					// So we need access to the underlying S3 client - we'll add this to the S3Service
					var originalHash = await s3Service.GetObjectOriginalHashFromMetadataAsync(bucketName, file.Key);
					if (!string.IsNullOrEmpty(originalHash)) {
						hashStore[file.Key] = originalHash;
						foundHashes++;
					}
				} catch (Exception ex) {
					_progressService.ShowMessage($"Warning: Could not get metadata for {file.Key}: {ex.Message}");
				}
			}

			// Update the in-memory hash store
			_bucketHashStores[bucketName] = hashStore;
			_dirtyFlags[bucketName] = true;

			_progressService.ShowMessage($"Rebuilt hash store with {foundHashes} hashes from {scannedFiles} files");
			_progressService.ShowMessage($"Saving rebuilt hash store to remote...");

			// Save to remote immediately
			await SaveToRemoteAsync(bucketName);

			_progressService.ShowSuccess($"Hash store rebuild complete for {bucketName}");
		}
	}
}