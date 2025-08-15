using log4net;
using log4net.Config;
using System.Reflection;

namespace EJRASync.Lib {
	public static class LoggingHelper {
		private static bool _isConfigured = false;

		static LoggingHelper() {
			// Set these as early as possible to prevent log4net from accessing configuration system
			Environment.SetEnvironmentVariable("log4net.Internal.Debug", "false");
			Environment.SetEnvironmentVariable("log4net.DisableXmlLogging", "true");
			Environment.SetEnvironmentVariable("log4net.Configuration.Watch", "false");
		}

		public static void ConfigureLogging(string appType = "CLI", int maxBackups = 10) {
			if (_isConfigured) return;

			// Completely disable log4net's configuration system access
			Environment.SetEnvironmentVariable("log4net.Internal.Debug", "false");
			Environment.SetEnvironmentVariable("log4net.DisableXmlLogging", "true");
			Environment.SetEnvironmentVariable("log4net.Configuration.Watch", "false");

			var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EJRASync.Logs");
			if (!Directory.Exists(logDir)) {
				Directory.CreateDirectory(logDir);
			}

			CleanupOldLogFiles(logDir, appType, maxBackups);

			// Set properties for the log4net configuration
			GlobalContext.Properties["AppType"] = appType;
			GlobalContext.Properties["LogFileName"] = $"{appType}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";

			// Extract embedded config file to temp location
			var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
			var resourceName = assembly.GetManifestResourceNames()
				.FirstOrDefault(name => name.EndsWith("log4net.config"));

			if (resourceName != null) {
				var tempConfigPath = Path.Combine(Path.GetTempPath(), "EJRASync_log4net.config");
				using (var resource = assembly.GetManifestResourceStream(resourceName))
				using (var file = File.Create(tempConfigPath)) {
					resource!.CopyTo(file);
				}

				XmlConfigurator.ConfigureAndWatch(
					LogManager.GetRepository(assembly),
					new FileInfo(tempConfigPath)
				);
			} else {
				// Fallback to basic configuration if embedded resource not found
				BasicConfigurator.Configure(LogManager.GetRepository(assembly));
			}

			_isConfigured = true;
		}

		public static ILog GetLogger(Type type) {
			return LogManager.GetLogger(type);
		}

		public static ILog GetLogger(string name) {
			return LogManager.GetLogger(name);
		}

		public static ILog GetFileOnlyLogger(Type type) {
			return LogManager.GetLogger("FileOnly");
		}

		private static void CleanupOldLogFiles(string logDir, string appType, int maxFiles) {
			try {
				var logFiles = Directory.GetFiles(logDir, $"{appType}_*.log*")
					.Select(f => new FileInfo(f))
					.OrderByDescending(f => f.CreationTime)
					.ToList();
				var filesToDelete = logFiles.Skip(maxFiles - 1);

				foreach (var file in filesToDelete) {
					try {
						file.Delete();
					} catch {
					}
				}
			} catch {
			}
		}
	}
}