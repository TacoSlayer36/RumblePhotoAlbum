using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using MelonLoader;
using Newtonsoft.Json.Linq;
using RumbleModdingAPI;
using UnityEngine;
using Il2CppRUMBLE.Interactions.InteractionBase;

namespace RumblePhotoAlbum;

public static class BuildInfo
{
    public const string ModName = "RumblePhotoAlbum";
    public const string ModVersion = "1.2.2";
    public const string Description = "Decorate your environment with framed pictures";
    public const string Author = "Kalamart";
    public const string Company = "";
}
public partial class MainClass : MelonMod
{
    /**
    * <summary>
    * Structure of each element in the "album" field of the config file.
    * </summary>
    */
    public class PictureDataInternal
    {
        public string path;
        public Vector3 position;
        public Vector3 rotation;
        public float width = 0;
        public float height = 0;
        public float padding = defaultPadding;
        public float thickness = defaultThickness;
        public Color color = defaultColor;
        public bool alpha = false;
        public bool visible = true;
        public GameObject obj = null;
        public JToken jsonConfig = null;
    }

    private const float maxPictureSize = 5f; // Maximum size for a picture in the gym

    // variables
    protected static float defaultSize = 0.5f; // Default size of the frame (width or height depending on the orientation)
    protected static float defaultThickness = 0.01f; // Default thickness of the frame
    protected static float defaultPadding = 0.01f; // Default frame padding around the picture
    protected static Color defaultColor = new Color(0.48f, 0.80f, 0.76f); // Rumble gym green as default frame color
    protected static bool enableAlpha = false; // Whether to enable alpha transparency for all pictures
    protected static bool visibility = true; // Whether the pictures are visible in cameras
    protected static bool buttonsVisibility = true; // Whether the buttons are visible on top of the held picture
    protected static GameObject photoAlbum = null; // Parent object for all framed pictures
    protected static string currentScene = "";
    private static bool flatlandFound = false;

    private static List<PictureData> PicturesList = null;

    /**
    * <summary>
    * Log to console.
    * </summary>
    */
    private static void Log(string msg)
    {
        MelonLogger.Msg(msg);
    }
    /**
    * <summary>
    * Log to console but in yellow.
    * </summary>
    */
    private static void LogWarn(string msg)
    {
        MelonLogger.Warning(msg);
    }
    /**
    * <summary>
    * Log to console but in red.
    * </summary>
    */
    private static void LogError(string msg)
    {
        MelonLogger.Error(msg);
    }

    /**
     * <summary>
     * Called when the mod is loaded into the game
     * </summary>
     */
    public override void OnLateInitializeMelon()
    {
        EnsureUserDataFolders();
        Calls.onMapInitialized += OnMapInitialized;
        Calls.onMyModsGathered += CheckMods;
    }

    /**
    * <summary>
    * Check which mods the user has installed (for inter-mod compatibility).
    * </summary>
    */
    private void CheckMods()
    {
        flatlandFound = Calls.Mods.findOwnMod("FlatLand", "1.0.0", false);
        if (flatlandFound)
        {
            Log("FlatLand mod detected");
        }
    }

    /**
    * <summary>
    * Called when the full map is initialized, and RMAPI calls can be used safely.
    * </summary>
    */
    private void OnMapInitialized()
    {
        initializeInteractionObjects();
        MelonCoroutines.Start(LoadAlbum(currentScene));
    }

    /**
    * <summary>
    * Called when the scene has finished loading.
    * </summary>
    */
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        currentScene = sceneName;
        if (sceneName == "Loader")
        {
            InitModUI();
            return;
        }
        else if (sceneName == "Gym"  && flatlandFound)
        {
            MelonCoroutines.Start(ListenForFlatLandButton());
        }
    }

    /**
    * <summary>
    * Waits for all objects to be initialized, and starts listener for the FlatLand button.
    * </summary>
    */
    private static IEnumerator<WaitForSeconds> ListenForFlatLandButton()
    {
        yield return new WaitForSeconds(1);
        GameObject.Find("FlatLand/FlatLandButton/Button").
            GetComponent<InteractionButton>().
            onPressed.
            AddListener(new System.Action(() =>
        {
            MelonCoroutines.Start(OnFlatLandEntered());
        }));
        yield break;
    }

    /**
    * <summary>
    * Called when the FlatLand button is pressed.
    * Waits for a second to give time for FlatLand to load, then loads the album.
    * </summary>
    */
    private static IEnumerator<WaitForSeconds> OnFlatLandEntered()
    {
        Log($"Loading into FlatLand");
        yield return new WaitForSeconds(1);
        currentScene = "FlatLand";
        initializeInteractionObjects();
        MelonCoroutines.Start(LoadAlbum(currentScene));
        yield break;
    }

    /**
     * <summary>
     * Called 50 times per second, used for frequent updates.
     * </summary>
     */
    public override void OnFixedUpdate()
    {
        if (currentScene != "Loader")
        {
            try
            {
                ProcessGrabbing();
            }
            catch (System.Exception e)
            {
                LogError($"Error in OnFixedUpdate: {e.Message}");
            }
        }
    }

    /**
     * <summary>
     * Called on every frame, used for updates that need to be really smooth.
     * </summary>
     */
    public override void OnUpdate()
    {
        if (currentScene != "Loader")
        {
            try
            {
                UpdateResizingIfNeeded();
            }
            catch (System.Exception e)
            {
                LogError($"Error in OnUpdate: {e.Message}");
            }
        }
    }
}
