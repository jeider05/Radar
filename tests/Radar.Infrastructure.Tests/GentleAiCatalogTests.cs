using Radar.Core.Learning;
using Radar.Infrastructure.Learning;

namespace Radar.Infrastructure.Tests;

/// <summary>Guards the built-in study catalog: complete topics, sourced content and answerable questions.</summary>
public sealed class GentleAiCatalogTests
{
    [Fact]
    public void Has_the_eleven_requested_topics_with_unique_keys()
    {
        Assert.Equal(11, GentleAiCatalog.Topics.Count);
        Assert.Equal(11, GentleAiCatalog.Topics.Select(t => t.Topic.Key).Distinct().Count());
    }

    [Fact]
    public void Every_topic_has_every_section_and_an_official_source()
    {
        Assert.All(GentleAiCatalog.Topics, entry =>
        {
            var topic = entry.Topic;
            Assert.False(string.IsNullOrWhiteSpace(topic.WhatItIs));
            Assert.False(string.IsNullOrWhiteSpace(topic.WhatItIsFor));
            Assert.False(string.IsNullOrWhiteSpace(topic.HowItWorks));
            Assert.False(string.IsNullOrWhiteSpace(topic.Example));
            Assert.False(string.IsNullOrWhiteSpace(topic.Differences));
            Assert.False(string.IsNullOrWhiteSpace(topic.ConfirmedFacts));
            Assert.StartsWith("https://github.com/Gentleman-Programming/gentle-ai", topic.SourceUrl);
            Assert.Contains("2026-10-27", topic.SourceReference);
        });
    }

    [Fact]
    public void Every_question_is_answerable_and_explained()
    {
        Assert.All(GentleAiCatalog.Topics.SelectMany(t => t.Questions), q =>
        {
            Assert.InRange(q.Options.Count, 3, 4);
            Assert.InRange(q.CorrectOption, 0, q.Options.Count - 1);
            Assert.Equal(q.Options.Count, q.Options.Distinct().Count());
            Assert.False(string.IsNullOrWhiteSpace(q.Explanation));
        });
    }

    [Fact]
    public void Question_wordings_are_unique()
    {
        var fingerprints = GentleAiCatalog.Topics.SelectMany(t => t.Questions).Select(q => ExamBuilder.Fingerprint(q.Prompt)).ToList();

        Assert.Equal(fingerprints.Count, fingerprints.Distinct().Count());
    }

    [Fact]
    public void Every_topic_has_basic_advanced_and_scenario_questions()
    {
        Assert.All(GentleAiCatalog.Topics, entry =>
        {
            Assert.Contains(entry.Questions, q => q.Difficulty == QuestionDifficulty.Basic);
            Assert.Contains(entry.Questions, q => q.Difficulty == QuestionDifficulty.Advanced);
            Assert.Contains(entry.Questions, q => q.Kind == QuestionKind.Scenario);
        });
    }

    [Fact]
    public void A_general_exam_of_twenty_questions_can_be_built_for_every_difficulty()
    {
        var questions = GentleAiCatalog.Topics.SelectMany(t => t.Questions).ToList();

        Assert.True(questions.Count(q => q.Difficulty == QuestionDifficulty.Basic) >= 20);
        Assert.True(questions.Count(q => q.Difficulty == QuestionDifficulty.Advanced) >= 20);
    }

    [Fact]
    public void Odd_is_presented_as_the_current_workflow_and_sdd_as_retired()
    {
        var odd = GentleAiCatalog.Topics.Single(t => t.Topic.Key == "odd").Topic;

        Assert.Contains("v4.0.0", odd.ConfirmedFacts);
        Assert.Contains("SDD", odd.Differences);
        Assert.DoesNotContain(GentleAiCatalog.Topics, t => t.Topic.ConfirmedFacts.Contains("SDD es el flujo por defecto"));
    }
}
