using Godot;
using System;
using Godot.Collections;

public class Player : Spatial
{
    public WebRTCPeerConnection p1 = new WebRTCPeerConnection();
    public WebRTCPeerConnection p2 = new WebRTCPeerConnection();

    public WebRTCDataChannel ch1;
    public WebRTCDataChannel ch2;
    
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        GD.Print("Starting Setup");
        var d1 = new Dictionary();
        d1.Add("id",1);
        d1.Add("negotiated",true);
        
        var d2 = new Dictionary();
        d2.Add("id",1);
        d2.Add("negotiated",true);

        ch1 =   p1.CreateDataChannel("chat",d1);
        ch2 =   p2.CreateDataChannel("chat",d2);

        p1.Connect("session_description_created", p1, "set_local_description");
        p1.Connect("session_description_created", p2, "set_remote_description");
        p1.Connect("ice_candidate_created", p2, "add_ice_candidate");
        p2.Connect("session_description_created", p2, "set_local_description");
        p2.Connect("session_description_created", p1, "set_remote_description");
        p2.Connect("ice_candidate_created", p1, "add_ice_candidate");
        p1.CreateOffer();
        
        
        // ch1.PutPacket(System.Text.Encoding.UTF8.GetBytes("Hi from P1"));
        // ch2.PutPacket(System.Text.Encoding.UTF8.GetBytes("Hi from P2"));
        GD.Print("Finishing Setup");
        
    }

    public override void _Process(float delta)
    {
        p1.Poll();
        p2.Poll();
        ch2.PutPacket(System.Text.Encoding.UTF8.GetBytes("Hi from P2"));
        
        if(ch1.GetReadyState() == WebRTCDataChannel.ChannelState.Open && ch1.GetAvailablePacketCount() > 0)
        {
            GD.Print(System.Text.Encoding.UTF8.GetString(ch1.GetPacket()));
        }
        if(ch2.GetReadyState() == WebRTCDataChannel.ChannelState.Open && ch2.GetAvailablePacketCount() > 0)
            GD.Print(System.Text.Encoding.UTF8.GetString(ch2.GetPacket()));
    }
}
