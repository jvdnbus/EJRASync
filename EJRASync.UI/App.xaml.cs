using Amazon.S3;
using EJRASync.Lib;
using EJRASync.Lib.Services;
using EJRASync.UI.Services;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Security.Principal;
using System.Windows;

namespace EJRASync.UI {
	public partial class App : Application {
		private static readonly ILog _logger = LoggingHelper.GetLogger(typeof(App));
		private IHost? _host;

		public App() {
			LoggingHelper.ConfigureLogging("UI", 10);

			var currentUser = WindowsIdentity.GetCurrent().Name;
			_logger.Info($"Application starting for user: {currentUser}");

			DispatcherUnhandledException += App_DispatcherUnhandledException;
			Sentry.SentrySdk.Init(options => {
				options.Dsn = Constants.SentryDSN;
				options.Debug = false;
				options.AutoSessionTracking = true;
				options.TracesSampleRate = 1.0;
				options.ProfilesSampleRate = 1.0;
			});

			Sentry.SentrySdk.ConfigureScope(scope => {
				scope.SetTag("username", currentUser);
			});
		}

		private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
			Sentry.SentrySdk.CaptureException(e.Exception);
			_logger.Error($"Unhandled exception in UI dispatcher: {e.Exception.Message}", e.Exception);
		}

		protected override async void OnStartup(StartupEventArgs e) {
			// Parse command line arguments
			string? acPathOverride = e.Args.Length > 0 ? e.Args[0] : null;

			// Fetch AWS credentials before configuring services
			var authApi = new EjraAuthApiService();
			var tokens = await authApi.GetTokensAsync();

			string awsAccessKeyId = "";
			string awsSecretAccessKey = "";
			string serviceUrl = Constants.R2Url;

			if (tokens?.UserRead != null) {
				awsAccessKeyId = tokens.UserRead.Aws.AccessKeyId;
				awsSecretAccessKey = tokens.UserRead.Aws.SecretAccessKey;
				serviceUrl = tokens.UserRead.S3Url;
			}

			_host = Host.CreateDefaultBuilder()
				.ConfigureServices((context, services) => {
					// Register AWS S3 client with fetched credentials
					services.AddSingleton<IAmazonS3>(provider => {
						var config = new AmazonS3Config {
							ServiceURL = serviceUrl,
							ForcePathStyle = true,
						};
						return new AmazonS3Client(awsAccessKeyId, awsSecretAccessKey, config);
					});

					// Register auth services from Lib
					services.AddSingleton<IEjraAuthApiService, EjraAuthApiService>();
					services.AddSingleton<IEjraAuthService, EjraAuthService>();
					// UI aliases for backwards compatibility
					services.AddSingleton<IEjraAuthApiService, EjraAuthApiService>();
					services.AddSingleton<IEjraAuthService, EjraAuthService>();

					// Register services from Lib with circular dependency resolution
					services.AddSingleton<IProgressService, SpectreProgressService>();
					services.AddSingleton<IS3Service, S3Service>();
					services.AddSingleton<Func<IS3Service>>(provider => () => provider.GetRequiredService<IS3Service>());
					services.AddSingleton<IHashStoreService, HashStoreService>();
					services.AddSingleton<IFileService, FileService>();
					services.AddSingleton<ICompressionService, CompressionService>();
					services.AddSingleton<IDownloadService, DownloadService>();
					services.AddSingleton<IContentStatusService, ContentStatusService>();

					// Register ViewModels
					services.AddTransient(provider => new MainWindowViewModel(
						provider.GetRequiredService<IS3Service>(),
						provider.GetRequiredService<IHashStoreService>(),
						provider.GetRequiredService<IFileService>(),
						provider.GetRequiredService<IContentStatusService>(),
						provider.GetRequiredService<ICompressionService>(),
						provider.GetRequiredService<IDownloadService>(),
						provider.GetRequiredService<IEjraAuthApiService>(),
						provider.GetRequiredService<IEjraAuthService>(),
						acPathOverride
					));

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
			// Clean up temporary files
			MainWindowViewModel.CleanupTempFiles();

			if (_host != null) {
				await _host.StopAsync();
				_host.Dispose();
			}

			base.OnExit(e);
		}
	}
}
