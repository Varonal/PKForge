using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class ExternalSavePathResolverTests
{
    private const string Rom = "/storage/emulated/0/ROMs/gba/Pokemon - Emerald Version.gba";
    private const string Stem = "Pokemon - Emerald Version";
    private const string Dir = "/storage/emulated/0/ROMs/gba";

    [Fact]
    public void RetroArchPutsTheSrmBesideTheRomFirstThenItsSavesFolders()
    {
        var paths = ExternalSavePathResolver.CandidatePaths(Rom, EmulatorKind.RetroArch);

        Assert.Equal($"{Dir}/{Stem}.srm", paths[0]);
        Assert.Equal($"{Dir}/{Stem}.sav", paths[1]);
        Assert.Contains($"{ExternalSavePathResolver.StorageRoot}/RetroArch/saves/{Stem}.srm", paths);
        Assert.Contains($"{ExternalSavePathResolver.StorageRoot}/retroarch/saves/Pokemon - Emerald Version.gba.srm", paths);
        Assert.True(paths.Count >= 5);
        Assert.All(paths, p => Assert.False(p.Contains('\\'), "paths must use '/' separators"));
    }

    [Fact]
    public void MelonDsPutsTheSavBesideTheRomFirst()
    {
        var paths = ExternalSavePathResolver.CandidatePaths(
            "/storage/emulated/0/ROMs/nds/Pokemon - Diamond Version.nds", EmulatorKind.MelonDS);

        Assert.Equal("/storage/emulated/0/ROMs/nds/Pokemon - Diamond Version.sav", paths[0]);
        Assert.Contains(paths, p => p.EndsWith("/Android/data/io.github.melonds/files/saves/Pokemon - Diamond Version.nds.sav", StringComparison.Ordinal));
    }

    [Fact]
    public void AzaharUsesTheTitleIdFromTheRomNameForTheNandCandidate()
    {
        var paths = ExternalSavePathResolver.CandidatePaths(
            "/storage/emulated/0/ROMs/3ds/Pokemon X (0004000000055E00).3ds", EmulatorKind.Azahar);

        Assert.Equal("/storage/emulated/0/ROMs/3ds/Pokemon X (0004000000055E00).sav", paths[0]);
        Assert.Contains($"{ExternalSavePathResolver.StorageRoot}/Azahar/nand/data/0004000000055e00/save/00000001", paths);
        // Citra MMJ shares the Citra-family layout.
        var citraPaths = ExternalSavePathResolver.CandidatePaths(
            "/storage/emulated/0/ROMs/3ds/Pokemon X (0004000000055E00).3ds", EmulatorKind.CitraMmj);
        Assert.Contains($"{ExternalSavePathResolver.StorageRoot}/citra-emu/nand/data/0004000000055e00/save/00000001", citraPaths);
    }

    [Fact]
    public void AzaharWithoutTitleIdFallsBackToTheSiblingSavOnly()
    {
        var paths = ExternalSavePathResolver.CandidatePaths(
            "/storage/emulated/0/ROMs/3ds/Pokemon X.3ds", EmulatorKind.Azahar);

        Assert.Single(paths);
        Assert.Equal("/storage/emulated/0/ROMs/3ds/Pokemon X.sav", paths[0]);
    }

    [Fact]
    public void DolphinLooksForIndividualGciSaves()
    {
        var paths = ExternalSavePathResolver.CandidatePaths(
            "/storage/emulated/0/ROMs/gc/01-GXXP-PokemonXD.iso", EmulatorKind.Dolphin);

        Assert.Equal("/storage/emulated/0/ROMs/gc/01-GXXP-PokemonXD.gci", paths[0]);
        Assert.Contains($"{ExternalSavePathResolver.StorageRoot}/dolphin-emu/GC/01-GXXP-PokemonXD.iso.gci", paths);
        Assert.DoesNotContain(paths, p => p.EndsWith(".raw", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(EmulatorKind.DraStic, ".dsv")]
    [InlineData(EmulatorKind.PizzaBoyGba, ".sav")]
    [InlineData(EmulatorKind.Eden, ".bin")]
    public void UnknownEmulatorsUseTheDefaultSiblingExtensions(EmulatorKind kind, string firstExtension)
    {
        var paths = ExternalSavePathResolver.CandidatePaths(Rom, kind);

        Assert.Equal($"{Dir}/{Stem}{firstExtension}", paths[0]);
        Assert.Contains($"{Dir}/main", paths);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrEmptyRomPathYieldsNoCandidatesAndNeverCrashes(string? romPath)
    {
        Assert.Empty(ExternalSavePathResolver.CandidatePaths(romPath, EmulatorKind.RetroArch));
        Assert.Empty(ExternalSavePathResolver.ExistingPaths(romPath, EmulatorKind.RetroArch, _ => true));
    }

    [Fact]
    public void RomPathWithoutDirectoryStillResolvesSiblings()
    {
        var paths = ExternalSavePathResolver.CandidatePaths("emerald.gba", EmulatorKind.RetroArch);
        Assert.Equal("emerald.srm", paths[0]);
    }

    [Fact]
    public void WindowsStyleSeparatorsAreNormalized()
    {
        var paths = ExternalSavePathResolver.CandidatePaths(
            "\\storage\\emulated\\0\\ROMs\\gba\\emerald.gba", EmulatorKind.MelonDS);
        Assert.Equal("/storage/emulated/0/ROMs/gba/emerald.sav", paths[0]);
    }

    [Fact]
    public void ExistingPathsKeepsOnlyWhatTheInjectedCheckAccepts()
    {
        var existing = $"{Dir}/{Stem}.srm";
        var paths = ExternalSavePathResolver.ExistingPaths(Rom, EmulatorKind.RetroArch, p => p == existing);

        Assert.Single(paths);
        Assert.Equal(existing, paths[0]);
    }

    [Fact]
    public void NoSaveOnDiskYetMeansAnEmptyListNotACrash()
    {
        Assert.Empty(ExternalSavePathResolver.ExistingPaths(Rom, EmulatorKind.RetroArch, _ => false));
    }

    [Fact]
    public void TheConventionRegistryIsExtensibleAndCoversTheRequiredEmulators()
    {
        foreach (var kind in new[] { EmulatorKind.RetroArch, EmulatorKind.MelonDS, EmulatorKind.Azahar, EmulatorKind.CitraMmj, EmulatorKind.Dolphin })
            Assert.True(ExternalSavePathResolver.Conventions.ContainsKey(kind), $"missing convention for {kind}");

        // Registering a new emulator changes resolution without touching the resolver.
        var conventions = (Dictionary<EmulatorKind, Func<string, IReadOnlyList<string>>>)ExternalSavePathResolver.Conventions;
        var custom = EmulatorKind.DraStic;
        var had = conventions.TryGetValue(custom, out var previous);
        try
        {
            conventions[custom] = rom => ["/custom/place.sav"];
            Assert.Equal(["/custom/place.sav"], ExternalSavePathResolver.CandidatePaths(Rom, custom));
        }
        finally
        {
            if (had) conventions[custom] = previous!;
            else conventions.Remove(custom);
        }
    }
}
