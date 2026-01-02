using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Harmony;
using Il2CppTMPro;
using MelonLoader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RumbleModdingAPI;
using ThreeDISevenZeroR.UnityGifDecoder;
using ThreeDISevenZeroR.UnityGifDecoder.Model;
using UnityEngine;
using UnityEngine.Playables;
using static RumbleModdingAPI.Calls;

namespace RumblePhotoAlbum;

public partial class MainClass : MelonMod
{
    // constants
    private const string UserDataPath = "UserData/RumblePhotoAlbum";
    private const string picturesFolder = "pictures";
    private const string configFile = "config.json";
    private const float imageOffset = 0.001f; // put the image 1mm in front of the frame

    // variables
    private static JObject root = null;
    private static string fullPath = Path.Combine(Application.dataPath, "..", UserDataPath, configFile);
    private static List<GifData> gifs = null;
    private static bool gifsLoading = false;
    private static bool gifsPlaying = false;
    private static float gifSpeed = 1f;
    private static float spawningFrequency = 0.02f;
    private static float gifDecodingFrequency = 0.01f;

    /**
    * <summary>
    * Creates the necessary folders in UserData if they don't exist.
    * </summary>
    */
    private static void EnsureUserDataFolders()
    {
        string picturesPath = Path.Combine(Application.dataPath, "..", UserDataPath, picturesFolder);
        Directory.CreateDirectory(UserDataPath);
        Directory.CreateDirectory(picturesPath);
    }

    /**
    * <summary>
    * Reads the config file, updates it if necessary (missing pictures in the stash,
    * extra images that don't exist on disk), and creates the objects in the scene.
    * </summary>
    */
    private static IEnumerator<WaitForSeconds> LoadAlbum(string sceneName)
    {
        Log($"Reading from disk");
        gifsLoading = false;
        PicturesList = new List<PictureData>();
        JArray album = null;
        JToken sceneObj = null;
        try
        {
            if (!File.Exists(fullPath))
            {
                LogWarn($"Creating new configuration file at: {fullPath}.");
                root = new JObject();
            }
            else
            {
                string json = File.ReadAllText(fullPath);
                root = JObject.Parse(json);
            }


            // if the field with this scen name doesn't exist, create it
            if (!root.TryGetValue(sceneName, out sceneObj) || sceneObj.Type != JTokenType.Object)
            {
                LogWarn($"No valid entry found for scene \"{sceneName}\". Creating an empty object.");
                sceneObj = new JObject();
                root[sceneName] = sceneObj;
            }


            album = sceneObj["album"] as JArray ?? new JArray();

            photoAlbum = new GameObject();
            photoAlbum.name = "PhotoAlbum";
        }
        catch (Exception ex)
        {
            LogError($"Failed to load or parse {configFile}: {ex.Message}");
        }

        if (album is null || sceneObj is null)
        {
            yield break;
        }

        var wait = new WaitForSeconds(spawningFrequency);
        gifs = new List<GifData>();
        gifsPlaying = false;
        gifsLoading = true;

        // Validate album entries
        var cleanedAlbum = new JArray();
        foreach (var entry in album)
        {
            PictureData pictureData = null;
            try
            {
                pictureData = ParsePictureData(entry);
                Log($"Creating picture {pictureData.path}");

                if (pictureData is null)
                    continue;
            }
            catch (Exception ex)
            {
                LogError($"Failed to parse entry: {ex.Message}");
                continue;
            }
            try
            {
                CreatePicture(ref pictureData, photoAlbum.transform);
                cleanedAlbum.Add(entry);
                pictureData.jsonConfig = cleanedAlbum[cleanedAlbum.Count - 1];
            }
            catch (Exception ex)
            {
                LogError($"Failed to parse entry: {ex.Message}");
                if (ex.Message != "file doesn't exist")
                {
                    cleanedAlbum.Add(entry);
                    pictureData.jsonConfig = cleanedAlbum[cleanedAlbum.Count - 1];
                }
                continue;
            }

            yield return wait; // Yield to avoid freezing the game
        }

        // play all gif animations (if any)
        gifsPlaying = true;
        MelonCoroutines.Start(PlayAllGifs());

        try
        {
            reloadStash();
            sceneObj["album"] = cleanedAlbum;

            // Save back the modified config
            File.WriteAllText(fullPath, root.ToString(Formatting.Indented));

            stashJson = (JArray)root[currentScene]["stash"];
            albumJson = (JArray)root[currentScene]["album"];
        }
        catch (Exception ex)
        {
            LogError($"Failed to update configuration file: {ex.Message}");
        }
    }

    /**
    * <summary>
    * Reloads the stash accordingly to what's in the "pictures" folder.
    * </summary>
    */
    private static void reloadStash()
    {
        JObject sceneObj = (JObject)root[currentScene];
        JArray stash = (JArray)sceneObj["stash"] ?? new JArray();
        JArray album = sceneObj["album"] as JArray ?? new JArray();
        HashSet<string> albumSet = new(album
                .Where(e => e["path"] != null)
                .Select(e => e["path"].ToString())
            );

        // Validate stash entries
        var cleanedStash = new HashSet<string>();
        foreach (var entry in stash)
        {
            string picturePath = entry.ToString();
            if (!File.Exists(picturePath))
            {
                // if the path is not absolute, assume it's relative to the pictures folder
                string globalPicturePath = Path.Combine(Application.dataPath, "..", UserDataPath, picturesFolder, picturePath);
                if (!File.Exists(globalPicturePath))
                {
                    LogWarn($"Removed missing file from stash: {picturePath}");
                    continue;
                }
                cleanedStash.Add(picturePath);
            }
        }

        // Get the list of images that are currently used in the album and/or stash
        var usedImages = new HashSet<string>(cleanedStash);
        usedImages.UnionWith(albumSet);

        // Check all image files in the "pictures" folder
        string picturesPath = Path.Combine(Application.dataPath, "..", UserDataPath, picturesFolder);
        var imageFiles = new HashSet<string>(
            Directory.Exists(picturesPath)
                ? Directory.GetFiles(picturesPath)
                          .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg") || f.EndsWith(".gif"))
                          .Select(f => f)
                : Enumerable.Empty<string>()
        );

        foreach (var file in imageFiles)
        {
            string fileName = Path.GetFileName(file);
            if (!usedImages.Contains(fileName))
            {
                cleanedStash.Add(fileName);
            }
        }

        // Rebuild updated stash/album
        sceneObj["stash"] = new JArray(cleanedStash);
    }

    /**
    * <summary>
    * Creates a Texture2D from an image file, blending it on top of a predetermined
    * color in order to remove alpha transparency. This way it looks like it has
    * transparency when superposed on top of a colored background.
    * </summary>
    */
    private static PictureData ParsePictureData(JToken pictureJson)
    {
        if (pictureJson == null || pictureJson.Type != JTokenType.Object)
            throw new ArgumentException("Invalid JSON object for PictureData.");

        JObject obj = (JObject)pictureJson;

        PictureData pictureData = new PictureData();

        // Required fields
        pictureData.path = obj.Value<string>("path");
        if (string.IsNullOrEmpty(pictureData.path))
        {
            throw new ArgumentException($"Missing field \"path\"");
        }

        pictureData.position = ParseVector3(obj["position"], "position");
        pictureData.rotation = ParseVector3(obj["rotation"], "rotation");

        // Optional fields with defaults
        pictureData.width = Math.Min(obj.Value<float?>("width") ?? 0, maxPictureSize);
        pictureData.height = Math.Min(obj.Value<float?>("height") ?? 0, maxPictureSize);
        pictureData.padding = obj.Value<float?>("padding") ?? defaultPadding;
        pictureData.thickness = obj.Value<float?>("thickness") ?? defaultThickness;

        pictureData.color = defaultColor; // Default color
        if (obj.TryGetValue("color", out JToken colorToken))
            pictureData.color = ParseColor(colorToken);

        pictureData.alpha = obj.Value<bool?>("alpha") ?? enableAlpha;
        pictureData.visible = obj.Value<bool?>("visible") ?? visibility;

        return pictureData;
    }

    /**
    * <summary>
    * Parses a json token as a Vector3
    * </summary>
    */
    private static Vector3 ParseVector3(JToken token, string fieldName)
    {
        if (token == null)
        {
            throw new ArgumentException($"Missing field \"{fieldName}\"");
        }

        if ( token.Type != JTokenType.Array)
        {
            throw new ArgumentException($"{fieldName}' must be an array [x, y, z] (got {token.ToString()})");
        }

        float[] values = token.ToObject<float[]>();
        if (values.Length != 3)
        {
            throw new ArgumentException($"{fieldName}' must have exactly 3 elements (got {token.ToString()})");
        }

        return new Vector3(values[0], values[1], values[2]);
    }

    /**
    * <summary>
    * Parses a json token as a color, either as a hex string or an array of floats.
    * </summary>
    */
    private static Color ParseColor(JToken token)
    {
        if (token.Type == JTokenType.Array)
        {
            float[] c = token.ToObject<float[]>();
            if (c.Length >= 3)
            {
                return new Color(c[0], c[1], c[2], c.Length >= 4 ? c[3] : 1.0f);
            }
        }
        else if (token.Type == JTokenType.String)
        {
            string hex = token.ToString();
            return Hex2Color(hex);
        }

        throw new ArgumentException("PictureData: 'color' must be [r,g,b,a?] or hex string.");
    }

    /**
    * <summary>
    * Parses a hex string as a color.
    * </summary>
    */
    private static Color Hex2Color(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out Color color))
        {
            return color;
        }
        throw new ArgumentException("PictureData: 'color' must be [r,g,b,a?] or hex string.");
    }

    /**
    * <summary>
    * Parses the image file and creates the physical picture in the scene.
    * </summary>
    */
    protected static void CreatePicture(ref PictureData pictureData, Transform parent)
    {
        string filePath = pictureData.path;
        if (!File.Exists(filePath))
        {
            // if the path is not absolute, assume it's relative to the pictures folder
            string globalPicturePath = Path.Combine(Application.dataPath, "..", UserDataPath, picturesFolder, pictureData.path);
            if (!File.Exists(globalPicturePath))
            {
                throw new Exception("file doesn't exist");
                return; // File does not exist, cannot create picture block
            }
            else
            {
                filePath = globalPicturePath;
            }
        }

        if (pictureData.path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            CreateGifBlock(filePath, ref pictureData, parent);
        }
        else
        {
            // Load texture from image file, with the frame's color as background
            Texture2D imageTexture = null;
            if (pictureData.alpha)
            {
                imageTexture = LoadFlattenedTexture(filePath, pictureData.color);
            }
            else
            {
                imageTexture = LoadTexture(filePath);
            }
            CreatePictureBlock(ref pictureData, parent, imageTexture);
        }
    }

    /**
    * <summary>
    * Creates the GameObject for a framed picture in the scene.
    * </summary>
    */
    private static Renderer CreatePictureBlock(ref PictureData pictureData, Transform parent, Texture2D imageTexture)
    {

        int pictureLayer = pictureData.visible?
            LayerMask.NameToLayer("UI") // No collision, visible
            : LayerMask.NameToLayer("PlayerFade"); // No collision, invisible

        float aspectRatio = (float)imageTexture.height / imageTexture.width;
        if (pictureData.width == 0 && pictureData.height == 0)
        {
            if (aspectRatio > 1) // vertical image
            {
                pictureData.height = defaultSize;
            }
            else // horizontal image
            {
                pictureData.width = defaultSize;
            }
        }
        if (pictureData.width == 0)
        {
            pictureData.width = (pictureData.height - 2*pictureData.padding) / aspectRatio + 2*pictureData.padding;
        }
        else
        {
            pictureData.height = (pictureData.width - 2*pictureData.padding) * aspectRatio + 2*pictureData.padding;
        }

        GameObject obj = new GameObject();
        obj.layer = pictureLayer;
        obj.name = $"PictureBlock: {Path.GetFileNameWithoutExtension(pictureData.path)}";
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = pictureData.position;
        obj.transform.localRotation = Quaternion.Euler(pictureData.rotation);

        // Create frame
        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.layer = pictureLayer;
        frame.name = "frame";
        frame.transform.SetParent(obj.transform, false);
        frame.transform.localScale = new Vector3(pictureData.width, pictureData.height, pictureData.thickness);
        frame.transform.localPosition = new Vector3(0f, 0f, pictureData.thickness / 2);

        Renderer frameRenderer = frame.GetComponent<Renderer>();
        frameRenderer.material.shader = Shader.Find("Shader Graphs/RUMBLE_Prop");
        frameRenderer.material.SetColor("_Overlay", pictureData.color);

        // Create quad with the image on it
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.layer = pictureLayer;
        quad.name = "picture";
        quad.transform.SetParent(obj.transform, false);
        quad.transform.localScale = new Vector3(pictureData.width - 2*pictureData.padding,
                                                 pictureData.height - 2*pictureData.padding,
                                                 1f);

        // Picture positioned 1mm in front of the frame (local +Z)
        quad.transform.localPosition = new Vector3(0f, 0f, -imageOffset);
        quad.transform.localRotation = Quaternion.identity;

        Renderer quadRenderer = quad.GetComponent<Renderer>();
        quadRenderer.material.shader = Shader.Find("Shader Graphs/RUMBLE_Prop");
        quadRenderer.material.SetTexture("_Albedo", imageTexture);

        // Make the picture interactable
        pictureData.obj = obj;
        pictureData = pictureData;
        PicturesList.Add(pictureData);

        CreateActionButtons(pictureData);

        return quadRenderer;
    }

    /**
    * <summary>
    * Data that needs to be stored for each frame of a GIF.
    * </summary>
    */
    private class FrameData
    {
        public Texture2D texture;
        public WaitForSeconds delay;
    }

    /**
    * <summary>
    * Data that needs to be stored for each GIF object to be animated
    * </summary>
    */
    private class GifData
    {
        public Renderer renderer;
        public GifStream gifStream;
        public FrameData firstFrame;
        public string path;
    }

    /**
    * <summary>
    * Creates the GameObject for an animated framed picture in the scene.
    * </summary>
    */
    private static void CreateGifBlock(string filePath, ref PictureData pictureData, Transform parent)
    {
        var gifStream = new GifStream(filePath);

        FrameData firstFrame = null;
        // read all data in the stream until finding an image
        while (firstFrame is null && gifStream.HasMoreData)
        {
            firstFrame = ReadGifFrame(gifStream);
        }
        var renderer = CreatePictureBlock(ref pictureData, parent, firstFrame.texture);

        // add indicator that the gif is loading
        GameObject loadingText = Calls.Create.NewText();
        TextMeshPro component = loadingText.GetComponent<TextMeshPro>();
        component.text = "Loading...";
        component.fontSize = 1f;
        component.color = Color.black;
        loadingText.name = "loadingText";
        loadingText.transform.SetParent(renderer.gameObject.transform.parent, true);
        loadingText.transform.localPosition = new Vector3(0, 0, -0.002f);
        loadingText.transform.localRotation = Quaternion.Euler(new Vector3(0, 0, 0));

        if (gifs is not null)
        {
            gifs.Add(new GifData
            {
                renderer = renderer,
                gifStream = gifStream,
                firstFrame = firstFrame,
                path = pictureData.path
            });
            if (!gifsLoading)
            {
                gifsLoading = true;
                gifsPlaying = true;
                MelonCoroutines.Start(PlayAllGifs());
            }
        }
    }

    /**
    * <summary>
    * Loads the gif frames one gif after another, yielding between each frame to avoid freezing the game.
    * Once a gif is fully loaded, a new coroutine is started to play it.
    * This function finishes when all gifs are loaded.
    * </summary>
    */
    private static IEnumerator<WaitForSeconds> PlayAllGifs()
    {
        if (gifs is null || gifs.Count == 0)
        {
            gifsLoading = false;
            yield break;
        }
        Log($"Starting coroutine to load all gifs");
        var wait = new WaitForSeconds(gifDecodingFrequency);
        int gifIndex = 0;
        while (gifsPlaying &&
            gifs is not null &&
            gifIndex < gifs.Count)
        {
            var gifData = gifs[gifIndex];
            var frames = new List<FrameData>();
            frames.Add(gifData.firstFrame);

            int index = 0;
            while (gifData.gifStream.HasMoreData) // read all data in the stream until there is no more
            {
                yield return wait;

                FrameData frame = ReadGifFrame(gifData.gifStream);
                if (frame is not null)
                {
                    frames.Add(frame);
                }
            }

            try
            {
                if (gifData.renderer is not null)
                {
                    // finished loading, remove indicator
                    GameObject.Destroy(gifData.renderer.gameObject.transform.parent.GetChild(2).gameObject);
                    Log($"Starting coroutine to play gif: {gifData.path}");
                    MelonCoroutines.Start(PlayGif(gifData.renderer, frames, gifData.path));
                }
            }
            catch (Exception ex)
            {
            }
            gifIndex++;
            yield return wait;
        }
        if (gifIndex == gifs.Count)
        {
            gifs = new List<GifData>();
        }
        gifsLoading = false;
        Log($"Stopping coroutine to load all gifs");
    }

    /**
    * <summary>
    * Animates the texture on the renderer with the frames from the GIF stream.
    * </summary>
    */
    private static IEnumerator<WaitForSeconds> PlayGif(Renderer renderer, List<FrameData> frames, string path)
    {
        int i = 0;
        while (gifsPlaying)
        {
            var frame = frames[i];

            if (frame.texture == null)
            {
                i = (i + 1) % frames.Count;
                yield return frame.delay;
                continue;
            }
            try
            {
                renderer.material.SetTexture("_Albedo", frame.texture);
            }
            catch (Exception ex)
            {
            }

            // Wait one frame to allow GPU update, then wait delay
            yield return frame.delay;

            i = (i + 1) % frames.Count;
        }
        Log($"Stopping coroutine to play gif: {path}");
    }

    /**
    * <summary>
    * Reads the next frame from the GIF stream, and returns its data. If none are left, returns null.
    * </summary>
    */
    private static FrameData ReadGifFrame(GifStream gifStream)
    {
        switch (gifStream.CurrentToken)
        {
            case GifStream.Token.Image:
                var image = gifStream.ReadImage();
                var tex = new Texture2D(
                    gifStream.Header.width,
                    gifStream.Header.height,
                    TextureFormat.ARGB32, false);

                tex.SetPixels32(image.colors);
                tex.Apply();
                float delay = image.SafeDelaySeconds / gifSpeed - 0.01f;
                if (delay<0.001f)
                {
                    delay = 0.001f;
                }

                // We have to store the texture and the delay for the playback,
                // because the delay can be irregular, and we save memory by preallocating everything.
                return new FrameData
                {
                    texture = tex,
                    delay = new WaitForSeconds(delay)
                };

            case GifStream.Token.EndOfFile:
                break;

            default:
                gifStream.SkipToken(); // Other tokens
                break;
        }
        return null;
    }


    /**
    * <summary>
    * Creates the action buttons on top of the picture frame.
    * </summary>
    */
    private static void CreateActionButtons(PictureData pictureData)
    {
        var frame = pictureData.obj.transform.GetChild(0).gameObject;

        float buttonSize = pictureData.width / 6;
        Vector3 buttonScale = new Vector3(10 * buttonSize, pictureData.thickness / 0.03f, 10 * buttonSize);
        float buttonHeight = pictureData.height / 2 + buttonSize * 0.6f;

        GameObject actionButtons = new GameObject();
         actionButtons.name = "actionButtons";
        actionButtons.transform.localScale = Vector3.one;

        System.Action action = () => stashPicture(pictureData);
        GameObject stashButton = NewFriendButton("stash", "Stash", action);
        stashButton.transform.localScale = buttonScale;
        stashButton.transform.SetParent(actionButtons.transform, true);
        stashButton.transform.localPosition = new Vector3(-pictureData.width / 2 + buttonSize / 2, 0, 0);
        stashButton.transform.localRotation = Quaternion.Euler(new Vector3(90f, 90f, -90));

        action = () => togglePictureVisibility(pictureData);
        GameObject visibilityButton = NewFriendButton("visibility", pictureData.visible ? "Hide" : "Show", action);
        visibilityButton.transform.localScale = buttonScale;
        visibilityButton.transform.SetParent(actionButtons.transform, true);
        visibilityButton.transform.localPosition = new Vector3(0, 0, 0);
        visibilityButton.transform.localRotation = Quaternion.Euler(new Vector3(90f, 90f, -90));

        action = () => deletePicture(pictureData, false);
        GameObject deleteButton = NewFriendButton("delete", "Delete", action);
        deleteButton.transform.localScale = buttonScale;
        deleteButton.transform.SetParent(actionButtons.transform, true);
        deleteButton.transform.localPosition = new Vector3(pictureData.width / 2 - buttonSize / 2, 0, 0);
        deleteButton.transform.localRotation = Quaternion.Euler(new Vector3(90f, 90f, -90));

        actionButtons.transform.SetParent(frame.transform);
        actionButtons.transform.localScale = new Vector3(1 / frame.transform.localScale.x,
                                                         1 / frame.transform.localScale.y,
                                                         1 / frame.transform.localScale.z);
        actionButtons.transform.localPosition = new Vector3(0, buttonHeight / pictureData.height, 0);
        actionButtons.transform.localRotation = Quaternion.Euler(Vector3.zero);
        actionButtons.SetActive(false);
    }

    /**
    * <summary>
    * Create a Texture2D from an image file, blending it on top of a predetermined
    * color in order to remove alpha transparency. This way it looks like it has
    * transparency when superposed on top of a colored background.
    * </summary>
    */
    private static Texture2D LoadTexture(string path)
    {
        // Load image into Texture2D with alpha channel
        byte[] data = File.ReadAllBytes(path);
        Texture2D output = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        output.LoadImage(data);
        return output;
    }

    /**
    * <summary>
    * Create a Texture2D from an image file, blending it on top of a predetermined
    * color in order to remove alpha transparency. This way it looks like it has
    * transparency when superposed on top of a colored background.
    * </summary>
    */
    private static Texture2D LoadFlattenedTexture(string path, Color background)
    {
        // Load image into Texture2D with alpha channel
        byte[] data = File.ReadAllBytes(path);
        Texture2D input = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        input.LoadImage(data);

        // Get all pixels of the image at once
        Color[] inputPixels = input.GetPixels();
        Color[] outputPixels = new Color[inputPixels.Length];

        for (int i = 0; i < inputPixels.Length; i++)
        {
            Color src = inputPixels[i];
            float a = src.a;

            // Alpha blend image over background, so it looks like
            // it has transparency over a similar background
            outputPixels[i] = new Color(
                src.r * a + background.r * (1f - a),
                src.g * a + background.g * (1f - a),
                src.b * a + background.b * (1f - a)
            );
        }

        // Prepare output texture (without transparency)
        Texture2D output = new Texture2D(input.width, input.height, TextureFormat.RGB24, false);
        output.SetPixels(outputPixels);
        output.Apply();

        return output;
    }

    /**
    * <summary>
    * Update the json config for a picture in the album, with the new position and size.
    * </summary>
    */
    private static void UpdatePictureConfig(PictureData pictureData)
    {
        if (pictureData.jsonConfig is null)
        {
            return;
        }
        Vector3 position = pictureData.obj.transform.position;
        Vector3 rotation = pictureData.obj.transform.eulerAngles;
        // Update position and rotation (overwrite with new arrays)
        pictureData.jsonConfig["position"] = new JArray { position.x, position.y, position.z };
        pictureData.jsonConfig["rotation"] = new JArray { rotation.x, rotation.y, rotation.z };

        // Check if "height" exists, then update; otherwise update "width"
        if (pictureData.jsonConfig["height"] != null)
        {
            pictureData.jsonConfig["height"] = pictureData.height;
        }
        else
        {
            pictureData.jsonConfig["width"] = pictureData.width;
        }
        if (pictureData.jsonConfig["visible"] != null || pictureData.visible!=visibility)
        {
            pictureData.jsonConfig["visible"] = pictureData.visible;
        }

        // Save full file back to disk
        File.WriteAllText(fullPath, root.ToString(Formatting.Indented));
    }
}
