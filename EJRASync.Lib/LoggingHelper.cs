using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;

namespace EJRASync.Lib {
	public static class LoggingHelper {
		private static bool _isConfigured = false;

		public static void ConfigureLogging(string appType = "CLI", int maxBackups = 10) {
			if (_isConfigured) return;

			var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EJRASync.Logs");
			if (!Directory.Exists(logDir)) {
				Directory.CreateDirectory(logDir);
			}

			var hierarchy = (Hierarchy)LogManager.GetRepository();

			var patternLayout = new PatternLayout();
			patternLayout.ConversionPattern = "%date{yyyy-MM-dd HH:mm:ss.fff} [%level] %message%newline";
			patternLayout.ActivateOptions();

			CleanupOldLogFiles(logDir, appType, maxBackups);

			var roller = new RollingFileAppender();
			roller.AppendToFile = false;
			roller.File = Path.Combine(logDir, $"{appType}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
			roller.Layout = patternLayout;
			roller.CountDirection = 1;
			roller.MaxSizeRollBackups = -1;
			roller.MaximumFileSize = "10MB";
			roller.RollingStyle = RollingFileAppender.RollingMode.Size;
			roller.StaticLogFileName = false;
			roller.ActivateOptions();

			var consoleAppender = new ConsoleAppender();
			consoleAppender.Layout = patternLayout;
			consoleAppender.Threshold = Level.Info;
			consoleAppender.ActivateOptions();

			hierarchy.Root.AddAppender(roller);
			hierarchy.Root.AddAppender(consoleAppender);
			hierarchy.Root.Level = Level.Info;
			hierarchy.Configured = true;

			_isConfigured = true;
		}

		public static ILog GetLogger(Type type) {
			return LogManager.GetLogger(type);
		}

		public static ILog GetLogger(string name) {
			return LogManager.GetLogger(name);
		}

		public static ILog GetFileOnlyLogger(Type type) {
			var logger = LogManager.GetLogger($"{type.FullName}.FileOnly");
			var hierarchy = (Hierarchy)LogManager.GetRepository();

			// Create a logger that only writes to file (no console)
			var fileLogger = hierarchy.GetLogger($"{type.FullName}.FileOnly") as Logger;
			if (fileLogger != null && fileLogger.Appenders.Count == 0) {
				var fileAppender = hierarchy.Root.Appenders.OfType<RollingFileAppender>().FirstOrDefault();
				if (fileAppender != null) {
					fileLogger.AddAppender(fileAppender);
					fileLogger.Level = Level.Info;
					fileLogger.Additivity = false;
				}
			}

			return logger;
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