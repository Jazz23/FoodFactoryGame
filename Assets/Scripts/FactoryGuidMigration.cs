// Provides canonical GUID parsing and repeatable mappings for legacy factory identities.
using System;
using System.Security.Cryptography;
using System.Text;

public static class FactoryGuidMigration
{
    public static Guid ForBuilding(uint legacyBuildingId)
    {
        return Create($"factory/building/{legacyBuildingId}");
    }

    public static Guid ForFloor(uint legacyBuildingId, int floorIndex)
    {
        return Create($"factory/floor/{legacyBuildingId}/{floorIndex}");
    }

    public static Guid ForEntity(
        uint legacyBuildingId,
        int floorIndex,
        uint legacyEntityId)
    {
        return Create($"factory/entity/{legacyBuildingId}/{floorIndex}/{legacyEntityId}");
    }

    public static Guid ForInsideTestBuilding()
    {
        return Create("nai/inside-test/building");
    }

    public static Guid ForInsideTestFloor()
    {
        return Create("nai/inside-test/floor/0");
    }

    public static Guid ForConnection(FactoryEntityEndpoint source, FactoryEntityEndpoint destination)
    {
        return Create(
            $"factory/connection/{source.BuildingGuid:D}/{source.FloorGuid:D}/{source.EntityGuid:D}/"
            + $"{destination.BuildingGuid:D}/{destination.FloorGuid:D}/{destination.EntityGuid:D}");
    }

    public static Guid Create(string stableKey)
    {
        if (string.IsNullOrWhiteSpace(stableKey))
        {
            throw new ArgumentException("A stable GUID key is required.", nameof(stableKey));
        }

        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(stableKey.Trim()));
        var bytes = new byte[16];
        Buffer.BlockCopy(hash, 0, bytes, 0, bytes.Length);

        // Mark the deterministic value as a standards-compliant version 5 UUID.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    public static string ToCanonical(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Guid.Empty is not a persisted identity.", nameof(value));
        }

        return value.ToString("D");
    }

    public static bool TryParseCanonical(string value, out Guid result)
    {
        return Guid.TryParseExact(value, "D", out result) && result != Guid.Empty;
    }
}
