using GedcomGeniSync.Core.Models.Interactive;

namespace GedcomGeniSync.Core.Services.Interactive;

/// <summary>
/// Service for interactive user confirmation of low-confidence matches
/// </summary>
public interface IInteractiveConfirmation
{
    /// <summary>
    /// Ask user to confirm or reject a match with low confidence
    /// </summary>
    /// <param name="request">Request with source person and candidates</param>
    /// <returns>User decision</returns>
    InteractiveConfirmationResult AskUser(InteractiveConfirmationRequest request);
}
