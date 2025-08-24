using CommunityToolkit.Mvvm.ComponentModel;

namespace EJRASync.UI.Models {
	public partial class ArchiveItem : ObservableObject {
		[ObservableProperty]
		private string _name = string.Empty;

		[ObservableProperty]
		private bool _isSelected = false;

		[ObservableProperty]
		private string _bucketName = string.Empty;

		[ObservableProperty]
		private long _estimatedSize = 0;

		public ArchiveItem(string name, string bucketName, long estimatedSize = 0) {
			Name = name;
			BucketName = bucketName;
			EstimatedSize = estimatedSize;
		}
	}
}