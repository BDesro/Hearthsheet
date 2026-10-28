namespace Hearthsheet.Cloud;

/// <summary>OS-protected secret storage. Values never touch config files or logs.</summary>
/// <remarks>Implementations report storage failures as <see cref="SecretStoreException"/> and nothing else.</remarks>
public interface ISecretStore
{
    string? Read(string key);
    void Write(string key, string secret);
    void Delete(string key);
}

/// <summary>The secret store could not be read or written; the platform's own error is the inner exception.</summary>
public sealed class SecretStoreException(string message, Exception? inner = null) : Exception(message, inner);
