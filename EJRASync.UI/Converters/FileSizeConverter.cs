using System.Globalization;
using System.Windows.Data;

namespace EJRASync.UI {
	public class FileSizeConverter : IValueConverter {
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
			if (value is long bytes) {
				return FormatFileSize(bytes);
			}

			long? nullableBytes;
			if (value is long?) {
				nullableBytes = (long?)value;
				if (nullableBytes.HasValue) {
					return FormatFileSize(nullableBytes.Value);
				}
			}

			return string.Empty;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
			throw new NotImplementedException();
		}

		private string FormatFileSize(long bytes) {
			if (bytes == 0) return "0 B";

			string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
			int suffixIndex = 0;
			double size = bytes;

			while (size >= 1024 && suffixIndex < suffixes.Length - 1) {
				size /= 1024;
				suffixIndex++;
			}

			return $"{size:F1} {suffixes[suffixIndex]}";
		}
	}
}