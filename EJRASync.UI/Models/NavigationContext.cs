using CommunityToolkit.Mvvm.ComponentModel;

namespace EJRASync.UI.Models {
	public partial class NavigationContext : ObservableObject {
		[ObservableProperty]
		private string _localBasePath = string.Empty;

		[ObservableProperty]
		private string _localCurrentPath = string.Empty;

		[ObservableProperty]
		private string? _selectedBucket;

		[ObservableProperty]
		private string _remoteCurrentPath = string.Empty;

		public List<string> AvailableBuckets { get; set; } = new() { EJRASync.Lib.Constants.CarsBucketName, EJRASync.Lib.Constants.TracksBucketName, EJRASync.Lib.Constants.FontsBucketName, EJRASync.Lib.Constants.AppsBucketName };
	}
}