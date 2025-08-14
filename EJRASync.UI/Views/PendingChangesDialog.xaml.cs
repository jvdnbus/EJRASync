using EJRASync.UI.Models;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;

namespace EJRASync.UI.Views {
	public partial class PendingChangesDialog : DarkThemeWindow {
		public ObservableCollection<PendingChange> PendingChanges { get; set; }

		public PendingChangesDialog(ObservableCollection<PendingChange> pendingChanges) {
			PendingChanges = pendingChanges;

			InitializeComponent();
			DataContext = this;

			// Set the window title after InitializeComponent
			Title = $"Pending Changes ({pendingChanges.Count} items)";
		}

		private void CloseButton_Click(object sender, RoutedEventArgs e) {
			Close();
		}

		private void CopyRowsButton_Click(object sender, RoutedEventArgs e) {
			try {
				var csv = GenerateCsvFromPendingChanges();
				Clipboard.SetText(csv);
			} catch (Exception ex) {
				Sentry.SentrySdk.CaptureException(ex);
			}
		}

		private string GenerateCsvFromPendingChanges() {
			var csv = new StringBuilder();
			
			// Add header row
			csv.AppendLine("Type,Description,Bucket,Size");
			
			// Add data rows
			foreach (var change in PendingChanges) {
				var type = EscapeCsvField(change.Type.ToString());
				var description = EscapeCsvField(change.Description);
				var bucket = EscapeCsvField(change.BucketName);
				var size = change.FileSizeBytes?.ToString() ?? "";
				
				csv.AppendLine($"{type},{description},{bucket},{size}");
			}
			
			return csv.ToString();
		}

		private string EscapeCsvField(string field) {
			if (string.IsNullOrEmpty(field)) {
				return "";
			}
			
			// If field contains comma, newline, or quote, wrap in quotes and escape internal quotes
			if (field.Contains(",") || field.Contains("\n") || field.Contains("\"")) {
				return "\"" + field.Replace("\"", "\"\"") + "\"";
			}
			
			return field;
		}
	}
}