using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
* Carl Eriksson
* 2023-01-28
*/

public class MovementController : MonoBehaviour  {
    [SerializeField] private float movementSpeed = 5.0f;
    [SerializeField] private float mouseSensitivity = 2.0f;
    [SerializeField] private float jumpForce = 2.0f;
    [SerializeField] private float crouchReturnSpeed = 1.3f;
    [SerializeField] private Vector3 crouchedPos = new Vector3(0.0f, 0.5f, 0.0f);

    private readonly float minX = -90f;
    private readonly float maxX = 90f;

    private float mouseX = 0f;
    private float mouseY = 0f;

    private readonly float startHeight = 2.5f;
    private readonly float endHeight = 1.0f;

    private float verticalVelocity = 0;
    private float speed;
    private float crouchSpeed;
    private float runSpeed;

    private bool crouched = false;
    private bool canStandUp = false;
    private Vector3 cameraDefaultPos = Vector3.zero;
    private CharacterController cc = null;
    private new Camera camera = null;
        
    private void Start() {
        camera = Camera.main;
        if (camera == null) {
            Debug.LogError("NO MAIN CAMERA FOUND");
            Debug.Break();
            return;
        }
        crouchSpeed = movementSpeed * 0.5f;
        runSpeed = movementSpeed * 1.6f;
        speed = movementSpeed;

        cameraDefaultPos = camera.transform.localPosition;
        cc = GetComponent<CharacterController>();
    }

    public float MovementSpeed {
        set => movementSpeed = value;
        get => movementSpeed;
    }

    public float CrouchSpeed {
        set => crouchSpeed = value;
        get => crouchSpeed;
    }

    private void Movement() {
        if (Input.GetKeyDown(KeyCode.LeftControl) && canStandUp) {
            crouched = !crouched;
        }

        if (!crouched) {
            float dist = Vector3.Distance(camera.transform.localPosition, cameraDefaultPos);
            speed = movementSpeed;

            if (dist > 0.01f) {
                camera.transform.localPosition = Vector3.Lerp(camera.transform.localPosition, cameraDefaultPos, crouchReturnSpeed * Time.fixedDeltaTime);
                cc.height = Mathf.Lerp(startHeight, endHeight, crouchReturnSpeed * Time.fixedDeltaTime);
            }
            if (Input.GetKey(KeyCode.LeftShift)) {
                speed = runSpeed;
            }
        } else {
            camera.transform.localPosition = Vector3.Lerp(camera.transform.localPosition, crouchedPos, crouchReturnSpeed * Time.fixedDeltaTime);
            cc.height = Mathf.Lerp(endHeight, startHeight, crouchReturnSpeed * Time.fixedDeltaTime);
            speed = crouchSpeed;
        }

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 forwardMovement = (transform.forward * vertical).normalized;
        Vector3 rightMovement = (transform.right * horizontal).normalized;
        Vector3 upMovement = transform.up * verticalVelocity;

        forwardMovement.y = 0;
        rightMovement.y = 0;

        if (Input.GetKeyDown(KeyCode.Space) && cc.isGrounded) {
            verticalVelocity = jumpForce;
        }

        if (verticalVelocity > 0) {
            verticalVelocity -= 10 * Time.deltaTime;
        }

        upMovement.y -= 9.81f;
        Vector3 dir = forwardMovement + rightMovement;
        dir.Normalize();

        Vector3 totalMovement = dir * (Time.deltaTime * speed);
        totalMovement += upMovement * Time.deltaTime;

        cc.Move(totalMovement);
    }

    private void CameraRotation() {
        mouseX += Input.GetAxis("Mouse Y") * mouseSensitivity * 0.5f;
        mouseY += Input.GetAxis("Mouse X") * mouseSensitivity * 0.5f;

        mouseX = Mathf.Clamp(mouseX, minX, maxX);
        camera.transform.eulerAngles = new Vector3(-mouseX, mouseY, 0);
        transform.eulerAngles = new Vector3(0, camera.transform.eulerAngles.y, 0);
    }

    private void Update() {
        Movement();
        CameraRotation();
        canStandUp = CanStandUp();
    }

    private bool CanStandUp() {
        RaycastHit hit;
        Ray forwardRay = new Ray(camera.transform.position, transform.up);
        return !Physics.Raycast(forwardRay, out hit, 0.5f);
    }
}
