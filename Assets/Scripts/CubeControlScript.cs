using UnityEngine;

public class CubeControl : MonoBehaviour
{
    PlayerControls controls;
    [SerializeField] private float moveSpeed = 5f;       // Amount to move the cube by
    [SerializeField] private float rotationSpeed = 100f; // Amount to rotate the cube by

    void Awake()
    {
        controls = new PlayerControls(); // Create an instance of the PlayerControls class

        controls.GamePlay.Grow.performed   += ctx => Grow();    // press G to grow the cube
        controls.GamePlay.Shrink.performed += ctx => Shrink();  // press H to shrink the cube

        // controls.GamePlay.Move.performed += ctx => HandleMovement();
        // controls.GamePlay.Move.canceled  += ctx => HandleMovement();
    }

    void OnEnable()
    {
        controls.GamePlay.Enable(); // Enable the GamePlay action map
    }

    void OnDisable()
    {
        controls.GamePlay.Disable(); // Disable the GamePlay action map
    }

    // Update is called once per frame
    void Update()
    {
        HandleMovement(); // Handle movement input AWSD
        HandleRotation(); // Handle rotation input JIKL
    }

    void HandleMovement()
    {
        Vector2 moveInput = controls.GamePlay.Move.ReadValue<Vector2>(); // Read the movement input from the PlayerControls
        Vector3 move = new Vector3(moveInput.x, 0, moveInput.y) * moveSpeed * Time.deltaTime; // Convert the input to a movement vector
        transform.Translate(move, Space.World); // Move the cube by the movement vector
    }

    void HandleRotation()
    {
        Vector2 rotateInput = controls.GamePlay.Rotate.ReadValue<Vector2>(); // Read the rotation input from the PlayerControls
        Vector3 rotation = new Vector3(rotateInput.y, rotateInput.x, 0) * rotationSpeed * Time.deltaTime; // Convert the input to a rotation vector
        transform.Rotate(rotation, Space.World); // Rotate the cube by the rotation vector
    }

    void Grow()
    {
        transform.localScale *= 1.1f; // Increase the scale of the cube by 10%
    }

    void Shrink()
    {
        transform.localScale *= 0.9f; // Decrease the scale of the cube by 10%
    }


}
