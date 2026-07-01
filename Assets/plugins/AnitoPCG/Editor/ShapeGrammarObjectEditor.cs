using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    [CustomEditor(typeof(ShapeGrammarObject))]
    public class ShapeGrammarObjectEditor : UnityEditor.Editor
    {
        private ShapeGrammarObject pathComponent;
        private int selectedVertexIndex = -1;
        private Tool lastActiveTool = Tool.Move;

        // 1. ADD THIS FLAG TO TRACK DRAGGING STATE
        private bool isDraggingHandle = false;

        private void OnEnable()
        {
            pathComponent = (ShapeGrammarObject)target;
            selectedVertexIndex = -1;
            isDraggingHandle = false;

            lastActiveTool = Tools.current;
            if (Tools.current != Tool.None)
            {
                Tools.current = Tool.None;
            }
        }

        private void OnDisable()
        {
            if (Tools.current == Tool.None)
            {
                Tools.current = lastActiveTool != Tool.None ? lastActiveTool : Tool.Move;
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (Tools.current != Tool.None)
            {
                lastActiveTool = Tools.current;
                Tools.current = Tool.None;
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Regenerate Shape Grammar", GUILayout.Height(35)))
            {
                RegenerateScene();
            }
        }

        private void RegenerateScene()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            

            List<Tree> allTreesInScene = new();
            List<List<System.Numerics.Vector3>> treeSeeds = new();

            foreach (var shapeGrammarObject in FindObjectsByType<ShapeGrammarObject>(FindObjectsSortMode.None))
            {
                allTreesInScene.Add(shapeGrammarObject.GetTree());
                treeSeeds.Add(shapeGrammarObject.InitializeEvaluation());
            }

            Gbe.ShapeGrammar.SpatialGraphRegistry.GenerateScene(allTreesInScene, treeSeeds);

            SceneView.RepaintAll();

            stopwatch.Stop();

            UnityEngine.Debug.Log($"Function took: {stopwatch.ElapsedMilliseconds} ms");
        }

        private void OnSceneGUI()
        {
            if (pathComponent == null) return;

            // 2. DETECT HANDLE RELEASE: If no handle is active but we were dragging, it means the user just let go
            if (GUIUtility.hotControl == 0 && isDraggingHandle)
            {
                isDraggingHandle = false;

                // Trigger the generation!
                RegenerateScene();
            }

            // =========================================================
            // 1. DRAW AXIOM PATH / INPUT VERTICES
            // =========================================================
            if (pathComponent.showAxiomLines && pathComponent.inputVertices != null)
            {
                Handles.color = Color.white;
                for (int i = 0; i < pathComponent.inputVertices.Count; i++)
                {
                    // Draw Vertex Handles
                    Vector3 worldPos = pathComponent.transform.TransformPoint(pathComponent.inputVertices[i]);
                    float size = HandleUtility.GetHandleSize(worldPos) * 0.1f;

                    if (Handles.Button(worldPos, Quaternion.identity, size, size, Handles.SphereHandleCap))
                    {
                        selectedVertexIndex = i;
                    }

                    // Draw connecting lines
                    if (i > 0)
                    {
                        Vector3 prevPos = pathComponent.transform.TransformPoint(pathComponent.inputVertices[i - 1]);
                        Handles.DrawLine(prevPos, worldPos, 2.0f);
                    }
                    else if (pathComponent.inputVertices.Count > 2)
                    {
                        // Close the polygon visually
                        Vector3 lastPos = pathComponent.transform.TransformPoint(pathComponent.inputVertices[pathComponent.inputVertices.Count - 1]);
                        Handles.DrawLine(lastPos, worldPos, 2.0f);
                    }
                }

                // Handle vertex movement if one is selected
                if (selectedVertexIndex >= 0 && selectedVertexIndex < pathComponent.inputVertices.Count)
                {
                    Vector3 worldPos = pathComponent.transform.TransformPoint(pathComponent.inputVertices[selectedVertexIndex]);

                    EditorGUI.BeginChangeCheck();

                    // Draw the standard movement gizmo
                    // Draws a flat square handle that only allows dragging along the X/Z floor plane
                    float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.3f;
                    Vector3 newWorldPos = Handles.Slider2D(
                        worldPos,
                        Vector3.up,       // Up normal defines the flat floor plane
                        Vector3.right,    // Slide direction 1
                        Vector3.forward,  // Slide direction 2
                        handleSize,
                        Handles.RectangleHandleCap,
                        Vector2.zero
                    );

                    if (EditorGUI.EndChangeCheck())
                    {
                        isDraggingHandle = true;

                        Undo.RecordObject(pathComponent, "Move Path Vertex");

                        // 1. Convert the newly dragged world position back to local space
                        Vector3 newLocalPos = pathComponent.transform.InverseTransformPoint(newWorldPos);

                        // 2. CONSTRAIN Y-AXIS: Overwrite the new Y with the original Y so it cannot change
                        newLocalPos.y = pathComponent.inputVertices[selectedVertexIndex].y;

                        // 3. Save the constrained position back into the array
                        pathComponent.inputVertices[selectedVertexIndex] = newLocalPos;
                    }
                }
            }

            // =========================================================
            // 2. DRAW GENERATED SHAPE WIREFRAMES
            // =========================================================
            if (pathComponent.showWireframes && pathComponent.generatedTriangles != null)
            {
                Handles.color = Color.cyan;

                foreach (var tri in pathComponent.generatedTriangles)
                {
                    if (tri == null || tri.Vertices == null || tri.Vertices.Count < 3) continue;

                    List<UnityEngine.Vector3> vs = new List<UnityEngine.Vector3>();
                    UnityEngine.Vector3 first = new UnityEngine.Vector3(tri.Vertices[0].X, tri.Vertices[0].Y, tri.Vertices[0].Z);
                    vs.Add(first);

                    UnityEngine.Vector3 v1 = first;
                    for (int i = 1; i < tri.Vertices.Count; i++)
                    {
                        UnityEngine.Vector3 v0 = new UnityEngine.Vector3(tri.Vertices[i - 1].X, tri.Vertices[i - 1].Y, tri.Vertices[i - 1].Z);
                        v1 = new UnityEngine.Vector3(tri.Vertices[i].X, tri.Vertices[i].Y, tri.Vertices[i].Z);
                        Handles.DrawLine(v0, v1, 1.0f);
                        vs.Add(v1);
                    }

                    Handles.DrawLine(v1, first, 1.0f);

                    // Optional transparent fill overlay
                    Handles.color = new UnityEngine.Color(0, 1, 1, 0.04f);
                    Handles.DrawAAConvexPolygon(vs.ToArray());
                    Handles.color = UnityEngine.Color.cyan; // reset
                }
            }
        }

        // Isolated deletion logic method to share cleanly between UI panels
        private void DeleteVertex(int index)
        {
            if (pathComponent == null || index < 0 || index >= pathComponent.inputVertices.Count) return;

            Undo.RecordObject(pathComponent, "Delete Path Vertex");
            pathComponent.inputVertices.RemoveAt(index);

            // Bounds protection checking for selected state target alignment fallback tracking
            if (selectedVertexIndex == index)
            {
                selectedVertexIndex = -1;
            }
            else if (selectedVertexIndex > index)
            {
                selectedVertexIndex--;
            }

            EditorUtility.SetDirty(pathComponent);
            SceneView.RepaintAll();
        }
    }
}