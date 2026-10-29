using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class AppUpdateRulesTests
{
    [Theory]
    [InlineData("1.3.0", "v1.3.1", true)]
    [InlineData("1.3.0", "v1.10.0", true)]
    [InlineData("1.3.0", "v1.3.0", false)]
    [InlineData("1.3.0", "v1.2.9", false)]
    [InlineData("3.0.3", "v3.1.0", true)] // the 3.1 release, as a 3.0.3 install sees it
    [InlineData("3.1.0", "v3.0.3", false)]
    [InlineData("1.4.0", "v1.4.0-beta+build.1", false)]
    [InlineData("1.4.0-beta", "v1.4.0", true)]        // a release outranks its own prerelease
    [InlineData("1.4.0", "v1.4.0-rc.1", false)]
    [InlineData("1.4.0-beta.1", "v1.4.0-beta.2", true)]  // numeric identifiers compare numerically
    [InlineData("1.4.0-beta.2", "v1.4.0-beta.10", true)] // 10 > 2, not an ordinal "1" < "2" read
    [InlineData("1.4.0-beta", "v1.4.0-beta.1", true)]    // more identifiers win a shared-prefix tie
    [InlineData("1.4.0-beta.1", "v1.4.0-beta", false)]
    [InlineData("1.4.0-alpha", "v1.4.0-beta", true)]    // ordinal among alphanumeric identifiers
    [InlineData("1.4.0-beta.1", "v1.4.0-2", false)]     // numeric ranks below alphanumeric
    [InlineData("garbage", "v1.4.0", false)]
    public void ComparesStableReleaseTags(string installed, string candidate, bool expected) =>
        Assert.Equal(expected, AppUpdateRules.IsNewerVersion(installed, candidate));

    [Fact]
    public void SkippedReleaseDoesNotPromptButNewerReleaseDoes()
    {
        Assert.False(AppUpdateRules.ShouldPromptAutomatically("1.3.0", "1.3.1", "1.3.1"));
        Assert.True(AppUpdateRules.ShouldPromptAutomatically("1.3.0", "1.4.0", "1.3.1"));
        Assert.False(AppUpdateRules.ShouldPromptAutomatically("1.3.0", "1.3.0", ""));
    }
}
