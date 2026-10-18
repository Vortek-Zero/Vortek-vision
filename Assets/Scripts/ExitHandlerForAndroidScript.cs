using UnityEngine;
using UnityEngine.InputSystem; // Required for New Input System
using UnityEngine.SceneManagement;

public class ExitHandlerForAndroidScript : MonoBehaviour
{
    private InputAction _backButtonAction;
    // public Button ipButton;
    public GameObject urlUpdatePanel;
    // button to reset the scene 
    // public Button resetSceneButton;


    void Awake()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep; // Prevent the screen from sleeping
    }

    private void OnEnable()
    {
        // 1. Initialize the action for the Escape key (Android Back button)
        _backButtonAction = new InputAction("Back", binding: "<Keyboard>/escape");

        // 2. Subscribe to the 'performed' event
        _backButtonAction.performed += OnBackButtonPressed;

        // 3. Enable the action
        _backButtonAction.Enable();
    }

    private void OnDisable()
    {
        // Unsubscribe and disable to prevent memory leaks
        _backButtonAction.performed -= OnBackButtonPressed;
        _backButtonAction.Disable();
    }

    private void OnBackButtonPressed(InputAction.CallbackContext context)
    {
        Debug.Log("Back button pressed! Exiting app...");
        Application.Quit();
    }

    public void IpButtonShowHide()
    {
        if(urlUpdatePanel.activeSelf)
        {
            // ipButton.gameObject.SetActive(true);
            urlUpdatePanel.SetActive(false);
        }
        else
        {
            // ipButton.gameObject.SetActive(false);
            urlUpdatePanel.SetActive(true);
        }
    }

    public void ResetApp()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}