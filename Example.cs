using Godot;
using System;

public class Example : Spatial
{
   
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        
    }

    public void _JoinMesh()
    {
        Networking networking = (Networking) GetNode("Networking");
        string url = ((TextEdit) GetNode("UI/TextEdit")).Text;
        networking._JoinMesh(url);
    }

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
//  public override void _Process(float delta)
//  {
//      
//  }
}
