using System.Text;
using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace CustomGolfStats.App.Platforms.Android.Services;

/// <summary>
/// Values encrypted with an AES-256-GCM key that lives in the Android Keystore (never exportable), stored as ciphertext in the
/// app's private preferences. A value that can no longer be decrypted (the key was lost, for example after a restore to another
/// device) reads as absent, which signs the golfer out instead of failing.
/// </summary>
public sealed class KeystoreSecureStore(Context context) : ISecureStore
{
    private const string KeyAlias = "cgs.securestore.v1";
    private const string PreferencesName = "cgs.securestore";
    private const string Transformation = "AES/GCM/NoPadding";
    private const int TagBits = 128;
    private readonly object _lock = new();

    private ISharedPreferences Preferences => context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public Task<string?> GetAsync(string key)
    {
        lock (_lock)
        {
            var stored = Preferences.GetString(key, null);
            if (stored is null)
                return Task.FromResult<string?>(null);
            try
            {
                var bytes = Convert.FromBase64String(stored);
                var ivLength = bytes[0];
                var cipher = Cipher.GetInstance(Transformation)!;
                cipher.Init(CipherMode.DecryptMode, GetOrCreateKey(), new GCMParameterSpec(TagBits, bytes, 1, ivLength));
                var plain = cipher.DoFinal(bytes, 1 + ivLength, bytes.Length - 1 - ivLength)!;
                return Task.FromResult<string?>(Encoding.UTF8.GetString(plain));
            }
            catch (Exception ex) when (ex is GeneralSecurityException or FormatException or IndexOutOfRangeException or Java.Lang.Exception)
            {
                Preferences.Edit()!.Remove(key)!.Commit();
                return Task.FromResult<string?>(null);
            }
        }
    }

    public Task SetAsync(string key, string value)
    {
        lock (_lock)
        {
            var cipher = Cipher.GetInstance(Transformation)!;
            cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());
            var iv = cipher.GetIV()!;
            var encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(value))!;
            var payload = new byte[1 + iv.Length + encrypted.Length];
            payload[0] = (byte)iv.Length;
            iv.CopyTo(payload, 1);
            encrypted.CopyTo(payload, 1 + iv.Length);
            if (!Preferences.Edit()!.PutString(key, Convert.ToBase64String(payload))!.Commit())
                throw new InvalidOperationException("The secure value could not be saved.");
            return Task.CompletedTask;
        }
    }

    public Task RemoveAsync(string key)
    {
        lock (_lock)
        {
            if (!Preferences.Edit()!.Remove(key)!.Commit())
                throw new InvalidOperationException("The secure value could not be removed.");
            return Task.CompletedTask;
        }
    }

    private static IKey GetOrCreateKey()
    {
        var keyStore = KeyStore.GetInstance("AndroidKeyStore")!;
        keyStore.Load(null);
        if (keyStore.GetKey(KeyAlias, null) is { } existing)
            return existing;

        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
        generator.Init(new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .Build());
        return generator.GenerateKey()!;
    }
}
