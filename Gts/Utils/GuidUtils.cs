using System.Security.Cryptography;
using System.Text;

namespace Gts.Utils;

/// <summary>
/// UUID version 5 (SHA-1 hash based) as per RFC 4122.
/// GTS uses namespace UUID(NAMESPACE_URL, "gts") and name = the GTS identifier string.
/// </summary>
internal static class GuidUtils
{    
    /// <summary>
    /// GTS namespace: uuid5(NAMESPACE_URL, "gts") as per spec.
    /// RFC 4122 namespace for URLs
    /// </summary>
    public static readonly Guid GtsNamespace = Create(
        new("6ba7b811-9dad-11d1-80b4-00c04fd430c8"), "gts");
    
    /// <summary>
    /// Creates a UUID v5 from a namespace and name (UTF-8 bytes are hashed).
    /// </summary>
    /// <param name="namespaceId">RFC 4122 namespace UUID.</param>
    /// <param name="name">Name string to hash (e.g. GTS ID).</param>
    /// <returns>The generated UUID v5.</returns>
    internal static Guid Create(Guid namespaceId, string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));

        byte[] namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);

        byte[] nameBytes = Encoding.UTF8.GetBytes(name);

        byte[] hashInput = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, hashInput, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, hashInput, namespaceBytes.Length, nameBytes.Length);

        byte[] hash = SHA1.HashData(hashInput);

        // Set version (5) and variant (RFC 4122)
        byte[] result = new byte[16];
        Buffer.BlockCopy(hash, 0, result, 0, 16);
        result[6] = (byte)((result[6] & 0x0F) | 0x50);
        result[8] = (byte)((result[8] & 0x3F) | 0x80);

        SwapByteOrder(result);
        return new Guid(result);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        if (guid.Length != 16) return;
        Swap(guid, 0, 3);
        Swap(guid, 1, 2);
        Swap(guid, 4, 5);
        Swap(guid, 6, 7);
    }

    private static void Swap(byte[] arr, int i, int j)
    {
        (arr[i], arr[j]) = (arr[j], arr[i]);
    }
}
