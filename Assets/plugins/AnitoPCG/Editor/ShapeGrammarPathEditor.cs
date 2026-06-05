using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Gbe.ShapeGrammar
{
    [CustomEditor(typeof(ShapeGrammarPath))]
    public class ShapeGrammarPathEditor : Editor
    {
        private ShapeGrammarPath pathComponent;
        private int selectedVertexIndex = -1;
        private Tool lastActiveTool = Tool.Move;

        private void OnEnable()
        {
            pathComponent = (ShapeGrammarPath)target;
            selectedVertexIndex = -1;

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

            if (GUILayout.Button("Append Vertex to End"))
            {
                Undo.RecordObject(pathComponent, "Add Path Vertex");
                pathComponent.inputVertices.Add(UnityEngine.Vector3.zero);
                selectedVertexIndex = pathComponent.inputVertices.Count - 1;
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Regenerate Shape Grammar", GUILayout.Height(35)))
            {
                Undo.RecordObject(pathComponent, "Execute Shape Grammar");
                pathComponent.ExecuteGrammarChain();
                SceneView.RepaintAll();
            }
        }

        private void OnSceneGUI()
        {
            if (pathComponent == null || pathComponent.inputVertices.Count == 0) return;

            if (Tools.current != Tool.None)
            {
                lastActiveTool = Tools.current;
                Tools.current = Tool.None;
            }

            Transform transform = pathComponent.transform;
            int vertexCount = pathComponent.inputVertices.Count;
            int insertIndex = -1;
            UnityEngine.Vector3 insertPosition = UnityEngine.Vector3.zero;

            // --- 1. Draw Path Outline & Midpoint Add Buttons ---
            for (int i = 0; i < vertexCount; i++)
            {
                int nextIndex = (i + 1) % vertexCount;
                UnityEngine.Vector3 worldPos = transform.TransformPoint(pathComponent.inputVertices[i]);
                UnityEngine.Vector3 nextWorldPos = transform.TransformPoint(pathComponent.inputVertices[nextIndex]);

                Handles.color = UnityEngine.Color.yellow;
                Handles.DrawLine(worldPos, nextWorldPos, 2.0f);

                UnityEngine.Vector3 midWorldPos = UnityEngine.Vector3.Lerp(worldPos, nextWorldPos, 0.5f);
                float midHandleSize = HandleUtility.GetHandleSize(midWorldPos) * 0.08f;

                Handles.color = UnityEngine.Color.green;
                if (Handles.Button(midWorldPos, transform.rotation, midHandleSize, midHandleSize, Handles.RectangleHandleCap))
                {
                    insertPosition = transform.InverseTransformPoint(midWorldPos);
                    insertPosition.y = 0f;
                    insertIndex = nextIndex;
                }
            }

            if (insertIndex != -1)
            {
                Undo.RecordObject(pathComponent, "Insert Midpoint Vertex");
                pathComponent.inputVertices.Insert(insertIndex, insertPosition);
                selectedVertexIndex = insertIndex;
                EditorUtility.SetDirty(pathComponent);
                Repaint();
            }

            // --- 2. Draw Main Corner Vertex Buttons & Handle Context Right-Clicks ---
            for (int i = 0; i < pathComponent.inputVertices.Count; i++)
            {
                UnityEngine.Vector3 worldPos = transform.TransformPoint(pathComponent.inputVertices[i]);
                float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.14f;

                Handles.color = (selectedVertexIndex == i) ? UnityEngine.Color.cyan : UnityEngine.Color.white;

                // Unique ID for each button control instance to track events accurately
                int controlID = GUIUtility.GetControlID(FocusType.Passive);

                if (Handles.Button(worldPos, transform.rotation, handleSize, handleSize, Handles.SphereHandleCap))
                {
                    selectedVertexIndex = i;
                    Repaint();
                }

                // Intercept mouse actions to create a custom right-click context menu over vertices
                Event currentEvent = Event.current;
                if (currentEvent.type == EventType.MouseDown && currentEvent.button == 1) // 1 is Right Click
                {
                    // Check if the mouse click point intersects our vertex disc handle radius
                    if (HandleUtility.DistanceToCircle(worldPos, handleSize) < 5f)
                    {
                        int indexToDelete = i; // Cache index state for the contextual callback loop
                        GenericMenu menu = new GenericMenu();
                        menu.AddItem(new GUIContent($"Delete Vertex V{i}"), false, () => DeleteVertex(indexToDelete));
                        menu.ShowAsContext();
                        currentEvent.Use(); // Consume event framework loop cycle safely
                    }
                }

                Handles.Label(worldPos + transform.up * (handleSize * 1.2f), $"V{i}", EditorStyles.miniLabel);
            }

            // --- 3. Render Position Gizmo ONLY for Selected Individual Vertex ---
            if (selectedVertexIndex >= 0 && selectedVertexIndex < pathComponent.inputVertices.Count)
            {
                UnityEngine.Vector3 targetWorldPos = transform.TransformPoint(pathComponent.inputVertices[selectedVertexIndex]);

                EditorGUI.BeginChangeCheck();
                UnityEngine.Vector3 newWorldPos = Handles.PositionHandle(targetWorldPos, transform.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(pathComponent, "Move Selected Handle");
                    UnityEngine.Vector3 localPos = transform.InverseTransformPoint(newWorldPos);

                    pathComponent.inputVertices[selectedVertexIndex] = new UnityEngine.Vector3(localPos.x, 0, localPos.z);
                    EditorUtility.SetDirty(pathComponent);
                }
            }

            // --- 4. Draw Triangles Layer ---
            if (pathComponent.generatedTriangles == null || pathComponent.generatedTriangles.Count == 0) return;

            Handles.color = UnityEngine.Color.cyan;
            foreach (var tri in pathComponent.generatedTriangles)
            {
                if (tri.Vertices.Count < 3) continue;

                UnityEngine.Vector3 v0 = new UnityEngine.Vector3(tri.Vertices[0].X, tri.Vertices[0].Y, tri.Vertices[0].Z);
                UnityEngine.Vector3 v1 = new UnityEngine.Vector3(tri.Vertices[1].X, tri.Vertices[1].Y, tri.Vertices[1].Z);
                UnityEngine.Vector3 v2 = new UnityEngine.Vector3(tri.Vertices[2].X, tri.Vertices[2].Y, tri.Vertices[2].Z);

                Handles.DrawLine(v0, v1, 1.0f);
                Handles.DrawLine(v1, v2, 1.0f);
                Handles.DrawLine(v2, v0, 1.0f);

                Handles.color = new UnityEngine.Color(0, 1, 1, 0.04f);
                Handles.DrawAAConvexPolygon(v0, v1, v2);
                Handles.color = UnityEngine.Color.cyan;
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