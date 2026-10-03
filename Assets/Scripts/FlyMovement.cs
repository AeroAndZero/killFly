using UnityEngine;

// Mimics how a house fly moves through the air:
// fly straight for a short time, snap into a sharp turn, repeat.
// A small wobble keeps it jittery, and it stays inside an area around its start point.
public class FlyMovement : MonoBehaviour
{
    [Header("Speed")]
    public float speed = 3f;

    [Header("Straight flight between turns (seconds)")]
    public float minStraightTime = 0.2f;
    public float maxStraightTime = 1f;

    [Header("Turns")]
    public float minTurnAngle = 40f;
    public float maxTurnAngle = 160f;
    public float turnDuration = 0.08f;   // very short = sharp, fly-like turns
    [Range(0f, 1f)] public float verticalVariation = 0.3f;

    [Header("Wobble")]
    public float wobbleAmount = 0.5f;
    public float wobbleSpeed = 6f;

    [Header("Area")]
    public float areaRadius = 3f;

    Vector3 center;
    Vector3 direction;
    Vector3 turnFrom, turnTo;
    bool turning;
    float turnProgress;
    float straightTimer;
    float seed;

    void Start()
    {
        center = transform.position;
        direction = RandomTurn(Vector3.forward);
        seed = Random.value * 100f;
        straightTimer = Random.Range(minStraightTime, maxStraightTime);
    }

    void Update()
    {
        if (turning)
        {
            // Rotate from the old direction to the new one over turnDuration.
            turnProgress += Time.deltaTime / turnDuration;
            direction = Vector3.Slerp(turnFrom, turnTo, turnProgress).normalized;

            if (turnProgress >= 1f)
            {
                turning = false;
                straightTimer = Random.Range(minStraightTime, maxStraightTime);
            }
        }
        else
        {
            straightTimer -= Time.deltaTime;

            Vector3 toCenter = center - transform.position;
            bool leavingArea = toCenter.magnitude > areaRadius && Vector3.Dot(direction, toCenter) < 0f;

            if (straightTimer <= 0f || leavingArea)
                StartTurn();
        }

        // Smooth random jitter on top of the main direction.
        float t = Time.time * wobbleSpeed;
        Vector3 wobble = new Vector3(
            Mathf.PerlinNoise(seed, t) - 0.5f,
            Mathf.PerlinNoise(seed + 10f, t) - 0.5f,
            Mathf.PerlinNoise(seed + 20f, t) - 0.5f) * (2f * wobbleAmount);

        transform.position += (direction * speed + wobble) * Time.deltaTime;
        transform.rotation = Quaternion.LookRotation(direction);
    }

    void StartTurn()
    {
        turnFrom = direction;

        Vector3 toCenter = center - transform.position;
        if (toCenter.magnitude > areaRadius)
            turnTo = (toCenter.normalized + Random.insideUnitSphere * 0.3f).normalized; // head back
        else
            turnTo = RandomTurn(direction);

        turnProgress = 0f;
        turning = true;
    }

    Vector3 RandomTurn(Vector3 from)
    {
        float angle = Random.Range(minTurnAngle, maxTurnAngle) * (Random.value < 0.5f ? -1f : 1f);

        Vector3 flat = new Vector3(from.x, 0f, from.z);
        if (flat.sqrMagnitude < 0.001f) flat = Vector3.forward;

        Vector3 turned = Quaternion.AngleAxis(angle, Vector3.up) * flat.normalized;
        turned.y = Random.Range(-verticalVariation, verticalVariation);
        return turned.normalized;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(Application.isPlaying ? center : transform.position, areaRadius);
    }

    public void RestFly()
    {
        transform.position = center; 
        transform.rotation = Quaternion.identity;
	}
}