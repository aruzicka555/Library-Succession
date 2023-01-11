using Landis.Core;
using Landis.SpatialModeling;

namespace Landis.Library.Succession
{
    /// <summary>
    /// Seeding algorithm: determines if a species seeds a site.
    /// <param name="species"></param>
    /// <param name="site">Site that may be seeded.</param>
    /// <returns>true if the species seeds the site.</returns>
    public delegate void SeedingAlgorithm(ISpecies   species,
                                          ActiveSite site, out bool established, out double seedlingProportion, ThreadSafeRandom randomGen = null);

    public delegate void DoesSpeciesSeedSite(ISpecies species,
                                        ActiveSite site, out bool established, out double seedlingProportion, ThreadSafeRandom randomGen = null);
}
