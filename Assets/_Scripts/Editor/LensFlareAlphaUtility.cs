using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class LensFlareAlphaUtility
{
    const string SourcePrefix = "LensFlareAlphaSource:";

    [MenuItem("Assets/Prepare Alpha For Additive Lens Flare", true)]
    static bool CanPrepare() => Selection.activeObject is LensFlareDataSRP;

    [MenuItem("Assets/Prepare Alpha For Additive Lens Flare")]
    static void PrepareSelected() => Prepare((LensFlareDataSRP)Selection.activeObject);

    public static void Prepare(LensFlareDataSRP data)
    {
        Undo.RecordObject(data, "Prepare lens flare alpha");
        for (var index = 0; index < data.elements.Length; index++)
        {
            var element = data.elements[index];
            if (element.flareType != SRPLensFlareType.Image || element.lensFlareTexture == null) continue;
            var sourcePath = AssetDatabase.GetAssetPath(element.lensFlareTexture);
            var importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("The flare image must be an imported texture.");
            if (importer.userData.StartsWith(SourcePrefix, StringComparison.Ordinal))
            {
                sourcePath = AssetDatabase.GUIDToAssetPath(importer.userData.Substring(SourcePrefix.Length));
                importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            }
            if (importer == null || !File.Exists(sourcePath))
                throw new InvalidOperationException("The original flare image could not be found.");
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, !importer.sRGBTexture);
            try
            {
                if (!ImageConversion.LoadImage(source, File.ReadAllBytes(sourcePath)))
                    throw new InvalidOperationException("Use a PNG or JPG as the flare source image.");
                var pixels = source.GetPixels();
                for (var pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
                {
                    var pixel = importer.sRGBTexture ? pixels[pixelIndex].linear : pixels[pixelIndex];
                    pixel *= pixels[pixelIndex].a;
                    if (importer.sRGBTexture) pixel = pixel.gamma;
                    pixel.a = 1f;
                    pixels[pixelIndex] = pixel;
                }
                source.SetPixels(pixels);
                source.Apply();
                var outputPath = Path.ChangeExtension(AssetDatabase.GetAssetPath(data), null) + "-Emission-" + index + ".png";
                File.WriteAllBytes(outputPath, source.EncodeToPNG());
                AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport);
                var outputImporter = (TextureImporter)AssetImporter.GetAtPath(outputPath);
                outputImporter.textureType = TextureImporterType.Default;
                outputImporter.sRGBTexture = importer.sRGBTexture;
                outputImporter.alphaSource = TextureImporterAlphaSource.None;
                outputImporter.alphaIsTransparency = false;
                outputImporter.wrapMode = TextureWrapMode.Clamp;
                outputImporter.mipmapEnabled = true;
                outputImporter.textureCompression = TextureImporterCompression.Uncompressed;
                outputImporter.maxTextureSize = importer.maxTextureSize;
                outputImporter.userData = SourcePrefix + AssetDatabase.AssetPathToGUID(sourcePath);
                foreach (var platform in new[] { "Standalone", "Android" })
                    outputImporter.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                    {
                        name = platform,
                        overridden = true,
                        maxTextureSize = importer.maxTextureSize,
                        format = TextureImporterFormat.RGBA32,
                        textureCompression = TextureImporterCompression.Uncompressed
                    });
                outputImporter.SaveAndReimport();
                element.lensFlareTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
                element.blendMode = SRPLensFlareBlendMode.Additive;
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
    }
}
