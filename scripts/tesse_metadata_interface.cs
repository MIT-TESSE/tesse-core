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

using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;


namespace tesse
{
    public class tesse_metadata_interface : tesse_base
    {
        /*
         * This class implements the interface to process metadata requests
         * and provide metadata to clients of the TESSE agent. It inherits
         * from tesse_base which contains some common convenience methods,
         * and itself inherits from the Unity Monobehavior class.
        */

        // port setup for all tesse components that inherit this class
        private int met_listen_port = 9001; // udp port to listen to requests
        // NOTE: you can simultaneously use tcp and udp ports
        private int met_send_port = 9001; // tcp port to send metadata information to clients

        private int met_broadcast_port = 9004; // udp port used to broadcast high right metadata information

        // udp socket object for listener
        private UdpClient met_request_client = null;

        // tcp socket for sending metadata to client
        private Socket met_client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private UdpClient met_broadcast = new UdpClient();

        // object to hold metadata request client's ip
        private IPAddress met_client_addr = null;

        // mutex lock for metadata requests
        private System.Object met_request_lock = new System.Object();

        // signal to unity rendering thread that metadata was requested
        private bool met_request_flag = false;

        // thread objects for handling metadata requests and sending
        private Thread met_request_thread = null;
        //private Thread met_send_thread = null;

        // flag for thread running
        private bool met_request_running = false;

        protected void Awake()
        {
            // setup tcp client socket
            met_client.NoDelay = true;
            met_client.SendTimeout = 1000;
            met_client.ReceiveTimeout = 500;
        }

        // Start is called before the first frame update
        new void Start()
        {
            base.Start(); // initialize common components using tesse_base.Start(

            // get ports pulled from command line parser
            met_listen_port = cla_parser.met_listen_port;
            met_send_port = cla_parser.met_send_port;
            met_broadcast_port = cla_parser.met_broadcast_port;

            // start metadata request listener thread
            met_request_thread = new Thread(new ThreadStart(met_request_listener));
            met_request_thread.Start();
            met_request_running = true;
        }

        private void Update()
        {
            // this is provided outside FixedUpdate() so that cached values of the metadata
            //can be provided to the user when the game is paused in fixed frame rate mode
            lock (met_request_lock)
            {
                if (met_request_flag && Time.timeScale == 0)
                {
                    string md = null;

                    // get agent metadata xml string
                    get_agent_state_metadata(out md, true);

                    // start metadata transmission thread
                    Thread met_send_thread = new Thread(() => met_send(md, met_client_addr));
                    met_send_thread.Start();
                    met_send_thread.Join();

                    // reset flag to thread
                    met_request_flag = false;
                }
            }
        }

        // FixedUpdate is called once per frame and is used by unity for physics calculations
        void FixedUpdate()
        {
            // agent metadata is pulled at every physics update calculation and broadcast
            //via UDP
            //this provides a high rate IMU update to any user listening to the UDP port
            //if tied into tcp the rate is drastically reduced
            lock( met_request_lock )
            {
                string md = null;
                get_agent_state_metadata(out md);
                byte[] test = Encoding.ASCII.GetBytes(md);
                IPEndPoint ep = new IPEndPoint(IPAddress.Broadcast , met_broadcast_port);
                met_broadcast.Send(test, test.Length, ep);

                if (met_request_flag && Time.timeScale != 0)
                {
                    // a threaded send is used and then immediately joined,
                    //this ran about 20% faster in testing versus fully serial execution
                    //and versus a de-scoped thread closure (versus an explicit Join())
                    get_agent_state_metadata(out md, true); // only check collisions for tcp requested metadata
                    Thread met_send_thread = new Thread(() => met_send(md, met_client_addr));
                    met_send_thread.Start();
                    met_send_thread.Join();
                    
                    // reset flag to thread
                    met_request_flag = false;
                }
            }
        }

        void OnApplicationQuit()
        {
            // signal metadata udp listener thread to terminate
            met_request_running = false;

            // if the thread hasn't terminated, abort it
            if( (met_request_thread != null) && (met_request_thread.IsAlive) )
            {
                met_request_thread.Abort();
            }
        }

        // metadata request listener thread
        private void met_request_listener()
        {
            print("Metadata request listener started...");

            met_request_client = new UdpClient(met_listen_port);

            // listen on met_listen_port from any ip address
            IPEndPoint ip = new IPEndPoint(IPAddress.Any, met_listen_port);

            while (met_request_running)
            {
                // poll socket to see if data has been sent every 1000 microseconds
                if (met_request_client.Client.Poll(10, SelectMode.SelectRead))
                {
                    // data data from socket
                    byte[] data = met_request_client.Receive(ref ip);

                    // process data request; valid requests consist of a 4-byte uchar tag
                    //and then a byte-packed array of request parameters, if required
                    if ((data.Length == 4) && (System.Convert.ToChar(data[0]) == 'r') && (System.Convert.ToChar(data[1]) == 'M')
                      && (System.Convert.ToChar(data[2]) == 'E') && (System.Convert.ToChar(data[3]) == 'T'))
                    {
                        print("received request for metadata!\n");
                        // process metadata request
                        lock (met_request_lock)
                        {
                            met_client_addr = ip.Address; // set send ip to requester's address
                            met_request_flag = true;
                        }
                    }
                }
            }
        }

        // get agent state and create metadata xml message as string
        public void get_agent_state_metadata(out string metadata_string, bool check_collision = false)
        {
            // this function will create a metadata string in xml format; 
            //it overwrites any information already contained in the string 
            //parameter passed to this function

            // metadata header with version information
            metadata_string = "<TESSE_Agent_Metadata_v0.5>\n";

            // postion of the agent in the game world 
            metadata_string += "  <position x=\'" + transform.position.x + "\' y=\'" + transform.position.y + "\' z=\'" + transform.position.z + "\'/>\n";

            // orientation of the agent in the game world
            metadata_string += "  <quaternion x=\'" + transform.rotation.x + "\' y=\'" + transform.rotation.y + "\' z=\'" + transform.rotation.z + "\' w=\'" + transform.rotation.w + "\'/>\n";

            // convert rigid body velocity to body frame coordinates
            Vector3 local_vel = stc.get_velocity();
            // agent's velocity in body frame
            metadata_string += "  <velocity x_dot=\'" + local_vel.x + "\' y_dot=\'" + local_vel.y + "\' z_dot=\'" + local_vel.z + "\'/>\n";

            // agent's angular rates
            Vector3 local_rot_vel = stc.get_ang_velocity();
            metadata_string += "  <angular_velocity x_ang_dot=\'" + local_rot_vel.x + "\' y_ang_dot=\'" + local_rot_vel.y + "\' " +
                                        "z_ang_dot=\'" + local_rot_vel.z + "\'/>\n";

            // get latest acceleration value in body frame from agent controller and add to metadata xml
            Vector3 temp = stc.get_accleration();
            metadata_string += "  <acceleration x_ddot=\'" + temp.x + "\' y_ddot=\'" + temp.y + "\' z_ddot=\'" + temp.z + "\'/>\n";

            // get latest angular acceleration value from agent controller and add to metadata xml
            temp = stc.get_ang_acceleration();
            metadata_string += "  <angular_acceleration x_ang_ddot=\'" + temp.x + "\' y_ang_ddot=\'" + temp.y + "\' z_ang_ddot=\'" + temp.z + "\'/>\n";

            // get simulation time and add to metadata xml
            metadata_string += "  <time>" + UnityEngine.Time.time + "</time>\n";

            // get cached collision information from agent controller
            if (check_collision) // don't check collision unless requested, this is done to ensure collision isn't reset by high frame rate IMU
            {
                if (stc.get_collision() == false)
                    metadata_string += "  <collision status=\'false\' name=\'\'/>\n";
                else
                    metadata_string += "  <collision status=\'true\' name=\'" + stc.get_collision_object_name().ToString() + "\'/>\n";
            }
            else
            {
                metadata_string += "  <collision status=\'false\' name=\'\'/>\n";
            }

            // add agent collider status
            if (GetComponent<CapsuleCollider>().enabled)
                metadata_string += "  <collider status=\'true\'/>\n";
            else
                metadata_string += "  <collider status=\'false\'/>\n";

            // add header closure statement
            metadata_string += "</TESSE_Agent_Metadata_v0.5>\n";
        }

        private void met_send(string metadata_string, IPAddress client_ip)
        {
            // this object sends metadata back to the requesting client
            IPEndPoint met_client_ep = new IPEndPoint(client_ip, met_send_port);

            met_client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // attempt to connect to client
            try
            {
                met_client.Connect(met_client_ep);
                while (!met_client.Connected)
                {
                    print("Could not connect to client, retrying...\n");
                    Thread.Sleep(500);
                    met_client.Connect(met_client_ep);
                }
            }
            catch (SocketException ex)
            {
                print("Socket exception in metadata send routine!\nDetails: " + ex.Message);
                met_client.Close();
                return;
            }

            try
            {
                // convert to metadata string to byte array for socket object
                byte[] bmd = Encoding.ASCII.GetBytes(metadata_string);

                // create tag for tesse interface v0.2 transmission protocol
                System.UInt32 tag = System.BitConverter.ToUInt32(Encoding.ASCII.GetBytes("meta"), 0);
                System.UInt32 meta_length = (System.UInt32)bmd.Length;
                System.UInt32[] uint_header = new System.UInt32[] { tag, meta_length };


                // create metadata header 
                byte[] p_header = new byte[uint_header.Length * sizeof(System.UInt32)];
                System.Buffer.BlockCopy(uint_header, 0, p_header, 0, uint_header.Length * sizeof(System.UInt32));

                met_client.Send(p_header);

                // send payload, call repeatedly until all data has been sent
                int send_count = 0;
                while (send_count < bmd.Length)
                {
                    int temp_send_count = met_client.Send(bmd, send_count, bmd.Length - send_count, SocketFlags.None);
                    send_count += temp_send_count;
                }

                while (socket_connected(met_client) )
            {
                    Thread.Sleep(10);
                }
            }
            catch (SocketException ex)
            {
                print("Socket exception during metadata transmission to client!\nError message: " + ex.Message);
            }

            met_client.Close();
        }
    }
}
