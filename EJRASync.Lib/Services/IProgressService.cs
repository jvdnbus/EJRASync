namespace EJRASync.Lib.Services {
	public interface IProgressService {
		Task RunWithProgressAsync(Func<IProgress<DownloadProgress>, Task> operation);
		Task RunWithSimpleProgressAsync(string description, int total, Func<IProgress<(int progress, string currentFile)>, Task> operation);
		void ShowMessage(string message);
		void ShowError(string error);
		void ShowSuccess(string message);
	}
}