using UnityEngine;

/*
* Carl Eriksson
* 2021-05-18
*/

public class Viewbob : MonoBehaviour {
    [Header("TWEAKABLES: ")]
    [SerializeField] private float bobStrength = 0.5f;
    [SerializeField] private float bobSpeed = 2f;

    private Vector3 origin = Vector3.zero;
    private Vector3 dest = Vector3.zero;
    private float time = -1.0f;
    private bool down = false;
    private CharacterController characterController;

    private void Start() {
        characterController = GetComponentInParent<CharacterController>();
        origin = transform.localPosition;
    }

    private void LateUpdate() {
        if (characterController.isGrounded && characterController.enabled) {
            Bobbing();
        }
    }

    private void Bobbing() {
        float vel = characterController.velocity.magnitude;

        time = Mathf.PingPong(Time.time * bobSpeed, 2.0f) - 1.0f;
        dest = (vel * 2.0f) * new Vector3(0, -Mathf.Sin(time * time * (bobStrength * 0.001f)), Mathf.Sin(time * (bobStrength * 0.001f)));

        if (transform.localPosition.y <= -0.04f && !down) {
            down = true;
            // footstep sound if available
        } else if (transform.localPosition.y > -0.03f) {
            down = false;
        }

        transform.localPosition = origin + dest;
    }
}