using System.IO;
using UnityEngine;

namespace MemPalaceLLM
{
    public static class WordImageCatalog
    {
        private const string ResourceRoot = "WordImages";
        private const string PlaceholderResourcePath = ResourceRoot + "/_placeholder";
        public const string PlaceholderDisplayPath = "Assets/Resources/WordImages/_placeholder.png";

        public static string BuildResourcePath(string word)
        {
            var key = SanitizeResourcePart(word);
            return string.IsNullOrWhiteSpace(key) ? PlaceholderResourcePath : ResourceRoot + "/" + key;
        }

        public static bool TryLoadWordImage(string word, out Texture2D texture, out string resourcePath, out string filePath, out bool usedPlaceholder)
        {
            texture = null;
            resourcePath = BuildResourcePath(word);
            filePath = string.Empty;
            usedPlaceholder = false;

            var source = Resources.Load<Texture2D>(resourcePath);
            if (source == null)
            {
                resourcePath = PlaceholderResourcePath;
                source = Resources.Load<Texture2D>(resourcePath);
                usedPlaceholder = true;
                if (source == null)
                {
                    return false;
                }
            }

            texture = CreateReadableCopy(source);
            filePath = "Resources/" + resourcePath;
            return texture != null;
        }

        public static Texture2D CreateBlankPlaceholder()
        {
            const int width = 512;
            const int height = 384;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D CreateReadableCopy(Texture2D source)
        {
            var previous = RenderTexture.active;
            var renderTexture = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);

            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
            return copy;
        }

        private static string SanitizeResourcePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var result = value.Trim().ToLowerInvariant();
            for (int i = 0; i < invalid.Length; i++)
            {
                result = result.Replace(invalid[i], '_');
            }

            return result.Replace(' ', '_');
        }
    }
}
