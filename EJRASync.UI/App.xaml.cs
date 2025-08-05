using Amazon.S3;
using EJRASync.Lib;
using EJRASync.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Security.Principal;
using System.Windows;

namespace EJRASync.UI {
	public partial class App : Application {
		private IHost? _host;

		public App() {
			var currentUser = WindowsIdentity.GetCurrent().Name;

			DispatcherUnhandledException += App_DispatcherUnhandledException;
			SentrySdk.Init(options => {
				options.Dsn = Constants.SentryDSN;
				options.Debug = false;
				options.AutoSessionTracking = true;
				options.TracesSampleRate = 1.0;
				options.ProfilesSampleRate = 1.0;
			});

			SentrySdk.ConfigureScope(scope => {
				scope.SetTag("username", currentUser);
			});
		}

		private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
			SentrySdk.CaptureException(e.Exception);
		}

		protected override async void OnStartup(StartupEventArgs e) {
			_host = Host.CreateDefaultBuilder()
				.ConfigureServices((context, services) => {
					// Register AWS S3 client
					services.AddSingleton<IAmazonS3>(provider => {
						var config = new AmazonS3Config {
							ServiceURL = Constants.MinioUrl,
							ForcePathStyle = true,
							UseHttp = true // Since it's HTTP, not HTTPS
						};
						return new AmazonS3Client("", "", config);
					});

					// Register services
					services.AddSingleton<IS3Service, S3Service>();
					services.AddSingleton<IFileService, FileService>();
					services.AddSingleton<ICompressionService, CompressionService>();
					services.AddSingleton<IContentStatusService, ContentStatusService>();

					// Register ViewModels
					services.AddTransient<MainWindowViewModel>();

					// Register Windows
					services.AddTransient<MainWindow>();
				})
				.Build();

			await _host.StartAsync();

			var mainWindow = _host.Services.GetRequiredService<MainWindow>();
			mainWindow.Show();

			base.OnStartup(e);
		}

		protected override async void OnExit(ExitEventArgs e) {
			if (_host != null) {
				await _host.StopAsync();
				_host.Dispose();
			}

			base.OnExit(e);
		}
	}
}
