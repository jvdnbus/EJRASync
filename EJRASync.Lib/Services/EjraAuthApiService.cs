using System.Text.Json;

namespace EJRASync.Lib.Services {
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

		public void Dispose() {
			_httpClient?.Dispose();
		}
	}
}