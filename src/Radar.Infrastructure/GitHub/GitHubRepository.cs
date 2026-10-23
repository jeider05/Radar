using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Radar.Infrastructure.GitHub;

/// <summary>
/// A public GitHub repository identified by owner and name. Single place that validates and normalizes user input.
/// </summary>
public sealed partial record GitHubRepository(string Owner, string Name)
{
    /// <summary>Identifier stored in Sources.ExternalId. GitHub names are case-insensitive.</summary>
    public string ExternalId => $"{Owner}/{Name}".ToLowerInvariant();

    public string DisplayName => $"{Owner}/{Name}";

    public string Url => $"https://github.com/{Owner}/{Name}";

    /// <summary>
    /// Accepts "owner/repository" or a github.com repository URL (with or without scheme, trailing slash or ".git").
    /// </summary>
    public static bool TryParse(string? input, [NotNullWhen(true)] out GitHubRepository? repository, out string error)
    {
        repository = null;
        var text = input?.Trim() ?? "";
        if (text.Length == 0)
        {
            error = "Enter a repository as owner/repository or a GitHub URL.";
            return false;
        }

        var prefix = GitHubUrlPrefix().Match(text);
        if (prefix.Success)
        {
            text = text[prefix.Length..];
        }
        else if (text.Contains("://", StringComparison.Ordinal))
        {
            error = "Only github.com repositories are supported.";
            return false;
        }

        text = text.TrimEnd('/');
        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^4];
        }

        if (text.Split('/') is not [var owner, var name] || owner.Length == 0 || name.Length == 0)
        {
            error = "Use the format owner/repository, for example jqlang/jq.";
            return false;
        }

        if (!OwnerPattern().IsMatch(owner))
        {
            error = $"'{owner}' is not a valid GitHub owner (letters, numbers and hyphens, up to 39 characters).";
            return false;
        }

        if (!NamePattern().IsMatch(name) || name is "." or "..")
        {
            error = $"'{name}' is not a valid repository name (letters, numbers, '.', '-' and '_', up to 100 characters).";
            return false;
        }

        repository = new GitHubRepository(owner, name);
        error = "";
        return true;
    }

    [GeneratedRegex(@"^(https?://)?(www\.)?github\.com/", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubUrlPrefix();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}$")]
    private static partial Regex OwnerPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex NamePattern();
}
