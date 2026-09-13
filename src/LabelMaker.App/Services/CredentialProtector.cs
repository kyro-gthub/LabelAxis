using System.Security.Cryptography;
using System.Text;

namespace LabelMaker.App.Services;

/// <summary>
/// Keeps a remembered database password encrypted with Windows DPAPI for the signed-in
/// user. The project file only ever holds the encrypted form, which is useless on
/// another account or PC — there the password is simply asked for again.
/// </summary>
public static class CredentialProtector
{
    private static readonly byte[] Entropy = "LabelMaker.DataConnection.v1"u8.ToArray();

    public static string Protect(string password) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));

    /// <summary>The password, or null when there is none or it was encrypted by someone else.</summary>
    public static string? TryUnprotect(string? protectedPassword)
    {
        if (string.IsNullOrEmpty(protectedPassword))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedPassword), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
