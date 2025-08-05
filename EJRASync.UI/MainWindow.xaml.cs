using EJRASync.UI.Utils;
using System.Windows;

namespace EJRASync.UI {
	public partial class MainWindow : Window {
		private readonly MainWindowViewModel _viewModel;

		public MainWindow(MainWindowViewModel viewModel) {
			_viewModel = viewModel;
			InitializeComponent();
			DataContext = _viewModel;
			Loaded += OnLoaded;
		}

		private async void OnLoaded(object sender, RoutedEventArgs e) {
			_ = Task.Run(async () => {
				try {
					await this.InvokeUIAsync(() => {
						_viewModel.StatusMessage = "Initializing...";
					});

					await _viewModel.InitializeAsync();
					// Load initial remote bucket list in background
					await _viewModel.RemoteFiles.LoadBucketsAsync();

					await this.InvokeUIAsync(() => {
						_viewModel.StatusMessage = "Ready";
					});
				} catch (Exception ex) {
					await this.InvokeUIAsync(() => {
						_viewModel.StatusMessage = $"Initialization failed: {ex.Message}";
					});
				}
			});
		}
	}
}
