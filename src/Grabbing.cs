using MelonLoader;
using UnityEngine;
using RumbleModdingAPI;
using Newtonsoft.Json.Linq;
using System;
using HarmonyLib;
using Il2CppRUMBLE.Players;

namespace RumblePhotoAlbum;

public partial class MainClass : MelonMod
{
    private static bool[] grip = { false, false }; // whether the grip is pressed on each controller
    private static bool[] holding = { false, false }; // whether the picture is being held by each hand
    private static GameObject[] hand = { null, null }; // left and right hand GameObjects
    private static PictureData[] currentlyModified = { null, null }; // two pictures can be modified at once
    private static GameObject resizingHandle = null; // parent to the picture when resizing

    private const float hold_distance = 0.05f; // Distance to consider holding a picture

    /**
    * <summary>
    * Initialize the GameObject variables, and reset the holding status.
    * </summary>
    */
    private static void InitGrabbing()
    {
        Transform playerTr = Calls.Players.GetPlayerController().gameObject.transform.GetChild(2);
        grip[0] = false;
        grip[1] = false;
        holding[0] = false;
        holding[1] = false;
        hand[0] = playerTr.GetChild(1).gameObject;
        hand[1] = playerTr.GetChild(2).gameObject;
        currentlyModified[0] = null;
        currentlyModified[1] = null;
        resizingHandle = new GameObject("ResizingHandle");
        resizingHandle.transform.SetParent(playerTr, true);
    }

    /**
    * <summary>
    * To be called regularly. Checks the triggers on both hands
    * and changes the holding state accordingly.
    * </summary>
    */
    private static void ProcessGrabbing()
    {
        bool grabbingChanged = ProcessGrabbingPerHand(0) || ProcessGrabbingPerHand(1);
        if (grabbingChanged)
        {
            bool doubleHolding = (currentlyModified[0] == currentlyModified[1]);
            for (int i = 0; i < 2; i++)
            {
                if (currentlyModified[i] is not null)
                {
                    var pictureData = currentlyModified[i];
                    UpdatePictureParent(pictureData);
                    GameObject actionButtons = pictureData.obj.transform.GetChild(0).GetChild(0).gameObject;
                    if (!holding[i])
                    {
                        if (!doubleHolding || !holding[1 - i]) // not holsing with the other hand either
                        {
                            actionButtons.SetActive(false);
                            // Reset currently modified picture:
                            if (doubleHolding) currentlyModified[1 - i] = null;
                        }
                        currentlyModified[i] = null;
                    }
                    else if (!doubleHolding && holding[1 - i])
                    {
                        // removing action buttons when holding two different pictures
                        actionButtons.SetActive(false);
                    }
                    else
                    {
                        actionButtons.SetActive(true);
                    }
                }
            }
        }
    }

    /**
    * <summary>
    * Check the grip on the controller of one hand,
    * and update the holding status of this hand accordingly.
    * </summary>
    */
    private static bool ProcessGrabbingPerHand(int index)
    {
        bool holdingChanged = CheckIfGripChanged(index);
        if (holdingChanged) // if the grip status changed
        {
            bool holding_old = holding[index];
            UpdateHolding(index); // check if that impacted any picture holding
            holdingChanged = (holding[index] != holding_old);
        }
        return holdingChanged; // Return true if holding state changed
    }

    /**
    * <summary>
    * Returns true if the grip status of the controller was changed (pressed or released),
    * and updates the grip status accordingly.
    * </summary>
    */
    private static bool CheckIfGripChanged(int index)
    {
        bool grip_new = false;
        // consider the grip active if either the trigger or
        // the grip is pressed on the controller
        if (index == 0)
        {
            grip_new = (Calls.ControllerMap.LeftController.GetTrigger() > 0.5f ||
                Calls.ControllerMap.LeftController.GetGrip() > 0.5f);
        }
        else if (index == 1)
        {
            grip_new = (Calls.ControllerMap.RightController.GetTrigger() > 0.5f ||
                Calls.ControllerMap.RightController.GetGrip() > 0.5f);
        }

        bool gripChanged = (grip_new != grip[index]);

        if (grip_new && !grip[index])
        {
            // Start grabbing
            grip[index] = true;
        }
        else if (!grip_new && grip[index])
        {
            // Stop grabbing
            grip[index] = false;
        }
        return gripChanged; // Return true if grip state changed
    }

    /**
    * <summary>
    * Updates the holding status of a hand by checking if it's close enough to a picture.
    * </summary>
    */
    private static void UpdateHolding(int index)
    {
        if (PicturesList is null)
        {
            return;
        }
        if (!grip[index]) // If the grip is not pressed, holding is impossible
        {
            holding[index] = false;
            return;
        }

        bool doubleHolding = false;
        // if there is already a picture we're holding, prioritize it
        if (currentlyModified[1 - index] is not null)
        {
            float dst = DistanceToPictureSurface(hand[index], currentlyModified[1 - index]);
            holding[index] = (dst < hold_distance); // less than 5cm away
            if (dst < hold_distance) // less than 5cm away
            {
                currentlyModified[index] = currentlyModified[1 - index];
                doubleHolding = true;
            }
        }
        if (!doubleHolding) // if not double-holding, find the closest picture within the hold distance
        {
            float dst_min = hold_distance;
            int i_min = -1;
            for (int i = 0; i < PicturesList.Count; i++)
            {
                var pictureData = PicturesList[i];
                if (pictureData.obj is null)
                {
                    LogWarn($"Framed picture {pictureData.path} has no GameObject associated with it.");
                    continue;
                }
                float dst = DistanceToPictureSurface(hand[index], pictureData);
                if (dst < dst_min)
                {
                    holding[index] = true;
                    dst_min = dst;
                    i_min = i;
                }
            }

            if (i_min != -1)
            {
                var pictureData = PicturesList[i_min];
                // Update currently modified picture
                currentlyModified[index] = pictureData; 

                if (buttonsVisibility)
                {
                    GameObject actionButtons = pictureData.obj.transform.GetChild(0).GetChild(0).gameObject;
                    actionButtons.SetActive(true);
                }
                if (pictureData == mailTubePicture)
                {
                    mailTubePicture = null;
                    albumJson.Add(pictureData.jsonConfig);
                }
                if (pictureData == rockCamPicture)
                {
                    rockCamPicture = null;
                    albumJson.Add(pictureData.jsonConfig);
                }
            }
        }
    }

    /**
    * <summary>
    * Get the distance from the hand to the frame's edge.
    * </summary>
    */
    private static float DistanceToPictureSurface(GameObject hand, PictureData pictureData)
    {
        Vector3 handPos = hand.transform.position;

        Transform picTransform = pictureData.obj.transform.GetChild(0);
        Vector3 center = picTransform.position;
        Quaternion rotation = picTransform.rotation;

        // Physical box half-size
        Vector3 extents = new Vector3(pictureData.width,
                                          pictureData.height,
                                          pictureData.thickness) * 0.5f;

        // Transform hand into the space of the picture
        Vector3 localHandPos = Quaternion.Inverse(rotation) * (handPos - center);

        // get distance along each axis
        float dx = Mathf.Max(0f, Mathf.Abs(localHandPos.x) - extents.x);
        float dy = Mathf.Max(0f, Mathf.Abs(localHandPos.y) - extents.y);
        float dz = Mathf.Max(0f, Mathf.Abs(localHandPos.z) - extents.z);

        // If all are zero, the hand is inside or touching the box: return 0
        if (dx == 0f && dy == 0f && dz == 0f)
            return 0f;

        // Approximate distance to the surface
        return Mathf.Sqrt(dx*dx + dy*dy + dz*dz);
    }

    /**
    * <summary>
    * Change the parent of the picture's tranform
    * depending on which hands are holding the picture.
    * </summary>
    */
    private static void UpdatePictureParent(PictureData pictureData)
    {
        if (holding[0] && currentlyModified[0]==pictureData)
        {
            if (holding[1] && currentlyModified[1] == pictureData) // holding with two hands
            {
                UpdateResizingHandle();
                pictureData.obj.transform.SetParent(resizingHandle.transform, true);
            }
            else // holding in left hand
            {
                pictureData.obj.transform.SetParent(hand[0].transform, true);
            }
        }
        else if (holding[1] && currentlyModified[1] == pictureData) // holding in right hand
        {
            pictureData.obj.transform.SetParent(hand[1].transform, true);
        }
        else
        {
            // If not holding this picture, return to default parent
            pictureData.obj.transform.SetParent(photoAlbum.transform, true);
            UpdatePictureConfig(pictureData);
        }
    }

    /**
    * <summary>
    * To be called regularly. Checks if the image is currently being held by two hands,
    * and if such is the case, updates it to get resized and rotated by them.
    * </summary>
    */
    private static void UpdateResizingIfNeeded()
    {
        if ((currentlyModified[0] is not null) && // holding something is left hand
            (currentlyModified[0] == currentlyModified[1])) // holding it with two hands
        {
            // update the scale and position of the handle in between the two hands
            UpdateResizingHandle();
            UpdatePictureSize(currentlyModified[0]);
        }
    }

    /**
    * <summary>
    * Update the position, rotation and scale of the resizing handle
    * </summary>
    */
    private static void UpdateResizingHandle()
    {
        Transform left = hand[0].transform;
        Transform right = hand[1].transform;

        // get middle position between the two hands
        Vector3 midPos = (left.localPosition + right.localPosition) * 0.5f;

        // X axis: from left to right hand
        Vector3 xAxis = (right.localPosition - left.localPosition).normalized;

        // Y axis: the averaged hand "up", but projected onto plane perpendicular to x
        Quaternion avgRotation = Quaternion.Slerp(left.localRotation, right.localRotation, 0.5f);
        Vector3 yAxis = Vector3.ProjectOnPlane(avgRotation * Vector3.up, xAxis).normalized;

        // Z axis: completing the right-handed basis
        Vector3 zAxis = Vector3.Cross(xAxis, yAxis).normalized;
        yAxis = Vector3.Cross(zAxis, xAxis); // guarantee orthonormality

        // construct picture rotation from this basis
        Quaternion rotation = Quaternion.LookRotation(zAxis, yAxis);

        // set proportional scale
        float distance = Vector3.Distance(left.localPosition, right.localPosition);
        float scale = currentlyModified[0].obj.transform.localScale.x * distance;
        Transform frame = currentlyModified[0].obj.transform.GetChild(0);
        float pictureSize = Math.Max(frame.localScale.x, frame.localScale.y);
        float limitation = Math.Min(pictureSize, maxPictureSize / scale)/ pictureSize;

        resizingHandle.transform.localScale = Vector3.one * distance * limitation;

        // apply the new position and rotation
        resizingHandle.transform.localPosition = midPos;
        resizingHandle.transform.localRotation = rotation;
    }

    /**
    * <summary>
    * Update the size of each element of the picture as it is being resized
    * </summary>
    */
    private static void UpdatePictureSize(PictureData pictureData)
    {
        // get the global scale of the picture
        float scale = pictureData.obj.transform.localScale.x *
                      resizingHandle.transform.localScale.x;

        Transform frame = pictureData.obj.transform.GetChild(0);
        Transform quad = pictureData.obj.transform.GetChild(1);
        float aspectRatio = quad.localScale.y / quad.localScale.x;

        // the width of the image is imposed by the width of the frame
        float localQuadWidth = frame.localScale.x - 2*pictureData.padding / scale;
        // the height is imposed by the aspect ratio of the image
        quad.localScale = new Vector3(localQuadWidth,
                                      localQuadWidth*aspectRatio,
                                      1f);

        // the frame's width is set by resizing, but the height follows the image's height
        frame.localScale = new Vector3(frame.localScale.x,
                                        quad.localScale.y + 2*pictureData.padding/scale,
                                        pictureData.thickness/scale);

        pictureData.width = frame.localScale.x * scale;
        pictureData.height = frame.localScale.y * scale;

        // move the frame to the back
        frame.transform.localPosition = new Vector3(0f, 0f, pictureData.thickness / (2*scale));
        // move the image quad to the front
        quad.transform.localPosition = new Vector3(0f, 0f, -imageOffset / scale);
    }
    /**
    * <summary>
    * Harmony patch that is called when the local player is initialized
    * </summary>
    */
    [HarmonyPatch(typeof(PlayerController), "Initialize", new System.Type[] { typeof(Player) })]
    private static class PlayerSpawnPatch
    {
        private static void Postfix(ref PlayerController __instance, ref Player player)
        {
            if (Calls.Players.GetLocalPlayer() == player)
            {
                InitGrabbing();
                initializeRockCam();
            }
        }
    }

}
