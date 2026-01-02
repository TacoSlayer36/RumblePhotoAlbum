# Rumble Photo Album

This mod allows you to customize your play area with framed pictures.

## How to get an image from the disk into the game

![Gear market button](img/GearMarketButton.png)

Simply drop the images you want into `UserData/RumblePhotoAlbum/pictures`, and press the "Spawn picture" button on the Gear Market. The picture will be prompty delivered to you via the Mail Tube! You can then grab and reposition the picture as you like, and even resize it by grabbing it with two hands. You can also hold two pictures at once, one in each hand!

![Spawned picture](img/SpawnedPicture.png)

When you grab the picture, 3 buttons appear on top of it:
- **Stash:** removes the picture from the scene and puts it back into the stash, so that it can be spawned again
- **Hide/Show:** toggles the picture's visibility to legacy and LIV cameras (including Rock Cam). Useful for private photos!
- **Delete:** removes the picture from the scene AND deletes the file on disk if 2 conditions are satisfied:
    - the file is from the "pictures" folder
    - it's not used anywhere else in the album, not even in another scene

![Action buttons](img/actionButtons.png)

The configuration is saved in a json file, and each scene gets its own configuration. The only scene that cannot have framed pictures is the Loader.

You can also spawn a picture from anywhere on your computer by manually adding it's full path to the "stash" list in `UserData/RumblePhotoAlbum/config.json`, for the specific scene where you want the picture to be available.

The park also features a copy of the Mail Tube and the "Spawn picture" button.

![Park mail tube and button](img/ParkButton.png)

For multiplayer maps, there can't be extra stuff on the arena, so you'll have to experiment by manually editing the json file... Or use the Rock Cam printing option!

## Rock Cam integration
You can print photos from your Rock Cam!

!["Print photo" button](img/printPhoto.png)

After taking a photo, press the "Print photo" button on the top of your cam, and a printed version will be spawned, attached to the camera!

If you detach the photo and press the button again, an older photo will be printed, until you run out of photos to print. But if you take another photo in the meantime, it will print the most recent one again.

You don't have to stay in the same scene to print, you can do it at any moment. The image file is copied to the UserData subfolder, so it's available for all scenes.

## GIF support

The mod also supports animated GIFs now!

**Warning:** Be mindful of the performance hit if you want to spawn a lot of them, I tried to optimize as much as I could, but I can't do much if you want to run hundreds of animations in one scene.

![Example of GIF playback](img/gif_example.gif)

## Customization options
Many aspects of the frames can be customized via ModUI or by editing the config.json file in UserData.

- **Frame color:** global color can be set in ModUI. To change for any specific picture, add the field **"color"** to the json configuration of the picture.

- **Frame padding:** this is the little outline around the image, and can also be globally set in ModUI. To change for any specific picture, add the field **"padding"** to the json configuration of the picture.

- **Frame thickness:** global thickness can be set in ModUI. To change for any specific picture, add the field **"thickness"** to the json configuration of the picture.

- **Default picture size:** This only affects the initial size of a newly spawned picture, and can be changed via ModUI. You can resize a picture either by grabbing it or by adding the field **width** or **"height"** to the json configuration of the picture.

- **Transparency:** transparency is disabled by default because it's not very optimized, and makes a small lag spike when creating the picture. You can enable it globally via ModUI, but to enable it for any specific picture, add the field **"alpha": true** to the json configuration of the picture.

- **Action buttons:** you can disable the buttons that appear on the picture. You won't be able to delete/stash/hide it anymore, but it can be useful for filmmaking for example!

- **Picture spawning frequency:** change the delay between loading two consecutive pictures. Making the number higher will make all the pictures in the scene appear slower, one after the other, and will have less performance impact on loading the scene. Useful if you have a hundred pictures to spawn.

- **GIF decoding frequency:** change the delay between reading two consecutive frames in a GIF. Decoding frames from the GIF file can be a costly operation, and making this delay higher will improve performance, at the cost of a longer time needed to finish loading the GIF. This only impact the initial loading in the scene.

- **GIF speed:** change the speed of GIF playback. Whether to make it faster or slower, this will affect all gifs at once. But there is a hardcoded limit of 0.001s between two frames.

## Reloading all the pictures currently in the scene
Any time the mod configuration in ModUI is saved, all the pictures are reloaded.

## For modders! There's an API now!

Use the `RumblePhotoAlbum` namespace to get access to the interface:

### The classes
The class `PictureData` represents a spawned picture. It contains values like the position, size, color, and even the GameObject reference for the picture.

The class `PhotoAPI` contains the API methods. It is currently pretty simple, with just 2 methods: create or delete a picture.

### PhotoAPI.CreatePicture
```cs
public static PictureData CreatePicture(string path, Vector3 position, Vector3 rotation, float width = 0, float height = 0, float? padding = null, float? thickness = null, Color? color = null, bool? alpha = null);
```
This method can be used by an outside mod in order to create a picture. A bunch of things can be customized, but they are all optional. The only mandatory arguments are the path, the position and the rotation of the picture. The rest can be added as named arguments, for example `color: Color.black`.

The returned value is a structure that represents the freshly spawned picture. If the spawning failed, then `pictureData.obj` will be `null`.

### PhotoAPI.DeletePicture
```cs
public static void DeletePicture(PictureData pictureData);
```
This method can be used by an outside mod in order to delete a picture. Simply provide the structure that was returned by `PhotoAPI.CreatePicture`.

### PictureData.delete
```cs
public void delete();
```
Equivalent to `PhotoAPI.DeletePicture`.