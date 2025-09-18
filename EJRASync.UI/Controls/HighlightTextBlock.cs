using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace EJRASync.UI.Controls {
	public class HighlightTextBlock : TextBlock {
		public static readonly DependencyProperty HighlightTextProperty =
			DependencyProperty.Register("HighlightText", typeof(string), typeof(HighlightTextBlock),
				new PropertyMetadata(string.Empty, OnHighlightTextChanged));

		public static readonly DependencyProperty SourceTextProperty =
			DependencyProperty.Register("SourceText", typeof(string), typeof(HighlightTextBlock),
				new PropertyMetadata(string.Empty, OnSourceTextChanged));

		public string HighlightText {
			get { return (string)GetValue(HighlightTextProperty); }
			set { SetValue(HighlightTextProperty, value); }
		}

		public string SourceText {
			get { return (string)GetValue(SourceTextProperty); }
			set { SetValue(SourceTextProperty, value); }
		}

		private static void OnHighlightTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			((HighlightTextBlock)d).UpdateText();
		}

		private static void OnSourceTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			((HighlightTextBlock)d).UpdateText();
		}

		private void UpdateText() {
			Inlines.Clear();

			if (string.IsNullOrEmpty(SourceText)) {
				return;
			}

			if (string.IsNullOrWhiteSpace(HighlightText)) {
				Inlines.Add(new Run(SourceText));
				return;
			}

			var searchTextLower = HighlightText.ToLowerInvariant();
			var textLower = SourceText.ToLowerInvariant();
			var lastIndex = 0;

			int index = textLower.IndexOf(searchTextLower, lastIndex);
			while (index >= 0) {
				// Add text before the match
				if (index > lastIndex) {
					Inlines.Add(new Run(SourceText.Substring(lastIndex, index - lastIndex)));
				}

				// Add highlighted match
				var highlightedRun = new Run(SourceText.Substring(index, HighlightText.Length));
				highlightedRun.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)); // Dark orange
				highlightedRun.Foreground = new SolidColorBrush(Colors.Black);
				Inlines.Add(highlightedRun);

				lastIndex = index + HighlightText.Length;
				index = textLower.IndexOf(searchTextLower, lastIndex);
			}

			// Add remaining text
			if (lastIndex < SourceText.Length) {
				Inlines.Add(new Run(SourceText.Substring(lastIndex)));
			}
		}
	}
}