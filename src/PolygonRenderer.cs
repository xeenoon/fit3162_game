using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Game;

/// <summary>Small immediate-mode helper for flat procedural geometry and thick outlines.</summary>
public sealed class PolygonRenderer : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly BasicEffect _effect;
    private readonly RasterizerState _rasterizerState = new() { CullMode = CullMode.None };
    private readonly BlendState _maskBlendState = new() { ColorWriteChannels = ColorWriteChannels.None };
    private readonly DepthStencilState _maskWriteState = new()
    {
        DepthBufferEnable = false,
        StencilEnable = true,
        StencilFunction = CompareFunction.Always,
        StencilPass = StencilOperation.Replace,
        ReferenceStencil = 1,
    };
    private readonly DepthStencilState _maskReadState = new()
    {
        DepthBufferEnable = false,
        StencilEnable = true,
        StencilFunction = CompareFunction.Equal,
        StencilPass = StencilOperation.Keep,
        ReferenceStencil = 1,
        StencilWriteMask = 0,
    };

    public PolygonRenderer(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
        _effect = new BasicEffect(graphicsDevice)
        {
            VertexColorEnabled = true,
        };
    }

    public void Begin(Viewport viewport)
    {
        _effect.World = Matrix.Identity;
        _effect.View = Matrix.Identity;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(
            // UI geometry uses a stable 1280x720 logical canvas; MonoGame scales it to the
            // requested backbuffer, including the 1920x1080 evidence mode.
            0, 1280, 720, 0, 0, 1);
        _graphicsDevice.RasterizerState = _rasterizerState;
        _graphicsDevice.BlendState = BlendState.AlphaBlend;
        _graphicsDevice.DepthStencilState = DepthStencilState.None;
    }

    public void BeginMask()
    {
        _graphicsDevice.BlendState = _maskBlendState;
        _graphicsDevice.DepthStencilState = _maskWriteState;
    }

    public void UseMask()
    {
        _graphicsDevice.BlendState = BlendState.AlphaBlend;
        _graphicsDevice.DepthStencilState = _maskReadState;
    }

    public void EndMask()
    {
        _graphicsDevice.BlendState = BlendState.AlphaBlend;
        _graphicsDevice.DepthStencilState = DepthStencilState.None;
    }

    public void DrawRegularPolygon(Vector2 center, float radius, int sides, Color color, float rotation = 0)
    {
        if (sides < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(sides), "A polygon needs at least three sides.");
        }

        // Each triangle shares the centre vertex and covers one edge of the shape.
        var vertices = new VertexPositionColor[sides * 3];
        for (var side = 0; side < sides; side++)
        {
            var nextSide = (side + 1) % sides;
            var vertexIndex = side * 3;
            vertices[vertexIndex] = CreateVertex(center, color);
            vertices[vertexIndex + 1] = CreateVertex(PointOnCircle(center, radius, sides, side, rotation), color);
            vertices[vertexIndex + 2] = CreateVertex(PointOnCircle(center, radius, sides, nextSide, rotation), color);
        }

        DrawVertexList(vertices, sides);
    }

    public void DrawTriangles(IReadOnlyList<Vector2> points, Color color)
    {
        var triangleCount = points.Count / 3;
        if (triangleCount == 0)
        {
            return;
        }

        var vertices = new VertexPositionColor[triangleCount * 3];
        for (var index = 0; index < vertices.Length; index++)
        {
            vertices[index] = CreateVertex(points[index], color);
        }

        DrawVertexList(vertices, triangleCount);
    }

    public void DrawTexturedTriangles(
        IReadOnlyList<Vector2> points,
        Texture2D texture,
        Rectangle textureBounds,
        Color tint)
    {
        var triangleCount = points.Count / 3;
        if (triangleCount == 0)
        {
            return;
        }

        var vertices = new VertexPositionColorTexture[triangleCount * 3];
        var inverseWidth = 1f / Math.Max(1, textureBounds.Width);
        var inverseHeight = 1f / Math.Max(1, textureBounds.Height);
        for (var index = 0; index < vertices.Length; index++)
        {
            var point = points[index];
            var textureCoordinate = new Vector2(
                (point.X - textureBounds.Left) * inverseWidth,
                (point.Y - textureBounds.Top) * inverseHeight);
            vertices[index] = new VertexPositionColorTexture(
                new Vector3(point, 0f),
                tint,
                textureCoordinate);
        }

        _effect.TextureEnabled = true;
        _effect.Texture = texture;
        _graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphicsDevice.DrawUserPrimitives(
                PrimitiveType.TriangleList,
                vertices,
                0,
                triangleCount);
        }

        _effect.TextureEnabled = false;
        _effect.Texture = null;
    }

    public void DrawFilledRectangle(float left, float top, float right, float bottom, Color color)
    {
        DrawTriangles(
        [
            new Vector2(left, top),
            new Vector2(right, top),
            new Vector2(right, bottom),
            new Vector2(left, top),
            new Vector2(right, bottom),
            new Vector2(left, bottom),
        ], color);
    }

    public void DrawPolylines(
        IReadOnlyList<Vector2[]> paths,
        Color color,
        float thickness = 1f,
        bool closed = false)
    {
        var vertices = new List<VertexPositionColor>();
        foreach (var path in paths)
        {
            AppendPolyline(vertices, path, color, thickness, closed);
        }

        if (vertices.Count > 0)
        {
            DrawVertexList(vertices.ToArray(), vertices.Count / 3);
        }
    }

    public void DrawLineSegments(IReadOnlyList<Vector2> points, Color color, float thickness = 1f)
    {
        var vertices = new List<VertexPositionColor>(points.Count * 3);
        for (var index = 0; index + 1 < points.Count; index += 2)
        {
            AppendLine(vertices, points[index], points[index + 1], color, thickness);
        }

        if (vertices.Count > 0)
        {
            DrawVertexList(vertices.ToArray(), vertices.Count / 3);
        }
    }

    public void DrawPolyline(
        IReadOnlyList<Vector2> points,
        Color color,
        float thickness = 1f,
        bool closed = false)
    {
        var segmentCount = closed ? points.Count : points.Count - 1;
        if (segmentCount < 1 || thickness <= 0f)
        {
            return;
        }

        var vertices = new List<VertexPositionColor>(segmentCount * 6);
        AppendPolyline(vertices, points, color, thickness, closed);
        if (vertices.Count == 0)
        {
            return;
        }

        DrawVertexList(vertices.ToArray(), vertices.Count / 3);
    }

    private static void AppendPolyline(
        ICollection<VertexPositionColor> vertices,
        IReadOnlyList<Vector2> points,
        Color color,
        float thickness,
        bool closed)
    {
        var segmentCount = closed ? points.Count : points.Count - 1;
        for (var segment = 0; segment < segmentCount; segment++)
        {
            AppendLine(vertices, points[segment], points[(segment + 1) % points.Count], color, thickness);
        }
    }

    private static void AppendLine(
        ICollection<VertexPositionColor> vertices,
        Vector2 from,
        Vector2 to,
        Color color,
        float thickness)
    {
        var direction = to - from;
        if (direction.LengthSquared() < 0.0001f)
        {
            return;
        }

        direction.Normalize();
        var normal = new Vector2(-direction.Y, direction.X) * (thickness * 0.5f);
        var first = from + normal;
        var second = to + normal;
        var third = to - normal;
        var fourth = from - normal;

        vertices.Add(CreateVertex(first, color));
        vertices.Add(CreateVertex(second, color));
        vertices.Add(CreateVertex(third, color));
        vertices.Add(CreateVertex(first, color));
        vertices.Add(CreateVertex(third, color));
        vertices.Add(CreateVertex(fourth, color));
    }

    private void DrawVertexList(VertexPositionColor[] vertices, int triangleCount)
    {
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, triangleCount);
        }
    }

    private static Vector2 PointOnCircle(Vector2 center, float radius, int sides, int point, float rotation)
    {
        var angle = rotation + MathHelper.TwoPi * point / sides;
        return center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private static VertexPositionColor CreateVertex(Vector2 point, Color color) =>
        new(new Vector3(point, 0), color);

    public void Dispose()
    {
        _effect.Dispose();
        _rasterizerState.Dispose();
        _maskBlendState.Dispose();
        _maskWriteState.Dispose();
        _maskReadState.Dispose();
    }
}
