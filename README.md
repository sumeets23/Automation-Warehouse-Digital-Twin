# Automation Warehouse Digital Twin


> This repository contains warehouse automation scripts only. Dashboard and NVIDIA Omniverse components are not included.

## Features

- Random valid package assignment at every Play start
- One-to-one robot/package ownership until conveyor delivery
- Complete Unity NavMesh path validation
- Robot selection using NavMesh path length
- Shelf approach, alignment, lift, pickup, undock, and lowering
- FIFO queueing for two conveyor destinations
- Battery-aware charging queue
- Reverse-undocking after charging
- Deterministic avoidance priorities
- Route recovery without replacing active packages
- Pallet-stack conveyor movement
- Rack IDs and package-to-rack metadata

## Requirements

- Unity 2022.3 LTS
- AI Navigation package
- Baked NavMesh covering aisles, conveyors, homes, and charger approaches
- Shelf and cardbox colliders
- Shelf objects tagged `Shelf`
- Pallet objects tagged `Pallet`


## Scene Setup

1. Add `RackTwin` to rack roots and set stable rack IDs.
2. Tag collision objects as `Shelf` and pallets as `Pallet`.
3. Run the pallet combiner and cardbox ID tool.
4. Run rack ID assignment and shelf obstacle configuration.
5. Bake the NavMesh.
6. Add robots to `AutomationWareHouseManager.robotAssignments`.
7. Assign two conveyor destinations and the destination waiting point.
8. Assign the charging station to each `RobotTwin`.
9. Enter Play mode.

## Runtime Invariants

- One robot owns one active package task.
- One package stack is assigned to one robot.
- New work is assigned only after conveyor delivery.
- A charged robot reverses clear before becoming available.
- Navigation retries preserve the current package.
- Loaded robots wait only for conveyors.
- Empty low-battery robots wait only for charging.



## Notes

Navigation behavior depends on the baked NavMesh, collider geometry, charger approach, conveyor transforms, model pivots, and aisle clearance. Re-bake navigation after changing warehouse geometry.
