using System.Windows;
using System.Windows.Input;

namespace EJRASync.UI.Views {
	public partial class UploadConfirmationDialog : DarkThemeWindow {
		public enum UploadMethod {
			Cancel,
			Compress,
			Raw
		}

		public UploadMethod Result { get; private set; } = UploadMethod.Cancel;

		public UploadConfirmationDialog(int fileCount = 1) {
			InitializeComponent();
			
			// Set appropriate message based on file count
			if (fileCount == 1) {
				MessageTextBlock.Text = "Would you like to compress this file before uploading?";
			} else {
				MessageTextBlock.Text = $"Would you like to compress these {fileCount} files before uploading?";
			}
			
			// Set up keyboard shortcuts
			KeyDown += OnKeyDown;
			
			// Focus the default button
			Loaded += (s, e) => YesButton.Focus();
		}

		private void OnKeyDown(object sender, KeyEventArgs e) {
			switch (e.Key) {
				case Key.Y:
					if (!e.Handled) {
						YesButton_Click(this, new RoutedEventArgs());
						e.Handled = true;
					}
					break;
				case Key.N:
					if (!e.Handled) {
						NoButton_Click(this, new RoutedEventArgs());
						e.Handled = true;
					}
					break;
				case Key.Escape:
					if (!e.Handled) {
						CancelButton_Click(this, new RoutedEventArgs());
						e.Handled = true;
					}
					break;
			}
		}

		private void YesButton_Click(object sender, RoutedEventArgs e) {
			Result = UploadMethod.Compress;
			DialogResult = true;
			Close();
		}

		private void NoButton_Click(object sender, RoutedEventArgs e) {
			Result = UploadMethod.Raw;
			DialogResult = true;
			Close();
		}

		private void CancelButton_Click(object sender, RoutedEventArgs e) {
			Result = UploadMethod.Cancel;
			DialogResult = false;
			Close();
		}
	}
}