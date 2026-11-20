using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

[CollectionDefinition(EmulatorCatalogCollection.Name)]
public sealed class EmulatorCatalogCollection
{
    public const string Name = "Emulator catalog state";
}

internal sealed class EmbeddedCoreAvailabilityScope : IDisposable
{
    private readonly bool _previousMgba;
    private readonly bool _previousMelonDs;

    public EmbeddedCoreAvailabilityScope(bool mgba, bool melonDs)
    {
        _previousMgba = EmulatorCatalog.EmbeddedCoreAvailable;
        _previousMelonDs = EmulatorCatalog.EmbeddedDsCoreAvailable;
        EmulatorCatalog.EmbeddedCoreAvailable = mgba;
        EmulatorCatalog.EmbeddedDsCoreAvailable = melonDs;
    }

    public void Dispose()
    {
        EmulatorCatalog.EmbeddedCoreAvailable = _previousMgba;
        EmulatorCatalog.EmbeddedDsCoreAvailable = _previousMelonDs;
    }
}
