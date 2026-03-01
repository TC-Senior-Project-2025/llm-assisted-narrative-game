using System;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using Newtonsoft.Json;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.World.Map
{
    public class MapConnections : MonoBehaviour
    {
        private const string FileName = "MapConnections";
        [SerializeField] private SerializedDictionary<int, int[]> connections;
        [SerializeField] private MapRenderer mapRenderer;

        [Header("Test")]
        [SerializeField] private int testSourceId;
        [SerializeField] private int testDestinationId;
        [SerializeField] private List<int> path;

        private void Start()
        {
            LoadConnections();
        }

        private void OnDrawGizmos()
        {
            if (path == null || path.Count < 2) return;

            Gizmos.color = Color.red;
            for (int i = 0; i < path.Count - 1; i++)
            {
                var start = mapRenderer.GetProvinceCenter(path[i]);
                var end = mapRenderer.GetProvinceCenter(path[i + 1]);
                Gizmos.DrawLine(start, end);
                Gizmos.DrawSphere(start, 0.1f);
            }
            Gizmos.DrawSphere(mapRenderer.GetProvinceCenter(path[^1]), 0.1f);
        }

        public int[] GetNeighbors(int provinceId)
        {
            return connections[provinceId];
        }

        public bool IsNeighborOf(int provinceId, int neighborId)
        {
            return connections[provinceId].Contains(neighborId);
        }

        public void LoadConnections()
        {
            var json = Resources.Load<TextAsset>(FileName);
            if (json is not null)
            {
                connections = JsonConvert.DeserializeObject<SerializedDictionary<int, int[]>>(json.text);
                return;
            }
            ;
            Debug.LogError($"JSON not found: {FileName}");
        }

        public List<int> Pathfind(int sourceProvinceId, int destinationProvinceId)
        {
            if (connections == null || !connections.ContainsKey(sourceProvinceId) || !connections.ContainsKey(destinationProvinceId))
            {
                return new List<int>();
            }

            var openSet = new List<int> { sourceProvinceId };
            var cameFrom = new Dictionary<int, int>();

            var gScore = new Dictionary<int, float>();
            gScore[sourceProvinceId] = 0;

            var fScore = new Dictionary<int, float>();
            fScore[sourceProvinceId] = Heuristic(sourceProvinceId, destinationProvinceId);

            while (openSet.Count > 0)
            {
                var current = openSet.OrderBy(id => fScore.GetValueOrDefault(id, float.PositiveInfinity)).First();

                if (current == destinationProvinceId)
                {
                    return ReconstructPath(cameFrom, current);
                }

                openSet.Remove(current);

                if (!connections.TryGetValue(current, out var neighbors)) continue;

                foreach (var neighbor in neighbors)
                {
                    var tentativeGScore = gScore[current] + Vector3.Distance(mapRenderer.GetProvinceCenter(current), mapRenderer.GetProvinceCenter(neighbor));

                    if (tentativeGScore < gScore.GetValueOrDefault(neighbor, float.PositiveInfinity))
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentativeGScore;
                        fScore[neighbor] = gScore[neighbor] + Heuristic(neighbor, destinationProvinceId);

                        if (!openSet.Contains(neighbor))
                        {
                            openSet.Add(neighbor);
                        }
                    }
                }
            }

            return new List<int>();
        }

        public void TestPathfind()
        {
            path = Pathfind(testSourceId, testDestinationId);
        }

        public void ClearPath()
        {
            path = null;
        }

        private float Heuristic(int a, int b)
        {
            return Vector3.Distance(mapRenderer.GetProvinceCenter(a), mapRenderer.GetProvinceCenter(b));
        }

        private List<int> ReconstructPath(Dictionary<int, int> cameFrom, int current)
        {
            var path = new List<int> { current };
            while (cameFrom.ContainsKey(current))
            {
                current = cameFrom[current];
                path.Insert(0, current);
            }
            return path;
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(MapConnections))]
    public class MapConnectionsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var mapConnections = (MapConnections)target;
            if (GUILayout.Button("Load Connections"))
            {
                mapConnections.LoadConnections();
            }

            if (GUILayout.Button("Test Pathfind"))
            {
                mapConnections.TestPathfind();
                EditorUtility.SetDirty(mapConnections);
            }

            if (GUILayout.Button("Clear Gizmos"))
            {
                mapConnections.ClearPath();
                EditorUtility.SetDirty(mapConnections);
            }
        }
    }
#endif
}