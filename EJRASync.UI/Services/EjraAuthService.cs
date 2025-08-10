using EJRASync.Lib;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Net;
using System.Text;
using System.IO;

namespace EJRASync.UI.Services {
	public class EjraAuthService : IEjraAuthService {
		private readonly HttpClient _httpClient;
		private readonly string _tokenFilePath;

		public EjraAuthService() {
			_httpClient = new HttpClient();
			
			// Store token in the user's AppData folder
			var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			var appFolder = Path.Combine(appDataPath, "EJRASync");
			Directory.CreateDirectory(appFolder);
			_tokenFilePath = Path.Combine(appFolder, "oauth_token.json");
		}

		public async Task<OAuthToken?> AuthenticateAsync() {
			try {
				var authUrl = string.Format(Constants.EjraAuth,
					Constants.EjraAuthClientId,
					Uri.EscapeDataString(Constants.EjraAuthRedirectUri));

				// Start local HTTP server to listen for the callback
				var redirectUri = new Uri(Constants.EjraAuthRedirectUri);
				var listenerPrefix = $"{redirectUri.Scheme}://{redirectUri.Host}:{redirectUri.Port}/";
				
				var httpListener = new HttpListener();
				httpListener.Prefixes.Add(listenerPrefix);
				httpListener.Start();

				var authTokenTask = ListenForAuthTokenAsync(httpListener);

				// Open the authorization URL in the default browser
				Process.Start(new ProcessStartInfo {
					FileName = authUrl,
					UseShellExecute = true
				});

				// Wait for the authorization code with timeout
				var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
				var completedTask = await Task.WhenAny(authTokenTask, timeoutTask);

				httpListener.Stop();
				httpListener.Close();

				if (completedTask == authTokenTask) {
					return await authTokenTask;
				}

				return null; // Timeout
			}
			catch (Exception ex) {
				SentrySdk.CaptureException(ex);
				return null;
			}
		}

		private async Task<OAuthToken?> FetchTokensAsync(string authorizationCode) {
		try {
			var formData = new List<KeyValuePair<string, string>>
			{
				new("grant_type", "authorization_code"),
				new("code", authorizationCode),
				new("client_id", Constants.EjraAuthClientId),
				new("redirect_uri", Constants.EjraAuthRedirectUri)
			};

			var formContent = new FormUrlEncodedContent(formData);
			var response = await _httpClient.PostAsync(Constants.EjraAuthTokenExchange, formContent);

			if (response.IsSuccessStatusCode) {
				var content = await response.Content.ReadAsStringAsync();
				var options = new JsonSerializerOptions {
					PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
					PropertyNameCaseInsensitive = true
				};

				var tokenResponse = JsonSerializer.Deserialize<OAuthToken>(content, options);
				return tokenResponse;
			}
			else {
				var errorContent = await response.Content.ReadAsStringAsync();
				Debug.WriteLine($"Token request failed: {response.StatusCode} - {errorContent}");
				return null;
			}
		}
		catch (Exception ex) {
			SentrySdk.CaptureException(ex);
			return null;
		}
	}

	private async Task<OAuthToken> ListenForAuthTokenAsync(HttpListener httpListener) {
			try {
				var context = await httpListener.GetContextAsync();
				var request = context.Request;
				var response = context.Response;

				// Extract authorization code from query parameters (after ?)
				var query = request.Url?.Query;
				if (string.IsNullOrEmpty(query)) {
					await SendResponseAsync(response, "Error: No query parameters received");
					return null;
				}

				// Parse query string manually
				var queryDict = ParseQueryString(query);
				var authorizationCode = queryDict.GetValueOrDefault("code");
				if (string.IsNullOrEmpty(authorizationCode)) {
					await SendResponseAsync(response, "Error: No authorization code received");
					return null;
				}

				// Fetch tokens with authorization code
				var tokenResponse = await FetchTokensAsync(authorizationCode);
				if (tokenResponse != null) {
					// Decode the JWT access token to get user profile information
					tokenResponse.UserProfile = DecodeJwtToken(tokenResponse.AccessToken);
					await SendResponseAsync(response, "Authentication successful! You can close this window.");
					return tokenResponse;
				}

				await SendResponseAsync(response, "Error: Failed to obtain access token");
				return null;
			}
			catch (Exception ex) {
				SentrySdk.CaptureException(ex);
				return null;
			}
		}

		private UserTokenClaims? DecodeJwtToken(string accessToken) {
			try {
				if (string.IsNullOrEmpty(accessToken)) return null;

				// JWT tokens have three parts separated by dots: header.payload.signature
				var parts = accessToken.Split('.');
				if (parts.Length != 3) return null;

				// Decode the payload (second part)
				var payload = parts[1];
				
				// Add padding if needed for Base64 decoding
				switch (payload.Length % 4) {
					case 2: payload += "=="; break;
					case 3: payload += "="; break;
				}

				var jsonBytes = Convert.FromBase64String(payload);
				var json = Encoding.UTF8.GetString(jsonBytes);

				var options = new JsonSerializerOptions {
					PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
					PropertyNameCaseInsensitive = true
				};

				return JsonSerializer.Deserialize<UserTokenClaims>(json, options);
			}
			catch (Exception ex) {
				SentrySdk.CaptureException(ex);
				return null;
			}
		}

		private async Task SendResponseAsync(HttpListenerResponse response, string message) {
			var html = $@"
<!DOCTYPE html>
<html>
<head>
    <title>EJRA Sync Authentication</title>
    <style>
        body {{ font-family: Arial, sans-serif; text-align: center; padding: 50px; }}
        .message {{ font-size: 18px; margin: 20px; }}
    </style>
</head>
<body>
    <h1>EJRA Sync</h1>
    <div class=""message"">{message}</div>
</body>
</html>";

			var buffer = Encoding.UTF8.GetBytes(html);
			response.ContentLength64 = buffer.Length;
			response.ContentType = "text/html";
			response.StatusCode = 200;

			await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
			response.OutputStream.Close();
		}

		private Dictionary<string, string> ParseQueryString(string query) {
			var result = new Dictionary<string, string>();
			if (string.IsNullOrEmpty(query)) return result;

			// Remove leading '?' or '#' if present
			if (query.StartsWith("?") || query.StartsWith("#")) {
				query = query.Substring(1);
			}

			var pairs = query.Split('&');
			foreach (var pair in pairs) {
				var keyValue = pair.Split('=');
				if (keyValue.Length == 2) {
					var key = Uri.UnescapeDataString(keyValue[0]);
					var value = Uri.UnescapeDataString(keyValue[1]);
					result[key] = value;
				}
			}

			return result;
		}

		public async Task SaveTokenAsync(OAuthToken token) {
			try {
				var jsonOptions = new JsonSerializerOptions {
					PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
					PropertyNameCaseInsensitive = true,
					WriteIndented = true
				};
				
				var json = JsonSerializer.Serialize(token, jsonOptions);
				await File.WriteAllTextAsync(_tokenFilePath, json);
			}
			catch (Exception ex) {
				SentrySdk.CaptureException(ex);
			}
		}

		public async Task<OAuthToken?> LoadSavedTokenAsync() {
			try {
				if (!File.Exists(_tokenFilePath))
					return null;

				var json = await File.ReadAllTextAsync(_tokenFilePath);
				if (string.IsNullOrWhiteSpace(json))
					return null;

				var jsonOptions = new JsonSerializerOptions {
					PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
					PropertyNameCaseInsensitive = true
				};

				var token = JsonSerializer.Deserialize<OAuthToken>(json, jsonOptions);
				return token;
			}
			catch (Exception ex) {
				SentrySdk.CaptureException(ex);
				return null;
			}
		}

		public async Task ClearSavedTokenAsync() {
			try {
				if (File.Exists(_tokenFilePath)) {
					File.Delete(_tokenFilePath);
				}
			}
			catch (Exception ex) {
				SentrySdk.CaptureException(ex);
			}
		}
	}
}