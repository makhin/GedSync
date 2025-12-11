using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace GedcomGeniSync.Services;

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

    // Birth can be either a simple string or an Event object
    [JsonPropertyName("birth")]
    public object? Birth { get; set; }

    [JsonPropertyName("birth_date")]
    public string? BirthDate { get; set; }

    [JsonPropertyName("birth_location")]
    public string? BirthPlace { get; set; }

    // Death can be either a simple string or an Event object
    [JsonPropertyName("death")]
    public object? Death { get; set; }

    [JsonPropertyName("death_date")]
    public string? DeathDate { get; set; }

    [JsonPropertyName("death_location")]
    public string? DeathPlace { get; set; }

    [JsonPropertyName("is_alive")]
    public bool? IsAlive { get; set; }

    [JsonPropertyName("big_tree")]
    public bool? BigTree { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    // Helper to extract numeric ID from full URL
    public string NumericId => Id.Replace("https://www.geni.com/api/profile-", "")
                                 .Replace("profile-", "");
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
    public string? BirthDate { get; set; } // Format: "YYYY-MM-DD" or "YYYY"
    public string? BirthPlace { get; set; }
    public string? DeathDate { get; set; }
    public string? DeathPlace { get; set; }
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
    public object? Birth { get; set; }

    [JsonPropertyName("death")]
    public object? Death { get; set; }

    [JsonPropertyName("occupation")]
    public string? Occupation { get; set; }

    [JsonPropertyName("about_me")]
    public string? AboutMe { get; set; }
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

    [JsonPropertyName("partners")]
    public List<string>? Partners { get; set; }

    [JsonPropertyName("children")]
    public List<string>? Children { get; set; }
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

[ExcludeFromCodeCoverage]
public class GeniBatchProfileResult
{
    [JsonPropertyName("results")]
    public List<GeniProfile>? Results { get; set; }
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
