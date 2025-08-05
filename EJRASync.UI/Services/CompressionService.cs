using System.IO;
using ZstdSharp;

namespace EJRASync.UI.Services {
	public class CompressionService : ICompressionService {
		private static readonly HashSet<string> CompressibleExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".dds", ".png", ".jpg", ".jpeg", ".bmp", ".tga",
			".ini", ".txt", ".cfg", ".json", ".xml", ".yaml", ".yml",
			".kn5", ".fbx", ".obj", ".lua"
		};

		private const long MinCompressionSize = 1024; // Don't compress files smaller than 1KB
		private const int CompressionLevel = 3; // Balanced compression level

		public async Task<byte[]> CompressFileAsync(string filePath, IProgress<int>? progress = null) {
			var data = await File.ReadAllBytesAsync(filePath);
			return await CompressDataAsync(data, progress);
		}

		public async Task<byte[]> CompressDataAsync(byte[] data, IProgress<int>? progress = null) {
			return await Task.Run(() => {
				progress?.Report(0);
				using var compressor = new Compressor(CompressionLevel);
				var compressed = compressor.Wrap(data).ToArray();
				progress?.Report(100);
				return compressed;
			});
		}

		public async Task<byte[]> DecompressDataAsync(byte[] compressedData, IProgress<int>? progress = null) {
			return await Task.Run(() => {
				progress?.Report(0);
				using var decompressor = new Decompressor();
				var decompressed = decompressor.Unwrap(compressedData).ToArray();
				progress?.Report(100);
				return decompressed;
			});
		}

		public bool ShouldCompress(string fileName, long fileSize) {
			if (fileSize < MinCompressionSize)
				return false;

			var extension = Path.GetExtension(fileName);
			return CompressibleExtensions.Contains(extension);
		}
	}
}