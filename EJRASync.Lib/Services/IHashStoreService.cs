namespace EJRASync.Lib.Services {
	public interface IHashStoreService {
		Task InitializeBucketAsync(string bucketName);
		string? GetOriginalHash(string bucketName, string key);
		void SetOriginalHash(string bucketName, string key, string originalHash);
		void RemoveHash(string bucketName, string key);
		bool IsDirty(string bucketName);
		Task SaveToRemoteAsync(string bucketName);
		void MarkClean(string bucketName);
		Task RebuildHashStoreAsync(string bucketName);
	}
}