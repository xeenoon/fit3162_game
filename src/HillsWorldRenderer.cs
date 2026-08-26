using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Game;

/// <summary>
/// An isolated renderer for the generated map diorama. The generated world remains plain data;
/// this class is the only place that turns it into persistent GPU resources.
/// </summary>
public sealed class HillsWorldRenderer : IDisposable
{
    public const int SceneWidth = 1164;
    public const int SceneHeight = 480;

    private static readonly Color SkyTop = new(111, 131, 143);
    private static readonly Color FogColor = new(TerrainLighting.FogColor);
    private readonly GraphicsDevice _graphicsDevice;
    private readonly int _sceneWidth;
    private readonly int _sceneHeight;
    private readonly BasicEffect _effect;
    private readonly BasicEffect _skyEffect;
    private readonly RasterizerState _rasterizerState = new() { CullMode = CullMode.None };
    private readonly DepthStencilState _depthRead = new()
    {
        DepthBufferEnable = true,
        DepthBufferWriteEnable = false,
    };
    private readonly MeshBuffer _terrain;
    private readonly Texture2D _terrainMaterial;
    private readonly MeshBuffer _terrainFeatures;
    private readonly MeshBuffer _rockFormations;
    private readonly MeshBuffer _forestMasses;
    private readonly MeshBuffer _groundCover;
    private readonly MeshBuffer _water;
    private readonly MeshBuffer _shadows;
    private readonly MeshBuffer _lowlandCanopyProps;
    private readonly MeshBuffer _lowlandGroundProps;
    private readonly MeshBuffer _mountainProps;
    private readonly MeshBuffer[] _landmarks;
    private readonly MeshBuffer _selectionMarker;
    private readonly MapCameraAngle _cameraAngle;
    private readonly bool _freezeCamera;
    private readonly bool _overviewCapture;
    private readonly bool _renderOnly;
    private bool _frozenFrameRendered;
    private float _cameraTime;
    private Vector2 _focus;

    public HillsWorldRenderer(GraphicsDevice graphicsDevice, GeneratedWorld world)
    {
        _graphicsDevice = graphicsDevice;
        World = world;
        _cameraAngle = ParseCameraAngle(Environment.GetEnvironmentVariable("SILENT_LABYRINTH_MAP_CAMERA"));
        _freezeCamera = Environment.GetEnvironmentVariable("SILENT_LABYRINTH_MAP_FREEZE") == "1";
        _overviewCapture = Environment.GetEnvironmentVariable("SILENT_LABYRINTH_MAP_ZOOM") is "4" or "16";
        _renderOnly = Environment.GetEnvironmentVariable("SILENT_LABYRINTH_RENDER_ONLY") == "1";
        var renderScale = Environment.GetEnvironmentVariable("SILENT_LABYRINTH_RENDER_1080") == "1" ? 1.5f : 1f;
        _sceneWidth = _renderOnly ? 1920 : (int)(SceneWidth * renderScale);
        _sceneHeight = _renderOnly ? 1080 : (int)(SceneHeight * renderScale);
        Scene = new RenderTarget2D(
            graphicsDevice,
            _sceneWidth,
            _sceneHeight,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        _effect = new BasicEffect(graphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = true,
            FogEnabled = true,
            FogColor = FogColor.ToVector3(),
            FogStart = 105f,
            FogEnd = 205f,
            // Two broad fill lights below approximate a hemisphere: cool sky on upward faces,
            // muted earth bounce on downward faces. A small neutral floor keeps vertical cracks
            // readable without flattening them.
            AmbientLightColor = new Vector3(0.10f, 0.11f, 0.10f),
            PreferPerPixelLighting = true,
        };
        _effect.DirectionalLight0.Enabled = true;
        _effect.DirectionalLight0.Direction = TerrainLighting.SunDirection;
        _effect.DirectionalLight0.DiffuseColor = TerrainLighting.SunColor * 0.78f;
        _effect.DirectionalLight0.SpecularColor = Vector3.Zero;
        _effect.DirectionalLight1.Enabled = true;
        _effect.DirectionalLight1.Direction = Vector3.Down;
        _effect.DirectionalLight1.DiffuseColor = TerrainLighting.SkyAmbient * 0.88f;
        _effect.DirectionalLight1.SpecularColor = Vector3.Zero;
        _effect.DirectionalLight2.Enabled = true;
        _effect.DirectionalLight2.Direction = Vector3.Up;
        _effect.DirectionalLight2.DiffuseColor = TerrainLighting.GroundAmbient * 0.72f;
        _effect.DirectionalLight2.SpecularColor = Vector3.Zero;
        _skyEffect = new BasicEffect(graphicsDevice) { VertexColorEnabled = true };

        _terrain = BuildTerrain(world);
        _terrainMaterial = TerrainMaterialTexture.Create(graphicsDevice, world);
        _terrainFeatures = BuildTerrainFeatures(world);
        _rockFormations = BuildRockFormations(world);
        _forestMasses = BuildForestMasses(world);
        _groundCover = BuildGroundCover(world);
        _water = BuildWater(world);
        _shadows = BuildShadows(world);
        _lowlandCanopyProps = BuildProps(world, prop => prop.Type is
            PropType.Tree or PropType.DeadTree or PropType.FallenLog or PropType.Stump);
        _lowlandGroundProps = BuildProps(world, prop => prop.Type is PropType.Shrub or PropType.Rock);
        _mountainProps = BuildProps(world, prop => prop.Type is
            PropType.PineTree or PropType.CrookedPine or PropType.AlpineShrub or PropType.LichenRock or PropType.DeadConifer);
        _landmarks = new MeshBuffer[world.DungeonSites.Count];
        for (var index = 0; index < _landmarks.Length; index++)
        {
            _landmarks[index] = BuildLandmark(world.DungeonSites[index]);
        }

        _selectionMarker = BuildSelectionMarker();
        var initialSite = world.DungeonSites[0];
        _focus = new Vector2(initialSite.Position.X, initialSite.Position.Z);
    }

    public GeneratedWorld World { get; }
    public RenderTarget2D Scene { get; }

    public void Render(GameTime gameTime, int selectedDungeon)
    {
        if (_freezeCamera && _frozenFrameRendered)
        {
            return;
        }

        var elapsed = Math.Min(0.1f, (float)gameTime.ElapsedGameTime.TotalSeconds);
        if (!_freezeCamera)
        {
            _cameraTime += elapsed;
        }
        var selectedSite = World.DungeonSites[Math.Clamp(selectedDungeon, 0, World.DungeonSites.Count - 1)];
        var desiredFocus = new Vector2(selectedSite.Position.X, selectedSite.Position.Z);
        if (!_freezeCamera)
        {
            var focusAmount = 1f - MathF.Exp(-elapsed * 1.65f);
            _focus = Vector2.Lerp(_focus, desiredFocus, focusAmount);
        }

        _graphicsDevice.SetRenderTarget(Scene);
        _graphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, FogColor, 1f, 0);
        DrawSky();

        var focus = _overviewCapture ? Vector2.Zero : _focus;
        var mapCenter = new Vector3(
            focus.X * 0.06f + MathF.Sin(_cameraTime * 0.10f) * 1.1f,
            8f,
            -9f + focus.Y * 0.035f + MathF.Sin(_cameraTime * 0.14f + 0.8f) * 0.55f);
        var cameraOffset = _cameraAngle switch
        {
            MapCameraAngle.Top => new Vector3(0f, 126f, 0f),
            MapCameraAngle.Slight => new Vector3(0f, 105f, 55f),
            _ => new Vector3(0f, 70f, 96f),
        };
        var generatedScale = World.TerrainSettings.Size / 140f;
        cameraOffset *= generatedScale;
        var cameraPosition = mapCenter + cameraOffset;
        var viewUp = _cameraAngle == MapCameraAngle.Top ? Vector3.Forward : Vector3.Up;
        var cameraDistance = cameraOffset.Length();
        // A restrained two-zone atmospheric falloff: the near field stays crisp, the far field
        // loses contrast and shifts toward the blue-grey sky instead of vanishing in white fog.
        _effect.FogStart = cameraDistance * 0.78f;
        _effect.FogEnd = cameraDistance * 1.58f;
        _effect.World = Matrix.Identity;
        _effect.View = Matrix.CreateLookAt(cameraPosition, mapCenter, viewUp);
        _effect.Projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(36.5f),
            _sceneWidth / (float)_sceneHeight,
            0.5f,
            Math.Max(240f, cameraDistance * 1.35f + 100f));

        _graphicsDevice.RasterizerState = _rasterizerState;
        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.DepthStencilState = DepthStencilState.Default;
        _effect.LightingEnabled = true;
        _effect.FogEnabled = true;
        _effect.EmissiveColor = Vector3.Zero;
        _effect.TextureEnabled = true;
        _effect.Texture = _terrainMaterial;
        _effect.AmbientLightColor = Vector3.One;
        _effect.DirectionalLight0.Enabled = false;
        _effect.DirectionalLight1.Enabled = false;
        _effect.DirectionalLight2.Enabled = false;
        _graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
        DrawMesh(_terrain, _effect);
        _effect.TextureEnabled = false;
        _effect.Texture = null;
        ApplyGeometryLighting();
        DrawMesh(_terrainFeatures, _effect);
        DrawMesh(_rockFormations, _effect);
        DrawMesh(_forestMasses, _effect);

        _effect.LightingEnabled = false;
        _graphicsDevice.BlendState = BlendState.AlphaBlend;
        _graphicsDevice.DepthStencilState = DepthStencilState.Default;
        DrawMesh(_water, _effect);

        _graphicsDevice.BlendState = BlendState.NonPremultiplied;
        _graphicsDevice.DepthStencilState = _depthRead;
        DrawMesh(_shadows, _effect);

        _effect.LightingEnabled = true;
        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.DepthStencilState = DepthStencilState.Default;
        DrawMesh(_lowlandGroundProps, _effect);
        DrawMesh(_groundCover, _effect);
        DrawMesh(_lowlandCanopyProps, _effect);
        DrawMesh(_mountainProps, _effect);

        for (var index = 0; index < _landmarks.Length; index++)
        {
            _effect.EmissiveColor = !_renderOnly && index == selectedDungeon
                ? new Vector3(0.34f, 0.23f, 0.06f)
                : Vector3.Zero;
            DrawMesh(_landmarks[index], _effect);
        }

        _effect.EmissiveColor = Vector3.Zero;
        if (!_renderOnly)
        {
            _effect.LightingEnabled = false;
            _effect.FogEnabled = false;
            _effect.World = Matrix.CreateTranslation(selectedSite.Position);
            _graphicsDevice.BlendState = BlendState.AlphaBlend;
            _graphicsDevice.DepthStencilState = _depthRead;
            DrawMesh(_selectionMarker, _effect);
        }
        _effect.World = Matrix.Identity;
        _graphicsDevice.SetRenderTarget(null);
        _frozenFrameRendered = _freezeCamera;
    }

    private void DrawSky()
    {
        _graphicsDevice.DepthStencilState = DepthStencilState.None;
        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.RasterizerState = _rasterizerState;
        VertexPositionColor[] vertices =
        [
            new(new Vector3(-1f, 1f, 0f), SkyTop),
            new(new Vector3(1f, 1f, 0f), SkyTop),
            new(new Vector3(1f, -1f, 0f), FogColor),
            new(new Vector3(-1f, 1f, 0f), SkyTop),
            new(new Vector3(1f, -1f, 0f), FogColor),
            new(new Vector3(-1f, -1f, 0f), FogColor),
        ];
        _skyEffect.World = Matrix.Identity;
        _skyEffect.View = Matrix.Identity;
        _skyEffect.Projection = Matrix.Identity;
        foreach (var pass in _skyEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, 2);
        }
    }

    private void ApplyGeometryLighting()
    {
        _effect.AmbientLightColor = new Vector3(0.10f, 0.11f, 0.10f);
        _effect.DirectionalLight0.Enabled = true;
        _effect.DirectionalLight0.Direction = TerrainLighting.SunDirection;
        _effect.DirectionalLight0.DiffuseColor = TerrainLighting.SunColor * 0.78f;
        _effect.DirectionalLight1.Enabled = true;
        _effect.DirectionalLight1.Direction = Vector3.Down;
        _effect.DirectionalLight1.DiffuseColor = TerrainLighting.SkyAmbient * 0.88f;
        _effect.DirectionalLight2.Enabled = true;
        _effect.DirectionalLight2.Direction = Vector3.Up;
        _effect.DirectionalLight2.DiffuseColor = TerrainLighting.GroundAmbient * 0.72f;
    }

    private void DrawMesh(MeshBuffer mesh, BasicEffect effect)
    {
        if (mesh.PrimitiveCount == 0)
        {
            return;
        }

        _graphicsDevice.SetVertexBuffer(mesh.Vertices);
        _graphicsDevice.Indices = mesh.Indices;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, mesh.PrimitiveCount);
        }
    }

    private MeshBuffer BuildTerrain(GeneratedWorld world)
    {
        var terrain = world.Terrain;
        var resolution = terrain.Settings.GridResolution;
        var vertices = new HillsVertex[resolution * resolution];
        for (var row = 0; row < resolution; row++)
        {
            for (var column = 0; column < resolution; column++)
            {
                var index = terrain.IndexOf(column, row);
                var position = terrain.PositionAt(column, row);
                var normal = terrain.NormalAt(column, row);
                var textureCoordinate = new Vector2(
                    column / (float)(resolution - 1),
                    row / (float)(resolution - 1));
                vertices[index] = new HillsVertex(position, normal, Color.White, textureCoordinate);
            }
        }

        var indices = new ushort[(resolution - 1) * (resolution - 1) * 6];
        var next = 0;
        for (var row = 0; row < resolution - 1; row++)
        {
            for (var column = 0; column < resolution - 1; column++)
            {
                var topLeft = (ushort)terrain.IndexOf(column, row);
                var topRight = (ushort)terrain.IndexOf(column + 1, row);
                var bottomLeft = (ushort)terrain.IndexOf(column, row + 1);
                var bottomRight = (ushort)terrain.IndexOf(column + 1, row + 1);
                var risingDiagonalDelta = MathF.Abs(
                    terrain.HeightAt(column + 1, row) - terrain.HeightAt(column, row + 1));
                var fallingDiagonalDelta = MathF.Abs(
                    terrain.HeightAt(column, row) - terrain.HeightAt(column + 1, row + 1));
                var useRisingDiagonal = risingDiagonalDelta < fallingDiagonalDelta ||
                                        (MathF.Abs(risingDiagonalDelta - fallingDiagonalDelta) < 0.0001f &&
                                         ((row + column) & 1) == 0);
                if (useRisingDiagonal)
                {
                    indices[next++] = topLeft;
                    indices[next++] = bottomLeft;
                    indices[next++] = topRight;
                    indices[next++] = topRight;
                    indices[next++] = bottomLeft;
                    indices[next++] = bottomRight;
                }
                else
                {
                    indices[next++] = topLeft;
                    indices[next++] = bottomLeft;
                    indices[next++] = bottomRight;
                    indices[next++] = topLeft;
                    indices[next++] = bottomRight;
                    indices[next++] = topRight;
                }
            }
        }

        return new MeshBuffer(_graphicsDevice, vertices, indices);
    }

    private static float DistanceToPolyline(Vector2 point, IReadOnlyList<Vector2> points)
    {
        var closest = float.MaxValue;
        for (var index = 0; index < points.Count - 1; index++)
        {
            var start = points[index];
            var segment = points[index + 1] - start;
            var amount = segment.LengthSquared() < 0.0001f
                ? 0f
                : MathHelper.Clamp(Vector2.Dot(point - start, segment) / segment.LengthSquared(), 0f, 1f);
            closest = Math.Min(closest, Vector2.Distance(point, start + segment * amount));
        }

        return closest;
    }

    private MeshBuffer BuildWater(GeneratedWorld world)
    {
        var builder = new GeometryBuilder();
        foreach (var lake in world.Lakes)
        {
            var center = new Vector3(lake.Center.X, lake.WaterHeight, lake.Center.Y);
            var bank = lake.Shoreline
                .Select(point => lake.Center + (point - lake.Center) * 1.11f)
                .ToArray();
            builder.AddHorizontalPolygon(
                bank,
                center.Y + 0.055f,
                new Color(87, 84, 73, 238),
                lake.Center);
            builder.AddHorizontalPolygon(
                lake.Shoreline,
                center.Y + 0.085f,
                new Color(world.Hills.LakeWater.R, world.Hills.LakeWater.G, world.Hills.LakeWater.B, (byte)228),
                lake.Center);
        }

        foreach (var drainage in world.MountainFormations.Where(formation => formation.Type == MountainFormationType.HillDrainage))
        {
            for (var index = 0; index < drainage.Points.Count - 1; index++)
            {
                var start2 = drainage.Points[index];
                var end2 = drainage.Points[index + 1];
                var start = new Vector3(start2.X, world.Terrain.SampleHeight(start2.X, start2.Y) + 0.035f, start2.Y);
                var end = new Vector3(end2.X, world.Terrain.SampleHeight(end2.X, end2.Y) + 0.035f, end2.Y);
                AddPathSegment(builder, world.Terrain, start, end, MathHelper.Lerp(0.18f, 0.56f, index / (float)drainage.Points.Count),
                    new Color(49, 68, 69, 188), 0.035f);
            }
        }

        return builder.Build(_graphicsDevice);
    }

    private MeshBuffer BuildTerrainFeatures(GeneratedWorld world)
    {
        var builder = new GeometryBuilder();
        foreach (var feature in world.Features)
        {
            if (feature.Type != TerrainFeatureType.WornPath)
            {
                continue;
            }

            var edgeColor = Tint(new Color(94, 91, 75), feature.ColorVariation);
            var pathColor = Tint(new Color(112, 98, 75), feature.ColorVariation);
            var centreColor = Tint(new Color(77, 67, 55), feature.ColorVariation);
            var stoneColor = Tint(new Color(123, 117, 105), feature.ColorVariation * 0.5f);
            for (var index = 0; index < feature.Points.Count - 1; index++)
            {
                var irregularWidth = 0.82f + MathF.Sin(index * 1.71f + feature.ColorVariation * 23f) * 0.16f +
                                     MathF.Sin(index * 3.37f + 0.8f) * 0.07f;
                var start = feature.Points[index];
                var end = feature.Points[index + 1];
                AddPathSegment(builder, world.Terrain, start, end, feature.Width * irregularWidth * 1.75f, edgeColor, 0.035f);
                var wornWidth = feature.Width * irregularWidth;
                AddPathSegment(builder, world.Terrain, start, end, wornWidth, pathColor, 0.055f);
                AddPathSegment(builder, world.Terrain, start, end, wornWidth * 0.12f, centreColor, 0.075f, -wornWidth * 0.18f);
                AddPathSegment(builder, world.Terrain, start, end, wornWidth * 0.12f, centreColor, 0.075f, wornWidth * 0.18f);
                if ((index & 1) == 0)
                {
                    var amount = 0.31f + (index % 3) * 0.17f;
                    var point = Vector3.Lerp(start, end, amount);
                    var direction = Vector2.Normalize(new Vector2(end.X - start.X, end.Z - start.Z));
                    var side = new Vector2(-direction.Y, direction.X) * feature.Width * (index % 4 < 2 ? 0.52f : -0.52f);
                    var position = new Vector3(point.X + side.X, world.Terrain.SampleHeight(point.X + side.X, point.Z + side.Y), point.Z + side.Y);
                    builder.AddFacetedEllipsoid(position + new Vector3(0f, 0.09f, 0f), new Vector3(0.16f, 0.11f, 0.20f), stoneColor, index * 0.83f, 6);
                }
            }
        }

        return builder.Build(_graphicsDevice);
    }

    private MeshBuffer BuildRockFormations(GeneratedWorld world)
    {
        var builder = new GeometryBuilder();
        var random = new StableRandom(unchecked((ulong)(uint)world.Seed) ^ 0x7f4a29c31d86e5b0UL);
        var terrain = world.Terrain;
        foreach (var formation in world.MountainFormations)
        {
            if (formation.Type == MountainFormationType.CliffFace)
            {
                for (var index = 1; index < formation.Points.Count - 1; index += 2)
                {
                    var point = formation.Points[index];
                    var next = formation.Points[index + 1];
                    var direction = next - point;
                    if (direction.LengthSquared() < 0.001f) continue;
                    direction.Normalize();
                    var normal = new Vector2(-direction.Y, direction.X);
                    var center = point + normal * random.Range(-formation.Width * 0.32f, formation.Width * 0.32f);
                    var height = terrain.SampleHeight(center.X, center.Y);
                    var yaw = MathF.Atan2(direction.Y, direction.X);
                    var shelfLength = random.Range(3.0f, 6.8f);
                    builder.AddFracturedRock(
                        new Vector3(center.X, height - 0.18f, center.Y),
                        new Vector3(shelfLength, random.Range(1.2f, 2.8f), random.Range(1.6f, 3.2f)),
                        Tint(new Color(91, 94, 92), random.Range(-0.09f, 0.08f)),
                        yaw,
                        random.NextUInt64());
                    for (var fragment = 0; fragment < 5; fragment++)
                    {
                        var fragmentCenter = center + normal * random.Range(1.4f, 4.8f) + direction * random.Range(-3.2f, 3.2f);
                        var fragmentHeight = terrain.SampleHeight(fragmentCenter.X, fragmentCenter.Y);
                        builder.AddFracturedRock(
                            new Vector3(fragmentCenter.X, fragmentHeight - 0.05f, fragmentCenter.Y),
                            new Vector3(random.Range(0.22f, 0.72f), random.Range(0.16f, 0.54f), random.Range(0.24f, 0.78f)),
                            Tint(new Color(104, 104, 99), random.Range(-0.10f, 0.08f)),
                            random.Range(0f, MathHelper.TwoPi),
                            random.NextUInt64());
                    }
                }
            }
            else if (formation.Type == MountainFormationType.Ravine)
            {
                for (var index = 2; index < formation.Points.Count - 2; index += 5)
                {
                    var point = formation.Points[index];
                    var height = terrain.SampleHeight(point.X, point.Y);
                    builder.AddFracturedRock(
                        new Vector3(point.X, height - 0.10f, point.Y),
                        new Vector3(random.Range(1.4f, 2.7f), random.Range(1.6f, 3.3f), random.Range(1.3f, 2.5f)),
                        Tint(new Color(82, 86, 86), random.Range(-0.08f, 0.07f)),
                        random.Range(0f, MathHelper.TwoPi),
                        random.NextUInt64());
                }
            }
            else if (formation.Type == MountainFormationType.ScreeFan)
            {
                // Loose fragments widen downhill with the same footprint used by the terrain
                // mask. Their small, irregular profiles keep the deposit from reading as a ring.
                for (var index = 1; index < formation.Points.Count; index++)
                {
                    var amount = index / (float)(formation.Points.Count - 1);
                    var center = formation.Points[index];
                    var direction = formation.Points[index] - formation.Points[index - 1];
                    if (direction.LengthSquared() < 0.001f) continue;
                    direction.Normalize();
                    var across = new Vector2(-direction.Y, direction.X);
                    var width = MathHelper.Lerp(0.65f, formation.Width, amount);
                    var fragments = 2 + (int)MathF.Round(amount * 5f);
                    for (var fragment = 0; fragment < fragments; fragment++)
                    {
                        var point = center + across * random.Range(-width, width) * random.Range(0.18f, 0.92f);
                        var height = terrain.SampleHeight(point.X, point.Y);
                        var scale = random.Range(0.16f, MathHelper.Lerp(0.30f, 0.64f, amount));
                        builder.AddFracturedRock(
                            new Vector3(point.X, height - 0.04f, point.Y),
                            new Vector3(scale * random.Range(0.7f, 1.5f), scale * random.Range(0.45f, 1.05f), scale * random.Range(0.8f, 1.6f)),
                            Tint(new Color(107, 106, 99), random.Range(-0.11f, 0.08f)),
                            random.Range(0f, MathHelper.TwoPi),
                            random.NextUInt64());
                    }
                }
            }
        }

        // A few large boulder groups turn bare mountain shoulders into discrete formations.
        foreach (var prop in world.Props.Where(prop => prop.Type == PropType.LichenRock).Take(34))
        {
            var count = 2 + random.Next(3);
            for (var index = 0; index < count; index++)
            {
                var angle = random.Range(0f, MathHelper.TwoPi);
                var radius = random.Range(0.3f, 1.5f);
                var x = prop.Position.X + MathF.Cos(angle) * radius;
                var z = prop.Position.Z + MathF.Sin(angle) * radius;
                var y = terrain.SampleHeight(x, z);
                builder.AddFracturedRock(
                    new Vector3(x, y - 0.08f, z),
                    new Vector3(random.Range(0.8f, 1.8f), random.Range(0.7f, 1.7f), random.Range(0.8f, 1.7f)),
                    Tint(new Color(96, 99, 95), random.Range(-0.08f, 0.09f)),
                    random.Range(0f, MathHelper.TwoPi),
                    random.NextUInt64());
            }
        }

        var ridgeOutcrops = 0;
        for (var row = 4; row < terrain.Settings.GridResolution - 4 && ridgeOutcrops < 22; row += 5)
        {
            for (var column = 4; column < terrain.Settings.GridResolution - 4 && ridgeOutcrops < 22; column += 5)
            {
                if (terrain.SurfaceAt(column, row) != TerrainSurface.MountainRock ||
                    terrain.NormalizedHeight(terrain.HeightAt(column, row)) < 0.64f ||
                    terrain.NormalAt(column, row).Y > 0.96f ||
                    random.NextFloat() > 0.31f)
                {
                    continue;
                }

                var position = terrain.PositionAt(column, row);
                builder.AddFracturedRock(
                    position - new Vector3(0f, 0.12f, 0f),
                    new Vector3(random.Range(1.6f, 3.8f), random.Range(0.7f, 1.8f), random.Range(1.1f, 2.8f)),
                    Tint(new Color(88, 92, 92), random.Range(-0.08f, 0.08f)),
                    random.Range(0f, MathHelper.TwoPi),
                    random.NextUInt64());
                ridgeOutcrops++;
            }
        }

        return builder.Build(_graphicsDevice);
    }

    private MeshBuffer BuildForestMasses(GeneratedWorld world)
    {
        var builder = new GeometryBuilder();
        var random = new StableRandom(unchecked((ulong)(uint)world.Seed) ^ 0x513ae7d9086c24bfUL);
        var terrain = world.Terrain;
        // The lowland biome reads as a canopy first. Sample its continuous mask directly so
        // distant forest does not depend on individually legible prop tokens.
        var spacing = terrain.Settings.Size <= 140.01f ? 3.0f : Math.Max(3f, terrain.Settings.Size / 54f);
        var half = terrain.Settings.Size * 0.5f - 2f;
        for (var z = -half; z <= half; z += spacing)
        {
            for (var x = -half; x <= half; x += spacing)
            {
                var jitterX = x + random.Range(-spacing * 0.38f, spacing * 0.38f);
                var jitterZ = z + random.Range(-spacing * 0.38f, spacing * 0.38f);
                var forest = terrain.SampleForestMask(jitterX, jitterZ);
                if (terrain.SampleMountainInfluence(jitterX, jitterZ) > 0.26f || forest < 0.50f || random.NextFloat() > forest * 0.96f ||
                    world.DungeonSites.Any(site => Vector2.Distance(new Vector2(jitterX, jitterZ), new Vector2(site.Position.X, site.Position.Z)) < site.ClearedAreaRadius + 2.5f) ||
                    world.Lakes.Any(lake => Vector2.Distance(new Vector2(jitterX, jitterZ), lake.Center) < Math.Min(lake.Radius.X, lake.Radius.Y) * 0.86f))
                {
                    continue;
                }
                var y = terrain.SampleHeight(jitterX, jitterZ);
                var wet = terrain.SampleWetValleyMask(jitterX, jitterZ);
                var radius = random.Range(1.45f, 2.25f);
                builder.AddSmoothEllipsoid(
                    new Vector3(jitterX, y + random.Range(1.05f, 1.55f), jitterZ),
                    new Vector3(radius * random.Range(0.86f, 1.16f), random.Range(0.72f, 1.12f), radius),
                    Tint(new Color(38, wet > 0.45f ? 55 : 61, 43), random.Range(-0.09f, 0.07f)),
                    random.Range(0f, MathHelper.TwoPi), 8, 3);
            }
        }

        var trees = world.Props.Where(prop => prop.Type is PropType.Tree or PropType.PineTree or PropType.CrookedPine).ToArray();
        foreach (var anchor in trees.Where((_, index) => index % 5 == 0))
        {
            var neighbours = trees.Count(tree => Vector2.Distance(
                new Vector2(tree.Position.X, tree.Position.Z),
                new Vector2(anchor.Position.X, anchor.Position.Z)) < 9f);
            if (neighbours < 3) continue;
            var conifer = anchor.Type is PropType.PineTree or PropType.CrookedPine;
            var color = conifer ? new Color(35, 48, 43) : new Color(44, 58, 45);
            builder.AddSmoothEllipsoid(
                anchor.Position + new Vector3(random.Range(-1.2f, 1.2f), conifer ? 1.05f : 1.18f, random.Range(-1.2f, 1.2f)),
                new Vector3(random.Range(2.4f, 3.9f), random.Range(0.62f, 1.02f), random.Range(2.1f, 3.6f)),
                Tint(color, random.Range(-0.08f, 0.06f)),
                random.Range(0f, MathHelper.TwoPi),
                12,
                4);
        }

        return builder.Build(_graphicsDevice);
    }

    private MeshBuffer BuildGroundCover(GeneratedWorld world)
    {
        var builder = new GeometryBuilder();
        var random = new StableRandom(unchecked((ulong)(uint)world.Seed) ^ 0xb237e659d4018acfUL);
        var anchors = world.Props.Where(prop => prop.Type is PropType.Tree or PropType.PineTree or PropType.Rock or PropType.LichenRock).ToArray();
        var target = Math.Min(560, anchors.Length * 4);
        for (var index = 0; index < target; index++)
        {
            var anchor = anchors[random.Next(anchors.Length)];
            var angle = random.Range(0f, MathHelper.TwoPi);
            var distance = random.Range(0.7f, anchor.Type is PropType.Rock or PropType.LichenRock ? 4.2f : 5.8f);
            var x = anchor.Position.X + MathF.Cos(angle) * distance;
            var z = anchor.Position.Z + MathF.Sin(angle) * distance;
            if (world.DungeonSites.Any(site => Vector2.Distance(new Vector2(x, z), new Vector2(site.Position.X, site.Position.Z)) < site.ClearedAreaRadius + 1.6f) ||
                world.Lakes.Any(lake => Vector2.Distance(new Vector2(x, z), lake.Center) < Math.Min(lake.Radius.X, lake.Radius.Y) * 0.72f))
            {
                continue;
            }

            var y = world.Terrain.SampleHeight(x, z);
            var position = new Vector3(x, y, z);
            var selector = random.NextFloat();
            if (selector < 0.63f)
            {
                var height = random.Range(0.28f, 0.72f);
                builder.AddCrossedCard(position, random.Range(0.22f, 0.48f), height, Tint(new Color(71, 83, 57), random.Range(-0.12f, 0.12f)), angle);
            }
            else if (selector < 0.82f)
            {
                builder.AddFacetedEllipsoid(position + new Vector3(0f, 0.10f, 0f), new Vector3(0.18f, 0.12f, 0.22f), new Color(100, 99, 91), angle, 6);
            }
            else if (selector < 0.94f)
            {
                builder.AddCrossedCard(position, random.Range(0.32f, 0.62f), random.Range(0.42f, 0.88f), new Color(82, 72, 57), angle);
            }
            else
            {
                builder.AddBox(position - new Vector3(0f, 0.05f, 0f), new Vector3(0.16f, 0.15f, random.Range(0.72f, 1.30f)), new Color(75, 61, 49), angle);
            }
        }

        // Wet corridors and clearing edges receive dense local ground cover even when no
        // individual tree prop happens to be nearby.
        for (var index = 0; index < 420; index++)
        {
            var x = random.Range(-world.Terrain.Settings.Size * 0.48f, world.Terrain.Settings.Size * 0.48f);
            var z = random.Range(-world.Terrain.Settings.Size * 0.48f, world.Terrain.Settings.Size * 0.48f);
            if (world.Terrain.SampleMountainInfluence(x, z) > 0.28f) continue;
            var wet = world.Terrain.SampleWetValleyMask(x, z);
            var clearing = world.Terrain.SampleClearingMask(x, z);
            var forestEdge = world.Terrain.SampleForestMask(x, z);
            var acceptance = Math.Max(wet, Math.Max(clearing * 0.72f, 1f - MathF.Abs(forestEdge - 0.50f) * 3.2f));
            if (random.NextFloat() > acceptance * 0.78f) continue;
            var y = world.Terrain.SampleHeight(x, z);
            var color = wet > 0.42f ? new Color(45, 66, 51) : clearing > 0.42f ? new Color(101, 92, 59) : new Color(61, 77, 52);
            builder.AddCrossedCard(new Vector3(x, y, z), random.Range(0.20f, 0.46f), random.Range(0.30f, 0.82f), color, random.Range(0f, MathHelper.TwoPi));
        }

        // Reeds and dark grass around pond margins make the wet material zones spatially legible.
        foreach (var lake in world.Lakes)
        {
            for (var index = 0; index < 46; index++)
            {
                var angle = MathHelper.TwoPi * index / 46f + random.Range(-0.05f, 0.05f);
                var radiusX = lake.Radius.X * random.Range(0.92f, 1.16f);
                var radiusZ = lake.Radius.Y * random.Range(0.92f, 1.16f);
                var x = lake.Center.X + MathF.Cos(angle) * radiusX;
                var z = lake.Center.Y + MathF.Sin(angle) * radiusZ;
                var y = world.Terrain.SampleHeight(x, z);
                builder.AddCrossedCard(
                    new Vector3(x, y, z),
                    random.Range(0.16f, 0.30f),
                    random.Range(0.34f, 0.78f),
                    Tint(new Color(58, 75, 57), random.Range(-0.10f, 0.10f)),
                    angle);
            }
        }

        // Sparse verge objects visually stitch the path into its surroundings.
        foreach (var feature in world.Features.Where(feature => feature.Type == TerrainFeatureType.WornPath))
        {
            for (var index = 1; index < feature.Points.Count - 1; index++)
            {
                var previous = feature.Points[index - 1];
                var next = feature.Points[index + 1];
                var direction = Vector2.Normalize(new Vector2(next.X - previous.X, next.Z - previous.Z));
                var side = new Vector2(-direction.Y, direction.X) * feature.Width * random.Range(0.85f, 1.55f) * (index % 2 == 0 ? 1f : -1f);
                var point = feature.Points[index];
                var x = point.X + side.X;
                var z = point.Z + side.Y;
                var y = world.Terrain.SampleHeight(x, z);
                if (index % 3 == 0)
                {
                    builder.AddFacetedEllipsoid(new Vector3(x, y + 0.12f, z), new Vector3(0.28f, 0.18f, 0.34f), new Color(105, 104, 97), index, 7);
                }
                else
                {
                    builder.AddCrossedCard(new Vector3(x, y, z), 0.38f, random.Range(0.42f, 0.78f), new Color(69, 77, 53), index * 0.71f);
                }
            }
        }

        // A handful of broad flower/scrub colonies, with empty terrain left between colonies.
        for (var patch = 0; patch < 7; patch++)
        {
            var center = new Vector2(random.Range(-56f, 56f), random.Range(-51f, 42f));
            if (world.Terrain.SampleMountainInfluence(center.X, center.Y) > 0.38f) continue;
            for (var item = 0; item < 18; item++)
            {
                var angle = random.Range(0f, MathHelper.TwoPi);
                var distance = MathF.Sqrt(random.NextFloat()) * random.Range(2.5f, 5.8f);
                var x = center.X + MathF.Cos(angle) * distance;
                var z = center.Y + MathF.Sin(angle) * distance;
                var y = world.Terrain.SampleHeight(x, z);
                var flower = item % 4 == 0 ? new Color(112, 103, 132) : new Color(76, 87, 57);
                builder.AddCrossedCard(new Vector3(x, y, z), random.Range(0.12f, 0.25f), random.Range(0.20f, 0.46f), flower, angle);
            }
        }

        return builder.Build(_graphicsDevice);
    }

    private static void AddPathSegment(
        GeometryBuilder builder,
        GeneratedTerrain terrain,
        Vector3 start,
        Vector3 end,
        float width,
        Color color,
        float heightOffset,
        float lateralOffset = 0f)
    {
        var direction = new Vector2(end.X - start.X, end.Z - start.Z);
        if (direction.LengthSquared() < 0.0001f)
        {
            return;
        }

        direction.Normalize();
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var side = perpendicular * (width * 0.5f);
        var offset = perpendicular * lateralOffset;
        Vector3 Ground(float x, float z) => new(x, terrain.SampleHeight(x, z) + heightOffset, z);
        const int subdivisions = 6;
        for (var subdivision = 0; subdivision < subdivisions; subdivision++)
        {
            var from = Vector3.Lerp(start, end, subdivision / (float)subdivisions);
            var to = Vector3.Lerp(start, end, (subdivision + 1) / (float)subdivisions);
            builder.AddGroundQuad(
                Ground(from.X + offset.X - side.X, from.Z + offset.Y - side.Y),
                Ground(from.X + offset.X + side.X, from.Z + offset.Y + side.Y),
                Ground(to.X + offset.X + side.X, to.Z + offset.Y + side.Y),
                Ground(to.X + offset.X - side.X, to.Z + offset.Y - side.Y),
                color);
        }
    }

    private MeshBuffer BuildProps(GeneratedWorld world, Func<PropPlacement, bool> include)
    {
        var builder = new GeometryBuilder();
        foreach (var prop in world.Props)
        {
            if (!include(prop))
            {
                continue;
            }

            switch (prop.Type)
            {
                case PropType.Tree:
                    AddTree(builder, prop);
                    break;
                case PropType.Shrub:
                    AddShrub(builder, prop);
                    break;
                case PropType.Rock:
                    AddRock(builder, prop);
                    break;
                case PropType.PineTree:
                    AddPine(builder, prop, world.Terrain.SampleSurface(prop.Position.X, prop.Position.Z));
                    break;
                case PropType.CrookedPine:
                    AddCrookedPine(builder, prop);
                    break;
                case PropType.AlpineShrub:
                    AddAlpineShrub(builder, prop, world.Terrain.SampleSurface(prop.Position.X, prop.Position.Z));
                    break;
                case PropType.LichenRock:
                    AddLichenRock(builder, prop);
                    break;
                case PropType.DeadConifer:
                    AddDeadConifer(builder, prop);
                    break;
                case PropType.DeadTree:
                    AddDeadTree(builder, prop);
                    break;
                case PropType.FallenLog:
                    AddFallenLog(builder, prop);
                    break;
                case PropType.Stump:
                    AddStump(builder, prop);
                    break;
            }
        }

        return builder.Build(_graphicsDevice);
    }

    private MeshBuffer BuildShadows(GeneratedWorld world)
    {
        var builder = new GeometryBuilder();
        var random = new StableRandom(unchecked((ulong)(uint)world.Seed) ^ 0x94d501bf3762a8ceUL);
        var shadowDirection = TerrainLighting.ShadowGroundDirection;
        Vector3 SunOffset(float distance) => new(shadowDirection.X * distance, 0f, shadowDirection.Y * distance);
        foreach (var site in world.DungeonSites)
        {
            builder.AddHorizontalEllipse(
                site.Position + SunOffset(0.48f) + new Vector3(0f, 0.025f, 0f),
                3.65f,
                2.35f,
                new Color(27, 34, 40, 64),
                site.OrientationRadians,
                18);
        }

        foreach (var prop in world.Props)
        {
            switch (prop.Type)
            {
                case PropType.Tree:
                    builder.AddHorizontalEllipse(prop.Position, 0.64f * prop.Scale.X, 0.54f * prop.Scale.Z, new Color(18, 23, 24, 108), prop.RotationRadians, 10);
                    builder.AddHorizontalEllipse(
                        prop.Position + SunOffset(1.10f * Math.Max(prop.Scale.X, prop.Scale.Z)),
                        2.25f * prop.Scale.X,
                        1.22f * prop.Scale.Z,
                        new Color(28, 36, 43, 72),
                        prop.RotationRadians + 0.28f,
                        14);
                    break;
                case PropType.Shrub:
                    builder.AddHorizontalEllipse(prop.Position, 0.46f * prop.Scale.X, 0.38f * prop.Scale.Z, new Color(20, 25, 25, 82), prop.RotationRadians, 9);
                    builder.AddHorizontalEllipse(
                        prop.Position + SunOffset(0.20f * Math.Max(prop.Scale.X, prop.Scale.Z)),
                        0.72f * prop.Scale.X,
                        0.44f * prop.Scale.Z,
                        new Color(31, 39, 43, 42),
                        prop.RotationRadians,
                        10);
                    break;
                case PropType.Rock:
                    builder.AddHorizontalEllipse(prop.Position, 0.74f * prop.Scale.X, 0.58f * prop.Scale.Z, new Color(18, 22, 23, 88), prop.RotationRadians, 10);
                    break;
                case PropType.PineTree:
                    builder.AddHorizontalEllipse(prop.Position, 0.56f * prop.Scale.X, 0.48f * prop.Scale.Z, new Color(17, 22, 23, 106), prop.RotationRadians, 10);
                    builder.AddHorizontalEllipse(
                        prop.Position + SunOffset(0.92f * Math.Max(prop.Scale.X, prop.Scale.Z)),
                        1.82f * prop.Scale.X,
                        0.88f * prop.Scale.Z,
                        new Color(27, 35, 42, 68),
                        prop.RotationRadians + 0.22f,
                        14);
                    break;
                case PropType.CrookedPine:
                    builder.AddHorizontalEllipse(prop.Position, 0.52f * prop.Scale.X, 0.42f * prop.Scale.Z, new Color(17, 22, 23, 98), prop.RotationRadians, 10);
                    builder.AddHorizontalEllipse(
                        prop.Position + SunOffset(0.58f * Math.Max(prop.Scale.X, prop.Scale.Z)),
                        1.25f * prop.Scale.X,
                        0.68f * prop.Scale.Z,
                        new Color(27, 35, 42, 62),
                        prop.RotationRadians + 0.22f,
                        12);
                    break;
                case PropType.AlpineShrub:
                    builder.AddHorizontalEllipse(
                        prop.Position + SunOffset(0.17f * Math.Max(prop.Scale.X, prop.Scale.Z)),
                        0.62f * prop.Scale.X,
                        0.38f * prop.Scale.Z,
                        new Color(30, 38, 43, 44),
                        prop.RotationRadians,
                        10);
                    break;
                case PropType.LichenRock:
                    builder.AddHorizontalEllipse(prop.Position, 0.72f * prop.Scale.X, 0.54f * prop.Scale.Z, new Color(18, 22, 23, 92), prop.RotationRadians, 10);
                    builder.AddHorizontalEllipse(
                        prop.Position + SunOffset(0.13f),
                        0.92f * prop.Scale.X,
                        0.62f * prop.Scale.Z,
                        new Color(29, 36, 41, 48),
                        prop.RotationRadians,
                        10);
                    break;
                case PropType.DeadConifer:
                    builder.AddHorizontalEllipse(
                        prop.Position + SunOffset(0.45f),
                        1.28f * prop.Scale.X,
                        0.48f * prop.Scale.Z,
                        new Color(28, 35, 41, 54),
                        prop.RotationRadians,
                        12);
                    break;
                case PropType.DeadTree:
                    builder.AddHorizontalEllipse(
                        prop.Position + SunOffset(0.50f),
                        1.4f * prop.Scale.X,
                        0.55f * prop.Scale.Z,
                        new Color(28, 35, 41, 56),
                        prop.RotationRadians,
                        12);
                    break;
                case PropType.FallenLog:
                    builder.AddHorizontalEllipse(
                        prop.Position,
                        0.65f * prop.Scale.X,
                        2.3f * prop.Scale.Z,
                        new Color(29, 36, 41, 54),
                        prop.RotationRadians,
                        12);
                    break;
                case PropType.Stump:
                    builder.AddHorizontalEllipse(prop.Position, 0.52f * prop.Scale.X, 0.46f * prop.Scale.Z, new Color(18, 22, 23, 82), prop.RotationRadians, 9);
                    break;
            }
        }

        var trees = world.Props.Where(prop => prop.Type is PropType.Tree or PropType.PineTree or PropType.CrookedPine).ToArray();
        foreach (var anchor in trees.Where((_, index) => index % 7 == 0))
        {
            var neighbours = trees.Count(tree => Vector2.Distance(
                new Vector2(tree.Position.X, tree.Position.Z),
                new Vector2(anchor.Position.X, anchor.Position.Z)) < 8f);
            if (neighbours >= 3)
            {
                builder.AddHorizontalEllipse(anchor.Position, 3.1f, 2.5f, new Color(18, 24, 24, 42), anchor.RotationRadians, 14);
            }
        }

        // Distant canopy masses receive broad, low-alpha contact pools. They disappear as
        // distinct decals at map distance but keep the forest base tied to the terrain.
        var terrain = world.Terrain;
        var canopySpacing = Math.Max(6.5f, terrain.Settings.Size / 34f);
        var half = terrain.Settings.Size * 0.5f - canopySpacing;
        for (var z = -half; z <= half; z += canopySpacing)
        {
            for (var x = -half; x <= half; x += canopySpacing)
            {
                var jitterX = x + random.Range(-canopySpacing * 0.32f, canopySpacing * 0.32f);
                var jitterZ = z + random.Range(-canopySpacing * 0.32f, canopySpacing * 0.32f);
                var density = terrain.SampleForestMask(jitterX, jitterZ);
                if (density < 0.56f || random.NextFloat() > density * 0.72f || terrain.SampleMountainInfluence(jitterX, jitterZ) > 0.28f)
                {
                    continue;
                }

                var position = new Vector3(jitterX, terrain.SampleHeight(jitterX, jitterZ), jitterZ) + SunOffset(0.38f);
                builder.AddHorizontalEllipse(position, random.Range(2.4f, 3.8f), random.Range(1.8f, 2.9f),
                    new Color(24, 31, 34, 28), random.Range(0f, MathHelper.TwoPi), 10);
            }
        }

        foreach (var formation in world.MountainFormations.Where(formation => formation.Type == MountainFormationType.CliffFace))
        {
            for (var index = 1; index < formation.Points.Count - 1; index += 2)
            {
                var point = formation.Points[index];
                var position = new Vector3(point.X, world.Terrain.SampleHeight(point.X, point.Y), point.Y);
                builder.AddHorizontalEllipse(position, random.Range(2.2f, 4.6f), random.Range(1.2f, 2.4f), new Color(18, 22, 23, 76), random.Range(0f, MathHelper.TwoPi), 12);
            }
        }

        return builder.Build(_graphicsDevice);
    }

    private MeshBuffer BuildLandmark(DungeonSite site)
    {
        var builder = new GeometryBuilder();
        Color[] stones =
        [
            new Color(96, 99, 96),
            new Color(107, 108, 101),
            new Color(119, 116, 104),
            new Color(101, 103, 98),
        ];
        var doorway = new Color(16, 21, 20);
        var ember = new Color(182, 132, 62);
        var yaw = site.OrientationRadians;

        builder.AddHorizontalRing(site.Position, 3.55f, 4.05f, new Color(108, 101, 85), 24);
        builder.AddBox(
            LocalOffset(site.Position, new Vector3(0f, 0.06f, 3.1f), yaw),
            new Vector3(2.9f, 0.10f, 5.2f),
            new Color(105, 95, 76),
            yaw);

        for (var sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            var side = sideIndex == 0 ? -1f : 1f;
            for (var course = 0; course < 4; course++)
            {
                var offset = new Vector3(
                    side * (2.24f + (course % 2 == 0 ? 0.05f : -0.04f)),
                    course * 1.03f,
                    course % 2 == 0 ? 0.04f : -0.05f);
                builder.AddBox(
                    LocalOffset(site.Position, offset, yaw),
                    new Vector3(1.20f - course * 0.035f, 1.08f, 1.38f - course * 0.025f),
                    stones[(course + sideIndex) % stones.Length],
                    yaw);
            }
        }

        for (var block = 0; block < 5; block++)
        {
            builder.AddBox(
                LocalOffset(site.Position, new Vector3(-2.34f + block * 1.17f, 4.08f + (block % 2) * 0.025f, 0f), yaw),
                new Vector3(1.20f, 1.08f, 1.46f),
                stones[(block + 2) % stones.Length],
                yaw);
        }

        builder.AddBox(LocalOffset(site.Position, new Vector3(0f, 0.05f, -0.42f), yaw), new Vector3(3.25f, 3.15f, 0.28f), doorway, yaw);
        builder.AddFacetedEllipsoid(LocalOffset(site.Position, new Vector3(-3.1f, 0.75f, 0.15f), yaw), new Vector3(0.75f, 0.78f, 0.65f), stones[1], yaw, 7);
        builder.AddFacetedEllipsoid(LocalOffset(site.Position, new Vector3(3.0f, 0.55f, -0.05f), yaw), new Vector3(0.58f, 0.62f, 0.72f), stones[3], yaw, 7);
        builder.AddOctahedron(LocalOffset(site.Position, new Vector3(-1.45f, 3.25f, 0.84f), yaw), new Vector3(0.22f, 0.55f, 0.22f), ember, yaw);
        builder.AddOctahedron(LocalOffset(site.Position, new Vector3(1.45f, 3.25f, 0.84f), yaw), new Vector3(0.22f, 0.55f, 0.22f), ember, yaw);
        return builder.Build(_graphicsDevice);
    }

    private MeshBuffer BuildSelectionMarker()
    {
        var builder = new GeometryBuilder();
        builder.AddHorizontalRing(Vector3.Zero, 5.55f, 5.86f, new Color(194, 154, 78, 176), 32);
        builder.AddOctahedron(new Vector3(0f, 5.9f, 0f), new Vector3(0.48f, 0.72f, 0.48f), new Color(213, 171, 88), 0f);
        return builder.Build(_graphicsDevice);
    }

    private static void AddTree(GeometryBuilder builder, PropPlacement prop)
    {
        var frame = FrameFor(prop);
        var trunk = Tint(new Color(76, 65, 54), prop.ColorVariation * 0.30f);
        var leaves = Tint(new Color(58, 71, 55), prop.ColorVariation * 0.72f);
        var sunLeaves = Tint(new Color(76, 83, 62), prop.ColorVariation * 0.68f);
        builder.AddOrientedBox(
            prop.Position - frame.Up * 0.11f,
            new Vector3(0.27f * prop.Scale.X, 4.35f * prop.Scale.Y, 0.27f * prop.Scale.Z),
            trunk,
            frame.Right,
            frame.Up,
            frame.Forward);
        builder.AddSmoothEllipsoid(
            PropPoint(prop, frame, new Vector3(-0.18f * prop.Scale.X, 4.55f * prop.Scale.Y, 0.12f * prop.Scale.Z)),
            new Vector3(1.18f * prop.Scale.X, 1.08f * prop.Scale.Y, 1.02f * prop.Scale.Z),
            leaves,
            prop.RotationRadians,
            14,
            6);
        builder.AddSmoothEllipsoid(
            PropPoint(prop, frame, new Vector3(-0.62f * prop.Scale.X, 4.18f * prop.Scale.Y, 0.18f * prop.Scale.Z)),
            new Vector3(0.82f * prop.Scale.X, 0.68f * prop.Scale.Y, 0.74f * prop.Scale.Z),
            sunLeaves,
            prop.RotationRadians,
            12,
            5);
        builder.AddSmoothEllipsoid(
            PropPoint(prop, frame, new Vector3(0.58f * prop.Scale.X, 4.34f * prop.Scale.Y, -0.24f * prop.Scale.Z)),
            new Vector3(0.78f * prop.Scale.X, 0.72f * prop.Scale.Y, 0.70f * prop.Scale.Z),
            leaves,
            -prop.RotationRadians,
            12,
            5);
        builder.AddSmoothEllipsoid(
            PropPoint(prop, frame, new Vector3(0.10f * prop.Scale.X, 5.22f * prop.Scale.Y, 0f)),
            new Vector3(0.68f * prop.Scale.X, 0.76f * prop.Scale.Y, 0.62f * prop.Scale.Z),
            sunLeaves,
            prop.RotationRadians * 0.45f,
            12,
            5);
    }

    private static void AddShrub(GeometryBuilder builder, PropPlacement prop)
    {
        var frame = FrameFor(prop);
        var deep = Tint(new Color(67, 79, 61), prop.ColorVariation * 0.72f - 0.02f);
        var light = Tint(new Color(82, 91, 67), prop.ColorVariation * 0.72f);
        builder.AddSmoothEllipsoid(
            PropPoint(prop, frame, new Vector3(-0.38f * prop.Scale.X, 0.37f * prop.Scale.Y, 0f)),
            new Vector3(0.68f * prop.Scale.X, 0.46f * prop.Scale.Y, 0.61f * prop.Scale.Z),
            deep,
            prop.RotationRadians,
            10,
            4);
        builder.AddSmoothEllipsoid(
            PropPoint(prop, frame, new Vector3(0.34f * prop.Scale.X, 0.40f * prop.Scale.Y, 0.10f * prop.Scale.Z)),
            new Vector3(0.62f * prop.Scale.X, 0.50f * prop.Scale.Y, 0.58f * prop.Scale.Z),
            light,
            -prop.RotationRadians,
            10,
            4);
        builder.AddSmoothEllipsoid(
            PropPoint(prop, frame, new Vector3(0f, 0.47f * prop.Scale.Y, -0.28f * prop.Scale.Z)),
            new Vector3(0.54f * prop.Scale.X, 0.48f * prop.Scale.Y, 0.52f * prop.Scale.Z),
            deep,
            prop.RotationRadians * 0.5f,
            10,
            4);
    }

    private static void AddRock(GeometryBuilder builder, PropPlacement prop)
    {
        var rock = Tint(new Color(115, 114, 108), prop.ColorVariation * 0.45f);
        var radii = new Vector3(prop.Scale.X, prop.Scale.Y * 0.78f, prop.Scale.Z) * 0.72f;
        builder.AddFacetedEllipsoid(prop.Position + new Vector3(0f, radii.Y * 0.58f, 0f), radii, rock, prop.RotationRadians, 8);
    }

    private static void AddPine(
        GeometryBuilder builder,
        PropPlacement prop,
        TerrainSurface surface)
    {
        var frame = FrameFor(prop);
        var trunk = Tint(new Color(74, 65, 55), prop.ColorVariation * 0.28f);
        var needles = Tint(new Color(48, 62, 54), prop.ColorVariation * 0.62f);
        builder.AddOrientedBox(
            prop.Position - frame.Up * 0.12f,
            new Vector3(0.22f * prop.Scale.X, 5.15f * prop.Scale.Y, 0.22f * prop.Scale.Z),
            trunk,
            frame.Right,
            frame.Up,
            frame.Forward);
        builder.AddOrientedCone(
            PropPoint(prop, frame, new Vector3(0f, 1.25f * prop.Scale.Y, 0f)),
            1.12f * prop.Scale.X,
            1.08f * prop.Scale.Z,
            3.35f * prop.Scale.Y,
            needles,
            frame.Right,
            frame.Up,
            frame.Forward,
            11);
        builder.AddOrientedCone(
            PropPoint(prop, frame, new Vector3(0f, 2.45f * prop.Scale.Y, 0f)),
            0.88f * prop.Scale.X,
            0.84f * prop.Scale.Z,
            2.95f * prop.Scale.Y,
            Tint(needles, 0.035f),
            frame.Right,
            frame.Up,
            frame.Forward,
            11);
        builder.AddOrientedCone(
            PropPoint(prop, frame, new Vector3(0f, 3.55f * prop.Scale.Y, 0f)),
            0.61f * prop.Scale.X,
            0.58f * prop.Scale.Z,
            2.22f * prop.Scale.Y,
            Tint(needles, -0.025f),
            frame.Right,
            frame.Up,
            frame.Forward,
            10);

        if (surface == TerrainSurface.MountainVegetatedSnow)
        {
            builder.AddSmoothEllipsoid(
                PropPoint(prop, frame, new Vector3(0f, 5.63f * prop.Scale.Y, 0f)),
                new Vector3(0.73f * prop.Scale.X, 0.22f * prop.Scale.Y, 0.69f * prop.Scale.Z),
                new Color(192, 201, 198),
                prop.RotationRadians,
                14,
                4);
        }
    }

    private static void AddCrookedPine(GeometryBuilder builder, PropPlacement prop)
    {
        var frame = FrameFor(prop);
        var trunk = Tint(new Color(78, 68, 57), prop.ColorVariation * 0.28f);
        var deepNeedles = Tint(new Color(43, 56, 50), prop.ColorVariation * 0.58f);
        var windNeedles = Tint(new Color(54, 66, 55), prop.ColorVariation * 0.52f);
        builder.AddOrientedBox(
            prop.Position - frame.Up * 0.13f,
            new Vector3(0.28f * prop.Scale.X, 3.45f * prop.Scale.Y, 0.27f * prop.Scale.Z),
            trunk,
            frame.Right,
            frame.Up,
            frame.Forward);
        builder.AddSmoothEllipsoid(
            PropPoint(prop, frame, new Vector3(0.30f * prop.Scale.X, 2.45f * prop.Scale.Y, 0f)),
            new Vector3(1.18f * prop.Scale.X, 0.72f * prop.Scale.Y, 0.88f * prop.Scale.Z),
            deepNeedles,
            prop.RotationRadians,
            12,
            5);
        builder.AddSmoothEllipsoid(
            PropPoint(prop, frame, new Vector3(0.58f * prop.Scale.X, 3.17f * prop.Scale.Y, 0.06f * prop.Scale.Z)),
            new Vector3(0.98f * prop.Scale.X, 0.65f * prop.Scale.Y, 0.72f * prop.Scale.Z),
            windNeedles,
            prop.RotationRadians + 0.24f,
            12,
            5);
        builder.AddSmoothEllipsoid(
            PropPoint(prop, frame, new Vector3(0.72f * prop.Scale.X, 3.72f * prop.Scale.Y, 0f)),
            new Vector3(0.58f * prop.Scale.X, 0.50f * prop.Scale.Y, 0.48f * prop.Scale.Z),
            deepNeedles,
            prop.RotationRadians - 0.18f,
            10,
            4);
    }

    private static void AddAlpineShrub(
        GeometryBuilder builder,
        PropPlacement prop,
        TerrainSurface surface)
    {
        var frame = FrameFor(prop);
        var needles = Tint(new Color(53, 67, 57), prop.ColorVariation * 0.66f);
        var lighter = Tint(new Color(67, 77, 63), prop.ColorVariation * 0.58f);
        builder.AddSmoothEllipsoid(
            PropPoint(prop, frame, new Vector3(-0.24f * prop.Scale.X, 0.38f * prop.Scale.Y, 0f)),
            new Vector3(0.62f * prop.Scale.X, 0.43f * prop.Scale.Y, 0.56f * prop.Scale.Z),
            needles,
            prop.RotationRadians,
            10,
            4);
        builder.AddSmoothEllipsoid(
            PropPoint(prop, frame, new Vector3(0.28f * prop.Scale.X, 0.41f * prop.Scale.Y, -0.08f * prop.Scale.Z)),
            new Vector3(0.58f * prop.Scale.X, 0.46f * prop.Scale.Y, 0.54f * prop.Scale.Z),
            lighter,
            -prop.RotationRadians,
            10,
            4);
        if (surface == TerrainSurface.MountainVegetatedSnow)
        {
            builder.AddHorizontalEllipse(
                PropPoint(prop, frame, new Vector3(0f, 0.78f * prop.Scale.Y, 0f)),
                0.42f * prop.Scale.X,
                0.32f * prop.Scale.Z,
                new Color(190, 199, 195, 215),
                prop.RotationRadians,
                10);
        }
    }

    private static void AddLichenRock(GeometryBuilder builder, PropPlacement prop)
    {
        var rock = Tint(new Color(103, 104, 99), prop.ColorVariation * 0.38f);
        var shadowRock = Tint(new Color(88, 91, 88), prop.ColorVariation * 0.32f);
        var lichen = Tint(new Color(110, 116, 87), prop.ColorVariation * 0.48f);
        builder.AddFacetedEllipsoid(
            prop.Position + new Vector3(-0.18f * prop.Scale.X, 0.36f * prop.Scale.Y, 0f),
            new Vector3(0.72f * prop.Scale.X, 0.52f * prop.Scale.Y, 0.66f * prop.Scale.Z),
            rock,
            prop.RotationRadians,
            7);
        builder.AddFacetedEllipsoid(
            prop.Position + new Vector3(0.52f * prop.Scale.X, 0.24f * prop.Scale.Y, 0.18f * prop.Scale.Z),
            new Vector3(0.46f * prop.Scale.X, 0.34f * prop.Scale.Y, 0.50f * prop.Scale.Z),
            shadowRock,
            -prop.RotationRadians,
            7);
        builder.AddHorizontalEllipse(
            prop.Position + new Vector3(-0.24f * prop.Scale.X, 0.88f * prop.Scale.Y, -0.03f * prop.Scale.Z),
            0.34f * prop.Scale.X,
            0.27f * prop.Scale.Z,
            lichen,
            prop.RotationRadians,
            9);
    }

    private static void AddDeadConifer(GeometryBuilder builder, PropPlacement prop)
    {
        var frame = FrameFor(prop);
        var bark = Tint(new Color(83, 76, 67), prop.ColorVariation * 0.38f);
        var weathered = Tint(new Color(105, 99, 86), prop.ColorVariation * 0.30f);
        builder.AddOrientedBox(
            prop.Position - frame.Up * 0.16f,
            new Vector3(0.28f * prop.Scale.X, 4.0f * prop.Scale.Y, 0.27f * prop.Scale.Z),
            bark,
            frame.Right,
            frame.Up,
            frame.Forward);
        builder.AddOrientedBox(
            PropPoint(prop, frame, new Vector3(-0.72f * prop.Scale.X, 2.30f * prop.Scale.Y, 0f)),
            new Vector3(1.45f * prop.Scale.X, 0.14f * prop.Scale.Y, 0.14f * prop.Scale.Z),
            weathered,
            frame.Right,
            frame.Up,
            frame.Forward);
        builder.AddOrientedBox(
            PropPoint(prop, frame, new Vector3(0f, 3.05f * prop.Scale.Y, -0.58f * prop.Scale.Z)),
            new Vector3(0.13f * prop.Scale.X, 0.13f * prop.Scale.Y, 1.18f * prop.Scale.Z),
            bark,
            frame.Right,
            frame.Up,
            frame.Forward);
    }

    private static void AddDeadTree(GeometryBuilder builder, PropPlacement prop)
    {
        var frame = FrameFor(prop);
        var bark = Tint(new Color(91, 78, 65), prop.ColorVariation * 0.45f);
        builder.AddOrientedBox(
            prop.Position - frame.Up * 0.15f,
            new Vector3(0.30f * prop.Scale.X, 4.8f * prop.Scale.Y, 0.30f * prop.Scale.Z),
            bark,
            frame.Right,
            frame.Up,
            frame.Forward);
        builder.AddOrientedBox(
            PropPoint(prop, frame, new Vector3(0f, 2.9f * prop.Scale.Y, 0f)),
            new Vector3(1.65f * prop.Scale.X, 0.18f * prop.Scale.Y, 0.18f * prop.Scale.Z),
            bark,
            frame.Right,
            frame.Up,
            frame.Forward);
        builder.AddOrientedBox(
            PropPoint(prop, frame, new Vector3(0f, 3.7f * prop.Scale.Y, 0f)),
            new Vector3(0.16f * prop.Scale.X, 0.16f * prop.Scale.Y, 1.25f * prop.Scale.Z),
            bark,
            frame.Right,
            frame.Up,
            frame.Forward);
    }

    private static void AddFallenLog(GeometryBuilder builder, PropPlacement prop)
    {
        var frame = FrameFor(prop);
        var bark = Tint(new Color(88, 72, 58), prop.ColorVariation * 0.45f);
        builder.AddOrientedBox(
            prop.Position - frame.Up * 0.18f,
            new Vector3(0.48f * prop.Scale.X, 0.44f * prop.Scale.Y, 4.1f * prop.Scale.Z),
            bark,
            frame.Right,
            frame.Up,
            frame.Forward);
    }

    private static void AddStump(GeometryBuilder builder, PropPlacement prop)
    {
        var bark = Tint(new Color(88, 72, 57), prop.ColorVariation * 0.45f);
        var cut = Tint(new Color(137, 116, 85), prop.ColorVariation * 0.25f);
        builder.AddFacetedEllipsoid(
            prop.Position + new Vector3(0f, 0.28f * prop.Scale.Y, 0f),
            new Vector3(0.48f * prop.Scale.X, 0.34f * prop.Scale.Y, 0.48f * prop.Scale.Z),
            bark,
            prop.RotationRadians,
            9);
        builder.AddHorizontalEllipse(
            prop.Position + new Vector3(0f, 0.63f * prop.Scale.Y, 0f),
            0.38f * prop.Scale.X,
            0.38f * prop.Scale.Z,
            cut,
            prop.RotationRadians,
            12);
    }

    private static PropFrame FrameFor(PropPlacement prop)
    {
        var leanDirection = new Vector3(
            MathF.Cos(prop.LeanDirectionRadians),
            0f,
            MathF.Sin(prop.LeanDirectionRadians));
        var up = Vector3.Lerp(Vector3.Up, prop.SurfaceNormal, 0.38f) + leanDirection * MathF.Tan(prop.LeanRadians);
        up.Normalize();
        var heading = new Vector3(MathF.Sin(prop.RotationRadians), 0f, MathF.Cos(prop.RotationRadians));
        var right = Vector3.Cross(heading, up);
        if (right.LengthSquared() < 0.0001f)
        {
            right = Vector3.Right;
        }

        right.Normalize();
        var forward = Vector3.Cross(up, right);
        forward.Normalize();
        return new PropFrame(right, up, forward);
    }

    private static Vector3 PropPoint(PropPlacement prop, PropFrame frame, Vector3 local) =>
        prop.Position + frame.Right * local.X + frame.Up * local.Y + frame.Forward * local.Z;

    private static Vector3 LocalOffset(Vector3 origin, Vector3 local, float yaw)
    {
        var cosine = MathF.Cos(yaw);
        var sine = MathF.Sin(yaw);
        return origin + new Vector3(local.X * cosine - local.Z * sine, local.Y, local.X * sine + local.Z * cosine);
    }

    private readonly record struct PropFrame(Vector3 Right, Vector3 Up, Vector3 Forward);

    private enum MapCameraAngle
    {
        Top,
        Slight,
        Steep,
    }

    private static Color ToColor(Rgb color) => new(color.R, color.G, color.B);

    private static MapCameraAngle ParseCameraAngle(string? value) => value?.ToLowerInvariant() switch
    {
        "top" => MapCameraAngle.Top,
        "slight" => MapCameraAngle.Slight,
        _ => MapCameraAngle.Steep,
    };

    private static float RemapSmooth(float from, float to, float value)
    {
        var amount = MathHelper.Clamp((value - from) / (to - from), 0f, 1f);
        return amount * amount * (3f - 2f * amount);
    }

    private static Color Tint(Color color, float amount)
    {
        var multiplier = 1f + amount;
        return new Color(
            (byte)Math.Clamp((int)(color.R * multiplier), 0, 255),
            (byte)Math.Clamp((int)(color.G * multiplier), 0, 255),
            (byte)Math.Clamp((int)(color.B * multiplier), 0, 255),
            color.A);
    }

    public void Dispose()
    {
        Scene.Dispose();
        _terrain.Dispose();
        _terrainMaterial.Dispose();
        _terrainFeatures.Dispose();
        _rockFormations.Dispose();
        _forestMasses.Dispose();
        _groundCover.Dispose();
        _water.Dispose();
        _shadows.Dispose();
        _lowlandCanopyProps.Dispose();
        _lowlandGroundProps.Dispose();
        _mountainProps.Dispose();
        foreach (var landmark in _landmarks)
        {
            landmark.Dispose();
        }

        _selectionMarker.Dispose();
        _effect.Dispose();
        _skyEffect.Dispose();
        _rasterizerState.Dispose();
        _depthRead.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct HillsVertex : IVertexType
    {
        public static readonly VertexDeclaration Declaration = new(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0),
            new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

        public HillsVertex(Vector3 position, Vector3 normal, Color color, Vector2 textureCoordinate = default)
        {
            Position = position;
            Normal = normal;
            Color = color;
            TextureCoordinate = textureCoordinate;
        }

        public Vector3 Position { get; }
        public Vector3 Normal { get; }
        public Color Color { get; }
        public Vector2 TextureCoordinate { get; }
        VertexDeclaration IVertexType.VertexDeclaration => Declaration;
    }

    private sealed class MeshBuffer : IDisposable
    {
        public MeshBuffer(GraphicsDevice graphicsDevice, HillsVertex[] vertices, ushort[] indices)
        {
            PrimitiveCount = indices.Length / 3;
            Vertices = new VertexBuffer(graphicsDevice, HillsVertex.Declaration, Math.Max(1, vertices.Length), BufferUsage.WriteOnly);
            Indices = new IndexBuffer(graphicsDevice, IndexElementSize.SixteenBits, Math.Max(1, indices.Length), BufferUsage.WriteOnly);
            if (vertices.Length > 0)
            {
                Vertices.SetData(vertices);
                Indices.SetData(indices);
            }
        }

        public VertexBuffer Vertices { get; }
        public IndexBuffer Indices { get; }
        public int PrimitiveCount { get; }

        public void Dispose()
        {
            Vertices.Dispose();
            Indices.Dispose();
        }
    }

    private sealed class GeometryBuilder
    {
        private readonly List<HillsVertex> _vertices = [];
        private readonly List<ushort> _indices = [];

        public void AddBox(Vector3 baseCenter, Vector3 size, Color color, float yaw)
        {
            var halfX = size.X * 0.5f;
            var halfZ = size.Z * 0.5f;
            Vector3[] points =
            [
                Transform(new Vector3(-halfX, 0f, -halfZ), baseCenter, yaw),
                Transform(new Vector3(halfX, 0f, -halfZ), baseCenter, yaw),
                Transform(new Vector3(halfX, size.Y, -halfZ), baseCenter, yaw),
                Transform(new Vector3(-halfX, size.Y, -halfZ), baseCenter, yaw),
                Transform(new Vector3(-halfX, 0f, halfZ), baseCenter, yaw),
                Transform(new Vector3(halfX, 0f, halfZ), baseCenter, yaw),
                Transform(new Vector3(halfX, size.Y, halfZ), baseCenter, yaw),
                Transform(new Vector3(-halfX, size.Y, halfZ), baseCenter, yaw),
            ];
            AddQuad(points[0], points[1], points[2], points[3], color);
            AddQuad(points[5], points[4], points[7], points[6], color);
            AddQuad(points[4], points[0], points[3], points[7], color);
            AddQuad(points[1], points[5], points[6], points[2], color);
            AddQuad(points[3], points[2], points[6], points[7], color);
            AddQuad(points[4], points[5], points[1], points[0], color);
        }

        public void AddOrientedBox(
            Vector3 baseCenter,
            Vector3 size,
            Color color,
            Vector3 right,
            Vector3 up,
            Vector3 forward)
        {
            var halfX = size.X * 0.5f;
            var halfZ = size.Z * 0.5f;
            Vector3 TransformOriented(float x, float y, float z) =>
                baseCenter + right * x + up * y + forward * z;
            Vector3[] points =
            [
                TransformOriented(-halfX, 0f, -halfZ),
                TransformOriented(halfX, 0f, -halfZ),
                TransformOriented(halfX, size.Y, -halfZ),
                TransformOriented(-halfX, size.Y, -halfZ),
                TransformOriented(-halfX, 0f, halfZ),
                TransformOriented(halfX, 0f, halfZ),
                TransformOriented(halfX, size.Y, halfZ),
                TransformOriented(-halfX, size.Y, halfZ),
            ];
            AddQuad(points[0], points[1], points[2], points[3], color);
            AddQuad(points[5], points[4], points[7], points[6], color);
            AddQuad(points[4], points[0], points[3], points[7], color);
            AddQuad(points[1], points[5], points[6], points[2], color);
            AddQuad(points[3], points[2], points[6], points[7], color);
            AddQuad(points[4], points[5], points[1], points[0], color);
        }

        public void AddGroundQuad(Vector3 first, Vector3 second, Vector3 third, Vector3 fourth, Color color) =>
            AddQuad(first, second, third, fourth, color);

        public void AddOrientedCone(
            Vector3 baseCenter,
            float radiusX,
            float radiusZ,
            float height,
            Color color,
            Vector3 right,
            Vector3 up,
            Vector3 forward,
            int sides)
        {
            var apex = baseCenter + up * height;
            for (var side = 0; side < sides; side++)
            {
                var angle = MathHelper.TwoPi * side / sides;
                var nextAngle = MathHelper.TwoPi * (side + 1) / sides;
                var edge = baseCenter + right * (MathF.Cos(angle) * radiusX) +
                           forward * (MathF.Sin(angle) * radiusZ);
                var nextEdge = baseCenter + right * (MathF.Cos(nextAngle) * radiusX) +
                               forward * (MathF.Sin(nextAngle) * radiusZ);
                AddTriangle(apex, nextEdge, edge, color);
            }
        }

        public void AddFacetedEllipsoid(Vector3 center, Vector3 radii, Color color, float yaw, int sides)
        {
            var top = center + new Vector3(0f, radii.Y, 0f);
            var bottom = center - new Vector3(0f, radii.Y, 0f);
            var upper = new Vector3[sides];
            var lower = new Vector3[sides];
            for (var side = 0; side < sides; side++)
            {
                var angle = MathHelper.TwoPi * side / sides + yaw;
                var direction = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
                upper[side] = center + new Vector3(direction.X * radii.X * 0.82f, radii.Y * 0.30f, direction.Z * radii.Z * 0.82f);
                lower[side] = center + new Vector3(direction.X * radii.X, -radii.Y * 0.34f, direction.Z * radii.Z);
            }

            for (var side = 0; side < sides; side++)
            {
                var next = (side + 1) % sides;
                AddTriangle(top, upper[next], upper[side], color);
                AddQuad(upper[side], upper[next], lower[next], lower[side], color);
                AddTriangle(bottom, lower[side], lower[next], color);
            }
        }

        public void AddFracturedRock(
            Vector3 baseCenter,
            Vector3 size,
            Color color,
            float yaw,
            ulong seed)
        {
            var random = new StableRandom(seed);
            var bottom = new Vector3[8];
            var middle = new Vector3[8];
            var top = new Vector3[8];
            for (var side = 0; side < 8; side++)
            {
                var angle = MathHelper.TwoPi * side / 8f + yaw;
                var direction = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
                var jag = random.Range(0.72f, 1.18f);
                bottom[side] = baseCenter + new Vector3(direction.X * size.X * 0.56f * jag, 0f, direction.Z * size.Z * 0.56f * jag);
                middle[side] = baseCenter + new Vector3(
                    direction.X * size.X * 0.50f * random.Range(0.70f, 1.12f),
                    size.Y * random.Range(0.36f, 0.58f),
                    direction.Z * size.Z * 0.50f * random.Range(0.70f, 1.12f));
                top[side] = baseCenter + new Vector3(
                    direction.X * size.X * 0.34f * random.Range(0.68f, 1.18f),
                    size.Y * random.Range(0.78f, 1.08f),
                    direction.Z * size.Z * 0.34f * random.Range(0.68f, 1.18f));
            }

            var cap = baseCenter + new Vector3(random.Range(-size.X * 0.12f, size.X * 0.12f), size.Y * 1.06f, random.Range(-size.Z * 0.12f, size.Z * 0.12f));
            for (var side = 0; side < 8; side++)
            {
                var next = (side + 1) % 8;
                AddQuad(bottom[side], bottom[next], middle[next], middle[side], Tint(color, (side % 3 - 1) * 0.055f));
                AddQuad(middle[side], middle[next], top[next], top[side], Tint(color, ((side + 1) % 4 - 1.5f) * 0.045f));
                AddTriangle(cap, top[side], top[next], Tint(color, 0.055f));
            }
        }

        public void AddCrossedCard(Vector3 baseCenter, float halfWidth, float height, Color color, float yaw)
        {
            for (var card = 0; card < 2; card++)
            {
                var angle = yaw + card * MathHelper.PiOver2;
                var direction = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
                var left = baseCenter - direction * halfWidth;
                var right = baseCenter + direction * halfWidth;
                AddQuad(left, right, right + Vector3.Up * height, left + Vector3.Up * height, color);
            }
        }

        public void AddSmoothEllipsoid(
            Vector3 center,
            Vector3 radii,
            Color color,
            float yaw,
            int sides,
            int rings)
        {
            var vertexCount = (rings + 1) * sides;
            if (_vertices.Count + vertexCount > ushort.MaxValue)
            {
                throw new InvalidOperationException("The Hills prop batch exceeded the 16-bit mesh budget.");
            }

            var firstVertex = _vertices.Count;
            var cosine = MathF.Cos(yaw);
            var sine = MathF.Sin(yaw);
            for (var ring = 0; ring <= rings; ring++)
            {
                var latitude = -MathHelper.PiOver2 + MathHelper.Pi * ring / rings;
                var ringRadius = MathF.Cos(latitude);
                var vertical = MathF.Sin(latitude);
                for (var side = 0; side < sides; side++)
                {
                    var longitude = MathHelper.TwoPi * side / sides;
                    var sphere = new Vector3(
                        MathF.Cos(longitude) * ringRadius,
                        vertical,
                        MathF.Sin(longitude) * ringRadius);
                    var phase = longitude * 3f + latitude * 2.1f + yaw * 1.7f;
                    var horizontalRoughness = 1f + MathF.Sin(phase) * 0.18f + MathF.Cos(phase * 1.73f) * 0.075f;
                    var verticalRoughness = 1f + MathF.Sin(phase * 1.29f + 0.8f) * 0.075f;
                    var localPosition = new Vector3(
                        sphere.X * radii.X * horizontalRoughness,
                        sphere.Y * radii.Y * verticalRoughness,
                        sphere.Z * radii.Z * horizontalRoughness);
                    var position = center + new Vector3(
                        localPosition.X * cosine - localPosition.Z * sine,
                        localPosition.Y,
                        localPosition.X * sine + localPosition.Z * cosine);
                    var localNormal = new Vector3(sphere.X / radii.X, sphere.Y / radii.Y, sphere.Z / radii.Z);
                    localNormal.Normalize();
                    var normal = new Vector3(
                        localNormal.X * cosine - localNormal.Z * sine,
                        localNormal.Y,
                        localNormal.X * sine + localNormal.Z * cosine);
                    var dapple = MathF.Sin(phase * 2.37f + ring * 0.73f) * 0.11f;
                    var warmth = MathF.Cos(phase * 1.61f - ring * 0.47f) * 0.035f;
                    _vertices.Add(new HillsVertex(position, normal, VaryFoliage(color, dapple, warmth)));
                }
            }

            for (var ring = 0; ring < rings; ring++)
            {
                for (var side = 0; side < sides; side++)
                {
                    var nextSide = (side + 1) % sides;
                    var lowerLeft = (ushort)(firstVertex + ring * sides + side);
                    var lowerRight = (ushort)(firstVertex + ring * sides + nextSide);
                    var upperLeft = (ushort)(firstVertex + (ring + 1) * sides + side);
                    var upperRight = (ushort)(firstVertex + (ring + 1) * sides + nextSide);
                    _indices.Add(lowerLeft);
                    _indices.Add(upperRight);
                    _indices.Add(lowerRight);
                    _indices.Add(lowerLeft);
                    _indices.Add(upperLeft);
                    _indices.Add(upperRight);
                }
            }
        }

        public void AddOctahedron(Vector3 center, Vector3 radii, Color color, float yaw)
        {
            var top = center + new Vector3(0f, radii.Y, 0f);
            var bottom = center - new Vector3(0f, radii.Y, 0f);
            var left = Transform(new Vector3(-radii.X, 0f, 0f), center, yaw);
            var right = Transform(new Vector3(radii.X, 0f, 0f), center, yaw);
            var near = Transform(new Vector3(0f, 0f, radii.Z), center, yaw);
            var far = Transform(new Vector3(0f, 0f, -radii.Z), center, yaw);
            AddTriangle(top, near, right, color);
            AddTriangle(top, right, far, color);
            AddTriangle(top, far, left, color);
            AddTriangle(top, left, near, color);
            AddTriangle(bottom, right, near, color);
            AddTriangle(bottom, far, right, color);
            AddTriangle(bottom, left, far, color);
            AddTriangle(bottom, near, left, color);
        }

        public void AddHorizontalRing(Vector3 center, float innerRadius, float outerRadius, Color color, int segments)
        {
            for (var segment = 0; segment < segments; segment++)
            {
                var angle = MathHelper.TwoPi * segment / segments;
                var nextAngle = MathHelper.TwoPi * (segment + 1) / segments;
                var inner = center + new Vector3(MathF.Cos(angle) * innerRadius, 0.18f, MathF.Sin(angle) * innerRadius);
                var outer = center + new Vector3(MathF.Cos(angle) * outerRadius, 0.18f, MathF.Sin(angle) * outerRadius);
                var nextInner = center + new Vector3(MathF.Cos(nextAngle) * innerRadius, 0.18f, MathF.Sin(nextAngle) * innerRadius);
                var nextOuter = center + new Vector3(MathF.Cos(nextAngle) * outerRadius, 0.18f, MathF.Sin(nextAngle) * outerRadius);
                AddQuad(inner, nextInner, nextOuter, outer, color);
            }
        }

        public void AddHorizontalEllipse(
            Vector3 center,
            float radiusX,
            float radiusZ,
            Color color,
            float yaw,
            int segments)
        {
            center.Y += 0.10f;
            for (var segment = 0; segment < segments; segment++)
            {
                var angle = MathHelper.TwoPi * segment / segments;
                var nextAngle = MathHelper.TwoPi * (segment + 1) / segments;
                var edge = Transform(
                    new Vector3(MathF.Cos(angle) * radiusX, 0f, MathF.Sin(angle) * radiusZ),
                    center,
                    yaw);
                var nextEdge = Transform(
                    new Vector3(MathF.Cos(nextAngle) * radiusX, 0f, MathF.Sin(nextAngle) * radiusZ),
                    center,
                    yaw);
                AddTriangle(center, nextEdge, edge, color);
            }
        }

        public void AddHorizontalPolygon(
            IReadOnlyList<Vector2> points,
            float height,
            Color color,
            Vector2 center)
        {
            var center3 = new Vector3(center.X, height, center.Y);
            for (var index = 0; index < points.Count; index++)
            {
                var next = (index + 1) % points.Count;
                AddTriangle(
                    center3,
                    new Vector3(points[next].X, height, points[next].Y),
                    new Vector3(points[index].X, height, points[index].Y),
                    color);
            }
        }

        public MeshBuffer Build(GraphicsDevice graphicsDevice) =>
            new(graphicsDevice, _vertices.ToArray(), _indices.ToArray());

        private void AddQuad(Vector3 first, Vector3 second, Vector3 third, Vector3 fourth, Color color)
        {
            AddTriangle(first, second, third, color);
            AddTriangle(first, third, fourth, color);
        }

        private void AddTriangle(Vector3 first, Vector3 second, Vector3 third, Color color)
        {
            if (_vertices.Count + 3 > ushort.MaxValue)
            {
                throw new InvalidOperationException("The Hills prop batch exceeded the 16-bit mesh budget.");
            }

            var normal = Vector3.Cross(second - first, third - first);
            if (normal.LengthSquared() < 0.0001f)
            {
                normal = Vector3.Up;
            }
            else
            {
                normal.Normalize();
            }

            for (var index = 0; index < 3; index++)
            {
                _indices.Add((ushort)(_vertices.Count + index));
            }

            _vertices.Add(new HillsVertex(first, normal, color));
            _vertices.Add(new HillsVertex(second, normal, color));
            _vertices.Add(new HillsVertex(third, normal, color));
        }

        private static Vector3 Transform(Vector3 local, Vector3 origin, float yaw)
        {
            var cosine = MathF.Cos(yaw);
            var sine = MathF.Sin(yaw);
            return origin + new Vector3(local.X * cosine - local.Z * sine, local.Y, local.X * sine + local.Z * cosine);
        }

        private static Color VaryFoliage(Color color, float lightness, float warmth) => new(
            (byte)Math.Clamp((int)(color.R * (1f + lightness + warmth)), 0, 255),
            (byte)Math.Clamp((int)(color.G * (1f + lightness + warmth * 0.35f)), 0, 255),
            (byte)Math.Clamp((int)(color.B * (1f + lightness - warmth * 0.40f)), 0, 255),
            color.A);
    }
}
