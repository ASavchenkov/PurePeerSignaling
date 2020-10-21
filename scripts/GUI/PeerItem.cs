using Godot;
using System;

public class PeerItem : HBoxContainer
{

    private Label label;
    private Button clickableName;

    public override void _Ready()
    {
        label = (Label) GetNode("Status/Label");
        clickableName = (Button) GetNode("Clickable Name");
    }
    public void Init(SignaledPeer peer)
    {
        
        peer.Connect(nameof(SignaledPeer.StatusUpdated),this, nameof(OnStatusChanged));
    }

    public void OnStatusChanged(SignaledPeer.ConnectionStateMachine state)
    {
        label.Text = state.ToString();
    }
}
