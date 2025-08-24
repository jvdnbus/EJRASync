using Amazon.S3;
using EJRASync.Lib;
using EJRASync.Lib.Services;
using log4net;
using Spectre.Console;
using System.Runtime.InteropServices;
using System.Security.Principal;

class CLI {
	private static ILog? _logger;

	static async Task Main(string[] args) {
		try {
			LoggingHelper.ConfigureLogging("CLI", 10);
			_logger = LoggingHelper.GetFileOnlyLogger(typeof(CLI));

			// Read optional parameter from the command line if present
			string? acPath = args.Length > 0 ? args[0] : null;
			var currentUser = GetCurrentUser();
			_logger?.Info($"Current user: {currentUser}");

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

			var progressService = new SpectreProgressService();
			progressService.ShowMessage($"Current version: {Constants.Version}");

			var autoUpdater = new AutoUpdater(@$"{AppContext.BaseDirectory}\{Constants.CliExecutableName}", progressService.ShowMessage, acPath: acPath);
			await autoUpdater.ProcessUpdates();

			// Fetch AWS credentials before configuring S3 client
			var authApi = new EjraApiService();
			var tokens = await authApi.GetTokensAsync();

			string awsAccessKeyId = "";
			string awsSecretAccessKey = "";
			string serviceUrl = Constants.R2Url;

			if (tokens?.UserRead != null) {
				awsAccessKeyId = tokens.UserRead.Aws.AccessKeyId;
				awsSecretAccessKey = tokens.UserRead.Aws.SecretAccessKey;
				serviceUrl = tokens.UserRead.S3Url;
				AnsiConsole.MarkupLine($"[green](✓)[/] Authenticated successfully");
				_logger?.Info("Authenticated successfully");
			} else {
				AnsiConsole.MarkupLine($"[yellow]/!\\[/] Using anonymous access");
				_logger?.Info("Using anonymous access");
			}

			//AWSConfigs.LoggingConfig.LogResponses = ResponseLoggingOption.Always;
			//AWSConfigs.LoggingConfig.LogMetrics = true;
			//AWSConfigs.LoggingConfig.LogTo = LoggingOptions.Console;

			var s3Config = new AmazonS3Config {
				ServiceURL = serviceUrl,
				ForcePathStyle = true,
			};
			var s3Client = new AmazonS3Client(awsAccessKeyId, awsSecretAccessKey, s3Config);

			var fileService = new FileService();
			var compressionService = new CompressionService();

			// Use lazy initialization to break circular dependency
			IS3Service? s3ServiceRef = null;
			var hashStoreService = new HashStoreService(() => s3ServiceRef!, progressService);
			var s3Service = new S3Service(s3Client, hashStoreService);
			s3ServiceRef = s3Service;

			var downloadService = new DownloadService(s3Service, fileService, compressionService);

			if (acPath != null) {
				AnsiConsole.MarkupLine($"[bold] AssettoCorsa Path:[/] {acPath}");
				_logger?.Info($"AssettoCorsa Path: {acPath}");
				SentrySdk.ConfigureScope(scope => scope.SetTag("ac.path", acPath));
			}

			SyncManager syncManager = new SyncManager(downloadService, s3Service, hashStoreService, progressService, acPath);
			await syncManager.SyncAllAsync();

			var rule = new Rule();
			AnsiConsole.Write(rule);
			var completeRule = new Rule("Sync complete!");
			completeRule.LeftJustified();
			AnsiConsole.Write(completeRule);
			AnsiConsole.Write(rule);
			_logger?.Info("Sync complete!");

			AnsiConsole.WriteLine("Press any key to exit...");
			Console.ReadKey();
		} catch (Exception ex) {
			_logger?.Error($"An unexpected error has occurred: {ex.Message}", ex);

			Console.WriteLine("An unexpected error has occurred. Press any key to exit...");
			Console.ReadKey();
		}
	}

	private static string GetCurrentUser() {
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			return WindowsIdentity.GetCurrent().Name;
		}
		return $"{Environment.UserDomainName}/${Environment.UserName}";
	}
}
