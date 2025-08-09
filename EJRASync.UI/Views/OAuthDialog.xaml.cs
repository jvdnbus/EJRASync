using EJRASync.UI.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace EJRASync.UI.Views {
	public partial class OAuthDialog : Window, INotifyPropertyChanged {
		private readonly IEjraAuthService _authService;
		private string _statusMessage = "Opening browser for authentication...";
		private bool _showUrlTextBox = false;
		private string _authUrl = "";

		// Windows API for dark title bar
		[DllImport("dwmapi.dll", PreserveSig = true)]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

		private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
		private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

		public string StatusMessage {
			get => _statusMessage;
			set {
				_statusMessage = value;
				OnPropertyChanged();
			}
		}

		public bool ShowUrlTextBox {
			get => _showUrlTextBox;
			set {
				_showUrlTextBox = value;
				OnPropertyChanged();
			}
		}

		public string AuthUrl {
			get => _authUrl;
			set {
				_authUrl = value;
				OnPropertyChanged();
			}
		}

		public OAuthToken? OAuthToken { get; private set; }

		public OAuthDialog(IEjraAuthService authService) {
			_authService = authService;
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

		public async Task<OAuthToken?> StartAuthenticationAsync() {
			try {
				// Generate the auth URL first
				var authUrl = string.Format(EJRASync.Lib.Constants.EjraAuth, 
					EJRASync.Lib.Constants.EjraAuthClientId, 
					Uri.EscapeDataString(EJRASync.Lib.Constants.EjraAuthRedirectUri));
				
				await Dispatcher.InvokeAsync(() => {
					AuthUrl = authUrl;
					StatusMessage = "Opening browser for authentication...";
					ShowUrlTextBox = true;
				});
				
				// Start the authentication process
				var oauthToken = await _authService.AuthenticateAsync();
				
				if (oauthToken == null) {
					// If authentication failed, show the URL for manual copy
					await Dispatcher.InvokeAsync(() => {
						StatusMessage = "Browser authentication failed or timed out. Please try copying the URL below:";
						ShowUrlTextBox = true;
					});
					return null;
				}

				await Dispatcher.InvokeAsync(() => {
					StatusMessage = "Authentication successful!";
					OAuthToken = oauthToken;
				});
				
				// Close dialog after a brief delay
				await Task.Delay(1000);
				await Dispatcher.InvokeAsync(() => {
					DialogResult = true;
				});
				return oauthToken;
			}
			catch (Exception ex) {
				await Dispatcher.InvokeAsync(() => {
					StatusMessage = $"Authentication failed: {ex.Message}";
					ShowUrlTextBox = true;
				});
				return null;
			}
		}

		private void UrlTextBox_GotFocus(object sender, RoutedEventArgs e) {
			if (sender is TextBox textBox && !string.IsNullOrEmpty(textBox.Text)) {
				// Copy to clipboard
				try {
					Clipboard.SetText(textBox.Text);
					// Briefly show feedback
					var originalMessage = StatusMessage;
					StatusMessage = "URL copied to clipboard!";
					
					_ = Task.Run(async () => {
						await Task.Delay(2000);
						await Dispatcher.InvokeAsync(() => {
							if (StatusMessage == "URL copied to clipboard!") {
								StatusMessage = originalMessage;
							}
						});
					});
				}
				catch {
					// Ignore clipboard errors
				}
			}
		}

		private void CancelButton_Click(object sender, RoutedEventArgs e) {
			DialogResult = false;
			Close();
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}