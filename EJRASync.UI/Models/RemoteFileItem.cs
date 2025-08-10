using CommunityToolkit.Mvvm.ComponentModel;

namespace EJRASync.UI.Models {
	public partial class RemoteFileItem : ObservableObject {
		[ObservableProperty]
		private string _name = string.Empty;
		
		[ObservableProperty]
		private string _key = string.Empty;
		
		[ObservableProperty]
		private string _displaySize = string.Empty;
		
		[ObservableProperty]
		private DateTime _lastModified;
		
		[ObservableProperty]
		private long _sizeBytes;
		
		[ObservableProperty]
		private bool _isDirectory;
		
		[ObservableProperty]
		private bool _isCompressed;
		
		[ObservableProperty]
		private bool? _isActive;
		
		[ObservableProperty]
		private string _eTag = string.Empty;
		
		[ObservableProperty]
		private string? _originalHash;
		
		[ObservableProperty]
		private string _status = string.Empty;
		
		[ObservableProperty]
		private bool _isPendingChange;
		
		[ObservableProperty]
		private bool _isFlashing;
		
		[ObservableProperty]
		private bool _isPreviewOnly;
	}
}