using UnityEngine;
using UnityEngine.UI;
using Unity.RenderStreaming;

public class AndroidDisplayHandler : MonoBehaviour
{
    public VideoStreamReceiver videoReceiver;
    public RawImage fullScreenDisplay;

    void Start()
    {
        if (videoReceiver != null)
        {
            videoReceiver.OnUpdateReceiveTexture += (Texture tex) => {
                if (fullScreenDisplay != null && tex != null)
                {
                    fullScreenDisplay.texture = tex;
                }
            };
        }
    }
}