namespace EJRASync.Lib.Utils {
	public static class FileSizeFormatter {
		private static readonly string[] Suffixes = { "B", "KB", "MB", "GB", "TB" };

		public static string FormatFileSize(long bytes) {
			if (bytes == 0) return "0 B";

			// Use logarithms to calculate the appropriate suffix index
			var log = Math.Log(bytes, 1024);
			var suffixIndex = Math.Min((int)Math.Floor(log), Suffixes.Length - 1);
			var size = bytes / Math.Pow(1024, suffixIndex);

			return $"{size:F1} {Suffixes[suffixIndex]}";
		}
	}
}