namespace EJRASync.UI.Services {
	public interface ICompressionService {
		Task<string> CompressFileAsync(string inputFilePath, IProgress<int>? progress = null);
		Task<string> CompressDataAsync(byte[] data, IProgress<int>? progress = null);
		Task DecompressFileAsync(string inputFilePath, string outputFilePath, IProgress<int>? progress = null);
		bool ShouldCompress(string fileName, long fileSize);
	}
}
