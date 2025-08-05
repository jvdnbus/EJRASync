using EJRASync.UI.Models;

namespace EJRASync.UI.Services {
	public interface IFileService {
		Task<List<LocalFileItem>> GetLocalFilesAsync(string directoryPath);
		Task<string> CalculateFileHashAsync(string filePath);
		string FormatFileSize(long bytes);
		bool IsValidDirectory(string path);
		string GetParentDirectory(string path);
		Task<LocalFileItem?> GetFileInfoAsync(string filePath);
	}
}