using GedcomGeniSync.ApiClient.Models;

namespace GedcomGeniSync.ApiClient.Services.Interfaces;

/// <summary>
/// Interface for authenticating against the Geni platform and persisting tokens.
/// </summary>
public interface IGeniAuthClient
{
    /// <summary>
    /// Loads an existing token from a file and returns it if present.
    /// </summary>
    /// <param name="tokenFile">Path to the token file.</param>
    /// <returns>The token if successfully loaded; otherwise, null.</returns>
    Task<GeniAuthToken?> LoadTokenAsync(string tokenFile);

    /// <summary>
    /// Performs an interactive login flow using Desktop OAuth (Implicit Grant) and returns the resulting token.
    /// Opens browser for user to authorize, then prompts user to paste the redirect URL.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the process.</param>
    /// <returns>The authenticated token; otherwise, null when authentication fails.</returns>
    Task<GeniAuthToken?> LoginInteractiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves a token to the specified file.
    /// </summary>
    /// <param name="token">Token to persist.</param>
    /// <param name="tokenFile">Destination file path.</param>
    Task SaveTokenAsync(GeniAuthToken token, string tokenFile);
}
