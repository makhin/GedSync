namespace GedcomGeniSync.Services.NameFix.Handlers;

/// <summary>
/// Handler for fixing capitalization issues.
/// Handles:
/// - ALL CAPS names → Proper Case
/// - all lowercase names → Proper Case
/// - Special patterns: McDonald, MacArthur, O'Brien, van der Berg
/// </summary>
public class CapitalizationHandler : NameFixHandlerBase
{
    public override string Name => "Capitalization";
    public override int Order => 95;  // Near end, after other processing

    public override void Handle(NameFixContext context)
    {
        // Process primary fields
        ProcessPrimaryFields(context);

        // Process all locales
        foreach (var locale in context.Names.Keys.ToList())
        {
            ProcessLocale(context, locale);
        }
    }

    private void ProcessPrimaryFields(NameFixContext context)
    {
        // FirstName
        if (!string.IsNullOrWhiteSpace(context.FirstName))
        {
            var fixed_ = FixCapitalization(context.FirstName);
            if (fixed_ != context.FirstName)
            {
                context.Changes.Add(new NameChange
                {
                    Field = "FirstName",
                    OldValue = context.FirstName,
                    NewValue = fixed_,
                    Reason = "Fixed capitalization",
                    Handler = Name
                });
                context.FirstName = fixed_;
            }
        }

        // LastName
        if (!string.IsNullOrWhiteSpace(context.LastName))
        {
            var fixed_ = FixCapitalization(context.LastName, isSurname: true);
            if (fixed_ != context.LastName)
            {
                context.Changes.Add(new NameChange
                {
                    Field = "LastName",
                    OldValue = context.LastName,
                    NewValue = fixed_,
                    Reason = "Fixed capitalization",
                    Handler = Name
                });
                context.LastName = fixed_;
            }
        }

        // MiddleName
        if (!string.IsNullOrWhiteSpace(context.MiddleName))
        {
            var fixed_ = FixCapitalization(context.MiddleName);
            if (fixed_ != context.MiddleName)
            {
                context.Changes.Add(new NameChange
                {
                    Field = "MiddleName",
                    OldValue = context.MiddleName,
                    NewValue = fixed_,
                    Reason = "Fixed capitalization",
                    Handler = Name
                });
                context.MiddleName = fixed_;
            }
        }

        // MaidenName
        if (!string.IsNullOrWhiteSpace(context.MaidenName))
        {
            var fixed_ = FixCapitalization(context.MaidenName, isSurname: true);
            if (fixed_ != context.MaidenName)
            {
                context.Changes.Add(new NameChange
                {
                    Field = "MaidenName",
                    OldValue = context.MaidenName,
                    NewValue = fixed_,
                    Reason = "Fixed capitalization",
                    Handler = Name
                });
                context.MaidenName = fixed_;
            }
        }
    }

    private void ProcessLocale(NameFixContext context, string locale)
    {
        var fields = context.GetLocaleFields(locale);
        if (fields == null) return;

        foreach (var field in NameFields.All.ToList())
        {
            if (!fields.TryGetValue(field, out var value)) continue;
            if (string.IsNullOrWhiteSpace(value)) continue;

            var isSurname = field == NameFields.LastName || field == NameFields.MaidenName;
            var fixed_ = FixCapitalization(value, isSurname);

            if (fixed_ != value)
            {
                SetName(context, locale, field, fixed_, "Fixed capitalization");
            }
        }
    }

    private string FixCapitalization(string text, bool isSurname = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Check if needs fixing (all caps or all lower)
        var hasUpper = text.Any(char.IsUpper);
        var hasLower = text.Any(char.IsLower);

        // If mixed case and not all caps, likely already correct
        if (hasUpper && hasLower && !IsAllCaps(text))
        {
            // Still apply special patterns for surnames
            if (isSurname)
            {
                return ApplySpecialSurnamePatterns(text);
            }
            return text;
        }

        // Convert to proper case
        var result = ToProperCase(text);

        // Apply special patterns for surnames
        if (isSurname)
        {
            result = ApplySpecialSurnamePatterns(result);
        }

        return result;
    }

    private bool IsAllCaps(string text)
    {
        var letters = text.Where(char.IsLetter).ToList();
        return letters.Count > 0 && letters.All(char.IsUpper);
    }

    private string ToProperCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();

        foreach (var word in words)
        {
            result.Add(CapitalizeWord(word));
        }

        return string.Join(" ", result);
    }

    private string CapitalizeWord(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;

        // Handle hyphenated names (Anna-Maria)
        if (word.Contains('-'))
        {
            var parts = word.Split('-');
            return string.Join("-", parts.Select(CapitalizeWord));
        }

        // Handle apostrophe (O'Brien, but not at start)
        var apostropheIndex = word.IndexOf('\'');
        if (apostropheIndex > 0 && apostropheIndex < word.Length - 1)
        {
            var before = word.Substring(0, apostropheIndex + 1);
            var after = word.Substring(apostropheIndex + 1);
            return char.ToUpper(before[0]) + before.Substring(1).ToLower() +
                   char.ToUpper(after[0]) + after.Substring(1).ToLower();
        }

        return char.ToUpper(word[0]) + word.Substring(1).ToLower();
    }

    private string ApplySpecialSurnamePatterns(string surname)
    {
        var result = surname;

        // McDonald, MacArthur patterns
        result = ApplyMcMacPattern(result);

        // O'Brien pattern
        result = ApplyOApostrophePattern(result);

        // Particles that should stay lowercase (if not at start)
        result = ApplyParticlePatterns(result);

        return result;
    }

    private string ApplyMcMacPattern(string surname)
    {
        // McDonald → McDonald (Mc + capital)
        if (surname.StartsWith("Mc", StringComparison.OrdinalIgnoreCase) && surname.Length > 2)
        {
            if (char.IsLetter(surname[2]) && char.IsLower(surname[2]))
            {
                return "Mc" + char.ToUpper(surname[2]) + surname.Substring(3);
            }
        }

        // MacArthur → MacArthur (Mac + capital)
        // But NOT "Mach" or "Machine" etc. - need at least 6 chars
        if (surname.StartsWith("Mac", StringComparison.OrdinalIgnoreCase) && surname.Length > 5)
        {
            // Check if 4th char is uppercase or should be (common Mac surnames)
            var commonMacSurnames = new[] { "donald", "arthur", "donald", "kenzie", "lean", "millan", "gregor", "intosh", "leod", "pherson" };
            var rest = surname.Substring(3).ToLowerInvariant();

            if (commonMacSurnames.Any(s => rest.StartsWith(s)))
            {
                return "Mac" + char.ToUpper(surname[3]) + surname.Substring(4);
            }
        }

        return surname;
    }

    private string ApplyOApostrophePattern(string surname)
    {
        // O'Brien, O'Connor, O'Neil
        if (surname.Length > 2 &&
            (surname[0] == 'O' || surname[0] == 'o') &&
            surname[1] == '\'')
        {
            if (surname.Length > 2 && char.IsLetter(surname[2]))
            {
                return "O'" + char.ToUpper(surname[2]) + surname.Substring(3).ToLower();
            }
        }

        return surname;
    }

    private string ApplyParticlePatterns(string surname)
    {
        // Common particles that should typically be lowercase when not at start
        // "van der Berg" → "van der Berg"
        // But "Van" at start can stay capitalized in some cultures

        var particlesToCheck = new[]
        {
            " Von ", " Van ", " De ", " Der ", " Den ", " La ", " Le ",
            " Del ", " Della ", " Dos ", " Das ", " Du "
        };

        foreach (var particle in particlesToCheck)
        {
            var lowerParticle = particle.ToLowerInvariant();
            if (surname.Contains(particle, StringComparison.OrdinalIgnoreCase))
            {
                // Find and replace preserving position
                var index = surname.IndexOf(particle, StringComparison.OrdinalIgnoreCase);
                if (index > 0) // Only lowercase if not at start
                {
                    surname = surname.Substring(0, index) + lowerParticle + surname.Substring(index + particle.Length);
                }
            }
        }

        return surname;
    }
}
