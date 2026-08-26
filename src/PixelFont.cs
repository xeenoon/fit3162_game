using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Game;

public sealed class PixelFont
{
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private readonly PolygonRenderer _renderer;

    private static readonly IReadOnlyDictionary<char, string> Glyphs = new Dictionary<char, string>
    {
        ['A'] = ".###.#...######...##...#", ['B'] = "####.#...#####.#...#####.",
        ['C'] = ".####....#....#....#.###.", ['D'] = "####.#...##...##...#####.",
        ['E'] = "#####....####.#....#####", ['F'] = "#####....####.#....#....",
        ['G'] = ".####....#.####...#.###.", ['H'] = "#...##...######...##...#",
        ['I'] = "#####..#....#....#..#####", ['J'] = "..###...#....#.#..#.##..",
        ['K'] = "#...##..#.##..#..#.#...#", ['L'] = "#....#....#....#....#####",
        ['M'] = "#...#######.#.##...##...#", ['N'] = "#...###..##.#.##..###...#",
        ['O'] = ".###.#...##...##...#.###.", ['P'] = "####.#...#####.#....#....",
        ['Q'] = ".###.#...##...##..#..##.#", ['R'] = "####.#...#####.#..#.#...#",
        ['S'] = ".####.... .###.....####.".Replace(" ", ""), ['T'] = "#####..#....#....#....#..",
        ['U'] = "#...##...##...##...#.###.", ['V'] = "#...##...##...#.#.#...#..",
        ['W'] = "#...##...##.#.#######...#", ['X'] = "#...#.#.#...#...#.#.#...#",
        ['Y'] = "#...#.#.#...#....#....#..", ['Z'] = "#####...#...#...#...#####",
        ['0'] = ".###.#..###.#.##..#.###.", ['1'] = "..#...##....#....#...###.",
        ['2'] = ".###.#...#...#...#..#####", ['3'] = "####....#..###....#####.",
        ['4'] = "#..##..##...######...#", ['5'] = "#####....####....#####.",
        ['6'] = ".###.#....####.#...#.###.", ['7'] = "#####...#...#...#....#..",
        ['8'] = ".###.#...#.###.#...#.###.", ['9'] = ".###.#...#.####....#.###.",
        [':'] = ".....#....#.........#....#....", ['-'] = "..........#####..........",
        ['.'] = "........................#....", ['|'] = "..#....#....#....#....#....#....#..",
        ['*'] = ".....#.#...#...#####.#.#...#.....",
    };

    public PixelFont(PolygonRenderer renderer) => _renderer = renderer;

    public void Draw(string text, Vector2 position, int scale, Color color)
    {
        var cursor = position;
        foreach (var rawCharacter in text)
        {
            var character = char.ToUpperInvariant(rawCharacter);
            if (character == ' ')
            {
                cursor.X += (GlyphWidth + 1) * scale;
                continue;
            }

            if (Glyphs.TryGetValue(character, out var glyph))
            {
                DrawGlyph(glyph, cursor, scale, color);
            }

            cursor.X += (GlyphWidth + 1) * scale;
        }
    }

    private void DrawGlyph(string glyph, Vector2 position, int scale, Color color)
    {
        for (var row = 0; row < GlyphHeight; row++)
        {
            for (var column = 0; column < GlyphWidth; column++)
            {
                var index = row * GlyphWidth + column;
                if (index >= glyph.Length || glyph[index] != '#')
                {
                    continue;
                }

                var left = position.X + column * scale;
                var top = position.Y + row * scale;
                _renderer.DrawFilledRectangle(left, top, left + scale, top + scale, color);
            }
        }
    }
}
