using GedcomGeniSync.Services.Interfaces;
using GedcomGeniSync.Services.NameFix;
using GedcomGeniSync.Services.NameFix.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli.Services;

/// <summary>
/// Factory for creating NameFixPipeline with all handlers registered.
/// Used by add and add-branch commands to apply name fixes before profile creation.
/// </summary>
public static class NameFixPipelineFactory
{
    /// <summary>
    /// Register all name fix handlers and pipeline in the service collection.
    /// </summary>
    public static void RegisterNameFixServices(IServiceCollection services, HashSet<string>? enabledHandlers = null)
    {
        // Register all handlers as INameFixHandler (ordered by execution priority)
        var handlerTypes = new (Type Type, string Name)[]
        {
            // Cleanup & extraction phase (Order: 5-15)
            (typeof(SpecialCharsCleanupHandler), "SpecialCharsCleanup"),   // Order: 5
            (typeof(TitleExtractHandler), "TitleExtract"),                 // Order: 8
            (typeof(ScriptSplitHandler), "ScriptSplit"),                   // Order: 10
            (typeof(SuffixExtractHandler), "SuffixExtract"),               // Order: 11
            (typeof(MaidenNameExtractHandler), "MaidenNameExtract"),       // Order: 12
            (typeof(NicknameExtractHandler), "NicknameExtract"),           // Order: 13
            (typeof(MarriedSurnameHandler), "MarriedSurname"),             // Order: 14
            (typeof(PatronymicHandler), "Patronymic"),                     // Order: 15

            // Script/Language detection phase (Order: 20-28)
            (typeof(CyrillicToRuHandler), "CyrillicToRu"),                 // Order: 20
            (typeof(UkrainianHandler), "Ukrainian"),                       // Order: 24
            (typeof(LithuanianHandler), "Lithuanian"),                     // Order: 25
            (typeof(EstonianHandler), "Estonian"),                         // Order: 26
            (typeof(LatinLanguageHandler), "LatinLanguage"),               // Order: 27
            (typeof(HebrewHandler), "Hebrew"),                             // Order: 28

            // Transliteration & normalization phase (Order: 30-42)
            (typeof(TranslitHandler), "Translit"),                         // Order: 30
            (typeof(EnsureEnglishHandler), "EnsureEnglish"),               // Order: 35 - MUST have English
            (typeof(FeminineSurnameHandler), "FeminineSurname"),           // Order: 40
            (typeof(SurnameParticleHandler), "SurnameParticle"),           // Order: 42

            // Detection & validation phase (Order: 50+)
            (typeof(TypoDetectionHandler), "TypoDetection"),               // Order: 50
        };

        foreach (var (type, name) in handlerTypes)
        {
            // Skip if handler is not in enabled list (when list is specified)
            if (enabledHandlers != null && !enabledHandlers.Contains(name))
                continue;

            if (type == typeof(TypoDetectionHandler))
            {
                services.AddSingleton<INameFixHandler>(sp =>
                {
                    var variantsService = sp.GetService<INameVariantsService>();
                    return new TypoDetectionHandler(variantsService);
                });
            }
            else
            {
                services.AddSingleton(typeof(INameFixHandler), type);
            }
        }

        // Register pipeline
        services.AddSingleton<INameFixPipeline, NameFixPipeline>();
    }

    /// <summary>
    /// Create a pipeline instance directly (for simple cases without DI)
    /// </summary>
    public static INameFixPipeline CreatePipeline(ILoggerFactory loggerFactory)
    {
        var handlers = new INameFixHandler[]
        {
            new SpecialCharsCleanupHandler(),
            new TitleExtractHandler(),
            new ScriptSplitHandler(),
            new SuffixExtractHandler(),
            new MaidenNameExtractHandler(),
            new NicknameExtractHandler(),
            new MarriedSurnameHandler(),
            new PatronymicHandler(),
            new CyrillicToRuHandler(),
            new UkrainianHandler(),
            new LithuanianHandler(),
            new EstonianHandler(),
            new LatinLanguageHandler(),
            new HebrewHandler(),
            new TranslitHandler(),
            new EnsureEnglishHandler(),
            new FeminineSurnameHandler(),
            new SurnameParticleHandler(),
            new TypoDetectionHandler(null)
        };

        return new NameFixPipeline(handlers, loggerFactory.CreateLogger<NameFixPipeline>());
    }
}
