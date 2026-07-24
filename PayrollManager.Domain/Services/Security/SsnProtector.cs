using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PayrollManager.Domain.Services.Security;

/// <summary>
/// The result of protecting a Social Security Number: the ciphertext to store at rest and the
/// last four digits (safe to store in plaintext and display on a pay stub).
/// </summary>
public sealed record ProtectedSsn(string Encrypted, string Last4);

/// <summary>
/// Encrypts and decrypts SSNs. Behind an interface so tests can inject a reversible fake rather
/// than depend on the OS key store.
/// </summary>
public interface ISsnProtector
{
    /// <summary>
    /// Normalizes a raw SSN (strips non-digits, requires exactly 9), encrypts it, and returns the
    /// ciphertext plus the last four digits.
    /// </summary>
    ProtectedSsn Protect(string rawSsn);

    /// <summary>Recovers the full SSN from ciphertext. Rarely needed - the pay stub uses last-4.</summary>
    string Unprotect(string encrypted);

    /// <summary>True if the value is nine digits after stripping separators.</summary>
    bool IsValidSsn(string rawSsn);
}

/// <summary>
/// Windows DPAPI-backed SSN protection. The OS holds the key material, so there is no
/// hand-rolled crypto and no key file for us to protect.
///
/// TRADEOFFS (intentional for a local single-user desktop app):
///   - DataProtectionScope.CurrentUser ties the ciphertext to this Windows user AND machine.
///     A database copied to another machine/user, or a lost Windows profile, leaves stored
///     SSNs undecryptable. The last-4 (stored separately in plaintext) is still readable, and a
///     full SSN is re-enterable reference data. W-2 generation, which would need the full SSN,
///     is out of scope.
///   - Windows-only. This app targets Windows (WinUI heritage, WebView2, win-x64 publish);
///     ProtectedData throws on other platforms. The DPAPI calls below suppress the
///     platform-compatibility analyzer (CA1416) locally, since this is a deliberate,
///     documented Windows-only implementation.
/// </summary>
public sealed class DpapiSsnProtector : ISsnProtector
{
    // Secondary entropy mixed into the DPAPI blob - app-specific, not a secret.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PayrollManager.Ssn.v1");

    private static readonly Regex NineDigits = new(@"^\d{9}$", RegexOptions.Compiled);

    public bool IsValidSsn(string rawSsn) => NineDigits.IsMatch(Normalize(rawSsn));

    public ProtectedSsn Protect(string rawSsn)
    {
        var digits = Normalize(rawSsn);
        if (!NineDigits.IsMatch(digits))
        {
            throw new ArgumentException("An SSN must contain exactly nine digits.", nameof(rawSsn));
        }

#pragma warning disable CA1416 // DPAPI is Windows-only by design; see the class summary.
        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(digits), Entropy, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416

        return new ProtectedSsn(Convert.ToBase64String(cipher), digits[^4..]);
    }

    public string Unprotect(string encrypted)
    {
#pragma warning disable CA1416 // DPAPI is Windows-only by design; see the class summary.
        var plain = ProtectedData.Unprotect(
            Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416

        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Removes spaces and dashes so "123-45-6789" and "123456789" are equivalent.</summary>
    private static string Normalize(string rawSsn) =>
        new(rawSsn.Where(char.IsDigit).ToArray());
}
