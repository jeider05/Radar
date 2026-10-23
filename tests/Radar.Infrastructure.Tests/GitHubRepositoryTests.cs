using Radar.Infrastructure.GitHub;

namespace Radar.Infrastructure.Tests;

public sealed class GitHubRepositoryTests
{
    [Theory]
    [InlineData("jqlang/jq", "jqlang", "jq")]
    [InlineData("  jqlang/jq  ", "jqlang", "jq")]
    [InlineData("Example-Org/My.Repo_1", "Example-Org", "My.Repo_1")]
    [InlineData("https://github.com/jqlang/jq", "jqlang", "jq")]
    [InlineData("https://github.com/jqlang/jq/", "jqlang", "jq")]
    [InlineData("https://github.com/jqlang/jq.git", "jqlang", "jq")]
    [InlineData("http://www.github.com/jqlang/jq", "jqlang", "jq")]
    [InlineData("github.com/jqlang/jq", "jqlang", "jq")]
    public void TryParse_accepts_owner_repo_and_github_urls(string input, string owner, string name)
    {
        Assert.True(GitHubRepository.TryParse(input, out var repository, out var error));

        Assert.Equal(new GitHubRepository(owner, name), repository);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("jq")]
    [InlineData("jqlang/")]
    [InlineData("/jq")]
    [InlineData("jqlang/jq/releases")]
    [InlineData("https://gitlab.com/jqlang/jq")]
    [InlineData("https://github.com/jqlang")]
    [InlineData("my org/jq")]
    [InlineData("-jqlang/jq")]
    [InlineData("jq_lang/jq")]
    [InlineData("jqlang/j q")]
    [InlineData("jqlang/..")]
    [InlineData("jqlang/.")]
    public void TryParse_rejects_invalid_input_with_a_message(string? input)
    {
        Assert.False(GitHubRepository.TryParse(input, out var repository, out var error));

        Assert.Null(repository);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryParse_rejects_names_that_are_too_long()
    {
        Assert.False(GitHubRepository.TryParse($"{new string('a', 40)}/repo", out _, out _));
        Assert.False(GitHubRepository.TryParse($"owner/{new string('a', 101)}", out _, out _));
        Assert.True(GitHubRepository.TryParse($"{new string('a', 39)}/{new string('b', 100)}", out _, out _));
    }

    [Fact]
    public void ExternalId_is_lowercase_and_Url_keeps_original_case()
    {
        var repository = new GitHubRepository("Example-Org", "My.Repo");

        Assert.Equal("example-org/my.repo", repository.ExternalId);
        Assert.Equal("Example-Org/My.Repo", repository.DisplayName);
        Assert.Equal("https://github.com/Example-Org/My.Repo", repository.Url);
    }
}
