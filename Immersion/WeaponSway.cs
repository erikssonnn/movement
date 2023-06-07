using UnityEngine;

/*
* Carl Eriksson
* 2021-05-15
*/

public class WeaponSway : MonoBehaviour {
    [Header("Movement sway: ")]
    [SerializeField] private float movementAmount = 0.2f;
    [SerializeField] private float movementSpeed = 2f;

    [Header("Rotation sway: ")]
    [SerializeField] private float rotationAmount = 25f;
    [SerializeField] private float rotationSpeed = 12f;

    private Vector3 startPos = Vector3.zero;
    private Vector3 desiredPos = Vector3.zero;

    private Vector3 desiredRot = Vector3.zero;
    private Vector3 startRot = Vector3.zero;

    private Vector3 right = Vector3.zero;
    private Vector3 forward = Vector3.zero;
    private float mouseX = 0f;
    private float mouseY = 0f;
    private readonly float loweredCoefficient = 0.1f;
    
    private void Start() {
        startPos = transform.localPosition;
        desiredPos = startPos;

        startRot = transform.localEulerAngles;
        desiredRot = startRot;
    }

    private void LateUpdate() {
        MovementSway();
        CameraSway();
    }

    private void CameraSway() {
        mouseY = Input.GetAxis("Mouse X") * (rotationAmount * loweredCoefficient);
        mouseX = Input.GetAxis("Mouse Y") * (rotationAmount * loweredCoefficient);

        desiredRot = new Vector3(mouseX, mouseY, right.x * -100f);
        Quaternion dest = Quaternion.Euler(startRot + desiredRot);

        float step = rotationSpeed * Time.deltaTime;
        transform.localRotation = Quaternion.Slerp(transform.localRotation, dest, step);
    }

    private void MovementSway() {
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        right = Vector3.right * (horizontal * (movementAmount * loweredCoefficient));
        forward = Vector3.forward * (vertical * (movementAmount *loweredCoefficient));

        desiredPos = right + forward;

        float step = movementSpeed * Time.deltaTime;
        transform.localPosition = Vector3.MoveTowards(transform.localPosition, desiredPos + startPos, step);
    }
}