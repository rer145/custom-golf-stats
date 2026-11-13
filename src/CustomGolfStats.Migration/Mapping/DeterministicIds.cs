using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CustomGolfStats.Migration.Mapping;

/// <summary>
/// Ids for imported rows: a UUID version 5 (RFC 4122, SHA-1) of <c>"golftrack:&lt;table&gt;:&lt;id&gt;"</c> in a fixed
/// namespace, so every run gives a golftrack row the same id. Never change <see cref="Namespace"/> or the names: a re-run
/// finds what an earlier run imported by these ids.
/// </summary>
public static class DeterministicIds
{
    public static readonly Guid Namespace = Guid.Parse("f213513d-47c2-41e9-bf0a-dcaf37214dfe");

    /// <summary>The id of golftrack row <paramref name="id"/> of <paramref name="table"/> (for example <c>Round</c>).</summary>
    public static Guid For(string table, int id) =>
        Create(Namespace, string.Create(CultureInfo.InvariantCulture, $"golftrack:{table}:{id}"));

    /// <summary>UUID version 5 of <paramref name="name"/> (UTF-8) in <paramref name="namespaceId"/>.</summary>
    public static Guid Create(Guid namespaceId, string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[16 + nameBytes.Length];
        namespaceId.TryWriteBytes(input, bigEndian: true, out _);
        nameBytes.CopyTo(input, 16);

        Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
#pragma warning disable CA5350 // SHA-1 is what RFC 4122 specifies for version 5; it is not used for security here.
        SHA1.HashData(input, hash);
#pragma warning restore CA5350

        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes, bigEndian: true);
    }
}
