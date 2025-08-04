using Amazon.S3;
using EJRASync.Lib;
using System.Security.Principal;
using Spectre.Console;

class CLI {
    static void Main(string[] args) {
		var currentUser = WindowsIdentity.GetCurrent().Name;
		Console.WriteLine($"Current user: {currentUser}");

		SentrySdk.Init(options =>
		{
			options.Dsn = Constants.SentryDSN;
			options.Debug = false;
			options.AutoSessionTracking = true;
			options.TracesSampleRate = 1.0;
			options.ProfilesSampleRate = 1.0;
		});

		SentrySdk.ConfigureScope(scope =>
		{
			scope.SetTag("username", currentUser);
			//scope.SetTag("steam.accountname", "");
			//scope.SetTag("steam.personaname", "");
		});

		var s3Client = new AmazonS3Client("", "", new AmazonS3Config {
			ServiceURL = Constants.MinioUrl,
			ForcePathStyle = true,
		});

		var autoUpdater = new AutoUpdater(@$"{AppContext.BaseDirectory}\{Constants.ExecutableName}");
		autoUpdater.ProcessUpdates().Wait();

		SyncManager syncManager;

		// Read optional parameter from the command line, if present
		if (args.Length > 0) {
			var acPath = args[0];
			AnsiConsole.MarkupLine($"[bold]Override AssettoCorsa Path:[/] {acPath}");
			SentrySdk.ConfigureScope(scope => scope.SetTag("ac.path", acPath));

			syncManager = new SyncManager(s3Client, acPath);
		} else {
			syncManager = new SyncManager(s3Client);
		}

		syncManager.SyncAllAsync().Wait();

		Console.WriteLine("==============================================================================");
		Console.WriteLine("Sync complete.");
		Console.WriteLine("==============================================================================");
		Console.WriteLine("Press any key to exit.");
		Console.ReadKey();
	}
}
