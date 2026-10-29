using PKForge.Domain;
using PKForge.Engine;
using PKHeX.Core;
using Xunit;

namespace PKForge.Engine.Tests;

/// <summary>
/// Nicknames and OT names must fit the mon's string buffer: a name longer than the format
/// stores would overflow it when encoded into the save. The box slot starts empty, so a
/// freshly built mon is placed first (the same write the generator performs).
/// </summary>
public sealed class NicknameLimitTests
{
    private static SaveEngineSession BlankSession(int generation) =>
        (SaveEngineSession)new SaveEngine().OpenBlankSession(generation);

    private static (int Box, int Slot) SeedMon(SaveEngineSession session)
    {
        var mon = session.SaveFile.BlankPKM;
        mon.Species = (ushort)Species.Bulbasaur;
        mon.CurrentLevel = 5;
        mon.RefreshChecksum();
        var bytes = new byte[mon.SIZE_PARTY];
        mon.WriteDecryptedDataParty(bytes);
        // Gen 1/2 blank saves hold the mon in the party (their box layout is a list the
        // blank save does not initialise); every other generation takes a box slot.
        var (box, slot) = mon.Format <= 2 ? (-1, 0) : (0, 0);
        Assert.True(session.ImportSlot(box, slot, bytes), $"gen {mon.Format}: could not seed a mon");
        return (box, slot);
    }

    // A blank Gen 1 session is Japanese (SaveEngine.VersionFor(1) => GameVersion.BU), so its
    // mons carry 6-byte string buffers (limits 5) and the Gen 1 JP table has no Latin letters:
    // the filler must be a kana. A blank Gen 2 session is English (limits 10/7, 'A' encodes).
    [Theory]
    [InlineData(1, 5, 'ア')]
    [InlineData(2, 10, 'A')]
    [InlineData(3, 10, 'A')]
    [InlineData(4, 10, 'A')]
    [InlineData(5, 10, 'A')]
    [InlineData(6, 12, 'A')]
    [InlineData(7, 12, 'A')]
    [InlineData(8, 12, 'A')]
    [InlineData(9, 12, 'A')]
    public void EditTruncatesOverlongNicknames(int generation, int limit, char filler)
    {
        using var session = BlankSession(generation);
        var (box, slot) = SeedMon(session);
        session.ApplyEdit(box, slot, new EntityEdit(Nickname: new string(filler, limit + 5)));
        Assert.Equal(limit, session.ReadEntity(box, slot).Nickname.Length);
    }

    [Theory]
    [InlineData(1, 5, 'ア')]
    [InlineData(2, 7, 'A')]
    [InlineData(3, 7, 'A')]
    [InlineData(4, 7, 'A')]
    [InlineData(5, 7, 'A')]
    [InlineData(6, 12, 'A')]
    [InlineData(7, 12, 'A')]
    [InlineData(8, 12, 'A')]
    [InlineData(9, 12, 'A')]
    public void EditTruncatesOverlongOriginalTrainerNames(int generation, int limit, char filler)
    {
        using var session = BlankSession(generation);
        var (box, slot) = SeedMon(session);
        session.ApplyEdit(box, slot, new EntityEdit(OriginalTrainer: new string(filler, limit + 5)));
        Assert.Equal(limit, session.ReadEntity(box, slot).OriginalTrainer.Length);
    }

    [Fact]
    public void ShortNamesPassThroughUnchanged()
    {
        using var session = BlankSession(3);
        var (box, slot) = SeedMon(session);
        session.ApplyEdit(box, slot, new EntityEdit(Nickname: "Sparky"));
        Assert.Equal("Sparky", session.ReadEntity(box, slot).Nickname);
    }

    [Theory]
    [InlineData(1, LanguageID.Japanese, 5)]
    [InlineData(1, LanguageID.English, 10)]
    [InlineData(2, LanguageID.Japanese, 5)]
    [InlineData(2, LanguageID.English, 10)]
    [InlineData(3, LanguageID.English, 10)]
    [InlineData(5, LanguageID.English, 10)]
    [InlineData(9, LanguageID.English, 12)]
    public void LimitFollowsTheLanguage(int generation, LanguageID language, int expected)
    {
        PKM mon;
        if (generation is 1 or 2)
        {
            // GBPKML.Japanese derives from the OT buffer length, not the Language property:
            // a Gen 1/2 mon is Japanese exactly when its buffers are 6 bytes long.
            var jp = language == LanguageID.Japanese;
            mon = generation == 1 ? new PK1(jp) : new PK2(jp);
        }
        else
        {
            using var session = BlankSession(generation);
            mon = session.SaveFile.BlankPKM;
            mon.Language = (int)language;
        }
        var trimmed = SaveEngineSession.TrimToNicknameLimit(new string('A', 20), mon);
        Assert.Equal(expected, trimmed.Length);
    }
}
