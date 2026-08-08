using UnityEditor;
using UnityEngine;

public sealed class TriplanarTerrainShaderGUI : ShaderGUI
{
    private const string DownTex = "_DownTex";
    private const string UseDownTexture = "_UseDownTexture";

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        base.OnGUI(materialEditor, properties);

        MaterialProperty downTexture = FindProperty(DownTex, properties, false);
        MaterialProperty useDownTexture = FindProperty(UseDownTexture, properties, false);

        if (downTexture == null || useDownTexture == null)
        {
            return;
        }

        float shouldUseDownTexture = downTexture.textureValue != null ? 1f : 0f;

        if (!Mathf.Approximately(useDownTexture.floatValue, shouldUseDownTexture))
        {
            useDownTexture.floatValue = shouldUseDownTexture;
        }
    }
}
