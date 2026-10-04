using UnityEngine;

public class KuroKeyboardController : MonoBehaviour
{
    public float moveSpeed = 0f;
    public float maxSpeed = 12f;
    public float acceleration = 4.5f;
    public float braking = 7f;
    public float CurrentSpeed { get; private set; }
    public float turnSpeed = 120f;

    [Header("QA Super Speed")]
    [Tooltip("When enabled, holding the Up arrow zooms the rider far faster than maxSpeed so QA can quickly scan the map for defects and flaws.")]
    public bool superSpeedEnabled = true;
    [Tooltip("Top speed reached while holding the Up arrow in super-speed mode.")]
    public float superSpeed = 90f;
    [Tooltip("How quickly super-speed builds up while the Up arrow is held.")]
    public float superAcceleration = 60f;

    void Update()
    {
        float forward = Input.GetAxisRaw("Vertical");
        float turn = Input.GetAxisRaw("Horizontal");
        bool superSpeeding = superSpeedEnabled && Input.GetKey(KeyCode.UpArrow);
        float targetMax = superSpeeding ? superSpeed : maxSpeed;
        float accel = superSpeeding ? superAcceleration : acceleration;
        if (forward > 0f) CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetMax, accel * Time.deltaTime);
        else if (forward < 0f) CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, 0f, braking * Time.deltaTime);
        else CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, 0f, acceleration * 0.28f * Time.deltaTime);
        moveSpeed = CurrentSpeed;
        transform.Rotate(0f, turn * turnSpeed * Time.deltaTime, 0f);
        transform.position += transform.forward * (CurrentSpeed * Time.deltaTime);
    }
}
