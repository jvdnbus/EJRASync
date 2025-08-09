using EJRASync.Lib;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EJRASync.UI.Services {
	public class EjraAuthApiService : IEjraAuthApiService {
		private readonly HttpClient _httpClient;

		private const string USER_READ_KEY = "user:read";
		private const string USER_WRITE_KEY = "user:write";

		public EjraAuthApiService() {
			_httpClient = new HttpClient();
		}

		public async Task<AuthTokenResponse?> GetTokensAsync(OAuthToken? oauthToken = null) {
			try {
				HttpResponseMessage response;
				
				if (string.IsNullOrEmpty(oauthToken?.UserProfile?.Properties.AccessToken)) {
					response = await _httpClient.GetAsync($"{Constants.EjraAuthApi}/r2/token");
				} else {
					var request = new HttpRequestMessage(HttpMethod.Get, $"{Constants.EjraAuthApi}/r2/token");
					request.Headers.Add("x-api-token", oauthToken?.UserProfile?.Properties.AccessToken);
					response = await _httpClient.SendAsync(request);
				}
				
				response.EnsureSuccessStatusCode();

				var content = await response.Content.ReadAsStringAsync();
				var options = new JsonSerializerOptions {
					PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
					PropertyNameCaseInsensitive = true
				};

				var jsonResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(content, options);
				if (jsonResponse == null) return null;

				var result = new AuthTokenResponse();

				if (jsonResponse.ContainsKey(USER_READ_KEY)) {
					var userReadJson = jsonResponse[USER_READ_KEY].ToString();
					if (!string.IsNullOrEmpty(userReadJson)) {
						result.UserRead = JsonSerializer.Deserialize<UserReadTokenJson>(userReadJson, options)?.ToUserReadToken();
					}
				}

				if (jsonResponse.ContainsKey(USER_WRITE_KEY)) {
					var userWriteJson = jsonResponse[USER_WRITE_KEY].ToString();
					if (!string.IsNullOrEmpty(userWriteJson)) {
						result.UserWrite = JsonSerializer.Deserialize<UserWriteTokenJson>(userWriteJson, options)?.ToUserWriteToken();
					}
				}

				return result;
			} catch (Exception ex) {
				SentrySdk.CaptureException(ex);
				return null;
			}
		}
	}

	internal class UserReadTokenJson {
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

	internal class UserWriteTokenJson {
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

	internal class AwsCredentialsJson {
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