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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace tesse
{
    public class tesse_position_interface : tesse_base
    {
        /*
         * This class implements the interface to process position, movement,
         * spawn and scene change requests from a remote client.
         * It inherits from tesse_base which contains some common convenience
         * methods as well as the command line parser.
         * tesse_base itself inherits from the Unity Monobehavior class.
        */

        // port assignments from tesse command line parser
        private int pos_listen_port = 9000; // udp port to listen for requests
        private int step_listen_port = 9005; // tcp port for listening to step requests
        private int pos_update_port = 9003; // udp port to send position responses back to clients
        // NOTE: you can simultaneously use tcp and udp ports
        private int pos_send_port = 9000; // tcp port to send responses to clients

        // udp socket object for listener
        private UdpClient pos_request_client = null;
        private UdpClient pos_update_client = null;

        // tcp listener object for step requests
        private TcpListener step_request_listener = null;
        private TcpClient client = null;
        private NetworkStream stream = null;

        // client request ip
        private IPAddress pos_client_addr = null; // ip address of position request client

        // signal to unity update that teleport movement is requested
        private static bool teleport_flag = false;

        // signal to unity update that force movement is requested
        private static bool add_force_flag = false;

        // signal to unity update that position/orientation change is requested
        private static bool set_pos_quat = false;

        // signal to enable or disable agent's collider
        private bool set_collider = false;

        // variable to hold desired collider state
        private bool collider_state = false;

        // mutex lock for position requests
        private System.Object pos_request_lock = new System.Object();

        // data object to hold teleport command parameters
        private Vector3 teleport_cmd = new Vector3(0.0f, 0.0f,0.0f);

        // data object to hold force command parameters
        //private Vector2 force_cmd = new Vector2(0.0f, 0.0f);
        private Vector3 force_cmd = new Vector3(0.0f, 0.0f, 0.0f);

        // data object to hold set position and orientation command parameters
        private Vector3 pos_cmd = new Vector3(0.0f, 0.0f, 0.0f);
        private Quaternion rot_cmd = new Quaternion(0.0f, 0.0f, 0.0f, 0.0f);

        // thread for position request listener
        private Thread pos_request_thread;
        private Thread step_request_thread;

        // flag for thread running
        private bool pos_request_running = false;
        private bool step_request_running = false;

        // reference to hover controller
        private simple_tesse_hover_controller hover_controller = null;

        // Set random seed request
        private bool seed_flag = false;
        private int requested_seed = 0;

        // scene change request objects
        private int current_scene_index = 1; // index to current scene in the build order
        private int new_scene_index = -1; // requested scene to change to
        private bool change_scene_flag = false; // flag for unity update loop
        private string scene_name = null; // name of scene for client response
        private int num_scenes = -1; // number of scenes in the build

        // Object spawning numbers
        private bool spawn_object_flag = false;
        private SpawnObject requested_spawn_object;
        private int next_spawn_object_id = 0;
        private bool remove_spawned_objects = false;
        private List<int> spawned_objects_to_remove = new List<int>();
        private bool request_spawned_objects_info = false;
        private Dictionary<int, UnityEngine.GameObject> spawned_objects = new Dictionary<int, UnityEngine.GameObject>();
        public List<GameObject> spawnableObjects;
        private tesse_spawn_manager spawner; // controls agent spawning


        // reference to object segmentation
        private object_segmentation os = null;

        // reference to instance segmentation
        private instance_segmentation instance_seg = null;

        // stuff for capture rate testing
        private float cmd_time = 0f;
        private float exec_time = 0f;
        private float last_time = 0f;
        private int frame_rate = 0;
        private bool keyboard_state;

        // flag for signaling a change to capture frame rate
        bool mod_frame_rate_flag = false;
        // flag for signaling a change to time scale
        bool set_time_scale_flag = false;

        // Start is called before the first frame update
        new void Start()
        {
            base.Start();  // initialize common components using tesse_base.Start()

            // attach hover controller
            hover_controller = GetComponent<simple_tesse_hover_controller>();

            // setup ports from tesse command line parser
            pos_listen_port = cla_parser.pos_listen_port;
            pos_send_port = cla_parser.pos_send_port;
            pos_update_port = cla_parser.pos_update_port;
            step_listen_port = cla_parser.step_listen_port;

            // start position request listener thread
            pos_request_thread = new Thread(() => pos_request_listener());
            pos_request_thread.Start();
            pos_request_running = true;

            // start step request listener thread
            step_request_thread = new Thread(() => step_request_server());
            step_request_thread.Start();
            step_request_running = true;

            // create udp socket object for telemetry broadcast
            pos_update_client = new UdpClient(pos_update_port);

            // get object segmentation object
            os = GetComponentInChildren<object_segmentation>();

            // get instance segmentation object
            instance_seg = GetComponentInChildren<instance_segmentation>();

            // get number of scenes in the build
            num_scenes = SceneManager.sceneCountInBuildSettings;

            // load first scene
            print("Loading initial scene...");
            SceneManager.LoadScene(current_scene_index, LoadSceneMode.Additive); // load new scene around tesse agent

            // update object segmentation for this new scene
            StartCoroutine(update_segmentation());

            // get name of this scene
            scene_name = SceneManager.GetSceneByBuildIndex(current_scene_index).name;
        }

        void Awake()
        {
            // setup various object parameters
            spawner = GetComponent<tesse_spawn_manager>();
        }

        // Update is called once per frame
        void Update()
        {
            lock(pos_request_lock)
            {
                if( set_collider ) // collider state change
                {
                    GetComponent<CapsuleCollider>().enabled = collider_state;
                    set_collider = false;
                }

                if (mod_frame_rate_flag && (cmd_time == 0f) ) // only change if a previous command isn't executing
                {
                    Time.captureFramerate = frame_rate;
                    mod_frame_rate_flag = false;
                    stc.keyboard_active = true; // re-activate keyboard control

                    if (Time.captureFramerate == 0 && Time.timeScale == 0f)
                        Time.timeScale = 1f;
                    else if (Time.captureFramerate > 0 && Time.timeScale == 1f)
                        Time.timeScale = 0f;
                }

                if (set_time_scale_flag && (exec_time == 0f)) // only change if no commands are being executed
                {
                    //Time.timeScale = 1f;

                    if ((Time.captureFramerate > 0) && (Time.timeScale == 0f))
                    {
                        Time.timeScale = 1f;
                    }
                    else if ((Time.captureFramerate > 0) && (Time.timeScale == 1f))
                    {
                        Time.timeScale = 0f;
                    }

                    set_time_scale_flag = false;
                }

                if (!add_force_flag) // reset force
                {
                    // this is done to ensure that any force commands are applied from the Update() where they were
                    //recieved until the next Update(); this ensures smooth dynamics and reduces impulse effects
                    //on the simulated IMU
                    force_cmd = Vector3.zero;
                }

                // note: only a single teleport, force movement OR set position/orientation
                //can be requested in a single frame
                if (teleport_flag) // teleport move requested
                {
                    // move the agent by the requested amount in the x and z directions, if the path is collision free;
                    // Otherwise, move until the point of collision
                    Vector3 theMove = agent_rigid_body.transform.TransformVector(new Vector3(teleport_cmd.x, 0.0f, teleport_cmd.y));
                    RaycastHit hit;
                    if (Physics.Raycast(agent_rigid_body.transform.position, theMove, out hit, theMove.magnitude))
                        theMove = Vector3.ClampMagnitude(theMove, hit.distance);

                    agent_rigid_body.transform.Translate(theMove, Space.World);
                    agent_rigid_body.transform.Rotate(new Vector3(0.0f, teleport_cmd.z, 0.0f)); // rotation the agent around its y axis by the requested amount

                    // reset flag to thread
                    teleport_flag = false;
                } else if( add_force_flag ) // force move requested
                {
                    // the actual force updates are in FixedUpdate()w
                    // reset flag to thread
                    add_force_flag = false;
                } else if( set_pos_quat ) // set position/orientation requested
                {
                    // this command directly sets the agent's position
                    //and orientation, cancelling out any physics-based movements
                    //this is useful if the user desires to use
                    //an external physics system, like Gazebo

                    // set agent's position in unity (x,y,z)
                    agent_rigid_body.transform.position = pos_cmd;
                    // set agent's position in unity quaternion
                    // note: Unity uses left handed convention
                    //-x is left, +z is forward and +y is up
                    agent_rigid_body.transform.rotation = rot_cmd;

                    // ensure no residual momentum moves the agent
                    agent_rigid_body.velocity = new Vector3(0f, 0f, 0f);
                    agent_rigid_body.angularVelocity = new Vector3(0f, 0f, 0f);

                    // reset the command flag
                    set_pos_quat = false;
                }

                // Set random number seed
                if (seed_flag)
                {
                    UnityEngine.Random.InitState(requested_seed);
                    seed_flag = false;
                }

                // change of scene requested; this agent supports having multiple scenes
                //the 'root' scene contains only the TESSE agent and then the environment
                //is additively loaded around the agent
                //scenes are identified by their index in the Unity build order
                if( change_scene_flag )
                {
                    // unload current scene
                    //this is done in a coroutine to ensure that the current environment
                    //is completely unloaded before beginning to load the new environment
                    //Unity unloads and loads scenes asychronously
                    StartCoroutine(unload_current_scene());
                    // set new scene index taken from the build order of the scene
                    current_scene_index = new_scene_index;

                    print("loading new scene...");
                    // load new scene additively
                    SceneManager.LoadScene(current_scene_index, LoadSceneMode.Additive);
                    // wait for the new scene to be loaded and then update the material property
                    //tags for all materials in the scene to ensure they work with the
                    //segmentation shader
                    StartCoroutine(update_segmentation());

                    // cache the name of the scene, retreived by querying based on build index
                    scene_name = SceneManager.GetSceneByBuildIndex(current_scene_index).name;

                    // Unload all unused assets
                    Resources.UnloadUnusedAssets();

                    spawned_objects.Clear(); // Clear out list of spawned objects

                    // send a response back to the user containing metadata on the newly
                    //loaded scene, this is sent via TCP
                    send_scene_response(true);

                    // reset the command flag
                    change_scene_flag = false;
                }

                // Spawn objects into scene
                if( spawn_object_flag )
                {
                    if (remove_spawned_objects)
                    { // remove spawned objects
                        if (spawned_objects_to_remove.Count == 0)
                        {  // Remove all objects
                            foreach (var spawned_object in spawned_objects.Values)
                                Destroy(spawned_object);

                            spawned_objects.Clear();
                        } else
                        { // Remove requested objects
                            foreach (var id in spawned_objects_to_remove)
                            {
                                UnityEngine.GameObject spawned_object;
                                if (spawned_objects.TryGetValue(id, out spawned_object))
                                {
                                    Destroy(spawned_object);
                                    spawned_objects.Remove(id);
                                }
                            }
                        }

                        remove_spawned_objects = false;
                    }
                    else if (request_spawned_objects_info)
                    { // just return information on spawned objects
                        request_spawned_objects_info = false;
                    }
                    else if (requested_spawn_object.index < spawnableObjects.Count)
                    { // Spawn in a new objects
                        GameObject new_object;

                        if (requested_spawn_object.method == 0)
                        { // add requested object at user location
                           new_object = Instantiate(spawnableObjects[requested_spawn_object.index],
                                requested_spawn_object.position,
                                requested_spawn_object.orientation);
                              new_object.name = smplMaleObject.name;
                              break;
                          }
                        } else { // add requested object at random spawn point
                           new_object = Instantiate(spawnableObjects[requested_spawn_object.index],
                               spawner.get_random_spawn_point(spawnableObjects[requested_spawn_object.index].name),
                               UnityEngine.Random.rotation);
                        }

                        new_object.name = spawnableObjects[requested_spawn_object.index].name;
                        spawned_objects.Add(next_spawn_object_id++, new_object);  // Add to dictionary of spawned objects

                        // Update semantic segmentation
                        Renderer r = new_object.GetComponent(typeof(Renderer)) as Renderer;
                        Color obj_color = os.get_object_segmentation_color_by_name(new_object.name);
                        var mpb = new MaterialPropertyBlock();
                        r.GetPropertyBlock(mpb); // ensure that we persist the values of the current properties block
                        mpb.SetColor("_ObjectColor", obj_color); // set the color property; this is used by the UberReplacement shader
                        r.SetPropertyBlock(mpb);
                    }
                    else
                    {
                        print("Unable to spawn object at index " + requested_spawn_object.index + ". Number of spawnable objects is " + spawnableObjects.Count + ".");
                    }
 
                    send_objects(); // send a response with list of objects
                    spawn_object_flag = false; // reset the command flag
                }
            }
        }

        private void FixedUpdate()
        {
            lock (pos_request_lock)
            {
                // check for fixed frame rate mode, and test if the command has executed for the appropriate amount of time
                // we also look to see if the amount of time that has elsapsed is the requested time (cmd_time) less a single
                //physics update, this ensures that an addition Update() will not be called and the user specified command time
                //duration will be respected
                if ( (cmd_time != 0f) && ((exec_time + (System.Convert.ToSingle(Time.fixedDeltaTime) * 1.5f)) >= cmd_time) && (Time.captureFramerate > 0) )
                {
                    force_cmd = Vector3.zero;
                    exec_time = 0f;
                    cmd_time = 0f;
                    stc.cmd_time = 0f; // reset keyboard command duration
                    stc.keyboard_active = keyboard_state; // return keyboard movement control to original state
                    Time.timeScale = 0f;

                    if (client != null && stream != null)
                    {
                        byte[] msg = System.Text.Encoding.ASCII.GetBytes("ack");
                        stream.Write(msg, 0, msg.Length);
                        client.Close();
                        stream.Close();
                    }

                }
                else if (Time.timeScale == 1 && Time.captureFramerate > 0) // add to the execution timer and continue to execute the action
                {
                    exec_time += Time.time - last_time;
                }
                else if( !add_force_flag ) // fixed frame rate mode is inactive, reset force
                {
                    // this is done to ensure that any force commands are applied from the Update() where they were
                    //recieved until the next Update(); this ensures smooth dynamics and reduces impulse effects
                    //on the simulated IMU
                    force_cmd = Vector3.zero;
                }
                last_time = Time.time;

                // add force to the agent's z and x axes by amount requested
                agent_rigid_body.AddRelativeForce(force_cmd.z, 0.0f, force_cmd.x);
                // add torque about the agent's y axis by amount requested
                agent_rigid_body.AddRelativeTorque(0.0f, force_cmd.y, 0.0f);

                // this is the force telemetry broadcast function
                //when uncommented, it will broadcast all force inputs
                //from the keyboard via udp on pos_update_port
                //if( stc.keyboard_active ) // only broadcast telemetry when the keyboard is active
                //    send_telemetry(true);
            }
        }

        void OnApplicationQuit()
        {
            // ensure position udp listener thread is stopped
            pos_request_running = false;

            // if the position udp listener thread is still running
            //abort the thead
            if( (pos_request_thread != null) && (pos_request_thread.IsAlive) )
            {
                pos_request_thread.Abort();
            }
        }
        private void pos_request_listener()
        {
            // this function listens on the prescribed UDP port for
            //commands sent by the TESSE interface using the TESSE protocol
            //this function runs in a dedicated thread, seperate from the Unity
            //engine thread(s)
            print("position request listener thread started");

            // instaniate Udp listener
            pos_request_client = new UdpClient(pos_listen_port);

            IPEndPoint pos_request_ip = null;
            try
            {
                // listen on listen_port from any ip address
                pos_request_ip = new IPEndPoint(IPAddress.Any, pos_listen_port);
            }
            catch (SocketException ex)
            {
                print("pos_listen_port = " + pos_listen_port);
                Debug.Log("Socket Exception! Details: " + ex.Message);
                Debug.Log("Exiting... A position listener is required.");
                Application.Quit();
            }

            // check to ensure the thread should continue running
            while (pos_request_running)
            {
                // poll port to see if data is available
                if (pos_request_client.Client.Poll(1000, SelectMode.SelectRead))
                {
                    // get data from udp port
                    byte[] data = pos_request_client.Receive(ref pos_request_ip);

                    // process data
                    if ((data.Length == 16) && (System.Convert.ToChar(data[0]) == 'T') && (System.Convert.ToChar(data[1]) == 'L')
                        && (System.Convert.ToChar(data[2]) == 'P') && (System.Convert.ToChar(data[3]) == 'T'))
                    {
                        // process teleport request
                        lock (pos_request_lock)
                        {
                            if (frame_rate == 0) // only accept this command if fixed capture mode is inactive
                            {
                                teleport_cmd.x = System.BitConverter.ToSingle(data, 4); // delta x
                                teleport_cmd.y = System.BitConverter.ToSingle(data, 8); // delta z
                                teleport_cmd.z = System.BitConverter.ToSingle(data, 12); // delta theta around y axis
                                teleport_flag = true; // set flag to signal command to Unity Update() thread
                            }
                        }
                    }
                    else if ((data.Length == 16) && (System.Convert.ToChar(data[0]) == 't') && (System.Convert.ToChar(data[1]) == 'l')
                        && (System.Convert.ToChar(data[2]) == 'p') && (System.Convert.ToChar(data[3]) == 't'))
                    {
                        // process teleport request
                        lock (pos_request_lock)
                        {
                            // we keep the fixed capture mode commands and real-time mode commands different
                            //to ensure that the user knows the mode of the simulation
                            if (frame_rate > 0) // only accept this command if fixed capture mode is active
                            {
                                teleport_cmd.x = System.BitConverter.ToSingle(data, 4); // delta x
                                teleport_cmd.y = System.BitConverter.ToSingle(data, 8); // delta z
                                teleport_cmd.z = System.BitConverter.ToSingle(data, 12); // delta theta around y axis
                                teleport_flag = true; // set flag to signal command to Unity Update() thread
                            }
                        }
                    }
                    else if ((data.Length == 12) && (System.Convert.ToChar(data[0]) == 'x') && (System.Convert.ToChar(data[1]) == 'B')
                      && (System.Convert.ToChar(data[2]) == 'F') && (System.Convert.ToChar(data[3]) == 'F'))
                    {
                        // process two axis add force request
                        lock (pos_request_lock)
                        {
                            if (frame_rate == 0) // only accept this command if fixed capture mode is inactive
                            {
                                force_cmd.x = System.BitConverter.ToSingle(data, 4);  // Force on Z axis
                                force_cmd.y = System.BitConverter.ToSingle(data, 8);  // Torque around Y axis
                                force_cmd.z = 0; // no x force provided
                                add_force_flag = true; // set flag to signal command to Unity Update() thread
                            }

                        }
                    }
                    else if ((data.Length == 20) && (System.Convert.ToChar(data[0]) == 'f') && (System.Convert.ToChar(data[1]) == 'B')
                      && (System.Convert.ToChar(data[2]) == 'f') && (System.Convert.ToChar(data[3]) == 'f'))
                    {
                        // process add force request with fixed capture mode for the given duration in game seconds
                        lock (pos_request_lock)
                        {
                            if (cmd_time == 0 && frame_rate > 0 && !mod_frame_rate_flag) // only accept the request if no command is currently in process
                            {
                                force_cmd.x = System.BitConverter.ToSingle(data, 4);  // Force on Z axis
                                force_cmd.y = System.BitConverter.ToSingle(data, 8);  // Torque around Y axis
                                force_cmd.z = System.BitConverter.ToSingle(data, 12); // Force on X axis (strafe)

                                // we keep the fixed capture mode commands and real-time mode commands different
                                //to ensure that the user knows the mode of the simulation
                                if (frame_rate > 0) // ensure fixed frame rate mode is active
                                {
                                    // ** the following line is in place to support variable time execution if fixed capture mode
                                    //cmd_time = System.BitConverter.ToSingle(data, 16); // time to run command, in game seconds
                                    // **
                                    cmd_time = 1f / System.Convert.ToSingle(frame_rate); // execute the command for a single frame duration
                                    stc.cmd_time = cmd_time; // set keyboard controller time to sync with user requested time
                                    keyboard_state = stc.keyboard_active; // cache current state of keyboard control
                                    stc.keyboard_active = false; // disable keyboard movement for duration of the command
                                    exec_time = 0; // reset execution time
                                    set_time_scale_flag = true; // signal that time should be resumed to execute the action
                                }
                                add_force_flag = true; // flag to signal command to Unity Update() thread
                            }
                        }
                    }
                    else if ((data.Length == 8) && (System.Convert.ToChar(data[0]) == 'f') && (System.Convert.ToChar(data[1]) == 'S')
                      && (System.Convert.ToChar(data[2]) == 'c') && (System.Convert.ToChar(data[3]) == 'R'))
                    {
                        // set capture rate request received
                        lock (pos_request_lock)
                        {
                            if (cmd_time == 0 && !add_force_flag)
                            {
                                frame_rate = System.BitConverter.ToInt32(data, 4); // set frame rate provided by user
                                                                                   //NOTE: a frame rate of '0' denotes real-time mode, any positive integer will fix the game
                                                                                   //time to run at 1/frame_rate per frame, regardless of how much wall time is required for
                                                                                   //those frames to be rendered by the machine (e.g. my computer can run 60 fps, but the frame_rate
                                                                                   //is set to 30, therefore 1 second of game time is 0.5 seconds of wall time
                                mod_frame_rate_flag = true; // flag to signal command to Unity Update() thread
                                stc.keyboard_active = false; // disable keyboard control
                            }
                        }
                    }
                    else if ((data.Length == 16) && (System.Convert.ToChar(data[0]) == 'x') && (System.Convert.ToChar(data[1]) == 'B')
                      && (System.Convert.ToChar(data[2]) == 'f') && (System.Convert.ToChar(data[3]) == 'f'))
                    {
                        // process add three axis force request
                        lock (pos_request_lock)
                        {
                            if (frame_rate == 0) // only accept this command if fixed capture mode is inactive
                            {
                                force_cmd.x = System.BitConverter.ToSingle(data, 4);  // Force on Z axis
                                force_cmd.y = System.BitConverter.ToSingle(data, 8);  // Torque around Y axis
                                force_cmd.z = System.BitConverter.ToSingle(data, 12); // Force on X axis
                                add_force_flag = true; // flag to signal command to Unity Update() thread
                            }
                        }
                    }
                    else if ((data.Length == 8) && (System.Convert.ToChar(data[0]) == 'x') && (System.Convert.ToChar(data[1]) == 'S')
                      && (System.Convert.ToChar(data[2]) == 'H') && (System.Convert.ToChar(data[3]) == 'h'))
                    {
                        // process set hover height request
                        //this command changes the target height used by the tesse_hover_controller
                        lock (pos_request_lock)
                        {
                            hover_controller.hover_height = System.BitConverter.ToSingle(data, 4);  // Hover height
                                                                                                    // this doesn't interact with the Unity Update() thread, so no flag is required
                        }
                    }

                    else if ((data.Length == 32) && (System.Convert.ToChar(data[0]) == 's') && (System.Convert.ToChar(data[1]) == 'P')
                      && (System.Convert.ToChar(data[2]) == 'o') && (System.Convert.ToChar(data[3]) == 'S'))
                    {
                        // process set position and orientation request
                        //the coordinates are provided in Unity coordinate frame
                        //and Unity rotation convention (left-handed, +y is up, +z is forward, +x is right)
                        //NOTE: this command can be used even when the game is paused in fixed frame rate mode
                        lock (pos_request_lock)
                        {
                            pos_cmd.x = System.BitConverter.ToSingle(data, 4); // x position (right)
                            pos_cmd.y = System.BitConverter.ToSingle(data, 8); // y position (up)
                            pos_cmd.z = System.BitConverter.ToSingle(data, 12); // z position (forward)
                            rot_cmd.x = System.BitConverter.ToSingle(data, 16); // quaternion x
                            rot_cmd.y = System.BitConverter.ToSingle(data, 20); // quaternion y
                            rot_cmd.z = System.BitConverter.ToSingle(data, 24); // quaternion z
                            rot_cmd.w = System.BitConverter.ToSingle(data, 28); // quaternion w

                            set_pos_quat = true; // flag to signal command to Unity Update() thread
                        }
                    }
                    else if ((data.Length == 4) && (System.Convert.ToChar(data[0]) == 'R') && (System.Convert.ToChar(data[1]) == 'S')
                      && (System.Convert.ToChar(data[2]) == 'P') && (System.Convert.ToChar(data[3]) == 'N'))
                    {
                        // respawn agent request received
                        stc.respawn_agent();
                    }
                    else if ((data.Length == 5) && (System.Convert.ToChar(data[0]) == 's') && (System.Convert.ToChar(data[1]) == 'C')
                      && (System.Convert.ToChar(data[2]) == 'O') && (System.Convert.ToChar(data[3]) == 'L'))
                    {
                        // agent collider state request received
                        lock (pos_request_lock)
                        {
                            set_collider = true;
                            if (System.BitConverter.ToBoolean(data, 4))
                            {
                                collider_state = true;
                            }
                            else
                            {
                                collider_state = false;
                            }
                        }
                    }
                    else if ((data.Length == 8) && (System.Convert.ToChar(data[0]) == 'C') && (System.Convert.ToChar(data[1]) == 'S')
                      && (System.Convert.ToChar(data[2]) == 'c') && (System.Convert.ToChar(data[3]) == 'N'))
                    {
                        // change scene request received
                        // ensure requested index is valid
                        int index = System.BitConverter.ToInt32(data, 4); // requested scene index
                        if (index <= num_scenes)
                        {
                            lock (pos_request_lock)
                            {
                                new_scene_index = index; // set new scene index
                                change_scene_flag = true; // flag to signal command to Unity Update() thread
                                pos_client_addr = pos_request_ip.Address; // set requester's ip address, for confirmation message
                            }
                        }
                        else
                        {
                            // invalid index requested, respond with fail message, this will give the user a valid
                            //set of scene indexes that can be chosen
                            send_scene_response(false);
                        }

                    }
                    else if ((data.Length == 8) && (System.Convert.ToChar(data[0]) == 'S') && (System.Convert.ToChar(data[1]) == 'E')
                      && (System.Convert.ToChar(data[2]) == 'E') && (System.Convert.ToChar(data[3]) == 'D'))
                    {
                        lock (pos_request_lock)
                        {
                            seed_flag = true;
                            requested_seed = System.BitConverter.ToInt32(data, 4);
                        }
                    }
                    else if ((data.Length == 40) && (System.Convert.ToChar(data[0]) == 'o') && (System.Convert.ToChar(data[1]) == 'S')
                      && (System.Convert.ToChar(data[2]) == 'p') && (System.Convert.ToChar(data[3]) == 'n'))
                    {
                        // Spawn a new game object into the scene
                        //NOTE: this command can be used even when the game is paused in fixed frame rate mode
                        lock (pos_request_lock)
                        {
                            requested_spawn_object.index = System.BitConverter.ToInt32(data, 4);
                            requested_spawn_object.method = System.BitConverter.ToInt32(data, 8);
                            requested_spawn_object.position.x = System.BitConverter.ToSingle(data, 12); // x position (right)
                            requested_spawn_object.position.y = System.BitConverter.ToSingle(data, 16); // y position (up)
                            requested_spawn_object.position.z = System.BitConverter.ToSingle(data, 20); // z position (forward)
                            requested_spawn_object.orientation.x = System.BitConverter.ToSingle(data, 24); // quaternion x
                            requested_spawn_object.orientation.y = System.BitConverter.ToSingle(data, 28); // quaternion y
                            requested_spawn_object.orientation.z = System.BitConverter.ToSingle(data, 32); // quaternion z
                            requested_spawn_object.orientation.w = System.BitConverter.ToSingle(data, 36); // quaternion w

                            spawn_object_flag = true; // flag to signal command to Unity Update() thread
                            pos_client_addr = pos_request_ip.Address; // set requester's ip address, for confirmation message
                        }

                    }
                    else if ((data.Length >= 4) && (System.Convert.ToChar(data[0]) == 'o') && (System.Convert.ToChar(data[1]) == 'R')
                     && (System.Convert.ToChar(data[2]) == 'e') && (System.Convert.ToChar(data[3]) == 'm'))
                    {
                        // Remove all spawned objects
                        lock (pos_request_lock)
                        {
                            remove_spawned_objects = true;
                            spawned_objects_to_remove.Clear();
                            for (int idx = 4; idx < data.Length; idx += 4)
                                spawned_objects_to_remove.Add(System.BitConverter.ToInt32(data, idx));

                            spawn_object_flag = true; // flag to signal command to Unity Update() thread
                            pos_client_addr = pos_request_ip.Address; // set requester's ip address, for confirmation message
                        }
                    }
                    else if ((data.Length == 4) && (System.Convert.ToChar(data[0]) == 'o') && (System.Convert.ToChar(data[1]) == 'R')
                    && (System.Convert.ToChar(data[2]) == 'e') && (System.Convert.ToChar(data[3]) == 'q'))
                    {
                        // Remove all spawned objects
                        lock (pos_request_lock)
                        {
                            request_spawned_objects_info = true;

                            spawn_object_flag = true; // flag to signal command to Unity Update() thread
                            pos_client_addr = pos_request_ip.Address; // set requester's ip address, for confirmation message
                        }
                    }
                }
            }
        }

        private void step_request_server()
        {
            // this function listens on the prescribed TCP port for
            //commands sent by the TESSE interface using the TESSE protocol
            //this function runs in a dedicated thread, seperate from the Unity
            //engine thread(s)
            // the listener is implemented as tcp to ensure step commands are
            //received from the user, this also allows for a response to be sent
            //back to the user as a blocking listen which is ideal for the
            //fixed capture mode where a user requests an action for a specific duration
            //and wants to be notified when the duration is completed
            print("step request listener thread started");

            try
            {
                // instaniate tcp listener
                step_request_listener = new TcpListener(IPAddress.Any, step_listen_port);
            }
            catch (SocketException ex)
            {
                print("step_listen_port = " + step_listen_port);
                Debug.Log("Socket Exception! Details: " + ex.Message);
                Debug.Log("Exiting... A step listener is required.");
                Application.Quit();
            }
            // start tcp listener
            step_request_listener.Start();

            // check to ensure the thread should continue running
            while (step_request_running)
            {
                // accept connection from client
                client = step_request_listener.AcceptTcpClient();

                // here we setup the data buffer for the connection,
                //currently the max protocol length for this listener
                //is 20 bytes
                byte[] data = new byte[20];
                stream = client.GetStream(); // fetch the NetworkStream for this connection

                stream.Read(data, 0, data.Length); // read the data from the connection to the buffer

                // check to see if the buffer contains a fixed capture mode force request
                if ((System.Convert.ToChar(data[0]) == 'f') && (System.Convert.ToChar(data[1]) == 'B')
                  && (System.Convert.ToChar(data[2]) == 'f') && (System.Convert.ToChar(data[3]) == 'f'))
                {
                    // process add force request with fixed capture mode for the given duration in game seconds
                    lock (pos_request_lock)
                    {
                        if (cmd_time == 0 && frame_rate > 0 && !mod_frame_rate_flag) // only accept the request if no command is currently in process
                        {
                            force_cmd.x = System.BitConverter.ToSingle(data, 4);  // Force on Z axis
                            force_cmd.y = System.BitConverter.ToSingle(data, 8);  // Torque around Y axis
                            force_cmd.z = System.BitConverter.ToSingle(data, 12); // Force on X axis (strafe)
                            if (frame_rate > 0) // ensure fixed frame rate mode is active
                            {

                                // ** the following line is in place to support variable time execution if fixed capture mode
                                //cmd_time = System.BitConverter.ToSingle(data, 16); // time to run command, in game seconds
                                // **
                                cmd_time = 1f / System.Convert.ToSingle(frame_rate); // execute the action for a single frame duration
                                stc.cmd_time = cmd_time; // set keyboard controller time to sync with user requested time
                                keyboard_state = stc.keyboard_active; // cache current state of keyboard control
                                stc.keyboard_active = false; // disable keyboard movement for duration of the command
                                exec_time = 0; // reset execution time
                                set_time_scale_flag = true; // signal that time should be resumed to execute the action
                            }
                            add_force_flag = true; // flag to signal command to Unity Update() thread
                        }
                    }
                }
                else if ((System.Convert.ToChar(data[0]) == 't') && (System.Convert.ToChar(data[1]) == 'l')
                        && (System.Convert.ToChar(data[2]) == 'p') && (System.Convert.ToChar(data[3]) == 't'))
                {
                    // process teleport request
                    lock (pos_request_lock)
                    {
                        // we keep the fixed capture mode commands and real-time mode commands different
                        //to ensure that the user knows the mode of the simulation
                        if (frame_rate > 0) // only accept this command if fixed capture mode is active
                        {
                            cmd_time = 1f / System.Convert.ToSingle(frame_rate); // execute the action for a single frame duration
                            stc.cmd_time = cmd_time; // set keyboard controller time to sync with user requested time
                            keyboard_state = stc.keyboard_active; // cache current state of keyboard control
                            stc.keyboard_active = false; // disable keyboard movement for duration of the command
                            exec_time = 0; // reset execution time
                            set_time_scale_flag = true; // signal that time should be resumed to execute the action

                            teleport_cmd.x = System.BitConverter.ToSingle(data, 4); // delta x
                            teleport_cmd.y = System.BitConverter.ToSingle(data, 8); // delta z
                            teleport_cmd.z = System.BitConverter.ToSingle(data, 12); // delta theta around y axis
                            teleport_flag = true; // set flag to signal command to Unity Update() thread
                        }
                    }
                }
                else if ((System.Convert.ToChar(data[0]) == 'f') && (System.Convert.ToChar(data[1]) == 'S')
                  && (System.Convert.ToChar(data[2]) == 'c') && (System.Convert.ToChar(data[3]) == 'R'))
                {
                    // set capture rate request received
                    lock (pos_request_lock)
                    {
                        if (cmd_time == 0 && !add_force_flag) // ensure no commands are currently being processed
                        {
                            print("execution time is " + exec_time);
                            frame_rate = System.BitConverter.ToInt32(data, 4); // set frame rate provided by user
                                                                               //NOTE: a frame rate of '0' denotes real-time mode, any positive integer will fix the game
                                                                               //time to run at 1/frame_rate per frame, regardless of how much wall time is required for
                                                                               //those frames to be rendered by the machine (e.g. my computer can run 60 fps, but the frame_rate
                                                                               //is set to 30, therefore 1 second of game time is 0.5 seconds of wall time
                            mod_frame_rate_flag = true; // flag to signal command to Unity Update() thread
                            stc.keyboard_active = false; // disable keyboard control
                        }
                    }
                }
            }
        }

        private void send_scene_response(bool valid = true)
        {
            /*
             * This function sends a response to the client after requesting a scene
             * change, or if a client requests a scene index that does not exist
             * for the current game build.
            */

            string response;
            if (valid)
            {
                response = "<current_scene>\n";
                response += "  <index>" + current_scene_index + "</index>\n";
                response += "  <name>" + scene_name + "</name>\n";
                response += "</current_scene>\n";
            }
            else
            {
                response = "<available scene indices are 1 - " + (num_scenes - 1) + "/>\n";
            }

            byte[] sinfo = Encoding.ASCII.GetBytes(response);

            // create metadata payload header
            byte[] tag = Encoding.ASCII.GetBytes("scni");
            System.UInt32 image_tag = System.BitConverter.ToUInt32(tag, 0);
            System.UInt32 res_data_length = (System.UInt32)sinfo.Length;

            System.UInt32[] uint_header = new System.UInt32[] { image_tag, res_data_length };

            byte[] p_header = new byte[uint_header.Length * sizeof(System.UInt32)];
            System.Buffer.BlockCopy(uint_header, 0, p_header, 0, uint_header.Length * sizeof(System.UInt32));

            // send image data back to client via tcp
            IPEndPoint client_ep = new IPEndPoint(pos_client_addr, pos_send_port);

            Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.NoDelay = true;
            client.SendTimeout = 1000;
            client.ReceiveTimeout = 500;

            // connect to client
            try
            {
                client.Connect(client_ep);
                while (!client.Connected)
                {
                    Debug.Log("could not connect to client, retrying...\n");
                    Thread.Sleep(500);
                    client.Connect(client_ep);
                }
            }
            catch (SocketException ex)
            {
                Debug.Log("Socket Exception! Details: " + ex.Message);
                client.Close();
                return;
            }

            // send camera information header to client
            client.Send(p_header);

            // send camera information payload to client
            try
            {
                int temp_send_count = 0;

                while (temp_send_count < sinfo.Length)
                {
                    client.NoDelay = true;
                    int send_count = client.Send(sinfo, temp_send_count, sinfo.Length - temp_send_count, SocketFlags.None);
                    temp_send_count += send_count;
                }

                while (socket_connected(client))
                {
                    Thread.Sleep(10);
                }
            }
            catch (SocketException ex)
            {
                Debug.Log("Socket exception happend during multi-image send with error code: " + ex.Message);
            }
            client.Close(); // close the connection to the remote server
        }

        private void send_objects()
        {
            string response = "<objects>\n";
            foreach (KeyValuePair<int, UnityEngine.GameObject> kvp in spawned_objects)
            {
                response += "  <object>\n";
                response += "    <type>" + kvp.Value.name + "</type>\n";
                response += "    <id>" + kvp.Key + "</id>\n";
                response += "    <position x =\'" + kvp.Value.transform.position.x + "\' y=\'" + kvp.Value.transform.position.y + "\' z=\'" + kvp.Value.transform.position.z + "\'/>\n";
                response += "    <quaternion x =\'" + kvp.Value.transform.rotation.x + "\' y=\'" + kvp.Value.transform.rotation.y + "\' z=\'" + kvp.Value.transform.rotation.z + "\' w=\'" + kvp.Value.transform.rotation.w + "\'/>\n";
                response += "  </object>\n";
            }
            response += "</objects>\n";

            send("obji", response);
        }

        private void send(string tag, string message)
        {
            byte[] sinfo = Encoding.ASCII.GetBytes(message);

            // create metadata payload header
            byte[] btag = Encoding.ASCII.GetBytes(tag);
            System.UInt32 message_tag = System.BitConverter.ToUInt32(btag, 0);
            System.UInt32 res_data_length = (System.UInt32)sinfo.Length;

            System.UInt32[] uint_header = new System.UInt32[] { message_tag, res_data_length };

            byte[] p_header = new byte[uint_header.Length * sizeof(System.UInt32)];
            System.Buffer.BlockCopy(uint_header, 0, p_header, 0, uint_header.Length * sizeof(System.UInt32));

            // send image data back to client via tcp
            IPEndPoint client_ep = new IPEndPoint(pos_client_addr, pos_send_port);

            Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.NoDelay = true;
            client.SendTimeout = 1000;
            client.ReceiveTimeout = 500;

            // connect to client
            try
            {
                client.Connect(client_ep);
                while (!client.Connected)
                {
                    Debug.Log("could not connect to client, retrying...\n");
                    Thread.Sleep(500);
                    client.Connect(client_ep);
                }
            }
            catch (SocketException ex)
            {
                Debug.Log("Socket Exception! Details: " + ex.Message);
                client.Close();
                return;
            }

            // send camera information header to client
            client.Send(p_header);

            // send camera information payload to client
            try
            {
                int temp_send_count = 0;

                while (temp_send_count < sinfo.Length)
                {
                    client.NoDelay = true;
                    int send_count = client.Send(sinfo, temp_send_count, sinfo.Length - temp_send_count, SocketFlags.None);
                    temp_send_count += send_count;
                }

                while (socket_connected(client))
                {
                    Thread.Sleep(10);
                }
            }
            catch (SocketException ex)
            {
                Debug.Log("Socket exception happend during tesse_position_interface send with error code: " + ex.Message);
            }
            client.Close(); // close the connection to the remote server
        }

        private IEnumerator unload_current_scene()
        {
            AsyncOperation op_complete = SceneManager.UnloadSceneAsync(current_scene_index);
            yield return op_complete;
        }

        private IEnumerator update_segmentation()
        {
            /*
             * This function updates all of the material property blocks in the scene
             * for use in the segmentation shader. It pulls values from the *scene*_segmentation_mapping.csv
             * file located in the *_Data/Resources/ folder. Note: it 'flushes' a frame first
             * to ensure all of the materials in the scene have been fully loaded since Unity
             * does not load the new scene in the same frame as the scene change function is called.
            */
            yield return 0; // this returns once and then does not after the first frame

            if (os != null)
            {
                print("updating object segmentation for scene...\n");
                // set the active scene so that the proper lighting, skybox, etc. are used
                SceneManager.SetActiveScene(SceneManager.GetSceneByBuildIndex(current_scene_index));
                // update object segmentation mapping for the newly loaded scene
                os.update_segmentation_for_scene(current_scene_index);
                // update spawn positions for the newly loaded scene
                stc.update_spawn_positions(current_scene_index);
                // respawn the agent in the new scene
                stc.respawn_agent();
            }

            if (instance_seg != null)
            {
                print("updating instance segmentation for scene...\n");
                // set the active scene so that the proper lighting, skybox, etc. are used
                // TODO do we need to call this twice?
                SceneManager.SetActiveScene(SceneManager.GetSceneByBuildIndex(current_scene_index));
                instance_seg.update_instance_segmentation_for_scene(current_scene_index);
            }

        }

        private void create_telemetry( out byte[] byte_array, bool force = true )
        {
            /*
             * This function is a stub for implementation of a send back function
             * that will provide a remote user with a confirmation of the force
             * commands sent to the agent. It is currently not implemented and will
             * be included in a future release.
             *
            */

            // set tag string for response based on input command
            //support for fixed capture mode is unnecessary since time
            //is paused when not actively executing and the commands
            //are sent via tcp to ensure delivery
            byte[] tag;
            if (force)
                tag = Encoding.ASCII.GetBytes("tFrc");
            else
                tag = Encoding.ASCII.GetBytes("tTlp");

            float image_tag = System.BitConverter.ToSingle(tag, 0); // convert tag to a float for array packing

            // get agent's current position and orientation
            float pos_x = transform.position.x;
            float pos_y = transform.position.y;
            float pos_z = transform.position.z;

            float quat_x = transform.rotation.x;
            float quat_y = transform.rotation.y;
            float quat_z = transform.rotation.z;
            float quat_w = transform.rotation.w;

            // capture current game time
            float current_time = Time.time;

            // capture force or teleport inputs, if any
            float[] float_array;

            if( force )
            {
                Vector3 cmd = stc.get_force_command();
                float force_x = cmd.x;
                float torque_y = cmd.y;
                float force_z = cmd.z;
                float_array = new float[] { image_tag, pos_x, pos_y, pos_z, quat_x, quat_y, quat_z,
                                            quat_w, current_time, force_x, force_z, torque_y };
            }
            else
            {
                float tlpt_x = teleport_cmd.x;
                float tlpt_z = teleport_cmd.y;
                float rot_y = teleport_cmd.z;
                float_array = new float[] { image_tag, pos_x, pos_y, pos_z, quat_x, quat_y, quat_z,
                                            quat_w, current_time, tlpt_x, tlpt_z, rot_y };
            }

            // create byte array for socket transmission
            byte_array = new byte[float_array.Length * 4];

            Buffer.BlockCopy(float_array, 0, byte_array, 0, byte_array.Length);
        }

        private void send_telemetry( bool force = true )
        {
            /*
             * This function is a stub for implementation of a send back function
             * that will provide a remote user with a confirmation of the force
             * commands sent to the agent. It is currently not implemented and will
             * be included in a future release.
            */

            // get telemetery data
            byte[] telemetry;
            create_telemetry(out telemetry, force);

            // define endpoint of tranmission
            IPEndPoint tele_ip = new IPEndPoint(IPAddress.Broadcast, pos_update_port);

            // send telemetery update to user via udp
            pos_update_client.Send(telemetry, telemetry.Length, tele_ip);
        }
    }

    // Data structure for spawning objects
    public struct SpawnObject
    {
        public Int32 index;
        public Int32 method; // 0 = User specified, 1 = Random
        public Vector3 position;
        public Quaternion orientation;
    }
}
