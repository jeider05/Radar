namespace Radar.Core.Learning;

/// <summary>A stored exam attempt with its answer totals. TopicId is null for a general exam.</summary>
public sealed record ExamAttempt(
    long Id,
    long? TopicId,
    ExamDifficulty Difficulty,
    int QuestionCount,
    int AnsweredCount,
    int CorrectCount,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc);

/// <summary>How often a question was answered and how often it was answered wrong, across all attempts.</summary>
public sealed record QuestionStats(long QuestionId, long TopicId, int Answered, int Wrong);
