using EJRASync.UI.Models;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EJRASync.UI.Views {
	public partial class PendingChangesDialog : Window {
		public string Title { get; set; } = "Pending Changes";
		public ObservableCollection<PendingChange> PendingChanges { get; set; }

		// Windows API for dark title bar
		[DllImport("dwmapi.dll", PreserveSig = true)]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

		private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
		private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

		public PendingChangesDialog(ObservableCollection<PendingChange> pendingChanges) {
			PendingChanges = pendingChanges;
			Title = $"Pending Changes ({pendingChanges.Count} items)";

			InitializeComponent();
			DataContext = this;
			SourceInitialized += OnSourceInitialized;
		}

		private void OnSourceInitialized(object sender, EventArgs e) {
			// Enable dark title bar on Windows 10/11
			if (PresentationSource.FromVisual(this) is HwndSource hwndSource) {
				var hwnd = hwndSource.Handle;
				if (hwnd != IntPtr.Zero) {
					SetDarkTitleBar(hwnd);
				}
			}
		}

		private void SetDarkTitleBar(IntPtr hwnd) {
			try {
				int darkMode = 1; // 1 for dark, 0 for light
				
				// Try the newer attribute first (Windows 11/10 20H1+)
				int result = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
				
				// If that fails, try the older attribute (Windows 10 before 20H1)
				if (result != 0) {
					DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
				}
			}
			catch {
				// Silently fail on older Windows versions that don't support this
			}
		}

		private void CloseButton_Click(object sender, RoutedEventArgs e) {
			Close();
		}
	}
}