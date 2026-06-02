using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    private enum PlayerState
    {
        Normal,
        Rolling,
        BarReaching,
        BarPulling,
        BarAttached
    }

    public float MoveSpeed => moveSpeed;
    public float JumpVelocity => jumpVelocity;
    public float Gravity => gravity;
    public int MaxJumps => maxJumps;

    public float StandingHeight => standingHeight;
    public float RollHeight => standingHeight * rollHeightMultiplier;
    public float RollCeilingClearance => rollCeilingClearance;
    public float RollTotalClearance => RollHeight + RollCeilingClearance;

    [Header("Input")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference crouchAction;

    [Header("References")]
    [SerializeField] private Animator characterAnimator;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private ProceduralAltitudeSpawner worldSpawner;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpVelocity = 6f;
    [SerializeField] private float gravity = -14f;
    [SerializeField] private int maxJumps = 2;

    [Range(0f, 1f)]
    [SerializeField] private float animationMoveSpeed = 1f;

    [Header("Roll / Crouch Collider")]
    [SerializeField, Range(0.3f, 1f)] private float rollHeightMultiplier = 0.65f;
    [SerializeField] private float rollCeilingClearance = 0.25f;
    [SerializeField] private float rollDurationFallback = 0.9f;

    [Header("Bar Swing")]
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField] private float swingReleaseVelocity = 4f;
    [SerializeField] private float swingDurationFallback = 0.8f;

    [Header("Bar Grab Window")]
    [SerializeField] private float maxGrabDistance = 0.9f;
    [SerializeField] private float grabWindowDuration = 0.45f;

    [Header("Bar Pull")]
    [SerializeField] private float barPullStrength = 18f;
    [SerializeField] private float barAttachDistance = 0.08f;
    [SerializeField] private float maxPullStepPerFrame = 0.35f;

    [Header("Fallback Hand Estimate")]
    [SerializeField, Range(0.5f, 1f)] private float estimatedHandHeight = 0.78f;

    [Header("Death Check")]
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float frontCheckDistance = 0.25f;
    [SerializeField] private float sideHitNormalThreshold = 0.6f;

    private PlayerState state = PlayerState.Normal;

    private bool isDead;
    private float verticalVelocity;
    private int jumpsUsed;

    private float standingHeight;
    private Vector3 standingCenter;
    private float gameplayPlaneZ;

    private float rollTimer;

    private BarInteractable currentBar;
    private float attachedTimer;

    private bool grabWindowOpen;
    private float grabWindowTimer;

    private void Awake()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        standingHeight = characterController.height;
        standingCenter = characterController.center;
        gameplayPlaneZ = transform.position.z;

        if (worldSpawner == null)
            worldSpawner = FindFirstObjectByType<ProceduralAltitudeSpawner>();
    }

    private void OnEnable()
    {
        moveAction.action.Enable();
        jumpAction.action.Enable();
        crouchAction.action.Enable();

        jumpAction.action.performed += OnJump;
        crouchAction.action.performed += OnCrouch;
    }

    private void OnDisable()
    {
        jumpAction.action.performed -= OnJump;
        crouchAction.action.performed -= OnCrouch;

        moveAction.action.Disable();
        jumpAction.action.Disable();
        crouchAction.action.Disable();

        if (characterController != null)
            characterController.enabled = true;

        if (worldSpawner != null)
            worldSpawner.EndBarTracking();
    }

    private void Update()
    {
        if (isDead)
            return;

        switch (state)
        {
            case PlayerState.Normal:
                UpdateNormal();
                break;

            case PlayerState.Rolling:
                UpdateRolling();
                break;

            case PlayerState.BarReaching:
                UpdateBarReaching();
                break;

            case PlayerState.BarPulling:
                UpdateBarPulling();
                break;

            case PlayerState.BarAttached:
                UpdateBarAttached();
                break;
        }

        UpdateAnimator();
    }

    private void UpdateNormal()
    {
        CheckFrontObstacleDeath();
        HandleGroundedReset();
        ApplyGravity();
        MoveVertically();
    }

    private void UpdateRolling()
    {
        CheckFrontObstacleDeath();

        rollTimer -= Time.deltaTime;

        if (rollTimer <= 0f)
        {
            EndRoll();
            return;
        }

        HandleGroundedReset();
        ApplyGravity();
        MoveVertically();
    }

    private void UpdateBarReaching()
    {
        if (currentBar == null)
        {
            ReleaseFromBar(false);
            return;
        }

        // Keep the natural jump/fall arc while reaching.
        HandleGroundedReset();
        ApplyGravity();
        MoveVertically();
        KeepPlayerOnGameplayPlane();

        if (!grabWindowOpen)
            return;

        grabWindowTimer -= Time.deltaTime;

        if (grabWindowTimer <= 0f)
        {
            ReleaseFromBar(false);
            return;
        }

        TryStartPullToBarIfCloseEnough();
    }

    private void UpdateBarPulling()
    {
        if (currentBar == null)
        {
            ReleaseFromBar(false);
            return;
        }

        if (characterController.enabled)
            characterController.enabled = false;

        Vector3 handCenter = GetHandCenterPosition();
        Vector3 correction = currentBar.AttachPosition - handCenter;

        Debug.DrawLine(handCenter, currentBar.AttachPosition, Color.yellow);

        // Do not allow forward/backward correction to snap the camera.
        // Z tracking is handled by the spawner after attach.
        correction.z = 0f;

        float followT = 1f - Mathf.Exp(-barPullStrength * Time.deltaTime);
        Vector3 pullMovement = correction * followT;

        pullMovement = Vector3.ClampMagnitude(pullMovement, maxPullStepPerFrame);

        transform.position += pullMovement;
        KeepPlayerOnGameplayPlane();

        Vector3 remaining = currentBar.AttachPosition - GetHandCenterPosition();
        remaining.z = 0f;

        if (remaining.magnitude <= barAttachDistance)
        {
            AttachToBar();
        }
    }

    private void UpdateBarAttached()
    {
        if (currentBar == null)
        {
            ReleaseFromBar(true);
            return;
        }

        if (characterController.enabled)
            characterController.enabled = false;

        // Once attached, the spawner tracks the bar to the hands.
        KeepPlayerOnGameplayPlane();

        attachedTimer -= Time.deltaTime;

        if (attachedTimer <= 0f)
        {
            ReleaseFromBar(true);
        }
    }

    private void AttachToBar()
    {
        attachedTimer = swingDurationFallback;
        state = PlayerState.BarAttached;

        if (worldSpawner != null)
        {
            worldSpawner.BeginBarTracking(
                currentBar.AttachPointTransform,
                leftHand,
                rightHand
            );
        }
    }

    private void HandleGroundedReset()
    {
        bool grounded = characterController.enabled && characterController.isGrounded;

        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
            jumpsUsed = 0;
        }
    }

    private void ApplyGravity()
    {
        verticalVelocity += gravity * Time.deltaTime;
    }

    private void MoveVertically()
    {
        if (!characterController.enabled)
            return;

        Vector3 velocity = Vector3.zero;

        // World moves. Player does not move forward.
        velocity.z = 0f;
        velocity.y = verticalVelocity;

        characterController.Move(velocity * Time.deltaTime);
    }

    private void OnJump(InputAction.CallbackContext context)
    {
        if (state == PlayerState.BarReaching ||
            state == PlayerState.BarPulling ||
            state == PlayerState.BarAttached)
        {
            ReleaseFromBar(false);
            verticalVelocity = jumpVelocity;
            TriggerJump();
            return;
        }

        if (state == PlayerState.Rolling)
            return;

        if (jumpsUsed >= maxJumps)
            return;

        verticalVelocity = jumpVelocity;
        jumpsUsed++;

        TriggerJump();
    }

    private void TriggerJump()
    {
        if (characterAnimator == null)
            return;

        characterAnimator.ResetTrigger("Roll");
        characterAnimator.ResetTrigger("Swing");
        characterAnimator.ResetTrigger("Jump");
        characterAnimator.SetTrigger("Jump");
    }

    private void OnCrouch(InputAction.CallbackContext context)
    {
        if (state != PlayerState.Normal)
            return;

        StartRoll();
    }

    private void StartRoll()
    {
        state = PlayerState.Rolling;
        rollTimer = rollDurationFallback;

        characterController.height = RollHeight;
        characterController.center = new Vector3(
            standingCenter.x,
            RollHeight * 0.5f,
            standingCenter.z
        );

        if (characterAnimator != null)
        {
            characterAnimator.ResetTrigger("Jump");
            characterAnimator.ResetTrigger("Swing");
            characterAnimator.ResetTrigger("Roll");
            characterAnimator.SetTrigger("Roll");
        }
    }

    public void OnRollFinished()
    {
        if (state == PlayerState.Rolling)
            EndRoll();
    }

    private void EndRoll()
    {
        characterController.height = standingHeight;
        characterController.center = standingCenter;

        if (state == PlayerState.Rolling)
            state = PlayerState.Normal;
    }

    private void OnTriggerEnter(Collider other)
    {
        BarInteractable bar = other.GetComponentInParent<BarInteractable>();

        if (bar == null)
            return;

        TryStartBarReach(bar);
    }

    private void TryStartBarReach(BarInteractable bar)
    {
        if (state != PlayerState.Normal && state != PlayerState.Rolling)
            return;

        if (characterController.isGrounded)
            return;

        if (state == PlayerState.Rolling)
            EndRoll();

        currentBar = bar;
        state = PlayerState.BarReaching;

        grabWindowOpen = false;
        grabWindowTimer = 0f;

        // Do not zero verticalVelocity.
        // The player should keep their natural jump arc.

        if (characterAnimator != null)
        {
            characterAnimator.ResetTrigger("Jump");
            characterAnimator.ResetTrigger("Roll");
            characterAnimator.ResetTrigger("Swing");
            characterAnimator.SetTrigger("Swing");
        }
    }

    // Animation Event:
    // Put this on the frame where hands are visually ready to grab.
    public void OnBarGrabFrame()
    {
        if (state != PlayerState.BarReaching)
            return;

        if (currentBar == null)
        {
            ReleaseFromBar(false);
            return;
        }

        grabWindowOpen = true;
        grabWindowTimer = grabWindowDuration;

        TryStartPullToBarIfCloseEnough();
    }

    private void TryStartPullToBarIfCloseEnough()
    {
        if (currentBar == null)
            return;

        Vector3 handCenter = GetHandCenterPosition();
        Vector3 toBar = currentBar.AttachPosition - handCenter;

        Debug.DrawLine(handCenter, currentBar.AttachPosition, Color.magenta);

        if (toBar.magnitude > maxGrabDistance)
            return;

        grabWindowOpen = false;
        grabWindowTimer = 0f;

        verticalVelocity = 0f;

        if (characterController.enabled)
            characterController.enabled = false;

        state = PlayerState.BarPulling;
    }

    // Animation Event:
    // Put this near the end of the swing animation.
    public void OnSwingFinished()
    {
        if (state == PlayerState.BarReaching ||
            state == PlayerState.BarPulling ||
            state == PlayerState.BarAttached)
        {
            ReleaseFromBar(true);
        }
    }

    private Vector3 GetHandCenterPosition()
    {
        if (leftHand != null && rightHand != null)
        {
            return (leftHand.position + rightHand.position) * 0.5f;
        }

        return transform.position + Vector3.up * (standingHeight * estimatedHandHeight);
    }

    private void ReleaseFromBar(bool applyReleaseVelocity)
    {
        grabWindowOpen = false;
        grabWindowTimer = 0f;

        currentBar = null;

        if (worldSpawner != null)
            worldSpawner.EndBarTracking();

        if (!characterController.enabled)
            characterController.enabled = true;

        KeepPlayerOnGameplayPlane();

        if (applyReleaseVelocity)
        {
            verticalVelocity = swingReleaseVelocity;
            jumpsUsed = 1;
        }

        state = PlayerState.Normal;

        if (characterAnimator != null)
        {
            characterAnimator.ResetTrigger("Swing");
            characterAnimator.SetBool("Grounded", false);
            characterAnimator.SetFloat("VerticalVel", verticalVelocity);
            characterAnimator.SetFloat("Speed", animationMoveSpeed);
        }
    }

    private void KeepPlayerOnGameplayPlane()
    {
        Vector3 position = transform.position;
        position.z = gameplayPlaneZ;
        transform.position = position;
    }

    private void CheckFrontObstacleDeath()
    {
        if (state == PlayerState.BarReaching ||
            state == PlayerState.BarPulling ||
            state == PlayerState.BarAttached)
            return;

        Vector3 center = transform.position + characterController.center;

        float radius = characterController.radius * 0.9f;
        float halfHeight = characterController.height * 0.5f;

        Vector3 bottom = center + Vector3.up * (-halfHeight + radius);
        Vector3 top = center + Vector3.up * (halfHeight - radius);

        Vector3 direction = Vector3.forward;

        Debug.DrawLine(center, center + direction * frontCheckDistance, Color.cyan);

        bool hitSomething = Physics.CapsuleCast(
            bottom,
            top,
            radius,
            direction,
            out RaycastHit hit,
            frontCheckDistance,
            obstacleMask,
            QueryTriggerInteraction.Ignore
        );

        if (!hitSomething)
            return;

        Debug.DrawRay(hit.point, hit.normal * 1.5f, Color.red);

        bool isWallInFront =
            Vector3.Dot(hit.normal, -direction) > sideHitNormalThreshold &&
            hit.normal.y < 0.5f;

        if (isWallInFront)
            Die();
    }

    private void UpdateAnimator()
    {
        if (characterAnimator == null)
            return;

        bool grounded =
            characterController.enabled &&
            characterController.isGrounded &&
            state != PlayerState.BarReaching &&
            state != PlayerState.BarPulling &&
            state != PlayerState.BarAttached;

        characterAnimator.SetBool("Grounded", grounded);
        characterAnimator.SetFloat("VerticalVel", verticalVelocity);
        characterAnimator.SetFloat("Speed", animationMoveSpeed, 0.15f, Time.deltaTime);
    }

    private void Die()
    {
        isDead = true;

        if (worldSpawner != null)
            worldSpawner.EndBarTracking();

        string currentSceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(currentSceneName);
    }
}