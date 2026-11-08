using UnityEditor;
using UnityEngine;

namespace Swarm.EditorTools
{
    /// <summary>Атлас импортируется как пиксель-арт: Point, без мипов и сжатия, NPOT как есть.</summary>
    public sealed class SwarmTextureImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith("Assets/Swarm/Resources/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.filterMode = FilterMode.Point;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.alphaIsTransparency = true;
            ti.sRGBTexture = true;
            ti.isReadable = false;
            ti.maxTextureSize = 2048;
        }
    }
}
