using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    public class BuildingBlock : Operation
    {
        [System.Serializable]
        public struct Settings
        {
            public float totalBuildingHeight; // Total target height of the building structure
            public float maxFloorHeight;      // The maximum allowable height for a single floor
            public float maxWallWidth;        // The maximum allowable width for subdivided wall segments
            public float buildingInsetAmount; // The thickness/inset offset for the exterior walls
        }

        public Settings Configuration = new Settings
        {
            totalBuildingHeight = 12.0f,
            maxFloorHeight = 3.5f,
            maxWallWidth = 2.0f,
            buildingInsetAmount = 1.0f
        };

        public override List<Shape> Apply(Shape initialShape)
        {
            List<Shape> finalBuildingElements = new List<Shape>();

            // --- Phase 1: Structural Layout & Division Math ---
            // Calculate how many floors fit into the target building height boundary
            int calculatedFloorCount = Math.Max(1, (int)Math.Ceiling(Configuration.totalBuildingHeight / Configuration.maxFloorHeight));
            // Derive the clean, uniform floor-to-floor height
            float exactFloorHeight = Configuration.totalBuildingHeight / calculatedFloorCount;


            // --- Phase 2: Separate the Building Footprint into Walls and Floors ---
            // Use InsetPolygon to split the footprint into the interior Floor space and the exterior Wall ring
            InsetPolygon insetEngine = new InsetPolygon(Configuration.buildingInsetAmount, InsetPolygon.Mode.Inner); //
            List<Shape> innerFloorFootprints = insetEngine.Apply(initialShape); //

            // If the inset operations returns a valid compressed center polygon, use it as the baseline floor plate
            Shape baselineFloorPlate = initialShape;

            // Separate the outer building ring into separate 2-vertex edges
            SeparateEdges separator = new SeparateEdges();
            List<Shape> baselineLineEdges = separator.Apply(initialShape); //


            // --- Phase 3: Create Chained Wall Assemblies (Front vs Back) ---
            List<Shape> generatedWallQuads = new List<Shape>();

            for (int i = 0; i < baselineLineEdges.Count; i++)
            {
                Shape lineEdge = baselineLineEdges[i];

                // Turn the 2-vertex line segment into a 4-vertex vertical single-story wall quad
                ExtrudeEdges extruder = new ExtrudeEdges { Height = exactFloorHeight };
                List<Shape> singleWallExtrusion = extruder.Apply(lineEdge);

                if (singleWallExtrusion.Count == 0) continue;
                Shape wallQuad = singleWallExtrusion[0];

                // Arbitrarily designate the first edge loop segment (index 0) as the "Back Wall".
                // You can adapt this condition to look for specific custom data flags if preferred.
                if (i == 0)
                {
                    // Back Wall: Kept solid, not subdivided.
                    // Forward it directly to the vertical duplication array list.
                    generatedWallQuads.Add(wallQuad);
                }
                else
                {
                    // Front & Side Walls: Process through horizontal subdivisions
                    SubdivideQuad subdivider = new SubdivideQuad { MaxWidth = Configuration.maxWallWidth }; //
                    List<Shape> subdividedSegments = subdivider.Apply(wallQuad); //
                    generatedWallQuads.AddRange(subdividedSegments);
                }
            }


            // --- Phase 4: Vertical Stacking Pipeline ---
            // Define a path tracking translator straight up along the positive Y axis
            RepeatAlongPath verticalStacker = new RepeatAlongPath();
            Vector3 startPath = Vector3.Zero;
            float totalTrajectoryDistance = exactFloorHeight * (calculatedFloorCount - 1);
            Vector3 endPath = new Vector3(0, totalTrajectoryDistance, 0);

            verticalStacker.SetupPath(startPath, endPath, exactFloorHeight); //


            // 1. Stack the Walls (Contains subdivided front segments and solid back walls)
            verticalStacker.IdToStoreIndex = "wall_floor_index"; //
            List<Shape> stackedWalls = verticalStacker.ApplySet(generatedWallQuads); //
            finalBuildingElements.AddRange(stackedWalls);


            // 2. Stack the Floor Plates (Repeats on every story level up until the ceiling)
            verticalStacker.IdToStoreIndex = "floor_plate_index"; //

            // To ensure a roof ceiling cap closes the top floor, ensure dropLast is false
            verticalStacker.DropFirst = false;
            verticalStacker.DropLast = false;

            List<Shape> stackedFloors = verticalStacker.Apply(baselineFloorPlate); //
            finalBuildingElements.AddRange(stackedFloors);

            return finalBuildingElements;
        }
    }
}