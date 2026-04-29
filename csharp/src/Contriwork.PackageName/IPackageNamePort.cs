namespace Contriwork.PackageName;

/// <summary>
/// Port definition — see CONTRACT.md for the language-agnostic specification.
/// Method names on this interface mirror the Python (<c>snake_case</c>) and
/// TypeScript (<c>camelCase</c>) ports; any signature change MUST land in
/// CONTRACT.md first and in all three languages in the same PR.
/// </summary>
public interface IPackageNamePort
{
    /// <summary>
    /// TODO: replace with a real contract method.
    /// </summary>
    /// <param name="input">Non-empty UTF-8 string, length &lt;= 4096.</param>
    /// <param name="cancellationToken">Cancellation for the caller.</param>
    /// <returns>
    /// A non-empty string derived deterministically from <paramref name="input"/>.
    /// </returns>
    /// <exception cref="System.ArgumentException">
    /// When <paramref name="input"/> fails validation (error code
    /// <c>INVALID_INPUT</c> per CONTRACT.md).
    /// </exception>
    Task<string> ExampleAsync(string input, CancellationToken cancellationToken = default);
}
