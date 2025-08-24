using EJRASync.Lib.Models;

namespace EJRASync.Lib.Services {
	public interface IFileService {
		Task<List<LocalFile>> GetLocalFilesAsync(string directoryPath, bool recursive = false);
		Task<string> CalculateFileHashAsync(string filePath);
		bool IsValidDirectory(string path);
		string GetParentDirectory(string path);
		Task<LocalFile?> GetFileInfoAsync(string filePath);
	}
}