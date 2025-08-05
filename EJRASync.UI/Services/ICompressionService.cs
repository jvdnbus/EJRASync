namespace EJRASync.UI.Services {
	public interface ICompressionService {
		Task<byte[]> CompressFileAsync(string filePath, IProgress<int>? progress = null);
		Task<byte[]> CompressDataAsync(byte[] data, IProgress<int>? progress = null);
		Task<byte[]> DecompressDataAsync(byte[] compressedData, IProgress<int>? progress = null);
		bool ShouldCompress(string fileName, long fileSize);
	}
}