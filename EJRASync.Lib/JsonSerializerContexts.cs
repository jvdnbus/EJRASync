using EJRASync.Lib.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EJRASync.Lib {
	// Context for authentication-related types
	[JsonSerializable(typeof(OAuthToken))]
	[JsonSerializable(typeof(UserTokenClaims))]
	[JsonSerializable(typeof(UserProperties))]
	[JsonSerializable(typeof(UserProviderData))]
	[JsonSerializable(typeof(TwitchUserProfile))]
	[JsonSerializable(typeof(DiscordUser))]
	[JsonSerializable(typeof(DiscordApplication))]
	[JsonSerializable(typeof(DiscordBot))]
	[JsonSerializable(typeof(DiscordClan))]
	[JsonSerializable(typeof(List<TwitchUserProfile>))]
	[JsonSerializable(typeof(List<string>))]
	[JsonSourceGenerationOptions(
		PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	)]
	public partial class AuthJsonSerializerContext : JsonSerializerContext {
	}

	// Context for API-related types (used for API calls)
	[JsonSerializable(typeof(Dictionary<string, JsonElement>), TypeInfoPropertyName = "DictionaryStringJsonElement")]
	[JsonSerializable(typeof(UserReadTokenJson))]
	[JsonSerializable(typeof(UserWriteTokenJson))]
	[JsonSerializable(typeof(AwsCredentialsJson))]
	[JsonSourceGenerationOptions(
		PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
		PropertyNameCaseInsensitive = true
	)]
	public partial class ApiJsonSerializerContext : JsonSerializerContext {
	}

	// Public classes for source generation (need to be public for JsonSerializerContext)
	public class UserReadTokenJson {
		[JsonPropertyName("cloudflare_token")]
		public string CloudflareToken { get; set; } = null!;

		[JsonPropertyName("aws")]
		public AwsCredentialsJson Aws { get; set; } = null!;

		[JsonPropertyName("s3_url")]
		public string S3Url { get; set; } = null!;

		public UserReadToken ToUserReadToken() => new() {
			CloudflareToken = CloudflareToken,
			Aws = Aws.ToAwsCredentials(),
			S3Url = S3Url
		};
	}

	public class UserWriteTokenJson {
		[JsonPropertyName("cloudflare_token")]
		public string CloudflareToken { get; set; } = null!;

		[JsonPropertyName("aws")]
		public AwsCredentialsJson Aws { get; set; } = null!;

		[JsonPropertyName("s3_url")]
		public string S3Url { get; set; } = null!;

		public UserWriteToken ToUserWriteToken() => new() {
			CloudflareToken = CloudflareToken,
			Aws = Aws.ToAwsCredentials(),
			S3Url = S3Url
		};
	}

	public class AwsCredentialsJson {
		[JsonPropertyName("access_key_id")]
		public string AccessKeyId { get; set; } = null!;

		[JsonPropertyName("secret_access_key")]
		public string SecretAccessKey { get; set; } = null!;

		public AwsCredentials ToAwsCredentials() => new() {
			AccessKeyId = AccessKeyId,
			SecretAccessKey = SecretAccessKey
		};
	}
}