using UnityEngine;

/// <summary>
/// Static utility class for creating colored primitive GameObjects and materials
/// at runtime with no imported assets. Uses a shader fallback chain that works
/// across URP, Built-in Render Pipeline, and any pipeline with Unlit/Color.
/// </summary>
public static class Placeholders
{
    /// <summary>
    /// Creates a primitive GameObject with a solid-color material.
    /// </summary>
    /// <param name="type">The type of primitive to create (Cube, Capsule, Plane, etc.).</param>
    /// <param name="color">The solid color to apply to the material.</param>
    /// <param name="name">Optional name for the GameObject. If null, defaults to the PrimitiveType name.</param>
    /// <returns>The newly created GameObject with the colored material applied.</returns>
    public static GameObject CreatePrimitive(PrimitiveType type, Color color, string name = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name ?? type.ToString();

        Material mat = CreateMaterial(color);
        if (mat != null)
        {
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material = mat;
            }
        }
        else
        {
            Debug.LogWarning(
                $"Placeholders.CreatePrimitive: CreateMaterial returned null for color {color}. " +
                $"Using default material for {go.name}.");
        }

        return go;
    }

    /// <summary>
    /// Creates a simple solid-color Material with the given color.
    /// Uses a shader fallback chain: Universal Render Pipeline/Lit → Standard → Unlit/Color.
    /// In Play mode, the material is marked with HideFlags.HideAndDontSave to prevent leaks.
    /// </summary>
    /// <param name="color">The solid color for the material.</param>
    /// <returns>A new Material with the given color, or null if no shader was found.</returns>
    public static Material CreateMaterial(Color color)
    {
        Shader shader = FindUsableShader();
        if (shader == null)
        {
            Debug.LogError("Placeholders.CreateMaterial: No usable shader found " +
                           "(tried URP Lit, Standard, and Unlit/Color).");
            return null;
        }

        Material material = new Material(shader);

        // Set the color property based on the shader that was found
        // URP/HDRP use _BaseColor, Built-in uses _Color
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        // In Play mode, prevent materials from leaking
        if (Application.isPlaying)
        {
            material.hideFlags = HideFlags.HideAndDontSave;
        }

        return material;
    }

    /// <summary>
    /// Attempts to find a usable shader using the fallback chain:
    /// 1. Universal Render Pipeline/Lit (URP/HDRP)
    /// 2. Standard (Built-in Render Pipeline)
    /// 3. Unlit/Color (fallback — guaranteed in all pipelines)
    /// </summary>
    /// <returns>A Shader reference, or null if all shaders failed.</returns>
    private static Shader FindUsableShader()
    {
        // Try URP Lit shader first (Unity 6 default)
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null && shader.isSupported)
            return shader;

        // Fall back to Built-in Standard
        shader = Shader.Find("Standard");
        if (shader != null && shader.isSupported)
            return shader;

        // Final fallback: Unlit/Color — available in all pipelines
        shader = Shader.Find("Unlit/Color");
        if (shader != null && shader.isSupported)
            return shader;

        return null;
    }
}
