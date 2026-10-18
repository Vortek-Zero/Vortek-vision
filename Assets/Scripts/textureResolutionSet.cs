using UnityEngine;

public class textureResolutionSet : MonoBehaviour
{
    // Reference your Render Texture and Video Player
    public RenderTexture streamTexture;

    void Start()
    {
        // Release the texture from memory so it can be modified
        streamTexture.Release(); 
    
        // Set your desired resolution (e.g., 2560 x 1440)
        // streamTexture.width = 2560; 
        // streamTexture.height = 1440;
        streamTexture.width = 1920; 
        streamTexture.height = 1080;
        // streamTexture.width = 1280; 
        // streamTexture.height = 720;
        
        // Create the texture with new dimensions and play
        streamTexture.Create();
    }
}
