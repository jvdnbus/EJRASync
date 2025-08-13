using EJRASync.Lib.Utils;
using System.Diagnostics;
using System.Text.Json;

namespace EJRASync.Lib {
	public class AutoUpdater {
		private string _exePath;
		private Action<string> _logAction;
		private Action<int>? _progressAction;

		public AutoUpdater(string exePath, Action<string> logAction, Action<int>? progressAction = null) {
			this._exePath = exePath;
			this._logAction = logAction;
			this._progressAction = progressAction;
		}

		public async Task ProcessUpdates() {
			_logAction($"Running from {PathUtils.NormalizePath(this._exePath)}");
			_logAction("Checking for updates...");

			var release = await this.UpdateAvailable(Constants.Version);
			if (release != null) {
				this.RenameExecutable();
				var result = await this.DownloadUpdate(release);
				if (result) {
					_logAction("Update complete.");
					this.RestartAndExit();

				} else {
					_logAction("Update failed.");
				}
			} else {
				_logAction("No updates available.");
			}
		}

		private async Task<GitHubRelease?> UpdateAvailable(string currentVersion) {
			// Get the JSON from the GitHub API
			// Parse the JSON
			// Compare the version number

			var release = await this.LatestRelease();

			if (release == null) {
				return null;
			}

			foreach (var asset in release.Assets) {
				if (asset.Name.EndsWith(".exe")) {
					_logAction($"Found asset: {asset.Name}");
					var latestVersion = new Version(release.TagName.TrimStart('v'));

					var current = new Version(currentVersion);
					if (latestVersion > current)
						return release;
				}
			}

			return null;
		}

		private async Task<GitHubRelease?> LatestRelease() {
			try {
				var url = Constants.GithubReleaseURL;
				using var client = new HttpClient();
				client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);
				var response = await client.GetAsync(url);

				if (response.IsSuccessStatusCode) {
					var json = await response.Content.ReadAsStringAsync();
					var release = JsonSerializer.Deserialize(json, GitHubReleaseContext.Default.GitHubRelease);
					return release;
				} else if (response.StatusCode == System.Net.HttpStatusCode.NotFound) {
					return null;
				}
				response.EnsureSuccessStatusCode();
			} catch (Exception ex) {
				_logAction(ex.Message);
			}
			return null;
		}

		private void RenameExecutable() {
			File.Move(this._exePath, $"{this._exePath}.OLD");
		}

		public async Task<GitHubRelease?> CheckForUpdate() {
			_logAction("Checking for updates...");
			return await this.UpdateAvailable(Constants.Version);
		}

		public async Task<bool> DownloadAndInstallUpdate(GitHubRelease release) {
			try {
				this.RenameExecutable();
				var result = await this.DownloadUpdate(release);
				if (result) {
					_logAction("Update complete.");
					this.RestartAndExit();
					return true;
				} else {
					_logAction("Update failed.");
					return false;
				}
			} catch (Exception ex) {
				_logAction($"Update failed: {ex.Message}");
				return false;
			}
		}

		private async Task<bool> DownloadUpdate(GitHubRelease release) {
			using var client = new HttpClient();
			client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);

			try {
				foreach (var asset in release.Assets) {
					if (asset.Name.EndsWith(".exe")) {
						_logAction($"Downloading {asset.Name}...");

						var response = await client.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
						response.EnsureSuccessStatusCode();

						var totalBytes = response.Content.Headers.ContentLength ?? 0;
						var downloadedBytes = 0L;

						using var stream = await response.Content.ReadAsStreamAsync();
						using var fileStream = new FileStream(this._exePath, FileMode.Create, FileAccess.Write, FileShare.None);

						var buffer = new byte[8192];
						int bytesRead;

						while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
							await fileStream.WriteAsync(buffer, 0, bytesRead);
							downloadedBytes += bytesRead;

							if (totalBytes > 0 && _progressAction != null) {
								var progress = (int)((downloadedBytes * 100) / totalBytes);
								_progressAction(progress);
							}
						}

						return true;
					}
				}
			} catch (Exception ex) {
				_logAction($"Downloading update failed: {ex.Message}");
				return false;
			}

			return false;
		}

		private void RestartAndExit() {
			var newProcess = new Process {
				StartInfo = new ProcessStartInfo {
					FileName = this._exePath,
					UseShellExecute = true,
				}
			};

			newProcess.Start();
			Environment.Exit(0);
		}
	}
}
