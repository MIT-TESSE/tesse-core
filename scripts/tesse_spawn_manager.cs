/*
 * #**************************************************************************************************
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
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using tesse;

[System.Serializable]
public class SpawnPoints
{
    public string name;
    public List<Vector3> points;

    public SpawnPoints(string objectName)
    {
        name = objectName;
        points = new List<Vector3>();
    }
}

[System.Serializable]
public class ListOfSpawnPoints
{
    public List<SpawnPoints> spawnPoints;

    public ListOfSpawnPoints(List<string> names)
    {
        spawnPoints = new List<SpawnPoints>();
        foreach (var name in names)
        {
            spawnPoints.Add(new SpawnPoints(name));
        }
    }

    public void AddIfMissing(List<string> names)
    {
        /* Add empty spawn points, if they were missing.
         * Useful when attempting to load from a file.
         */
        bool exists;
        foreach (var name in names)
        {
            exists = false;
            for (var i = 0; i < spawnPoints.Count; i++)
            {
                if (spawnPoints[i].name == name)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
                spawnPoints.Add(new SpawnPoints(name));
        }
    }
}

public class tesse_spawn_manager : MonoBehaviour
{
    /*
         * This class implements the spawn manager for the TESSE agent.
         * It provides a reader for the _spawn_points.csv files created
         * by a user or during spawn point capture mode. It then uses
         * these points to provide valid spawn locations to the agent
         * on a respawn.
    */
    
    private bool enteredSpawnPointCaptureMode = false; // spawn point capture mode flag
    private List<string> objects = new List<string>(); // list of all spawnable objects in scene

    private Bounds sceneBounds = new Bounds(); // bounds of the current scene
    private ListOfSpawnPoints sceneSpawnPoints; // spawn points for the current current
    private string sceneSpawnFile; // file with spawn points for the current scene

    private float startHoldOKeyTime = 0f;

    private void Start()
    {
        // set random seed
        Random.InitState((int)System.DateTime.Now.Ticks);

        tesse_position_interface position_interface = GetComponentInParent<tesse_position_interface>();
        objects.Add(position_interface.name);
        for (var i = 0; i < position_interface.spawnableObjects.Count; i++)
        {
            objects.Add(position_interface.spawnableObjects[i].name);
        }

        // load spawn points from csv, if it exists
        load_spawn_points();
    }

    // Update is called once per frame
    void Update()
    {
        // Determine if user enters spawn point mode. Note that you stay in this mode once entered.
        if (Input.GetKey(KeyCode.LeftShift) &&
            Input.GetKey(KeyCode.LeftControl) &&
            Input.GetKeyDown(KeyCode.G) &&
            !enteredSpawnPointCaptureMode)
        {
            print("You have entered spawn point mode.");
            enteredSpawnPointCaptureMode = true;
        }

        if (enteredSpawnPointCaptureMode)
        {
            /* Key code:
             * I - save current point
             * O - save points once per second
             * P - write points to file
             * K - delete previous point
             * L - delete all points
             * ; - load points from file
             */

            if (Input.GetKeyDown(KeyCode.O))
            {
                startHoldOKeyTime = Time.time;
            }

            if (Input.GetKeyDown(KeyCode.I) || (Input.GetKey(KeyCode.O) && Time.time - startHoldOKeyTime > 1.0))
            {                
                sceneSpawnPoints.spawnPoints[0].points.Add(transform.position);
                print("Added new point at " + sceneSpawnPoints.spawnPoints[0].points[sceneSpawnPoints.spawnPoints[0].points.Count - 1] +
                    " for " + sceneSpawnPoints.spawnPoints[0].points.Count + " total points.");
                startHoldOKeyTime = Time.time;
            }

            if (Input.GetKeyDown(KeyCode.P))
            {
                System.IO.File.WriteAllText(sceneSpawnFile, JsonUtility.ToJson(sceneSpawnPoints, true));
                print("Saved points to " + sceneSpawnFile + ".");
            }

            if (Input.GetKeyDown(KeyCode.K) && sceneSpawnPoints.spawnPoints[0].points.Count>0)
            {
                print("Removed last point at " + sceneSpawnPoints.spawnPoints[0].points[sceneSpawnPoints.spawnPoints[0].points.Count - 1] + ".");
                sceneSpawnPoints.spawnPoints[0].points.RemoveAt(sceneSpawnPoints.spawnPoints[0].points.Count - 1);
            }

            if (Input.GetKeyDown(KeyCode.L))
            {
                sceneSpawnPoints.spawnPoints[0].points.Clear();
                print("Cleared points.");
            }

            if (Input.GetKeyDown(KeyCode.Semicolon))
            {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(sceneSpawnFile), sceneSpawnPoints);
                sceneSpawnPoints.AddIfMissing(objects);
                print("Loaded points from " + sceneSpawnFile + ".");
            }
        }
    }

    public Vector3 get_random_spawn_point(string name = "", float radius = 2.0f)
    {
        if (name == "") 
            name = sceneSpawnPoints.spawnPoints[0].name; // default to first object in list (the "agent")

        foreach (var spawnPoints in sceneSpawnPoints.spawnPoints)
        {
            if (spawnPoints.name == name && spawnPoints.points.Count>0) // There are spawn points defined
            {
                int idx = Random.Range(0, spawnPoints.points.Count);
                Vector3 origin = spawnPoints.points[idx];

                // provide random jitter to the chosen spawn point
                Vector3 direction = new Vector3();
                float offset;
                float angle;
                do
                {
                    offset = Random.Range(0, radius);
                    angle = Random.Range(0, 2 * (float)System.Math.PI);

                    direction.x = offset * (float)System.Math.Cos(angle);
                    direction.z = offset * (float)System.Math.Sin(angle);
                } while (Physics.Raycast(origin, direction, offset)); // Only accept if there are no collisions between to the candidate point and the point from the spawn file

                return origin + direction;
            }
        }

        // Sample randomly from the scene bounds as a fallback
        return new Vector3(
            Random.Range(sceneBounds.min.x, sceneBounds.max.x),
            Random.Range(sceneBounds.min.y, sceneBounds.max.y),
            Random.Range(sceneBounds.min.z, sceneBounds.max.z)
            );

    }

    public void load_spawn_points(int scene_index = 0)
    {
        sceneSpawnFile = Application.streamingAssetsPath + Path.DirectorySeparatorChar + SceneManager.GetSceneByBuildIndex(scene_index).name + ".points";
        if (File.Exists(sceneSpawnFile))
        {
            JsonUtility.FromJsonOverwrite(File.ReadAllText(sceneSpawnFile), sceneSpawnPoints);
            sceneSpawnPoints.AddIfMissing(objects);
        }
        else
        {
            sceneSpawnPoints = new ListOfSpawnPoints(objects);
        }

        // this is a fallback in case a scene doesn't have pre-generated spawn points
        var renderers = FindObjectsOfType<Renderer>();
        sceneBounds = new Bounds();
        if (renderers.Length>0)
        {
            sceneBounds = renderers[0].bounds; // get total bounding volume of all objects in the scene

            for (int i = 1; i < renderers.Length; ++i)
            {
                sceneBounds.Encapsulate(renderers[i].bounds);
            }
        }
    }

}
