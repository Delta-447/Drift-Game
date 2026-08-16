using UnityEngine;

/// <summary>Spins a collectible coin around the world Y axis.</summary>
public class Coin : MonoBehaviour
{
    public float spinSpeed = 240f;

    void Update()
    {
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
    }
}
