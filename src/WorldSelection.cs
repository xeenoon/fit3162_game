using System;
using System.Collections.Generic;

namespace Game;

public enum AppScreen
{
    MainMenu,
    HowToPlay,
    WorldMap,
    Gameplay,
}

public enum Biome
{
    Hills,
    Mire,
    Frost,
    Ember,
    Grove,
    Ruins,
    Mountain,
}

public sealed record DungeonChoice(int Number, string Name, Biome Biome, int Difficulty);

public sealed class GeneratedWorld
{
    public GeneratedWorld(
        WorldSeed worldSeed,
        TerrainSettings terrainSettings,
        HillsBiomeDefinition hills,
        MountainBiomeDefinition mountains,
        GeneratedTerrain terrain,
        IReadOnlyList<DungeonChoice> dungeons,
        IReadOnlyList<DungeonSite> dungeonSites,
        IReadOnlyList<PropPlacement> props,
        IReadOnlyList<TerrainFeature> features,
        IReadOnlyList<LakeDefinition> lakes,
        IReadOnlyList<MountainFormation> mountainFormations)
    {
        WorldSeed = worldSeed;
        TerrainSettings = terrainSettings;
        Hills = hills;
        Mountains = mountains;
        Terrain = terrain;
        Dungeons = dungeons;
        DungeonSites = dungeonSites;
        Props = props;
        Features = features;
        Lakes = lakes;
        MountainFormations = mountainFormations;
    }

    public WorldSeed WorldSeed { get; }
    public int Seed => WorldSeed.Value;
    public TerrainSettings TerrainSettings { get; }
    public HillsBiomeDefinition Hills { get; }
    public MountainBiomeDefinition Mountains { get; }
    public GeneratedTerrain Terrain { get; }
    public IReadOnlyList<DungeonChoice> Dungeons { get; }
    public IReadOnlyList<DungeonSite> DungeonSites { get; }
    public IReadOnlyList<PropPlacement> Props { get; }
    public IReadOnlyList<TerrainFeature> Features { get; }
    public IReadOnlyList<LakeDefinition> Lakes { get; }
    public IReadOnlyList<MountainFormation> MountainFormations { get; }
}

public static class WorldGenerator
{
    public const int DungeonCount = 3;
    internal static readonly string[] DungeonNames = ["WHISPER VAULT", "GLASS KEEP", "SUNKEN ARCHIVE"];

    public static GeneratedWorld Generate(
        int seed,
        HillsBiomeDefinition? hills = null,
        MountainBiomeDefinition? mountains = null,
        float worldScale = 1f) =>
        HillsWorldGeneration.Generate(
            seed,
            hills ?? HillsBiomeDefinition.Default,
            mountains ?? MountainBiomeDefinition.Default,
            worldScale);
}

public sealed class GameNavigation
{
    private static readonly string[] MenuOptions = ["BEGIN JOURNEY", "HOW TO PLAY", "QUIT"];

    public AppScreen Screen { get; private set; } = AppScreen.MainMenu;
    public int MenuIndex { get; private set; }
    public int DungeonIndex { get; private set; }
    public bool QuitRequested { get; private set; }
    public string SelectedMenuOption => MenuOptions[MenuIndex];

    public void MovePrevious()
    {
        if (Screen == AppScreen.MainMenu)
        {
            MenuIndex = Wrap(MenuIndex - 1, MenuOptions.Length);
        }
        else if (Screen == AppScreen.WorldMap)
        {
            DungeonIndex = Wrap(DungeonIndex - 1, WorldGenerator.DungeonCount);
        }
    }

    public void MoveNext()
    {
        if (Screen == AppScreen.MainMenu)
        {
            MenuIndex = Wrap(MenuIndex + 1, MenuOptions.Length);
        }
        else if (Screen == AppScreen.WorldMap)
        {
            DungeonIndex = Wrap(DungeonIndex + 1, WorldGenerator.DungeonCount);
        }
    }

    public bool Confirm()
    {
        if (Screen == AppScreen.MainMenu)
        {
            switch (MenuIndex)
            {
                case 0:
                    Screen = AppScreen.WorldMap;
                    return true;
                case 1:
                    Screen = AppScreen.HowToPlay;
                    return true;
                case 2:
                    QuitRequested = true;
                    return true;
            }
        }

        if (Screen == AppScreen.WorldMap)
        {
            Screen = AppScreen.Gameplay;
            return true;
        }

        return false;
    }

    public void Back()
    {
        Screen = Screen switch
        {
            AppScreen.Gameplay => AppScreen.WorldMap,
            AppScreen.WorldMap or AppScreen.HowToPlay => AppScreen.MainMenu,
            _ => AppScreen.MainMenu,
        };
    }

    public void ReturnToWorldMap() => Screen = AppScreen.WorldMap;

    private static int Wrap(int value, int count) => (value % count + count) % count;
}
