using EJRASync.Lib.Models;
using System.Text;
using System.Text.Json;

namespace EJRASync.Lib.Services {
	public class EjraApiService : IEjraApiService {
		private readonly HttpClient _httpClient;

		private const string USER_READ_KEY = "user:read";
		private const string USER_WRITE_KEY = "user:write";

		public EjraApiService() {
			_httpClient = new HttpClient();
		}

		public async Task<AuthTokenResponse?> GetTokensAsync(OAuthToken? oauthToken = null) {
			try {
				HttpResponseMessage response;

				if (string.IsNullOrEmpty(oauthToken?.UserProfile?.Properties.AccessToken)) {
					response = await _httpClient.GetAsync($"{Constants.EjraApi}/r2/token");
				} else {
					var request = new HttpRequestMessage(HttpMethod.Get, $"{Constants.EjraApi}/r2/token");
					request.Headers.Add("x-api-token", oauthToken?.UserProfile?.Properties.AccessToken);
					response = await _httpClient.SendAsync(request);
				}

				response.EnsureSuccessStatusCode();

				var content = await response.Content.ReadAsStringAsync();

				var jsonResponse = JsonSerializer.Deserialize(content, ApiJsonSerializerContext.Default.DictionaryStringJsonElement);
				if (jsonResponse == null) return null;

				var result = new AuthTokenResponse();

				if (jsonResponse.ContainsKey(USER_READ_KEY)) {
					var userReadJson = jsonResponse[USER_READ_KEY].GetRawText();
					if (!string.IsNullOrEmpty(userReadJson)) {
						result.UserRead = JsonSerializer.Deserialize(userReadJson, ApiJsonSerializerContext.Default.UserReadTokenJson)?.ToUserReadToken();
					}
				}

				if (jsonResponse.ContainsKey(USER_WRITE_KEY)) {
					var userWriteJson = jsonResponse[USER_WRITE_KEY].GetRawText();
					if (!string.IsNullOrEmpty(userWriteJson)) {
						result.UserWrite = JsonSerializer.Deserialize(userWriteJson, ApiJsonSerializerContext.Default.UserWriteTokenJson)?.ToUserWriteToken();
					}
				}

				return result;
			} catch (Exception ex) {
				SentrySdk.CaptureException(ex);
				return null;
			}
		}

		public async Task<ArchiveMetadataResponse?> SubmitArchiveMetadataAsync(List<ArchiveMetadataItem> metadata, OAuthToken? oauthToken = null, CancellationToken cancellationToken = default) {
			try {
				var request = new ArchiveMetadataRequest {
					Metadata = metadata
				};

				var jsonContent = JsonSerializer.Serialize(request, new JsonSerializerOptions {
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});

				var httpRequest = new HttpRequestMessage(HttpMethod.Put, $"{Constants.EjraApi}/archive/metadata");
				httpRequest.Headers.Add("x-api-token", oauthToken?.UserProfile?.Properties.AccessToken);
				httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

				var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

				if (!response.IsSuccessStatusCode) {
					var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
					var errorResponse = JsonSerializer.Deserialize<ApiErrorResponse>(errorContent, new JsonSerializerOptions {
						PropertyNamingPolicy = JsonNamingPolicy.CamelCase
					});
					throw new HttpRequestException($"API request failed: {errorResponse?.Error ?? response.ReasonPhrase}");
				}

				var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
				return JsonSerializer.Deserialize<ArchiveMetadataResponse>(responseContent, new JsonSerializerOptions {
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});

			} catch (Exception ex) {
				SentrySdk.CaptureException(ex);
				throw;
			}
		}

		public void Dispose() {
			_httpClient?.Dispose();
		}
	}
}