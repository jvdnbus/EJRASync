using System.Text.RegularExpressions;

namespace EJRASync.Lib.Utils {
	public static class PathUtils {
		private static readonly Regex DuplicateForwardSlashRegex = new(@"/{2,}", RegexOptions.Compiled);
		private static readonly Regex DuplicateBackwardSlashRegex = new(@"\\{2,}", RegexOptions.Compiled);

		public static string NormalizePath(string path, bool unixStyle = true) {
			if (string.IsNullOrEmpty(path))
				return path;

			var normalized = path;
			if (unixStyle) {
				// Convert all backslashes to forward slashes
				normalized = normalized.Replace('\\', '/');
			} else {
				// Convert all forward slashes to backslashes
				normalized = normalized.Replace('/', '\\');
			}

			// Then remove duplicate forward slashes and duplicate backward slashes
			normalized = DuplicateForwardSlashRegex.Replace(normalized, "/");
			normalized = DuplicateBackwardSlashRegex.Replace(normalized, "\\");

			return normalized;
		}

		public static bool IsExcluded(string bucketName, string filePath) {
			if (string.IsNullOrEmpty(filePath))
				return false;

			// Normalize the path before checking
			var normalizedPath = NormalizePath(filePath);

			// Get exclusion patterns for this bucket
			if (!Constants.ExclusionPatterns.TryGetValue(bucketName, out var patterns))
				return false;

			// Check if any pattern matches
			foreach (var pattern in patterns) {
				if (string.IsNullOrEmpty(pattern))
					continue;

				try {
					if (Regex.IsMatch(normalizedPath, pattern, RegexOptions.IgnoreCase)) {
						return true;
					}
				} catch {
					continue;
				}
			}

			return false;
		}
	}
}
