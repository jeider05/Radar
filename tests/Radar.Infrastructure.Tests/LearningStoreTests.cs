using Microsoft.Data.Sqlite;
using Radar.Core.Learning;
using Radar.Infrastructure.Learning;
using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Tests;

public sealed class LearningStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Radar.Tests", Guid.NewGuid().ToString("N"));
    private readonly RadarDatabase _database;
    private readonly LearningStore _store;

    public LearningStoreTests()
    {
        _database = new RadarDatabase(Path.Combine(_directory, "radar.db"));
        _database.Initialize();
        _store = new LearningStore(_database);
    }

    [Fact]
    public void SeedCatalog_stores_every_catalog_topic_and_question()
    {
        _store.SeedCatalog(GentleAiCatalog.Topics);

        Assert.Equal(GentleAiCatalog.Topics.Count, _store.GetTopics().Count);
        Assert.Equal(GentleAiCatalog.Topics.Sum(t => t.Questions.Count), _store.GetQuestions().Count);
        Assert.All(_store.GetQuestions(), q => Assert.Equal(GentleAiCatalog.Origin, q.Origin));
    }

    [Fact]
    public void Seeding_twice_creates_no_duplicates_and_keeps_progress_and_history()
    {
        _store.SeedCatalog(GentleAiCatalog.Topics);
        var topic = _store.GetTopics()[0];
        var question = _store.GetQuestions().First(q => q.TopicId == topic.Id);
        _store.SetStatus(topic.Id, StudyStatus.Studied);
        var attemptId = _store.StartAttempt(topic.Id, ExamDifficulty.Mixed, 1);
        _store.SaveAnswer(attemptId, question.Id, selectedOption: 0, isCorrect: false);

        _store.SeedCatalog(GentleAiCatalog.Topics);

        Assert.Equal(GentleAiCatalog.Topics.Count, _store.GetTopics().Count);
        Assert.Equal(GentleAiCatalog.Topics.Sum(t => t.Questions.Count), _store.GetQuestions().Count);
        Assert.Equal(StudyStatus.Studied, _store.GetStatuses()[topic.Id]);
        Assert.Single(_store.GetAttempts());
    }

    [Fact]
    public void Topics_without_progress_have_no_status_entry()
    {
        _store.SeedCatalog(GentleAiCatalog.Topics);

        Assert.Empty(_store.GetStatuses());
    }

    [Fact]
    public void Progress_and_exam_history_survive_a_new_store_instance()
    {
        _store.SeedCatalog(GentleAiCatalog.Topics);
        var question = _store.GetQuestions()[0];
        _store.SetStatus(question.TopicId, StudyStatus.InProgress);
        var attemptId = _store.StartAttempt(topicId: null, ExamDifficulty.Basic, 2);
        _store.SaveAnswer(attemptId, question.Id, question.CorrectOption, isCorrect: true);
        _store.FinishAttempt(attemptId);

        var reopened = new LearningStore(new RadarDatabase(_database.DatabasePath));

        Assert.Equal(StudyStatus.InProgress, reopened.GetStatuses()[question.TopicId]);
        var attempt = Assert.Single(reopened.GetAttempts());
        Assert.Null(attempt.TopicId);
        Assert.Equal(ExamDifficulty.Basic, attempt.Difficulty);
        Assert.Equal(2, attempt.QuestionCount);
        Assert.Equal(1, attempt.AnsweredCount);
        Assert.Equal(1, attempt.CorrectCount);
        Assert.NotNull(attempt.FinishedAtUtc);
    }

    [Fact]
    public void GetQuestionStats_counts_answers_and_mistakes_per_question()
    {
        _store.SeedCatalog(GentleAiCatalog.Topics);
        var question = _store.GetQuestions()[0];
        var first = _store.StartAttempt(null, ExamDifficulty.Mixed, 1);
        _store.SaveAnswer(first, question.Id, 0, isCorrect: false);
        var second = _store.StartAttempt(null, ExamDifficulty.Mixed, 1);
        _store.SaveAnswer(second, question.Id, question.CorrectOption, isCorrect: true);

        var stats = Assert.Single(_store.GetQuestionStats());

        Assert.Equal(question.Id, stats.QuestionId);
        Assert.Equal(question.TopicId, stats.TopicId);
        Assert.Equal(2, stats.Answered);
        Assert.Equal(1, stats.Wrong);
    }

    [Fact]
    public void GetFailedQuestions_returns_only_the_wrong_answers_of_an_attempt()
    {
        _store.SeedCatalog(GentleAiCatalog.Topics);
        var questions = _store.GetQuestions();
        var attemptId = _store.StartAttempt(null, ExamDifficulty.Mixed, 2);
        _store.SaveAnswer(attemptId, questions[0].Id, 0, isCorrect: false);
        _store.SaveAnswer(attemptId, questions[1].Id, questions[1].CorrectOption, isCorrect: true);

        var failed = Assert.Single(_store.GetFailedQuestions(attemptId));

        Assert.Equal(questions[0].Id, failed.Id);
    }

    [Fact]
    public void TryAddQuestion_rejects_a_question_with_the_same_wording()
    {
        _store.SeedCatalog(GentleAiCatalog.Topics);
        var existing = _store.GetQuestions()[0];
        var reworded = existing with { Id = 0, Prompt = "  " + existing.Prompt.ToUpperInvariant() + "  ", Origin = "Gemini test" };

        Assert.Null(_store.TryAddQuestion(reworded));
        Assert.Equal(GentleAiCatalog.Topics.Sum(t => t.Questions.Count), _store.GetQuestions().Count);
    }

    [Fact]
    public void TryAddQuestion_stores_a_new_question_with_its_origin()
    {
        _store.SeedCatalog(GentleAiCatalog.Topics);
        var topic = _store.GetTopics()[0];
        var question = new StudyQuestion(0, topic.Id, QuestionKind.Scenario, QuestionDifficulty.Advanced,
            "Pregunta nueva de prueba", ["A", "B", "C"], 2, "Porque C.", "Gemini test-model");

        var added = _store.TryAddQuestion(question);

        Assert.NotNull(added);
        Assert.Equal(question with { Id = added.Id }, _store.GetQuestions().Single(q => q.Id == added.Id), QuestionComparer.Instance);
    }

    [Fact]
    public void SaveAnswer_for_an_unknown_attempt_throws()
    {
        _store.SeedCatalog(GentleAiCatalog.Topics);

        Assert.Throws<SqliteException>(() => _store.SaveAnswer(999, _store.GetQuestions()[0].Id, 0, isCorrect: false));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>Records compare lists by reference; this compares the option texts instead.</summary>
    private sealed class QuestionComparer : IEqualityComparer<StudyQuestion>
    {
        public static readonly QuestionComparer Instance = new();

        public bool Equals(StudyQuestion? x, StudyQuestion? y) =>
            x is not null && y is not null && x.Options.SequenceEqual(y.Options) && (x with { Options = [] }) == (y with { Options = [] });

        public int GetHashCode(StudyQuestion obj) => obj.Id.GetHashCode();
    }
}
