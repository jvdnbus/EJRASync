using EJRASync.Lib.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace EJRASync.UI.Views {
	public partial class OAuthDialog : DarkThemeWindow, INotifyPropertyChanged {
		private readonly IEjraAuthService _authService;
		private string _statusMessage = "Opening browser for authentication...";
		private bool _showUrlTextBox = false;
		private string _authUrl = "";

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
			} catch (Exception ex) {
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
				} catch {
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