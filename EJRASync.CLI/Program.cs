using Amazon.S3;
using EJRASync.Lib;
using EJRASync.Lib.Services;
using Spectre.Console;
using System.Runtime.InteropServices;
using System.Security.Principal;

class CLI {
	static async Task Main(string[] args) {
		var currentUser = GetCurrentUser();
		Console.WriteLine($"Current user: {currentUser}");

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

		// Fetch AWS credentials before configuring S3 client
		var authApi = new EjraAuthApiService();
		var tokens = await authApi.GetTokensAsync();

		string awsAccessKeyId = "";
		string awsSecretAccessKey = "";
		string serviceUrl = Constants.R2Url;

		if (tokens?.UserRead != null) {
			awsAccessKeyId = tokens.UserRead.Aws.AccessKeyId;
			awsSecretAccessKey = tokens.UserRead.Aws.SecretAccessKey;
			serviceUrl = tokens.UserRead.S3Url;
			AnsiConsole.MarkupLine($"[green](✓)[/] Authenticated successfully");
		} else {
			AnsiConsole.MarkupLine($"[yellow]/!\\[/] Using anonymous access");
		}

		//AWSConfigs.LoggingConfig.LogResponses = ResponseLoggingOption.Always;
		//AWSConfigs.LoggingConfig.LogMetrics = true;
		//AWSConfigs.LoggingConfig.LogTo = LoggingOptions.Console;

		var s3Config = new AmazonS3Config {
			ServiceURL = serviceUrl,
			ForcePathStyle = true,
		};
		var s3Client = new AmazonS3Client(awsAccessKeyId, awsSecretAccessKey, s3Config);

		var progressService = new SpectreProgressService();
		var autoUpdater = new AutoUpdater(@$"{AppContext.BaseDirectory}\{Constants.CliExecutableName}", progressService.ShowMessage);
		await autoUpdater.ProcessUpdates();
		
		var fileService = new FileService();
		var compressionService = new CompressionService();

		// Use lazy initialization to break circular dependency
		IS3Service? s3ServiceRef = null;
		var hashStoreService = new HashStoreService(() => s3ServiceRef!, progressService);
		var s3Service = new S3Service(s3Client, hashStoreService);
		s3ServiceRef = s3Service;

		var downloadService = new DownloadService(s3Service, fileService, compressionService);

		SyncManager syncManager;

		// Read optional parameter from the command line, if present
		if (args.Length > 0) {
			var acPath = args[0];
			AnsiConsole.MarkupLine($"[bold]Override AssettoCorsa Path:[/] {acPath}");
			SentrySdk.ConfigureScope(scope => scope.SetTag("ac.path", acPath));

			syncManager = new SyncManager(downloadService, s3Service, hashStoreService, progressService, acPath);
		} else {
			syncManager = new SyncManager(downloadService, s3Service, hashStoreService, progressService);
		}

		await syncManager.SyncAllAsync();

		var rule = new Rule();
		AnsiConsole.Write(rule);
		var completeRule = new Rule("Sync complete!");
		completeRule.LeftJustified();
		AnsiConsole.Write(completeRule);
		AnsiConsole.Write(rule);
		AnsiConsole.WriteLine("Press any key to exit...");
		Console.ReadKey();
	}

	private static string GetCurrentUser() {
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			return WindowsIdentity.GetCurrent().Name;
		}
		return $"{Environment.UserDomainName}/${Environment.UserName}";
	}
}
