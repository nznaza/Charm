﻿using System.Collections.Concurrent;

namespace Tiger.Schema;

public class Hash64Map : Strategy.StrategistSingleton<Hash64Map>
{
    private ConcurrentDictionary<ulong, uint> _map = new();

    public Hash64Map(TigerStrategy strategy) : base(strategy)
    {
        // Pre-BL has no Hash64s
        if (_strategy >= TigerStrategy.DESTINY2_WITCHQUEEN_6307)
        {
            Initialise();
        }
    }

    /// <summary>
    /// We assume it exists, otherwise will throw an exception
    /// </summary>
    public uint GetHash32(ulong tag64)
    {
        return _map[tag64];
    }

    private void Initialise()
    {
        List<ushort> packageIds = PackageResourcer.Get().GetAllPackageIds();
        Parallel.ForEach(packageIds, packageId =>
        {
            IPackage package = PackageResourcer.Get().GetPackage(packageId);
            foreach (Hash64Definition definition in package.GetHash64List())
            {
                _map.TryAdd(definition.Hash64, definition.Hash32);
            }
        });
    }
}
