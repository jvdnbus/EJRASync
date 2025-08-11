using System.IO;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EJRASync.UI.Services {
	public class ContentStatusService : IContentStatusService {
		private readonly IS3Service _s3Service;
		private readonly Dictionary<string, HashSet<string>> _activeContent;
		private readonly Dictionary<string, string> _bucketYamlFiles;

		public event EventHandler<ContentStatusChangedEventArgs>? ContentStatusChanged;

		public ContentStatusService(IS3Service s3Service) {
			_s3Service = s3Service;
			_activeContent = new Dictionary<string, HashSet<string>>();
			_bucketYamlFiles = new Dictionary<string, string>
			{
				{ EJRASync.Lib.Constants.CarsBucketName, EJRASync.Lib.Constants.CarsYamlFile },
				{ EJRASync.Lib.Constants.TracksBucketName, EJRASync.Lib.Constants.TracksYamlFile }
			};
		}

		public async Task InitializeAsync() {
			var buckets = new[] { EJRASync.Lib.Constants.CarsBucketName, EJRASync.Lib.Constants.TracksBucketName };

			foreach (var bucket in buckets) {
				if (_bucketYamlFiles.TryGetValue(bucket, out var yamlFile)) {
					await LoadYamlForBucketAsync(bucket, yamlFile);
				}
			}
		}

		private async Task LoadYamlForBucketAsync(string bucketName, string yamlFileName) {
			try {
				var tempFilePath = await _s3Service.DownloadObjectAsync(bucketName, yamlFileName, null, null);
				var yamlContent = await File.ReadAllTextAsync(tempFilePath);
				
				// Clean up temp file
				if (File.Exists(tempFilePath)) {
					File.Delete(tempFilePath);
				}

				var deserializer = new DeserializerBuilder()
					.WithNamingConvention(UnderscoredNamingConvention.Instance)
					.Build();

				var activeList = deserializer.Deserialize<List<string>>(yamlContent) ?? new List<string>();
				_activeContent[bucketName] = new HashSet<string>(activeList, StringComparer.OrdinalIgnoreCase);
			} catch (Exception ex) {
				// If YAML file doesn't exist or can't be loaded, start with empty set
				Console.WriteLine($"Could not load {yamlFileName} from {bucketName}: {ex.Message}");
				_activeContent[bucketName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
		}

		public bool? IsContentActive(string bucketName, string contentName) {
			if (!_bucketYamlFiles.ContainsKey(bucketName))
				return null; // Not applicable for this bucket

			if (!_activeContent.TryGetValue(bucketName, out var activeSet))
				return null;

			return activeSet.Contains(contentName);
		}

		public async Task SetContentActiveAsync(string bucketName, string contentName, bool isActive) {
			if (!_bucketYamlFiles.ContainsKey(bucketName))
				throw new ArgumentException($"Bucket {bucketName} does not support active/inactive content management");

			if (!_activeContent.TryGetValue(bucketName, out var activeSet)) {
				activeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				_activeContent[bucketName] = activeSet;
			}

			bool changed = false;
			if (isActive && !activeSet.Contains(contentName)) {
				activeSet.Add(contentName);
				changed = true;
			} else if (!isActive && activeSet.Contains(contentName)) {
				activeSet.Remove(contentName);
				changed = true;
			}

			if (changed) {
				ContentStatusChanged?.Invoke(this, new ContentStatusChangedEventArgs {
					BucketName = bucketName,
					ContentName = contentName,
					IsActive = isActive
				});
			}
		}

		public async Task<byte[]> GenerateYamlAsync(string bucketName) {
			if (!_activeContent.TryGetValue(bucketName, out var activeSet))
				activeSet = new HashSet<string>();

			var activeList = activeSet.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

			var serializer = new SerializerBuilder()
				.WithNamingConvention(UnderscoredNamingConvention.Instance)
				.Build();

			var yamlContent = serializer.Serialize(activeList);
			return Encoding.UTF8.GetBytes(yamlContent);
		}

		public List<string> GetActiveContent(string bucketName) {
			if (!_activeContent.TryGetValue(bucketName, out var activeSet))
				return new List<string>();

			return activeSet.ToList();
		}
	}
}