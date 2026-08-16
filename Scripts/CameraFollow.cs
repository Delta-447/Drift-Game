using UnityEngine;

public class CameraFollow : MonoBehaviour
{
	public Transform target; // The car
	public Vector3 offset = new Vector3(0f, 5f, -8f);
	public float followSpeed = 5f;

	void LateUpdate()
	{
		if (target == null) return;

		Vector3 desiredPosition = target.position + target.TransformDirection(offset);
		transform.position = Vector3.Lerp(transform.position, desiredPosition, followSpeed * Time.deltaTime);

		transform.LookAt(target);
	}
}