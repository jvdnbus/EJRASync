using System.Text.Json.Serialization;

namespace EJRASync.Lib {
	public class GitHubRelease {
		[JsonPropertyName("tag_name")]
		public string TagName { get; set; }
		[JsonPropertyName("assets")]
		public Asset[] Assets { get; set; }
	}
}
