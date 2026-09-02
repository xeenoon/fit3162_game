using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Game;

public sealed class Game1 : Microsoft.Xna.Framework.Game
{
    private static readonly Color Background = new(8, 13, 18);
    private static readonly Color Ink = new(218, 229, 216);
    private static readonly Color MutedInk = new(125, 151, 144);
    private static readonly Color Gold = new(239, 190, 92);
    private static readonly Color Teal = new(76, 211, 186);

    private readonly GraphicsDeviceManager _graphics;
    private readonly bool _renderOnly;
    private readonly GameNavigation _navigation = new();
    private PolygonRenderer _polygons = null!;
    private PixelFont _font = null!;
    private DungeonMap _dungeon = null!;
    private PlayerController _player = null!;
    private GameplaySession _session = null!;
    private GeneratedWorld _world = null!;
    private HillsWorldRenderer _hillsWorld = null!;
    private ProgressStore _progress = null!;
    private KeyboardState _previousKeyboard;
    private float _watcherTime;

    public Game1()
    {
        _renderOnly = Environment.GetEnvironmentVariable("SILENT_LABYRINTH_RENDER_ONLY") == "1";
        var render1080 = _renderOnly || Environment.GetEnvironmentVariable("SILENT_LABYRINTH_RENDER_1080") == "1";
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = render1080 ? 1920 : 1280,
            PreferredBackBufferHeight = render1080 ? 1080 : 720,
            SynchronizeWithVerticalRetrace = true,
        };

        IsMouseVisible = true;
        IsFixedTimeStep = true;
        Window.AllowUserResizing = false;
        Window.Title = "Silent Labyrinth";
    }

    protected override void LoadContent()
    {
        _polygons = new PolygonRenderer(GraphicsDevice);
        _font = new PixelFont(_polygons);
        _world = WorldGenerator.Generate(ReadSeed(), worldScale: ReadWorldScale());
        _hillsWorld = new HillsWorldRenderer(GraphicsDevice, _world);
        _dungeon = CreateSelectedDungeon();
        _player = new PlayerController(_dungeon.StartPosition, radius: 11f, speed: 190f);
        _session = new GameplaySession(_dungeon, 1);
        _progress = new ProgressStore(ProgressStore.DefaultPath());
        UpdateWindowTitle();
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        if (_renderOnly)
        {
            if (keyboard.IsKeyDown(Keys.Escape))
            {
                Exit();
            }
            _previousKeyboard = keyboard;
            Window.Title = "Silent Labyrinth | Procedural Terrain | 1920x1080";
            base.Update(gameTime);
            return;
        }

        UpdateNavigation(keyboard);

        if (_navigation.QuitRequested)
        {
            Exit();
            return;
        }

        if (_navigation.Screen == AppScreen.Gameplay)
        {
            UpdateGameplay(keyboard, gameTime);
        }

        _previousKeyboard = keyboard;
        UpdateWindowTitle();
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_renderOnly)
        {
            _hillsWorld.Render(gameTime, 0);
            GraphicsDevice.Clear(Background);
            _polygons.Begin(GraphicsDevice.Viewport);
            _polygons.DrawTexturedTriangles(
            [
                Vector2.Zero,
                new Vector2(1280f, 0f),
                new Vector2(1280f, 720f),
                Vector2.Zero,
                new Vector2(1280f, 720f),
                new Vector2(0f, 720f),
            ],
            _hillsWorld.Scene,
            new Rectangle(0, 0, _hillsWorld.Scene.Width, _hillsWorld.Scene.Height),
            Color.White);
            base.Draw(gameTime);
            return;
        }

        if (_navigation.Screen == AppScreen.WorldMap)
        {
            _hillsWorld.Render(gameTime, _navigation.DungeonIndex);
        }

        GraphicsDevice.Clear(Background);
        _polygons.Begin(GraphicsDevice.Viewport);

        switch (_navigation.Screen)
        {
            case AppScreen.MainMenu:
                DrawMainMenu();
                break;
            case AppScreen.HowToPlay:
                DrawHowToPlay();
                break;
            case AppScreen.WorldMap:
                DrawWorldMap();
                break;
            case AppScreen.Gameplay:
                DrawGameplay();
                break;
        }

        base.Draw(gameTime);
    }

    private void UpdateNavigation(KeyboardState keyboard)
    {
        if (_navigation.Screen != AppScreen.Gameplay &&
            (Pressed(keyboard, Keys.Up) || Pressed(keyboard, Keys.W) ||
             Pressed(keyboard, Keys.Left) || Pressed(keyboard, Keys.A)))
        {
            _navigation.MovePrevious();
        }

        if (_navigation.Screen != AppScreen.Gameplay &&
            (Pressed(keyboard, Keys.Down) || Pressed(keyboard, Keys.S) ||
             Pressed(keyboard, Keys.Right) || Pressed(keyboard, Keys.D)))
        {
            _navigation.MoveNext();
        }

        if (_navigation.Screen != AppScreen.Gameplay &&
            (Pressed(keyboard, Keys.Enter) || Pressed(keyboard, Keys.Space)))
        {
            if (_navigation.Confirm() && _navigation.Screen == AppScreen.Gameplay)
            {
                StartSelectedDungeon();
            }
        }

        if (Pressed(keyboard, Keys.Escape))
        {
            if (_navigation.Screen == AppScreen.MainMenu)
            {
                Exit();
            }
            else
            {
                _navigation.Back();
            }
        }
    }

    private void UpdateGameplay(KeyboardState keyboard, GameTime gameTime)
    {
        if (Pressed(keyboard, Keys.R))
        {
            RestartDungeon();
            return;
        }

        switch (_session.Phase)
        {
            case GameplayPhase.Exploring:
                var movement = new MovementInput(
                    Horizontal: Axis(keyboard, Keys.A, Keys.Left, Keys.D, Keys.Right),
                    Vertical: Axis(keyboard, Keys.W, Keys.Up, Keys.S, Keys.Down));
                _player.Update(movement, (float)gameTime.ElapsedGameTime.TotalSeconds, _dungeon);
                _watcherTime += (float)gameTime.ElapsedGameTime.TotalSeconds;
                _session.CheckHazards(_player.Position, WatcherPosition());
                if (Pressed(keyboard, Keys.E))
                {
                    _session.Interact(_player.Position);
                    if (_session.Phase == GameplayPhase.Completed)
                    {
                        _progress.RecordCompletion(_navigation.DungeonIndex + 1, _session.Stars);
                    }
                }
                break;
            case GameplayPhase.PinTumbler:
                if (Pressed(keyboard, Keys.Left) || Pressed(keyboard, Keys.A)) _session.PinPuzzle!.Move(-1);
                if (Pressed(keyboard, Keys.Right) || Pressed(keyboard, Keys.D)) _session.PinPuzzle!.Move(1);
                if (Pressed(keyboard, Keys.Up) || Pressed(keyboard, Keys.W)) _session.PinPuzzle!.Adjust(1);
                if (Pressed(keyboard, Keys.Down) || Pressed(keyboard, Keys.S)) _session.PinPuzzle!.Adjust(-1);
                if (Pressed(keyboard, Keys.Enter) || Pressed(keyboard, Keys.Space)) _session.FinishPinPuzzle();
                break;
            case GameplayPhase.VaultDial:
                if (Pressed(keyboard, Keys.Left) || Pressed(keyboard, Keys.A)) _session.TurnVaultDial(DialDirection.Left);
                else if (Pressed(keyboard, Keys.Right) || Pressed(keyboard, Keys.D)) _session.TurnVaultDial(DialDirection.Right);
                else if (Pressed(keyboard, Keys.Up) || Pressed(keyboard, Keys.W)) _session.TurnVaultDial(DialDirection.Up);
                else if (Pressed(keyboard, Keys.Down) || Pressed(keyboard, Keys.S)) _session.TurnVaultDial(DialDirection.Down);
                break;
            case GameplayPhase.LightPuzzle:
                if (Pressed(keyboard, Keys.Left) || Pressed(keyboard, Keys.A)) _session.LightPuzzle!.Move(-1);
                if (Pressed(keyboard, Keys.Right) || Pressed(keyboard, Keys.D)) _session.LightPuzzle!.Move(1);
                if (Pressed(keyboard, Keys.Up) || Pressed(keyboard, Keys.Down) ||
                    Pressed(keyboard, Keys.W) || Pressed(keyboard, Keys.S)) _session.LightPuzzle!.Toggle();
                if (Pressed(keyboard, Keys.Enter) || Pressed(keyboard, Keys.Space)) _session.FinishLightPuzzle();
                break;
            case GameplayPhase.Completed:
                if (Pressed(keyboard, Keys.Enter) || Pressed(keyboard, Keys.Space)) _navigation.ReturnToWorldMap();
                break;
            case GameplayPhase.GameOver:
                if (Pressed(keyboard, Keys.Enter) || Pressed(keyboard, Keys.Space))
                {
                    RestartDungeon();
                }
                break;
        }
    }

    private void DrawMainMenu()
    {
        DrawHeader("A NON VIOLENT PUZZLE ADVENTURE");
        _font.Draw("SILENT", new Vector2(414, 130), 9, Ink);
        _font.Draw("LABYRINTH", new Vector2(330, 210), 9, Teal);
        _font.Draw("EVERY PATH REMEMBERS", new Vector2(466, 292), 2, MutedInk);

        string[] options = ["BEGIN JOURNEY", "HOW TO PLAY", "QUIT"];
        for (var index = 0; index < options.Length; index++)
        {
            var top = 370 + index * 70;
            var selected = index == _navigation.MenuIndex;
            var fill = selected ? new Color(27, 57, 60) : new Color(13, 23, 29);
            var edge = selected ? Teal : new Color(42, 60, 62);
            _polygons.DrawFilledRectangle(420, top, 860, top + 50, fill);
            _polygons.DrawFilledRectangle(420, top, 426, top + 50, edge);
            _font.Draw(options[index], new Vector2(470, top + 17), 3, selected ? Ink : MutedInk);
        }

        _font.Draw("UP DOWN  SELECT     ENTER  CONFIRM", new Vector2(391, 624), 2, MutedInk);
    }

    private void DrawHowToPlay()
    {
        DrawHeader("FIELD NOTES");
        _font.Draw("HOW TO PLAY", new Vector2(392, 112), 7, Ink);
        DrawInstructionCard(150, "MOVE", "WASD OR ARROW KEYS", Gold);
        DrawInstructionCard(265, "INTERACT", "PRESS E NEAR OBJECTS", Teal);
        DrawInstructionCard(380, "THINK", "LIGHT AND LOCKS OPEN PATHS", new Color(170, 141, 230));
        DrawInstructionCard(495, "SURVIVE", "EVADE WATCHERS  NO COMBAT", new Color(229, 115, 102));
        _font.Draw("ESC  BACK TO MENU", new Vector2(506, 642), 2, MutedInk);
    }

    private void DrawInstructionCard(int top, string heading, string detail, Color accent)
    {
        _polygons.DrawFilledRectangle(252, top, 1028, top + 86, new Color(13, 23, 29));
        _polygons.DrawFilledRectangle(252, top, 260, top + 86, accent);
        _font.Draw(heading, new Vector2(290, top + 18), 3, accent);
        _font.Draw(detail, new Vector2(290, top + 52), 2, Ink);
    }

    private void DrawWorldMap()
    {
        DrawHeader($"WORLD SEED {_world.Seed}");
        const int sceneLeft = 58;
        const int sceneTop = 86;
        const int sceneRight = 1222;
        const int sceneBottom = 566;
        _polygons.DrawFilledRectangle(sceneLeft - 3, sceneTop - 3, sceneRight + 3, sceneBottom + 3, new Color(41, 67, 68));
        _polygons.DrawTexturedTriangles(
        [
            new Vector2(sceneLeft, sceneTop),
            new Vector2(sceneRight, sceneTop),
            new Vector2(sceneRight, sceneBottom),
            new Vector2(sceneLeft, sceneTop),
            new Vector2(sceneRight, sceneBottom),
            new Vector2(sceneLeft, sceneBottom),
        ],
        _hillsWorld.Scene,
        new Rectangle(sceneLeft, sceneTop, sceneRight - sceneLeft, sceneBottom - sceneTop),
        Color.White);

        _polygons.DrawFilledRectangle(76, 102, 442, 144, new Color(8, 18, 18, 205));
        _font.Draw("HILLS + MOUNTAINS", new Vector2(96, 116), 3, Ink);
        _polygons.DrawFilledRectangle(58, 496, 1222, 638, new Color(9, 17, 19, 232));
        _polygons.DrawFilledRectangle(58, 496, 66, 638, Gold);

        var choice = _world.Dungeons[_navigation.DungeonIndex];
        var theme = ThemeFor(choice.Biome);
        _font.Draw($"DUNGEON {choice.Number} OF 3", new Vector2(92, 518), 2, MutedInk);
        _font.Draw(choice.Name, new Vector2(92, 548), 4, Ink);
        _font.Draw(choice.Biome.ToString().ToUpperInvariant(), new Vector2(688, 528), 4, theme.Accent);
        var best = _progress.BestStars(choice.Number);
        _font.Draw(best == 0 ? "UNEXPLORED" : $"BEST {new string('*', best)}",
            new Vector2(688, 574), 2, best == 0 ? MutedInk : Gold);
        _font.Draw("GOLD RING  SELECTED", new Vector2(936, 528), 2, Gold);
        _font.Draw("LEFT RIGHT  CHOOSE     ENTER  DESCEND     ESC  BACK", new Vector2(286, 672), 2, MutedInk);
    }

    private void DrawDungeonCard(DungeonChoice choice, Rectangle bounds, bool selected)
    {
        var theme = ThemeFor(choice.Biome);
        _polygons.DrawFilledRectangle(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom,
            selected ? theme.Panel : new Color(13, 23, 29));
        _polygons.DrawFilledRectangle(bounds.Left, bounds.Top, bounds.Right, bounds.Top + 8,
            selected ? theme.Accent : new Color(42, 60, 62));
        _polygons.DrawRegularPolygon(new Vector2(bounds.Center.X, bounds.Top + 94), 48, 8, theme.Deep, MathHelper.PiOver4);
        _polygons.DrawRegularPolygon(new Vector2(bounds.Center.X, bounds.Top + 94), 28, 8, theme.Accent, MathHelper.PiOver4);
        _font.Draw($"DUNGEON {choice.Number}", new Vector2(bounds.Left + 92, bounds.Top + 172), 2, MutedInk);
        _font.Draw(choice.Name, new Vector2(bounds.Left + 34, bounds.Top + 210), 3, selected ? Ink : MutedInk);
        _font.Draw(choice.Biome.ToString().ToUpperInvariant(), new Vector2(bounds.Left + 112, bounds.Top + 258), 3, theme.Accent);
        var best = _progress.BestStars(choice.Number);
        _font.Draw(best == 0 ? "UNEXPLORED" : $"BEST {new string('*', best)}",
            new Vector2(bounds.Left + 94, bounds.Top + 295), 2, best == 0 ? MutedInk : Gold);
    }

    private void DrawGameplay()
    {
        var choice = _world.Dungeons[_navigation.DungeonIndex];
        var theme = ThemeFor(choice.Biome);
        switch (_session.Phase)
        {
            case GameplayPhase.Exploring:
                DrawExploration(choice, theme);
                break;
            case GameplayPhase.PinTumbler:
                DrawPinTumbler(theme);
                break;
            case GameplayPhase.VaultDial:
                DrawVaultDial(theme);
                break;
            case GameplayPhase.LightPuzzle:
                DrawLightPuzzle(theme);
                break;
            case GameplayPhase.Completed:
                DrawCompletion(choice, theme);
                break;
            case GameplayPhase.GameOver:
                DrawGameOver(theme);
                break;
        }
    }

    private void DrawExploration(DungeonChoice choice, BiomeTheme theme)
    {
        DrawHeader($"{choice.Name}  {choice.Biome}  SEED {_dungeon.Seed}");
        DrawDungeon(theme);
        DrawGameplayObjects(theme);
        DrawPlayer(theme.Accent);

        _polygons.DrawFilledRectangle(24, 640, 1256, 704, new Color(13, 23, 29));
        _font.Draw(_session.Status, new Vector2(44, 650), 2, theme.Accent);
        _font.Draw("MOVE  WASD OR ARROWS    E  INTERACT    R  RESTART    ESC  MAP", new Vector2(44, 680), 2, MutedInk);
    }

    private void DrawGameplayObjects(BiomeTheme theme)
    {
        var npc = _dungeon.CellCenter(_session.NpcCell);
        _polygons.DrawRegularPolygon(npc, 12, 4, Teal, MathHelper.PiOver4);
        _polygons.DrawRegularPolygon(npc, 5, 8, Ink);

        var switchCenter = _dungeon.CellCenter(_session.SwitchCell);
        var switchColor = _session.SwitchActivated ? Teal : MutedInk;
        _polygons.DrawFilledRectangle(switchCenter.X - 11, switchCenter.Y - 8, switchCenter.X + 11, switchCenter.Y + 9, new Color(13, 23, 29));
        _polygons.DrawFilledRectangle(switchCenter.X - 7, switchCenter.Y - 4, switchCenter.X + 7, switchCenter.Y + 5, switchColor);

        if (!_session.DoorUnlocked)
        {
            var lockCenter = _dungeon.CellCenter(_session.LockCell);
            _polygons.DrawFilledRectangle(lockCenter.X - 10, lockCenter.Y - 9, lockCenter.X + 10, lockCenter.Y + 10, new Color(91, 57, 34));
            _polygons.DrawRegularPolygon(lockCenter - new Vector2(0, 8), 7, 12, Gold);
            DrawGate(_session.DoorCell, Gold);
        }

        if (!_session.LightSolved)
        {
            var light = _dungeon.CellCenter(_session.LightCell);
            _polygons.DrawRegularPolygon(light, 15, 8, new Color(153, 116, 221, 65), MathHelper.PiOver4);
            _polygons.DrawRegularPolygon(light, 8, 4, new Color(181, 151, 238), MathHelper.PiOver4);
            DrawGate(_session.LightGateCell, new Color(181, 151, 238));
        }

        var chest = _dungeon.CellCenter(_session.ChestCell);
        _polygons.DrawFilledRectangle(chest.X - 13, chest.Y - 9, chest.X + 13, chest.Y + 10, new Color(112, 70, 34));
        _polygons.DrawFilledRectangle(chest.X - 13, chest.Y - 9, chest.X + 13, chest.Y - 2, Gold);
        _polygons.DrawFilledRectangle(chest.X - 2, chest.Y - 2, chest.X + 2, chest.Y + 8, Gold);

        var pit = _dungeon.CellCenter(_session.PitCell);
        _polygons.DrawRegularPolygon(pit, 13, 18, new Color(3, 6, 9));
        _polygons.DrawRegularPolygon(pit, 8, 18, new Color(13, 17, 25));

        var watcher = WatcherPosition();
        _polygons.DrawRegularPolygon(watcher, 14, 16, new Color(93, 32, 35));
        _polygons.DrawRegularPolygon(watcher, 8, 16, new Color(235, 93, 86));
        _polygons.DrawRegularPolygon(watcher, 3, 12, Ink);

        var promptCell = InteractionCell();
        if (promptCell is Point cell)
        {
            var prompt = _dungeon.CellCenter(cell) - new Vector2(5, 31);
            _polygons.DrawRegularPolygon(prompt, 11, 8, new Color(13, 23, 29));
            _font.Draw("E", prompt - new Vector2(5, 7), 2, theme.Accent);
        }
    }

    private Point? InteractionCell()
        => _session.NearestInteractionCell(_player.Position);

    private void DrawGate(Point cell, Color color)
    {
        var bounds = _dungeon.GetCellBounds(cell.X, cell.Y);
        for (var bar = 0; bar < 4; bar++)
        {
            var left = bounds.Left + 5 + bar * 8;
            _polygons.DrawFilledRectangle(left, bounds.Top + 3, left + 3, bounds.Bottom - 3, color);
        }
    }

    private void DrawPinTumbler(BiomeTheme theme)
    {
        DrawHeader("LOCKPUZZLE  PIN TUMBLER");
        _font.Draw("PIN TUMBLER", new Vector2(362, 112), 7, Ink);
        _font.Draw("MATCH EACH PIN TO ITS GLOWING MARK", new Vector2(410, 174), 2, MutedInk);
        var puzzle = _session.PinPuzzle!;
        for (var pin = 0; pin < puzzle.Heights.Count; pin++)
        {
            var left = 338 + pin * 218;
            var selected = pin == puzzle.SelectedPin;
            _polygons.DrawFilledRectangle(left, 230, left + 142, 500, selected ? theme.Panel : new Color(13, 23, 29));
            _polygons.DrawFilledRectangle(left, 230, left + 142, 237, selected ? theme.Accent : theme.Edge);
            var targetY = 455 - puzzle.TargetHeights[pin] * 50;
            _polygons.DrawFilledRectangle(left + 18, targetY, left + 124, targetY + 5, theme.Accent);
            var pinTop = 465 - puzzle.Heights[pin] * 50;
            _polygons.DrawFilledRectangle(left + 55, pinTop, left + 87, 480, Gold);
            _font.Draw($"PIN {pin + 1}", new Vector2(left + 39, 520), 2, selected ? Ink : MutedInk);
        }

        _font.Draw("LEFT RIGHT  CHOOSE PIN    UP DOWN  SET HEIGHT    ENTER  TRY LOCK", new Vector2(240, 626), 2, MutedInk);
        _font.Draw(_session.Status, new Vector2(470, 670), 2, theme.Accent);
    }

    private void DrawVaultDial(BiomeTheme theme)
    {
        DrawHeader("LOCKPUZZLE  VAULT DIAL");
        _font.Draw("VAULT DIAL", new Vector2(414, 112), 7, Ink);
        _font.Draw("REPEAT THE FOUR DIRECTION COMBINATION", new Vector2(406, 174), 2, MutedInk);
        var puzzle = _session.VaultPuzzle!;
        var center = new Vector2(640, 365);
        _polygons.DrawRegularPolygon(center, 132, 48, new Color(13, 23, 29));
        _polygons.DrawRegularPolygon(center, 104, 48, theme.Panel);
        _polygons.DrawRegularPolygon(center, 64, 12, theme.Accent, _watcherTime);
        _polygons.DrawRegularPolygon(center, 20, 12, Gold);
        string[] directions = ["LEFT", "RIGHT", "UP", "DOWN"];
        for (var index = 0; index < directions.Length; index++)
        {
            var color = index < puzzle.Progress ? Gold : MutedInk;
            _font.Draw(directions[index], new Vector2(360 + index * 170, 548), 3, color);
        }

        _font.Draw($"SEQUENCE {puzzle.Progress} OF {puzzle.StepCount}", new Vector2(520, 610), 3, theme.Accent);
        _font.Draw("USE ARROW KEYS OR WASD", new Vector2(492, 662), 2, MutedInk);
    }

    private void DrawLightPuzzle(BiomeTheme theme)
    {
        DrawHeader("LIGHT PUZZLE  MIRROR ARRAY");
        _font.Draw("MIRROR ARRAY", new Vector2(362, 112), 7, Ink);
        _font.Draw("TURN THE FIRST AND THIRD MIRRORS TOWARD THE CRYSTAL", new Vector2(314, 174), 2, MutedInk);
        var puzzle = _session.LightPuzzle!;
        _polygons.DrawFilledRectangle(160, 362, 1120, 370, new Color(theme.Accent, 55));
        _polygons.DrawRegularPolygon(new Vector2(180, 366), 18, 16, theme.Accent);
        for (var mirror = 0; mirror < puzzle.Orientations.Count; mirror++)
        {
            var center = new Vector2(410 + mirror * 230, 366);
            var selected = mirror == puzzle.SelectedMirror;
            _polygons.DrawRegularPolygon(center, 50, 8, selected ? theme.Panel : new Color(13, 23, 29), MathHelper.PiOver4);
            var slope = puzzle.Orientations[mirror] ? 1f : -1f;
            _polygons.DrawLineSegments(
            [
                center + new Vector2(-28, -28 * slope),
                center + new Vector2(28, 28 * slope),
            ], selected ? Gold : Ink, 8f);
            _font.Draw($"MIRROR {mirror + 1}", new Vector2(center.X - 55, 446), 2, selected ? Ink : MutedInk);
        }

        _polygons.DrawRegularPolygon(new Vector2(1095, 366), 24, 4, new Color(181, 151, 238), MathHelper.PiOver4);
        _font.Draw("LEFT RIGHT  CHOOSE    UP DOWN  TURN    ENTER  TEST BEAM", new Vector2(300, 616), 2, MutedInk);
        _font.Draw(_session.Status, new Vector2(456, 662), 2, theme.Accent);
    }

    private void DrawCompletion(DungeonChoice choice, BiomeTheme theme)
    {
        DrawHeader($"{choice.Name}  COMPLETE");
        _font.Draw("DUNGEON COMPLETE", new Vector2(286, 126), 7, Ink);
        _font.Draw(new string('*', _session.Stars), new Vector2(532, 250), 10, Gold);
        _font.Draw($"{_session.Stars} OF 3 STARS", new Vector2(506, 340), 3, theme.Accent);
        DrawResultLine(415, "ESCAPED THE LABYRINTH", true, theme.Accent);
        DrawResultLine(465, "AVOIDED THE WATCHER", !_session.WasCaught, theme.Accent);
        DrawResultLine(515, "FOUND THE KEEPER", _session.HiddenObjective, theme.Accent);
        _font.Draw("INVENTORY  BRASS KEY  LUMEN SHARD", new Vector2(410, 588), 2, MutedInk);
        _font.Draw("ENTER  RETURN TO WORLD MAP", new Vector2(454, 654), 2, Ink);
    }

    private void DrawResultLine(int top, string label, bool achieved, Color accent)
    {
        _font.Draw(achieved ? "YES" : "NO", new Vector2(390, top), 3, achieved ? accent : new Color(229, 115, 102));
        _font.Draw(label, new Vector2(480, top), 3, Ink);
    }

    private void DrawGameOver(BiomeTheme theme)
    {
        DrawHeader("THE LABYRINTH CLAIMS ANOTHER");
        var danger = new Color(229, 92, 86);
        _font.Draw("GAME OVER", new Vector2(414, 150), 8, danger);
        _polygons.DrawRegularPolygon(new Vector2(640, 355), 90, 20, new Color(59, 24, 29));
        _polygons.DrawRegularPolygon(new Vector2(640, 355), 52, 20, danger);
        _polygons.DrawRegularPolygon(new Vector2(640, 355), 18, 16, Ink);
        var reason = _session.GameOverReason == GameOverReason.Pit ? "FELL INTO THE DEPTHS" : "CAUGHT BY A WATCHER";
        _font.Draw(reason, new Vector2(450, 490), 3, Ink);
        _font.Draw("R OR ENTER  RESTART LEVEL", new Vector2(454, 584), 3, theme.Accent);
        _font.Draw("ESC  RETURN TO WORLD MAP", new Vector2(490, 646), 2, MutedInk);
    }

    private void DrawDungeon(BiomeTheme theme)
    {
        for (var row = 0; row < _dungeon.Rows; row++)
        {
            for (var column = 0; column < _dungeon.Columns; column++)
            {
                var bounds = _dungeon.GetCellBounds(column, row);
                if (_dungeon.IsWalkable(column, row))
                {
                    var floor = (column + row) % 2 == 0 ? theme.Floor : theme.FloorAlt;
                    _polygons.DrawFilledRectangle(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, floor);
                    continue;
                }

                _polygons.DrawFilledRectangle(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, theme.Deep);
                if (_dungeon.IsWalkable(column, row + 1))
                {
                    _polygons.DrawFilledRectangle(bounds.Left, bounds.Bottom - 4, bounds.Right, bounds.Bottom, theme.Edge);
                }
            }
        }

        foreach (var decoration in _dungeon.Decorations)
        {
            if (!IsGameplayObjectCell(decoration.Cell))
            {
                DrawDecoration(decoration, theme);
            }
        }

        var exit = _dungeon.GetCellBounds(_dungeon.ExitCell.X, _dungeon.ExitCell.Y);
        var center = new Vector2(exit.Center.X, exit.Center.Y);
        _polygons.DrawRegularPolygon(center, 14, 4, theme.Deep, MathHelper.PiOver4);
        _polygons.DrawRegularPolygon(center, 8, 4, theme.Accent, MathHelper.PiOver4);
    }

    private void DrawDecoration(DungeonDecoration decoration, BiomeTheme theme)
    {
        var bounds = _dungeon.GetCellBounds(decoration.Cell.X, decoration.Cell.Y);
        var center = new Vector2(bounds.Center.X, bounds.Center.Y);
        switch (decoration.Feature)
        {
            case DungeonFeature.Pillar:
                _polygons.DrawRegularPolygon(center + new Vector2(2, 4), 11, 8, new Color(7, 12, 15, 170));
                _polygons.DrawRegularPolygon(center, 10, 8, theme.Edge, MathHelper.PiOver4);
                _polygons.DrawRegularPolygon(center - new Vector2(0, 3), 6, 8, theme.FloorAlt, MathHelper.PiOver4);
                break;
            case DungeonFeature.Stairs:
                for (var step = 0; step < 4; step++)
                {
                    var inset = 5 + step * 3;
                    _polygons.DrawFilledRectangle(
                        bounds.Left + inset,
                        bounds.Top + 6 + step * 6,
                        bounds.Right - inset,
                        bounds.Top + 9 + step * 6,
                        theme.Edge);
                }
                break;
            case DungeonFeature.CrackedTile:
                _polygons.DrawLineSegments(
                [
                    center + new Vector2(-11, -8), center + new Vector2(-2, -1),
                    center + new Vector2(-2, -1), center + new Vector2(7, -6),
                    center + new Vector2(-2, -1), center + new Vector2(3, 10),
                    center + new Vector2(3, 10), center + new Vector2(11, 6),
                ], theme.Edge, 2f);
                break;
            case DungeonFeature.Pathway:
                var vertical = decoration.Variant % 2 == 0;
                _polygons.DrawFilledRectangle(
                    vertical ? center.X - 6 : bounds.Left + 2,
                    vertical ? bounds.Top + 2 : center.Y - 6,
                    vertical ? center.X + 6 : bounds.Right - 2,
                    vertical ? bounds.Bottom - 2 : center.Y + 6,
                    new Color(theme.Edge, 150));
                break;
            case DungeonFeature.Chest:
                _polygons.DrawFilledRectangle(center.X - 11, center.Y - 8, center.X + 11, center.Y + 9, new Color(112, 70, 34));
                _polygons.DrawFilledRectangle(center.X - 11, center.Y - 8, center.X + 11, center.Y - 3, Gold);
                _polygons.DrawFilledRectangle(center.X - 2, center.Y - 3, center.X + 2, center.Y + 7, Gold);
                break;
            case DungeonFeature.Light:
                _polygons.DrawRegularPolygon(center, 14, 16, new Color(theme.Accent, 55));
                _polygons.DrawRegularPolygon(center, 7, 8, theme.Accent, MathHelper.PiOver4);
                _polygons.DrawRegularPolygon(center, 3, 8, Ink, MathHelper.PiOver4);
                break;
            case DungeonFeature.Coin:
                _polygons.DrawRegularPolygon(center, 6, 12, new Color(119, 77, 24));
                _polygons.DrawRegularPolygon(center - new Vector2(0, 1), 4, 12, Gold);
                break;
        }
    }

    private void DrawPlayer(Color accent)
    {
        var position = _player.Position;
        _polygons.DrawRegularPolygon(position + new Vector2(2, 4), 15, 16, new Color(5, 9, 12, 170));
        _polygons.DrawRegularPolygon(position, 12, 16, Gold);
        _polygons.DrawRegularPolygon(position - new Vector2(2, 3), 4, 8, accent);
    }

    private void DrawHeader(string subtitle)
    {
        _polygons.DrawFilledRectangle(0, 0, 1280, 72, new Color(13, 23, 29));
        _polygons.DrawFilledRectangle(0, 70, 1280, 72, new Color(41, 67, 68));
        _font.Draw("SILENT LABYRINTH", new Vector2(32, 22), 4, Ink);
        _font.Draw(subtitle, new Vector2(820, 28), 2, MutedInk);
    }

    private void UpdateWindowTitle()
    {
        Window.Title = _navigation.Screen switch
        {
            AppScreen.MainMenu => $"Silent Labyrinth | Menu | Selected:{_navigation.SelectedMenuOption}",
            AppScreen.HowToPlay => "Silent Labyrinth | How To Play",
            AppScreen.WorldMap => $"Silent Labyrinth | World Map | Selected:{_navigation.DungeonIndex + 1} | Biome:{SelectedBiome()}",
            AppScreen.Gameplay => GameplayWindowTitle(),
            _ => "Silent Labyrinth",
        };
    }

    private string GameplayWindowTitle() => _session.Phase switch
    {
        GameplayPhase.Exploring => $"Silent Labyrinth | Explore | Dungeon:{_navigation.DungeonIndex + 1} | Biome:{SelectedBiome()} | X:{(int)_player.Position.X} Y:{(int)_player.Position.Y} | Status:{_session.Status}",
        GameplayPhase.PinTumbler => $"Silent Labyrinth | Lockpick | PIN TUMBLER | Pin:{_session.PinPuzzle!.SelectedPin + 1} | Values:{string.Concat(_session.PinPuzzle.Heights)}",
        GameplayPhase.VaultDial => $"Silent Labyrinth | Lockpick | VAULT DIAL | Progress:{_session.VaultPuzzle!.Progress}/{_session.VaultPuzzle.StepCount}",
        GameplayPhase.LightPuzzle => $"Silent Labyrinth | Light Puzzle | Mirror:{_session.LightPuzzle!.SelectedMirror + 1} | Values:{string.Concat(_session.LightPuzzle.Orientations.Select(value => value ? '1' : '0'))}",
        GameplayPhase.Completed => $"Silent Labyrinth | Complete | Dungeon:{_navigation.DungeonIndex + 1} | Stars:{_session.Stars}",
        GameplayPhase.GameOver => $"Silent Labyrinth | Game Over | Cause:{_session.GameOverReason.ToString().ToUpperInvariant()}",
        _ => "Silent Labyrinth",
    };

    private string SelectedBiome() => _world.Dungeons[_navigation.DungeonIndex].Biome.ToString().ToUpperInvariant();

    private bool Pressed(KeyboardState keyboard, Keys key) =>
        keyboard.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);

    private static float Axis(KeyboardState keyboard, Keys negative, Keys negativeAlt, Keys positive, Keys positiveAlt)
    {
        var result = 0f;
        if (keyboard.IsKeyDown(negative) || keyboard.IsKeyDown(negativeAlt)) result -= 1f;
        if (keyboard.IsKeyDown(positive) || keyboard.IsKeyDown(positiveAlt)) result += 1f;
        return result;
    }

    private static int ReadSeed() =>
        int.TryParse(Environment.GetEnvironmentVariable("SILENT_LABYRINTH_SEED"), out var seed)
            ? seed
            : Environment.TickCount;

    private static float ReadWorldScale() =>
        float.TryParse(Environment.GetEnvironmentVariable("SILENT_LABYRINTH_MAP_ZOOM"), out var scale) &&
        scale is 4f or 16f
            ? scale
            : 1f;

    private DungeonMap CreateSelectedDungeon()
    {
        var choice = _world.Dungeons[_navigation.DungeonIndex];
        return DungeonMap.CreateProceduralDungeon(
            unchecked(_world.Seed * 397 ^ choice.Number * 7919),
            choice.Number);
    }

    private void StartSelectedDungeon()
    {
        _dungeon = CreateSelectedDungeon();
        _player = new PlayerController(_dungeon.StartPosition, radius: 11f, speed: 190f);
        _session = new GameplaySession(_dungeon, _navigation.DungeonIndex + 1);
        _watcherTime = 0f;
    }

    private void RestartDungeon()
    {
        _session.Reset();
        _player.Reset(_dungeon.StartPosition);
        _watcherTime = 0f;
    }

    private Vector2 WatcherPosition() =>
        _dungeon.CellCenter(new Point(17, 1)) + new Vector2(MathF.Sin(_watcherTime * 1.8f) * 11f, 0);

    private bool IsGameplayObjectCell(Point cell) =>
        cell == _session.NpcCell || cell == _session.SwitchCell || cell == _session.LockCell || cell == _session.DoorCell ||
        cell == _session.LightCell || cell == _session.LightGateCell || cell == _session.ChestCell ||
        cell == _session.PitCell;

    private static BiomeTheme ThemeFor(Biome biome) => biome switch
    {
        Biome.Hills => new(new Color(35, 54, 36), new Color(43, 66, 40), new Color(18, 29, 20), new Color(75, 99, 60), new Color(174, 196, 104), new Color(47, 72, 43)),
        Biome.Mountain => new(new Color(56, 62, 61), new Color(68, 74, 72), new Color(27, 34, 36), new Color(104, 112, 111), new Color(191, 202, 198), new Color(55, 65, 64)),
        Biome.Mire => new(new Color(23, 43, 39), new Color(28, 54, 48), new Color(10, 24, 22), new Color(56, 86, 72), new Color(81, 190, 142), new Color(21, 55, 44)),
        Biome.Frost => new(new Color(27, 43, 56), new Color(32, 52, 68), new Color(12, 24, 37), new Color(63, 91, 112), new Color(115, 206, 231), new Color(25, 60, 76)),
        Biome.Ember => new(new Color(56, 36, 31), new Color(68, 42, 34), new Color(31, 18, 19), new Color(105, 61, 48), new Color(238, 125, 75), new Color(73, 37, 30)),
        Biome.Grove => new(new Color(31, 49, 36), new Color(38, 59, 41), new Color(15, 27, 20), new Color(70, 98, 65), new Color(151, 210, 96), new Color(38, 67, 42)),
        _ => new(new Color(43, 39, 51), new Color(52, 47, 62), new Color(23, 20, 30), new Color(82, 75, 96), new Color(181, 152, 218), new Color(51, 42, 68)),
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hillsWorld?.Dispose();
            _polygons?.Dispose();
        }

        base.Dispose(disposing);
    }

    private readonly record struct BiomeTheme(
        Color Floor,
        Color FloorAlt,
        Color Deep,
        Color Edge,
        Color Accent,
        Color Panel);
}
