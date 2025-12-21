using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace GedcomGeniSync.ApiClient.Models;

#region DTOs

[ExcludeFromCodeCoverage]
public class GeniProfile
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

    // Helper to extract numeric ID from full URL
    public string NumericId => Id.Replace("https://www.geni.com/api/profile-", "")
                                 .Replace("profile-", "");

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

[ExcludeFromCodeCoverage]
public class GeniProfileCreate
{
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? MaidenName { get; set; }
    public string? Suffix { get; set; }
    public string? Gender { get; set; } // "male" or "female"

    // Event objects (proper API format for add-child/add-parent)
    public GeniEventInput? Birth { get; set; }
    public GeniEventInput? Death { get; set; }
    public GeniEventInput? Burial { get; set; }

    public string? Occupation { get; set; }
    public string? Nicknames { get; set; }
}

[ExcludeFromCodeCoverage]
public class GeniProfileUpdate
{
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

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("birth")]
    public GeniEventInput? Birth { get; set; }

    [JsonPropertyName("death")]
    public GeniEventInput? Death { get; set; }

    [JsonPropertyName("baptism")]
    public GeniEventInput? Baptism { get; set; }

    [JsonPropertyName("burial")]
    public GeniEventInput? Burial { get; set; }

    [JsonPropertyName("occupation")]
    public string? Occupation { get; set; }

    [JsonPropertyName("about_me")]
    public string? AboutMe { get; set; }

    /// <summary>
    /// Multilingual names support
    /// Example: {"ru": {"first_name": "Иван", "last_name": "Иванов"}, "en": {"first_name": "Ivan", "last_name": "Ivanov"}}
    /// Supported fields per locale: first_name, middle_name, last_name, maiden_name, suffix, title
    /// </summary>
    [JsonPropertyName("names")]
    public Dictionary<string, Dictionary<string, string>>? Names { get; set; }

    [JsonPropertyName("nicknames")]
    public string? Nicknames { get; set; }  // comma-delimited

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("is_alive")]
    public bool? IsAlive { get; set; }

    [JsonPropertyName("cause_of_death")]
    public string? CauseOfDeath { get; set; }
}

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
}

[ExcludeFromCodeCoverage]
public class GeniUnion
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

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

    // Helper to extract numeric ID from full URL
    public string NumericId => Id?.Replace("https://www.geni.com/api/union-", "")
                                 .Replace("union-", "") ?? string.Empty;

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

[ExcludeFromCodeCoverage]
public class GeniSearchResult
{
    [JsonPropertyName("results")]
    public List<GeniProfile>? Results { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }
}

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

[ExcludeFromCodeCoverage]
public class GeniBatchProfileResult
{
    [JsonPropertyName("results")]
    public List<GeniProfile>? Results { get; set; }
}

[ExcludeFromCodeCoverage]
public class GeniBatchUnionResult
{
    [JsonPropertyName("results")]
    public List<GeniUnion>? Results { get; set; }
}

[ExcludeFromCodeCoverage]
public class GeniAddResult
{
    [JsonPropertyName("profile")]
    public GeniProfile? Profile { get; set; }

    [JsonPropertyName("union")]
    public GeniUnion? Union { get; set; }
}

public class GeniPhoto
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

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    public string NumericId => Id
        .Replace("https://www.geni.com/api/photo-", "")
        .Replace("photo-", "");
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

public class GeniPhotoListResult
{
    [JsonPropertyName("results")]
    public List<GeniPhoto>? Results { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }
}

public class GeniPhotoTagsResult
{
    [JsonPropertyName("results")]
    public List<GeniPhotoTag>? Results { get; set; }
}

#endregion
