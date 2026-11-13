using Foundation;
using Security;

namespace CustomGolfStats.App.Platforms.iOS.Services;

/// <summary>Generic-password Keychain items, readable only on this device after its first unlock (never in backups to other devices).</summary>
public sealed class KeychainSecureStore : ISecureStore
{
    private const string Service = "com.customgolfstats.app.secure";
    private readonly object _lock = new();

    public Task<string?> GetAsync(string key)
    {
        lock (_lock)
        {
            var match = SecKeyChain.QueryAsRecord(Query(key), out var status);
            if (status == SecStatusCode.ItemNotFound)
                return Task.FromResult<string?>(null);
            if (status != SecStatusCode.Success)
                throw new InvalidOperationException($"Keychain read failed ({status}).");
            return Task.FromResult(match?.ValueData is { } data ? NSString.FromData(data, NSStringEncoding.UTF8)?.ToString() : null);
        }
    }

    public Task SetAsync(string key, string value)
    {
        lock (_lock)
        {
            var data = NSData.FromString(value, NSStringEncoding.UTF8);
            var status = SecKeyChain.Update(Query(key), new SecRecord(SecKind.GenericPassword) { ValueData = data });
            if (status == SecStatusCode.ItemNotFound)
            {
                var record = Query(key);
                record.ValueData = data;
                record.Accessible = SecAccessible.AfterFirstUnlockThisDeviceOnly;
                status = SecKeyChain.Add(record);
            }
            if (status != SecStatusCode.Success)
                throw new InvalidOperationException($"Keychain write failed ({status}).");
            return Task.CompletedTask;
        }
    }

    public Task RemoveAsync(string key)
    {
        lock (_lock)
        {
            var status = SecKeyChain.Remove(Query(key));
            if (status is not (SecStatusCode.Success or SecStatusCode.ItemNotFound))
                throw new InvalidOperationException($"Keychain delete failed ({status}).");
            return Task.CompletedTask;
        }
    }

    private static SecRecord Query(string key) => new(SecKind.GenericPassword) { Service = Service, Account = key };
}
