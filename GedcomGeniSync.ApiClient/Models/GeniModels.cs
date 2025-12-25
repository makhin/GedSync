using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GedcomGeniSync.ApiClient.Models;

#region Base Types

/// <summary>
/// Interface for Geni entities with an ID that can extract numeric part.
/// </summary>
public interface IGeniEntity
{
    string Id { get; }
}

/// <summary>
/// Extension methods for Geni entities.
/// </summary>
public static class GeniEntityExtensions
{
    /// <summary>
    /// Extracts the numeric ID from a Geni entity ID, removing URL and type prefixes.
    /// </summary>
    public static string GetNumericId(this IGeniEntity entity)
    {
        if (string.IsNullOrEmpty(entity.Id))
            return string.Empty;

        return entity.Id
            .Replace("https://www.geni.com/api/", "")
            .Replace("profile-", "")
            .Replace("union-", "")
            .Replace("photo-", "");
    }
}

/// <summary>
/// Base class for API results containing a list of items.
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniListResult<T>
{
    [JsonPropertyName("results")]
    public List<T>? Results { get; set; }
}

/// <summary>
/// Base class for paginated API results.
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniPaginatedResult<T> : GeniListResult<T>
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }
}

#endregion

#region DTOs

[ExcludeFromCodeCoverage]
public class GeniProfile : IGeniEntity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("middle_name")]
    public string? MiddleName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("maiden_name")]
    public string? MaidenName { get; set; }

    [JsonPropertyName("suffix")]
    public string? Suffix { get; set; }

    [JsonPropertyName("names")]
    public Dictionary<string, Dictionary<string, string>>? Names { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    // Birth event object (from batch API)
    [JsonPropertyName("birth")]
    public GeniEvent? Birth { get; set; }

    // Legacy fields (from single profile API, may be null)
    [JsonPropertyName("birth_date")]
    public string? BirthDateString { get; set; }

    [JsonPropertyName("birth_location")]
    public string? BirthLocationString { get; set; }

    // Death event object (from batch API)
    [JsonPropertyName("death")]
    public GeniEvent? Death { get; set; }

    // Legacy fields (from single profile API, may be null)
    [JsonPropertyName("death_date")]
    public string? DeathDateString { get; set; }

    [JsonPropertyName("death_location")]
    public string? DeathLocationString { get; set; }

    [JsonPropertyName("is_alive")]
    public bool? IsAlive { get; set; }

    [JsonPropertyName("big_tree")]
    public bool? BigTree { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("profile_url")]
    public string? ProfileUrl { get; set; }

    [JsonPropertyName("public")]
    public bool? IsPublic { get; set; }

    [JsonPropertyName("occupation")]
    public string? Occupation { get; set; }

    [JsonPropertyName("created_by")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("claimed")]
    public bool? Claimed { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("unions")]
    public List<string>? Unions { get; set; }

    [JsonPropertyName("relationship")]
    public string? Relationship { get; set; }

    [JsonPropertyName("marriage_orders")]
    public Dictionary<string, int>? MarriageOrders { get; set; }

    [JsonPropertyName("birth_order")]
    public int? BirthOrder { get; set; }

    [JsonPropertyName("living")]
    public bool? Living { get; set; }

    [JsonPropertyName("location")]
    public GeniLocation? Location { get; set; }

    [JsonPropertyName("current_residence")]
    public GeniLocation? CurrentResidence { get; set; }

    [JsonPropertyName("creator")]
    public string? Creator { get; set; }

    [JsonPropertyName("account_type")]
    public string? AccountType { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("deleted")]
    public bool? Deleted { get; set; }

    [JsonPropertyName("is_curator")]
    public bool? IsCurator { get; set; }

    // Helper to extract numeric ID from full URL (uses IGeniEntity extension)
    public string NumericId => this.GetNumericId();

    /// <summary>
    /// Get birth date as formatted string (from either Birth event or legacy BirthDateString)
    /// </summary>
    public string? BirthDate => Birth?.Date?.FormattedDate ?? BirthDateString;

    /// <summary>
    /// Get birth place as string (from either Birth event or legacy BirthLocationString)
    /// </summary>
    public string? BirthPlace => Birth?.Location?.FormattedLocation ?? Birth?.Location?.PlaceName ?? BirthLocationString;

    /// <summary>
    /// Get death date as formatted string (from either Death event or legacy DeathDateString)
    /// </summary>
    public string? DeathDate => Death?.Date?.FormattedDate ?? DeathDateString;

    /// <summary>
    /// Get death place as string (from either Death event or legacy DeathLocationString)
    /// </summary>
    public string? DeathPlace => Death?.Location?.FormattedLocation ?? Death?.Location?.PlaceName ?? DeathLocationString;
}

/// <summary>
/// Base class for profile data used in Geni API operations.
/// API documentation: https://www.geni.com/platform/developer/help/api?path=profile%2Fadd
/// Note: Same fields are supported for both create and update operations.
/// </summary>
[ExcludeFromCodeCoverage]
public abstract class GeniProfileDataBase
{
    // Name fields
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? MaidenName { get; set; }
    public string? Suffix { get; set; }
    public string? Title { get; set; }  // Dr., Mr., Mrs., etc.
    public string? DisplayName { get; set; }
    public string? Gender { get; set; } // "male" or "female"

    // Event objects
    public GeniEventInput? Birth { get; set; }
    public GeniEventInput? Death { get; set; }
    public GeniEventInput? Baptism { get; set; }
    public GeniEventInput? Burial { get; set; }

    // Additional info
    public string? Occupation { get; set; }
    public string? Nicknames { get; set; }  // comma-delimited list
    public string? AboutMe { get; set; }
    public string? CauseOfDeath { get; set; }

    /// <summary>
    /// Multilingual names support.
    /// Example: {"ru": {"first_name": "Иван"}, "en": {"first_name": "Ivan"}}
    /// Supported fields per locale: first_name, middle_name, last_name, maiden_name, suffix, title
    /// </summary>
    public Dictionary<string, Dictionary<string, string>>? Names { get; set; }

    /// <summary>
    /// Whether the person is alive. Should be set to true unless there's a death date.
    /// </summary>
    public bool? IsAlive { get; set; }
}

/// <summary>
/// Profile data for creating a new profile via add-child, add-parent, add-partner APIs.
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniProfileCreate : GeniProfileDataBase { }

/// <summary>
/// Profile data for updating an existing profile.
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniProfileUpdate : GeniProfileDataBase { }

/// <summary>
/// Input model for creating/updating events (birth, death, baptism, burial) in Geni API
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniEventInput
{
    [JsonPropertyName("date")]
    public GeniDateInput? Date { get; set; }

    [JsonPropertyName("location")]
    public GeniLocationInput? Location { get; set; }
}

/// <summary>
/// Input model for date values when creating/updating events
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniDateInput
{
    [JsonPropertyName("day")]
    public int? Day { get; set; }

    [JsonPropertyName("month")]
    public int? Month { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }
}

/// <summary>
/// Input model for location values when creating/updating events
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniLocationInput
{
    [JsonPropertyName("place_name")]
    public string? PlaceName { get; set; }
}

[ExcludeFromCodeCoverage]
public class GeniImmediateFamily
{
    [JsonPropertyName("focus")]
    public GeniProfile? Focus { get; set; }

    [JsonPropertyName("nodes")]
    public Dictionary<string, GeniNode>? Nodes { get; set; }
}

[ExcludeFromCodeCoverage]
public class GeniNode
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("middle_name")]
    public string? MiddleName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("maiden_name")]
    public string? MaidenName { get; set; }

    [JsonPropertyName("suffix")]
    public string? Suffix { get; set; }

    [JsonPropertyName("names")]
    public Dictionary<string, Dictionary<string, string>>? Names { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("birth_date")]
    public string? BirthDate { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }  // Sometimes full name is here

    // Relations
    [JsonPropertyName("edges")]
    public GeniEdges? Edges { get; set; }

    // Union data (only present for union nodes)
    [JsonIgnore]
    public GeniUnion? Union { get; set; }
}

[ExcludeFromCodeCoverage]
public class GeniEdges
{
    [JsonPropertyName("union")]
    public List<string>? Unions { get; set; }

    /// <summary>
    /// Captures additional edge data not mapped to properties.
    /// For union nodes, this contains profile IDs as keys with {rel: "partner"|"child"} as values.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>
    /// Extracts partner profile IDs from the edges data (for union nodes).
    /// </summary>
    public List<string> GetPartnerProfileIds()
    {
        var partners = new List<string>();
        if (AdditionalData == null) return partners;

        foreach (var (key, value) in AdditionalData)
        {
            if (!key.StartsWith("profile-")) continue;

            // Check if this edge has rel="partner"
            if (value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("rel", out var relProp) &&
                relProp.GetString() == "partner")
            {
                partners.Add(key);
            }
        }

        return partners;
    }
}

[ExcludeFromCodeCoverage]
public class GeniUnion : IGeniEntity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("partners")]
    public List<string>? Partners { get; set; }

    [JsonPropertyName("children")]
    public List<string>? Children { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    // Legacy fields (might be returned as objects instead of strings)
    [JsonPropertyName("marriage_date")]
    public GeniDate? MarriageDateObject { get; set; }

    [JsonPropertyName("marriage_location")]
    public GeniLocation? MarriageLocationObject { get; set; }

    [JsonPropertyName("divorce_date")]
    public GeniDate? DivorceDateObject { get; set; }

    [JsonPropertyName("divorce_location")]
    public GeniLocation? DivorceLocationObject { get; set; }

    // Event objects (preferred format)
    [JsonPropertyName("marriage")]
    public GeniEvent? Marriage { get; set; }

    [JsonPropertyName("divorce")]
    public GeniEvent? Divorce { get; set; }

    // Helper to extract numeric ID from full URL (uses IGeniEntity extension)
    public string NumericId => this.GetNumericId();

    /// <summary>
    /// Get marriage date as formatted string (from Marriage event or legacy MarriageDateObject)
    /// </summary>
    public string? MarriageDate => Marriage?.Date?.FormattedDate ?? MarriageDateObject?.FormattedDate;

    /// <summary>
    /// Get marriage place as string (from Marriage event or legacy MarriageLocationObject)
    /// </summary>
    public string? MarriagePlace => Marriage?.Location?.FormattedLocation
                                 ?? Marriage?.Location?.PlaceName
                                 ?? MarriageLocationObject?.FormattedLocation
                                 ?? MarriageLocationObject?.PlaceName;

    /// <summary>
    /// Get divorce date as formatted string (from Divorce event or legacy DivorceDateObject)
    /// </summary>
    public string? DivorceDate => Divorce?.Date?.FormattedDate ?? DivorceDateObject?.FormattedDate;

    /// <summary>
    /// Get divorce place as string (from Divorce event or legacy DivorceLocationObject)
    /// </summary>
    public string? DivorcePlace => Divorce?.Location?.FormattedLocation
                                ?? Divorce?.Location?.PlaceName
                                ?? DivorceLocationObject?.FormattedLocation
                                ?? DivorceLocationObject?.PlaceName;
}

/// <summary>
/// Search results containing profiles with pagination.
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniSearchResult : GeniPaginatedResult<GeniProfile> { }

/// <summary>
/// Represents a date in Geni API with day, month, year, and formatted date
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniDate
{
    [JsonPropertyName("day")]
    public int? Day { get; set; }

    [JsonPropertyName("month")]
    public int? Month { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("formatted_date")]
    public string? FormattedDate { get; set; }
}

/// <summary>
/// Represents a location in Geni API
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniLocation
{
    [JsonPropertyName("place_name")]
    public string? PlaceName { get; set; }

    [JsonPropertyName("formatted_location")]
    public string? FormattedLocation { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("county")]
    public string? County { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }
}

/// <summary>
/// Represents a birth or death event in Geni API
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniEvent
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("date")]
    public GeniDate? Date { get; set; }

    [JsonPropertyName("location")]
    public GeniLocation? Location { get; set; }
}

/// <summary>
/// Batch profile fetch results.
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniBatchProfileResult : GeniListResult<GeniProfile> { }

/// <summary>
/// Batch union fetch results.
/// </summary>
[ExcludeFromCodeCoverage]
public class GeniBatchUnionResult : GeniListResult<GeniUnion> { }

[ExcludeFromCodeCoverage]
public class GeniAddResult
{
    [JsonPropertyName("profile")]
    public GeniProfile? Profile { get; set; }

    [JsonPropertyName("union")]
    public GeniUnion? Union { get; set; }
}

public class GeniPhoto : IGeniEntity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("content_url")]
    public string? ContentUrl { get; set; }

    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// Date can be either a string or an object (e.g., {"year": 2000, "month": 1})
    /// Use JsonElement to handle both cases
    /// </summary>
    [JsonPropertyName("date")]
    public JsonElement? Date { get; set; }

    /// <summary>
    /// Location can be either a string or an object
    /// Use JsonElement to handle both cases
    /// </summary>
    [JsonPropertyName("location")]
    public JsonElement? Location { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    // Helper to extract numeric ID from full URL (uses IGeniEntity extension)
    public string NumericId => this.GetNumericId();
}

public class GeniPhotoUpdate
{
    public string? Title { get; set; }
    public string? Date { get; set; }
    public string? Location { get; set; }
}

public class GeniPhotoTag
{
    [JsonPropertyName("profile")]
    public string? ProfileId { get; set; }

    [JsonPropertyName("x")]
    public double? X { get; set; }

    [JsonPropertyName("y")]
    public double? Y { get; set; }

    [JsonPropertyName("width")]
    public double? Width { get; set; }

    [JsonPropertyName("height")]
    public double? Height { get; set; }
}

public class PhotoTagPosition
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>
/// Photo list results with pagination.
/// </summary>
public class GeniPhotoListResult : GeniPaginatedResult<GeniPhoto> { }

/// <summary>
/// Photo tags list results.
/// </summary>
public class GeniPhotoTagsResult : GeniListResult<GeniPhotoTag> { }

#endregion
