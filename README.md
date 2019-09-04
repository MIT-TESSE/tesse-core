# TESSE_core
This repository contains the core components of the TESSE simulation including materials, interfaces and agents.
It is meant to be included into a full Unity project that contains build settings, scenes, third party asset packages, custom
assets, etc.
For an example of how this repository integrates into a full simulation build, 
see [the TESSE open source project](https://github.mit.edu/TESS/TESSE_open_source).

## Including to Existing Project

### If the Project is a Git Repository
To include the TESSE core components to an existing git project, simply add this repository as a submodule in
*project_root*/Assets/TESSE.

### If the Project is not a Git Repository
Clone the repository into your Unity project in the Assets folder.

## Adding the TESSE Agent to your project

The TESSE agent exists in a dedicated scene called *tesse_multiscene* located in *project_root*/Assets/TESSE_core/Scenes.
This scene acts as the entry point to the simulation and environments are 
[loaded 'around' the agent](https://github.mit.edu/TESS/TESSE_core/blob/master/scripts/tesse_position_interface.cs#L153) 
by [additively loading](https://docs.unity3d.com/ScriptReference/SceneManagement.LoadSceneMode.Additive.html) a new scene to Unity.

To add the TESS agent to your scene, first add the *tesse_multiscene* scene to the build settings 
[as described here](https://youtu.be/MQKJfZCAEa8?t=102). Next, add the desired scene, or scenes, to the build settings
[as previously described](https://youtu.be/MQKJfZCAEa8?t=102). Ensure that all added scenes are **below** the *tesse_multiscene*
scene in the [build order](https://youtu.be/MQKJfZCAEa8?t=126). The TESSE agent code that handles the scene changing expects 
the *tesse_multiscene* scene to be index 0 in the build order and all scenes after it to be valid scenes to load around the agent. 
Ensure all scenes that are desired to be included in the build are checked in the build settings.

## Using the TESSE Agent

Once you have included the TESSE_core components to your project and added any desired scenes, you can now use the agent in the editor
or as a standalone build. Instructions on how to interact with the agent locally via the keyboard or via the python interface
can be found [here](https://github.mit.edu/TESS/TESSE_open_source).


