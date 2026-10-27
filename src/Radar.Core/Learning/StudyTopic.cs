namespace Radar.Core.Learning;

/// <summary>
/// A study topic. <see cref="ConfirmedFacts"/> holds only statements backed by <see cref="SourceUrl"/>;
/// the other sections are didactic explanations built on top of them.
/// </summary>
public sealed record StudyTopic(
    long Id,
    string Key,
    string Category,
    string Title,
    int SortOrder,
    string WhatItIs,
    string WhatItIsFor,
    string HowItWorks,
    string Example,
    string Differences,
    string ConfirmedFacts,
    string SourceUrl,
    string SourceReference);

public enum StudyStatus
{
    Pending,
    InProgress,
    Studied,
}

public enum QuestionKind
{
    MultipleChoice,
    Scenario,
}

public enum QuestionDifficulty
{
    Basic,
    Advanced,
}

/// <summary>A question of a topic. <see cref="Origin"/> says where it came from (the built-in catalog or a Gemini model).</summary>
public sealed record StudyQuestion(
    long Id,
    long TopicId,
    QuestionKind Kind,
    QuestionDifficulty Difficulty,
    string Prompt,
    IReadOnlyList<string> Options,
    int CorrectOption,
    string Explanation,
    string Origin)
{
    public bool IsCorrect(int selectedOption) => selectedOption == CorrectOption;
}
