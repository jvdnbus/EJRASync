using Spectre.Console;

namespace EJRASync.Lib.Services {
	public class SpectreProgressService : IProgressService {
		private const int MaxIndividualProgressBars = 8;

		public async Task RunWithProgressAsync(Func<IProgress<DownloadProgress>, Task> operation) {
			if (!AnsiConsole.Profile.Capabilities.Interactive) {
				// Fallback for non-interactive terminals
				await RunWithPercentageProgressAsync(operation);
				return;
			}

			await AnsiConsole.Progress()
				.StartAsync(async ctx => {
					// Overall progress task
					var overallTask = ctx.AddTask("[green]Overall Progress[/]");
					overallTask.MaxValue = 100;

					// Create fixed number of file progress tasks that we'll reuse
					var fileTasks = new ProgressTask[MaxIndividualProgressBars];
					for (int i = 0; i < MaxIndividualProgressBars; i++) {
						fileTasks[i] = ctx.AddTask($"[dim]Waiting...[/]");
						fileTasks[i].MaxValue = 1;
						fileTasks[i].Value = 0;
					}

					DownloadProgress? finalProgress = null;
					var progressWithCapture = new Progress<DownloadProgress>(p => {
						finalProgress = p;

						// Update overall progress
						if (p.TotalFiles > 0) {
							var overallPercent = (double)p.CompletedFiles / p.TotalFiles * 100;
							overallTask.Value = overallPercent;
							overallTask.Description = $"[green]Overall Progress[/] ([cyan]{p.CompletedFiles}/{p.TotalFiles} files[/])";
						}

						// Update individual file progress tasks
						var activeDownloads = p.ActiveDownloads.Take(MaxIndividualProgressBars).ToList();

						for (int i = 0; i < MaxIndividualProgressBars; i++) {
							if (i < activeDownloads.Count) {
								var download = activeDownloads[i];
								var task = fileTasks[i];

								task.MaxValue = Math.Max(download.TotalBytes, 1);
								task.Value = download.CompletedBytes;

								string description;
								int padding = 80;
								if (download.IsFailed) {
									description = $"[red](x) Failed[/] [dim]{download.FileName}[/] - [red]{download.ErrorMessage}[/]";
									padding += 24; // Spectre tags
									task.Value = task.MaxValue; // Show as complete but failed
								} else if (download.IsDecompressing) {
									var fileName = download.FileName;
									if (fileName.Length > 50) {
										fileName = "..." + fileName.Substring(fileName.Length - 47);
									}
									description = $"[yellow]Decompressing[/] [dim]{fileName}[/]";
									padding += 19;
								} else {
									var fileName = download.FileName;
									if (fileName.Length > 50) {
										fileName = "..." + fileName.Substring(fileName.Length - 47);
									}
									description = $"[blue]Downloading[/] [dim]{fileName}[/]";
									padding += 17;
								}
								task.Description = description.PadRight(padding);
							} else {
								// Hide unused tasks
								fileTasks[i].Description = "[dim]...[/]";
								fileTasks[i].Value = 0;
								fileTasks[i].MaxValue = 1;
							}
						}

						// Show additional files count if more than max
						if (p.ActiveDownloads.Count > MaxIndividualProgressBars) {
							var additionalCount = p.ActiveDownloads.Count - MaxIndividualProgressBars;
							// Use the last task to show additional count
							var lastTask = fileTasks[MaxIndividualProgressBars - 1];
							lastTask.Description = $"[dim]+ {additionalCount} more files...[/]";
							lastTask.Value = 1;
							lastTask.MaxValue = 1;
						}
					});

					await operation(progressWithCapture);

					// Ensure overall progress shows 100% when complete
					overallTask.Value = 100;
					overallTask.Description = "[green](✓) Download Complete[/]";

					// Clean up file tasks
					for (int i = 0; i < MaxIndividualProgressBars; i++) {
						fileTasks[i].Description = "[green](✓) Complete[/]";
						fileTasks[i].Value = fileTasks[i].MaxValue;
					}

					// Show failed downloads after completion
					if (finalProgress?.FailedDownloads.Count != 0) {
						foreach (var failed in finalProgress!.FailedDownloads) {
							AnsiConsole.MarkupLine($"[red](x) Failed to download:[/] [dim]{failed.FileName}[/] - [red]{failed.ErrorMessage}[/]");
						}
					}
				});
		}

		public async Task RunWithSimpleProgressAsync(string description, int total, Func<IProgress<(int progress, string currentFile)>, Task> operation) {
			if (!AnsiConsole.Profile.Capabilities.Interactive) {
				// Fallback for non-interactive terminals
				await RunWithSimplePercentageProgressAsync(description, total, operation);
				return;
			}

			await AnsiConsole.Progress()
				.StartAsync(async ctx => {
					var task = ctx.AddTask($"[blue]{description}[/]");
					task.MaxValue = total;

					var progress = new Progress<(int progress, string currentFile)>(p => {
						task.Value = p.progress;
						// Pad or truncate filename to prevent progress bar jumping
						var fileName = p.currentFile ?? "";
						if (fileName.Length > 50) {
							fileName = "..." + fileName.Substring(fileName.Length - 47); // Keep end of path visible
						}
						fileName = fileName.PadRight(50); // Pad to fixed width
						var fileInfo = !string.IsNullOrEmpty(p.currentFile) ? $" - [dim]{fileName}[/]" : "";
						task.Description = $"[blue]{description}[/] ([cyan]{p.progress}/{total}[/]){fileInfo}";
					});

					await operation(progress);

					// Ensure progress shows 100% when complete
					task.Value = total;
					task.Description = $"[green](✓) {description} Complete[/]";
				});
		}

		private async Task RunWithSimplePercentageProgressAsync(string description, int total, Func<IProgress<(int progress, string currentFile)>, Task> operation) {
			var progress = new Progress<(int progress, string currentFile)>(p => {
				var percent = total > 0 ? (double)p.progress / total * 100 : 100;
				// For non-interactive terminals, we don't need to worry, but still truncate very long paths
				var fileName = p.currentFile ?? "";
				if (fileName.Length > 60) {
					fileName = "..." + fileName.Substring(fileName.Length - 57);
				}
				var fileInfo = !string.IsNullOrEmpty(p.currentFile) ? $" - {fileName}" : "";
				Console.WriteLine($"{description}: {percent:F1}% ({p.progress}/{total}){fileInfo}");
			});

			await operation(progress);
			Console.WriteLine($"(✓) {description} Complete");
		}

		private async Task RunWithPercentageProgressAsync(Func<IProgress<DownloadProgress>, Task> operation) {
			DownloadProgress? finalProgress = null;
			var progress = new Progress<DownloadProgress>(p => {
				finalProgress = p;
				if (p.TotalFiles > 0) {
					var percent = (double)p.CompletedFiles / p.TotalFiles * 100;
					Console.WriteLine($"Progress: {percent:F1}% ({p.CompletedFiles}/{p.TotalFiles} files)");

					if (p.ActiveDownloads.Any()) {
						var currentFile = p.ActiveDownloads.FirstOrDefault();
						if (currentFile != null) {
							if (currentFile.IsFailed) {
								// Console.WriteLine($"\n(x) Failed: {currentFile.FileName} - {currentFile.ErrorMessage}\n");
							} else {
								var status = currentFile.IsDecompressing ? "Decompressing" : "Downloading";
								Console.WriteLine($"{status}: {currentFile.FileName}");
							}
						}
					}
				}
			});

			await operation(progress);
			Console.WriteLine("(✓) Download Complete");

			// Show failed downloads after completion
			if (finalProgress?.FailedDownloads.Any() == true) {
				Console.WriteLine();
				Console.WriteLine("Failed Downloads:");
				foreach (var failed in finalProgress.FailedDownloads) {
					Console.WriteLine($"  (x) {failed.FileName} - {failed.ErrorMessage}");
				}
			}
		}

		private static string GetFileKey(string fileName) {
			// Create a safe key for tracking file progress
			return fileName.Replace('\\', '_').Replace('/', '_');
		}

		public void ShowMessage(string message) {
			AnsiConsole.MarkupLine($"[blue](i)[/] {message}");
		}

		public void ShowError(string error) {
			AnsiConsole.MarkupLine($"[red](x)[/] {error}");
		}

		public void ShowSuccess(string message) {
			AnsiConsole.MarkupLine($"[green](✓)[/] {message}");
		}
	}
}