using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EJRASync.UI {
	public class OrBooleanToVisibilityConverter : IMultiValueConverter {
		public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
			if (values == null || values.Length == 0) {
				return Visibility.Collapsed;
			}

			// Return Visible if any of the boolean values is true
			foreach (var value in values) {
				if (value is bool boolValue && boolValue) {
					return Visibility.Visible;
				}
			}

			return Visibility.Collapsed;
		}

		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
			throw new NotImplementedException();
		}
	}
}