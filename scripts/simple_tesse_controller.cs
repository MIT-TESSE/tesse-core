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

using UnityEngine;

public class simple_tesse_controller : MonoBehaviour
{
    /* 
     * This class provides the keyboard interface to the TESSE agent.
     * The keyboard controller supports control when the agent is in
     * real-time mode and fixed capture rate mode.
     * Additionally, it calculates the IMU outputs and handles collisions.
    */

    // variables for keyboard control
    private Rigidbody agent_rigid_body;
    public float speed = 2f; // force multiplier for movement forces
    public float turn_speed = .05f; // torque multiplier for movement forces

    // variables to hold input forces
    //NOTE: forces are applied until the next Update()
    //i.e. they are applied for all FixedUpdate() steps
    //that take place between the Update() the action is
    //applied and the next Update() - this is done to ensure
    //forces are smoothly applied as opposed to very short 
    //duration impulses
    private Vector3 power_input;
    private Vector3 turn_input;

    // variables for physics
    private float prev_time;
    private Vector3 accel;
    private Vector3 rot_accel;
    private Vector3 vel;
    private Vector3 rot_vel;
    private Vector3 last_rot;

    private bool collision_flag = false; // has a collision taken place since last update
    private string collision_object_name = null; // placeholder to hold name of object after collision

    public bool keyboard_active = true; // flag for keyboard control

    private bool respawn_agent_flag = false; // should agent respawn at next Update()

    private tesse_spawn_manager spawner; // controls agent spawning
    private simple_tesse_hover_controller hover_controller; // controls hover height

    private int attempt = 0; // debug for respawner

    // duration variables for fixed frame rate execution 
    public float cmd_time = 0f; // amount of game time in seconds for the action to execute
    private float exec_time = 0f; // time action is being executed
    private float last_time = 0f; // time of last Update() loop

    void Awake()
    {
        // setup various object parameters
        //NOTE: Awake() runs before Start()
        agent_rigid_body = GetComponent<Rigidbody>();

        spawner = GetComponent<tesse_spawn_manager>();
        hover_controller = GetComponent<simple_tesse_hover_controller>();

        // initialize imu variables
        //NOTE: IMU accelerations are done by taking the finite
        //difference
        accel = new Vector3(0f, 0f, 0f);
        rot_accel = new Vector3(0f, 0f, 0f);
        vel = new Vector3(0f, 0f, 0f);
        rot_vel = new Vector3(0f, 0f, 0f);

        // set random seed for respawn system
        Random.InitState((int)System.DateTime.Now.Ticks);

        // cache time
        prev_time = Time.time;
    }

    // Update is called once per frame
    void Update()
    {

        if (keyboard_active)
        {
            //if ( Time.captureFramerate > 0 && Time.timeScale != 0 )
            //    cmd_time = 1f / System.Convert.ToSingle(Time.captureFramerate); // set command time to single frame
            /*
            // fixed frame rate execution logic 
            if (((exec_time + (Time.fixedDeltaTime*1.5)) >= cmd_time) && Time.captureFramerate > 0)
            {
                // execution time reached, reset forces and pause game
                power_input = new Vector3(0f, 0f, 0f);
                turn_input = new Vector3(0f, 0f, 0f);
                exec_time = 0;
                Time.timeScale = 0; // stops game time advancing, Update() still runs
            }
            else if (Time.timeScale == 1 && Time.captureFramerate > 0)
            {
                exec_time += Time.time - last_time; // add to execution time
            }
            else
            */
            {
                // default to reset input forces if not in fixed frame rate mode
                power_input = Vector3.zero;
                turn_input = Vector3.zero;
            }
            
            
            // cache current time for next Update() loop
            

            //*** Keyboard input handling ***//
            //NOTE: force inputs are applied in the FixedUpdate() loop until the next Update()
            if (Input.GetKey(KeyCode.W)) // move forward (Z axis)
            {
                power_input = new Vector3(0f, 0f, speed);
                // for fixed frame rate mode
                if (Time.captureFramerate > 0 && cmd_time == 0f)
                {
                    cmd_time = 1f / System.Convert.ToSingle(Time.captureFramerate); // set command time to single frame
                    Time.timeScale = 1;
                }
            }

            if (Input.GetKey(KeyCode.A)) // rotate left
            {
                turn_input = new Vector3(0f, -turn_speed, 0f);
                // for fixed frame rate mode
                if (Time.captureFramerate > 0 && cmd_time == 0f)
                {
                    cmd_time = 1f / System.Convert.ToSingle(Time.captureFramerate); // set command time to single frame
                    Time.timeScale = 1;
                }
            }

            if (Input.GetKey(KeyCode.D)) // rotate right
            {
                turn_input = new Vector3(0f, turn_speed, 0f);
                // for fixed frame rate mode
                if (Time.captureFramerate > 0 && cmd_time == 0f)
                {
                    cmd_time = 1f / System.Convert.ToSingle(Time.captureFramerate); // set command time to single frame
                    Time.timeScale = 1;
                }
            }

            if (Input.GetKey(KeyCode.S)) // move backward (Z axis)
            {
                power_input = new Vector3(0f, 0f, -speed);
                // for fixed frame rate mode
                if (Time.captureFramerate > 0 && cmd_time == 0f )
                {
                    cmd_time = 1f / System.Convert.ToSingle(Time.captureFramerate); // set command time to single frame
                    Time.timeScale = 1;
                }
            }

            if ( Input.GetKey(KeyCode.Q) ) // move left (X axis)
            {
                power_input = new Vector3(-speed, 0f, 0f);
                // for fixed frame rate mode
                if (Time.captureFramerate > 0 && cmd_time == 0f )
                {
                    cmd_time = 1f / System.Convert.ToSingle(Time.captureFramerate); // set command time to single frame
                    Time.timeScale = 1;
                }
            }

            if( Input.GetKey(KeyCode.E) ) // move right (X axis)
            {
                power_input = new Vector3(speed, 0f, 0f);
                // for fixed frame rate mode
                if (Time.captureFramerate > 0 && cmd_time == 0f )
                {
                    cmd_time = 1f / System.Convert.ToSingle(Time.captureFramerate); // set command time to single frame
                    Time.timeScale = 1;
                }
            }

            //*** Hover height modification ***//
            // these commands change the target height of the 
            //hover controller up and down at 0.1m increments
            if (Input.GetKeyDown(KeyCode.Space))
                hover_controller.hover_height += 0.1f;

            if (Input.GetKeyDown(KeyCode.C))
                hover_controller.hover_height -= 0.1f;

            //*** Stop command ***//
            // stops the agent instantly
            if (Input.GetKey(KeyCode.X))
            {
                // stop agent
                agent_rigid_body.velocity = new Vector3(0f, 0f, 0f);
                agent_rigid_body.angularVelocity = new Vector3(0f, 0f, 0f);
            }

            //*** respawn command ***//
            if (Input.GetKeyDown(KeyCode.R))
            {
                // respawn agent
                respawn_agent_flag = true;
                collision_object_name = null;
                collision_flag = true;
                attempt = 0;
            }
        }

        //*** Exit application ***//
        if (Input.GetKeyDown(KeyCode.Escape))
            Application.Quit();

        //*** Toggle keyboard control ***//
        // when keyboard_active == false, all keyboard movement 
        //commands are ignored, this can be helpful when conducting experiments
        //via the python api
        if( Input.GetKeyDown(KeyCode.T) && Input.GetKey(KeyCode.LeftShift) )
        {
            if (keyboard_active)
                keyboard_active = false;
            else
                keyboard_active = true;
        }

        //*** respawn agent logic ***//
        if (respawn_agent_flag && get_collision())
        {
            // this will happen if the agent is respawned 
            //in a location, but it has collided with an object
            //so another respawn attempt is made
            
            // respawn agent
            reset_agent();
            attempt++;
        }
        else if (respawn_agent_flag && !get_collision())
        {
            // agent spawned in a good location (no collision)
            respawn_agent_flag = false;
            // flush collision information
            collision_object_name = null;
        }
        else if( attempt > 100 )
        {
            // give up if the agent can't be respawn without 
            //a collision after 100 attempts
            //this will leave the agent possibly clipped into
            //an object, this is very unlikely if a decent
            //spawn points csv file has been generated
            respawn_agent_flag = false;
            collision_flag = false;
            collision_object_name = null;
        }
    }

    // FixedUpdate() runs at the physics update rate, independent of the frame rate
    void FixedUpdate()
    {
        
        if (keyboard_active)
        {
            // fixed frame rate execution logic 
            if ( (cmd_time != 0f) && ((exec_time + (System.Convert.ToSingle(Time.fixedDeltaTime) * 1.5f)) >= cmd_time) && (Time.captureFramerate > 0) ) 
            //if (((exec_time + (Time.fixedDeltaTime * 1.5)) >= cmd_time) && Time.captureFramerate > 0)
            {
                // execution time reached, reset forces and pause game
                power_input = new Vector3(0f, 0f, 0f);
                turn_input = new Vector3(0f, 0f, 0f);
                exec_time = 0;
                cmd_time = 0;
                Time.timeScale = 0; // stops game time advancing, Update() still runs
            }
            else if (Time.timeScale == 1 && Time.captureFramerate > 0)
            {
                exec_time += Time.time - last_time; // add to execution time
            }

            // apply any forces that were requested in the last Update() loop
            // this is done because (rate of FixedUpdate() ) >= (rate of Update() )
            // i.e. physics runs faster than frame rate normally
            agent_rigid_body.AddRelativeForce(power_input);
            agent_rigid_body.AddRelativeTorque(turn_input);
        }

        // cache current time for next Update() loop
        last_time = Time.time;
        

        //*** update internal physics information for IMU readings ***//
        update_imu();
    }

    public Vector3 get_force_command()
    {
        // this function provides a hook to the keyboard force input
        //it is used in the force telemetry broadcast function located
        //in tesse_position_interface
        Vector3 cmd = new Vector3(power_input.x, turn_input.y, power_input.z);

        return cmd;
    }

    void update_imu()
    {
        /*
         * This function updates the state of the agent
        */

        // calculate elapsed time since last physics update,
        //this should primarily be the physics update rate
        //set in the build settings, but can vary depending
        //on the hardware of the system being run on
        float dt = Time.time - prev_time;

        Vector3 last_vel = vel; // last captured velocity
        Vector3 last_rot_vel = rot_vel; // last captured rotation velocity

        // use rigidbody velocity transformed to body frame
        vel = transform.InverseTransformDirection(agent_rigid_body.velocity);

        // update accelerations
        accel = (vel - last_vel) / dt;
       
        rot_vel = agent_rigid_body.angularVelocity;

        // update rotation accelerations
        rot_accel = (rot_vel - last_rot_vel) / dt;
      
        // set time for next iteration
        prev_time = Time.time;
    }

    public void reset_agent_position(Vector3 pos)
    {
        /* 
         * This function sets the agent's location to the 
         * position provided by pos
        */

        // stop all motion
        agent_rigid_body.velocity = new Vector3(0f, 0f, 0f);
        agent_rigid_body.angularVelocity = new Vector3(0f, 0f, 0f);
        // set agent's new position
        transform.position = pos;
        transform.eulerAngles = new Vector3(0f, Random.Range(0, 360), 0f);
    }

    public void reset_agent_position(Vector3 pos, Vector3 rot)
    {
        /*
         * This function sets the agent's position and rotation
         * to the position provided in pos and the rotation provided
         * in rot.
        */

        // stop all motion
        agent_rigid_body.velocity = new Vector3(0f, 0f, 0f);
        agent_rigid_body.angularVelocity = new Vector3(0f, 0f, 0f);
        // set agent's new position
        transform.position = pos;
        transform.eulerAngles = rot;
    }

    private void reset_agent()
    {
        /*
         * This function randomly spawns the agent to a new 
         * spawn point pulled from the spawn points csv
        */

        // stop all motion
        agent_rigid_body.velocity = new Vector3(0f, 0f, 0f);
        agent_rigid_body.angularVelocity = new Vector3(0f, 0f, 0f);
        // set agent's new position
        bool is_valid = false;
        int trys = 0; // attempt to find a location 100 times before giving up
        while (!is_valid && trys < 100) // attempt to find valid spawn location
        {
            transform.position = spawner.get_random_spawn_point();
            RaycastHit hit;
            if( Physics.Raycast(transform.position, Vector3.down, out hit, 20f) )
            {
                if( System.Math.Abs(hit.distance - hover_controller.hover_height) < 0.2) 
                {
                    is_valid = true;
                }
            }
            trys++;
        }
        // set random rotation around y axis
        transform.eulerAngles = new Vector3(0f, Random.Range(0, 360), 0f);
    }

    public void respawn_agent()
    {
        // respawn agent
        respawn_agent_flag = true; // flag to signal command to Unity Update() thread
        collision_object_name = null; // reset collision information
        collision_flag = true; // set collision flag
        attempt = 0; // reset attempt counter
    }

    public void update_spawn_positions(int scene_index=0)
    {
        // loads spawn point locations from csv
        spawner.load_spawn_points(scene_index);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // This is a Unity callback that is triggers if this object collides with 
        //another object in the scene

        // set collision flag
        collision_flag = true;

        // get name of object that was collided with to send back as well
        collision_object_name = collision.gameObject.name;
    }

    //*** access functions for physics information ***//
    public Vector3 get_accleration()
    {
        return accel;
    }

    public Vector3 get_ang_acceleration()
    {
        return rot_accel;
    }

    public Vector3 get_velocity()
    {
        return vel;
    }

    public Vector3 get_ang_velocity()
    {
        return rot_vel;
    }

    public bool get_collision()
    {
        if (collision_flag)
        {
            // object collided with something since last check
            collision_flag = false; // reset collision flag
            return true;
        }
        else
        {
            // no collision since last check
            return false;
        }
    }

    public string get_collision_object_name()
    {
        // return the name of the object that was last collided with
        if( collision_object_name != null )
        {
            string temp = collision_object_name;
            collision_object_name = null;
            return temp;
        }
        else
        {
            return "";
        }
    }
}
