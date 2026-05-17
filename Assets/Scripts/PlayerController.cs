using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference crouchAction;
    private Vector2 moveInput;
    private bool isCrouching;
    private float verticalVelocity = 0f;
    [SerializeField] private Animator characterAnimator;
    [SerializeField] private CharacterController characterController;


    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpVelocity = 7f;
    [SerializeField] private float gravity = -9.82f;

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
    }

    // Update is called once per frame

    private void Update()
    {
        
        bool grounded = characterController.isGrounded;
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = Vector3.zero;
        velocity.x = moveSpeed;
        velocity.y = verticalVelocity;

        characterController.Move(velocity * Time.deltaTime);

         if (characterAnimator != null)
        {
            characterAnimator.SetBool("Grounded", characterController.isGrounded);
            characterAnimator.SetFloat("VerticalVel", verticalVelocity);
            characterAnimator.SetFloat("Speed", 1f, 0.15f, Time.deltaTime);
        }

    }
    

    private void OnCrouchStarted(InputAction.CallbackContext context)
    {
        characterAnimator.SetBool("Crouch",true);
    }

    private void OnCrouchEnded(InputAction.CallbackContext context)
    {
        characterAnimator.SetBool("Crouch", false);
    }
    private void OnJump(InputAction.CallbackContext context)
    {
        if (!characterController.isGrounded)
            return;

        verticalVelocity = jumpVelocity;

        characterAnimator.ResetTrigger("Jump");
        characterAnimator.SetTrigger("Jump");
    
    }
}
