using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Radar.Core.Learning;
using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Learning;

/// <summary>
/// Study topics, questions, topic progress and exam history. Content and progress live in separate tables,
/// so re-seeding the catalog never touches what the user has studied or answered.
/// </summary>
public sealed class LearningStore(RadarDatabase database)
{
    /// <summary>
    /// Inserts or refreshes the catalog topics (matched by key) and adds catalog questions not stored yet.
    /// Progress, attempts and answers are kept. Safe to call on every start.
    /// </summary>
    public void SeedCatalog(IReadOnlyList<CatalogTopic> catalog)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var (topic, questions) in catalog)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO StudyTopics (Key, Category, Title, SortOrder, WhatItIs, WhatItIsFor, HowItWorks, Example, Differences,
                                         ConfirmedFacts, SourceUrl, SourceReference)
                VALUES ($key, $category, $title, $sortOrder, $whatItIs, $whatItIsFor, $howItWorks, $example, $differences,
                        $confirmedFacts, $sourceUrl, $sourceReference)
                ON CONFLICT (Key) DO UPDATE SET
                    Category = excluded.Category, Title = excluded.Title, SortOrder = excluded.SortOrder,
                    WhatItIs = excluded.WhatItIs, WhatItIsFor = excluded.WhatItIsFor, HowItWorks = excluded.HowItWorks,
                    Example = excluded.Example, Differences = excluded.Differences, ConfirmedFacts = excluded.ConfirmedFacts,
                    SourceUrl = excluded.SourceUrl, SourceReference = excluded.SourceReference
                RETURNING Id;
                """;
            command.Parameters.AddWithValue("$key", topic.Key);
            command.Parameters.AddWithValue("$category", topic.Category);
            command.Parameters.AddWithValue("$title", topic.Title);
            command.Parameters.AddWithValue("$sortOrder", topic.SortOrder);
            command.Parameters.AddWithValue("$whatItIs", topic.WhatItIs);
            command.Parameters.AddWithValue("$whatItIsFor", topic.WhatItIsFor);
            command.Parameters.AddWithValue("$howItWorks", topic.HowItWorks);
            command.Parameters.AddWithValue("$example", topic.Example);
            command.Parameters.AddWithValue("$differences", topic.Differences);
            command.Parameters.AddWithValue("$confirmedFacts", topic.ConfirmedFacts);
            command.Parameters.AddWithValue("$sourceUrl", topic.SourceUrl);
            command.Parameters.AddWithValue("$sourceReference", topic.SourceReference);
            var topicId = (long)command.ExecuteScalar()!;

            foreach (var question in questions)
            {
                Insert(connection, transaction, question with { TopicId = topicId });
            }
        }

        transaction.Commit();
    }

    /// <summary>Returns the topics ordered for display.</summary>
    public IReadOnlyList<StudyTopic> GetTopics()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Key, Category, Title, SortOrder, WhatItIs, WhatItIsFor, HowItWorks, Example, Differences,
                   ConfirmedFacts, SourceUrl, SourceReference
            FROM StudyTopics
            ORDER BY SortOrder, Id;
            """;

        var topics = new List<StudyTopic>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            topics.Add(new StudyTopic(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
                reader.GetString(10), reader.GetString(11), reader.GetString(12)));
        }

        return topics;
    }

    /// <summary>Returns every stored question, oldest first.</summary>
    public IReadOnlyList<StudyQuestion> GetQuestions()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"{SelectQuestions} ORDER BY Id;";
        return ReadQuestions(command);
    }

    /// <summary>
    /// Stores a question unless one with the same wording already exists (see <see cref="ExamBuilder.Fingerprint"/>).
    /// Returns the stored question, or null for a duplicate.
    /// </summary>
    public StudyQuestion? TryAddQuestion(StudyQuestion question)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var id = Insert(connection, transaction, question);
        transaction.Commit();
        return id is { } newId ? question with { Id = newId } : null;
    }

    /// <summary>Study status per topic id. Topics never opened have no entry (they are pending).</summary>
    public IReadOnlyDictionary<long, StudyStatus> GetStatuses()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TopicId, Status FROM StudyTopicProgress;";

        var statuses = new Dictionary<long, StudyStatus>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            statuses[reader.GetInt64(0)] = Enum.Parse<StudyStatus>(reader.GetString(1));
        }

        return statuses;
    }

    public void SetStatus(long topicId, StudyStatus status)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StudyTopicProgress (TopicId, Status, UpdatedAtUtc)
            VALUES ($topicId, $status, $updatedAtUtc)
            ON CONFLICT (TopicId) DO UPDATE SET Status = excluded.Status, UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$topicId", topicId);
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$updatedAtUtc", Format(DateTime.UtcNow));
        command.ExecuteNonQuery();
    }

    /// <summary>Records the start of an exam and returns its id.</summary>
    public long StartAttempt(long? topicId, ExamDifficulty difficulty, int questionCount)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ExamAttempts (TopicId, Difficulty, QuestionCount, StartedAtUtc)
            VALUES ($topicId, $difficulty, $questionCount, $startedAtUtc)
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$topicId", (object?)topicId ?? DBNull.Value);
        command.Parameters.AddWithValue("$difficulty", difficulty.ToString());
        command.Parameters.AddWithValue("$questionCount", questionCount);
        command.Parameters.AddWithValue("$startedAtUtc", Format(DateTime.UtcNow));
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>
    /// Stores the answer to one question of an attempt; answering the same question again keeps the first answer.
    /// Throws <see cref="SqliteException"/> when the attempt or the question does not exist.
    /// </summary>
    public void SaveAnswer(long attemptId, long questionId, int selectedOption, bool isCorrect)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ExamAnswers (AttemptId, QuestionId, SelectedOption, IsCorrect, AnsweredAtUtc)
            VALUES ($attemptId, $questionId, $selectedOption, $isCorrect, $answeredAtUtc)
            ON CONFLICT (AttemptId, QuestionId) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$attemptId", attemptId);
        command.Parameters.AddWithValue("$questionId", questionId);
        command.Parameters.AddWithValue("$selectedOption", selectedOption);
        command.Parameters.AddWithValue("$isCorrect", isCorrect);
        command.Parameters.AddWithValue("$answeredAtUtc", Format(DateTime.UtcNow));
        command.ExecuteNonQuery();
    }

    public void FinishAttempt(long attemptId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ExamAttempts SET FinishedAtUtc = $finishedAtUtc WHERE Id = $id AND FinishedAtUtc IS NULL;";
        command.Parameters.AddWithValue("$id", attemptId);
        command.Parameters.AddWithValue("$finishedAtUtc", Format(DateTime.UtcNow));
        command.ExecuteNonQuery();
    }

    /// <summary>Returns the exam history, newest first.</summary>
    public IReadOnlyList<ExamAttempt> GetAttempts()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.Id, a.TopicId, a.Difficulty, a.QuestionCount, COUNT(e.Id), COALESCE(SUM(e.IsCorrect), 0),
                   a.StartedAtUtc, a.FinishedAtUtc
            FROM ExamAttempts a
            LEFT JOIN ExamAnswers e ON e.AttemptId = a.Id
            GROUP BY a.Id
            ORDER BY a.Id DESC;
            """;

        var attempts = new List<ExamAttempt>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            attempts.Add(new ExamAttempt(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                Enum.Parse<ExamDifficulty>(reader.GetString(2)),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : Parse(reader.GetString(7))));
        }

        return attempts;
    }

    /// <summary>Answer totals of every question answered at least once.</summary>
    public IReadOnlyList<QuestionStats> GetQuestionStats()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT q.Id, q.TopicId, COUNT(*), SUM(CASE WHEN e.IsCorrect = 0 THEN 1 ELSE 0 END)
            FROM ExamAnswers e
            JOIN StudyQuestions q ON q.Id = e.QuestionId
            GROUP BY q.Id
            ORDER BY q.Id;
            """;

        var stats = new List<QuestionStats>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            stats.Add(new QuestionStats(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3)));
        }

        return stats;
    }

    /// <summary>Questions answered wrong in an attempt, in answer order.</summary>
    public IReadOnlyList<StudyQuestion> GetFailedQuestions(long attemptId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            {SelectQuestions}
            JOIN ExamAnswers e ON e.QuestionId = q.Id
            WHERE e.AttemptId = $attemptId AND e.IsCorrect = 0
            ORDER BY e.Id;
            """;
        command.Parameters.AddWithValue("$attemptId", attemptId);
        return ReadQuestions(command);
    }

    private const string SelectQuestions = """
        SELECT q.Id, q.TopicId, q.Kind, q.Difficulty, q.Prompt, q.OptionsJson, q.CorrectOption, q.Explanation, q.Origin
        FROM StudyQuestions q
        """;

    private static long? Insert(SqliteConnection connection, SqliteTransaction transaction, StudyQuestion question)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO StudyQuestions (TopicId, Kind, Difficulty, Prompt, OptionsJson, CorrectOption, Explanation, Origin,
                                        Fingerprint, CreatedAtUtc)
            VALUES ($topicId, $kind, $difficulty, $prompt, $optionsJson, $correctOption, $explanation, $origin,
                    $fingerprint, $createdAtUtc)
            ON CONFLICT (Fingerprint) DO NOTHING
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$topicId", question.TopicId);
        command.Parameters.AddWithValue("$kind", question.Kind.ToString());
        command.Parameters.AddWithValue("$difficulty", question.Difficulty.ToString());
        command.Parameters.AddWithValue("$prompt", question.Prompt.Trim());
        command.Parameters.AddWithValue("$optionsJson", JsonSerializer.Serialize(question.Options));
        command.Parameters.AddWithValue("$correctOption", question.CorrectOption);
        command.Parameters.AddWithValue("$explanation", question.Explanation);
        command.Parameters.AddWithValue("$origin", question.Origin);
        command.Parameters.AddWithValue("$fingerprint", ExamBuilder.Fingerprint(question.Prompt));
        command.Parameters.AddWithValue("$createdAtUtc", Format(DateTime.UtcNow));
        return command.ExecuteScalar() as long?;
    }

    private static List<StudyQuestion> ReadQuestions(SqliteCommand command)
    {
        var questions = new List<StudyQuestion>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            questions.Add(new StudyQuestion(
                reader.GetInt64(0),
                reader.GetInt64(1),
                Enum.Parse<QuestionKind>(reader.GetString(2)),
                Enum.Parse<QuestionDifficulty>(reader.GetString(3)),
                reader.GetString(4),
                JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [],
                reader.GetInt32(6),
                reader.GetString(7),
                reader.GetString(8)));
        }

        return questions;
    }

    private static string Format(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime Parse(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

/// <summary>A catalog topic with its questions. Ids are assigned when stored.</summary>
public sealed record CatalogTopic(StudyTopic Topic, IReadOnlyList<StudyQuestion> Questions);
