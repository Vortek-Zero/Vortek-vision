using UnityEngine;
using UnityEngine.SceneManagement;

public class SwitchBtwScenesScript : MonoBehaviour
{
    // [SerializeField] 
    // [Tooltip("Button List")]
    // public Button exitButton;

    // public GameObject wsSettingPanel;
    // public GameObject showWSSettingPanelButton;

    public GameObject urlUpdatePanel;



    [Tooltip("Target Height for the application")]

    // In Awake, we set the screen to never sleep and set a target framerate for smoother performance, 
    // especially important for AR applications.
    void Awake()
    {
        // This prevents the screen from dimming or going to sleep
        // Screen.sleepTimeout = SleepTimeout.NeverSleep;


        // disable vsync to allow the application to run at the target frame rate
        // QualitySettings.vSyncCount = 0;

        // target framerate is set to 60 for smoother performance, especially important for AR applications
        // Application.targetFrameRate = 24; // You can adjust this value based on your performance needs and device capabilities
    }

    void Start()
    {
        // wsSettingPanel.SetActive(false);
        urlUpdatePanel.SetActive(false);
    }

    
    // void OnEnable()
    // {
    //     if(exitButton != null)
    //     {
    //         exitButton.onClick.AddListener(ExitApplication);
    //     }
    // }

    // void OnDisable()
    // {
    //     if(exitButton != null)
    //     {
    //         exitButton.onClick.RemoveListener(ExitApplication);
    //     }
    // }

    public void ExitApplication()
    {
        Application.Quit();
        // SceneManager.LoadScene("ARMetaverseScene"); 
    }

    // public void ShowWSSettingPanelButton()
    // {
    //     wsSettingPanel.SetActive(true);  // Show the WebSocket settings panel
    //     exitButton.interactable = false; // Disable the exit button while the settings panel is active
    //     showWSSettingPanelButton.SetActive(false); // Hide the button that shows the settings panel
    // }

    // public void CloseWSSettingPanel()
    // {
    //     wsSettingPanel.SetActive(false); // Hide the WebSocket settings panel
    //     exitButton.interactable = true;  // Re-enable the exit button
    //     showWSSettingPanelButton.SetActive(true); // Show the button that shows the settings panel
    // }

    public void ShowURLUpdatePanel()
    {
        urlUpdatePanel.SetActive(true); 
    }
    public void CloseURLUpdatePanel()
    {
        urlUpdatePanel.SetActive(false); 
    }

}
