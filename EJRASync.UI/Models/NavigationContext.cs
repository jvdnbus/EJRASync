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

		public List<string> AvailableBuckets { get; set; } = new() {
			Lib.Constants.CarsBucketName,
			Lib.Constants.TracksBucketName,
			Lib.Constants.FontsBucketName,
			Lib.Constants.GuiBucketName,
			Lib.Constants.AppsBucketName
		};
	}
}