using CommunityToolkit.Mvvm.ComponentModel;

namespace EJRASync.UI.Models {
	public partial class RemoteFileItem : ObservableObject {
		private readonly EJRASync.Lib.Models.RemoteFile _libModel;

		public RemoteFileItem(EJRASync.Lib.Models.RemoteFile libModel) {
			_libModel = libModel;
		}

		public RemoteFileItem() {
			_libModel = new EJRASync.Lib.Models.RemoteFile();
		}

		// Expose the underlying lib model for service operations
		public EJRASync.Lib.Models.RemoteFile LibModel => _libModel;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private string _name = string.Empty;
		partial void OnNameChanged(string value) => _libModel.Name = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private string _key = string.Empty;
		partial void OnKeyChanged(string value) => _libModel.Key = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private string _displaySize = string.Empty;
		partial void OnDisplaySizeChanged(string value) => _libModel.DisplaySize = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private DateTime _lastModified;
		partial void OnLastModifiedChanged(DateTime value) => _libModel.LastModified = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private long _sizeBytes;
		partial void OnSizeBytesChanged(long value) => _libModel.SizeBytes = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private bool _isDirectory;
		partial void OnIsDirectoryChanged(bool value) => _libModel.IsDirectory = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private bool _isCompressed;
		partial void OnIsCompressedChanged(bool value) => _libModel.IsCompressed = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private bool? _isActive;
		partial void OnIsActiveChanged(bool? value) => _libModel.IsActive = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private string _eTag = string.Empty;
		partial void OnETagChanged(string value) => _libModel.ETag = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private string? _originalHash;
		partial void OnOriginalHashChanged(string? value) => _libModel.OriginalHash = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private string _status = string.Empty;
		partial void OnStatusChanged(string value) => _libModel.Status = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private bool _isPendingChange;
		partial void OnIsPendingChangeChanged(bool value) => _libModel.IsPendingChange = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private bool _isFlashing;
		partial void OnIsFlashingChanged(bool value) => _libModel.IsFlashing = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private bool _isPreviewOnly;
		partial void OnIsPreviewOnlyChanged(bool value) => _libModel.IsPreviewOnly = value;

		// Static method to convert from lib model to UI model
		public static RemoteFileItem FromLib(EJRASync.Lib.Models.RemoteFile libModel) {
			var uiModel = new RemoteFileItem(libModel);
			uiModel.CopyFromLib();
			return uiModel;
		}

		// Method to sync UI properties from lib model
		public void CopyFromLib() {
			Name = _libModel.Name;
			Key = _libModel.Key;
			DisplaySize = _libModel.DisplaySize;
			LastModified = _libModel.LastModified;
			SizeBytes = _libModel.SizeBytes;
			IsDirectory = _libModel.IsDirectory;
			IsCompressed = _libModel.IsCompressed;
			IsActive = _libModel.IsActive;
			ETag = _libModel.ETag;
			OriginalHash = _libModel.OriginalHash;
			Status = _libModel.Status;
			IsPendingChange = _libModel.IsPendingChange;
			IsFlashing = _libModel.IsFlashing;
			IsPreviewOnly = _libModel.IsPreviewOnly;
		}
	}
}