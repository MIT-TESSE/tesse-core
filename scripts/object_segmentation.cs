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

public class object_segmentation : MonoBehaviour {

    /*
     * This class enables object semantic segmenation labelling of a scene.
     * It uses a list of materials generated from the 
     * scenes and assigns them to classes. It replaces the 
     * materials/textures in those objects using the 'UberReplacement' shader.
    */

    public Shader seg_replacement_shader;

    private Dictionary<string, Color> color_mapping; // dictionary that holds mapping from class name to color

    private Camera cam; // camera that this script is attached to

    // Use this for initialization
    void Awake()
    {
        if (!seg_replacement_shader)
            seg_replacement_shader = Shader.Find("TESSE/TESSE_segmentation"); // default shader if it isn't specified

        cam = GetComponent<Camera>();

        init_segmentation_camera(cam, seg_replacement_shader, 0, Color.white);

        update_segmentation_for_scene(0); // update all the material tags of objects contained in the currently loaded scenes
    }

    private void parse_color_mapping_csv( string csv_path )
    {
        /*
         * This function parses the csv file that holds the mapping
         * from class labels to color. This file can be changed by a user
         * and the new values will be loaded at next start up of the game.
        */

        if ( color_mapping != null )
        {
            color_mapping.Clear();
        }

        color_mapping = new Dictionary<string, Color>();

        using (var reader = new StreamReader(csv_path))
        {
            reader.ReadLine();
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                var values = line.Split(',');

                int r = -1, g = -1, b = -1, a = -1;

                // in the csv column 0 is the class label, column 1 is the red value, column 2 is the green value and column 3 is the blue value
                if (int.TryParse(values[1], out r) && int.TryParse(values[2], out g) && int.TryParse(values[3], out b) && int.TryParse(values[4], out a) )
                {
                    color_mapping.Add(values[0], new Color32((byte)r, (byte)g, (byte)b, (byte)a));
                }
                else
                {
                    print("failed to parse csv values!");
                }
            }
        }
    }

    private void init_segmentation_camera( Camera cam, Shader shader, int mode, Color clear_color )
    {
        /*
         * This function initialized the camera for use in object segmentation.
         * It can generalize to be used for future effects as functionality is added to 
         * the shader suite.
        */

        var cb = new CommandBuffer();

        cam.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, cb); // set when this effect runs in the rendering pipeline; note that this effect requires the camera use the Foward rendering path
        cam.AddCommandBuffer(CameraEvent.BeforeFinalPass, cb); // same as above
        cam.SetReplacementShader(shader, "SegClassID"); // set our replacement shader; this can be changed to use the 'tag' method by setting the replacement tag to 'SegClassID'
        cam.backgroundColor = clear_color; // the color for areas with no objects
        cam.clearFlags = CameraClearFlags.SolidColor;
    }

    public void update_segmentation_for_scene(int scene_index)
    {
        /* 
         * This function finds all renderers in the scene and sets the _ObjectColor parameter in their material properties block
         * based on the class label color mapping provided in the segmentation_class_mapping.csv file.
        */

        // setup for objects
        var renderers = Object.FindObjectsOfType<Renderer>();
        var mpb = new MaterialPropertyBlock();

        HashSet<string> rends = new HashSet<string>();

        string file_path = Application.streamingAssetsPath + "/" + SceneManager.GetSceneByBuildIndex(scene_index).name + "_segmentation_mapping.csv";

        if (!File.Exists(file_path))
        {
            // create new mapping file if one does not already exist for this scene

            foreach (var r in renderers)
            {
                //print("render " + r.name);
                var idx = r.name.IndexOf(" ");
                if (idx != -1)
                    rends.Add(r.name.Substring(0, idx));
                else
                    rends.Add(r.name);
            }

            var csv = new StringBuilder();
            foreach (var r in rends)
            {
                int cr = Random.Range(0, 255);
                int cg = Random.Range(0, 255);
                int cb = Random.Range(0, 255);
                int ca = 255;

                var new_line = string.Format("{0},{1},{2},{3},{4}", r, cr, cg, cb, ca);
                csv.AppendLine(new_line);
            }

            File.WriteAllText(file_path, csv.ToString());
        }

        // assign colors to materials based on csv
        parse_color_mapping_csv(file_path);
        foreach (var r in renderers)
        {
            var idx = r.name.IndexOf(" ");
            string rend_name;
            if (idx != -1)
                rend_name = r.name.Substring(0, idx);
            else
                rend_name = r.name;
            
            // get the object color from the materials or game object used by this renderer
            Color obj_color = get_object_segmentation_color_by_name(rend_name);

            foreach( var mat in r.sharedMaterials )
            {
                if (mat != null)
                {
                    if ((!mat.name.ToLower().Contains("glass")) || (SceneManager.GetActiveScene().name.ToLower().Contains("windridge")))
                    {
                        mat.SetOverrideTag("SegClassID", "object");
                    }
                    else
                        mat.SetOverrideTag("SegClassID", "");
                }
            }

            {
                // ensure that we persist the values of the current properties block
                r.GetPropertyBlock(mpb);
                // set the color property; this is used by the UberReplacement shader
                mpb.SetColor("_ObjectColor", obj_color);

                r.SetPropertyBlock(mpb);
            }
        }
    }

    private Color get_object_segmentation_color_by_name(string name)
    {
        /* 
         * This function will look at the object name and compare it against the object_category mapping dictionary
         * If no match is found in the dictionary, it will look at all materials attached to the renderer and 
         * assign its color based on the material to class label mapping dictionary.
        */

        Color c = new Color();
        if( color_mapping.TryGetValue(name, out c) )
        {
            // found the color, return it
            return c;
        }
        return new Color(0f, 0f, 0f, 0f);
    }

}
