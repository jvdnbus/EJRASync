using EJRASync.UI.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace EJRASync.UI.Views {
	public partial class PendingChangesDialog : Window {
		public string Title { get; set; } = "Pending Changes";
		public ObservableCollection<PendingChange> PendingChanges { get; set; }

		public PendingChangesDialog(ObservableCollection<PendingChange> pendingChanges) {
			PendingChanges = pendingChanges;
			Title = $"Pending Changes ({pendingChanges.Count} items)";

			InitializeComponent();
			DataContext = this;
		}

		private void CloseButton_Click(object sender, RoutedEventArgs e) {
			Close();
		}
	}
}