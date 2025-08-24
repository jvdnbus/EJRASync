namespace EJRASync.Lib {
	public static partial class Constants {
		public static readonly string AssettoCorsaAppId = "244210";
		public static readonly string AssettoCorsaSubPath = @"steamapps\common\assettocorsa";

		public static readonly string CarsBucketName = "ejra-cars";
		public static readonly string TracksBucketName = "ejra-tracks";
		public static readonly string FontsBucketName = "ejra-fonts";
		public static readonly string GuiBucketName = "ejra-gui";
		public static readonly string AppsBucketName = "ejra-apps";

		public static readonly (string, string, string[])[] Buckets = [
			(CarsBucketName, "content/cars", [""]),
			(TracksBucketName, "content/tracks", [""]),
			(FontsBucketName, "content/fonts", [""]),
			(GuiBucketName, "content/gui", [""]),
			(AppsBucketName, "apps", ["lua", "python"])
		];

		public static readonly Dictionary<string, string> SteamRegistryKeys = new()
		{
			{ @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath" },
			{ @"HKEY_LOCAL_MACHINE\Software\Valve\Steam", "InstallPath" },
		};
		public static readonly string SteamLibraryFile = @"steamapps\libraryfolders.vdf";
		public static readonly string R2Url = "https://fcf72ca08f6cbb4aa49a20fe73c05bbc.r2.cloudflarestorage.com";
		public static readonly string EjraAuth = @"https://ejra-auth.enroy.lol/authorize?client_id={0}&redirect_uri={1}&response_type=code";
		public static readonly string EjraAuthTokenExchange = "https://ejra-auth.enroy.lol/token";
		public static readonly string EjraAuthClientId = "dab81f8c37e09d2145";
		public static readonly string EjraAuthRedirectUri = "http://localhost:5050/callback";
		public static readonly string EjraApi = "https://ejra-api.enroy.lol";

		public static readonly string CarsYamlFile = "cars.yaml";
		public static readonly string TracksYamlFile = "tracks.yaml";

		public static readonly string SentryDSN =
			"https://156c49180a097534338f2d006f5ea259@o4509791289409536.ingest.de.sentry.io/4509791296749648";

		public static readonly string GithubReleaseURL = "https://api.github.com/repos/jvdnbus/ejrasync/releases/latest";

		public static readonly string CliExecutableName = "EJRASync.CLI.exe";
		public static readonly string GuiExecutableName = "EJRASync.UI.exe";

		public static readonly string UserAgent = "EJRASync.Lib.AutoUpdater";

		public static readonly Dictionary<string, string[]> ExclusionPatterns = new() {
			{
				CarsBucketName, new[]
				{
					@".*skins/.*/outline\.png$"
				}
			}, {
				TracksBucketName, new string[] {}
			}, {
				FontsBucketName, new string[] {}
			}, {
				GuiBucketName, new string[] {}
			}, {
				AppsBucketName, new string[] {}
			}
		};
	}
}
