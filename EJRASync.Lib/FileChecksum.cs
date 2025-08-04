using System.Security.Cryptography;

namespace EJRASync.Lib {
	public static class FileChecksum {
		public static string Calculate(string filePath) {
			using var md5 = MD5.Create();
			using var stream = File.OpenRead(filePath);
			var hash = md5.ComputeHash(stream);
			return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
		}
	}
}
