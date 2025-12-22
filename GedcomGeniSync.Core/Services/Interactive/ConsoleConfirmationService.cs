using System.Text;
using GedcomGeniSync.Core.Models.Interactive;
using GedcomGeniSync.Models;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Core.Services.Interactive;

/// <summary>
/// Console-based implementation of interactive confirmation
/// </summary>
public class ConsoleConfirmationService : IInteractiveConfirmation
{
    private readonly ILogger<ConsoleConfirmationService> _logger;

    // ANSI color codes
    private const string ANSI_RESET = "\x1b[0m";
    private const string ANSI_BOLD = "\x1b[1m";
    private const string ANSI_GREEN = "\x1b[32m";
    private const string ANSI_YELLOW = "\x1b[33m";
    private const string ANSI_RED = "\x1b[31m";
    private const string ANSI_CYAN = "\x1b[36m";
    private const string ANSI_GRAY = "\x1b[90m";

    public ConsoleConfirmationService(ILogger<ConsoleConfirmationService> logger)
    {
        _logger = logger;
    }

    public InteractiveConfirmationResult AskUser(InteractiveConfirmationRequest request)
    {
        try
        {
            // Temporarily suppress debug logs to avoid interference with interactive prompt
            // Debug logs go to Console.Out which can interfere with Console.ReadLine()
            _logger.LogInformation("=== INTERACTIVE MODE: Requesting user confirmation for {SourceId} ===",
                request.SourcePerson.Id);

            // CRITICAL: Force flush Console.Out to ensure all buffered logs are written
            // BEFORE we start the interactive prompt. Otherwise buffered debug logs will
            // appear mixed with the prompt and confuse ReadLine().
            Console.Out.Flush();
            System.Threading.Thread.Sleep(100); // Give logger time to flush

            // Use Console.Error instead of Console.Out because SimpleConsole logger captures Console.Out
            Console.Error.WriteLine();
            PrintHeader(request);
            PrintSourcePerson(request.SourcePerson, request.FoundVia, request.Level);
            PrintCandidates(request.Candidates, request.MaxCandidates);
            PrintFooter(request.Candidates.Count);
            Console.Error.Flush();

            var decision = GetUserInput(request.Candidates.Count);

            _logger.LogInformation("=== INTERACTIVE MODE: User decision: {Decision} for {SourceId} ===",
                decision.Decision, request.SourcePerson.Id);
            return decision;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AskUser");
            throw;
        }
    }

    private void PrintHeader(InteractiveConfirmationRequest request)
    {
        var bestScore = request.Candidates.Count > 0 ? request.Candidates[0].Score : 0;
        var scoreColor = GetScoreColor(bestScore);

        Console.Error.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.Error.WriteLine($"  {ANSI_BOLD}ТРЕБУЕТСЯ ПОДТВЕРЖДЕНИЕ{ANSI_RESET} (Score: {scoreColor}{bestScore}/100{ANSI_RESET})");
        Console.Error.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.Error.WriteLine();
    }

    private void PrintSourcePerson(PersonRecord person, string foundVia, int level)
    {
        Console.Error.WriteLine($"  {ANSI_BOLD}Персона в MyHeritage:{ANSI_RESET}");
        Console.Error.WriteLine($"    ID: {ANSI_GRAY}{person.Id}{ANSI_RESET}");
        Console.Error.WriteLine($"    Имя: {ANSI_BOLD}{FormatPersonName(person)}{ANSI_RESET}");

        if (person.BirthDate != null || person.BirthPlace != null)
        {
            var birthInfo = new StringBuilder("    Рождение: ");
            if (person.BirthDate != null)
            {
                birthInfo.Append(FormatDate(person.BirthDate));
            }
            if (person.BirthPlace != null)
            {
                if (person.BirthDate != null) birthInfo.Append(", ");
                birthInfo.Append(person.BirthPlace);
            }
            Console.Error.WriteLine(birthInfo.ToString());
        }

        if (person.DeathDate != null || person.DeathPlace != null)
        {
            var deathInfo = new StringBuilder("    Смерть: ");
            if (person.DeathDate != null)
            {
                deathInfo.Append(FormatDate(person.DeathDate));
            }
            if (person.DeathPlace != null)
            {
                if (person.DeathDate != null) deathInfo.Append(", ");
                deathInfo.Append(person.DeathPlace);
            }
            Console.Error.WriteLine(deathInfo.ToString());
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine($"  {ANSI_CYAN}Найдено через:{ANSI_RESET} {foundVia} (Level {level})");
        Console.Error.WriteLine();
        Console.Error.WriteLine("───────────────────────────────────────────────────────────────");
        Console.Error.WriteLine($"  {ANSI_BOLD}КАНДИДАТЫ В GENI:{ANSI_RESET}");
        Console.Error.WriteLine("───────────────────────────────────────────────────────────────");
        Console.Error.WriteLine();
    }

    private void PrintCandidates(List<CandidateMatch> candidates, int maxCandidates)
    {
        var candidatesToShow = candidates.Take(maxCandidates).ToList();

        for (int i = 0; i < candidatesToShow.Count; i++)
        {
            var candidate = candidatesToShow[i];
            PrintCandidate(i + 1, candidate);
            if (i < candidatesToShow.Count - 1)
            {
                Console.Error.WriteLine();
            }
        }

        if (candidates.Count > maxCandidates)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"  {ANSI_GRAY}... и ещё {candidates.Count - maxCandidates} кандидат(ов){ANSI_RESET}");
        }
    }

    private void PrintCandidate(int number, CandidateMatch candidate)
    {
        var scoreColor = GetScoreColor(candidate.Score);
        Console.Error.WriteLine($"  {ANSI_BOLD}[{number}]{ANSI_RESET} {FormatPersonName(candidate.Person)} (Score: {scoreColor}{candidate.Score}{ANSI_RESET})");
        Console.Error.WriteLine($"      ID: {ANSI_GRAY}{candidate.Person.Id}{ANSI_RESET}");

        if (candidate.Person.BirthDate != null || candidate.Person.BirthPlace != null)
        {
            var birthInfo = new StringBuilder("      Рождение: ");
            if (candidate.Person.BirthDate != null)
            {
                birthInfo.Append(FormatDate(candidate.Person.BirthDate));
            }
            if (candidate.Person.BirthPlace != null)
            {
                if (candidate.Person.BirthDate != null) birthInfo.Append(", ");
                birthInfo.Append(candidate.Person.BirthPlace);
            }
            Console.Error.WriteLine(birthInfo.ToString());
        }

        PrintScoreBreakdown(candidate.Breakdown);
    }

    private void PrintScoreBreakdown(ScoreBreakdown breakdown)
    {
        var parts = new List<string>();

        if (breakdown.FirstNameScore > 0)
        {
            var color = GetPercentColor(breakdown.FirstNameScore * 100);
            var details = !string.IsNullOrEmpty(breakdown.FirstNameDetails) ? $" ({breakdown.FirstNameDetails})" : "";
            parts.Add($"Имя {color}{breakdown.FirstNameScore:P0}{ANSI_RESET}{details}");
        }

        if (breakdown.LastNameScore > 0)
        {
            var color = GetPercentColor(breakdown.LastNameScore * 100);
            var details = !string.IsNullOrEmpty(breakdown.LastNameDetails) ? $" ({breakdown.LastNameDetails})" : "";
            parts.Add($"Фамилия {color}{breakdown.LastNameScore:P0}{ANSI_RESET}{details}");
        }

        if (breakdown.BirthDateScore > 0)
        {
            var color = GetPercentColor(breakdown.BirthDateScore * 100);
            var details = !string.IsNullOrEmpty(breakdown.BirthDateDetails) ? $" ({breakdown.BirthDateDetails})" : "";
            parts.Add($"Дата {color}{breakdown.BirthDateScore:P0}{ANSI_RESET}{details}");
        }

        if (breakdown.BirthPlaceScore > 0)
        {
            var color = GetPercentColor(breakdown.BirthPlaceScore * 100);
            parts.Add($"Место {color}{breakdown.BirthPlaceScore:P0}{ANSI_RESET}");
        }

        if (breakdown.ParentsTotal > 0)
        {
            var color = breakdown.ParentsMatching == breakdown.ParentsTotal ? ANSI_GREEN :
                       breakdown.ParentsMatching > 0 ? ANSI_YELLOW : ANSI_RED;
            parts.Add($"Родители {color}{breakdown.ParentsMatching}/{breakdown.ParentsTotal}{ANSI_RESET}");
        }

        if (breakdown.ChildrenTotal > 0)
        {
            var color = breakdown.ChildrenMatching > 0 ? ANSI_GREEN : ANSI_GRAY;
            parts.Add($"Дети {color}{breakdown.ChildrenMatching}/{breakdown.ChildrenTotal}{ANSI_RESET}");
        }

        if (breakdown.SiblingsTotal > 0)
        {
            var color = breakdown.SiblingsMatching > 0 ? ANSI_GREEN : ANSI_GRAY;
            parts.Add($"Сибсы {color}{breakdown.SiblingsMatching}/{breakdown.SiblingsTotal}{ANSI_RESET}");
        }

        if (breakdown.SpouseMatches)
        {
            parts.Add($"Супруг {ANSI_GREEN}✓{ANSI_RESET}");
        }

        if (parts.Count > 0)
        {
            Console.Error.WriteLine($"      Совпадения: {string.Join(", ", parts)}");
        }
    }

    private void PrintFooter(int candidatesCount)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("═══════════════════════════════════════════════════════════════");
        if (candidatesCount > 0)
        {
            Console.Error.Write($"  Выберите: {ANSI_CYAN}[1-{candidatesCount}]{ANSI_RESET} принять, ");
        }
        else
        {
            Console.Error.Write($"  Выберите: ");
        }
        Console.Error.Write($"{ANSI_YELLOW}[S]{ANSI_RESET} пропустить, ");
        Console.Error.WriteLine($"{ANSI_RED}[R]{ANSI_RESET} отклонить все");
        Console.Error.WriteLine("═══════════════════════════════════════════════════════════════");
    }

    private InteractiveConfirmationResult GetUserInput(int candidatesCount)
    {
        while (true)
        {
            Console.Error.Write($"Ваш выбор: ");
            Console.Error.Flush(); // Force flush before reading

            var input = Console.ReadLine()?.Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(input))
            {
                continue;
            }

            // Skip
            if (input == "S")
            {
                return new InteractiveConfirmationResult
                {
                    Decision = UserDecision.Skipped
                };
            }

            // Reject
            if (input == "R")
            {
                return new InteractiveConfirmationResult
                {
                    Decision = UserDecision.Rejected
                };
            }

            // Select candidate
            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= candidatesCount)
            {
                // Will be filled by caller with actual candidate
                return new InteractiveConfirmationResult
                {
                    Decision = UserDecision.Confirmed,
                    SelectedCandidate = null, // Caller will set this
                    SelectedScore = choice // Using as index temporarily
                };
            }

            Console.Error.WriteLine($"{ANSI_RED}Неверный выбор. Попробуйте снова.{ANSI_RESET}");
        }
    }

    private string GetScoreColor(int score)
    {
        if (score >= 80) return ANSI_GREEN;
        if (score >= 50) return ANSI_YELLOW;
        return ANSI_RED;
    }

    private string GetPercentColor(double percent)
    {
        if (percent >= 80) return ANSI_GREEN;
        if (percent >= 50) return ANSI_YELLOW;
        return ANSI_RED;
    }

    private string FormatPersonName(PersonRecord person)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(person.FirstName))
            parts.Add(person.FirstName);

        if (!string.IsNullOrEmpty(person.MiddleName))
            parts.Add(person.MiddleName);

        if (!string.IsNullOrEmpty(person.LastName))
            parts.Add(person.LastName);

        if (!string.IsNullOrEmpty(person.MaidenName))
            parts.Add($"({person.MaidenName})");

        return parts.Count > 0 ? string.Join(" ", parts) : "[Имя не указано]";
    }

    private string FormatDate(DateInfo date)
    {
        if (date.Year.HasValue)
        {
            if (date.Month.HasValue && date.Day.HasValue)
            {
                return $"{date.Day:D2} {GetMonthName(date.Month.Value)} {date.Year}";
            }
            else if (date.Month.HasValue)
            {
                return $"{GetMonthName(date.Month.Value)} {date.Year}";
            }
            else
            {
                return date.Year.Value.ToString();
            }
        }
        return "?";
    }

    private string GetMonthName(int month)
    {
        return month switch
        {
            1 => "янв",
            2 => "фев",
            3 => "мар",
            4 => "апр",
            5 => "май",
            6 => "июн",
            7 => "июл",
            8 => "авг",
            9 => "сен",
            10 => "окт",
            11 => "ноя",
            12 => "дек",
            _ => "?"
        };
    }
}
