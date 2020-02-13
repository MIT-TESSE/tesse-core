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

using UnityEngine;
using UnityEngine.Rendering;

public class depth_camera_effect : MonoBehaviour
{ 
    /* 
     * This class provides the attached camera a depth rendering 
     * capability. It uses the depth shader to generate an image
     * that is the distance from the camera to the closest object
     * in the image.
     * 
    */

    public Shader depth_replacement_shader;
    private Camera cam;
    public Material mat;
    public float depth_level = 0.5f;

    private Texture2D tx;

    // Use this for initialization
    void Awake ()
    {
        // set the depth shader, use a default option if none is provided in the Unity Editor
        if (!depth_replacement_shader)
            depth_replacement_shader = Shader.Find("TESSE/TESSE_depth"); // default shader if it isn't specified

        cam = GetComponent<Camera>();

        // set the shader of the depth replacement material
        mat.shader = depth_replacement_shader;

        // set the depth mode to ensure a depth buffer is drawn
        cam.depthTextureMode = DepthTextureMode.Depth;

        tx = new Texture2D(Screen.width, Screen.height);
    }
    
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        mat.SetMatrix("_ViewProjectInverse", (cam.projectionMatrix * cam.worldToCameraMatrix).inverse);
        // replace the source texture with the depth texture
        Graphics.Blit(source, destination, mat);
    }

    private void init_depth_camera(Camera cam, Shader shader, int mode, Color clear_color)
    {
        var cb = new CommandBuffer();

        cam.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, cb); // set when this effect runs in the rendering pipeline; note that this effect requires the camera use the Foward rendering path
        cam.AddCommandBuffer(CameraEvent.BeforeFinalPass, cb); // same as above
        // the following ensures that only materials that are used by the segmentation shader are seen in the depth image;
        //helpful for glass and other transparent objects
        cam.SetReplacementShader(shader, "SegClassID"); // set our replacement shader; this can be changed to use the 'tag' method by setting the replacement tag to 'SegClassID'
        cam.backgroundColor = clear_color; // the color for areas with no objects
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.depthTextureMode = DepthTextureMode.Depth;
    }
}
