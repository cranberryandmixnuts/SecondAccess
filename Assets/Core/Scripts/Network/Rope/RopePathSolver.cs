using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct RopePathOptions
{
    public float SurfaceOffset { get; }
    public int CircleSegments { get; }
    public int MaxGraphNodes { get; }

    public RopePathOptions(float surfaceOffset, int circleSegments, int maxGraphNodes)
    {
        SurfaceOffset = Mathf.Max(0f, surfaceOffset);
        CircleSegments = Mathf.Max(12, circleSegments);
        MaxGraphNodes = Mathf.Max(8, maxGraphNodes);
    }
}

public static class RopePathSolver
{
    private const float Epsilon = 0.0001f;
    private const float SqrEpsilon = Epsilon * Epsilon;

    private sealed class ObstaclePolygon
    {
        public readonly List<Vector2> Points = new();
        public Vector2 Center;
        public bool IsCircle;
    }

    private readonly struct GraphNode
    {
        public Vector2 Point { get; }
        public int ObstacleIndex { get; }
        public int VertexIndex { get; }

        public GraphNode(Vector2 point, int obstacleIndex, int vertexIndex)
        {
            Point = point;
            ObstacleIndex = obstacleIndex;
            VertexIndex = vertexIndex;
        }
    }

    public static bool TrySolve(Vector3 start, Vector3 end, RopePathOptions options, List<Vector3> result)
    {
        result.Clear();

        Vector2 start2 = ToPlanar(start);
        Vector2 end2 = ToPlanar(end);

        if ((end2 - start2).sqrMagnitude <= SqrEpsilon)
        {
            result.Add(start);
            result.Add(end);
            return true;
        }

        List<ObstaclePolygon> obstacles = BuildObstaclePolygons(options);
        RemovePolygonsContainingEndpoint(obstacles, start2, end2);

        List<GraphNode> nodes = BuildGraphNodes(start2, end2, obstacles, options.MaxGraphNodes);

        if (nodes.Count < 2)
            return false;

        float[,] distances = BuildVisibilityGraph(nodes, obstacles);

        if (!TryFindShortestPath(distances, out int[] previous))
            return false;

        List<int> path = BuildNodePath(previous);

        if (path.Count < 2)
            return false;

        BuildWorldPath(nodes, path, start, end, result);
        return true;
    }

    public static float GetLength(IReadOnlyList<Vector3> path)
    {
        float length = 0f;

        for (int i = 1; i < path.Count; i++)
            length += Vector3.Distance(path[i - 1], path[i]);

        return length;
    }

    private static List<ObstaclePolygon> BuildObstaclePolygons(RopePathOptions options)
    {
        List<ObstaclePolygon> polygons = new();
        IReadOnlyList<RopeObstacle> obstacles = RopeObstacle.Registered;

        for (int i = 0; i < obstacles.Count; i++)
        {
            RopeObstacle obstacle = obstacles[i];

            if (obstacle == null || !obstacle.AffectsRope)
                continue;

            IReadOnlyList<Collider> colliders = obstacle.Colliders;

            for (int j = 0; j < colliders.Count; j++)
            {
                Collider targetCollider = colliders[j];

                if (!CanUseCollider(targetCollider))
                    continue;

                if (TryBuildPolygon(targetCollider, options, out ObstaclePolygon polygon))
                    polygons.Add(polygon);
            }
        }

        return polygons;
    }

    private static bool CanUseCollider(Collider targetCollider)
    {
        if (targetCollider == null)
            return false;

        if (!targetCollider.enabled)
            return false;

        if (targetCollider.isTrigger)
            return false;

        return targetCollider.gameObject.activeInHierarchy;
    }

    private static bool TryBuildPolygon(Collider targetCollider, RopePathOptions options, out ObstaclePolygon polygon)
    {
        if (targetCollider is BoxCollider boxCollider)
            return TryBuildBoxPolygon(boxCollider, options.SurfaceOffset, out polygon);

        if (targetCollider is SphereCollider sphereCollider)
            return TryBuildSpherePolygon(sphereCollider, options, out polygon);

        if (targetCollider is CapsuleCollider capsuleCollider && capsuleCollider.direction == 1)
            return TryBuildCapsuleCirclePolygon(capsuleCollider, options, out polygon);

        return TryBuildBoundsPolygon(targetCollider.bounds, options.SurfaceOffset, out polygon);
    }

    private static bool TryBuildBoxPolygon(BoxCollider boxCollider, float surfaceOffset, out ObstaclePolygon polygon)
    {
        polygon = new ObstaclePolygon();

        Transform boxTransform = boxCollider.transform;
        Vector3 center = boxTransform.TransformPoint(boxCollider.center);
        Vector3 rightVector = boxTransform.TransformVector(Vector3.right * boxCollider.size.x * 0.5f);
        Vector3 forwardVector = boxTransform.TransformVector(Vector3.forward * boxCollider.size.z * 0.5f);

        Vector2 center2 = ToPlanar(center);
        Vector2 right = ToPlanarDirection(rightVector);
        Vector2 forward = ToPlanarDirection(forwardVector);
        float rightExtent = ToPlanar(rightVector).magnitude + surfaceOffset;
        float forwardExtent = ToPlanar(forwardVector).magnitude + surfaceOffset;

        if (right.sqrMagnitude <= SqrEpsilon || forward.sqrMagnitude <= SqrEpsilon)
            return false;

        polygon.Center = center2;
        polygon.Points.Add(center2 - right * rightExtent - forward * forwardExtent);
        polygon.Points.Add(center2 + right * rightExtent - forward * forwardExtent);
        polygon.Points.Add(center2 + right * rightExtent + forward * forwardExtent);
        polygon.Points.Add(center2 - right * rightExtent + forward * forwardExtent);
        EnsureCounterClockwise(polygon.Points);
        return true;
    }

    private static bool TryBuildSpherePolygon(SphereCollider sphereCollider, RopePathOptions options, out ObstaclePolygon polygon)
    {
        Vector3 center = sphereCollider.transform.TransformPoint(sphereCollider.center);
        Vector3 scale = sphereCollider.transform.lossyScale;
        float radius = sphereCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) + options.SurfaceOffset;
        return TryBuildCirclePolygon(ToPlanar(center), radius, options.CircleSegments, out polygon);
    }

    private static bool TryBuildCapsuleCirclePolygon(CapsuleCollider capsuleCollider, RopePathOptions options, out ObstaclePolygon polygon)
    {
        Vector3 center = capsuleCollider.transform.TransformPoint(capsuleCollider.center);
        Vector3 scale = capsuleCollider.transform.lossyScale;
        float radius = capsuleCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) + options.SurfaceOffset;
        return TryBuildCirclePolygon(ToPlanar(center), radius, options.CircleSegments, out polygon);
    }

    private static bool TryBuildCirclePolygon(Vector2 center, float radius, int segments, out ObstaclePolygon polygon)
    {
        polygon = new ObstaclePolygon
        {
            Center = center,
            IsCircle = true
        };

        if (radius <= Epsilon)
            return false;

        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            polygon.Points.Add(point);
        }

        return true;
    }

    private static bool TryBuildBoundsPolygon(Bounds bounds, float surfaceOffset, out ObstaclePolygon polygon)
    {
        polygon = new ObstaclePolygon();

        Vector2 center = ToPlanar(bounds.center);
        float xExtent = bounds.extents.x + surfaceOffset;
        float zExtent = bounds.extents.z + surfaceOffset;

        if (xExtent <= Epsilon || zExtent <= Epsilon)
            return false;

        polygon.Center = center;
        polygon.Points.Add(center + new Vector2(-xExtent, -zExtent));
        polygon.Points.Add(center + new Vector2(xExtent, -zExtent));
        polygon.Points.Add(center + new Vector2(xExtent, zExtent));
        polygon.Points.Add(center + new Vector2(-xExtent, zExtent));
        return true;
    }

    private static void RemovePolygonsContainingEndpoint(List<ObstaclePolygon> polygons, Vector2 start, Vector2 end)
    {
        for (int i = polygons.Count - 1; i >= 0; i--)
        {
            if (IsInsideOrOnPolygon(start, polygons[i].Points) || IsInsideOrOnPolygon(end, polygons[i].Points))
                polygons.RemoveAt(i);
        }
    }

    private static List<GraphNode> BuildGraphNodes(Vector2 start, Vector2 end, List<ObstaclePolygon> obstacles, int maxGraphNodes)
    {
        List<GraphNode> nodes = new()
        {
            new GraphNode(start, -1, -1),
            new GraphNode(end, -1, -1)
        };

        for (int obstacleIndex = 0; obstacleIndex < obstacles.Count; obstacleIndex++)
        {
            ObstaclePolygon obstacle = obstacles[obstacleIndex];

            for (int vertexIndex = 0; vertexIndex < obstacle.Points.Count; vertexIndex++)
            {
                if (nodes.Count >= maxGraphNodes)
                    return nodes;

                nodes.Add(new GraphNode(obstacle.Points[vertexIndex], obstacleIndex, vertexIndex));
            }
        }

        return nodes;
    }

    private static float[,] BuildVisibilityGraph(List<GraphNode> nodes, List<ObstaclePolygon> obstacles)
    {
        int count = nodes.Count;
        float[,] distances = new float[count, count];

        for (int i = 0; i < count; i++)
        {
            for (int j = 0; j < count; j++)
                distances[i, j] = float.PositiveInfinity;
        }

        for (int i = 0; i < count; i++)
        {
            distances[i, i] = 0f;

            for (int j = i + 1; j < count; j++)
            {
                if (!IsVisible(nodes[i], nodes[j], obstacles))
                    continue;

                float distance = Vector2.Distance(nodes[i].Point, nodes[j].Point);
                distances[i, j] = distance;
                distances[j, i] = distance;
            }
        }

        return distances;
    }

    private static bool IsVisible(GraphNode a, GraphNode b, List<ObstaclePolygon> obstacles)
    {
        if ((b.Point - a.Point).sqrMagnitude <= SqrEpsilon)
            return false;

        for (int i = 0; i < obstacles.Count; i++)
        {
            ObstaclePolygon polygon = obstacles[i];

            if (a.ObstacleIndex == i && b.ObstacleIndex == i)
            {
                if (AreAdjacentVertices(a.VertexIndex, b.VertexIndex, polygon.Points.Count))
                    continue;

                return false;
            }

            if (IsSegmentBlockedByPolygon(a, b, polygon, i))
                return false;
        }

        return true;
    }

    private static bool IsSegmentBlockedByPolygon(GraphNode a, GraphNode b, ObstaclePolygon polygon, int polygonIndex)
    {
        Vector2 midpoint = (a.Point + b.Point) * 0.5f;

        if (IsStrictlyInsidePolygon(midpoint, polygon.Points))
            return true;

        int count = polygon.Points.Count;

        for (int i = 0; i < count; i++)
        {
            Vector2 edgeStart = polygon.Points[i];
            Vector2 edgeEnd = polygon.Points[(i + 1) % count];

            if (!TryGetSegmentIntersection(a.Point, b.Point, edgeStart, edgeEnd, out Vector2 intersection, out bool collinearOverlap))
                continue;

            if (collinearOverlap)
                return true;

            if (IsAllowedEndpointTouch(a, b, intersection, polygonIndex))
                continue;

            return true;
        }

        return false;
    }

    private static bool IsAllowedEndpointTouch(GraphNode a, GraphNode b, Vector2 intersection, int polygonIndex)
    {
        if (IsSamePoint(intersection, a.Point) && a.ObstacleIndex == polygonIndex)
            return true;

        if (IsSamePoint(intersection, b.Point) && b.ObstacleIndex == polygonIndex)
            return true;

        return false;
    }

    private static bool TryFindShortestPath(float[,] distances, out int[] previous)
    {
        int count = distances.GetLength(0);
        float[] best = new float[count];
        bool[] visited = new bool[count];
        previous = new int[count];

        for (int i = 0; i < count; i++)
        {
            best[i] = float.PositiveInfinity;
            previous[i] = -1;
        }

        best[0] = 0f;

        for (int iteration = 0; iteration < count; iteration++)
        {
            int current = GetClosestUnvisited(best, visited);

            if (current < 0)
                break;

            if (current == 1)
                return true;

            visited[current] = true;

            for (int next = 0; next < count; next++)
            {
                if (visited[next])
                    continue;

                float edgeDistance = distances[current, next];

                if (float.IsPositiveInfinity(edgeDistance))
                    continue;

                float candidate = best[current] + edgeDistance;

                if (candidate >= best[next])
                    continue;

                best[next] = candidate;
                previous[next] = current;
            }
        }

        return previous[1] >= 0;
    }

    private static int GetClosestUnvisited(float[] distances, bool[] visited)
    {
        int closest = -1;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < distances.Length; i++)
        {
            if (visited[i])
                continue;

            if (distances[i] >= closestDistance)
                continue;

            closest = i;
            closestDistance = distances[i];
        }

        return closest;
    }

    private static List<int> BuildNodePath(int[] previous)
    {
        List<int> path = new();
        int current = 1;

        while (current >= 0)
        {
            path.Add(current);

            if (current == 0)
                break;

            current = previous[current];
        }

        path.Reverse();
        return path;
    }

    private static void BuildWorldPath(List<GraphNode> nodes, List<int> path, Vector3 start, Vector3 end, List<Vector3> result)
    {
        List<Vector2> planarPath = new();

        for (int i = 0; i < path.Count; i++)
            planarPath.Add(nodes[path[i]].Point);

        float totalLength = 0f;

        for (int i = 1; i < planarPath.Count; i++)
            totalLength += Vector2.Distance(planarPath[i - 1], planarPath[i]);

        float travelled = 0f;

        for (int i = 0; i < planarPath.Count; i++)
        {
            if (i > 0)
                travelled += Vector2.Distance(planarPath[i - 1], planarPath[i]);

            float t = totalLength <= Epsilon ? 0f : travelled / totalLength;
            float y = Mathf.Lerp(start.y, end.y, t);
            result.Add(new Vector3(planarPath[i].x, y, planarPath[i].y));
        }
    }

    private static bool TryGetSegmentIntersection(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 intersection, out bool collinearOverlap)
    {
        intersection = Vector2.zero;
        collinearOverlap = false;

        Vector2 r = b - a;
        Vector2 s = d - c;
        float denominator = Cross(r, s);
        float numerator = Cross(c - a, r);

        if (Mathf.Abs(denominator) <= Epsilon)
        {
            if (Mathf.Abs(numerator) > Epsilon)
                return false;

            float rr = Vector2.Dot(r, r);

            if (rr <= Epsilon)
                return false;

            float t0 = Vector2.Dot(c - a, r) / rr;
            float t1 = Vector2.Dot(d - a, r) / rr;

            if (t0 > t1)
                (t0, t1) = (t1, t0);

            float overlapStart = Mathf.Max(0f, t0);
            float overlapEnd = Mathf.Min(1f, t1);

            if (overlapEnd < overlapStart - Epsilon)
                return false;

            collinearOverlap = overlapEnd - overlapStart > Epsilon;
            intersection = a + r * overlapStart;
            return true;
        }

        float t = Cross(c - a, s) / denominator;
        float u = Cross(c - a, r) / denominator;

        if (t < -Epsilon || t > 1f + Epsilon || u < -Epsilon || u > 1f + Epsilon)
            return false;

        intersection = a + r * Mathf.Clamp01(t);
        return true;
    }

    private static bool IsStrictlyInsidePolygon(Vector2 point, List<Vector2> polygon)
    {
        bool hasPositive = false;
        bool hasNegative = false;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            float cross = Cross(b - a, point - a);

            if (cross > Epsilon)
                hasPositive = true;
            else if (cross < -Epsilon)
                hasNegative = true;
            else
                return false;

            if (hasPositive && hasNegative)
                return false;
        }

        return true;
    }

    private static bool IsInsideOrOnPolygon(Vector2 point, List<Vector2> polygon)
    {
        bool hasPositive = false;
        bool hasNegative = false;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            float cross = Cross(b - a, point - a);

            if (cross > Epsilon)
                hasPositive = true;
            else if (cross < -Epsilon)
                hasNegative = true;

            if (hasPositive && hasNegative)
                return false;
        }

        return true;
    }

    private static void EnsureCounterClockwise(List<Vector2> points)
    {
        float area = 0f;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Count];
            area += a.x * b.y - b.x * a.y;
        }

        if (area < 0f)
            points.Reverse();
    }

    private static bool AreAdjacentVertices(int a, int b, int count)
    {
        int difference = Mathf.Abs(a - b);
        return difference == 1 || difference == count - 1;
    }

    private static Vector2 ToPlanar(Vector3 point) => new(point.x, point.z);

    private static Vector2 ToPlanarDirection(Vector3 vector)
    {
        Vector2 planar = new(vector.x, vector.z);

        if (planar.sqrMagnitude <= SqrEpsilon)
            return Vector2.zero;

        return planar.normalized;
    }

    private static bool IsSamePoint(Vector2 a, Vector2 b) => (a - b).sqrMagnitude <= SqrEpsilon;

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}
