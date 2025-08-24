using EJRASync.UI.Models;
using EJRASync.UI.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;

namespace EJRASync.UI.Views {
	public partial class ArchiveDialog : DarkThemeWindow {
		public ArchiveDialog(ArchiveDialogViewModel viewModel) {
			InitializeComponent();
			DataContext = viewModel;
			Closing += Window_Closing;
		}

		private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
			if (DataContext is ArchiveDialogViewModel viewModel && viewModel.IsProcessing) {
				var result = MessageBox.Show(
					"An archive operation is in progress. Do you want to cancel it?",
					"Archive in progress",
					MessageBoxButton.YesNo,
					MessageBoxImage.Question);

				if (result == MessageBoxResult.No) {
					e.Cancel = true;
					return;
				}

				// Cancel the operation if user confirms
				viewModel.StartCommand.Execute(null);
			}
		}

		private void ListBoxItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
			if (sender is ListBoxItem listBoxItem && listBoxItem.DataContext is ArchiveItem releaseItem) {
				releaseItem.IsSelected = !releaseItem.IsSelected;
			}
		}

		private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
			try {
				Process.Start(new ProcessStartInfo {
					FileName = e.Uri.AbsoluteUri,
					UseShellExecute = true
				});
				e.Handled = true;
			} catch {
				// Silently handle any errors opening the URL
			}
		}
	}
}