using EJRASync.Lib.Models;

namespace EJRASync.Lib.Services {
	public interface IEjraApiService {
		Task<AuthTokenResponse?> GetTokensAsync(OAuthToken? accessToken = null);
		Task<ArchiveMetadataResponse?> SubmitArchiveMetadataAsync(List<ArchiveMetadataItem> metadata, OAuthToken? accessToken = null, CancellationToken cancellationToken = default);
	}

	public class AuthTokenResponse {
		public UserReadToken UserRead { get; set; } = null!;
		public UserWriteToken? UserWrite { get; set; }
	}

	public class UserReadToken {
		public string CloudflareToken { get; set; } = null!;
		public AwsCredentials Aws { get; set; } = null!;
		public string S3Url { get; set; } = null!;
	}

	public class UserWriteToken {
		public string CloudflareToken { get; set; } = null!;
		public AwsCredentials Aws { get; set; } = null!;
		public string S3Url { get; set; } = null!;
	}

	public class AwsCredentials {
		public string AccessKeyId { get; set; } = null!;
		public string SecretAccessKey { get; set; } = null!;
	}
}