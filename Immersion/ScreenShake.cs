using System.Collections;
using UnityEngine;

/*
* Carl Eriksson
* 2021-05-17
*/

public class ScreenShake : MonoBehaviour {
    [SerializeField] private Transform shakeOrigin = null;
    [SerializeField] private Transform shakeCam = null;
    [SerializeField] private float shakeRotationTangent = 2f;

    private Vector3 startPos = Vector3.zero;
    private Vector3 desiredPos = Vector3.zero;
    private Vector3 startRot = Vector3.zero;
    private Vector3 desiredRot = Vector3.zero;

    private Vector3 camStartPos = Vector3.zero;
    private Vector3 camDesiredPos = Vector3.zero;
    private Vector3 camStartRot = Vector3.zero;
    private Vector3 camDesiredRot = Vector3.zero;

    private void Start() {
        if(shakeOrigin == null || shakeCam == null) {
            Debug.Break();
            Debug.LogError("shakeOrigin is null");
        }
        startPos = shakeOrigin.localPosition;
        startRot = shakeOrigin.localEulerAngles;
        camStartPos = shakeCam.localPosition;
        camStartRot = shakeCam.localEulerAngles;
    }

    public IEnumerator Shake(float shakeAmount, float shakeTime) {
        float time = 0.0f;

        float step = Time.deltaTime * (shakeAmount * 2.0f);
        float rotStep = Time.deltaTime * (shakeAmount * shakeRotationTangent);

        while (time < shakeTime) {
            float up = UnityEngine.Random.Range(-1.0f, 1.0f) * (shakeAmount * 0.1f);
            float right = UnityEngine.Random.Range(-1.0f, 1.0f) * (shakeAmount * 0.1f);

            desiredPos = new Vector3(right, up, 0);
            camDesiredPos = new Vector3(right, up, 0);

            shakeOrigin.localPosition = Vector3.MoveTowards(shakeOrigin.localPosition, desiredPos * 0.2f + startPos, step);
            shakeCam.localPosition = Vector3.MoveTowards(shakeCam.localPosition, camDesiredPos * 0.2f + camStartPos, step);

            desiredRot = new Vector3(right, up, 0) * shakeRotationTangent;
            Quaternion dest = Quaternion.Euler(startRot + desiredRot);
            camDesiredRot = new Vector3(right, up, 0) * shakeRotationTangent;
            Quaternion camDest = Quaternion.Euler(camStartRot + camDesiredRot);

            shakeOrigin.localRotation = Quaternion.Slerp(shakeOrigin.localRotation, dest, rotStep);
            shakeCam.localRotation = Quaternion.Slerp(shakeCam.localRotation, camDest, rotStep);

            time += Time.deltaTime;
            yield return null;
        }

        shakeOrigin.localEulerAngles = startRot;
        shakeCam.localEulerAngles = camStartPos;
        StartCoroutine(ReturnHome(step));
        StartCoroutine(ReturnHomeCam(step));
    }

    private IEnumerator ReturnHome(float step) {
        float time = 0.0f;

        while (time < step) {
            time += Time.deltaTime;
            float dist = Vector3.Distance(shakeOrigin.localPosition, startPos);

            if (!(dist > 0.001f)) continue;
            shakeOrigin.localPosition = Vector3.MoveTowards(shakeOrigin.localPosition, startPos, step * 0.1f);
            yield return null;
        }

        shakeOrigin.localPosition = startPos;
    }

    private IEnumerator ReturnHomeCam(float step) {
        float time = 0.0f;

        while (time < step) {
            time += Time.deltaTime;
            float dist = Vector3.Distance(shakeCam.localPosition, camStartPos);

            if (!(dist > 0.001f)) continue;
            shakeCam.localPosition = Vector3.MoveTowards(shakeCam.localPosition, camStartPos, step * 0.1f);
            yield return null;
        }

        shakeCam.localPosition = camStartPos;
    }
}

