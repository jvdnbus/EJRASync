using System;
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