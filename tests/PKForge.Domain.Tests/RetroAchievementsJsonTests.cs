using System.Text.Json;
using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>
/// The RA web API adds DateEarned/DateEarnedHardcore to an achievement only once the
/// player unlocked it, but older responses returned an empty string for a locked one:
/// the property's presence alone must never count as an unlock.
/// </summary>
public sealed class RetroAchievementsJsonTests
{
    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Fact]
    public void EarnedAchievementKeepsItsTimestamp()
    {
        var root = Parse("""
        {
          "ID": 1, "Title": "Sonic the Hedgehog", "NumAchievements": 1,
          "Achievements": {
            "9": { "ID": 9, "Title": "That Was Easy", "Description": "Complete act 1",
                   "Points": 3, "BadgeName": "250336",
                   "DateEarned": "2016-03-12 17:47:29", "DateEarnedHardcore": "2016-03-12 17:47:29" }
          }
        }
        """);
        var progress = RetroAchievementsJson.ParseGameProgress(root, 1);
        var achievement = Assert.Single(progress.Achievements);
        Assert.True(achievement.Earned);
        Assert.True(achievement.EarnedHardcore);
        Assert.Equal(new DateTimeOffset(2016, 3, 12, 17, 47, 29, TimeSpan.Zero), achievement.DateEarned);
        Assert.Equal(1, progress.Unlocked);
        Assert.Equal(3, progress.PointsEarned);
    }

    [Fact]
    public void AchievementWithoutEarnedFieldsIsLocked()
    {
        var root = Parse("""
        {
          "ID": 1, "Title": "Sonic the Hedgehog",
          "Achievements": {
            "9": { "ID": 9, "Title": "That Was Easy", "Description": "Complete act 1",
                   "Points": 3, "BadgeName": "250336" }
          }
        }
        """);
        var achievement = Assert.Single(RetroAchievementsJson.ParseGameProgress(root, 1).Achievements);
        Assert.False(achievement.Earned);
        Assert.False(achievement.EarnedHardcore);
        Assert.Null(achievement.DateEarned);
    }

    [Theory]
    [InlineData("\"DateEarned\": \"\"")]
    [InlineData("\"DateEarned\": \"   \"")]
    [InlineData("\"DateEarned\": null")]
    public void EmptyStringDateEarnedIsNotAnUnlock(string dateField)
    {
        var root = Parse($$"""
        {
          "ID": 1, "Title": "Sonic the Hedgehog",
          "Achievements": {
            "9": { "ID": 9, "Title": "That Was Easy", "Description": "Complete act 1",
                   "Points": 3, "BadgeName": "250336", {{dateField}} }
          }
        }
        """);
        var achievement = Assert.Single(RetroAchievementsJson.ParseGameProgress(root, 1).Achievements);
        Assert.False(achievement.Earned);
        Assert.Null(achievement.DateEarned);
    }

    [Fact]
    public void SoftcoreOnlyUnlockIsNotHardcore()
    {
        var root = Parse("""
        {
          "ID": 1, "Title": "Sonic the Hedgehog",
          "Achievements": {
            "9": { "ID": 9, "Title": "That Was Easy", "Description": "Complete act 1",
                   "Points": 3, "BadgeName": "250336", "DateEarned": "2016-03-12 17:47:29" }
          }
        }
        """);
        var achievement = Assert.Single(RetroAchievementsJson.ParseGameProgress(root, 1).Achievements);
        Assert.True(achievement.Earned);
        Assert.False(achievement.EarnedHardcore);
    }

    [Fact]
    public void MissingFieldsDefaultToEmptyAndZero()
    {
        var root = Parse("""{ "ID": 7, "Achievements": { "3": { "ID": 3 } } }""");
        var progress = RetroAchievementsJson.ParseGameProgress(root, 7);
        Assert.Equal(7, progress.GameId);
        Assert.Equal(string.Empty, progress.Title);
        var achievement = Assert.Single(progress.Achievements);
        Assert.Equal(3, achievement.Id);
        Assert.Equal(string.Empty, achievement.Title);
        Assert.Equal(0, achievement.Points);
        Assert.False(achievement.Earned);
    }

    [Fact]
    public void EmptyAchievementSetSerializesAsAnObject()
    {
        // issue #484 on RAWeb: a game with no published set returns {} - never an array.
        var root = Parse("""{ "ID": 7, "Title": "Empty", "Achievements": {} }""");
        var progress = RetroAchievementsJson.ParseGameProgress(root, 7);
        Assert.Empty(progress.Achievements);
        Assert.Equal(100, progress.Percent); // nothing to do counts as complete
    }

    [Fact]
    public void AchievementsAreSortedById()
    {
        var root = Parse("""
        {
          "ID": 1,
          "Achievements": {
            "30": { "ID": 30, "Title": "C" },
            "2":  { "ID": 2,  "Title": "A" },
            "17": { "ID": 17, "Title": "B" }
          }
        }
        """);
        var progress = RetroAchievementsJson.ParseGameProgress(root, 1);
        Assert.Equal([2, 17, 30], progress.Achievements.Select(a => a.Id).ToArray());
    }

    [Theory]
    [InlineData("""{"ID": 1, "Title": "Sonic"}""", true)]
    [InlineData("""{"ID": "not a number"}""", true)]   // presence is what the client checks
    [InlineData("""[]""", false)]                        // an empty array is the error shape
    [InlineData("""{"error": "Unknown game"}""", false)]
    public void LooksLikeGameOnlyAcceptsAnObjectWithAnId(string json, bool expected) =>
        Assert.Equal(expected, RetroAchievementsJson.LooksLikeGame(Parse(json)));

    [Fact]
    public void LooksLikeProfileRejectsTheErrorShape() =>
        Assert.False(RetroAchievementsJson.LooksLikeProfile(Parse("""{"error": "Unknown user"}""")));
}
