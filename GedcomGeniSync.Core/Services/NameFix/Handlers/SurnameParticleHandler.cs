namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler for surname particles (von, van, de, la, etc.).
/// Ensures particles are properly capitalized and not split incorrectly.
/// </summary>
public class SurnameParticleHandler : NameFixHandlerBase
{
    public override string Name => "SurnameParticle";
    public override int Order => 42;  // After feminine surname handler

    // Common surname particles by language/origin
    private static readonly Dictionary<string, ParticleInfo> Particles = new(StringComparer.OrdinalIgnoreCase)
    {
        // German
        ["von"] = new("von", false),           // von Neumann
        ["von der"] = new("von der", false),   // von der Leyen
        ["vom"] = new("vom", false),
        ["zum"] = new("zum", false),
        ["zur"] = new("zur", false),

        // Dutch
        ["van"] = new("van", false),           // van Gogh
        ["van de"] = new("van de", false),
        ["van der"] = new("van der", false),
        ["van den"] = new("van den", false),
        ["van het"] = new("van het", false),
        ["de"] = new("de", false),
        ["den"] = new("den", false),
        ["het"] = new("het", false),
        ["ter"] = new("ter", false),
        ["ten"] = new("ten", false),

        // French
        ["de"] = new("de", false),             // de Gaulle
        ["du"] = new("du", false),
        ["de la"] = new("de la", false),
        ["des"] = new("des", false),
        ["le"] = new("Le", true),              // Le Pen (usually capitalized)
        ["la"] = new("La", true),
        ["l'"] = new("L'", true),

        // Spanish/Portuguese
        ["de"] = new("de", false),
        ["del"] = new("del", false),
        ["de los"] = new("de los", false),
        ["de las"] = new("de las", false),
        ["da"] = new("da", false),
        ["das"] = new("das", false),
        ["do"] = new("do", false),
        ["dos"] = new("dos", false),

        // Italian
        ["di"] = new("di", false),
        ["della"] = new("della", false),
        ["dello"] = new("dello", false),
        ["dei"] = new("dei", false),
        ["degli"] = new("degli", false),
        ["dalle"] = new("dalle", false),

        // Irish/Scottish
        ["o'"] = new("O'", true),              // O'Brien
        ["mc"] = new("Mc", true),              // McDonald
        ["mac"] = new("Mac", true),            // MacArthur

        // Arabic
        ["al"] = new("al-", false),            // al-Rashid
        ["el"] = new("El", true),              // El-Amin
        ["bin"] = new("bin", false),           // bin Laden
        ["ibn"] = new("ibn", false),

        // Jewish
        ["ben"] = new("ben", false),           // ben David
        ["bar"] = new("bar", false),           // bar Kokhba
        ["bat"] = new("bat", false),           // bat Miriam
    };

    public override void Handle(NameFixContext context)
    {
        // Process primary LastName
        ProcessLastName(context);

        // Process localized last names
        foreach (var locale in context.Names.Keys.ToList())
        {
            ProcessLocaleLastName(context, locale);
        }
    }

    private void ProcessLastName(NameFixContext context)
    {
        if (string.IsNullOrWhiteSpace(context.LastName)) return;

        var normalized = NormalizeSurnameWithParticle(context.LastName);
        if (normalized != context.LastName)
        {
            context.Changes.Add(new NameChange
            {
                Field = "LastName",
                OldValue = context.LastName,
                NewValue = normalized,
                Reason = "Normalized surname particle capitalization",
                Handler = Name
            });

            context.LastName = normalized;
        }
    }

    private void ProcessLocaleLastName(NameFixContext context, string locale)
    {
        var lastName = context.GetName(locale, NameFields.LastName);
        if (string.IsNullOrWhiteSpace(lastName)) return;

        var normalized = NormalizeSurnameWithParticle(lastName);
        if (normalized != lastName)
        {
            SetName(context, locale, NameFields.LastName, normalized,
                "Normalized surname particle capitalization");
        }
    }

    private string NormalizeSurnameWithParticle(string surname)
    {
        if (string.IsNullOrWhiteSpace(surname)) return surname;

        var result = surname;

        // Check for each particle
        foreach (var (particle, info) in Particles)
        {
            // Check if surname starts with this particle
            if (StartsWithParticle(surname, particle, out var actualParticle, out var restOfName))
            {
                // Normalize the particle
                var normalizedParticle = info.AlwaysCapitalized
                    ? info.NormalizedForm
                    : info.NormalizedForm.ToLowerInvariant();

                // Capitalize the main part of the surname
                var normalizedRest = CapitalizeFirstLetter(restOfName);

                // Don't add space after particles ending with apostrophe (O', L')
                var separator = normalizedParticle.EndsWith("'") ? "" : " ";
                result = normalizedParticle + separator + normalizedRest;
                break;
            }
        }

        // Handle special cases like McDonald, O'Brien
        result = NormalizeSpecialParticles(result);

        return result;
    }

    private bool StartsWithParticle(string surname, string particle, out string actualParticle, out string restOfName)
    {
        actualParticle = "";
        restOfName = surname;

        // Check for particle followed by space or apostrophe
        var particleWithSpace = particle + " ";
        var particleWithApostrophe = particle + "'";

        if (surname.StartsWith(particleWithSpace, StringComparison.OrdinalIgnoreCase))
        {
            actualParticle = surname.Substring(0, particle.Length);
            restOfName = surname.Substring(particleWithSpace.Length).TrimStart();
            return true;
        }

        if (particle.EndsWith("'") && surname.StartsWith(particle, StringComparison.OrdinalIgnoreCase))
        {
            actualParticle = surname.Substring(0, particle.Length);
            restOfName = surname.Substring(particle.Length);
            return true;
        }

        return false;
    }

    private string NormalizeSpecialParticles(string surname)
    {
        // McDonald, MacDonald - Mc/Mac followed by capital letter
        if (surname.StartsWith("mc", StringComparison.OrdinalIgnoreCase) && surname.Length > 2)
        {
            var rest = surname.Substring(3);
            return "Mc" + char.ToUpper(surname[2]) + rest.ToLowerInvariant();
        }

        if (surname.StartsWith("mac", StringComparison.OrdinalIgnoreCase) && surname.Length > 3)
        {
            // Check if it's really Mac + Name (not just "machin")
            if (char.IsUpper(surname[3]) || surname.Length > 5)
            {
                var rest = surname.Substring(4);
                return "Mac" + char.ToUpper(surname[3]) + rest.ToLowerInvariant();
            }
        }

        // O'Brien, O'Connor
        var apostropheIndex = surname.IndexOf('\'');
        if (apostropheIndex == 1 && (surname[0] == 'o' || surname[0] == 'O'))
        {
            if (surname.Length > 2)
            {
                return "O'" + char.ToUpper(surname[2]) + surname.Substring(3);
            }
        }

        return surname;
    }

    private static string CapitalizeFirstLetter(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Handle hyphenated names (Neumann-Schmidt)
        if (text.Contains('-'))
        {
            var parts = text.Split('-');
            return string.Join("-", parts.Select(CapitalizeFirstLetter));
        }

        // Convert to proper case: first letter uppercase, rest lowercase
        return char.ToUpper(text[0]) + text.Substring(1).ToLowerInvariant();
    }

    private record ParticleInfo(string NormalizedForm, bool AlwaysCapitalized);
}
