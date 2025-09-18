using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace EJRASync.UI.ViewModels {
	public abstract partial class BaseFileListViewModel<T> : ObservableObject where T : class {
		[ObservableProperty]
		private ObservableCollection<T> _files = new();

		[ObservableProperty]
		private string _currentPath = string.Empty;

		[ObservableProperty]
		private T? _selectedFile;

		[ObservableProperty]
		private ObservableCollection<T> _selectedFiles = new();

		[ObservableProperty]
		private bool _isLoading = false;

		[ObservableProperty]
		private bool _isSearchVisible = false;

		[ObservableProperty]
		private string _searchText = string.Empty;

		protected ObservableCollection<T> _allFiles = new();
		protected ObservableCollection<T> _filteredFiles = new();

		// Quick navigation properties
		private string _quickNavText = string.Empty;
		private DispatcherTimer? _quickNavTimer;

		protected BaseFileListViewModel() {
			// Initialize Files to point to the filtered collection
			Files = _filteredFiles;
		}

		partial void OnSearchTextChanged(string value) {
			FilterFiles();
		}

		[RelayCommand]
		private void ToggleSearch() {
			IsSearchVisible = !IsSearchVisible;
			if (!IsSearchVisible) {
				SearchText = string.Empty;
			}
		}

		[RelayCommand]
		private void HideSearch() {
			IsSearchVisible = false;
			SearchText = string.Empty;
		}

		protected virtual void FilterFiles() {
			_filteredFiles.Clear();

			if (string.IsNullOrWhiteSpace(SearchText)) {
				foreach (var file in _allFiles) {
					_filteredFiles.Add(file);
				}
			} else {
				var searchTerm = SearchText.ToLowerInvariant();
				foreach (var file in _allFiles) {
					if (GetFileName(file).ToLowerInvariant().Contains(searchTerm)) {
						_filteredFiles.Add(file);
					}
				}
			}
		}

		public void HandleQuickNavigation(char character) {
			if (!char.IsLetterOrDigit(character))
				return;

			// Reset timer
			_quickNavTimer?.Stop();
			_quickNavTimer = new DispatcherTimer {
				Interval = TimeSpan.FromSeconds(1)
			};
			_quickNavTimer.Tick += (s, e) => {
				_quickNavText = string.Empty;
				_quickNavTimer?.Stop();
			};

			// Append character to search string
			_quickNavText += character.ToString().ToLowerInvariant();

			// Find first matching item
			var matchingItem = Files.FirstOrDefault(f => GetFileName(f).StartsWith(_quickNavText, StringComparison.OrdinalIgnoreCase));
			if (matchingItem != null) {
				SelectedFiles.Clear();
				SelectedFiles.Add(matchingItem);
				SelectedFile = matchingItem;
			}

			_quickNavTimer.Start();
		}

		// Abstract method that derived classes must implement to get the file name for filtering and navigation
		protected abstract string GetFileName(T file);
	}
}