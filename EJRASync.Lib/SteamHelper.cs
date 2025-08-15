using EJRASync.Lib.Utils;
using Microsoft.Win32;

namespace EJRASync.Lib {
	public static class SteamHelper {
		/// <summary>
		/// Finds the Steam installation path.
		/// </summary>
		/// <returns>Steam installation path</returns>
		/// <exception cref="Exception"></exception>
		public static string FindSteam() {
			foreach (var (key, value) in Constants.SteamRegistryKeys) {
				var steamPath = Registry.GetValue(key, value, null);
				if (steamPath == null)
					continue;

				var libPath = PathUtils.NormalizePath(Path.Combine((string)steamPath, Constants.SteamLibraryFile), false);

				if (!File.Exists(libPath))
					continue;

				return libPath;
			}

			throw new Exception("Steam installation not found.");
		}

		/// <summary>
		/// Finds the Assetto Corsa installation path.
		/// </summary>
		/// <param name="steamLibraryPath">Steam library path</param>
		/// <returns>Assetto Corsa installation path</returns>
		/// <exception cref="Exception"></exception>
		public static string FindAssettoCorsa(string steamLibraryPath) {
			var steamLibrary = File.ReadAllLines(steamLibraryPath);
			var libraryPath = "";

			foreach (var line in steamLibrary) {
				if (line.Contains('"' + "path" + '"')) {
					libraryPath = line.Split('"')[3];
					continue;
				}

				if (!line.Contains('"' + Constants.AssettoCorsaAppId + '"'))
					continue;

				var acPath = PathUtils.NormalizePath(Path.Combine(libraryPath, Constants.AssettoCorsaSubPath), false);

				if (!Directory.Exists(acPath))
					continue;

				return acPath;
			}

			throw new Exception("Assetto Corsa installation not found.");
		}
	}
}
