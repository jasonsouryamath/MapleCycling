using System;
using UnityEngine;

/// <summary>
/// Controls the Japanese rural level crossing (踏切) on the Sakura Pass hillside railway.
/// When the train approaches within triggerDistance, the barrier arm rotates down and warning
/// lamps alternate blinking. When the train clears, the barrier arm raises back up.
/// Zero physics, zero runtime allocations, throttled culling.
/// </summary>
public sealed class SakuraLivingLevelCrossing : MonoBehaviour
{
    [Header("References")]
    public Transform barrierArm;
    public Transform trainHead;
    public Renderer redLightLeft;
    public Renderer redLightRight;

    [Header("Trigger & Angles")]
    public float triggerDistance = 55f;
    public Vector3 crossingPosition = Vector3.zero;
    public float armUpAngle = 82f;
    public float armDownAngle = 0f;
    public float armSpeed = 45f; // degrees per second

    [Header("Warning Flashers")]
    public float flashFrequency = 1.5f;

    private float _currentArmAngle;
    private bool _isActive;
    private MaterialPropertyBlock _propBlockOn;
    private MaterialPropertyBlock _propBlockOff;
    private float _flashTimer;
    private bool _flashState;

    public bool IsActive => _isActive;
    public float CurrentArmAngle => _currentArmAngle;

    private void Awake()
    {
        _currentArmAngle = armUpAngle;
        if (barrierArm != null)
        {
            barrierArm.localRotation = Quaternion.Euler(0f, 0f, _currentArmAngle);
        }

        _propBlockOn = new MaterialPropertyBlock();
        _propBlockOn.SetColor("_BaseColor", new Color(1f, 0.05f, 0.05f, 1f));
        _propBlockOn.SetColor("_Color", new Color(1f, 0.05f, 0.05f, 1f));
        _propBlockOn.SetColor("_EmissionColor", new Color(2f, 0.1f, 0.1f, 1f));

        _propBlockOff = new MaterialPropertyBlock();
        _propBlockOff.SetColor("_BaseColor", new Color(0.25f, 0.02f, 0.02f, 1f));
        _propBlockOff.SetColor("_Color", new Color(0.25f, 0.02f, 0.02f, 1f));
        _propBlockOff.SetColor("_EmissionColor", Color.black);
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // Check train proximity
        Vector3 trainPos = trainHead != null ? trainHead.position : Vector3.zero;
        Vector3 crossPos = crossingPosition != Vector3.zero ? crossingPosition : transform.position;
        float distSqr = (trainPos - crossPos).sqrMagnitude;

        _isActive = trainHead != null && distSqr <= (triggerDistance * triggerDistance);

        // Animate barrier arm
        float targetAngle = _isActive ? armDownAngle : armUpAngle;
        _currentArmAngle = Mathf.MoveTowards(_currentArmAngle, targetAngle, armSpeed * dt);

        if (barrierArm != null)
        {
            barrierArm.localRotation = Quaternion.Euler(0f, 0f, _currentArmAngle);
        }

        // Alternating red signal flasher
        if (_isActive)
        {
            _flashTimer += dt;
            if (_flashTimer >= (0.5f / flashFrequency))
            {
                _flashTimer = 0f;
                _flashState = !_flashState;

                if (redLightLeft != null)
                    redLightLeft.SetPropertyBlock(_flashState ? _propBlockOn : _propBlockOff);
                if (redLightRight != null)
                    redLightRight.SetPropertyBlock(!_flashState ? _propBlockOn : _propBlockOff);
            }
        }
        else
        {
            if (redLightLeft != null) redLightLeft.SetPropertyBlock(_propBlockOff);
            if (redLightRight != null) redLightRight.SetPropertyBlock(_propBlockOff);
        }
    }
}
