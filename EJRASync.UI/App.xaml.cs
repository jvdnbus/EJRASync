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

					// Register auth services
					services.AddSingleton<IEjraAuthApiService, EjraAuthApiService>();
					services.AddSingleton<IEjraAuthService, EjraAuthService>();

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
