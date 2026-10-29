using System.Collections.Generic;
using System.Linq;
using Gwent.Core;
using Gwent.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Gwent.Content.Tests
{
    // The content the game loads through Addressables (see GwentContent) is all there, at the addresses the game asks for
    public class AddressablesContentTests
    {
        private static List<AddressableAssetEntry> Entries(string group_name)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            Assert.IsNotNull(settings, "No Addressables settings (menu Gwent > Content > Update Addressables makes them)");
            AddressableAssetGroup group = settings.FindGroup(group_name);
            Assert.IsNotNull(group, "No \"" + group_name + "\" group (menu Gwent > Content > Update Addressables makes it)");
            return group.entries.ToList();
        }

        [Test]
        public void EveryCardHasItsArt()
        {
            Dictionary<string, AddressableAssetEntry> art = Entries(GwentContent.CardArtGroup).ToDictionary(entry => entry.address);
            List<string> missing = CardLibrary.Get().All.Select(card => CardArt.Address(card._id)).Where(address => !art.ContainsKey(address)).ToList();
            Assert.IsEmpty(missing, "Cards with no art in Addressables");
            foreach (AddressableAssetEntry entry in art.Values)
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(entry.AssetPath), entry.address + " (" + entry.AssetPath + ") is not a sprite");
        }

        [Test]
        public void EveryTrackIsLabelledMusic()
        {
            List<AddressableAssetEntry> tracks = Entries(GwentContent.MusicGroup);
            Assert.IsNotEmpty(tracks);
            foreach (AddressableAssetEntry track in tracks)
            {
                Assert.IsTrue(track.labels.Contains(GwentContent.MusicLabel), track.address + " has no \"" + GwentContent.MusicLabel + "\" label: it would never play");
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AudioClip>(track.AssetPath), track.address + " is not an audio clip");
            }
        }

        // A file added to a folder is in its group (every build updates the groups; this catches it before)
        [Test]
        public void TheGroupsHoldTheirWholeFolders()
        {
            foreach (KeyValuePair<string, string> pair in new Dictionary<string, string>
            {
                { GwentContent.CardArtFolder, GwentContent.CardArtGroup },
                { GwentContent.MusicFolder, GwentContent.MusicGroup },
                { GwentContent.StoryMapsFolder, GwentContent.StoryMapsGroup },
            })
            {
                HashSet<string> entries = new HashSet<string>(Entries(pair.Value).Select(entry => entry.guid));
                List<string> left_out = AssetDatabase.FindAssets("", new[] { pair.Key })
                    .Where(guid => !AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(guid)) && !entries.Contains(guid))
                    .Select(AssetDatabase.GUIDToAssetPath).ToList();
                Assert.IsEmpty(left_out, "Not in the \"" + pair.Value + "\" group (menu Gwent > Content > Update Addressables)");
            }
        }

        // The cards' images compress on every platform: their size there (Android's and the web's capped,
        // CardTextureImport) has both sides multiples of 4, else it would ship uncompressed, several times larger
        [Test]
        public void EveryCardImageCompressesOnEveryPlatform()
        {
            List<string> wrong = new List<string>();
            int seen = 0;
            foreach ((string folder, int _, int _) in CardTextureImport.Rules)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder.TrimEnd('/') }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                    int cap = CardTextureImport.MobileCap(path, width, height);
                    seen++;
                    if (width % 4 != 0 || height % 4 != 0)
                        wrong.Add(path + ": " + width + "x" + height);
                    foreach (string platform in CardTextureImport.Mobile)
                    {
                        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                        if (settings.overridden != cap > 0 || cap > 0 && settings.maxTextureSize != cap)
                            wrong.Add(path + ": " + platform + "'s cap is not the rule's (" + cap + "): reimport it");
                        Vector2Int there = CardTextureImport.Capped(width, height, cap);
                        if (there.x % 4 != 0 || there.y % 4 != 0)
                            wrong.Add(path + ": " + there.x + "x" + there.y + " on " + platform);
                    }
                }
            }
            Assert.Greater(seen, 300, "The cards' images were not found");
            Assert.IsEmpty(wrong, "Card images that would ship uncompressed");
        }

        // A censored image (CensoredArt) stands in for its original: beside it, the same size, imported the same way (a single
        // sprite: the game loads it as one)
        [Test]
        public void EveryCensoredImageMatchesItsOriginal()
        {
            List<string> wrong = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D " + CensoredArt.Suffix, new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!name.EndsWith(CensoredArt.Suffix))
                    continue;
                string original = path.Replace(CensoredArt.Suffix + System.IO.Path.GetExtension(path), System.IO.Path.GetExtension(path));
                TextureImporter mine = (TextureImporter)AssetImporter.GetAtPath(path);
                TextureImporter theirs = AssetImporter.GetAtPath(original) as TextureImporter;
                if (theirs == null)
                {
                    wrong.Add(path + ": no original (" + original + ")");
                    continue;
                }
                mine.GetSourceTextureWidthAndHeight(out int w, out int h);
                theirs.GetSourceTextureWidthAndHeight(out int ow, out int oh);
                if (w != ow || h != oh)
                    wrong.Add(path + ": " + w + "x" + h + ", its original " + ow + "x" + oh);
                if (mine.textureType != theirs.textureType || mine.spriteImportMode != theirs.spriteImportMode)
                    wrong.Add(path + ": not imported as its original (reimport it)");
            }
            Assert.IsEmpty(wrong);
        }

        // An asset both in Resources and in a bundle would ship twice
        [Test]
        public void NothingAddressableIsInResources()
        {
            List<string> twice = AddressableAssetSettingsDefaultObject.Settings.groups
                .SelectMany(group => group.entries).Select(entry => entry.AssetPath)
                .Where(path => path.Contains("/Resources/")).ToList();
            Assert.IsEmpty(twice);
        }
    }
}
