using System.Text.Json.Serialization;

namespace EJRASync.Lib {
	[JsonSerializable(typeof(GitHubRelease))]
	[JsonSerializable(typeof(Asset))]
	public partial class GitHubReleaseContext : JsonSerializerContext {
	}
}
