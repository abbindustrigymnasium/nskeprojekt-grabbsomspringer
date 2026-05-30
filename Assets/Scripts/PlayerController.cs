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
        BarSnapping,
        BarSwinging
    }

    public float MoveSpeed => movement.moveSpeed;
    public float JumpVelocity => movement.jumpVelocity;
    public float Gravity => movement.gravity;
    public int MaxJumps => movement.maxJumps;

    public float StandingHeight => standingHeight;
    public float RollHeight => standingHeight * roll.rollHeightMultiplier;
    public float RollCeilingClearance => roll.rollCeilingClearance;
    public float RollTotalClearance => RollHeight + RollCeilingClearance;

    [System.Serializable]
    private class InputSettings
    {
        public InputActionReference moveAction;
        public InputActionReference jumpAction;
        public InputActionReference crouchAction;
    }

    [System.Serializable]
    private class MovementSettings
    {
        public float moveSpeed = 5f;
        public float jumpVelocity = 7f;
        public float gravity = -4f;
        public int maxJumps = 2;

        [Range(0f, 1f)]
        public float animationMoveSpeed = 1f;
    }

    [System.Serializable]
    private class RollSettings
    {
        [Range(0.3f, 1f)] public float rollHeightMultiplier = 0.65f;
        public float rollCeilingClearance = 0.25f;
        public float durationFallback = 0.9f;
    }

    [System.Serializable]
    private class BarSettings
    {
        [Header("References")]
        public Transform handAnchor;

        [Header("Timing")]
        public float snapDuration = 0.12f;
        public float swingDuration = 0.75f;

        [Header("Release")]
        public float releaseVerticalVelocity = 4f;

        [Header("Fallback Hand Estimate")]
        [Range(0.5f, 1f)] public float estimatedHandHeight = 0.78f;

        [Header("Fine Tune")]
        public Vector3 handOffsetCorrection = Vector3.zero;
    }

    [System.Serializable]
    private class DeathCheckSettings
    {
        public LayerMask obstacleMask;
        public float frontCheckDistance = 0.25f;
        public float sideHitNormalThreshold = 0.6f;
    }

    [Header("Input")]
    [SerializeField] private InputSettings input = new InputSettings();

    [Header("Movement")]
    [SerializeField] private MovementSettings movement = new MovementSettings();

    [Header("Roll")]
    [SerializeField] private RollSettings roll = new RollSettings();

    [Header("Bar Swing")]
    [SerializeField] private BarSettings bar = new BarSettings();

    [Header("Death Check")]
    [SerializeField] private DeathCheckSettings deathCheck = new DeathCheckSettings();

    [Header("References")]
    [SerializeField] private Animator characterAnimator;
    [SerializeField] private CharacterController characterController;

    private PlayerState state = PlayerState.Normal;

    private float verticalVelocity;
    private int jumpsUsed;
    private bool isDead;

    private float standingHeight;
    private Vector3 standingCenter;

    private float rollTimer;

    private BarInteractable currentBar;
    private float barSnapTimer;
    private float swingTimer;
    private Vector3 barSnapStartPosition;

    private void Awake()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        standingHeight = characterController.height;
        standingCenter = characterController.center;
    }

    private void OnEnable()
    {
        input.moveAction.action.Enable();
        input.jumpAction.action.Enable();
        input.crouchAction.action.Enable();

        input.jumpAction.action.performed += OnJump;
        input.crouchAction.action.performed += OnCrouch;
    }

    private void OnDisable()
    {
        input.jumpAction.action.performed -= OnJump;
        input.crouchAction.action.performed -= OnCrouch;

        input.moveAction.action.Disable();
        input.jumpAction.action.Disable();
        input.crouchAction.action.Disable();
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

            case PlayerState.BarSnapping:
                UpdateBarSnapping();
                break;

            case PlayerState.BarSwinging:
                UpdateBarSwinging();
                break;
        }

        UpdateAnimator();
    }

    private void UpdateNormal()
    {
        CheckFrontObstacleDeath();

        bool grounded = characterController.isGrounded;

        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
            jumpsUsed = 0;
        }

        ApplyGravity();
        MoveCharacter();
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

        bool grounded = characterController.isGrounded;

        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
            jumpsUsed = 0;
        }

        ApplyGravity();
        MoveCharacter();
    }

    private void UpdateBarSnapping()
    {
        if (currentBar == null)
        {
            ReleaseFromBar();
            return;
        }

        barSnapTimer += Time.deltaTime;

        float t = Mathf.Clamp01(barSnapTimer / Mathf.Max(0.01f, bar.snapDuration));
        t = Smooth01(t);

        Vector3 targetPosition = GetPlayerRootPositionForBar();

        characterController.enabled = false;
        transform.position = Vector3.Lerp(barSnapStartPosition, targetPosition, t);

        Debug.DrawLine(transform.position, targetPosition, Color.yellow);

        if (t >= 1f)
        {
            transform.position = targetPosition;
            swingTimer = bar.swingDuration;
            state = PlayerState.BarSwinging;
        }
    }

    private void UpdateBarSwinging()
    {
        if (currentBar == null)
        {
            ReleaseFromBar();
            return;
        }

        characterController.enabled = false;

        // Keep hands locked to the live moving bar.
        transform.position = GetPlayerRootPositionForBar();

        swingTimer -= Time.deltaTime;

        if (swingTimer <= 0f)
        {
            ReleaseFromBar();
        }
    }

    private void ApplyGravity()
    {
        verticalVelocity += movement.gravity * Time.deltaTime;
    }

    private void MoveCharacter()
    {
        Vector3 velocity = Vector3.zero;

        // World moves, player does not move forward.
        velocity.z = 0f;
        velocity.y = verticalVelocity;

        characterController.Move(velocity * Time.deltaTime);
    }

    private void OnJump(InputAction.CallbackContext context)
    {
        if (state == PlayerState.BarSnapping || state == PlayerState.BarSwinging)
        {
            ReleaseFromBar();
            verticalVelocity = movement.jumpVelocity;
            TriggerJump();
            return;
        }

        if (jumpsUsed >= movement.maxJumps)
            return;

        verticalVelocity = movement.jumpVelocity;
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
        rollTimer = roll.durationFallback;

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
        BarInteractable detectedBar = other.GetComponentInParent<BarInteractable>();

        if (detectedBar == null)
            return;

        TryCatchBar(detectedBar);
    }

    private void TryCatchBar(BarInteractable detectedBar)
    {
        if (state == PlayerState.BarSnapping || state == PlayerState.BarSwinging)
            return;

        if (characterController.isGrounded)
            return;

        currentBar = detectedBar;
        barSnapTimer = 0f;
        barSnapStartPosition = transform.position;

        verticalVelocity = 0f;

        state = PlayerState.BarSnapping;

        if (characterAnimator != null)
        {
            characterAnimator.ResetTrigger("Jump");
            characterAnimator.ResetTrigger("Roll");
            characterAnimator.ResetTrigger("Swing");
            characterAnimator.SetTrigger("Swing");
        }
    }

    private Vector3 GetPlayerRootPositionForBar()
    {
        Vector3 handOffsetFromRoot = GetHandOffsetFromRoot();
        return currentBar.AttachPosition - handOffsetFromRoot;
    }

    private Vector3 GetHandOffsetFromRoot()
    {
        if (bar.handAnchor != null)
        {
            return bar.handAnchor.position - transform.position + bar.handOffsetCorrection;
        }

        Vector3 estimatedOffset = Vector3.up * (standingHeight * bar.estimatedHandHeight);
        return estimatedOffset + bar.handOffsetCorrection;
    }

    private void ReleaseFromBar()
    {
        currentBar = null;

        if (!characterController.enabled)
            characterController.enabled = true;

        verticalVelocity = bar.releaseVerticalVelocity;
        jumpsUsed = 1;

        state = PlayerState.Normal;

        if (characterAnimator != null)
        {
            characterAnimator.ResetTrigger("Swing");
            characterAnimator.SetBool("Grounded", false);
            characterAnimator.SetFloat("VerticalVel", verticalVelocity);
            characterAnimator.SetFloat("Speed", movement.animationMoveSpeed);
        }
    }

    private void CheckFrontObstacleDeath()
    {
        Vector3 center = transform.position + characterController.center;

        float radius = characterController.radius * 0.9f;
        float halfHeight = characterController.height * 0.5f;

        Vector3 bottom = center + Vector3.up * (-halfHeight + radius);
        Vector3 top = center + Vector3.up * (halfHeight - radius);

        Vector3 direction = Vector3.forward;

        Debug.DrawLine(center, center + direction * deathCheck.frontCheckDistance, Color.cyan);

        bool hitSomething = Physics.CapsuleCast(
            bottom,
            top,
            radius,
            direction,
            out RaycastHit hit,
            deathCheck.frontCheckDistance,
            deathCheck.obstacleMask,
            QueryTriggerInteraction.Ignore
        );

        if (!hitSomething)
            return;

        Debug.DrawRay(hit.point, hit.normal * 1.5f, Color.red);

        bool isWallInFront =
            Vector3.Dot(hit.normal, -direction) > deathCheck.sideHitNormalThreshold &&
            hit.normal.y < 0.5f;

        if (isWallInFront)
            Die();
    }

    private void UpdateAnimator()
    {
        if (characterAnimator == null)
            return;

        bool grounded = characterController.enabled && characterController.isGrounded;

        characterAnimator.SetBool("Grounded", grounded);
        characterAnimator.SetFloat("VerticalVel", verticalVelocity);
        characterAnimator.SetFloat("Speed", movement.animationMoveSpeed, 0.15f, Time.deltaTime);
    }

    private static float Smooth01(float t)
    {
        return t * t * (3f - 2f * t);
    }

    private void Die()
    {
        isDead = true;

        string currentSceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(currentSceneName);
    }
}