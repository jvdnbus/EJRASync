using System.Windows;

namespace EJRASync.UI.Utils {
	public static class DispatcherExtensions {
		/// <summary>
		/// Invokes an action on the UI thread
		/// </summary>
		public static async Task InvokeUIAsync(this object _, Action action) {
			await Application.Current.Dispatcher.InvokeAsync(action);
		}

		/// <summary>
		/// Invokes a function on the UI thread and returns the result
		/// </summary>
		public static async Task<T> InvokeUIAsync<T>(this object _, Func<T> func) {
			return await Application.Current.Dispatcher.InvokeAsync(func);
		}

		/// <summary>
		/// Invokes an async function on the UI thread
		/// </summary>
		public static async Task InvokeUIAsync(this object _, Func<Task> asyncFunc) {
			await await Application.Current.Dispatcher.InvokeAsync(asyncFunc);
		}

		/// <summary>
		/// Invokes an async function on the UI thread and returns the result
		/// </summary>
		public static async Task<T> InvokeUIAsync<T>(this object _, Func<Task<T>> asyncFunc) {
			return await await Application.Current.Dispatcher.InvokeAsync(asyncFunc);
		}

		/// <summary>
		/// Static helper for when you don't have an object to extend
		/// </summary>
		public static class UI {
			public static async Task InvokeAsync(Action action) {
				await Application.Current.Dispatcher.InvokeAsync(action);
			}

			public static async Task<T> InvokeAsync<T>(Func<T> func) {
				return await Application.Current.Dispatcher.InvokeAsync(func);
			}
		}
	}
}