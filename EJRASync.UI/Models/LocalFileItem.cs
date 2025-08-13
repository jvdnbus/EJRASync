using CommunityToolkit.Mvvm.ComponentModel;

namespace EJRASync.UI.Models {
	public partial class LocalFileItem : ObservableObject {
		private readonly EJRASync.Lib.Models.LocalFile _libModel;

		public LocalFileItem(EJRASync.Lib.Models.LocalFile libModel) {
			_libModel = libModel;
		}

		public LocalFileItem() {
			_libModel = new EJRASync.Lib.Models.LocalFile();
		}

		// Expose the underlying lib model for service operations
		public EJRASync.Lib.Models.LocalFile LibModel => _libModel;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private string _name = string.Empty;
		partial void OnNameChanged(string value) => _libModel.Name = value;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LibModel))]
		private string _fullPath = string.Empty;
		partial void OnFullPathChanged(string value) => _libModel.FullPath = value;

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
		private string _fileHash = string.Empty;
		partial void OnFileHashChanged(string value) => _libModel.FileHash = value;

		// Static method to convert from lib model to UI model
		public static LocalFileItem FromLib(EJRASync.Lib.Models.LocalFile libModel) {
			var uiModel = new LocalFileItem(libModel);
			uiModel.CopyFromLib();
			return uiModel;
		}

		// Method to sync UI properties from lib model
		public void CopyFromLib() {
			Name = _libModel.Name;
			FullPath = _libModel.FullPath;
			DisplaySize = _libModel.DisplaySize;
			LastModified = _libModel.LastModified;
			SizeBytes = _libModel.SizeBytes;
			IsDirectory = _libModel.IsDirectory;
			FileHash = _libModel.FileHash;
		}
	}
}