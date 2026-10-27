using Radar.Core.Learning;

namespace Radar.Infrastructure.Tests;

public sealed class ExamBuilderTests
{
    private static readonly StudyQuestion[] Questions =
    [
        Question(1, topicId: 1, QuestionDifficulty.Basic),
        Question(2, topicId: 1, QuestionDifficulty.Basic),
        Question(3, topicId: 1, QuestionDifficulty.Advanced),
        Question(4, topicId: 2, QuestionDifficulty.Basic),
        Question(5, topicId: 2, QuestionDifficulty.Advanced),
        Question(6, topicId: 2, QuestionDifficulty.Advanced),
    ];

    [Fact]
    public void Basic_exam_only_has_basic_questions_up_to_the_requested_count()
    {
        var exam = ExamBuilder.Build(Questions, topicId: null, ExamDifficulty.Basic, count: 2, new Random(1));

        Assert.Equal(2, exam.Count);
        Assert.All(exam, q => Assert.Equal(QuestionDifficulty.Basic, q.Difficulty));
        Assert.Equal(exam.Count, exam.Distinct().Count());
    }

    [Fact]
    public void Advanced_exam_only_has_advanced_questions()
    {
        var exam = ExamBuilder.Build(Questions, topicId: null, ExamDifficulty.Advanced, count: 20, new Random(1));

        Assert.Equal([3, 5, 6], exam.Select(q => q.Id).Order());
    }

    [Fact]
    public void Mixed_exam_alternates_basic_and_advanced_questions()
    {
        var exam = ExamBuilder.Build(Questions, topicId: null, ExamDifficulty.Mixed, count: 4, new Random(1));

        Assert.Equal(
            [QuestionDifficulty.Basic, QuestionDifficulty.Advanced, QuestionDifficulty.Basic, QuestionDifficulty.Advanced],
            exam.Select(q => q.Difficulty));
    }

    [Fact]
    public void Topic_exam_only_has_questions_of_that_topic()
    {
        var exam = ExamBuilder.Build(Questions, topicId: 2, ExamDifficulty.Mixed, count: 10, new Random(1));

        Assert.Equal([4, 5, 6], exam.Select(q => q.Id).Order());
    }

    [Fact]
    public void Returns_fewer_questions_when_not_enough_exist()
    {
        var exam = ExamBuilder.Build(Questions, topicId: null, ExamDifficulty.Mixed, count: 20, new Random(1));

        Assert.Equal(Questions.Length, exam.Count);
    }

    [Fact]
    public void TopicsToReview_lists_topics_with_wrong_answers_most_wrong_first()
    {
        var answers = new[]
        {
            (Questions[0], false),
            (Questions[3], false),
            (Questions[4], false),
            (Questions[1], true),
        };

        Assert.Equal([2L, 1L], ExamBuilder.TopicsToReview(answers));
    }

    [Fact]
    public void Fingerprint_ignores_case_accents_punctuation_and_spacing()
    {
        Assert.Equal(
            ExamBuilder.Fingerprint("¿Qué es   Engram?"),
            ExamBuilder.Fingerprint("que es engram"));
        Assert.NotEqual(ExamBuilder.Fingerprint("Qué es Engram"), ExamBuilder.Fingerprint("Qué es ODD"));
    }

    private static StudyQuestion Question(long id, long topicId, QuestionDifficulty difficulty) =>
        new(id, topicId, QuestionKind.MultipleChoice, difficulty, $"Question {id}", ["A", "B"], 0, "Because A.", "test");
}
