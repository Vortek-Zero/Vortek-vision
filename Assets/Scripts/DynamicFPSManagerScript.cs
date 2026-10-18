using System.Collections;
using UnityEngine;

public class DynamicFPSManagerScript : MonoBehaviour
{
    [SerializeField] private int activeFPS = 35;
    [SerializeField] private int idleFPS = 15;
    [SerializeField] private float movementThreshold = 0.01f;
    [SerializeField] private float checkInterval = 0.2f; // Check 5 times per second, not 60!

    private Vector3 lastPosition;
    private bool isIdle = false;

    void Start()
    {
        lastPosition = transform.position;
        // Start the loop instead of using Update
        StartCoroutine(MovementCheckLoop());
    }

    IEnumerator MovementCheckLoop()
    {
        while (true) // Infinite loop that runs in the background
        {
            float moveDist = Vector3.Distance(transform.position, lastPosition);

            if (moveDist > movementThreshold)
            {
                if (isIdle) { SetFPS(activeFPS); isIdle = false; }
            }
            else
            {
                if (!isIdle) { SetFPS(idleFPS); isIdle = true; }
            }

            lastPosition = transform.position;

            // This is the "Magic": The script sleeps for 0.2 seconds 
            // and releases the CPU for other tasks.
            yield return new WaitForSecondsRealtime(checkInterval);
        }
    }

    void SetFPS(int target) => Application.targetFrameRate = target;
}