using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Microsoft.Xna.Framework;

namespace Game.Tests;

public sealed class HillsWorldGenerationTests
{
    [Fact]
    public void LightingPalette_UsesWarmThirtyFiveDegreeSunAndCoolSkyFill()
    {
        var incomingSun = -TerrainLighting.SunDirection;
        var horizontalLength = MathF.Sqrt(incomingSun.X * incomingSun.X + incomingSun.Z * incomingSun.Z);
        var elevation = MathHelper.ToDegrees(MathF.Atan2(incomingSun.Y, horizontalLength));

        Assert.InRange(elevation, 34.5f, 35.5f);
        Assert.True(TerrainLighting.SunColor.X > TerrainLighting.SunColor.Y);
        Assert.True(TerrainLighting.SunColor.Y > TerrainLighting.SunColor.Z);
        Assert.True(TerrainLighting.SkyAmbient.Z > TerrainLighting.SkyAmbient.X);
        Assert.True(TerrainLighting.GroundAmbient.Z < TerrainLighting.SkyAmbient.Z);
    }

    [Fact]
    public void SnowCoverage_BlendsContinuouslyWithoutSurfaceCellClassification()
    {
        Assert.Equal(0f, TerrainLighting.SnowCoverage(0f, 0.90f, 0.5f, 8f));

        var previous = TerrainLighting.SnowCoverage(0f, 0.84f, 0.5f, 14f);
        for (var step = 1; step <= 100; step++)
        {
            var current = TerrainLighting.SnowCoverage(step / 100f, 0.84f, 0.5f, 14f);
            Assert.True(current >= previous, $"snow coverage decreased at step {step}");
            Assert.True(current - previous < 0.05f, $"snow coverage jumped by {current - previous:F3} at step {step}");
            previous = current;
        }

        Assert.True(TerrainLighting.SnowCoverage(0.72f, 0.88f, 0.5f, 12f) > 0.65f);
        Assert.True(TerrainLighting.SnowCoverage(0.72f, 0.88f, 0.5f, 38f) <
                    TerrainLighting.SnowCoverage(0.72f, 0.88f, 0.5f, 12f));
    }

    [Fact]
    public void KnownSeed_HasStableCompleteWorldFingerprint()
    {
        var world = WorldGenerator.Generate(2026);

        Assert.Equal(979_774_998_149_744_912UL, Fingerprint(world));
    }

    [Fact]
    public void ErodedTerrain_ExposesConcaveValleysAndConvexRidgesForMaterials()
    {
        var terrain = WorldGenerator.Generate(2026).Terrain;
        var half = terrain.Settings.Size * 0.45f;
        var curvatures = new List<float>();
        for (var z = -half; z <= half; z += terrain.GridSpacing * 3f)
            for (var x = -half; x <= half; x += terrain.GridSpacing * 3f)
                curvatures.Add(terrain.SampleCurvature(x, z));

        Assert.True(curvatures.Count(value => value > 0.025f) > 40);
        Assert.True(curvatures.Count(value => value < -0.025f) > 40);
        Assert.True(terrain.FlowAccumulations.Max() > 100f);
        Assert.True(terrain.AlluvialDeposits.Count(value => value > 0.25f) > 20);
    }

    [Fact]
    public void SameSeed_ReproducesTerrainSitesAndEveryProp()
    {
        var first = WorldGenerator.Generate(91_337);
        var second = WorldGenerator.Generate(91_337);

        Assert.Equal(first.Terrain.Heights, second.Terrain.Heights);
        Assert.Equal(first.Terrain.MountainInfluences, second.Terrain.MountainInfluences);
        Assert.Equal(first.Terrain.MountainBands, second.Terrain.MountainBands);
        Assert.Equal(first.Terrain.Normals, second.Terrain.Normals);
        Assert.Equal(first.Terrain.Surfaces, second.Terrain.Surfaces);
        Assert.Equal(first.Terrain.AmbientAccessibility, second.Terrain.AmbientAccessibility);
        Assert.Equal(first.Terrain.SunVisibility, second.Terrain.SunVisibility);
        Assert.Equal(first.DungeonSites, second.DungeonSites);
        Assert.Equal(first.Props, second.Props);
        AssertFeaturesEqual(first.Features, second.Features);
        AssertLakesEqual(first.Lakes, second.Lakes);
        AssertMountainFormationsEqual(first.MountainFormations, second.MountainFormations);
    }

    [Fact]
    public void TerrainLighting_GeneratesReadableOcclusionAndSoftSunShadowRanges()
    {
        ValidateSeedRange(0, 8, world =>
        {
            var terrain = world.Terrain;
            Assert.All(terrain.AmbientAccessibility, value => Assert.InRange(value, 0.36f, 1f));
            Assert.All(terrain.SunVisibility, value => Assert.InRange(value, 0.12f, 1f));

            var openCells = terrain.AmbientAccessibility.Count(value => value > 0.88f);
            var occludedCells = terrain.AmbientAccessibility.Count(value => value < 0.78f);
            var shadowedCells = terrain.SunVisibility.Count(value => value < 0.72f);
            Assert.True(openCells > 400, $"only {openCells} open sky samples");
            Assert.True(occludedCells > 120, $"only {occludedCells} terrain AO samples");
            Assert.True(shadowedCells > 120, $"only {shadowedCells} terrain sun-shadow samples");

            var drainageAccessibility = terrain.AmbientAccessibility
                .Where((_, index) => Math.Min(terrain.DistancesToDrainage[index], terrain.DistancesToHillDrainage[index]) < 1.3f)
                .Average();
            var broadOpenAccessibility = terrain.AmbientAccessibility
                .Where((_, index) => terrain.Normals[index].Y > 0.985f &&
                    Math.Min(terrain.DistancesToDrainage[index], terrain.DistancesToHillDrainage[index]) > 8f)
                .Average();
            Assert.True(drainageAccessibility < broadOpenAccessibility,
                $"drainage AO {drainageAccessibility:F3} was not darker than open terrain {broadOpenAccessibility:F3}");
        });
    }

    [Theory]
    [InlineData(4f, 560f, 193)]
    [InlineData(16f, 2240f, 241)]
    public void OverviewScales_GenerateLargerRepeatablePopulatedTerrain(
        float scale,
        float expectedSize,
        int expectedResolution)
    {
        var first = WorldGenerator.Generate(2026, worldScale: scale);
        var second = WorldGenerator.Generate(2026, worldScale: scale);
        var halfSize = expectedSize * 0.5f;

        Assert.Equal(expectedSize, first.TerrainSettings.Size);
        Assert.Equal(expectedResolution, first.TerrainSettings.GridResolution);
        Assert.Equal(first.Terrain.Heights, second.Terrain.Heights);
        Assert.Equal(first.Terrain.MountainInfluences, second.Terrain.MountainInfluences);
        Assert.Equal(first.Terrain.MountainBands, second.Terrain.MountainBands);
        Assert.Equal(first.Terrain.Normals, second.Terrain.Normals);
        Assert.Equal(first.Terrain.Surfaces, second.Terrain.Surfaces);
        Assert.Equal(first.DungeonSites, second.DungeonSites);
        Assert.Equal(first.Props, second.Props);
        AssertFeaturesEqual(first.Features, second.Features);
        AssertLakesEqual(first.Lakes, second.Lakes);
        AssertMountainFormationsEqual(first.MountainFormations, second.MountainFormations);
        Assert.All(first.Terrain.Heights, height => Assert.True(float.IsFinite(height)));
        Assert.All(first.Terrain.Normals, normal => Assert.InRange(normal.Length(), 0.9999f, 1.0001f));
        Assert.Equal(3, first.DungeonSites.Count);
        Assert.True(first.Lakes.Count >= 3 * MathF.Sqrt(scale));
        Assert.Contains(first.Lakes, lake => MathF.Abs(lake.Center.X) > 70f || MathF.Abs(lake.Center.Y) > 70f);
        Assert.Contains(first.MountainFormations.SelectMany(formation => formation.Points), point =>
            MathF.Abs(point.X) > halfSize * 0.80f || MathF.Abs(point.Y) > halfSize * 0.80f);
        Assert.Contains(first.DungeonSites, site =>
            MathF.Abs(site.Position.X) > halfSize * 0.25f || MathF.Abs(site.Position.Z) > halfSize * 0.25f);
        Assert.True(first.Terrain.MountainInfluences.Count(value => value > 0.28f) > expectedResolution * 2);

        for (var firstSite = 0; firstSite < first.DungeonSites.Count; firstSite++)
        {
            for (var secondSite = firstSite + 1; secondSite < first.DungeonSites.Count; secondSite++)
            {
                Assert.True(HorizontalDistance(
                    first.DungeonSites[firstSite].Position,
                    first.DungeonSites[secondSite].Position) >= 18f * scale);
            }
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(2f)]
    [InlineData(8f)]
    public void UnsupportedOverviewScale_IsRejected(float scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldGenerator.Generate(2026, worldScale: scale));
    }

    [Fact]
    public void ChangingShrubDefinition_CannotPerturbOtherStreams()
    {
        var baseline = WorldGenerator.Generate(2026);
        var denserShrubs = HillsBiomeDefinition.Default with
        {
            Shrubs = HillsBiomeDefinition.Default.Shrubs with
            {
                TargetCount = HillsBiomeDefinition.Default.Shrubs.TargetCount + 24,
            },
        };
        var modified = WorldGenerator.Generate(2026, denserShrubs);

        Assert.Equal(baseline.Terrain.Heights, modified.Terrain.Heights);
        Assert.Equal(baseline.DungeonSites, modified.DungeonSites);
        AssertFeaturesEqual(baseline.Features, modified.Features);
        AssertLakesEqual(baseline.Lakes, modified.Lakes);
        AssertMountainFormationsEqual(baseline.MountainFormations, modified.MountainFormations);
        Assert.Equal(PropsOf(baseline, PropType.Tree), PropsOf(modified, PropType.Tree));
        Assert.Equal(PropsOf(baseline, PropType.Rock), PropsOf(modified, PropType.Rock));
        Assert.Equal(PropsOf(baseline, PropType.PineTree), PropsOf(modified, PropType.PineTree));
        Assert.Equal(PropsOf(baseline, PropType.CrookedPine), PropsOf(modified, PropType.CrookedPine));
        Assert.Equal(PropsOf(baseline, PropType.AlpineShrub), PropsOf(modified, PropType.AlpineShrub));
        Assert.Equal(PropsOf(baseline, PropType.LichenRock), PropsOf(modified, PropType.LichenRock));
        Assert.Equal(PropsOf(baseline, PropType.DeadConifer), PropsOf(modified, PropType.DeadConifer));
        Assert.Equal(
            baseline.Props.Where(prop => prop.Type is PropType.DeadTree or PropType.FallenLog or PropType.Stump),
            modified.Props.Where(prop => prop.Type is PropType.DeadTree or PropType.FallenLog or PropType.Stump));
        Assert.NotEqual(PropsOf(baseline, PropType.Shrub).Count, PropsOf(modified, PropType.Shrub).Count);
    }

    [Fact]
    public void Terrain_IsRegularBoundedAndHasUnitNormalsAndReadableBands()
    {
        var world = WorldGenerator.Generate(2026);
        var terrain = world.Terrain;
        var expectedSamples = terrain.Settings.GridResolution * terrain.Settings.GridResolution;

        Assert.Equal(193, terrain.Settings.GridResolution);
        Assert.Equal(140f, terrain.Settings.Size);
        Assert.Equal(expectedSamples, terrain.Heights.Count);
        Assert.Equal(expectedSamples, terrain.MountainInfluences.Count);
        Assert.Equal(expectedSamples, terrain.MountainBands.Count);
        Assert.Equal(expectedSamples, terrain.Normals.Count);
        Assert.All(terrain.Heights, height => Assert.InRange(height, terrain.Settings.MinHeight, terrain.Settings.MaxHeight));
        Assert.All(terrain.Normals, normal => Assert.InRange(normal.Length(), 0.9999f, 1.0001f));
        Assert.Contains(TerrainSurface.LowGrass, terrain.Surfaces);
        Assert.Contains(TerrainSurface.HillGrass, terrain.Surfaces);
        Assert.Contains(TerrainSurface.RidgeGrass, terrain.Surfaces);
        Assert.Contains(TerrainSurface.SteepEarth, terrain.Surfaces);
        Assert.Contains(TerrainSurface.MountainRock, terrain.Surfaces);
        Assert.Contains(TerrainSurface.MountainVegetatedRock, terrain.Surfaces);
        Assert.Contains(TerrainSurface.MountainSnow, terrain.Surfaces);
        Assert.Contains(TerrainSurface.MountainVegetatedSnow, terrain.Surfaces);
        Assert.Contains(MountainElevationBand.LowerSlope, terrain.MountainBands);
        Assert.Contains(MountainElevationBand.MidSlope, terrain.MountainBands);
        Assert.Contains(MountainElevationBand.Treeline, terrain.MountainBands);
        Assert.Contains(MountainElevationBand.Alpine, terrain.MountainBands);
        Assert.Contains(MountainElevationBand.Peak, terrain.MountainBands);
        Assert.True(terrain.Heights.Max() - terrain.Heights.Min() > 15f);

        var hillsMean = terrain.Heights
            .Where((_, index) => terrain.MountainInfluences[index] < 0.08f)
            .Average();
        var mountainMean = terrain.Heights
            .Where((_, index) => terrain.MountainInfluences[index] > 0.72f)
            .Average();
        Assert.True(mountainMean > hillsMean + 7f, $"mountains={mountainMean}, hills={hillsMean}");

        var fineDelta = MeanHorizontalDelta(terrain, 1);
        var ridgeDelta = MeanHorizontalDelta(terrain, 4);
        var broadDelta = MeanHorizontalDelta(terrain, 16);
        Assert.InRange(fineDelta, 0.03f, 1.1f);
        Assert.True(ridgeDelta > fineDelta * 1.4f, $"ridge={ridgeDelta}, fine={fineDelta}");
        Assert.True(broadDelta > ridgeDelta * 1.15f, $"broad={broadDelta}, ridge={ridgeDelta}");
    }

    [Fact]
    public void TerrainNormals_UseAFilteredFiveSampleFootprint()
    {
        var terrain = WorldGenerator.Generate(2026).Terrain;
        var maximumError = 0f;
        var differsFromSingleCellGradient = false;
        for (var row = 2; row < terrain.Settings.GridResolution - 2; row++)
        {
            for (var column = 2; column < terrain.Settings.GridResolution - 2; column++)
            {
                var expected = new Vector3(
                    terrain.HeightAt(column - 2, row) + terrain.HeightAt(column - 1, row) * 2f -
                    terrain.HeightAt(column + 1, row) * 2f - terrain.HeightAt(column + 2, row),
                    terrain.GridSpacing * 8f,
                    terrain.HeightAt(column, row - 2) + terrain.HeightAt(column, row - 1) * 2f -
                    terrain.HeightAt(column, row + 1) * 2f - terrain.HeightAt(column, row + 2));
                expected.Normalize();
                var actual = terrain.NormalAt(column, row);
                maximumError = Math.Max(maximumError, Vector3.Distance(expected, actual));

                if (terrain.MountainBandAt(column, row) != MountainElevationBand.None)
                {
                    var singleCell = new Vector3(
                        terrain.HeightAt(column - 1, row) - terrain.HeightAt(column + 1, row),
                        terrain.GridSpacing * 2f,
                        terrain.HeightAt(column, row - 1) - terrain.HeightAt(column, row + 1));
                    singleCell.Normalize();
                    differsFromSingleCellGradient |= Vector3.Distance(actual, singleCell) > 0.025f;
                }
            }
        }

        Assert.InRange(maximumError, 0f, 0.00001f);
        Assert.True(differsFromSingleCellGradient);
    }

    [Fact]
    public void EverySeed_HasThreeSeparatedGentleVisibleDungeonSites()
    {
        ValidateSeedRange(0, 96, world =>
        {
            Assert.Equal(3, world.DungeonSites.Count);
            Assert.Equal([1, 2, 3], world.DungeonSites.Select(site => site.DungeonId));
            Assert.Equal([Biome.Hills, Biome.Hills, Biome.Mountain], world.Dungeons.Select(choice => choice.Biome));
            for (var index = 0; index < world.DungeonSites.Count; index++)
            {
                var site = world.DungeonSites[index];
                Assert.Equal(world.Dungeons[index].Name, site.Name);
                Assert.InRange(SlopeDegrees(world.Terrain.SampleNormal(site.Position.X, site.Position.Z)), 0f, 8.5f);
                Assert.InRange(site.Position.X, -55f, 56f);
                Assert.InRange(site.Position.Z, -42f, 41f);
                Assert.DoesNotContain(world.Lakes, lake => IsInsideLake(site.Position, lake, site.ClearedAreaRadius + 1.49f));
                for (var other = index + 1; other < world.DungeonSites.Count; other++)
                {
                    Assert.True(HorizontalDistance(site.Position, world.DungeonSites[other].Position) >= 18f);
                }
            }

            Assert.True(world.DungeonSites[0].Position.Z > world.DungeonSites[1].Position.Z);
            Assert.True(world.DungeonSites[1].Position.Z > world.DungeonSites[2].Position.Z);
            Assert.InRange(world.Terrain.SampleMountainInfluence(
                world.DungeonSites[2].Position.X,
                world.DungeonSites[2].Position.Z), 0.239f, 1f);
        });
    }

    [Fact]
    public void Props_StayGroundedSpacedFilteredAndOutsideClearings()
    {
        ValidateSeedRange(0, 48, world =>
        {
            Assert.InRange(PropsOf(world, PropType.Tree).Count, 15, world.Hills.Trees.TargetCount);
            Assert.InRange(PropsOf(world, PropType.Shrub).Count, 70, world.Hills.Shrubs.TargetCount);
            Assert.InRange(PropsOf(world, PropType.Rock).Count, 24, world.Hills.Rocks.TargetCount);
            Assert.InRange(PropsOf(world, PropType.PineTree).Count, 12, world.Mountains.Pines.TargetCount);
            var crookedPineCount = PropsOf(world, PropType.CrookedPine).Count;
            var crookedCells = world.Terrain.MountainBands.Select((band, index) => (band, index)).Count(sample =>
                sample.band is MountainElevationBand.MidSlope or MountainElevationBand.Treeline &&
                world.Terrain.NormalizedHeight(world.Terrain.Heights[sample.index]) is >= 0.30f and <= 0.86f &&
                SlopeDegrees(world.Terrain.Normals[sample.index]) <= world.Mountains.CrookedPines.MaximumSlopeDegrees);
            var crookedBandSlopes = world.Terrain.MountainBands.Select((band, index) => (band, index))
                .Where(sample => sample.band is MountainElevationBand.MidSlope or MountainElevationBand.Treeline)
                .Select(sample => SlopeDegrees(world.Terrain.Normals[sample.index]))
                .ToArray();
            Assert.True(crookedPineCount >= 1 && crookedPineCount <= world.Mountains.CrookedPines.TargetCount,
                $"crooked pines={crookedPineCount}, qualifying terrain cells={crookedCells}, " +
                $"band cells={crookedBandSlopes.Length}, min slope={crookedBandSlopes.DefaultIfEmpty(-1f).Min()}");
            var alpineShrubCount = PropsOf(world, PropType.AlpineShrub).Count;
            var alpineCells = world.Terrain.MountainBands.Select((band, index) => (band, index)).Count(sample =>
                sample.band is MountainElevationBand.Treeline or MountainElevationBand.Alpine &&
                world.Terrain.NormalizedHeight(world.Terrain.Heights[sample.index]) is >= 0.30f and <= 0.91f &&
                SlopeDegrees(world.Terrain.Normals[sample.index]) <= world.Mountains.AlpineShrubs.MaximumSlopeDegrees);
            Assert.True(alpineShrubCount >= 8 && alpineShrubCount <= world.Mountains.AlpineShrubs.TargetCount,
                $"alpine shrubs={alpineShrubCount}, qualifying terrain cells={alpineCells}");
            Assert.InRange(PropsOf(world, PropType.LichenRock).Count, 10, world.Mountains.LichenRocks.TargetCount);
            Assert.InRange(PropsOf(world, PropType.DeadConifer).Count, 2, world.Mountains.DeadConifers.TargetCount);
            foreach (var prop in world.Props)
            {
                Assert.InRange(MathF.Abs(prop.Position.Y - world.Terrain.SampleHeight(prop.Position.X, prop.Position.Z)), 0f, 0.0001f);
                Assert.InRange(Vector3.Distance(prop.SurfaceNormal, world.Terrain.SampleNormal(prop.Position.X, prop.Position.Z)), 0f, 0.0001f);
                Assert.DoesNotContain(world.DungeonSites, site =>
                    HorizontalDistance(prop.Position, site.Position) < site.ClearedAreaRadius + 1.149f);
                if (prop.Type == PropType.Tree)
                {
                    Assert.DoesNotContain(world.DungeonSites, site =>
                        HorizontalDistance(prop.Position, site.Position) < site.ClearedAreaRadius + 4.599f);
                }
                else if (prop.Type is PropType.PineTree or PropType.CrookedPine or PropType.DeadConifer)
                {
                    Assert.DoesNotContain(world.DungeonSites, site =>
                        HorizontalDistance(prop.Position, site.Position) < site.ClearedAreaRadius + 4.099f);
                }

                var lakeMargin = prop.Type switch
                {
                    PropType.Tree => 4.8f,
                    PropType.Shrub => 0.8f,
                    PropType.Rock => 0.35f,
                    PropType.PineTree => 3.4f,
                    PropType.CrookedPine => 3.4f,
                    PropType.AlpineShrub => 0.7f,
                    PropType.LichenRock => 0.35f,
                    PropType.DeadConifer => 3.4f,
                    _ => 1f,
                };
                Assert.DoesNotContain(world.Lakes, lake => IsInsideLake(prop.Position, lake, lakeMargin - 0.001f));
                var slope = SlopeDegrees(world.Terrain.SampleNormal(prop.Position.X, prop.Position.Z));
                if (prop.Type is PropType.Tree or PropType.Shrub or PropType.Rock or
                    PropType.PineTree or PropType.CrookedPine or PropType.AlpineShrub or PropType.LichenRock or PropType.DeadConifer)
                {
                    var rule = RuleFor(world, prop.Type);
                    var height = world.Terrain.NormalizedHeight(prop.Position.Y);
                    Assert.InRange(slope, rule.MinimumSlopeDegrees - 0.001f, rule.MaximumSlopeDegrees + 0.001f);
                    Assert.InRange(height, rule.MinimumNormalizedHeight - 0.001f, rule.MaximumNormalizedHeight + 0.001f);
                }
                else
                {
                    Assert.InRange(slope, 0f, 18.001f);
                }
            }

            foreach (var type in new[]
                     {
                         PropType.Tree,
                         PropType.Shrub,
                         PropType.Rock,
                         PropType.PineTree,
                         PropType.CrookedPine,
                         PropType.AlpineShrub,
                         PropType.LichenRock,
                         PropType.DeadConifer,
                     })
            {
                var props = PropsOf(world, type);
                var spacing = RuleFor(world, type).MinimumSpacing;
                for (var first = 0; first < props.Count; first++)
                {
                    for (var second = first + 1; second < props.Count; second++)
                    {
                        Assert.True(HorizontalDistance(props[first].Position, props[second].Position) >= spacing - 0.0001f);
                    }
                }
            }
        });
    }

    [Fact]
    public void Vegetation_HasHierarchyGrovesUnderstoryAndRareDeadwood()
    {
        ValidateSeedRange(0, 32, world =>
        {
            var trees = PropsOf(world, PropType.Tree);
            var shrubs = PropsOf(world, PropType.Shrub);
            Assert.All(trees.Take(4), tree => Assert.True(tree.Scale.Y > 1.08f));
            Assert.All(trees.Skip(4).Take(10), tree => Assert.True(tree.Scale.Y < 0.94f));
            Assert.Contains(trees.Skip(14), tree => tree.Scale.Y is >= 0.66f and <= 1.15f);

            var pines = PropsOf(world, PropType.PineTree);
            Assert.All(pines.Take(3), pine => Assert.True(pine.Scale.Y > 1.05f));
            Assert.All(pines.Take(3), pine => Assert.Equal(
                MountainElevationBand.LowerSlope,
                world.Terrain.SampleMountainBand(pine.Position.X, pine.Position.Z)));
            Assert.All(pines.Skip(3).Take(8), pine => Assert.True(pine.Scale.Y < 0.99f));

            Assert.All(PropsOf(world, PropType.CrookedPine), pine =>
            {
                Assert.InRange(pine.LeanRadians, 0.09f, 0.22f);
                Assert.True(pine.Scale.Y < 0.90f);
            });

            var shrubsInUnderstory = shrubs.Count(shrub => trees.Any(tree => HorizontalDistance(shrub.Position, tree.Position) <= 7.5f));
            Assert.True(shrubsInUnderstory >= shrubs.Count / 2,
                $"only {shrubsInUnderstory}/{shrubs.Count} shrubs were gathered near tree cover");

            Assert.Single(PropsOf(world, PropType.DeadTree));
            Assert.Single(PropsOf(world, PropType.FallenLog));
            Assert.Single(PropsOf(world, PropType.Stump));
        });
    }

    [Fact]
    public void AuthoredPaths_ConnectSitesFollowTerrainAndStayClearOfProps()
    {
        ValidateSeedRange(0, 32, world =>
        {
            Assert.Equal(2, world.Features.Count);
            for (var featureIndex = 0; featureIndex < world.Features.Count; featureIndex++)
            {
                var feature = world.Features[featureIndex];
                Assert.Equal(TerrainFeatureType.WornPath, feature.Type);
                Assert.Equal(13, feature.Points.Count);
                Assert.InRange(feature.Width, 1.25f, 1.75f);
                Assert.Equal(world.DungeonSites[featureIndex].Position, feature.Points[0]);
                Assert.Equal(world.DungeonSites[featureIndex + 1].Position, feature.Points[^1]);
                Assert.All(feature.Points, point => Assert.InRange(
                    MathF.Abs(point.Y - world.Terrain.SampleHeight(point.X, point.Z)), 0f, 0.0001f));
            }

            foreach (var prop in world.Props.Where(prop => prop.Type is
                         PropType.Tree or PropType.Shrub or PropType.Rock or PropType.PineTree or PropType.CrookedPine or
                         PropType.AlpineShrub or PropType.LichenRock or PropType.DeadConifer))
            {
                var requiredMargin = prop.Type switch
                {
                    PropType.Tree => 1.65f,
                    PropType.Shrub => 0.65f,
                    PropType.PineTree or PropType.CrookedPine or PropType.DeadConifer => 1.5f,
                    PropType.AlpineShrub => 0.55f,
                    _ => 0.35f,
                };
                Assert.True(DistanceToFeatures(prop.Position, world.Features) >= requiredMargin - 0.0001f);
            }
        });
    }

    [Fact]
    public void Mountains_TransitionSmoothlyThroughEcologyBandsToSparseSnowCaps()
    {
        ValidateSeedRange(0, 32, world =>
        {
            var terrain = world.Terrain;
            Assert.Contains(TerrainSurface.MountainRock, terrain.Surfaces);
            Assert.Contains(TerrainSurface.MountainVegetatedRock, terrain.Surfaces);
            Assert.Contains(TerrainSurface.MountainSnow, terrain.Surfaces);
            Assert.Contains(TerrainSurface.MountainVegetatedSnow, terrain.Surfaces);
            Assert.True(terrain.Surfaces.Count(surface => surface == TerrainSurface.MountainRock) >= 80);
            Assert.True(terrain.Surfaces.Count(surface => surface == TerrainSurface.MountainVegetatedRock) >= 80);
            var mountainSampleCount = terrain.MountainBands.Count(band => band != MountainElevationBand.None);
            var snowSampleCount = terrain.Surfaces.Count(surface => surface == TerrainSurface.MountainSnow);
            Assert.InRange(snowSampleCount, 6, (int)(mountainSampleCount * 0.18f));
            Assert.All(
                terrain.Surfaces.Select((surface, index) => (surface, index))
                    .Where(sample => sample.surface == TerrainSurface.MountainSnow),
                sample =>
                {
                    Assert.Equal(MountainElevationBand.Peak, terrain.MountainBands[sample.index]);
                    Assert.InRange(SlopeDegrees(terrain.Normals[sample.index]), 0f, 34.001f);
                });
            Assert.All(
                terrain.Surfaces.Select((surface, index) => (surface, index))
                    .Where(sample => sample.surface == TerrainSurface.MountainVegetatedSnow),
                sample => Assert.Equal(MountainElevationBand.Alpine, terrain.MountainBands[sample.index]));
            Assert.All(
                terrain.MountainBands.Select((band, index) => (band, index))
                    .Where(sample => sample.band == MountainElevationBand.Peak),
                sample => Assert.Contains(
                    terrain.Surfaces[sample.index],
                    new[] { TerrainSurface.MountainRock, TerrainSurface.MountainSnow }));
            Assert.True(terrain.MountainInfluences.Count(value => value is > 0.12f and < 0.88f) > 420);

            Assert.All(PropsOf(world, PropType.PineTree), prop =>
                Assert.Contains(terrain.SampleMountainBand(prop.Position.X, prop.Position.Z),
                    new[] { MountainElevationBand.LowerSlope, MountainElevationBand.MidSlope }));
            Assert.All(PropsOf(world, PropType.CrookedPine), prop =>
                Assert.Contains(terrain.SampleMountainBand(prop.Position.X, prop.Position.Z),
                    new[] { MountainElevationBand.MidSlope, MountainElevationBand.Treeline }));
            Assert.All(PropsOf(world, PropType.AlpineShrub), prop =>
                Assert.Contains(terrain.SampleMountainBand(prop.Position.X, prop.Position.Z),
                    new[] { MountainElevationBand.Treeline, MountainElevationBand.Alpine }));
            Assert.All(PropsOf(world, PropType.LichenRock), prop =>
                Assert.Contains(terrain.SampleMountainBand(prop.Position.X, prop.Position.Z),
                    new[]
                    {
                        MountainElevationBand.MidSlope,
                        MountainElevationBand.Treeline,
                        MountainElevationBand.Alpine,
                        MountainElevationBand.Peak,
                    }));
            Assert.All(PropsOf(world, PropType.DeadConifer), prop =>
                Assert.Contains(terrain.SampleMountainBand(prop.Position.X, prop.Position.Z),
                    new[] { MountainElevationBand.LowerSlope, MountainElevationBand.MidSlope, MountainElevationBand.Treeline }));
            Assert.DoesNotContain(world.Props.Where(prop => prop.Type is
                    PropType.PineTree or PropType.CrookedPine or PropType.AlpineShrub or PropType.DeadConifer),
                prop => terrain.SampleMountainBand(prop.Position.X, prop.Position.Z) == MountainElevationBand.Peak);
            Assert.DoesNotContain(world.Props.Where(prop => prop.Type is PropType.Tree or PropType.Shrub),
                prop => terrain.SampleMountainBand(prop.Position.X, prop.Position.Z) != MountainElevationBand.None);

            var hillsMean = terrain.Heights
                .Where((_, index) => terrain.MountainInfluences[index] < 0.08f)
                .Average();
            var mountainMean = terrain.Heights
                .Where((_, index) => terrain.MountainInfluences[index] > 0.72f)
                .Average();
            Assert.True(mountainMean > hillsMean + 6f, $"mountains={mountainMean}, hills={hillsMean}");
            Assert.True(MaxAdjacentInfluenceDelta(terrain) < 0.18f);
        });
    }

    [Fact]
    public void Mountains_UseConnectedRidgesAndEmergentDrainageAndTalus()
    {
        ValidateSeedRange(0, 32, world =>
        {
            var cliffs = world.MountainFormations
                .Where(formation => formation.Type == MountainFormationType.CliffFace)
                .ToArray();
            var drainages = world.MountainFormations
                .Where(formation => formation.Type == MountainFormationType.Drainage)
                .ToArray();
            var screeFans = world.MountainFormations
                .Where(formation => formation.Type == MountainFormationType.ScreeFan)
                .ToArray();
            Assert.Equal(3, cliffs.Length);
            Assert.InRange(drainages.Length, 8, 12);
            Assert.Empty(screeFans);
            Assert.All(cliffs, cliff =>
            {
                Assert.InRange(cliff.Points.Count, 7, 11);
                Assert.True(PolylineLength(cliff.Points) > 22f);
                Assert.True(cliff.Width > 2f);
            });
            Assert.All(drainages, drainage =>
            {
                Assert.True(drainage.Points.Count >= 5);
                var startHeight = world.Terrain.SampleHeight(drainage.Points[0].X, drainage.Points[0].Y);
                var endHeight = world.Terrain.SampleHeight(drainage.Points[^1].X, drainage.Points[^1].Y);
                Assert.True(startHeight > endHeight, $"drainage rose from {startHeight} to {endHeight}");
            });

            Assert.True(world.Terrain.FlowAccumulations.Max() > 24f);
            Assert.True(world.Terrain.RockMasks.Count(mask => mask > 0.55f) > 120);
            Assert.True(world.Terrain.ScreeDeposits.Count(mask => mask > 0.20f) > 12);
            var screeCells = world.Terrain.ScreeMasks.Count(mask => mask > 0.35f);
            Assert.True(screeCells >= 20, $"only {screeCells} resolved scree cells");
            Assert.True(world.Terrain.ForestMasks.Count(mask => mask > 0.25f) > 80);

            var steepMountainSamples = world.Terrain.Normals
                .Where((normal, index) => world.Terrain.MountainBands[index] != MountainElevationBand.None)
                .Count(normal => SlopeDegrees(normal) >= 30f);
            Assert.True(steepMountainSamples >= 180, $"only {steepMountainSamples} sharp Mountain samples");
        });
    }

    [Fact]
    public void Hills_UseBroadRidgesShallowDrainageAndForestFirstBiomeMasks()
    {
        ValidateSeedRange(0, 12, world =>
        {
            var terrain = world.Terrain;
            var lowlandCells = terrain.MountainInfluences
                .Select((influence, index) => (influence, index))
                .Where(sample => sample.influence < 0.20f)
                .Select(sample => sample.index)
                .ToArray();
            var forestCoverage = lowlandCells.Count(index => terrain.ForestMasks[index] >= 0.50f) / (float)lowlandCells.Length;
            var clearingCoverage = lowlandCells.Count(index => terrain.ClearingMasks[index] >= 0.32f) / (float)lowlandCells.Length;
            Assert.True(forestCoverage is >= 0.70f and <= 0.86f,
                $"forest coverage {forestCoverage:P1} was outside 70-86%");
            Assert.True(clearingCoverage is >= 0.08f and <= 0.24f,
                $"clearing coverage {clearingCoverage:P1} was outside 8-24%");
            Assert.True(lowlandCells.Count(index => terrain.WetValleyMasks[index] > 0.30f) > 90);
            Assert.True(lowlandCells.Count(index => terrain.HillOutcropMasks[index] > 0.18f) > 12);
            Assert.Equal(4, world.MountainFormations.Count(formation => formation.Type == MountainFormationType.HillRidge));
            Assert.InRange(world.MountainFormations.Count(formation => formation.Type == MountainFormationType.HillDrainage), 6, 9);
            Assert.All(world.MountainFormations.Where(formation => formation.Type == MountainFormationType.HillDrainage), drainage =>
            {
                var start = terrain.SampleHeight(drainage.Points[0].X, drainage.Points[0].Y);
                var end = terrain.SampleHeight(drainage.Points[^1].X, drainage.Points[^1].Y);
                // Smoothed display polylines may end within a tenth of a metre above their
                // sampled start even though the underlying multi-flow receivers are downhill.
                Assert.True(start + 0.1f > end, $"hill drainage rose from {start} to {end}");
            });
        });
    }

    [Fact]
    public void Lakes_AreStableCarvedBasinsWithTreeFreeShores()
    {
        ValidateSeedRange(0, 32, world =>
        {
            Assert.Equal(3, world.Lakes.Count);
            foreach (var lake in world.Lakes)
            {
                Assert.Equal(36, lake.Shoreline.Count);
                Assert.True(world.Terrain.SampleHeight(lake.Center.X, lake.Center.Y) < lake.WaterHeight - 0.15f);
                Assert.InRange(world.Terrain.SampleMountainInfluence(lake.Center.X, lake.Center.Y), 0f, 0.27f);
                Assert.DoesNotContain(PropsOf(world, PropType.Tree), tree => IsInsideLake(tree.Position, lake, 4.799f));
                Assert.DoesNotContain(PropsOf(world, PropType.PineTree), tree => IsInsideLake(tree.Position, lake, 3.399f));
                Assert.DoesNotContain(PropsOf(world, PropType.CrookedPine), tree => IsInsideLake(tree.Position, lake, 3.399f));
                Assert.DoesNotContain(PropsOf(world, PropType.DeadConifer), tree => IsInsideLake(tree.Position, lake, 3.399f));
            }
        });
    }

    [Fact]
    public void DifferentSeeds_KeepHillsIdentityButChangeTheLandscape()
    {
        var first = WorldGenerator.Generate(1001);
        var second = WorldGenerator.Generate(1002);
        var meanDifference = first.Terrain.Heights
            .Zip(second.Terrain.Heights, (left, right) => MathF.Abs(left - right))
            .Average();

        Assert.True(meanDifference > 0.55f, $"mean terrain difference was {meanDifference}");
        Assert.NotEqual(first.DungeonSites, second.DungeonSites);
        Assert.NotEqual(first.Props, second.Props);
        Assert.NotEqual(
            first.MountainFormations.SelectMany(formation => formation.Points),
            second.MountainFormations.SelectMany(formation => formation.Points));
        Assert.All(new[] { first, second }, world =>
            Assert.True(world.Terrain.Heights.Max() - world.Terrain.Heights.Min() > 7f));
    }

    private static void ValidateSeedRange(int seed, int count, Action<GeneratedWorld> assertion)
    {
        if (count == 0)
        {
            return;
        }

        try
        {
            assertion(WorldGenerator.Generate(seed));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Generated world seed {seed} failed recursive validation.", exception);
        }
        ValidateSeedRange(seed + 1, count - 1, assertion);
    }

    private static List<PropPlacement> PropsOf(GeneratedWorld world, PropType type) =>
        world.Props.Where(prop => prop.Type == type).ToList();

    private static void AssertFeaturesEqual(
        IReadOnlyList<TerrainFeature> expected,
        IReadOnlyList<TerrainFeature> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Type, actual[index].Type);
            Assert.Equal(expected[index].Points, actual[index].Points);
            Assert.Equal(expected[index].Width, actual[index].Width);
            Assert.Equal(expected[index].ColorVariation, actual[index].ColorVariation);
        }
    }

    private static void AssertLakesEqual(
        IReadOnlyList<LakeDefinition> expected,
        IReadOnlyList<LakeDefinition> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Center, actual[index].Center);
            Assert.Equal(expected[index].Radius, actual[index].Radius);
            Assert.Equal(expected[index].WaterHeight, actual[index].WaterHeight);
            Assert.Equal(expected[index].Shoreline, actual[index].Shoreline);
        }
    }

    private static void AssertMountainFormationsEqual(
        IReadOnlyList<MountainFormation> expected,
        IReadOnlyList<MountainFormation> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Type, actual[index].Type);
            Assert.Equal(expected[index].Points, actual[index].Points);
            Assert.Equal(expected[index].Width, actual[index].Width);
            Assert.Equal(expected[index].Strength, actual[index].Strength);
        }
    }

    private static ScatterDefinition RuleFor(GeneratedWorld world, PropType type) => type switch
    {
        PropType.Tree => world.Hills.Trees,
        PropType.Shrub => world.Hills.Shrubs,
        PropType.Rock => world.Hills.Rocks,
        PropType.PineTree => world.Mountains.Pines,
        PropType.CrookedPine => world.Mountains.CrookedPines,
        PropType.AlpineShrub => world.Mountains.AlpineShrubs,
        PropType.LichenRock => world.Mountains.LichenRocks,
        _ => world.Mountains.DeadConifers,
    };

    private static float SlopeDegrees(Vector3 normal) =>
        MathHelper.ToDegrees(MathF.Acos(MathHelper.Clamp(normal.Y, -1f, 1f)));

    private static float HorizontalDistance(Vector3 first, Vector3 second) =>
        Vector2.Distance(new Vector2(first.X, first.Z), new Vector2(second.X, second.Z));

    private static bool IsInsideLake(Vector3 position, LakeDefinition lake, float margin)
    {
        var dx = (position.X - lake.Center.X) / (lake.Radius.X + margin);
        var dz = (position.Z - lake.Center.Y) / (lake.Radius.Y + margin);
        return dx * dx + dz * dz <= 1f;
    }

    private static float MaxAdjacentInfluenceDelta(GeneratedTerrain terrain)
    {
        var maximum = 0f;
        for (var row = 0; row < terrain.Settings.GridResolution; row++)
        {
            for (var column = 0; column < terrain.Settings.GridResolution; column++)
            {
                if (column + 1 < terrain.Settings.GridResolution)
                {
                    maximum = Math.Max(maximum, MathF.Abs(
                        terrain.MountainInfluenceAt(column + 1, row) - terrain.MountainInfluenceAt(column, row)));
                }

                if (row + 1 < terrain.Settings.GridResolution)
                {
                    maximum = Math.Max(maximum, MathF.Abs(
                        terrain.MountainInfluenceAt(column, row + 1) - terrain.MountainInfluenceAt(column, row)));
                }
            }
        }

        return maximum;
    }

    private static float MeanHorizontalDelta(GeneratedTerrain terrain, int stride)
    {
        var total = 0f;
        var count = 0;
        for (var row = 0; row < terrain.Settings.GridResolution; row++)
        {
            for (var column = 0; column + stride < terrain.Settings.GridResolution; column++)
            {
                total += MathF.Abs(terrain.HeightAt(column + stride, row) - terrain.HeightAt(column, row));
                count++;
            }
        }

        return total / count;
    }

    private static int RavineCutCount(GeneratedTerrain terrain, MountainFormation ravine)
    {
        var cutCount = 0;
        for (var index = 1; index < ravine.Points.Count - 1; index++)
        {
            var point = ravine.Points[index];
            var tangent = ravine.Points[index + 1] - ravine.Points[index - 1];
            tangent.Normalize();
            var perpendicular = new Vector2(-tangent.Y, tangent.X) * (ravine.Width * 1.15f);
            var centerHeight = terrain.SampleHeight(point.X, point.Y);
            var shoulders = (terrain.SampleHeight(point.X + perpendicular.X, point.Y + perpendicular.Y) +
                             terrain.SampleHeight(point.X - perpendicular.X, point.Y - perpendicular.Y)) * 0.5f;
            if (shoulders - centerHeight >= 0.45f)
            {
                cutCount++;
            }
        }

        return cutCount;
    }

    private static float PolylineLength(IReadOnlyList<Vector2> points)
    {
        var length = 0f;
        for (var index = 0; index < points.Count - 1; index++)
        {
            length += Vector2.Distance(points[index], points[index + 1]);
        }

        return length;
    }

    private static float DistanceToFeatures(Vector3 position, IReadOnlyList<TerrainFeature> features)
    {
        var closest = float.MaxValue;
        var point = new Vector2(position.X, position.Z);
        foreach (var feature in features)
        {
            for (var index = 0; index < feature.Points.Count - 1; index++)
            {
                var start = new Vector2(feature.Points[index].X, feature.Points[index].Z);
                var end = new Vector2(feature.Points[index + 1].X, feature.Points[index + 1].Z);
                var segment = end - start;
                var amount = segment.LengthSquared() < 0.0001f
                    ? 0f
                    : MathHelper.Clamp(Vector2.Dot(point - start, segment) / segment.LengthSquared(), 0f, 1f);
                closest = Math.Min(closest, Vector2.Distance(point, start + segment * amount) - feature.Width * 0.5f);
            }
        }

        return closest;
    }

    private static ulong Fingerprint(GeneratedWorld world)
    {
        var hash = 14695981039346656037UL;
        Add(ref hash, world.WorldSeed.Value);
        foreach (var height in world.Terrain.Heights)
        {
            Add(ref hash, BitConverter.SingleToInt32Bits(height));
        }

        foreach (var influence in world.Terrain.MountainInfluences)
        {
            Add(ref hash, BitConverter.SingleToInt32Bits(influence));
        }

        foreach (var band in world.Terrain.MountainBands)
        {
            Add(ref hash, (int)band);
        }

        foreach (var dungeon in world.Dungeons)
        {
            Add(ref hash, dungeon.Number);
            Add(ref hash, (int)dungeon.Biome);
        }

        foreach (var site in world.DungeonSites)
        {
            Add(ref hash, site.DungeonId);
            Add(ref hash, site.Name);
            Add(ref hash, site.Position);
            Add(ref hash, BitConverter.SingleToInt32Bits(site.OrientationRadians));
            Add(ref hash, BitConverter.SingleToInt32Bits(site.ClearedAreaRadius));
        }

        foreach (var prop in world.Props)
        {
            Add(ref hash, (int)prop.Type);
            Add(ref hash, prop.Position);
            Add(ref hash, BitConverter.SingleToInt32Bits(prop.RotationRadians));
            Add(ref hash, prop.Scale);
            Add(ref hash, BitConverter.SingleToInt32Bits(prop.ColorVariation));
            Add(ref hash, prop.SurfaceNormal);
            Add(ref hash, BitConverter.SingleToInt32Bits(prop.LeanRadians));
            Add(ref hash, BitConverter.SingleToInt32Bits(prop.LeanDirectionRadians));
        }

        foreach (var feature in world.Features)
        {
            Add(ref hash, (int)feature.Type);
            foreach (var point in feature.Points)
            {
                Add(ref hash, point);
            }

            Add(ref hash, BitConverter.SingleToInt32Bits(feature.Width));
            Add(ref hash, BitConverter.SingleToInt32Bits(feature.ColorVariation));
        }

        foreach (var lake in world.Lakes)
        {
            Add(ref hash, lake.Center);
            Add(ref hash, lake.Radius);
            Add(ref hash, BitConverter.SingleToInt32Bits(lake.WaterHeight));
            foreach (var point in lake.Shoreline)
            {
                Add(ref hash, point);
            }
        }

        foreach (var formation in world.MountainFormations)
        {
            Add(ref hash, (int)formation.Type);
            foreach (var point in formation.Points)
            {
                Add(ref hash, point);
            }

            Add(ref hash, BitConverter.SingleToInt32Bits(formation.Width));
            Add(ref hash, BitConverter.SingleToInt32Bits(formation.Strength));
        }

        return hash;
    }

    private static void Add(ref ulong hash, Vector3 value)
    {
        Add(ref hash, BitConverter.SingleToInt32Bits(value.X));
        Add(ref hash, BitConverter.SingleToInt32Bits(value.Y));
        Add(ref hash, BitConverter.SingleToInt32Bits(value.Z));
    }

    private static void Add(ref ulong hash, Vector2 value)
    {
        Add(ref hash, BitConverter.SingleToInt32Bits(value.X));
        Add(ref hash, BitConverter.SingleToInt32Bits(value.Y));
    }

    private static void Add(ref ulong hash, string value)
    {
        foreach (var character in value)
        {
            Add(ref hash, character);
        }
    }

    private static void Add(ref ulong hash, int value)
    {
        hash ^= unchecked((uint)value);
        hash *= 1099511628211UL;
    }
}
