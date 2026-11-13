namespace CustomGolfStats.App.Core.Platform;

/// <summary>Keychain / Android Keystore-backed storage. The only place auth tokens may be persisted.</summary>
public interface ISecureStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task RemoveAsync(string key);
}
