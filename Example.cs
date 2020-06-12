using Godot;
using System;

public class Example : Spatial
{
   	Networking networking;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		networking = (Networking) GetNode("Networking");	
	}

	public void _JoinMesh()
	{
		string url = ((TextEdit) GetNode("UI/URL")).Text;
		string secret = ((TextEdit) GetNode("UI/Secret")).Text;
		networking._JoinMesh(url,secret);
	}

	public void _StartServer()
	{
		string secret = ((TextEdit) GetNode("UI/Secret")).Text;
		networking._StartServer(secret);
	}

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
//  public override void _Process(float delta)
//  {
//      
//  }
}
