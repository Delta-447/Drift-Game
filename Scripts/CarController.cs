using UnityEngine;

public class CarController : MonoBehaviour
{
	public float forwardSpeed = 15f;
	public float horizontalSpeed = 8f;
	public float turnTilt = 20f;

	void Update()
	{
		float horizontalInput = Input.GetAxis("Horizontal");

		// Always move forward
		Vector3 movement = new Vector3(
			horizontalInput * horizontalSpeed,
			0f,
			forwardSpeed
		);

		transform.Translate(movement * Time.deltaTime, Space.World);

		// Slightly rotate the car when moving left or right
		float targetYRotation = horizontalInput * turnTilt;

		transform.rotation = Quaternion.Lerp(
			transform.rotation,
			Quaternion.Euler(0f, targetYRotation, 0f),
			8f * Time.deltaTime
		);
	}
}