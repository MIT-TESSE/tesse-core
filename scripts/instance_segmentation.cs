/*
#**************************************************************************************************
# Distribution authorized to U.S. Government agencies and their contractors. Other requests for 
# this document shall be referred to the MIT Lincoln Laboratory Technology Office.
#
# This material is based upon work supported by the Under Secretary of Defense for Research and 
# Engineering under Air Force Contract No. FA8702-15-D-0001. Any opinions, findings, conclusions
# or recommendations expressed in this material are those of the author(s) and do not necessarily 
# reflect the views of the Under Secretary of Defense for Research and Engineering.
# 
# © 2019 Massachusetts Institute of Technology.
#
# The software/firmware is provided to you on an As-Is basis
#
# Delivered to the U.S. Government with Unlimited Rights, as defined in DFARS Part 252.227-7013 
# or 7014 (Feb 2014). Notwithstanding any copyright notice, U.S. Government rights in this work 
# are defined by DFARS 252.227-7013 or DFARS 252.227-7014 as detailed above. Use of this work other 
# than as specifically authorized by the U.S. Government may violate any copyrights that exist in 
# this work.
#**************************************************************************************************
*/

using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using System.Text;

public class instance_segmentation : MonoBehaviour 
{
    public Shader instance_seg_replacement_shader;
    private Camera cam; 
    private int current_scene_obj_idx = 0;


    void Awake()
    {
        if (!instance_seg_replacement_shader)
        {
            instance_seg_replacement_shader = Shader.Find("TESSE/TESSE_instance_segmentation");
        }

        cam = GetComponent<Camera>();
        init_segmentation_camera(cam, instance_seg_replacement_shader, 0, Color.white);
   		update_instance_segmentation_for_scene(0);
    }

    private void init_segmentation_camera( Camera cam, Shader shader, int mode, Color clear_color )
    {
        var cb = new CommandBuffer();

        cam.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, cb);
        cam.AddCommandBuffer(CameraEvent.BeforeFinalPass, cb); 
        cam.SetReplacementShader(shader, "SegObjID"); 
        cam.backgroundColor = clear_color; 
        cam.clearFlags = CameraClearFlags.SolidColor;
    }

    public void update_instance_segmentation_for_scene(int scene_index)
    {
        var renderers = Object.FindObjectsOfType<Renderer>();
        var mpb = new MaterialPropertyBlock();

        HashSet<string> rends = new HashSet<string>();

        foreach (var r in renderers)
        {
        	var go = r.gameObject;

            foreach( var mat in r.sharedMaterials )
            {
                if (mat != null)
                {
                    if ((!mat.name.ToLower().Contains("glass")) || (SceneManager.GetActiveScene().name.ToLower().Contains("windridge")))
                    {
                        mat.SetOverrideTag("SegObjID", "object");
                    }else
                    {
                        mat.SetOverrideTag("SegObjID", "");
                    }
                }
            }

            Color obj_color = get_object_segmentation_color_by_idx(go.GetInstanceID());
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_ObjectIDColor", obj_color);
                r.SetPropertyBlock(mpb);
            }
        }
    }

    public Color get_object_segmentation_color_by_idx(int idx)
    {
        current_scene_obj_idx += 1;
        List<byte> color = id_to_bytes(current_scene_obj_idx);
        byte r = color[0];
        byte g = color[1];
        byte b = color[2];
        return new Color(r / 255.0f, g / 255.0f, b / 255.0f , 0f);
    }

    public List<byte> id_to_bytes(int id)
    {
		/*
		* Turn an instance ID to a list of bytes for image encoding
		*/
		int r = id % 255;
		int g = id / 255;
		int b = id / (int)System.Math.Pow(255, 2);
		return new List<byte>() { (byte)r, (byte)g, (byte)b};
    }
}
