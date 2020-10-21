using Godot;
using System;

public class IDDisplay : Label
{

    public void OnIDUpdate(int UID)
    {
        Text = UID.ToString();
    }
}
