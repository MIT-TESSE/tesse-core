/*
###################################################################################################
# DISTRIBUTION STATEMENT A. Approved for public release. Distribution is unlimited.
#
# This material is based upon work supported by the Under Secretary of Defense for Research and
# Engineering under Air Force Contract No. FA8702-15-D-0001. Any opinions, findings, conclusions
# or recommendations expressed in this material are those of the author(s) and do not necessarily
# reflect the views of the Under Secretary of Defense for Research and Engineering.
#
# (c) 2020 Massachusetts Institute of Technology.
#
# MIT Proprietary, Subject to FAR52.227-11 Patent Rights - Ownership by the contractor (May 2014)
#
# The software/firmware is provided to you on an As-Is basis
#
# Delivered to the U.S. Government with Unlimited Rights, as defined in DFARS Part 252.227-7013
# or 7014 (Feb 2014). Notwithstanding any copyright notice, U.S. Government rights in this work
# are defined by DFARS 252.227-7013 or DFARS 252.227-7014 as detailed above. Use of this work other
# than as specifically authorized by the U.S. Government may violate any copyrights that exist in
# this work.
###################################################################################################
*/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class camera_parameters : MonoBehaviour
{
    /*
     * This class holds camera parameters that are used
     * by the tesse_image_interface to satisfy user image
     * requests.
     * 
    */

    private UInt32 img_width = 0; // this is the image width in pixels set by the user via the interface
    private UInt32 img_height = 0; // this is the image height in pixels set by the user via the interface
    private string camera_name = null;

    // Use this for initialization
    void Start () {
        // default with the game screen resolution
        img_width = (UInt32)Screen.width;
        img_height = (UInt32)Screen.height;
        camera_name = transform.name; // set the camera's name based on the Unity object's name
	}

    public void set_camera_resolution( UInt32 width, UInt32 height )
    {
        // set the request image resolution of the camera
        img_width = width;
        img_height = height;
    }

    public UInt32 get_camera_width()
    {
        // return the currently set request image width, in pixels
        return img_width;
    }

    public UInt32 get_camera_height()
    {
        // return the currently set request image height, in pixels
        return img_height;
    }

    public string get_camera_name()
    {
        // return the camera's name
        return camera_name;
    }
}
