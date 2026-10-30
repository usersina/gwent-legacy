using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Gwent.EditorTools
{
    // The content the game loads on demand, through Addressables: each card's full art, each music track and each of the
    // story's maps, an asset bundle apiece. A build's data holds none of it; the bundles sit next to it (StreamingAssets/aa) and a card's art is
    // loaded the first time it shows (on the web, downloaded then, and cached by the browser). Everything small and needed
    // at once (the card thumbnails, backs, icons, sound effects, decks) stays in Resources.
    //
    // The groups and their entries are made from the folders below, by Update: menu Gwent > Content > Update
    // Addressables, and every build runs it first, so a file added to a folder is never left out. The Addressables
    // content itself is built with each player build (BuildWithPlayer).
    public static class GwentContent
    {
        public const string CardArtFolder = "Assets/Content/CardArt";
        public const string MusicFolder = "Assets/Content/Music";
        public const string StoryMapsFolder = "Assets/Content/Story/Maps";

        public const string CardArtGroup = "Card Art";
        public const string MusicGroup = "Music";
        public const string StoryMapsGroup = "Story Maps";
        public const string MusicLabel = "music"; // the runtime finds the tracks by it (GameAudio)

        [MenuItem("Gwent/Content/Update Addressables")]
        public static void Update()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            // A battle asks for both decks' art at once: on the web, a few more downloads side by side
            settings.MaxConcurrentWebRequests = 6;
            // The state a content update of a released build starts from: the game ships its content with the build and
            // never updates it apart, so the file stays out of the project (it would be one more per platform)
            settings.ContentStateBuildPath = "Library/com.unity.addressables/ContentState";
            settings.AddLabel(MusicLabel, false);

            int changes = Fill(settings, Group(settings, CardArtGroup), CardArtFolder, "CardArt/", null)
                + Fill(settings, Group(settings, MusicGroup), MusicFolder, "Music/", MusicLabel)
                + Fill(settings, Group(settings, StoryMapsGroup), StoryMapsFolder, "StoryMaps/", null);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
            if (changes > 0)
                Debug.Log("Addressables: " + changes + " entries added, moved or renamed");
        }

        // A group of local bundles, one per asset (a card's art loads without the others)
        private static AddressableAssetGroup Group(AddressableAssetSettings settings, string name)
        {
            AddressableAssetGroup group = settings.FindGroup(name)
                ?? settings.CreateGroup(name, false, false, false, null, typeof(BundledAssetGroupSchema));
            BundledAssetGroupSchema schema = group.GetSchema<BundledAssetGroupSchema>() ?? group.AddSchema<BundledAssetGroupSchema>(false);
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            // LZ4: loads fast and, on the web, without the long main-thread decompression LZMA would need
            schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
            schema.IncludeInBuild = true;
            schema.IncludeAddressInCatalog = true;
            schema.IncludeLabelsInCatalog = true;
            schema.IncludeGUIDInCatalog = false; // loaded by address or label only: a smaller catalog
            return group;
        }

        // Every asset of the folder in the group, at "<prefix><file name without extension>"; entries whose file is gone
        // leave the group. Returns how many entries changed.
        private static int Fill(AddressableAssetSettings settings, AddressableAssetGroup group, string folder, string prefix, string label)
        {
            int changes = 0;
            HashSet<string> present = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path))
                    continue;
                present.Add(guid);
                string address = prefix + Path.GetFileNameWithoutExtension(path);
                AddressableAssetEntry entry = settings.FindAssetEntry(guid);
                if (entry == null || entry.parentGroup != group || entry.address != address)
                {
                    entry = settings.CreateOrMoveEntry(guid, group, false, false);
                    entry.address = address;
                    changes++;
                }
                if (label != null)
                    entry.SetLabel(label, true, false, false);
            }
            foreach (AddressableAssetEntry stale in group.entries.Where(e => !present.Contains(e.guid)).ToList())
            {
                settings.RemoveAssetEntry(stale.guid, false);
                changes++;
            }
            return changes;
        }
    }
}
