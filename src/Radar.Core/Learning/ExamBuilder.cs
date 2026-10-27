using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Radar.Core.Learning;

public enum ExamDifficulty
{
    Basic,
    Mixed,
    Advanced,
    /// <summary>Repetition of previously failed questions.</summary>
    Review,
}

/// <summary>Selects exam questions and summarizes results. Pure logic, no storage.</summary>
public static class ExamBuilder
{
    public static readonly IReadOnlyList<int> QuestionCounts = [5, 10, 20];

    /// <summary>
    /// Picks up to <paramref name="count"/> random questions of a topic (or of every topic when null) for the difficulty.
    /// Mixed alternates basic and advanced questions while both are available. Returns fewer when not enough exist.
    /// </summary>
    public static IReadOnlyList<StudyQuestion> Build(
        IReadOnlyList<StudyQuestion> questions, long? topicId, ExamDifficulty difficulty, int count, Random random)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var candidates = questions.Where(q => topicId is not { } id || q.TopicId == id).ToList();
        return difficulty switch
        {
            ExamDifficulty.Basic => Shuffle(candidates.Where(q => q.Difficulty == QuestionDifficulty.Basic), random).Take(count).ToList(),
            ExamDifficulty.Advanced => Shuffle(candidates.Where(q => q.Difficulty == QuestionDifficulty.Advanced), random).Take(count).ToList(),
            _ => Interleave(
                    Shuffle(candidates.Where(q => q.Difficulty == QuestionDifficulty.Basic), random),
                    Shuffle(candidates.Where(q => q.Difficulty == QuestionDifficulty.Advanced), random))
                .Take(count)
                .ToList(),
        };
    }

    /// <summary>Topics with at least one wrong answer, most wrong answers first.</summary>
    public static IReadOnlyList<long> TopicsToReview(IEnumerable<(StudyQuestion Question, bool IsCorrect)> answers) =>
        answers
            .Where(a => !a.IsCorrect)
            .GroupBy(a => a.Question.TopicId)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => g.Key)
            .ToList();

    /// <summary>
    /// Stable identity of a question's wording: case, accents, punctuation and spacing are ignored,
    /// so the same question generated twice is detected as a duplicate.
    /// </summary>
    public static string Fingerprint(string prompt)
    {
        var decomposed = prompt.Normalize(NormalizationForm.FormD);
        var text = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (char.IsLetterOrDigit(c))
            {
                text.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsWhiteSpace(c) && text.Length > 0 && text[^1] != ' ')
            {
                text.Append(' ');
            }
        }

        var normalized = text.ToString().Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLower(CultureInfo.InvariantCulture);
    }

    private static List<StudyQuestion> Shuffle(IEnumerable<StudyQuestion> questions, Random random)
    {
        var list = questions.ToList();
        random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
        return list;
    }

    private static IEnumerable<StudyQuestion> Interleave(List<StudyQuestion> first, List<StudyQuestion> second)
    {
        for (var i = 0; i < Math.Max(first.Count, second.Count); i++)
        {
            if (i < first.Count)
            {
                yield return first[i];
            }

            if (i < second.Count)
            {
                yield return second[i];
            }
        }
    }
}
