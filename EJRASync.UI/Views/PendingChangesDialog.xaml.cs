using EJRASync.UI.Models;
using System.Collections.ObjectModel;
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
	}
}