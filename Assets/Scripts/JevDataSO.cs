using UnityEngine;
using UnityEngine.UIElements;

[CreateAssetMenu(fileName = "JevDataSO", menuName = "Scriptable Objects/JevDataSO")]
public class JevDataSO : ScriptableObject
{
    // What is the position of the housefly on the image?
    public float topLeft;
    public Color topLeftColor;

    public float topCenter;
	public Color topCenterColor;

	public float topRight;
    public Color topRightColor;

    public float centerLeft;
    public Color centerLeftColor;

    public float center;
    public Color centerColor;

    public float centerRight;
    public Color centerRightColor;

    public float bottomLeft;
    public Color bottomLeftColor;

    public float bottomCenter;
    public Color bottomCenterColor;

    public float bottomRight;
    public Color bottomRightColor;

    // Is the housefly visible?
    public float yes;
    public Color yesColor;
    public float no;
    public Color noColor;
    
    // How close is the housefly?
    public float veryClose;
    public Color veryCloseColor;
    public float close;
    public Color closeColor;
    public float mediumRange;
    public Color mediumRangeColor;
    public float far;
    public Color farColor;
    public float veryFar;
    public Color veryFarColor;

    // Kill count
    public int killCount;

    // Kill Animation
    public float killAnimationAlpha;

}
