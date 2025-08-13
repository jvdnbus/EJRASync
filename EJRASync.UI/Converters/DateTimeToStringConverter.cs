using System.Globalization;
using System.Windows.Data;

namespace EJRASync.UI {
	public class DateTimeToStringConverter : IValueConverter {
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
			if (value is DateTime dateTime) {
				// Return empty string for DateTime.MinValue
				if (dateTime == DateTime.MinValue) {
					return string.Empty;
				}

				// Convert UTC time to local time if the DateTime kind is UTC or Unspecified
				// (S3 returns dates in UTC, but they might be marked as Unspecified)
				if (dateTime.Kind == DateTimeKind.Utc) {
					dateTime = dateTime.ToLocalTime();
				} else if (dateTime.Kind == DateTimeKind.Unspecified) {
					// Assume unspecified dates from S3 are UTC and convert to local
					dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc).ToLocalTime();
				}

				// Use the format string from parameter if provided, otherwise use default
				string format = parameter as string ?? "yyyy-MM-dd HH:mm";
				return dateTime.ToString(format);
			}

			return string.Empty;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
			throw new NotImplementedException();
		}
	}
}