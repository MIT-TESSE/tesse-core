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

namespace tesse
{
    /*
         * This class implements a command line parser for other
         * TESSE classes. Specifically it captures various command line
         * arugments or sets default values (these can be changed here
         * or in the editor).
         * NOTE: All warnings and errors are redirected to the Unity
         * log file.
    */

    public class tesse_command_line_parser : MonoBehaviour
    {
        // port setup for all tesse components 
        public int pos_listen_port = 9000; // udp port to listen for requests
        public int met_listen_port = 9001; // udp port to listen to requests
        public int img_listen_port = 9002; // port to listen for image requests on 
        public int pos_update_port = 9003; // udp port to send position responses back to clients
        public int step_listen_port = 9005; // tcp port to listen for fixed capture mode step commands
        // NOTE: you can simultaneously use tcp and udp ports
        public int pos_send_port = 9000; // tcp port to send responses to clients
        public int met_send_port = 9001; // tcp port to send metadata information to clients
        public int img_send_port = 9002; // tcp port to response to image requests on
        public int met_broadcast_port = 9004; // udp port to send high rate metadata information

        // default parameters for keyboard control
        public float speed = 10f;
        public float turn_speed = 0.5f;
        // default parameters for IMU update
        public int imu_update_rate = 200;
        // default parameter for fixed frame rate mode (capture_rate = 0 denotes real-time mode)
        public int capture_rate = 0;
        // default parameter for execution time when in fixed frame rate mode (i.e. the time an action is executed)
        public float execution_time = 1f;

        // tesse base processes all command line inputs
        protected void Awake()
        {
            // command line interface parameters
            if (!Application.isEditor)
            {
                // player mode, look for command line arguments
                var args = System.Environment.GetCommandLineArgs();

                bool fullscreen_check = false;

                // Process command line arguments
                //NOTE: Unity redirects all output from STDOUT to a logfile,
                //so these messages will not be displayed on the command line
                for (int i = 1; i < args.Length; ++i)
                {
                    print("argument " + i + " value is " + args[i]);
                    if (args[i] == "--listen_port")
                    {
                        if (!((args.Length >= i + 1) && System.Int32.TryParse(args[i + 1], out pos_listen_port)))
                        {
                            print("failed to parse value for argument " + args[i]);
                            print("using default listen_port of " + pos_listen_port);
                            print("value use of this argument is: \'--listen_port XXXX\' where XXXX is a valid port");
                        }
                        ++i;

                        met_listen_port = pos_listen_port + 1;
                        img_listen_port = met_listen_port + 1;
                        pos_update_port = img_listen_port + 1;
                        step_listen_port = pos_update_port + 2;

                    }
                    else if (args[i] == "--send_port")
                    {
                        if (!((args.Length >= i + 1) && System.Int32.TryParse(args[i + 1], out pos_send_port)))
                        {
                            print("failed to parse value for argument " + args[i]);
                            print("using default send_port of " + met_send_port);
                            print("value use of this argument is: \'--send_port XXXX\' where XXXX is a valid port");
                        } else
                            ++i;

                        met_send_port = pos_send_port + 1;
                        img_send_port = met_send_port + 1;
                        met_broadcast_port = img_send_port + 2;
                    }
                    else if (args[i] == "--set_resolution")
                    {
                        int width = 0, height = 0;
                        if ((args.Length >= i + 2) && (System.Int32.TryParse(args[i + 1], out width)) && (System.Int32.TryParse(args[i + 2], out height)))
                        {
                            print("setting screen resolution to " + width + " x " + height);
                            Screen.SetResolution(width, height, false);
                            i += 2;
                        }
                        else
                        {
                            print("failed to parse value for argument " + args[i]);
                            print("using default screen resolution of 640x480");
                            print("value use of this argument is: \'--set_resolution width height\' where width and height are reasonable unsigned integer values for your system");
                            Screen.SetResolution(640, 480, false);
                        }
                    }
                    else if (args[i] == "--fullscreen")
                    {
                        fullscreen_check = true;
                        Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
                    }

                    else if (args[i] == "--speed")
                    {
                        if (!((args.Length >= i + 1) && System.Single.TryParse(args[i + 1], out speed)))
                            print("Failed to parse value for argument " + args[i]);
                        else
                            ++i;
                    }

                    else if (args[i] == "--turn_speed")
                    {
                        if (!((args.Length >= i + 1) && System.Single.TryParse(args[i + 1], out turn_speed)))
                            print("Failed to parse value for argument " + args[i]);
                        else
                            ++i;
                    }

                    else if (args[i] == "--imu_update_rate")
                    {
                        if (!((args.Length >= i + 1) && System.Int32.TryParse(args[i + 1], out imu_update_rate)))
                            print("Failed to parse value for argument " + args[i]);
                        else
                            ++i;
                    }
                    else if (args[i] == "--capture_rate")
                    {
                        if (!((args.Length >= i + 1) && System.Int32.TryParse(args[i + 1], out capture_rate)))
                            print("Failed to parse value for argument " + args[i]);
                        else
                            ++i;

                        if (!((args.Length >= i + 1) && System.Single.TryParse(args[i + 1], out execution_time)))
                            print("Failed to parse execution time value for argument " + args[i]);
                        else
                            ++i;
                    }
                    else if( args[i] == "--help" || args[i] == "-h" )
                    {
                        print("\"--listen_port int\" - sets the ports that the interface listens on, all listen ports operate on the UDP protocol \n" +
                              " \"--send port int\" - sets the sned ports that the interface sends data back to a client on, all send ports operate on the TCP protocol\n" +
                              " \"--set_resolution width_int height_int\" - sets the screen resolution of the game to width_int x height_int \n" +
                              " \"--fullscreen\" - if this argument is given, then the game will be fullscreen \n" +
                              " \"--speed float\" - sets the force given to the agent when pressing the 'w', 's', 'q' and 'e' keys to float \n" +
                              " \"--turn_speed float\" - sets the torque given to the agent when pressing the 'a' and 'd' keys to float\n" +
                              " \"--imu_update_rate int\" - sets the update rate of the imu to int updates per second\n"
                            );
                    }
                    else
                    {
                        print("Warning! " + args[i] + " is an unrecognized command!");
                    }

                }
                
                // this is done because Unity will cache the last settings used and default to them at startup
                //this ensure that if the user did not explicitly request the game to run fullscreen, the game
                //will run windowed
                if (!fullscreen_check)
                {
                    Screen.fullScreenMode = FullScreenMode.Windowed;
                }
            }
        }
    }
}
