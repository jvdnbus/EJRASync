using System.Text.RegularExpressions;

namespace EJRASync.Lib.Utils {
	public static class PathUtils {
		private static readonly Regex DuplicateForwardSlashRegex = new(@"/{2,}", RegexOptions.Compiled);

		public static string NormalizePath(string path) {
			if (string.IsNullOrEmpty(path))
				return path;

			// Convert all backslashes to forward slashes first
			var normalized = path.Replace('\\', '/');

			// Then remove duplicate forward slashes
			normalized = DuplicateForwardSlashRegex.Replace(normalized, "/");

			return normalized;
		}
	}
}
