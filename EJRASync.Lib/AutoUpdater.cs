using System.Diagnostics;
using System.Text.Json;

namespace EJRASync.Lib {
	public class AutoUpdater {
		private string _exePath;

		public AutoUpdater(string exePath) {
			this._exePath = exePath;
		}

		public async Task ProcessUpdates() {
			Console.WriteLine($"Running from {this._exePath}");
			Console.WriteLine("Checking for updates...");

			var release = await this.UpdateAvailable(Constants.Version);
			if (release != null) {
				this.RenameExecutable();
				var result = await this.DownloadUpdate(release);
				if (result) {
					Console.WriteLine("Update complete.");
					this.RestartAndExit();

				} else {
					Console.WriteLine("Update failed.");
				}
			} else {
				Console.WriteLine("No updates available.");
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
					Console.WriteLine($"Found asset: {asset.Name}");
					var latestVersion = new Version(release.TagName.TrimStart('v'));

					var current = new Version(currentVersion);
					if (latestVersion > current)
						return release;
				}
			}

			return null;
		}

		private async Task<GitHubRelease> LatestRelease() {
			try {
				var url = Constants.GithubReleaseURL;
				var client = new HttpClient();
				client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);
				var response = await client.GetAsync(url);
				response.EnsureSuccessStatusCode();

				var json = await response.Content.ReadAsStringAsync();
				var release = JsonSerializer.Deserialize(json, GitHubReleaseContext.Default.GitHubRelease);

				return release;
			} catch (Exception ex) {
				Console.WriteLine(ex.Message);
				return null;
			}
		}

		private void RenameExecutable() {
			File.Move(this._exePath, $"{this._exePath}.OLD");
		}

		private async Task<bool> DownloadUpdate(GitHubRelease release) {
			var client = new HttpClient();
			client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);

			foreach (var asset in release.Assets) {
				if (asset.Name.EndsWith(".exe")) {
					var response = await client.GetAsync(asset.BrowserDownloadUrl);
					response.EnsureSuccessStatusCode();

					var bytes = await response.Content.ReadAsByteArrayAsync();
					File.WriteAllBytes(this._exePath, bytes);

					return true;
				}
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
