using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    public class Shape
    {
        public List<Vector3> Vertices { get; set; } = new List<Vector3>();
        public Dictionary<string, List<int>> Data { get; set; } = new Dictionary<string, List<int>>();

        // Constructors
        public Shape()
        {
        }

        public Shape(List<Vector3> vertices)
        {
            Vertices = vertices;
        }

        public int GetDataSingle(string id)
        {
            if (Data.TryGetValue(id, out var list) && list.Count > 0)
            {
                return list[0];
            }
            return -1;
        }

        public void SetDataSingle(string id, int value)
        {
            Data[id] = new List<int> { value };
        }

        public List<Tuple<Vector3, Vector3>> GetLines(out Vector3 cross)
        {
            var lines = new List<Tuple<Vector3, Vector3>>();
            cross = new Vector3(0, 1, 0);

            for (int i = 1; i < Vertices.Count; i++)
            {
                lines.Add(Tuple.Create(Vertices[i], Vertices[i - 1]));
            }

            if (lines.Count == 0)
            {
                return lines;
            }

            if (Vertices.Count > 2)
            {
                Vector3 edge1 = lines[0].Item2 - lines[0].Item1;
                Vector3 edge2 = lines[1].Item2 - lines[1].Item1;
                cross = Vector3.Normalize(Vector3.Cross(edge1, edge2));

                lines.Add(Tuple.Create(Vertices[0], Vertices[Vertices.Count - 1]));
            }
            else
            {
                Vector3 edge1 = lines[0].Item2 - lines[0].Item1;
                cross = Vector3.Normalize(Vector3.Cross(edge1, new Vector3(1, 0, 0)));
            }

            return lines;
        }
    }
}