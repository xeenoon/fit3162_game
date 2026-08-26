using System;
using System.IO;
using Game;

namespace Game.Tests;

public sealed class ProgressStoreTests
{
    [Fact]
    public void Progress_PersistsBestStarsAcrossInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), $"silent-labyrinth-{Guid.NewGuid():N}.json");
        try
        {
            var progress = new ProgressStore(path);
            Assert.True(progress.RecordCompletion(2, 2));
            Assert.False(progress.RecordCompletion(2, 1));

            var reloaded = new ProgressStore(path);

            Assert.Equal(2, reloaded.BestStars(2));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CorruptSave_DoesNotPreventFreshProgress()
    {
        var path = Path.Combine(Path.GetTempPath(), $"silent-labyrinth-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "not json");

            var progress = new ProgressStore(path);

            Assert.Equal(0, progress.BestStars(1));
            Assert.True(progress.RecordCompletion(1, 3));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
