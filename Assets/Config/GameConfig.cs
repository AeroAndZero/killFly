using UnityEngine;

public class Config : MonoBehaviour
{
    public int framerate = 30;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Application.targetFrameRate = framerate;
    }   
}
