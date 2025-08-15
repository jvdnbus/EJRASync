using EJRASync.Lib.Services;
using log4net;
using System.Windows.Threading;

namespace EJRASync.UI.Services {
	public class UIProgressService : IProgressService {
		private static readonly ILog _fileOnlyLogger = EJRASync.Lib.LoggingHelper.GetFileOnlyLogger(typeof(UIProgressService));
		private readonly Action<string> _updateStatusMessage;
		private readonly Action<double> _updateProgress;
		private readonly Action<bool> _setIndeterminate;
		private readonly Dispatcher _dispatcher;

		public UIProgressService(Action<string> updateStatusMessage, Action<double> updateProgress, Action<bool> setIndeterminate, Dispatcher dispatcher) {
			_updateStatusMessage = updateStatusMessage;
			_updateProgress = updateProgress;
			_setIndeterminate = setIndeterminate;
			_dispatcher = dispatcher;
		}

		public async Task RunWithProgressAsync(Func<IProgress<DownloadProgress>, Task> operation) {
			var progress = new Progress<DownloadProgress>(p => {
				_dispatcher.Invoke(() => {
					if (p.TotalFiles > 0) {
						var percent = (double)p.CompletedFiles / p.TotalFiles * 100;
						_updateProgress(percent);
						_updateStatusMessage($"Downloading files: {p.CompletedFiles}/{p.TotalFiles} ({percent:F1}%)");
						_setIndeterminate(false);
					} else {
						_updateStatusMessage("Preparing download...");
						_setIndeterminate(true);
					}
				});

				// Log progress to file
				if (p.TotalFiles > 0) {
					var percent = (double)p.CompletedFiles / p.TotalFiles * 100;
					//_fileOnlyLogger.Debug($"Download progress: {percent:F1}% ({p.CompletedFiles}/{p.TotalFiles} files)");
				}
			});

			await operation(progress);

			_dispatcher.Invoke(() => {
				_updateProgress(100);
				_updateStatusMessage("Download complete");
				_setIndeterminate(false);
			});
		}

		public async Task RunWithSimpleProgressAsync(string description, int total, Func<IProgress<(int progress, string currentFile)>, Task> operation) {
			var progress = new Progress<(int progress, string currentFile)>(p => {
				_dispatcher.Invoke(() => {
					if (total > 0) {
						var percent = (double)p.progress / total * 100;
						_updateProgress(percent);
						_updateStatusMessage($"{description}: {p.progress}/{total} ({percent:F1}%)");
						_setIndeterminate(false);
					} else {
						_updateStatusMessage(description);
						_setIndeterminate(true);
					}
				});

				// Log progress to file
				if (total > 0) {
					var percent = (double)p.progress / total * 100;
					//_fileOnlyLogger.Debug($"{description}: {percent:F1}% ({p.progress}/{total})");
				}
			});

			await operation(progress);

			_dispatcher.Invoke(() => {
				_updateProgress(100);
				_updateStatusMessage($"{description} complete");
				_setIndeterminate(false);
			});
		}

		public void ShowMessage(string message) {
			_dispatcher.Invoke(() => {
				_updateStatusMessage(message);
			});
			_fileOnlyLogger.Info(message);
		}

		public void ShowError(string error) {
			_dispatcher.Invoke(() => {
				_updateStatusMessage($"Error: {error}");
			});
			_fileOnlyLogger.Error(error);
		}

		public void ShowSuccess(string message) {
			_dispatcher.Invoke(() => {
				_updateStatusMessage(message);
			});
			_fileOnlyLogger.Info(message);
		}
	}
}