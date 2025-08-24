using System.Text.Json.Serialization;

namespace EJRASync.Lib.Services {
	public enum AuthProvider {
		Discord = 0,
		Twitch = 1,
		Email = 2
	}
	public class OAuthToken {
		public string AccessToken { get; set; } = string.Empty;
		public string RefreshToken { get; set; } = string.Empty;
		public int ExpiresIn { get; set; }
		public UserTokenClaims? UserProfile { get; set; }

		// Convenience properties to get user info regardless of provider
		public string? GetDisplayName() {
			return UserProfile?.Properties.Provider switch {
				AuthProvider.Twitch => UserProfile.Properties.Data.Data?.FirstOrDefault()?.DisplayName,
				AuthProvider.Discord => UserProfile.Properties.Data.User?.GlobalName ?? UserProfile.Properties.Data.User?.Username,
				_ => null
			};
		}

		public string? GetUserId() {
			return UserProfile?.Properties.Provider switch {
				AuthProvider.Twitch => UserProfile.Properties.Data.Data?.FirstOrDefault()?.Id,
				AuthProvider.Discord => UserProfile.Properties.Data.User?.Id,
				_ => null
			};
		}

		public string? GetProfileImageUrl() {
			return UserProfile?.Properties.Provider switch {
				AuthProvider.Twitch => UserProfile.Properties.Data.Data?.FirstOrDefault()?.ProfileImageUrl,
				AuthProvider.Discord => UserProfile.Properties.Data.User?.Avatar != null
					? $"https://cdn.discordapp.com/avatars/{UserProfile.Properties.Data.User.Id}/{UserProfile.Properties.Data.User.Avatar}.png"
					: null,
				_ => null
			};
		}

		public string GetProviderName() {
			return UserProfile?.Properties.Provider switch {
				AuthProvider.Discord => "Discord",
				AuthProvider.Twitch => "Twitch",
				AuthProvider.Email => "Email",
				_ => "Unknown"
			};
		}

		public bool IsExpired() {
			if (UserProfile?.Exp == null) return true;

			var expirationTime = DateTimeOffset.FromUnixTimeSeconds(UserProfile.Exp);
			return DateTimeOffset.UtcNow >= expirationTime;
		}
	}

	public class UserTokenClaims {
		public string Mode { get; set; } = string.Empty;
		public string Type { get; set; } = string.Empty;
		public UserProperties Properties { get; set; } = new();
		public string Aud { get; set; } = string.Empty;
		public string Iss { get; set; } = string.Empty;
		public string Sub { get; set; } = string.Empty;
		public long Exp { get; set; }
	}

	public class UserProperties {
		public string Id { get; set; } = string.Empty;
		public string Scope { get; set; } = string.Empty;
		[JsonPropertyName("access_token")]
		public string AccessToken { get; set; } = string.Empty;
		public AuthProvider Provider { get; set; }
		public UserProviderData Data { get; set; } = new();
	}

	public class UserProviderData {
		// Twitch provider data (AuthProvider.Twitch)
		public List<TwitchUserProfile>? Data { get; set; }

		// Discord provider data (AuthProvider.Discord)
		public DiscordApplication? Application { get; set; }
		public string? Expires { get; set; }
		public DiscordUser? User { get; set; }
	}

	// Twitch user profile data
	public class TwitchUserProfile {
		public string Id { get; set; } = string.Empty;
		public string Login { get; set; } = string.Empty;
		[JsonPropertyName("display_name")]
		public string DisplayName { get; set; } = string.Empty;
		public string Type { get; set; } = string.Empty;
		[JsonPropertyName("broadcaster_type")]
		public string BroadcasterType { get; set; } = string.Empty;
		public string Description { get; set; } = string.Empty;
		[JsonPropertyName("profile_image_url")]
		public string ProfileImageUrl { get; set; } = string.Empty;
		[JsonPropertyName("offline_image_url")]
		public string OfflineImageUrl { get; set; } = string.Empty;
		[JsonPropertyName("view_count")]
		public int ViewCount { get; set; }
		[JsonPropertyName("created_at")]
		public string CreatedAt { get; set; } = string.Empty;
	}

	// Discord user profile data
	public class DiscordUser {
		public string Id { get; set; } = string.Empty;
		public string Username { get; set; } = string.Empty;
		public string? Avatar { get; set; }
		public string Discriminator { get; set; } = string.Empty;
		[JsonPropertyName("public_flags")]
		public int PublicFlags { get; set; }
		public int Flags { get; set; }
		public string? Banner { get; set; }
		[JsonPropertyName("accent_color")]
		public int? AccentColor { get; set; }
		[JsonPropertyName("global_name")]
		public string? GlobalName { get; set; }
		[JsonPropertyName("banner_color")]
		public string? BannerColor { get; set; }
		public DiscordClan? Clan { get; set; }
		[JsonPropertyName("primary_guild")]
		public DiscordClan? PrimaryGuild { get; set; }
	}

	public class DiscordApplication {
		public string Id { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;
		public string? Icon { get; set; }
		public string Description { get; set; } = string.Empty;
		public DiscordBot? Bot { get; set; }
		[JsonPropertyName("verify_key")]
		public string VerifyKey { get; set; } = string.Empty;
	}

	public class DiscordBot {
		public string Id { get; set; } = string.Empty;
		public string Username { get; set; } = string.Empty;
		public string? Avatar { get; set; }
		public string Discriminator { get; set; } = string.Empty;
		public bool Bot { get; set; }
	}

	public class DiscordClan {
		[JsonPropertyName("identity_guild_id")]
		public string? IdentityGuildId { get; set; }
		[JsonPropertyName("identity_enabled")]
		public bool IdentityEnabled { get; set; }
		public string? Tag { get; set; }
		public string? Badge { get; set; }
	}

	public interface IEjraAuthService {
		Task<OAuthToken?> AuthenticateAsync();
		Task SaveTokenAsync(OAuthToken token);
		Task<OAuthToken?> LoadSavedTokenAsync();
		Task ClearSavedTokenAsync();
	}
}