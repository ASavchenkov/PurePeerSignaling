using Godot;
using System;
using System.Collections;
/*
    The default WebRTC can't handle receiving ice candidates before sdps are set
    This is an extended version that mitigates this.
*/
public class BufferedWebRTCPeerConnection : WebRTCPeerConnection
{

    private bool readyForIce = false;
    private ArrayList IceBuffer;
    private struct BufferedIce
    {
        public readonly string media;
        public readonly int index;
        public readonly string name;
        public BufferedIce(string _media, int _index, string _name)
        {
            media = _media;
            index = _index;
            name = _name;
        }
    }

    //Put Ice candidates in a list temporarily until releaseIceCandidates is called.
    //If it already has been called, immediately add them
    public Error BufferIceCandidate(string media, int index, string name)
    {
        if(readyForIce)
            return AddIceCandidate(media, index, name);
        else{
            IceBuffer.Add(new BufferedIce(media, index, name));
            return Error.Ok;
        }
        
    }

    public void ReleaseIceCandidates()
    {
        readyForIce = true;
        foreach(BufferedIce c in IceBuffer)
        {
            AddIceCandidate(c.media, c.index, c.name);
        }
    }
}
