using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Recording.LCK;
using Il2CppRUMBLE.Serialization;
using Il2CppRUMBLE.Tutorial.MoveLearning;
using Il2CppRUMBLE.Utilities;
using Il2CppTMPro;
using MelonLoader;
using Newtonsoft.Json.Linq;
using RumbleModdingAPI;
using UnityEngine;
using Newtonsoft.Json;

namespace RumblePhotoAlbum;
public partial class MainClass : MelonMod
{
    private static GameObject AlbumInteractionItems = null;
    private static GameObject friendButton = null;
    private static GameObject gearMarketButton = null;
    private static GameObject mailTubeObj = null;
    private static GameObject rockCamButton = null;
    private static Transform rockCamTf = null;
    private static GameObject rockCamHandle = null;
    private static PictureData rockCamPicture = null;
    private static bool rockCamInitialized = false;
    private static int PhotoPrintingIndex = 0;
    private static MailTube mailTube = null;
    private static GameObject mailTubeHandle = null;
    private static PictureData mailTubePicture = null;
    private static Transform purchaseSlab = null;
    private static bool animationRunning = false;

    private static JArray stashJson;
    private static JArray albumJson;

    private const float mailTubeScale = 0.505f;

    /**
    * <summary>
    * Initializes the buttons and various interactables.
    * </summary>
    */
    private static void initializeInteractionObjects()
    {
        rockCamInitialized = false;
        if (currentScene == "Loader")
        {
            return;
        }
        if (currentScene == "Gym")
        {
            if (AlbumInteractionItems is null)
            {
                initializeGlobals();
            }
            initializeGymObjects();
        }
        else if (currentScene == "Park")
        {
            initializeParkObjects();
        }
        else if (currentScene == "FlatLand")
        {
            initializeFlatLandObjects();
        }
        initializeRockCam();
    }

    /**
    * <summary>
    * Initializes the objects that are to be saved to DontDestroyOnLoad (used in multiple scenes).
    * </summary>
    */
    private static void initializeGlobals()
    {
        AlbumInteractionItems = new GameObject();
        AlbumInteractionItems.name = "AlbumInteractionItems";
        GameObject.DontDestroyOnLoad(AlbumInteractionItems);

        friendButton = GameObject.Instantiate(Calls.GameObjects.Gym.LOGIC.Heinhouserproducts
            .Telephone20REDUXspecialedition
            .SettingsScreen
            .InteractionButton1
            .Button
            .GetGameObject());
        friendButton.name = "friendButton";
        friendButton.SetActive(false);
        friendButton.GetComponent<InteractionButton>().enabled = true;
        friendButton.transform.GetChild(4).gameObject.SetActive(false);
        GameObject buttonText = Calls.Create.NewText();
        TextMeshPro textComponent = buttonText.GetComponent<TextMeshPro>();
        textComponent.alignment = TextAlignmentOptions.Center;
        Color textColor = new Color(1f, 0.98f, 0.75f); // very light slightly orangy yellow
        textComponent.color = textColor;
        textComponent.colorGradient = new VertexGradient(textColor);
        textComponent.fontSize = 0.4f;
        textComponent.name = "text";
        buttonText.transform.SetParent(friendButton.transform, false);
        buttonText.transform.localPosition = new Vector3(0, 0.015f, 0);
        buttonText.transform.localRotation = Quaternion.Euler(new Vector3(90, 180, 0));
        friendButton.transform.SetParent(AlbumInteractionItems.transform);

        // get Gear Market large button
        gearMarketButton = GameObject.Instantiate(Calls.GameObjects.Gym.LOGIC.Heinhouserproducts
            .Gearmarket
            .Messagescreen
            .OneButtonLayout
            .GetGameObject());
        gearMarketButton.name = "gearMarketButton";
        gearMarketButton.SetActive(false);
        gearMarketButton.transform.GetChild(0).gameObject.GetComponent<InteractionTouch>().enabled = true;
        gearMarketButton.transform.SetParent(AlbumInteractionItems.transform);

        //Get the mail tube object in the gym
        mailTubeObj = GameObject.Instantiate(Calls.GameObjects.Gym.LOGIC.Heinhouserproducts
            .Gearmarket
            .MailTube
            .GetGameObject());
        mailTubeObj.name = "mailTube";
        mailTubeObj.SetActive(false);
        mailTubeObj.transform.SetParent(AlbumInteractionItems.transform);

        // get Rock Cam "flip camera" button
        rockCamTf = Calls.Players.GetPlayerController().gameObject.transform.GetChild(10).GetChild(2);
        rockCamButton = GameObject.Instantiate(rockCamTf.GetChild(2).GetChild(0).GetChild(1).GetChild(4).GetChild(0).gameObject);
        rockCamButton.name = "rockCamButton";
        rockCamButton.SetActive(false);
        rockCamButton.transform.SetParent(AlbumInteractionItems.transform);
    }

    /**
    * <summary>
    * Initializes the Rock Cam print button and handle to print to.
    * </summary>
    */
    private static void initializeRockCam()
    {
        try
        {
            if (rockCamInitialized || rockCamButton is null || gearMarketButton is null)
            {
                return;
            }
            var playerController = Calls.Players.GetPlayerController();
            if (playerController is null)
            {
                return;
            }
            rockCamPicture = null;
            rockCamTf = playerController.gameObject.transform.GetChild(10).GetChild(2);

            // add a "Print photo" button to the top edge of Rock Cam
            System.Action action = () => PrintPhoto();
            GameObject printButton = NewRockCamButton("printButton", "Print photo", action);
            printButton.transform.SetParent(rockCamTf.GetChild(2).GetChild(0), true);
            printButton.transform.localPosition = new Vector3(-0.08f, 0.034f, 0.143f);
            printButton.transform.localRotation = Quaternion.Euler(new Vector3(90f, 0, 0));

            // Create an inclined handle to attach "printed" pictures to (above Rock Cam)
            rockCamHandle = new GameObject();
            rockCamHandle.name = "printHandle";
            rockCamHandle.transform.localScale = Vector3.one;
            rockCamHandle.transform.SetParent(rockCamTf.GetChild(2).GetChild(0), true);
            rockCamHandle.transform.localPosition = new Vector3(0, 0.079f, 0.22f);
            rockCamHandle.transform.localRotation = Quaternion.Euler(new Vector3(-50, 180, 0));

            // If the print button is pressed, it will print the most recent photo
            PhotoPrintingIndex = 0;
            rockCamInitialized = true;
        }
        catch (Exception e)
        {
            rockCamInitialized = false;
        }
    }

    /**
    * <summary>
    * Initializes the objects that are specific to the Gym scene.
    * </summary>
    */
    private static void initializeGymObjects()
    {
        //Get the mail tube object in the gym
        mailTube = Calls.GameObjects.Gym.LOGIC.Heinhouserproducts.Gearmarket.MailTube.GetGameObject().GetComponent<MailTube>();

        // Create a new button on the gear market for spawning pictures
        System.Action action = () => SpawnPicture();
        GameObject spawnButton = NewGearMarketButton("spawnButton", "Spawn picture", action);
        GameObject gearMarket = Calls.GameObjects.Gym.LOGIC.Heinhouserproducts.Gearmarket.GetGameObject();
        spawnButton.transform.SetParent(gearMarket.transform);
        spawnButton.transform.localPosition = new Vector3(0.075f, 1.1f, 0.19f);
        spawnButton.transform.localRotation = Quaternion.Euler(new Vector3(270, 270, 0));

        initializeMailTubeObjects();
    }

    /**
    * <summary>
    * Removes a picture from the scene, but does not delete the corresponding image file.
    * </summary>
    */
    private static void stashPicture(PictureData pictureData)
    {
        if (pictureData.jsonConfig is not null)
        {
            pictureData.jsonConfig.Remove();
        }
        GameObject.Destroy(pictureData.obj);
        for (int i = 0; i < 2; i++)
        {
            if (currentlyModified[i] == pictureData)
            {
                currentlyModified[i] = null;
            }
        }
        stashJson.Add(pictureData.path);
        PicturesList.Remove(pictureData);
        File.WriteAllText(fullPath, root.ToString(Formatting.Indented));
        Log($"Stashed a picture");
    }

    /**
    * <summary>
    * Toggles the visibility of a picture to LIV and legacy camera.
    * </summary>
    */
    private static void togglePictureVisibility(PictureData pictureData)
    {
        pictureData.visible = !pictureData.visible;
        int pictureLayer = pictureData.visible ?
            LayerMask.NameToLayer("UI") // No collision, visible
            : LayerMask.NameToLayer("PlayerFade"); // No collision, invisible
        pictureData.obj.transform.GetChild(0).gameObject.layer = pictureLayer;
        pictureData.obj.transform.GetChild(1).gameObject.layer = pictureLayer;
        Transform visibilityButton = pictureData.obj.transform.GetChild(0).GetChild(0).GetChild(1);
        TextMeshPro buttonText = visibilityButton.GetChild(6).gameObject.GetComponent<TextMeshPro>();
        buttonText.SetText(pictureData.visible ? "Hide" : "Show");
        Log($"Made a picture {(pictureData.visible ? "visible" : "invisible")} to all cameras");
    }

    /**
    * <summary>
    * Removes a picture from the scene, and deletes the corresponding image file
    * if it's in the "pictures" folder and not referenced elsewhere.
    * </summary>
    */
    protected static void deletePicture(PictureData pictureData, bool keepFile)
    {
        if (pictureData.jsonConfig is not null)
        {
            pictureData.jsonConfig.Remove();
            File.WriteAllText(fullPath, root.ToString(Formatting.Indented));
        }
        GameObject.Destroy(pictureData.obj);
        for (int i = 0; i < 2; i++)
        {
            if (currentlyModified[i] == pictureData)
            {
                currentlyModified[i] = null;
            }
        }
        PicturesList.Remove(pictureData);
        if (File.Exists(pictureData.path) || keepFile)
        {
            return;
        }
        string globalPicturePath = Path.Combine(Application.dataPath, "..", UserDataPath, picturesFolder, pictureData.path);
        if (File.Exists(globalPicturePath))
        {
            bool usedElsewhere = false;
            foreach (var scene in root)
            {
                JArray album = (JArray)scene.Value["album"];
                foreach (var entry in album)
                {
                    if (entry.Value<string>("path") == pictureData.path)
                    {
                        usedElsewhere = true;
                        break;
                    }
                }
                if (usedElsewhere)
                {
                    break;
                }
            }
            if (usedElsewhere)
            {
                Log($"File not deleted because it's used elsewhere: {pictureData.path}");
            }
            else
            {
                File.Delete(globalPicturePath);
                Log($"Deleted file: {pictureData.path}");
            }
        }
    }

    /**
    * <summary>
    * Initializes the objects that are specific to the Park scene.
    * </summary>
    */
    private static void initializeParkObjects()
    {
        //Copy the mail tube object that comes from the gym
        mailTube = NewMailTube().GetComponent<MailTube>();
        mailTube.gameObject.name = "mailTube";
        mailTube.transform.position = new Vector3(-13.3f, -5.88f, 4.71f);
        mailTube.transform.rotation = Quaternion.Euler(new Vector3(0, 180, 0));

        // Create a new button on the gear market for spawning pictures
        System.Action action = () => SpawnPicture();
        GameObject spawnButton = NewGearMarketButton("spawnButton", "Spawn picture", action);
        spawnButton.transform.position = new Vector3(-13.19f, -4.68f, 5.42f);
        spawnButton.transform.rotation = Quaternion.Euler(new Vector3(-90, 30, 0));

        initializeMailTubeObjects();
    }

    /**
    * <summary>
    * Initializes the objects that are specific to the FlatLand scene.
    * </summary>
    */
    private static void initializeFlatLandObjects()
    {
        //Copy the mail tube object that comes from the gym
        mailTube = NewMailTube().GetComponent<MailTube>();
        mailTube.gameObject.name = "mailTube";
        mailTube.transform.position = new Vector3(4.3f, 0f, - 4f);
        mailTube.transform.rotation = Quaternion.Euler(new Vector3(0, 70, 0));

        // Create a new button on the gear market for spawning pictures
        System.Action action = () => SpawnPicture();
        GameObject spawnButton = NewGearMarketButton("spawnButton", "Spawn picture", action);
        spawnButton.transform.position = new Vector3(3.8f, 1.1f, - 4.12f);
        spawnButton.transform.rotation = Quaternion.Euler(new Vector3(-90, -92, 0));

        initializeMailTubeObjects();
    }

    /**
    * <summary>
    * Initializes all the objects that are needed for the mail tube to work for delivering pictures.
    * </summary>
    */
    private static void initializeMailTubeObjects()
    {
        // reset state variables
        animationRunning = false;
        mailTubePicture = null;

        // Initialize transforms for positioning the picture during the animation
        purchaseSlab = mailTube.gameObject.transform.GetChild(5);
        mailTubeHandle = new GameObject();
        mailTubeHandle.name = "mailTubeHandle";
        mailTubeHandle.transform.localScale = mailTubeScale * Vector3.one;
        mailTubeHandle.transform.SetParent(purchaseSlab, true);
        mailTubeHandle.transform.localPosition = Vector3.zero;
        mailTubeHandle.transform.localRotation = Quaternion.Euler(Vector3.zero);
    }

    /**
    * <summary>
    * Creates a copy of the long press button from the friend board.
    * </summary>
    */
    private static GameObject NewFriendButton(string name, string text, System.Action action)
    {
        // Copy the object that we saved to DontDestroyOnLoad earlier
        GameObject newButton = GameObject.Instantiate(friendButton);
        newButton.SetActive(true);
        newButton.name = name;
        newButton.GetComponent<InteractionButton>().onPressed.AddListener(action);
        TextMeshPro buttonText = newButton.transform.GetChild(6).gameObject.GetComponent<TextMeshPro>();
        buttonText.SetText(text);
        return newButton;
    }

    /**
    * <summary>
    * Creates a copy of the Mail Tube on the Gear Market.
    * </summary>
    */
    private static GameObject NewMailTube()
    {
        // Copy the object that we saved to DontDestroyOnLoad earlier
        GameObject newMailTube = GameObject.Instantiate(mailTubeObj);
        newMailTube.SetActive(true);
        return newMailTube;
    }

    /**
    * <summary>
    * Creates a copy of the large gear market button.
    * </summary>
    */
    private static GameObject NewGearMarketButton(string name, string text, System.Action action)
    {
        // Copy the object that we saved to DontDestroyOnLoad earlier
        GameObject newButton = GameObject.Instantiate(gearMarketButton);
        newButton.SetActive(true);
        newButton.name = name;
        // onEndInteraction is the moment you release the button
        newButton.transform.GetChild(0).gameObject.GetComponent<InteractionTouch>().onEndInteraction.AddListener(action);
        TextMeshPro buttonText = newButton.transform.GetChild(0).GetChild(3).gameObject.GetComponent<TextMeshPro>();
        buttonText.fontSize = 0.6f;
        buttonText.m_text = text;
        return newButton;
    }

    /**
    * <summary>
    * Creates a copy of the "flip camera" button from Rock Cam.
    * </summary>
    */
    private static GameObject NewRockCamButton(string name, string text, System.Action action)
    {
        // Copy the object that we saved to DontDestroyOnLoad earlier
        GameObject newButton = GameObject.Instantiate(rockCamButton);
        newButton.SetActive(true);
        newButton.name = name;
        newButton.GetComponent<InteractionButton>().onPressed.AddListener(action);
        TextMeshPro buttonText = newButton.transform.GetChild(1).gameObject.GetComponent<TextMeshPro>();
        buttonText.m_text = text;
        return newButton;
    }

    /**
    * <summary>
    * Retrieves the N-th most recent photo, copies it to the pictures folder, and returns its file name.
    * </summary>
    */
    private static string GetNthMostRecentPhoto(string sourceFolder, int n)
    {
        string picturesPath = Path.Combine(Application.dataPath, "..", UserDataPath, picturesFolder);
        // order by creation time, most recent first
        var files = Directory.GetFiles(sourceFolder, "*.*")
                             .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg"))
                             .OrderByDescending(File.GetCreationTime)
                             .ToList();

        if (n >= files.Count) // there aren't enough photos in the folder
            return null;

        string src = files[n];
        string fileName = Path.GetFileName(src);
        string dst = Path.Combine(picturesPath, fileName);
        File.Copy(src, dst, overwrite: true);
        return fileName;
    }

    /**
    * <summary>
    * Creates a printed photo from the most recent photo taken with Rock Cam.
    * If printed a photo right before that, it will print the next most recent photo.
    * </summary>
    */
    private static void PrintPhoto()
    {
        if (rockCamPicture is not null)
        {
            // if the rock cam slot is still busy, ignore the button press
            return;
        }

        RecordingConfiguration recordingConfig = Singleton<RecordingCamera>.instance.configuration;
        LCKTabletUtility rockCamUtility = rockCamTf.gameObject.GetComponent<LCKTabletUtility>();
        string recordingPath = Path.Combine(recordingConfig.LCKSavePath, rockCamUtility.photosFolderName);
        string imageFile = GetNthMostRecentPhoto(recordingPath, PhotoPrintingIndex);
        PhotoPrintingIndex++; // next time you press the buton, it will print the next photo
        if (imageFile is null)
        {
            LogWarn("No photo to print");
            return;
        }

        rockCamPicture = new PictureData();
        rockCamPicture.path = imageFile;

        // The spawned picture will use the default size and color
        rockCamPicture.padding = defaultPadding;
        rockCamPicture.thickness = defaultThickness;
        rockCamPicture.color = defaultColor;

        // Create the json object that will be used to save the config
        rockCamPicture.jsonConfig = new JObject();
        rockCamPicture.jsonConfig["path"] = rockCamPicture.path;

        CreatePicture(ref rockCamPicture, rockCamHandle.transform);
        rockCamPicture.obj.transform.localPosition = new Vector3(0, rockCamPicture.height / 2, 0);
    }

    /**
    * <summary>
    * Action associated with the "Spawn picture" button.
    * Summons a new picture from the stash, and adds it to the album.
    * </summary>
    */
    private static void SpawnPicture()
    {
        if (animationRunning || mailTubePicture is not null)
        {
            // if the mail tube is still busy, ignore the button press
            return;
        }
        // reload the stash in case other pictures were added to the folder
        reloadStash();
        stashJson = (JArray)root[currentScene]["stash"];
        if (stashJson is not null && stashJson.Count == 0)
        {
            LogWarn("No pictures in stash, cannot spawn new picture");
            return;
        }
        // start the full mail tube animation in a coroutine in order to not block the main thread
        MelonCoroutines.Start(RunMailTubeAnimation());
    }

    /**
    * <summary>
    * Sets the visibility of the preview slab in the mail tube.
    * </summary>
    */
    private static void SetPreviewSlabVisibility(bool visible)
    {
        if (purchaseSlab is not null)
        {
            purchaseSlab.GetChild(0).gameObject.SetActive(visible);
            purchaseSlab.GetChild(1).gameObject.SetActive(visible);
            purchaseSlab.GetChild(2).gameObject.SetActive(visible);
        }
    }

    /**
    * <summary>
    * Delivers the first picture from the stash via the mail tube,
    * </summary>
    */
    private static IEnumerator<WaitForSeconds> RunMailTubeAnimation()
    {
        mailTubePicture = new PictureData();
        mailTubePicture.path = stashJson[0].ToString(); // first image in stash
        stashJson.RemoveAt(0); // remove it from the stash

        // The spawned picture will use the default size and color
        mailTubePicture.padding = defaultPadding;
        mailTubePicture.thickness = defaultThickness;
        mailTubePicture.color = defaultColor;
        mailTubePicture.rotation = new Vector3(0, 180, 0);

        // Create the json object that will be used to save the config
        mailTubePicture.jsonConfig = new JObject();
        mailTubePicture.jsonConfig["path"] = mailTubePicture.path;

        CreatePicture(ref mailTubePicture, mailTubeHandle.transform);

        // Start the built-in animation of the mail tube
        animationRunning = true;
        mailTube.ExecuteMailTubeAnimation();

        // make the preview slab invisible
        SetPreviewSlabVisibility(false);

        // wait 7 seconds for the animation to put the picture in a nice plase,
        // then stop its movement, so it can easily be grabbed.
        yield return new WaitForSeconds(7f);
        if (mailTubePicture is not null)
        {
            mailTubePicture.obj.transform.SetParent(photoAlbum.transform, true);
            mailTubePicture.obj.transform.localScale = Vector3.one; // ensure proper scale
        }

        // wait 2 seconds to re-enable the slab, so that it's back in the mail tube.
        yield return new WaitForSeconds(2f);
        SetPreviewSlabVisibility(true);
        animationRunning = false;
    }

    /**
     * <summary>
     * Harmony patch that catches the moment a photo is taken, and resets the index of the next printed photo.
     * </summary>
     */
    [HarmonyPatch(typeof(LCKTabletUtility), "TakePhoto", new Type[] { })]
    private static class PhotoTakenPatch
    {
        private static bool Prefix(ref LCKTabletUtility __instance)
        {
            PhotoPrintingIndex = 0;
            return true;
        }
    }
}
