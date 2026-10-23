namespace Radar.Core.Contents;

/// <summary>An AI-generated explanation of a content item, stored so it is not requested again.</summary>
public sealed record ContentExplanation(long ContentItemId, string Text, string Model, DateTime CreatedAtUtc);
