using ZstdSharp;

namespace EJRASync.Lib.Services {
	public class CompressionService : ICompressionService {
		private static readonly HashSet<string> CompressibleExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".dds", ".png", ".jpg", ".jpeg", ".bmp", ".tga",
			".ini", ".txt", ".cfg", ".json", ".xml", ".yaml", ".yml", ".lut", ".csv", ".2Dlut",
			".fx", ".mp3", ".wav",  ".exe", ".dll",
			".fbx", ".obj", ".lua", ".ttf", ".bank", ".ai", ".bin", ".py", ".pyc", ".pyd", ".ahk",
			".acd", ".knh", ".kn5", ".ksanim", ".vao-patch", ".log"
		};

		private const long MinCompressionSize = 1024;
		private const int CompressionLevel = 3; // Balanced compression level

		public async Task<string> CompressFileAsync(string inputFilePath, IProgress<int>? progress = null) {
			var tempCompressedFile = Path.GetTempFileName();

			await Task.Run(() => {
				progress?.Report(0);

				using var inputStream = File.OpenRead(inputFilePath);
				using var outputStream = File.OpenWrite(tempCompressedFile);
				using var compressionStream = new CompressionStream(outputStream, CompressionLevel);

				inputStream.CopyTo(compressionStream);
				progress?.Report(100);
			});

			return tempCompressedFile;
		}

		public async Task<string> CompressDataAsync(byte[] data, IProgress<int>? progress = null) {
			var tempCompressedFile = Path.GetTempFileName();

			await Task.Run(() => {
				progress?.Report(0);

				using var inputStream = new MemoryStream(data);
				using var outputStream = new FileStream(tempCompressedFile, FileMode.Create, FileAccess.Write);
				using var compressionStream = new CompressionStream(outputStream, CompressionLevel);

				inputStream.CopyTo(compressionStream);
				progress?.Report(100);
			});

			return tempCompressedFile;
		}

		public async Task DecompressFileAsync(string inputFilePath, string outputFilePath, IProgress<int>? progress = null) {
			await Task.Run(() => {
				progress?.Report(0);

				using var inputStream = File.OpenRead(inputFilePath);
				using var outputStream = File.OpenWrite(outputFilePath);
				using var decompressionStream = new DecompressionStream(inputStream);

				decompressionStream.CopyTo(outputStream);
				progress?.Report(100);
			});
		}

		public bool ShouldCompress(string fileName, long fileSize) {
			// Disregard size for now
			// if (fileSize < MinCompressionSize)
			// 	return false;

			var extension = Path.GetExtension(fileName);
			return CompressibleExtensions.Contains(extension);
		}
	}
}