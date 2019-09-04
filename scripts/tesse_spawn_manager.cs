/*
DISTRIBUTION STATEMENT A. Approved for public release. Distribution is unlimited.

This material is based upon work supported by the Under Secretary of Defense for Research 
and Engineering under Air Force Contract No. FA8702-15-D-0001. Any opinions, findings, 
conclusions or recommendations expressed in this material are those of the author(s) and 
do not necessarily reflect the views of the Under Secretary of Defense for Research and 
Engineering.

© 2019 Massachusetts Institute of Technology.

MIT Proprietary, Subject to FAR52.227-11 Patent Rights - Ownership by the contractor (May 2014)

The software/firmware is provided to you on an As-Is basis

Delivered to the U.S. Government with Unlimited Rights, as defined in DFARS Part 252.227-7013 
or 7014 (Feb 2014). Notwithstanding any copyright notice, U.S. Government rights in this work
are defined by DFARS 252.227-7013 or DFARS 252.227-7014 as detailed above. Use of this work 
other than as specifically authorized by the U.S. Government may violate any copyrights that 
exist in this work.
*/

using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class tesse_spawn_manager : MonoBehaviour
{
    /*
         * This class implements the spawn manager for the TESSE agent.
         * It provides a reader for the _spawn_points.csv files created
         * by a user or during spawn point capture mode. It then uses
         * these points to provide valid spawn locations to the agent
         * on a respawn.
    */

    // spawn point capture mode flag
    private bool spawn_point_capture_mode = false;

    // list of spawn points for the scene
    private List<Vector3> spawn_points = new List<Vector3>();

    // file path for spawn points
    private string file_path = null;

    // create csv object
    private StringBuilder csv = new StringBuilder();

    private void Awake()
    {
        // set random seed
        Random.InitState((int)System.DateTime.Now.Ticks);

        // load spawn points from csv, if it exists
        load_spawn_points();
    }

    // Update is called once per frame
    void Update()
    {
        //NOTE: If spawn point capture mode is entered, the game must be closed properly
        //via the Escape (Esc) key.
        if( spawn_point_capture_mode ) // this mode can be entered via the keyboard
        {
            // capture a new spawn point every second
            csv.AppendLine(capture_spawn_point());

            // spawn point capture is active
            if ((Input.GetKey(KeyCode.LeftShift)) && (Input.GetKey(KeyCode.LeftControl)) && (Input.GetKeyDown(KeyCode.G)))
            {
                // exit spawn point capture mode
                spawn_point_capture_mode = false;

                // write captured data to file
                File.AppendAllText(file_path, csv.ToString());

                // clear string builder list
                csv.Clear();
            }
        }
        else // spawn point capture mode is inactive
        {
            // spawn point capture is not active
            if( (Input.GetKey(KeyCode.LeftShift)) && (Input.GetKey(KeyCode.LeftControl)) && (Input.GetKeyDown(KeyCode.G)) )
            {
                // enter spawn point capture mode
                spawn_point_capture_mode = true;

                // clear any points that are in csv string builder
                csv.Clear();
            }
        }
    }

    private void OnApplicationQuit()
    {
        // ensure that any spawn points that are currently cached get written to file
        if( (csv.Length > 0) && (file_path != null) )
        {
            File.AppendAllText(file_path, csv.ToString());
        }
    }

    IEnumerator wait_for_seconds( float wait_sec = 1.0f )
    {
        // convience function that waits for wait_sec number of
        //seconds to pass before continuing the function
        yield return new WaitForSeconds(wait_sec);
    }

    private string capture_spawn_point( float wait_time = 1.0f )
    {
        // wait for wait_time()
        StartCoroutine(wait_for_seconds(wait_time));

        // write current position of tesse agent to file
        string new_line = string.Format("{0},{1},{2}", transform.position.x, transform.position.y, transform.position.z);

        return new_line;
    }

    public Vector3 get_random_spawn_point( float radius = 2.0f)
    {
        // get point in random spawn list
        int idx = Random.Range(0, spawn_points.Count - 1);

        // provide random jitter to the chosen spawn point
        float offset = Random.Range(-radius, radius);
        float angle = Random.Range(0, 2*(float)System.Math.PI);

        // this will select the direction to apply the offset
        //based on the angle chosen
        float cx = offset * (float)System.Math.Cos(angle);
        float cz = offset * (float)System.Math.Sin(angle);

        // get new spawn position with random offset
        Vector3 new_pos = new Vector3(spawn_points[idx].x +cx, spawn_points[idx].y, spawn_points[idx].z + cz);

        return new_pos;
    }

    public void load_spawn_points(int scene_index=0)
    {
        // clear any currently loaded spawn points
        spawn_points.Clear();

        // get name of spawn points csv file
        file_path = Application.streamingAssetsPath + "/" + SceneManager.GetSceneByBuildIndex(scene_index).name + "_spawn_points.csv";

        // if a spawn points csv does not exist, unity will create a large number
        //of spawn points at random within the bounding volume of the scene. These
        //will probably not be very good, but a user can use them to find a good
        //starting point and then enter spawn point capture mode to generate a good
        //set of spawn points to save to the spawn points csv that will be used
        //the next time the scene is loaded.
        if (!File.Exists(file_path))
        {
            print(file_path);
            // randomly sample spawn points from a bounding volume
            generate_spawn_points_from_bounding_volume();
        }
        else
        {
            // load spawn points from spawn points csv file
            using (var reader = new StreamReader(file_path))
            {
                reader.ReadLine();
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine(); // read a line of the file
                    var values = line.Split(','); // csv is comma delimited

                    float x = 0, y = 0, z = 0;

                    // in the csv column 0 is the x position, column 1 is the y position, column 2 is the z position 
                    if (float.TryParse(values[0], out x) && float.TryParse(values[1], out y) && float.TryParse(values[2], out z))
                    {
                        spawn_points.Add(new Vector3(x, y, z)); // add this point to the internal spawn points list
                    }
                    else
                    {
                        print("failed to parse csv values!");
                    }
                }
            }
        }
    }

    private void generate_spawn_points_from_bounding_volume(int points_to_generate=20000)
    {
        // this is a fallback in case a scene doesn't have pre-generated spawn points
        //it is.... non-optimal
        // This function generates points_to_generate random points drawn from the total
        //bounding volume of the scene.
        var rends = FindObjectsOfType<Renderer>();
        if (rends.Length == 0)
        {
            // if the scene is empty, add a single (0, 0, 0) spawn point and return
            spawn_points.Add(Vector3.zero);
            return;
        }

        Bounds b = rends[0].bounds; // get total bounding volume of all objects in the scene

        for (int i = 1; i < rends.Length; ++i)
        {
            b.Encapsulate(rends[i].bounds);
        }

        // randomly sample 20000 points from the bounding volume
        for ( int i=0; i < points_to_generate; ++i )
        {
            float x = Random.Range( b.center.x - (b.extents.x/2), b.center.x + (b.extents.x/2) );
            float y = Random.Range( b.center.y - (b.extents.y/2), b.center.y + (b.extents.y/2) );
            float z = Random.Range( b.center.z - (b.extents.z/2), b.center.z + (b.extents.z/2) );
            spawn_points.Add(new Vector3(x, y, z));
        }

    }
}
