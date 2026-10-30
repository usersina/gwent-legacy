using System.IO;
using Gwent.Rules;
using NUnit.Framework;

namespace Gwent.Story.Tests
{
    // The story's save (StorySave): written and read back whole; a damaged save, or a newer version's, is refused and left
    // as it is; Start over sets the old one aside with its decks, never deletes them
    public class StorySaveTests
    {
        private string folder;

        [SetUp]
        public void UseATemporaryFolder()
        {
            folder = Path.Combine(Path.GetTempPath(), "gwent-story-tests-" + System.Guid.NewGuid().ToString("N"));
            StorySave.FolderOverride = folder;
        }

        [TearDown]
        public void RemoveIt()
        {
            StorySave.FolderOverride = null;
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        private static StoryProgress Played()
        {
            StoryContent content = StoryLibrary.Get();
            StoryProgress story = StoryLibrary.Over(StoryProgress.NewState(content, 5));
            story.Win(content.Place("wo_scholar"));
            story.Win(content.Place("vizima_nobleman"));
            story.Win(content.Place("blackbough_merchant"));
            return story;
        }

        [Test]
        public void NoSaveIsNoProblem()
        {
            string problem;
            Assert.IsNull(StorySave.Load(out problem));
            Assert.IsNull(problem);
            Assert.IsFalse(StorySave.Exists);
        }

        [Test]
        public void ASavedStoryReadsBackTheSame()
        {
            StoryProgress story = Played();
            Assert.IsTrue(StorySave.Save(story.State));
            Assert.IsTrue(StorySave.Save(story.State), "saved over itself");
            string problem;
            StoryState state = StorySave.Load(out problem);
            Assert.IsNull(problem);
            StoryProgress loaded = StoryLibrary.Over(state);
            Assert.That(state.Cards, Is.EqualTo(story.State.Cards));
            Assert.That(state.Crowns, Is.EqualTo(story.State.Crowns));
            Assert.That(state.Drawn, Is.EqualTo(1));
            Assert.That(loaded.Wins("wo_scholar"), Is.EqualTo(1));
            Assert.IsTrue(loaded.IsOpen(loaded.Content.Region("velen")));
            Assert.That(Directory.GetFiles(folder), Has.Length.EqualTo(1), "no temp file left");
        }

        [TestCase("{ not json")]
        [TestCase("{}")]
        [TestCase("{\"Format\": 99, \"Cards\": [1, 2]}")]
        public void AnUnreadableSaveIsRefusedAndKept(string text)
        {
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "story.json");
            File.WriteAllText(file, text);
            string problem;
            Assert.IsNull(StorySave.Load(out problem));
            Assert.That(problem, Is.Not.Null);
            Assert.That(File.ReadAllText(file), Is.EqualTo(text), "the save is left as it is");
        }

        [Test]
        public void StartingOverSetsTheOldStoryAside()
        {
            Assert.IsTrue(StorySave.Save(Played().State));
            Directory.CreateDirectory(Path.Combine(folder, "Decks", "NR"));
            File.WriteAllText(Path.Combine(folder, "Decks", "NR", "starter_deck.json"), "{}");
            Assert.IsTrue(StorySave.SetAside());
            Assert.IsFalse(StorySave.Exists);
            Assert.IsFalse(Directory.Exists(Path.Combine(folder, "Decks")));
            string[] earlier = Directory.GetDirectories(Path.Combine(folder, "Earlier"));
            Assert.That(earlier, Has.Length.EqualTo(1));
            Assert.IsTrue(File.Exists(Path.Combine(earlier[0], "story.json")));
            Assert.IsTrue(File.Exists(Path.Combine(earlier[0], "Decks", "NR", "starter_deck.json")), "the story's decks go with it");
        }
    }
}
