using Godot;
using System;

public class Menu : Control
{

    public WebRTCPeer rtcSingleton;
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        rtcSingleton = (WebRTCPeer) GetNode("/root/WebRTCPeer");
        GetNode("Buttons/ClearMesh").Connect("pressed",rtcSingleton,"_OnClearMesh");        
    }

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
//  public override void _Process(float delta)
//  {
//      
//  }
}
