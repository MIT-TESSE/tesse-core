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

public class simple_tesse_hover_controller : MonoBehaviour
{
    /*
     * This controls the TESSE agent hover behavior.
     * It uses a PID control loop to apply forces to the agent
     * to keep its height at the target y distance from the ground.
    */

    private Rigidbody agent_rigid_body; // reference to the agent's physics component

    // parameters for the PD controller
    public float hover_force = 65f;
    public float hover_height = 2f;
    public float p_gain = 25f;
    public float d_gain = 10f;

    // Start is called before the first frame update
    void Start()
    {
        agent_rigid_body = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        // cast a ray from the agent, down
        Ray ray = new Ray(transform.position, -transform.up);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 4 * hover_height))
        {
            // the ray hit something, adjust hover force using PD controller
            float pos_err = hover_height - hit.distance;
            float d_pos_err = -1 * agent_rigid_body.velocity.y;
            Vector3 applied_hover_force = (p_gain * pos_err + d_gain * d_pos_err) * Vector3.up;
            applied_hover_force += -Physics.gravity;
            agent_rigid_body.AddForce(applied_hover_force, ForceMode.Acceleration);
        }
    }
}
