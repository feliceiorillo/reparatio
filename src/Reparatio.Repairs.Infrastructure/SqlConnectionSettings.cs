using System.Security.Cryptography;
using System.Text;

namespace Reparatio.Repairs.Infrastructure;

public static class SqlConnectionSettings
{
    public static string DefaultSecretPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Codex", ".secrets", "reparatio", "sql.dpapi");

    public static string? ReadOptional()
    {
        var connection = Environment.GetEnvironmentVariable("REPARATIO_SQL_CONNECTION");
        if (!string.IsNullOrWhiteSpace(connection)) return connection;
        var path = Environment.GetEnvironmentVariable("REPARATIO_SQL_SECRET_PATH");
        if (string.IsNullOrWhiteSpace(path)) path = DefaultSecretPath;
        if (!File.Exists(path)) return null;
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("The local SQL secret requires Windows. Use REPARATIO_SQL_CONNECTION on other platforms.");
        byte[] plaintext;
        try
        {
            plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("The SQL secret cannot be decrypted by this Windows user. Recreate the local secret.");
        }
        try
        {
            var result = Encoding.UTF8.GetString(plaintext);
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public static string ReadRequired() => ReadOptional()
        ?? throw new InvalidOperationException("Configure the local SQL secret or REPARATIO_SQL_CONNECTION.");
}
