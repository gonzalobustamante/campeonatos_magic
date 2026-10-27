using System.Text.Json;
using Microsoft.AspNetCore.Identity;

namespace CampeonatoMagic.Services;

public record AdminCredential(string Username, string PasswordHash, string Stamp);
public class AdminAccount(string dataPath)
{
    private readonly string path = Path.Combine(dataPath, "admin.json");
    private readonly PasswordHasher<string> hasher = new();
    public AdminCredential? Read() => File.Exists(path) ? JsonSerializer.Deserialize<AdminCredential>(File.ReadAllText(path)) : null;
    public bool Verify(string username, string password)
    {
        var credential = Read();
        // Always perform the expensive hash verification, including for unknown users.
        var hash = credential?.PasswordHash ?? hasher.HashPassword("admin", "unconfigured-account");
        var valid = hasher.VerifyHashedPassword(username, hash, password) != PasswordVerificationResult.Failed;
        return credential is not null && string.Equals(username, credential.Username, StringComparison.Ordinal) && valid;
    }
    public void Set(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 60 || password.Length < 12)
            throw new ArgumentException("Usa un usuario de 1 a 60 caracteres y una contraseña de al menos 12 caracteres.");
        Directory.CreateDirectory(dataPath);
        var credential = new AdminCredential(username.Trim(), hasher.HashPassword(username.Trim(), password), Guid.NewGuid().ToString());
        File.WriteAllText(path, JsonSerializer.Serialize(credential));
    }
}
