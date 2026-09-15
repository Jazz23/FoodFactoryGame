// Transfers buffered items between complementary elevator parts on adjacent factory floors.
using System.Collections.Generic;
using UnityEngine;

public static class FactoryElevatorTransfer
{
    public static int TransferPairedElevators(IEnumerable<OutsideTestFloorRecord> floors)
    {
        var floorLookup = new Dictionary<OutsideTestFloorKey, OutsideTestFloorRecord>();
        foreach (var floor in floors)
        {
            if (floor is not null)
            {
                floorLookup[new OutsideTestFloorKey(floor.BuildingInstanceId, floor.FloorIndex)] = floor;
            }
        }

        var transferred = 0;
        foreach (var lowerFloor in floorLookup.Values)
        {
            if (!floorLookup.TryGetValue(
                    new OutsideTestFloorKey(lowerFloor.BuildingInstanceId, lowerFloor.FloorIndex + 1),
                    out var upperFloor))
            {
                continue;
            }

            foreach (var lowerEntity in lowerFloor.Entities)
            {
                if (!lowerEntity.IsElevator
                    || lowerEntity.DefinitionId != FactoryEntityDefinitions.ElevatorBottomDefinitionId)
                {
                    continue;
                }

                foreach (var upperEntity in upperFloor.Entities)
                {
                    if (!upperEntity.IsElevator
                        || upperEntity.DefinitionId != FactoryEntityDefinitions.ElevatorTopDefinitionId
                        || Vector2Int.FloorToInt(upperEntity.LogicalPosition)
                            != Vector2Int.FloorToInt(lowerEntity.LogicalPosition))
                    {
                        continue;
                    }

                    if (lowerEntity.TryTransferElevatorInputTo(upperEntity))
                    {
                        transferred++;
                    }

                    if (upperEntity.TryTransferElevatorInputTo(lowerEntity))
                    {
                        transferred++;
                    }

                    break;
                }
            }
        }

        return transferred;
    }
}
