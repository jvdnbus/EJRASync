using EJRASync.Lib.Utils;
using System.Globalization;
using System.Windows.Data;

namespace EJRASync.UI {
	public class FileSizeConverter : IValueConverter {
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
			if (value is long bytes) {
				return FileSizeFormatter.FormatFileSize(bytes);
			}

			long? nullableBytes;
			if (value is long?) {
				nullableBytes = (long?)value;
				if (nullableBytes.HasValue) {
					return FileSizeFormatter.FormatFileSize(nullableBytes.Value);
				}
			}

			return string.Empty;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
			throw new NotImplementedException();
		}

	}
}