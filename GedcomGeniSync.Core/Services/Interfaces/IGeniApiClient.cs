namespace GedcomGeniSync.Services;

/// <summary>
/// Interface for Geni API Client
/// Provides combined access to profile and photo operations on Geni.com
/// </summary>
public interface IGeniApiClient : IGeniProfileClient, IGeniPhotoClient
{
}
