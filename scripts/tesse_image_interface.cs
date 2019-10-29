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

using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Unity.Collections;

namespace tesse
{
    public class tesse_image_interface : tesse_base
    {
        /*
         * This class implements the interface to process image requests,
         * camera position requests, camera rotation requests and camera
         * information requests. It inherits from tesse_base which contains 
         * some common convenience methods, and itself inherits from the 
         * Unity Monobehavior class.
        */

        // reference to all cameras attached to the TESSE agent
        //these cameras are exposed to the interface
        //NOTE: this can be set in the Unity Editor, if the list
        //is empty all cameras attached to the agent will be used,
        //if you do not want to expose all cameras to the interface,
        //you must manually populate this List
        public List<Camera> agent_cameras = new List<Camera>();

        // port assignments from tesse command line parser
        private int img_listen_port = 9002; // udp port to listen for requests, default is 9002
        // NOTE: you can simultaneously use tcp and udp ports
        private int img_send_port = 9002; // tcp port to send responses to clients, default is 9002

        // udp socket object for listener
        private UdpClient img_request_client = null;

        // flag for image completion from tesse_camera_capture components
        public int num_images_complete = 0;

        // tcp object for sending images to client, this connection is used by multiple threads so it needs
        //to exist at class scope. The metadata sender is lightweight enough to not warrant this.
        Socket img_client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        // client request ip
        //all images are sent back to the ip address of the requester
        //multiple requesters can be serviced by a single client
        //(e.g. I request images from computer A, and then afterwards request
        //images from computer B; these requests cannot be simultaneous)
        private IPAddress img_client_addr = null; // ip address of position request client

        // signal to unity thread that image without metadata requested
        private bool img_request_flag = false;

        // signal to unity thread that image with metadata requested
        private bool img_meta_request_flag = false;

        // signal to unity thread that camera information/change request received
        private bool cam_info_request_flag = false; // get camera information (id, name, etc.)
        private bool cam_param_request_flag = false; // change camera parameters (fov, resolution)
        private bool cam_pos_request_flag = false; // change relative position (i.e. relative to agent's center point)
        private bool cam_rot_request_flag = false; // change relative rotation (i.e. relative to agent's orientation)

        //  mutex locks for request and send
        //these are required because the udp message listener thread and Unity Update() 
        //thread cannot directly iteract with each other, as such a flag that signals 
        //that a command or information request has been made is set to true and that
        //flag is read in the next Unity Update()
        private System.Object img_request_lock = new System.Object();
        private System.Object img_send_lock = new System.Object();
        private System.Object image_count = new System.Object();

        // placeholders for camera parameter values
        int request_width = -1; // width of image requested in pixels
        int request_height = -1; // height of image requested in pixels
        int request_cam_id = -1; // id of camera requested, this is the camera's position in the agent_cameras List
        float request_fov = -1; // field of view of the camera
        float request_nearClipPlane = -1; // near clip plane of the camera
        float request_farClipPlane = -1; // far clip plane of the camera
        float request_cam_x = -1; // x position of the camera relative to the agent; also used for x coordinate of the camera rotation matrix
        float request_cam_y = -1; // y position of the camera relative to the agent; also used for y coordinate of the camera rotation matrix
        float request_cam_z = -1; // z position of the camera relative to the agent; also used for z coordinate of the camera rotation matrix
        float request_cam_w = -1; // w coordinate of the camera rotation matrix

        // struct to hold requested image information
        private struct img_requested
        {
            // this makes caching requested camera information much easier
            //and cleans up communication between Unity Update() and the 
            //image udp listener thread
            public int cam_requested;
            public bool compressed; // user requests compressed image, this is currently unoptimized and slower than uncompressed
            public bool single_channel; // user requests a single channel image, this is currently upoptimized and slower than three channel (c# array issues)

            // struct constructor
            public img_requested(int _cam_requested = 0, bool _compressed = false, bool _single_channel = false)
            {
                cam_requested = _cam_requested;
                compressed = _compressed;
                single_channel = _single_channel;
            }
        };

        // hold list of cameras requested
        private List<img_requested> cams_requested = new List<img_requested>();

        // thread for position request listener
        private Thread img_request_thread;

        // flag for threads
        private bool img_request_running = false;

        // reference to metadata interface
        //this is required to fetch metadata for image
        //requests with metadata
        private tesse_metadata_interface tmi = null;

        // multi image response message tag
        private System.UInt32 mult_image_tag = 0;

        // Start is called before the first frame update
        new void Start()
        {
            base.Start(); // initialize tesse_base

            // setup ports from tesse command line parser
            img_listen_port = cla_parser.img_listen_port;
            img_send_port = cla_parser.img_send_port;

            // start position request listener thread
            img_request_thread = new Thread(() => img_request_listener());
            img_request_thread.Start();
            img_request_running = true;

            // if cameras weren't assigned in the editor
            //populate agent camera list with all child 
            //cameras of the tesse agent
            if (agent_cameras.Count == 0)
            {
                // agent cameras wasn't set in editor, populate it here
                foreach (var c in GetComponentsInChildren<Camera>())
                {
                    agent_cameras.Add(c);
                }
            }

            // set multiple image header response tag
            byte[] mult_tag = Encoding.ASCII.GetBytes("mult");
            mult_image_tag = System.BitConverter.ToUInt32(mult_tag, 0);

            // get reference to tesse metadata interface
            tmi = GetComponent<tesse_metadata_interface>();
        }

        // Update is called once per frame
        void LateUpdate()
        {
            // lock used to ensure listener thread and Unity Update() threads do not race
            lock (img_request_lock)
            {
                // only process requests if the game is in real-time mode or no fixed capture rate commands are still active
                if ( (img_request_flag || img_meta_request_flag) && ((Time.captureFramerate == 0) || (Time.timeScale == 0)) )
                {
                    // images requested by client
                    get_and_send_multi_camera_screenshot();
                    // reset command flags
                    img_request_flag = false;
                    img_meta_request_flag = false;
                    num_images_complete = 0; // placeholder for future optimization workflow
                }

                // camera information and parameter checks, only a single 
                //camera request can be made per frame, this ensures that 
                //changes are properly captured in the information sent
                //back to the user regarding camera state
                if (cam_info_request_flag) // camera information is requested
                {
                    send_camera_info(); // send info to client
                    cam_info_request_flag = false; // reset command flag
                } else if (cam_param_request_flag)
                {
                    set_camera_params(); // set command parameters
                    cam_param_request_flag = false;
                } else if (cam_pos_request_flag) // set camera position
                {
                    set_camera_position();
                    cam_pos_request_flag = false;
                } else if (cam_rot_request_flag) // set camera rotation
                {
                    set_camera_rotation();
                    cam_rot_request_flag = false;
                }
            }

        }

        private void OnApplicationQuit()
        {
            // stop image udp listener thread
            img_request_running = false;

            // if the image udp listener thread fails to stop, abort the thread
            if (img_request_thread != null && img_request_thread.IsAlive)
                img_request_thread.Abort();
        }

        private void img_request_listener()
        {
            // image udp listener thread, this thread lives seperate from the 
            //Unity Update() thread and therefore the execution speed is not
            //tied to the frame rate of Unity 

            print("Image request listener started");

            // instaniate udp listener
            img_request_client = new UdpClient(img_listen_port);

            // listen on img_listen_port from any ip address
            IPEndPoint ip = new IPEndPoint(IPAddress.Any, img_listen_port);

            while (img_request_running)
            {
                // poll port to see if data is available every millisecond
                if (img_request_client.Client.Poll(1000, SelectMode.SelectRead))
                {
                    // get data from udp socket object
                    byte[] data = img_request_client.Receive(ref ip);

                    // process received data
                    if ((data.Length > 8) && (System.Convert.ToChar(data[0]) == 'r') && (System.Convert.ToChar(data[1]) == 'I')
                       && (System.Convert.ToChar(data[2]) == 'M') && (System.Convert.ToChar(data[3]) == 'G'))
                    {
                        // process multiple image request
                        lock (img_request_lock)
                        {
                            // cache requester's ip address for response
                            img_client_addr = ip.Address;

                            // capture all image requests contained in the message
                            for (int i = 4; i < data.Length; i += 12)
                            {
                                bool compressed = false, single_channel = false;
                                if (System.BitConverter.ToUInt32(data, i + 4) > 0)
                                    compressed = true; // user requested compressed rgb image
                                if (System.BitConverter.ToUInt32(data, i + 8) > 0)
                                    single_channel = true; // user requested single channel image

                                cams_requested.Add(new img_requested(System.BitConverter.ToInt32(data, i), compressed, single_channel)); // add to list of requested images
                            }

                            img_request_flag = true; // flag to signal command to Unity Update() thread
                        }
                    }
                    else if ((data.Length > 8) && (System.Convert.ToChar(data[0]) == 't') && (System.Convert.ToChar(data[1]) == 'I')
                            && (System.Convert.ToChar(data[2]) == 'M') && (System.Convert.ToChar(data[3]) == 'G'))
                    {
                        // process multiple image request with associated metadata
                        lock (img_request_lock)
                        {
                            // cache requester's ip address for response
                            img_client_addr = ip.Address;

                            // capture all image requests contained in the message
                            for (int i = 4; i < data.Length; i += 12)
                            {
                                bool compressed = false, single_channel = false;
                                if (System.BitConverter.ToUInt32(data, i + 4) > 0)
                                    compressed = true; // user requested compressed rgb image
                                if (System.BitConverter.ToUInt32(data, i + 8) > 0)
                                    single_channel = true; // user requested single channel image

                                cams_requested.Add(new img_requested(System.BitConverter.ToInt32(data, i), compressed, single_channel)); // add to list of requested images
                            }

                            img_meta_request_flag = true; // flag to signal command to Unity Update() thread
                        }
                    }
                    else if ((data.Length == 8) && (System.Convert.ToChar(data[0]) == 'g') && (System.Convert.ToChar(data[1]) == 'C')
                  && (System.Convert.ToChar(data[2]) == 'a') && (System.Convert.ToChar(data[3]) == 'I'))
                    {
                        // camera information request received
                        lock (img_request_lock)
                        {
                            // cache requester's ip address for response
                            img_client_addr = ip.Address;
                            request_cam_id = System.BitConverter.ToInt32(data, 4); // requested id, if -1 then all ids are returned

                            cam_info_request_flag = true; // flag to signal command to Unity Update() thread
                        }
                    }
                    else if ((data.Length >= 20) && (System.Convert.ToChar(data[0]) == 's') && (System.Convert.ToChar(data[1]) == 'C')
                      && (System.Convert.ToChar(data[2]) == 'a') && (System.Convert.ToChar(data[3]) == 'R'))
                    {
                        // set camera resolution requested
                        lock (img_request_lock)
                        {
                            // cache requester's ip address for response
                            img_client_addr = ip.Address;
                            // requested parameters
                            request_cam_id = System.BitConverter.ToInt32(data, 4); // id of camera to change the values on, -1 means all cameras
                            request_height = System.BitConverter.ToInt32(data, 8); // new camera height (rows)
                            request_width = System.BitConverter.ToInt32(data, 12); // new camera width (cols)
                            request_fov = System.BitConverter.ToSingle(data, 16); // new camera field of view, in degrees

                            if (data.Length >= 24)
                                request_nearClipPlane = System.BitConverter.ToSingle(data, 20);
                            else
                                request_nearClipPlane = -1;

                            if (data.Length >= 28)
                                request_farClipPlane = System.BitConverter.ToSingle(data, 24);
                            else
                                request_farClipPlane = -1;

                            cam_param_request_flag = true; // flag to signal command to Unity Update() thread
                        }
                    }
                    else if ((data.Length == 20) && (System.Convert.ToChar(data[0]) == 's') && (System.Convert.ToChar(data[1]) == 'C')
                      && (System.Convert.ToChar(data[2]) == 'a') && (System.Convert.ToChar(data[3]) == 'P'))
                    {
                        // set camera position request recieved
                        lock (img_request_lock)
                        {
                            // cache requester's ip address for response
                            img_client_addr = ip.Address;
                            // requested parameters
                            //all positions are relative to the TESSE agent center point
                            request_cam_id = System.BitConverter.ToInt32(data, 4); // id of camera to change the values on, -1 means all cameras
                            request_cam_x = System.BitConverter.ToSingle(data, 8); // new camera offset in x dimension, in meters
                            request_cam_y = System.BitConverter.ToSingle(data, 12); // new camera offset in y dimension, in meters
                            request_cam_z = System.BitConverter.ToSingle(data, 16); // new camera offset in z dimension, in meters
                            
                            cam_pos_request_flag = true; // flag to signal command to Unity Update() thread
                        }
                    }
                    else if ((data.Length == 24) && (System.Convert.ToChar(data[0]) == 's') && (System.Convert.ToChar(data[1]) == 'C')
                      && (System.Convert.ToChar(data[2]) == 'a') && (System.Convert.ToChar(data[3]) == 'Q'))
                    {
                        // set camera rotation request received
                        lock (img_request_lock)
                        {
                            // cache requester's ip address for response
                            img_client_addr = ip.Address;
                            // requested parameters
                            //all rotations are relative to the TESSE agent rotations
                            request_cam_id = System.BitConverter.ToInt32(data, 4); // id of camera to change the values on, -1 means all cameras
                            request_cam_x = System.BitConverter.ToSingle(data, 8); // new camera quaternion x value
                            request_cam_y = System.BitConverter.ToSingle(data, 12); // new camera quaternion y value
                            request_cam_z = System.BitConverter.ToSingle(data, 16); // new camera quaternion z values
                            request_cam_w = System.BitConverter.ToSingle(data, 20); // new camera quaternion w value
                            
                            cam_rot_request_flag = true; // flag to signal command to Unity Update() thread
                        }
                    }
                }
            }
        }

        private void get_and_send_multi_camera_screenshot()
        {
            /* 
            * This function gets a screen shot from each requested camera and sends the data
            * back to the requesting client. The imagery begins getting sent as soon
            * as the first camera's data is available. This is done for speed.
            */

            // connect to client
            connect_to_tcp_client();

            // get metadata from tesse metadata interface, if requested
            byte[] meta_data = null;
            if (img_meta_request_flag)
            {
                string metadata = null;
                tmi.get_agent_state_metadata(out metadata, true);
                meta_data = Encoding.ASCII.GetBytes(metadata);
            }

            // create multi image header
            System.UInt32 img_data_length = 0;

            // loop through all user requests
            foreach (var cam_req in cams_requested)
            {
                Camera c = agent_cameras[cam_req.cam_requested]; // get Unity Camera object reference

                if (c != null) // ensure camera is valid
                {
                    int w = 0, h = 0;

                    try
                    {
                        //NOTE: a seperate resolution is cached in the camera_parameters 
                        //script to enable game resolution and interface resolution can 
                        //be decoupled (e.g. image requests by users can be lower or higher
                        //than the screen resolution of the game)
                        w = (int)c.GetComponent<camera_parameters>().get_camera_width();
                        h = (int)c.GetComponent<camera_parameters>().get_camera_height();
                    }
                    catch
                    {
                        // if a seperate camera resolution for the interface hasn't been set, use the game resolution 
                        //instead
                        Debug.Log("The camera: " + c.ToString() + " does not have a camera_parameters script attached to it!");
                        Debug.Log("using default resolution...");
                        w = c.pixelWidth;
                        h = c.pixelHeight;
                    }

                    // set maximum response data length for use in the response header back to the client
                    //this is used to ensure that the client knows the upper bound on how much data to
                    //expect back for given request; it is an upper bound because if compression is used
                    //then the total payload size could be smaller, this logic is handled in the TESSE_interface
                    //python package
                    if (cam_req.single_channel)
                        img_data_length += (System.UInt32)(w * h) + 32; // per image header is 32 bytes
                    else if ( cam_req.cam_requested != 3 )
                        img_data_length += (System.UInt32)(w * h * 3) + 32;  // per image header is 32 bytes
                    else if ( cam_req.cam_requested == 3 )
                        img_data_length += (System.UInt32)(w * h * 4) + 32;  // per image header is 32 bytes, single channel float image
                }
            }

            // this is all C# tom foolery to get a fixed size array that can be sent via the tcp socket
            System.UInt32[] mult_uint_header;

            // the easiest thing to do is create an array of the information and convert to a byte array, for some reason
            if ( meta_data != null )
                mult_uint_header = new System.UInt32[] { mult_image_tag, (System.UInt32)img_data_length, (System.UInt32)meta_data.Length };
            else
                mult_uint_header = new System.UInt32[] { mult_image_tag, (System.UInt32)img_data_length, (System.UInt32)4 };

            byte[] mult_header = mult_uint_header.SelectMany(System.BitConverter.GetBytes).ToArray();

            // send overall response header back to requester
            //this contains the response tag and upper bound on image data payload and metadata 
            //payload, if requested
            send_img_data(mult_header);

            // process each requested camera
            foreach (var cam_req in cams_requested)
            {
                // capture the requested camera
                Camera cam = null;
                if (cam_req.cam_requested == 0)
                {
                    // rgb_left requested
                    cam = transform.Find("rgb_left").GetComponent<Camera>();
                }
                else if (cam_req.cam_requested == 1)
                {
                    // rgb_right requested
                    cam = transform.Find("rgb_right").GetComponent<Camera>();
                }
                else if (cam_req.cam_requested == 2)
                {
                    // segmentation camera requested
                    cam = transform.Find("segmentation").GetComponent<Camera>();
                }
                else if (cam_req.cam_requested == 3)
                {
                    // depth camera reqested
                    cam = transform.Find("depth").GetComponent<Camera>();
                }
                else if (cam_req.cam_requested == 4)
                {
                    // third person camera requested
                    cam = transform.Find("3pv").GetComponent<Camera>();
                }

                // capture current camera state
                bool cam_state = cam.enabled;
                // if camera is disabled, enable it to get a screenshot
                if (cam.enabled == false)
                {
                    cam.enabled = true;
                }

                // ensure camera exists
                if (cam != null)
                {
                    int img_width = 0, img_height = 0;

                    // use requested image width and height
                    try
                    {
                        //NOTE: a seperate resolution is cached in the camera_parameters 
                        //script to enable game resolution and interface resolution can 
                        //be decoupled (e.g. image requests by users can be lower or higher
                        //than the screen resolution of the game)
                        img_width = (int)cam.GetComponent<camera_parameters>().get_camera_width();
                        img_height = (int)cam.GetComponent<camera_parameters>().get_camera_height();
                    }
                    catch
                    {
                        Debug.Log("The camera: " + cam.ToString() + " does not have a camera_parameters script attached to it!");
                        Debug.Log("using default resolution...");
                        img_width = cam.pixelWidth;
                        img_height = cam.pixelHeight;

                    }

                    // create a render texture to hold non-depth images
                    RenderTexture rt = new RenderTexture(img_width, img_height, 0, RenderTextureFormat.ARGB32);
                    // a seperate render texture is used to hold depth images to ensure the proper color space is used
                    RenderTexture rtd = new RenderTexture(img_width, img_height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); 

                    // ensure interpolation is not used
                    rt.filterMode = FilterMode.Point;
                    rt.wrapMode = TextureWrapMode.Clamp;
                    // set the camera target texture
                    if (cam_req.cam_requested != 3)
                        cam.targetTexture = rt;
                    else if (cam_req.cam_requested == 3)
                        cam.targetTexture = rtd;

                    // create texture object
                    Texture2D tex;
                    
                    if (cam_req.cam_requested != 3)
                       tex = new Texture2D(img_width, img_height, TextureFormat.RGB24, false); // color image
                    else
                       tex = new Texture2D(img_width, img_height, TextureFormat.RGBA32, false); // 4 channel byte-encoded float used for depth
                   
                    // set texture settings
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Point;

                    // render the camera
                    cam.Render();

                    // set the appropriate texture for the desired camera type
                    if (cam_req.cam_requested != 3)
                        RenderTexture.active = rt;
                    else if (cam_req.cam_requested == 3)
                        RenderTexture.active = rtd;

                    // read the data from the GPU texture memory to the CPU
                    tex.ReadPixels(new Rect(0, 0, img_width, img_height), 0, 0);
                    
                    // reset the camera properties and destroy temporaries
                    cam.targetTexture = null;
                    RenderTexture.active = null;
                    Destroy(rt);
                    Destroy(rtd);

                    // create a Unity NativeArray to hold the image data
                    //a NativeArray is used because it returns a pointer from the 
                    //GetRawTextureData function as opposed to doing a deep copy 
                    //to a byte array
                    NativeArray<byte> temp_img;
                    temp_img = tex.GetRawTextureData<byte>();

                    // set response information
                    string img_return_type = null;
                    if (cam_req.compressed && !cam_req.single_channel)
                    {
                        // tag for compressed three channel images
                        img_return_type = "cRGB";
                    }
                    else if (!cam_req.compressed && cam_req.single_channel)
                    {

                        // tag single channel uncompressed image, not supported
                        img_return_type = "xGRY";
                    }
                    else if (!cam_req.compressed && !cam_req.single_channel && (cam_req.cam_requested != 3) )
                    {
                        // tag for uncompressed three channel image
                        img_return_type = "xRGB";
                    }
                    else if( !cam_req.compressed && !cam_req.single_channel && (cam_req.cam_requested == 3))
                    {
                        // tag for 4 channel byte-encoded float image
                        img_return_type = "xFLT";
                    }

                    // remove temporary Texture2D object
                    Destroy(tex);

                    // create image response header
                    byte[] tag = Encoding.ASCII.GetBytes("uImG");
                    System.UInt32 image_tag = System.BitConverter.ToUInt32(tag, 0);
                    System.UInt32 cam_id = (System.UInt32)cam_req.cam_requested;
                    System.UInt32 image_payload_length = 0;
                    image_payload_length = System.Convert.ToUInt32(temp_img.Length);

                    byte[] type = Encoding.ASCII.GetBytes(img_return_type);
                    System.UInt32 image_type = System.BitConverter.ToUInt32(type, 0);

                    // two placeholders are used at the end for future features
                    System.UInt32[] uint_header = new System.UInt32[] { image_tag, image_payload_length, (System.UInt32)img_width, (System.UInt32)img_height, cam_id, image_type, 0, 0 };

                    if (!cam_req.compressed && cam_req.single_channel)
                    {
                        // send single channel image back to client
                        byte[] temp = new byte[img_height * img_width];

                        for (int i = 0; i < img_height; ++i)
                        {
                            for (int j = 0; j < img_width; ++j)
                            {
                                if( cam_req.cam_requested != 3 )
                                    temp[(i * img_width) + j] = temp_img[(i * img_width * 3) + (j * 3) + 0];
                                else if( cam_req.cam_requested == 3 )
                                    temp[(i * img_width) + j] = temp_img[(i * img_width * 4) + (j * 4) + 0];

                            }
                        }

                        // create individual image header
                        uint_header[1] = (System.UInt32)temp.Length; // capture actual image data payload length, for compressed images
                        byte[] p_header = uint_header.SelectMany(System.BitConverter.GetBytes).ToArray();
                        // send individual image header to client, this is fast
                        send_img_data(p_header);
                        // send image payload to client
                        send_img_data(temp);
                    }
                    else if (cam_req.compressed && !cam_req.single_channel)
                    {
                        // send compressed three channel image data back to client, slower than uncompressed currently
                        print("Sending compressed image...");
                        byte[] temp = tex.EncodeToPNG(); // compress image to png format
                        uint_header[1] = (System.UInt32)temp.Length; // get actual length of compressed image
                        print("image payload length is " + uint_header[1]);
                        byte[] p_header = uint_header.SelectMany(System.BitConverter.GetBytes).ToArray();
                        // send indvidual image header
                        send_img_data(p_header);
                        // send image payload
                        send_img_data(temp);
                    }
                    else
                    {
                        // send uncompressed three channel image
                        byte[] p_header = uint_header.SelectMany(System.BitConverter.GetBytes).ToArray();

                        // send individual image header information, length isn't modified since uncompressed three channel
                        //was assumed on header creation
                        send_img_data(p_header);
                        // send image payload back to user
                        send_img_data(temp_img.ToArray());
                    }

                }
                else
                {
                    // camera does not exist
                    print("ERROR! Camera " + cam.name + " is null!!");
                }

                cam.enabled = cam_state; // return camera state back to original value
            }

            cams_requested.Clear(); // clear list of requested cameras

            if (meta_data != null)
            {
                // send metadata associated with this frame if requested
                //and terminate the tcp connection
                send_img_data(meta_data, true);
            }
            else
            {
                lock (img_send_lock)
                {
                    // if metadata is not requested, send a null byte and 
                    //terminate the tcp connection
                    send_img_data(System.BitConverter.GetBytes(0) , true);
                }
            }
        }

        public void increment_send_count()
        {
            /*
             * This function is a placeholder for a more advanced image 
             * transmission function. It will may be used at some point
             * in the future.
             * 
            */
            lock (image_count)
            {
                num_images_complete += 1;
            }
        }

        private void connect_to_tcp_client()
        {
            // this function creates a connection to a tcp listener
            lock (img_send_lock)
            {
                img_client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                // setup tcp client for image transmission
                img_client.NoDelay = true;
                img_client.SendTimeout = 1000;
                img_client.ReceiveTimeout = 500;

                lock (img_request_lock)
                {
                    // set image request client IP and port
                    IPEndPoint img_client_ep = new IPEndPoint(img_client_addr, img_send_port);

                    // connect to client
                    try
                    {
                        img_client.Connect(img_client_ep);
                        while (!img_client.Connected)
                        {
                            Debug.Log("could not connect to client, retrying...\n");
                            Thread.Sleep(500);
                            img_client.Connect(img_client_ep);
                        }
                    }
                    catch (SocketException ex)
                    {
                        Debug.Log("Socket Exception! Details: " + ex.Message);
                        img_client.Close();
                        return;
                    }
                }
            }
        }

        public void send_img_data(byte[] tcp_payload, bool close_conn = false)
        {
            // send image payload information to tcp listener socket
            lock (img_send_lock)
            {
                try
                {
                    // send payload to img_client
                    int total_send_count = 0;
                    while (total_send_count < tcp_payload.Length) // keep sending data until the entire payload is sent
                    {
                        int send_count = img_client.Send(tcp_payload, total_send_count, tcp_payload.Length - total_send_count, SocketFlags.None);
                        total_send_count += send_count;
                    }
                }
                catch (SocketException ex)
                {
                    Debug.Log("Socket exception happend during multi-image send with error code: " + ex.Message);
                }
                if (close_conn)
                {
                    img_client.Close(10);
                }
            }
        }

        private void send_camera_info()
        {
            // send camera information back to client

            // create camera info xml formatted string
            string cam_info = "<TESSE_Agent_CameraInfo_v0.4>\n";

            if (request_cam_id >= agent_cameras.Count)
            {
                cam_info += "  <Cam ID " + request_cam_id + " is not a valid id!/>\n";
            }
            else if (request_cam_id != -1)
            {
                cam_info += create_camera_info(request_cam_id);
            }
            else if (request_cam_id == -1)
            {
                cam_info += "";
                for (int c = 0; c < agent_cameras.Count; ++c)
                {
                    cam_info += create_camera_info(c);
                }
            }

            cam_info += "</TESSE_Agent_CameraInfo_v0.4>\n";

            byte[] caminfo = Encoding.ASCII.GetBytes(cam_info);

            // create metadata payload header
            byte[] tag = Encoding.ASCII.GetBytes("cami");
            System.UInt32 image_tag = System.BitConverter.ToUInt32(tag, 0);
            System.UInt32 cam_data_length = (System.UInt32)caminfo.Length;

            System.UInt32[] uint_header = new System.UInt32[] { image_tag, cam_data_length };

            byte[] p_header = new byte[uint_header.Length * sizeof(System.UInt32)];
            System.Buffer.BlockCopy(uint_header, 0, p_header, 0, uint_header.Length * sizeof(System.UInt32));

            // send image data back to client via tcp
            IPEndPoint client_ep = new IPEndPoint(img_client_addr, img_send_port);

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

                while (temp_send_count < caminfo.Length)
                {
                    client.NoDelay = true;
                    int send_count = client.Send(caminfo, temp_send_count, caminfo.Length - temp_send_count, SocketFlags.None);
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

        private string create_camera_info(int cam_id)
        {
            // create camera information xml string
            //this is implemented as a string to reduce dependancy requirements on the overall project and maintain
            //portability between MAC, Linux, and Windows
            string info = "  <camera_info>\n";
            info += "    <name>" + agent_cameras[cam_id].GetComponent<camera_parameters>().get_camera_name() + "</name>\n";
            info += "    <id>" + cam_id + "</id>\n";
            info += "    <parameters height=\'" + agent_cameras[cam_id].GetComponent<camera_parameters>().get_camera_height().ToString() + "\' " +
                                  "width=\'" + agent_cameras[cam_id].GetComponent<camera_parameters>().get_camera_width().ToString() + "\' " +
                                  "fov=\'" + agent_cameras[cam_id].fieldOfView + "\'/>\n";
            info += "    <position x=\'" + agent_cameras[cam_id].transform.localPosition.x + "\' " +
                                "y=\'" + agent_cameras[cam_id].transform.localPosition.y + "\' " +
                                "z=\'" + agent_cameras[cam_id].transform.localPosition.z + "\'/>\n";
            info += "    <rotation x=\'" + agent_cameras[cam_id].transform.localRotation.x + "\' " +
                                "y=\'" + agent_cameras[cam_id].transform.localRotation.y + "\' " +
                                "z=\'" + agent_cameras[cam_id].transform.localRotation.z + "\' " +
                                "w=\'" + agent_cameras[cam_id].transform.localRotation.w + "\'/>\n";
            info += "    <draw_distance near=\'" + agent_cameras[cam_id].nearClipPlane + "\' " +
                                        "far=\'" + agent_cameras[cam_id].farClipPlane + "\'/>\n";
            info += "  </camera_info>\n";

            return info;
        }

        private void set_camera_params()
        {
            // set camera parameters (resolution, etc.) based on user request
            if (request_cam_id >= agent_cameras.Count)
            {
                send_camera_info();
            }
            else if (request_cam_id != -1)
            {
                // only a single camera change requested
                uint w, h = 0;
                if (request_height > 0)
                {
                    // height change requested
                    h = (uint)request_height;
                }
                else
                {
                    // do not change height
                    h = agent_cameras[request_cam_id].GetComponent<camera_parameters>().get_camera_height();
                }

                if (request_width > 0)
                {
                    // width change requested
                    w = (uint)request_width;
                }
                else
                {
                    // do not change width
                    w = agent_cameras[request_cam_id].GetComponent<camera_parameters>().get_camera_width();
                }

                agent_cameras[request_cam_id].GetComponent<camera_parameters>().set_camera_resolution(w, h);

                if (request_fov > 0)
                {
                    // fov change requested
                    agent_cameras[request_cam_id].fieldOfView = request_fov;
                }

                if (request_nearClipPlane > 0 && request_farClipPlane > 0 && request_nearClipPlane < request_farClipPlane)
                {
                    // clip plane change requested
                    agent_cameras[request_cam_id].nearClipPlane = request_nearClipPlane;
                    agent_cameras[request_cam_id].farClipPlane = request_farClipPlane;
                }

                send_camera_info();

            }
            else if (request_cam_id == -1)
            {
                // request for all cameras to be changed
                foreach (var c in agent_cameras)
                {
                    // only a single camera change requested
                    uint w, h = 0;
                    if (request_height > 0)
                    {
                        // height change requested
                        h = (uint)request_height;
                    }
                    else
                    {
                        // do not change height
                        h = c.GetComponent<camera_parameters>().get_camera_height();
                    }

                    if (request_width > 0)
                    {
                        // width change requested
                        w = (uint)request_width;
                    }
                    else
                    {
                        // do not change width
                        w = c.GetComponent<camera_parameters>().get_camera_width();
                    }

                    c.GetComponent<camera_parameters>().set_camera_resolution(w, h);

                    if (request_fov > 0)
                    {
                        // fov change requested
                        c.fieldOfView = request_fov;
                    }

                    if (request_nearClipPlane > 0 && request_farClipPlane > 0 && request_nearClipPlane < request_farClipPlane)
                    {
                        // clip plane change requested
                        c.nearClipPlane = request_nearClipPlane;
                        c.farClipPlane = request_farClipPlane;
                    }
                }
                send_camera_info();
            }
        }

        private void set_camera_position()
        {
            // set relative position of camera
            if (request_cam_id >= agent_cameras.Count) // camera index is invalid
            {
                send_camera_info(); // send user camera information containing valid indices
            }
            else if (request_cam_id != -1) // modify requested camera
            {
                agent_cameras[request_cam_id].transform.localPosition = new Vector3(request_cam_x, request_cam_y, request_cam_z);

                send_camera_info(); // send new camera states to user
            }
            else if (request_cam_id == -1) // modify all cameras
            {
                Vector3 new_pos = new Vector3(request_cam_x, request_cam_y, request_cam_z);
                // request for all cameras to be changed
                foreach (var c in agent_cameras)
                {
                    c.transform.localPosition = new_pos;
                }
                send_camera_info(); // send new camera states to user
            }
        }

        private void set_camera_rotation()
        {
            // set relative rotation of camera
            if (request_cam_id > agent_cameras.Count) // camera index is invalid
            {
                send_camera_info(); // send user camera information containing valid indicies
            }
            else if (request_cam_id != -1) // modify requested camera
            {
                agent_cameras[request_cam_id].transform.localRotation = new Quaternion(request_cam_x, request_cam_y, request_cam_z, request_cam_w);
 
                send_camera_info(); // send new camera states to user
            }
            else if (request_cam_id == -1) // modify all cameras
            {
                Quaternion new_rot = new Quaternion(request_cam_x, request_cam_y, request_cam_z, request_cam_w);
                // request for all cameras to be changed
                foreach (var c in agent_cameras)
                {
                    c.transform.localRotation = new_rot;
                }
                send_camera_info(); // send new camera states to user
            }
        }
    }
}
