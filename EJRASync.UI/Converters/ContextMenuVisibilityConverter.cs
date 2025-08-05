using System.Globalization;
using System.Windows.Data;

namespace EJRASync.UI {
	public class ContextMenuVisibilityConverter : IMultiValueConverter {
		public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
			if (values == null || values.Length < 1)
				return null;

			var selectedBucket = values[0] as string;

			// No context menu if we're at the root bucket list (selectedBucket is null)
			if (string.IsNullOrEmpty(selectedBucket))
				return null;

			// We're inside a bucket - show context menu
			return parameter;
		}

		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
			throw new NotImplementedException();
		}
	}
}