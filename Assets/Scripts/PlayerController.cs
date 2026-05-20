using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference crouchAction;
    private Vector2 moveInput;
    private bool isCrouching;
    private bool isDead;
    private float verticalVelocity = 0f;
    [SerializeField] private Animator characterAnimator;
    [SerializeField] private CharacterController characterController;

    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float frontCheckDistance = 0.25f;
    [SerializeField] private float sideHitNormalThreshold = 0.6f;

    [SerializeField] private float moveSpeed = 5f;
    [Range(0, 1f)]
    [SerializeField] private float animationMoveSpeed = 5f;
    [SerializeField] private float jumpVelocity = 7f;
    [SerializeField] private float gravity = -4f;
    [SerializeField] private float rollLandingVelocity = -12f;
    [SerializeField] private int maxJumps = 2;
    private int jumpsUsed = 0;
    private bool wasGrounded;

    private void OnEnable()
    {
        moveAction.action.Enable();
        jumpAction.action.Enable();
        crouchAction.action.Enable();

        crouchAction.action.performed += OnCrouchStarted;
        crouchAction.action.canceled += OnCrouchEnded;
        jumpAction.action.performed += OnJump;
    }

    private void OnDisable()
    {
        crouchAction.action.performed -= OnCrouchStarted;
        crouchAction.action.canceled -= OnCrouchEnded;
        jumpAction.action.performed -= OnJump;


        moveAction.action.Disable();
        jumpAction.action.Disable();
        crouchAction.action.Disable();
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        characterController = GetComponent<CharacterController>();
        Time.timeScale = 0.4f;
        Time.fixedDeltaTime = 0.02F * Time.timeScale;
    }

    // Update is called once per frame

    private void Update()
    {
        CheckFrontObstacleDeath();

        bool grounded = characterController.isGrounded;
        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
            jumpsUsed = 0;
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = Vector3.zero;
        velocity.z = moveSpeed;
        velocity.y = verticalVelocity;

        characterController.Move(velocity * Time.deltaTime);
        characterController.Move(velocity * Time.deltaTime);

        bool groundedAfterMove = characterController.isGrounded;

        if (!wasGrounded && groundedAfterMove)
        {
            OnLanded(verticalVelocity);
        }

        wasGrounded = groundedAfterMove;
        if (characterAnimator != null)
        {
            characterAnimator.SetBool("Grounded", characterController.isGrounded);
            characterAnimator.SetFloat("VerticalVel", verticalVelocity);
            characterAnimator.SetFloat("Speed", animationMoveSpeed, 0.15f, Time.deltaTime);
        }

    }
    private void OnLanded(float landingVelocity)
    {
        if (landingVelocity <= rollLandingVelocity)
        {
            characterAnimator.ResetTrigger("Jump");
            characterAnimator.ResetTrigger("Roll");
            characterAnimator.SetTrigger("Roll");

            Debug.Log($"Roll landing. Velocity: {landingVelocity}");
        }
        else
        {
            Debug.Log($"Normal landing. Velocity: {landingVelocity}");
        }
    }

    private void OnCrouchStarted(InputAction.CallbackContext context)
    {
        characterAnimator.SetBool("Crouch", true);
    }

    private void OnCrouchEnded(InputAction.CallbackContext context)
    {
        characterAnimator.SetBool("Crouch", false);
    }
    private void OnJump(InputAction.CallbackContext context)
    {
        if (jumpsUsed >= maxJumps)
            return;

        verticalVelocity = jumpVelocity;
        jumpsUsed++;

        characterAnimator.ResetTrigger("Jump");
        characterAnimator.SetTrigger("Jump");
    }

    private void CheckFrontObstacleDeath()
    {
        if (isDead)
            return;

        Vector3 center = transform.position + characterController.center;

        float radius = characterController.radius * 0.9f;
        float halfHeight = characterController.height * 0.5f;

        Vector3 bottom = center + Vector3.up * (-halfHeight + radius);
        Vector3 top = center + Vector3.up * (halfHeight - radius);

        Vector3 direction = Vector3.forward;


        // Draw the cast direction
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

        if (hitSomething)
        {
            Debug.DrawRay(hit.point, hit.normal * 1.5f, Color.red);

            Debug.Log(
                $"FRONT SENSOR HIT: {hit.collider.name} | normal={hit.normal} | point={hit.point}"
            );

            bool isWallInFront =
                Vector3.Dot(hit.normal, -direction) > sideHitNormalThreshold &&
                hit.normal.y < 0.5f;

            if (isWallInFront)
            {
                Debug.Log("DEATH: front wall detected");
                Die();
            }
        }
    }
    private void Die()
    {
        string currentSceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(currentSceneName);
    }
}
