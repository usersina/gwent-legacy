using UnityEditor;
using UnityEngine;

namespace Gwent.EditorTools
{
    // How the cards' images import. They are kept at about twice the size the battle draws them at 1920x1080, sharp up
    // to 4K; Android and the web take the larger ones at about the size a 1080p screen draws them (their screens are no
    // larger, and every MB counts there): a cap per platform, applied as each platform builds. A size is compressed only
    // when both its sides are multiples of 4, on every platform: the sizes and caps are chosen so (a 380x592 image capped
    // at 512 would be 329 wide and ship uncompressed, five times larger; the content tests check it). No mipmaps: Unity
    // compresses a texture with mipmaps only when both its sides are powers of two (the cards' are not), and a card drawn
    // at half its size (the showcase, the board at 1080p) is filtered cleanly without them. A censored image ("<name>-censored"
    // beside its original, CensoredArt) imports as its original does, wherever it is (a sprite, not a sheet of them).
    public class CardTextureImport : AssetPostprocessor
    {
        // Each folder, the largest side Android and the web keep (0: as it is), and the size from which it applies
        public static readonly (string folder, int mobile, int above)[] Rules =
        {
            ("Assets/Content/CardArt/", 512, 600),    // the print art's 672x1048: 328x512 (the showcase draws 325x506);
                                                      // the cards still at 380x592 stay as they are
            ("Assets/Resources/Cards/List/", 128, 136), // the board's 168x256: 84x128 (a card on a row is 87x135);
                                                        // the ones still at 88x136 stay as they are
            ("Assets/Resources/Cards/Back/", 0, 0),   // 216x328, seven of them
            ("Assets/Resources/Cards/Ability/", 0, 0),// 60x60
            ("Assets/Resources/Cards/Row/", 0, 0),
            (StoryMaps, 2048, 2048),                  // the story's maps, 4096 on their longest side (tools/story-maps.py):
                                                      // exactly halved, as their sides are multiples of 8
        };

        // The story's maps: sprites (the map screen shows them in UI Toolkit), kept at 4096 on a computer (the default
        // would halve them)
        public const string StoryMaps = "Assets/Content/Story/Maps/";

        public static readonly string[] Mobile = { "Android", "WebGL" };

        // The largest side Android and the web keep of an image there, that size (0: as it is; -1: not a card's image)
        public static int MobileCap(string asset_path, int width, int height)
        {
            foreach ((string folder, int mobile, int above) in Rules)
                if (asset_path.StartsWith(folder))
                    return mobile > 0 && Mathf.Max(width, height) > Mathf.Max(above, mobile) ? mobile : 0;
            return -1;
        }

        // The size an image is imported at under that cap (Unity scales its longest side to the cap, rounding the other)
        public static Vector2Int Capped(int width, int height, int cap)
        {
            int longest = Mathf.Max(width, height);
            if (cap <= 0 || longest <= cap)
                return new Vector2Int(width, height);
            float scale = (float)cap / longest;
            return new Vector2Int(Mathf.RoundToInt(width * scale), Mathf.RoundToInt(height * scale));
        }

        private static readonly string[] Platforms = { "Standalone", "Android", "WebGL", "iOS" };

        // A censored image: its original's settings (its type, sprite and filtering, each platform's size and format)
        private void ImportAsOriginal(TextureImporter importer)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (!name.EndsWith(Gwent.Core.CensoredArt.Suffix))
                return;
            string original = assetPath.Substring(0, assetPath.Length - System.IO.Path.GetFileName(assetPath).Length)
                + name.Substring(0, name.Length - Gwent.Core.CensoredArt.Suffix.Length) + System.IO.Path.GetExtension(assetPath);
            if (!(AssetImporter.GetAtPath(original) is TextureImporter source))
                return;
            TextureImporterSettings settings = new TextureImporterSettings();
            source.ReadTextureSettings(settings);
            importer.SetTextureSettings(settings);
            importer.textureCompression = source.textureCompression;
            foreach (string platform in Platforms)
                importer.SetPlatformTextureSettings(source.GetPlatformTextureSettings(platform));
        }

        private void OnPreprocessTexture()
        {
            TextureImporter importer = (TextureImporter)assetImporter;
            ImportAsOriginal(importer);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            int cap = MobileCap(assetPath, width, height);
            if (cap < 0)
                return;
            importer.mipmapEnabled = false;
            importer.mipmapLimitGroupName = "";
            if (assetPath.StartsWith(StoryMaps))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.maxTextureSize = 4096;
                importer.wrapMode = TextureWrapMode.Clamp;
            }
            foreach (string platform in Mobile)
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = cap > 0;
                if (cap > 0)
                {
                    settings.maxTextureSize = cap;
                    settings.format = TextureImporterFormat.Automatic;
                    settings.textureCompression = importer.textureCompression;
                }
                importer.SetPlatformTextureSettings(settings);
            }
        }
    }
}
