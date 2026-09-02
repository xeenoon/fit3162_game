using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Game;

public sealed class AreaPolygon
{
    public AreaPolygon(Vector2[] outline, bool triangulate = true)
    {
        Outline = outline;
        FillTriangles = triangulate ? PolygonTriangulator.Triangulate(outline) : [];
    }

    public Vector2[] Outline { get; }
    public Vector2[] FillTriangles { get; }
}

/// <summary>Converts a thresholded scalar field into closed, interpolated marching-squares contours.</summary>
public static class ContourGenerator
{
    private enum EdgeOrientation
    {
        Horizontal,
        Vertical,
    }

    private readonly record struct NodeKey(int X, int Y, EdgeOrientation Orientation);

    private readonly record struct SegmentKey
    {
        public SegmentKey(NodeKey first, NodeKey second)
        {
            if (Compare(first, second) <= 0)
            {
                First = first;
                Second = second;
            }
            else
            {
                First = second;
                Second = first;
            }
        }

        public NodeKey First { get; }
        public NodeKey Second { get; }

        private static int Compare(NodeKey left, NodeKey right)
        {
            var xComparison = left.X.CompareTo(right.X);
            if (xComparison != 0)
            {
                return xComparison;
            }

            var yComparison = left.Y.CompareTo(right.Y);
            return yComparison != 0
                ? yComparison
                : left.Orientation.CompareTo(right.Orientation);
        }
    }

    public static IReadOnlyList<AreaPolygon> Generate(
        ProceduralField field,
        float threshold,
        float minimumArea = 350f,
        bool triangulate = true)
    {
        var neighbours = new Dictionary<NodeKey, List<NodeKey>>();
        var positions = new Dictionary<NodeKey, Vector2>();

        for (var row = 0; row < field.Rows - 1; row++)
        {
            for (var column = 0; column < field.Columns - 1; column++)
            {
                var topLeftValue = field[column, row];
                var topRightValue = field[column + 1, row];
                var bottomRightValue = field[column + 1, row + 1];
                var bottomLeftValue = field[column, row + 1];
                var cellCase = 0;
                if (topLeftValue >= threshold) cellCase |= 1;
                if (topRightValue >= threshold) cellCase |= 2;
                if (bottomRightValue >= threshold) cellCase |= 4;
                if (bottomLeftValue >= threshold) cellCase |= 8;

                if (cellCase is 0 or 15)
                {
                    continue;
                }

                var top = new NodeKey(column, row, EdgeOrientation.Horizontal);
                var right = new NodeKey(column + 1, row, EdgeOrientation.Vertical);
                var bottom = new NodeKey(column, row + 1, EdgeOrientation.Horizontal);
                var left = new NodeKey(column, row, EdgeOrientation.Vertical);

                positions.TryAdd(top, Interpolate(
                    field.PositionOf(column, row), field.PositionOf(column + 1, row),
                    topLeftValue, topRightValue, threshold));
                positions.TryAdd(right, Interpolate(
                    field.PositionOf(column + 1, row), field.PositionOf(column + 1, row + 1),
                    topRightValue, bottomRightValue, threshold));
                positions.TryAdd(bottom, Interpolate(
                    field.PositionOf(column, row + 1), field.PositionOf(column + 1, row + 1),
                    bottomLeftValue, bottomRightValue, threshold));
                positions.TryAdd(left, Interpolate(
                    field.PositionOf(column, row), field.PositionOf(column, row + 1),
                    topLeftValue, bottomLeftValue, threshold));

                var centerIsInside =
                    (topLeftValue + topRightValue + bottomRightValue + bottomLeftValue) * 0.25f >= threshold;

                switch (cellCase)
                {
                    case 1: AddSegment(left, top); break;
                    case 2: AddSegment(top, right); break;
                    case 3: AddSegment(left, right); break;
                    case 4: AddSegment(right, bottom); break;
                    case 5:
                        if (centerIsInside)
                        {
                            AddSegment(top, right);
                            AddSegment(bottom, left);
                        }
                        else
                        {
                            AddSegment(left, top);
                            AddSegment(right, bottom);
                        }
                        break;
                    case 6: AddSegment(top, bottom); break;
                    case 7: AddSegment(left, bottom); break;
                    case 8: AddSegment(bottom, left); break;
                    case 9: AddSegment(top, bottom); break;
                    case 10:
                        if (centerIsInside)
                        {
                            AddSegment(left, top);
                            AddSegment(right, bottom);
                        }
                        else
                        {
                            AddSegment(top, right);
                            AddSegment(bottom, left);
                        }
                        break;
                    case 11: AddSegment(right, bottom); break;
                    case 12: AddSegment(left, right); break;
                    case 13: AddSegment(top, right); break;
                    case 14: AddSegment(left, top); break;
                }

                void AddSegment(NodeKey first, NodeKey second)
                {
                    if (!neighbours.TryGetValue(first, out var firstNeighbours))
                    {
                        firstNeighbours = new List<NodeKey>(2);
                        neighbours[first] = firstNeighbours;
                    }

                    if (!neighbours.TryGetValue(second, out var secondNeighbours))
                    {
                        secondNeighbours = new List<NodeKey>(2);
                        neighbours[second] = secondNeighbours;
                    }

                    firstNeighbours.Add(second);
                    secondNeighbours.Add(first);
                }
            }
        }

        var polygons = new List<AreaPolygon>();
        var visited = new HashSet<SegmentKey>();
        foreach (var pair in neighbours)
        {
            foreach (var firstStep in pair.Value)
            {
                if (visited.Contains(new SegmentKey(pair.Key, firstStep)))
                {
                    continue;
                }

                var outline = TraceLoop(pair.Key, firstStep, neighbours, positions, visited);
                outline = RemoveNearlyCollinearPoints(outline);
                if (outline.Count >= 3 && MathF.Abs(SignedArea(outline)) >= minimumArea)
                {
                    outline = MakeOrganic(outline, smoothingPasses: 2, waveStrength: 1.15f);
                    polygons.Add(new AreaPolygon(outline.ToArray(), triangulate));
                }
            }
        }

        return polygons;
    }

    private static List<Vector2> TraceLoop(
        NodeKey start,
        NodeKey firstStep,
        IReadOnlyDictionary<NodeKey, List<NodeKey>> neighbours,
        IReadOnlyDictionary<NodeKey, Vector2> positions,
        ISet<SegmentKey> visited)
    {
        var result = new List<Vector2> { positions[start] };
        var previous = start;
        var current = firstStep;
        visited.Add(new SegmentKey(previous, current));
        var safety = neighbours.Count + 1;

        while (!current.Equals(start) && safety-- > 0)
        {
            result.Add(positions[current]);
            if (!neighbours.TryGetValue(current, out var options) || options.Count < 2)
            {
                return new List<Vector2>();
            }

            var next = options[0].Equals(previous) ? options[1] : options[0];
            visited.Add(new SegmentKey(current, next));
            previous = current;
            current = next;
        }

        return current.Equals(start) ? result : new List<Vector2>();
    }

    private static Vector2 Interpolate(
        Vector2 from,
        Vector2 to,
        float fromValue,
        float toValue,
        float threshold)
    {
        var difference = toValue - fromValue;
        var amount = MathF.Abs(difference) < 0.00001f
            ? 0.5f
            : MathHelper.Clamp((threshold - fromValue) / difference, 0f, 1f);
        return Vector2.Lerp(from, to, amount);
    }

    private static List<Vector2> RemoveNearlyCollinearPoints(List<Vector2> points)
    {
        if (points.Count < 4)
        {
            return points;
        }

        var result = new List<Vector2>(points.Count);
        for (var index = 0; index < points.Count; index++)
        {
            var previous = points[(index - 1 + points.Count) % points.Count];
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            var span = next - previous;
            var distanceFromLine = MathF.Abs(Cross(span, current - previous)) /
                MathF.Max(0.001f, span.Length());
            if (distanceFromLine > 0.2f)
            {
                result.Add(current);
            }
        }

        return result.Count >= 3 ? result : points;
    }

    private static List<Vector2> MakeOrganic(
        IReadOnlyList<Vector2> source,
        int smoothingPasses,
        float waveStrength)
    {
        var points = new List<Vector2>(source);
        for (var pass = 0; pass < smoothingPasses; pass++)
        {
            var rounded = new List<Vector2>(points.Count * 2);
            for (var index = 0; index < points.Count; index++)
            {
                var current = points[index];
                var next = points[(index + 1) % points.Count];
                // Chaikin corner cutting turns threshold corners into a soft, yielding edge.
                rounded.Add(Vector2.Lerp(current, next, 0.25f));
                rounded.Add(Vector2.Lerp(current, next, 0.75f));
            }

            points = rounded;
        }

        var phase = source[0].X * 0.031f + source[0].Y * 0.047f + source.Count;
        var distance = 0f;
        var waved = new List<Vector2>(points.Count);
        for (var index = 0; index < points.Count; index++)
        {
            var previous = points[(index - 1 + points.Count) % points.Count];
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            distance += Vector2.Distance(previous, current);
            var tangent = next - previous;
            if (tangent.LengthSquared() < 0.0001f)
            {
                waved.Add(current);
                continue;
            }

            tangent.Normalize();
            var normal = new Vector2(-tangent.Y, tangent.X);
            var wave = MathF.Sin(distance * 0.075f + phase) * 0.7f +
                MathF.Sin(distance * 0.18f - phase * 0.63f) * 0.3f;
            waved.Add(current + normal * wave * waveStrength);
        }

        return waved;
    }

    private static float SignedArea(IReadOnlyList<Vector2> points)
    {
        var twiceArea = 0f;
        for (var index = 0; index < points.Count; index++)
        {
            var next = (index + 1) % points.Count;
            twiceArea += points[index].X * points[next].Y - points[next].X * points[index].Y;
        }

        return twiceArea * 0.5f;
    }

    private static float Cross(Vector2 left, Vector2 right) => left.X * right.Y - left.Y * right.X;
}

internal static class PolygonTriangulator
{
    public static Vector2[] Triangulate(IReadOnlyList<Vector2> polygon)
    {
        if (polygon.Count < 3)
        {
            return Array.Empty<Vector2>();
        }

        var indices = new List<int>(polygon.Count);
        if (SignedArea(polygon) > 0f)
        {
            for (var index = 0; index < polygon.Count; index++) indices.Add(index);
        }
        else
        {
            for (var index = polygon.Count - 1; index >= 0; index--) indices.Add(index);
        }

        var triangles = new List<Vector2>((polygon.Count - 2) * 3);
        var failedPasses = 0;
        while (indices.Count > 3 && failedPasses <= indices.Count)
        {
            var earFound = false;
            for (var index = 0; index < indices.Count; index++)
            {
                var previousIndex = indices[(index - 1 + indices.Count) % indices.Count];
                var currentIndex = indices[index];
                var nextIndex = indices[(index + 1) % indices.Count];
                var previous = polygon[previousIndex];
                var current = polygon[currentIndex];
                var next = polygon[nextIndex];

                if (Cross(current - previous, next - current) <= 0.0001f ||
                    ContainsOtherPoint(polygon, indices, previousIndex, currentIndex, nextIndex))
                {
                    continue;
                }

                triangles.Add(previous);
                triangles.Add(current);
                triangles.Add(next);
                indices.RemoveAt(index);
                earFound = true;
                failedPasses = 0;
                break;
            }

            if (!earFound)
            {
                failedPasses++;
                break;
            }
        }

        if (indices.Count == 3)
        {
            triangles.Add(polygon[indices[0]]);
            triangles.Add(polygon[indices[1]]);
            triangles.Add(polygon[indices[2]]);
        }

        // An incomplete triangulation is worse than leaving an outline unfilled.
        return indices.Count == 3 ? triangles.ToArray() : Array.Empty<Vector2>();
    }

    private static bool ContainsOtherPoint(
        IReadOnlyList<Vector2> polygon,
        IReadOnlyList<int> indices,
        int firstIndex,
        int secondIndex,
        int thirdIndex)
    {
        var first = polygon[firstIndex];
        var second = polygon[secondIndex];
        var third = polygon[thirdIndex];
        foreach (var pointIndex in indices)
        {
            if (pointIndex == firstIndex || pointIndex == secondIndex || pointIndex == thirdIndex)
            {
                continue;
            }

            var point = polygon[pointIndex];
            var firstCross = Cross(second - first, point - first);
            var secondCross = Cross(third - second, point - second);
            var thirdCross = Cross(first - third, point - third);
            if (firstCross >= -0.0001f && secondCross >= -0.0001f && thirdCross >= -0.0001f)
            {
                return true;
            }
        }

        return false;
    }

    private static float SignedArea(IReadOnlyList<Vector2> points)
    {
        var twiceArea = 0f;
        for (var index = 0; index < points.Count; index++)
        {
            var next = (index + 1) % points.Count;
            twiceArea += points[index].X * points[next].Y - points[next].X * points[index].Y;
        }

        return twiceArea * 0.5f;
    }

    private static float Cross(Vector2 left, Vector2 right) => left.X * right.Y - left.Y * right.X;
}
