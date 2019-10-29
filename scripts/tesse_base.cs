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

using System.Net.Sockets;
using UnityEngine;

namespace tesse
{
    public class tesse_base : MonoBehaviour
    {
        /*
         * This class implements the a base set of functions used by 
         * other TESSE interfaces. Specifically, it provides hooks to the
         * command line parser, a basic socket testing function and checks
         * to ensure all required components are available.
        */

        // links to other tesse control objects
        protected simple_tesse_controller stc;
        protected Rigidbody agent_rigid_body;
        protected tesse_command_line_parser cla_parser;

        protected void Start()
        {
            // initialize links to other tesse components
            try
            {
                stc = GetComponent<simple_tesse_controller>();
            }
            catch
            {
                stc = null;
                print("simple tesse controller not found!");
                print("exiting...");
                Application.Quit();
            }

            try
            {
                agent_rigid_body = GetComponent<Rigidbody>();
            }
            catch
            {
                agent_rigid_body = null;
                print("agent rigid boyd not found!\nAn agent rigid body component is required for v0.2.");
                print("exiting...");
                Application.Quit();
            }

            try
            {
                cla_parser = GetComponent<tesse_command_line_parser>();
            }
            catch
            {
                cla_parser = null;
                print("tesse command line parser not found!\nA tesse command line parser component is required for v0.2.");
                print("exiting...");
                Application.Quit();
            }

            stc.speed = cla_parser.speed; // set speed variable for keyboard control
            stc.turn_speed = cla_parser.turn_speed; // set turn speed variable for keyboard control

            Time.captureFramerate = cla_parser.capture_rate; // set fixed capture rate
            stc.cmd_time = cla_parser.execution_time; // set time to execute actions in fixed capture rate mode
        }

        // convenience function for checking if a socket is still active
        protected bool socket_connected(Socket s)
        {
            if ((s.Poll(1000, SelectMode.SelectRead)) && (s.Available == 0))
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}

